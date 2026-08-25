using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _1RM.Utils;
using _1RM.Utils.PortForward;
using _1RM.Utils.Proxy;
using Renci.SshNet;
using Shawn.Utils;

namespace _1RM.Service
{
    /// <summary>
    /// Owns the standing port forwards and the SSH sessions carrying them.
    ///
    /// Sessions are shared per host: a bastion with a web console, a database and a SOCKS forward on it
    /// should cost one login, not three. That sharing is why starting and stopping both run through this one
    /// class instead of living on the individual forwards.
    /// </summary>
    public class PortForwardService : IDisposable
    {
        /// <summary>
        /// How often a live forward is checked against the session actually carrying it. A dropped session
        /// takes its forwards down without raising anything, so polling is the only way to notice; the
        /// interval is a compromise between a badge that lies and a check nobody asked for.
        /// </summary>
        private const double HEALTH_CHECK_INTERVAL_MS = 15 * 1000;

        private readonly ConfigurationService _configurationService;
        private readonly ProxyService _proxyService;
        private readonly System.Timers.Timer _healthCheck;

        /// <summary>
        /// Guards the two dictionaries below, and nothing else.
        ///
        /// INVARIANT: never talk to the network while holding it — no login, no ForwardedPort.Stop, no
        /// SshClient.Disconnect. The UI thread enters this same lock (the settings page reconciles on load,
        /// and Stop All is a button), so a holder that is waiting on a bastion freezes the whole app, hosted
        /// sessions included, until that bastion answers or times out. Decide under the lock, hand the
        /// blocking part to <see cref="PendingClose"/> and run it after the lock is released.
        /// </summary>
        private readonly object _lock = new object();

        /// <summary>Keyed by the config instance, so renaming a forward does not lose track of it.</summary>
        private readonly Dictionary<PortForwardConfig, LiveForward> _live = new Dictionary<PortForwardConfig, LiveForward>();

        /// <summary>
        /// Keyed by <see cref="ProxyConfig.GetEndPointKey"/>, one authenticated client per host. Entries are
        /// lazy so the login itself happens outside <see cref="_lock"/>; threads racing for the same host
        /// still serialise on the <see cref="Lazy{T}"/> and share one session.
        /// </summary>
        private readonly Dictionary<string, Lazy<SshClient>> _sessions = new Dictionary<string, Lazy<SshClient>>();

        /// <summary>Set while a health check is running, so a slow one does not have the next tick pile up behind it.</summary>
        private int _healthCheckRunning;

        public PortForwardService(ConfigurationService configurationService, ProxyService proxyService)
        {
            _configurationService = configurationService;
            _proxyService = proxyService;

            _healthCheck = new System.Timers.Timer(HEALTH_CHECK_INTERVAL_MS) { AutoReset = true };
            _healthCheck.Elapsed += (_, _) =>
            {
                // Elapsed is raised on a fresh pool thread every interval whatever the last one is doing, and
                // tearing down a session the network has already eaten can outlast the interval.
                if (Interlocked.Exchange(ref _healthCheckRunning, 1) != 0) return;
                try
                {
                    RefreshStatuses();
                }
                catch (Exception e)
                {
                    // an escaping exception on a timer thread would take the process down
                    SimpleLogHelper.Warning($"PortForwardService: health check failed, {e.Message}");
                }
                finally
                {
                    Volatile.Write(ref _healthCheckRunning, 0);
                }
            };
            _healthCheck.Start();
        }

        private sealed class LiveForward
        {
            public LiveForward(string sessionKey, ForwardedPort port)
            {
                SessionKey = sessionKey;
                Port = port;
            }

            public string SessionKey { get; }
            public ForwardedPort Port { get; }
        }

        /// <summary>
        /// Everything a decision taken under <see cref="_lock"/> still has to close. Stopping a forwarded
        /// port waits for its channels and disconnecting a session is a protocol exchange, so both belong
        /// out here rather than inside the lock.
        /// </summary>
        private sealed class PendingClose
        {
            public List<(string Name, ForwardedPort Port, SshClient? Owner)> Ports { get; } = new List<(string, ForwardedPort, SshClient?)>();
            public List<SshClient> Sessions { get; } = new List<SshClient>();
            public bool IsEmpty => Ports.Count == 0 && Sessions.Count == 0;
        }

