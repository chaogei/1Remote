using System;
using System.Collections.Generic;
using System.Linq;
using _1RM.Model.Protocol.Base;
using _1RM.Utils.Proxy;
using Shawn.Utils;

namespace _1RM.Service
{
    /// <summary>
    /// Owns the global proxy list and the live tunnels built from it.
    ///
    /// Protocols are never taught to speak SOCKS or HTTP CONNECT themselves. Instead every proxied session
    /// is pointed at a loopback port that relays through the proxy, so RDP (an ActiveX control) and VNC (a
    /// pre-built package) get proxy support for free, and there is exactly one implementation to maintain.
    /// </summary>
    public class ProxyService : IDisposable
    {
        private readonly ConfigurationService _configurationService;
        private readonly ProxyTunnelPool _pool = new ProxyTunnelPool();

        public ProxyService(ConfigurationService configurationService)
        {
            _configurationService = configurationService;
        }

        public List<ProxyConfig> Proxies => _configurationService.Proxies;

        public ProxyConfig? Find(string? name)
        {
            return string.IsNullOrEmpty(name)
                ? null
                : Proxies.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.Ordinal));
        }

        public void Save()
        {
            foreach (var proxy in Proxies)
                proxy.EncryptPassword();
            _configurationService.Save();
        }

        /// <summary>
        /// Points <paramref name="protocol"/> at a loopback endpoint that tunnels to its real address through
        /// the proxy it selected. Call it on the decrypted clone, after any credential has been applied and
        /// before the protocol reaches a runner. Returns true when the address was rewritten.
        /// </summary>
        public bool ApplyTo(ProtocolBase protocol)
        {
            if (protocol is not ProtocolBaseWithAddressPort target)
                return false;

            var proxy = Find(protocol.ProxyName);
            if (proxy == null)
                return false;
            if (!proxy.IsUsable)
            {
                SimpleLogHelper.Warning($"ProxyService: proxy '{protocol.ProxyName}' is incomplete, connecting directly");
                return false;
            }

            var host = target.Address?.Trim() ?? "";
            var port = target.GetPort();
            if (string.IsNullOrEmpty(host) || port <= 0)
                return false;

            if (proxy.BypassForLocalAddress && ProxyTunnelPool.IsLocalAddress(host))
            {
                SimpleLogHelper.Info($"ProxyService: '{host}' is a local address, bypassing proxy '{proxy.Name}'");
                return false;
            }

            try
            {
                var tunnel = _pool.GetOrCreate(proxy, host, port);

                // The Address setter renames the server when the display name still mirrors the old address,
                // which would retitle a session named after its IP to "127.0.0.1".
                var displayName = protocol.DisplayName;
                target.Address = ProxyTunnel.LOCAL_HOST;
                target.Port = tunnel.LocalPort.ToString();
                protocol.DisplayName = displayName;

                SimpleLogHelper.Info($"ProxyService: {displayName} -> {host}:{port} through proxy '{proxy.Name}' at {ProxyTunnel.LOCAL_HOST}:{tunnel.LocalPort}");
                return true;
            }
            catch (Exception e)
            {
                SimpleLogHelper.Error(e);
                return false;
            }
        }

        public void Dispose()
        {
            _pool.Dispose();
        }
    }
}
