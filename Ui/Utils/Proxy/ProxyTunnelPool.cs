using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using Shawn.Utils;

namespace _1RM.Utils.Proxy
{
    /// <summary>
    /// Keeps one <see cref="ProxyTunnel"/> alive per (proxy endpoint, target) pair for the lifetime of the
    /// app. Tunnels are just a bound loopback socket until something connects through them, so holding them
    /// open is cheaper than tearing one down and rebuilding it on every reconnect — and it keeps the local
    /// port stable, which is what the host key and certificate caches downstream key off.
    /// </summary>
    public sealed class ProxyTunnelPool : IDisposable
    {
        /// <summary>IANA dynamic/private port range, where a stable-but-arbitrary port is least likely to clash.</summary>
        private const int FIRST_DYNAMIC_PORT = 49152;
        private const int LAST_DYNAMIC_PORT = 65535;

        private readonly object _lock = new object();

        /// <summary>
        /// Entries are lazy so that building a tunnel — which for a jump host means authenticating over the
        /// network — happens outside <see cref="_lock"/>. Threads racing for the same key still serialise on
        /// the <see cref="Lazy{T}"/> and end up sharing one tunnel; threads on other keys do not wait.
        /// </summary>
        private readonly Dictionary<string, Lazy<ITunnel>> _tunnels = new Dictionary<string, Lazy<ITunnel>>();

        public ITunnel GetOrCreate(ProxyConfig proxy, string targetHost, int targetPort)
        {
            if (proxy == null) throw new ArgumentNullException(nameof(proxy));
            if (!proxy.IsUsable) throw new ArgumentException($"proxy '{proxy.Name}' is not configured completely", nameof(proxy));

            var key = $"{proxy.GetEndPointKey()}=>{targetHost}:{targetPort}";

            // At most two passes: the first may turn up a tunnel that has since died or whose credentials
            // just changed, and the replacement registered on the second is what gets returned.
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var entry = GetOrAddEntry(key, proxy, targetHost, targetPort);

                ITunnel tunnel;
                try
                {
                    tunnel = entry.Value;
                }
                catch
                {
                    // A Lazy remembers the failure, so the entry has to go or this route stays broken for
                    // the rest of the session.
                    Forget(key, entry);
                    throw;
                }

                if (tunnel.IsAlive)
                {
                    // The key covers the route but not the secret, so hand the current one over. A jump
                    // tunnel cannot swap credentials on a live session and retires itself instead, hence
                    // the second look.
                    tunnel.RefreshCredentials(proxy);
                    if (tunnel.IsAlive)
                        return tunnel;
                }

                Forget(key, entry);
                tunnel.Dispose();
            }

            throw new InvalidOperationException($"could not keep a tunnel to {targetHost}:{targetPort} alive");
        }

        private Lazy<ITunnel> GetOrAddEntry(string key, ProxyConfig proxy, string targetHost, int targetPort)
        {
            lock (_lock)
            {
                if (_tunnels.TryGetValue(key, out var existed))
                    return existed;

                var created = new Lazy<ITunnel>(
                    () => Create(proxy, targetHost, targetPort, PreferredLocalPort(key)),
                    LazyThreadSafetyMode.ExecutionAndPublication);
                _tunnels[key] = created;
                return created;
            }
        }

        private void Forget(string key, Lazy<ITunnel> entry)
        {
            lock (_lock)
            {
                // Only when it is still the same entry: another thread may already have put a fresh one in.
                if (_tunnels.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
                    _tunnels.Remove(key);
            }
        }

        private static ITunnel Create(ProxyConfig proxy, string targetHost, int targetPort, int preferredLocalPort)
        {
            return proxy.Type == EProxyType.SshJump
                ? SshJumpTunnel.Start(proxy, targetHost, targetPort, preferredLocalPort)
                : ProxyTunnel.Start(proxy, targetHost, targetPort, preferredLocalPort);
        }

        /// <summary>
        /// A deterministic loopback port for this tunnel, so it survives a restart of the app.
        ///
        /// FNV-1a rather than string.GetHashCode(): the latter is randomised per process on .NET Core, which
        /// would hand out a different port every launch and invalidate PuTTY's cached SSH host key and the
        /// RDP certificate trust for the endpoint each time.
        /// </summary>
        private static int PreferredLocalPort(string key)
        {
            const uint fnvOffsetBasis = 2166136261;
            const uint fnvPrime = 16777619;
            var hash = fnvOffsetBasis;
            // FNV-1a is defined over arithmetic modulo 2^32 and relies on the multiply wrapping around.
            // This project is built with CheckForOverflowUnderflow, which makes that wrap throw instead,
            // so the block has to opt out explicitly.
            unchecked
            {
                foreach (var c in key)
                {
                    hash ^= c;
                    hash *= fnvPrime;
                }
            }
            return FIRST_DYNAMIC_PORT + (int)(hash % (uint)(LAST_DYNAMIC_PORT - FIRST_DYNAMIC_PORT));
        }

        /// <summary>
        /// True only when the address is this very machine.
        ///
        /// Private ranges (10/8, 172.16/12, 192.168/16, fc00::/7) deliberately do NOT count. Reaching a
        /// machine inside a LAN from outside it is the single most common reason to configure a proxy here,
        /// so treating those as "local" would bypass the proxy in exactly the case it was set up for.
        /// Whether a private address is directly reachable depends on which network the user is on right
        /// now, which we cannot know — so we honour the explicit choice and tunnel it.
        /// </summary>
        public static bool IsLocalAddress(string host)
        {
            if (string.IsNullOrWhiteSpace(host)) return true;
            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)) return true;
            if (!IPAddress.TryParse(host, out var ip)) return false;
            if (IPAddress.IsLoopback(ip)) return true;
            return LocalAddresses.Contains(ip);
        }

        /// <summary>
        /// Addresses bound to this machine's own adapters. Resolved once — a proxy decision made mid-session
        /// on a stale list is far less disruptive than querying every adapter on every connect.
        /// </summary>
        private static readonly Lazy<HashSet<IPAddress>> LazyLocalAddresses = new Lazy<HashSet<IPAddress>>(() =>
        {
            var set = new HashSet<IPAddress>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                    foreach (var addr in ni.GetIPProperties().UnicastAddresses)
                        set.Add(addr.Address);
            }
            catch (Exception e)
            {
                SimpleLogHelper.Warning($"ProxyTunnelPool: could not enumerate local addresses, {e.Message}");
            }
            return set;
        });

        private static HashSet<IPAddress> LocalAddresses => LazyLocalAddresses.Value;

        public void Dispose()
        {
            Lazy<ITunnel>[] entries;
            lock (_lock)
            {
                entries = _tunnels.Values.ToArray();
                _tunnels.Clear();
            }
            foreach (var entry in entries)
            {
                // An entry still being built has nothing to close, and asking for its value here would
                // block the shutdown on a network round trip.
                if (!entry.IsValueCreated) continue;
                try
                {
                    entry.Value.Dispose();
                }
                catch (Exception e)
                {
                    SimpleLogHelper.Warning(e);
                }
            }
        }
    }
}