        public List<PortForwardConfig> Forwards => _configurationService.PortForwards;

        public void Save() => _configurationService.Save();

        /// <summary>The SSH entries on the proxy page, which are the only hosts a forward can run through.</summary>
        public IReadOnlyList<ProxyConfig> AvailableHosts =>
            _proxyService.Proxies.Where(x => x.Type == EProxyType.SshJump).ToList();

        /// <summary>
        /// Brings a forward up, replacing it if it was already running. Blocks while the SSH session is
        /// established, so callers on the UI thread should hand it to <see cref="StartAsync"/> instead.
        /// </summary>
        public void Start(PortForwardConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            var invalid = config.Validate();
            if (invalid != null)
            {
                Fail(config, invalid);
                return;
            }

            var host = _proxyService.Find(config.SshHostName);
            if (host == null)
            {
                Fail(config, IoC.Translate("port_forward_host_gone", config.SshHostName));
                return;
            }
            if (host.Type != EProxyType.SshJump)
            {
                Fail(config, IoC.Translate("port_forward_host_not_ssh", host.Name));
                return;
            }
            if (!host.IsUsable)
            {
                Fail(config, IoC.Translate("proxy_incomplete_hint", host.Name));
                return;
            }

            try
            {
                // Replacing whatever was running and logging in are both network work, so both happen with
                // the lock released; see the INVARIANT on _lock.
                Detach(config);

                var sessionKey = host.GetEndPointKey();
                var client = GetOrConnectSession(sessionKey, host);
                var port = Build(config);
                client.AddForwardedPort(port);
                // A forward that the far side refuses fails here and nowhere else; without this the
                // entry would sit there looking healthy while nothing could get through it.
                port.Exception += (_, e) => OnPortException(config, e.Exception);
                port.Start();
                lock (_lock)
                {
                    _live[config] = new LiveForward(sessionKey, port);
                }

                config.LastError = "";
                config.Status = EPortForwardStatus.Running;
                SimpleLogHelper.Info($"PortForwardService: '{config.Name}' up, {config.Summary}");
            }
            catch (Exception e)
            {
                SimpleLogHelper.Warning($"PortForwardService: '{config.Name}' failed to start, {e.Message}");
                Stop(config);
                Fail(config, e.Message);
            }
        }

        public Task StartAsync(PortForwardConfig config) => Task.Run(() => Start(config));

        /// <summary>
        /// Takes a forward down. Blocks while the port and possibly its session are closed, so callers on
        /// the UI thread should use <see cref="StopAsync"/> instead.
        /// </summary>
        public void Stop(PortForwardConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            Detach(config);
            config.Status = EPortForwardStatus.Stopped;
            config.LastError = "";
        }

        /// <summary>Unregisters a forward and closes it, with <see cref="_lock"/> released for the closing part.</summary>
        private void Detach(PortForwardConfig config)
        {
            var pending = new PendingClose();
            lock (_lock)
            {
                DetachLocked(config, pending);
                PruneSessionsLocked(pending);
            }
            Close(pending);
        }

        public Task StopAsync(PortForwardConfig config) => Task.Run(() => Stop(config));

        /// <summary>
        /// Starts everything marked auto-start, off the caller's thread. Each one authenticates, so doing
        /// this inline would add seconds to app startup for no benefit.
        /// </summary>
        public Task StartAutoStartsAsync()
        {
            var pending = Forwards.Where(x => x.AutoStart).ToList();
            if (pending.Count == 0) return Task.CompletedTask;
            return Task.Run(() =>
            {
                foreach (var config in pending)
                    Start(config);
            });
        }

