using System;
using System.Collections.Generic;
using System.Linq;
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

        private readonly object _lock = new object();

        /// <summary>Keyed by the config instance, so renaming a forward does not lose track of it.</summary>
        private readonly Dictionary<PortForwardConfig, LiveForward> _live = new Dictionary<PortForwardConfig, LiveForward>();

        /// <summary>Keyed by <see cref="ProxyConfig.GetEndPointKey"/>, one authenticated client per host.</summary>
        private readonly Dictionary<string, SshClient> _sessions = new Dictionary<string, SshClient>();

        public PortForwardService(ConfigurationService configurationService, ProxyService proxyService)
        {
            _configurationService = configurationService;
            _proxyService = proxyService;

            _healthCheck = new System.Timers.Timer(HEALTH_CHECK_INTERVAL_MS) { AutoReset = true };
            _healthCheck.Elapsed += (_, _) =>
            {
                try
                {
                    RefreshStatuses();
                }
                catch (Exception e)
                {
                    // an escaping exception on a timer thread would take the process down
                    SimpleLogHelper.Warning($"PortForwardService: health check failed, {e.Message}");
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
                lock (_lock)
                {
                    StopLocked(config);

                    var sessionKey = host.GetEndPointKey();
                    var client = GetOrConnectLocked(sessionKey, host);
                    var port = Build(config);
                    client.AddForwardedPort(port);
                    // A forward that the far side refuses fails here and nowhere else; without this the
                    // entry would sit there looking healthy while nothing could get through it.
                    port.Exception += (_, e) => OnPortException(config, e.Exception);
                    port.Start();
                    _live[config] = new LiveForward(sessionKey, port);
                }

                config.LastError = "";
                config.Status = EPortForwardStatus.Running;
                SimpleLogHelper.Info($"PortForwardService: '{config.Name}' up, {config.Summary}");
            }
            catch (Exception e)
            {
                SimpleLogHelper.Warning($"PortForwardService: '{config.Name}' failed to start, {e.Message}");
                lock (_lock)
                {
                    StopLocked(config);
                    PruneSessionsLocked();
                }
                Fail(config, e.Message);
            }
        }

        public Task StartAsync(PortForwardConfig config) => Task.Run(() => Start(config));

        public void Stop(PortForwardConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            lock (_lock)
            {
                StopLocked(config);
                PruneSessionsLocked();
            }
            config.Status = EPortForwardStatus.Stopped;
            config.LastError = "";
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
            lock (_lock)
            {
                if (_live.Count == 0) return;
                broken = _live
                    .Where(pair => !IsCarrying(pair.Value))
                    .Select(pair => pair.Key)
                    .ToList();

                foreach (var config in broken)
                    StopLocked(config);
                if (broken.Count > 0)
                    PruneSessionsLocked();
            }

            foreach (var config in broken)
                Fail(config, IoC.Translate("port_forward_session_lost"));
        }

        private bool IsCarrying(LiveForward live)
        {
            try
            {
                return _sessions.TryGetValue(live.SessionKey, out var client)
                       && client.IsConnected
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

        private SshClient GetOrConnectLocked(string sessionKey, ProxyConfig host)
        {
            if (_sessions.TryGetValue(sessionKey, out var existing))
            {
                if (existing.IsConnected)
                    return existing;
                _sessions.Remove(sessionKey);
                Close(existing);
            }

            var client = SshConnectionFactory.Connect(host);
            _sessions[sessionKey] = client;
            return client;
        }

        private void StopLocked(PortForwardConfig config)
        {
            if (!_live.TryGetValue(config, out var live)) return;
            _live.Remove(config);

            try
            {
                if (live.Port.IsStarted)
                    live.Port.Stop();
            }
            catch (Exception e)
            {
                SimpleLogHelper.Warning($"PortForwardService: stopping '{config.Name}' failed, {e.Message}");
            }

            if (_sessions.TryGetValue(live.SessionKey, out var client))
            {
                try
                {
                    client.RemoveForwardedPort(live.Port);
                }
                catch (Exception e)
                {
                    SimpleLogHelper.Warning($"PortForwardService: detaching '{config.Name}' failed, {e.Message}");
                }
            }

            try
            {
                // IDisposable lives on the concrete forward types, not on the ForwardedPort base.
                (live.Port as IDisposable)?.Dispose();
            }
            catch (Exception e)
            {
                SimpleLogHelper.Warning($"PortForwardService: disposing '{config.Name}' failed, {e.Message}");
            }
        }

        /// <summary>
        /// Drops sessions that no longer carry anything. Cannot be folded into <see cref="StopLocked"/>:
        /// other forwards may still be riding the same login.
        /// </summary>
        private void PruneSessionsLocked()
        {
            var inUse = new HashSet<string>(_live.Values.Select(x => x.SessionKey), StringComparer.Ordinal);
            foreach (var key in _sessions.Keys.Where(k => !inUse.Contains(k)).ToList())
            {
                var client = _sessions[key];
                _sessions.Remove(key);
                Close(client);
            }
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

        public void StopAll()
        {
            List<PortForwardConfig> running;
            lock (_lock)
            {
                running = _live.Keys.ToList();
                foreach (var config in running)
                    StopLocked(config);
                PruneSessionsLocked();
            }
            foreach (var config in running)
            {
                config.Status = EPortForwardStatus.Stopped;
                config.LastError = "";
            }
        }

        public void Dispose()
        {
            _healthCheck.Stop();
            _healthCheck.Dispose();
            StopAll();
        }
    }
}
