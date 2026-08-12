using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
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
        private readonly Dictionary<string, ProxyTunnel> _tunnels = new Dictionary<string, ProxyTunnel>();

        public ProxyTunnel GetOrCreate(ProxyConfig proxy, string targetHost, int targetPort)
        {
            if (proxy == null) throw new ArgumentNullException(nameof(proxy));
            if (!proxy.IsUsable) throw new ArgumentException($"proxy '{proxy.Name}' is not configured completely", nameof(proxy));

            var key = $"{proxy.GetEndPointKey()}=>{targetHost}:{targetPort}";
            lock (_lock)
            {
                if (_tunnels.TryGetValue(key, out var existed))
                {
                    if (existed.IsAlive) return existed;
                    _tunnels.Remove(key);
                }

                var tunnel = ProxyTunnel.Start(proxy, targetHost, targetPort, PreferredLocalPort(key));
                _tunnels[key] = tunnel;
                return tunnel;
            }
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
            foreach (var c in key)
            {
                hash ^= c;
                hash *= fnvPrime;
            }
            return FIRST_DYNAMIC_PORT + (int)(hash % (uint)(LAST_DYNAMIC_PORT - FIRST_DYNAMIC_PORT));
        }

        /// <summary>
        /// True when the address points at this machine, in which case tunnelling it through a remote proxy
        /// is almost certainly a mistake.
        /// </summary>
        public static bool IsLocalAddress(string host)
        {
            if (string.IsNullOrWhiteSpace(host)) return true;
            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)) return true;
            if (!IPAddress.TryParse(host, out var ip)) return false;
            if (IPAddress.IsLoopback(ip)) return true;

            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                var b = ip.GetAddressBytes();
                if (b[0] == 10) return true;                                  // 10.0.0.0/8
                if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;     // 172.16.0.0/12
                if (b[0] == 192 && b[1] == 168) return true;                  // 192.168.0.0/16
                if (b[0] == 169 && b[1] == 254) return true;                  // 169.254.0.0/16 link-local
            }
            else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            {
                if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal) return true;
                if ((ip.GetAddressBytes()[0] & 0xFE) == 0xFC) return true;     // fc00::/7 unique local
            }
            return false;
        }

        public void Dispose()
        {
            ProxyTunnel[] tunnels;
            lock (_lock)
            {
                tunnels = _tunnels.Values.ToArray();
                _tunnels.Clear();
            }
            foreach (var tunnel in tunnels)
            {
                try
                {
                    tunnel.Dispose();
                }
                catch (Exception e)
                {
                    SimpleLogHelper.Warning(e);
                }
            }
        }
    }
}