        /// <summary>
        /// Reconciles what the entries claim with what is actually up. A session dropped by the network or
        /// reaped by the server takes its forwards down silently, and nothing else would notice.
        /// </summary>
        public void RefreshStatuses()
        {
            List<PortForwardConfig> broken;
            var pending = new PendingClose();
            lock (_lock)
            {
                if (_live.Count == 0) return;
                broken = _live
                    .Where(pair => !IsCarryingLocked(pair.Value))
                    .Select(pair => pair.Key)
                    .ToList();

                foreach (var config in broken)
                    DetachLocked(config, pending);
                if (broken.Count > 0)
                    PruneSessionsLocked(pending);
            }

            Close(pending);

            foreach (var config in broken)
                Fail(config, IoC.Translate("port_forward_session_lost"));
        }

        /// <summary>
        /// Caller must hold <see cref="_lock"/>. Only reads state that is already there: an entry whose
        /// login has not finished is not carrying anything yet, and forcing the <see cref="Lazy{T}"/> here
        /// would drag the network into the lock.
        /// </summary>
        private bool IsCarryingLocked(LiveForward live)
        {
            try
            {
                return _sessions.TryGetValue(live.SessionKey, out var entry)
                       && entry.IsValueCreated
                       && entry.Value.IsConnected
                       && live.Port.IsStarted;
            }
            catch
            {
                return false;
            }
        }

        private static ForwardedPort Build(PortForwardConfig config)
        {
            var boundPort = (uint)config.BoundPort;
            return config.Type switch
            {
                EPortForwardType.Local => new ForwardedPortLocal(config.BoundAddress, boundPort, config.DestinationHost, (uint)config.DestinationPort),
                // The bound address of a remote forward is interpreted by sshd, and binding it anywhere but
                // loopback additionally needs GatewayPorts enabled there — a server-side setting we cannot
                // check from here, so a refusal surfaces through port.Exception instead.
                EPortForwardType.Remote => new ForwardedPortRemote(config.BoundAddress, boundPort, config.DestinationHost, (uint)config.DestinationPort),
                EPortForwardType.Dynamic => new ForwardedPortDynamic(config.BoundAddress, boundPort),
                _ => throw new NotSupportedException($"unsupported forward type {config.Type}"),
            };
        }

        /// <summary>
        /// The session for <paramref name="host"/>, logging in if there is none. Caller must NOT hold
        /// <see cref="_lock"/>: authenticating is a network round trip.
        /// </summary>
        private SshClient GetOrConnectSession(string sessionKey, ProxyConfig host)
        {
            // At most two passes: the first may turn up a session that has since died, and the replacement
            // registered on the second is what gets returned.
            for (var attempt = 0; attempt < 2; attempt++)
            {
                Lazy<SshClient>? entry;
                lock (_lock)
                {
                    if (!_sessions.TryGetValue(sessionKey, out entry) || entry == null)
                    {
                        entry = new Lazy<SshClient>(() => SshConnectionFactory.Connect(host), LazyThreadSafetyMode.ExecutionAndPublication);
                        _sessions[sessionKey] = entry;
                    }
                }

                SshClient client;
                try
                {
                    client = entry.Value;
                }
                catch
                {
                    // A Lazy remembers the failure, so the entry has to go or this host stays unusable for
                    // the rest of the run.
                    ForgetSession(sessionKey, entry);
                    throw;
                }

                if (client.IsConnected)
                    return client;

                ForgetSession(sessionKey, entry);
                Close(client);
            }

            throw new InvalidOperationException($"could not keep a session to '{host.Name}' alive");
        }

        private void ForgetSession(string sessionKey, Lazy<SshClient> entry)
        {
            lock (_lock)
            {
                // Only when it is still the same entry: another thread may already have put a fresh one in.
                if (_sessions.TryGetValue(sessionKey, out var current) && ReferenceEquals(current, entry))
                    _sessions.Remove(sessionKey);
            }
        }

        /// <summary>
        /// Caller must hold <see cref="_lock"/>. Unregisters the forward and hands its port to
        /// <paramref name="pending"/>; nothing here touches the network.
        /// </summary>
        private void DetachLocked(PortForwardConfig config, PendingClose pending)
        {
            if (!_live.TryGetValue(config, out var live)) return;
            _live.Remove(config);

            SshClient? owner = null;
            if (_sessions.TryGetValue(live.SessionKey, out var entry) && entry.IsValueCreated)
                owner = entry.Value;
            pending.Ports.Add((config.Name, live.Port, owner));
        }

        /// <summary>
        /// Caller must hold <see cref="_lock"/>. Drops sessions that no longer carry anything. Cannot be
        /// folded into <see cref="DetachLocked"/>: other forwards may still be riding the same login. An
        /// entry still logging in is left alone — its forward is not in <c>_live</c> yet.
        /// </summary>
        private void PruneSessionsLocked(PendingClose pending)
        {
            var inUse = new HashSet<string>(_live.Values.Select(x => x.SessionKey), StringComparer.Ordinal);
            foreach (var key in _sessions.Keys.Where(k => !inUse.Contains(k)).ToList())
            {
                var entry = _sessions[key];
                if (!entry.IsValueCreated) continue;
                _sessions.Remove(key);
                pending.Sessions.Add(entry.Value);
            }
        }

        /// <summary>Runs the blocking part of a teardown. Caller must NOT hold <see cref="_lock"/>.</summary>
        private static void Close(PendingClose pending)
        {
            if (pending.IsEmpty) return;

            foreach (var (name, port, owner) in pending.Ports)
            {
                try
                {
                    if (port.IsStarted)
                        port.Stop();
                }
                catch (Exception e)
                {
                    SimpleLogHelper.Warning($"PortForwardService: stopping '{name}' failed, {e.Message}");
                }

                if (owner != null)
                {
                    try
                    {
                        owner.RemoveForwardedPort(port);
                    }
                    catch (Exception e)
                    {
                        SimpleLogHelper.Warning($"PortForwardService: detaching '{name}' failed, {e.Message}");
                    }
                }

                try
                {
                    // IDisposable lives on the concrete forward types, not on the ForwardedPort base.
                    (port as IDisposable)?.Dispose();
                }
                catch (Exception e)
                {
                    SimpleLogHelper.Warning($"PortForwardService: disposing '{name}' failed, {e.Message}");
                }
            }

            foreach (var client in pending.Sessions)
                Close(client);
        }

        private void OnPortException(PortForwardConfig config, Exception exception)
        {
            SimpleLogHelper.Warning($"PortForwardService: '{config.Name}' - {exception.Message}");
            config.LastError = exception.Message;
        }

        private static void Fail(PortForwardConfig config, string reason)
        {
            config.LastError = reason;
            config.Status = EPortForwardStatus.Failed;
        }

        private static void Close(SshClient client)
        {
            try
            {
                if (client.IsConnected)
                    client.Disconnect();
            }
            catch (Exception e)
            {
                SimpleLogHelper.Warning($"PortForwardService: disconnect failed, {e.Message}");
            }
            try
            {
                client.Dispose();
            }
            catch (Exception e)
            {
                SimpleLogHelper.Warning($"PortForwardService: dispose failed, {e.Message}");
            }
        }

        /// <summary>
        /// Takes every forward down. Blocks while the sessions carrying them are closed, so callers on the
        /// UI thread should use <see cref="StopAllAsync"/> instead.
        /// </summary>
        public void StopAll()
        {
            List<PortForwardConfig> running;
            var pending = new PendingClose();
            lock (_lock)
            {
                running = _live.Keys.ToList();
                foreach (var config in running)
                    DetachLocked(config, pending);
                PruneSessionsLocked(pending);
            }
            Close(pending);
            foreach (var config in running)
            {
                config.Status = EPortForwardStatus.Stopped;
                config.LastError = "";
            }
        }

        public Task StopAllAsync() => Task.Run(StopAll);

        /// <summary>Reconciles off the caller's thread, for the settings page.</summary>
        public Task RefreshStatusesAsync() => Task.Run(RefreshStatuses);

        public void Dispose()
        {
            _healthCheck.Stop();
            _healthCheck.Dispose();
            StopAll();
        }
    }
}
