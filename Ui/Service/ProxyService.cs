using System;
using System.Collections.Generic;
using System.Linq;
using _1RM.Model;
using _1RM.Model.Protocol.Base;
using _1RM.Utils;
using _1RM.Utils.Proxy;
using Shawn.Utils;

namespace _1RM.Service
{
    /// <summary>
    /// What happened when a session was pointed at its proxy.
    /// </summary>
    public enum EProxyApplyResult
    {
        /// <summary>No proxy was asked for, or the target is this machine — connecting straight out is intended.</summary>
        Direct,

        /// <summary>The address now points at a loopback endpoint that relays through the proxy.</summary>
        Tunnelled,

        /// <summary>The chosen proxy was unusable and the user refused to connect without it.</summary>
        Abort,
    }

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
            _configurationService.Save();
        }

        /// <summary>
        /// The servers that route through <paramref name="proxyName"/>. Servers reference a proxy by name, so
        /// this is what tells the settings page how much damage a rename or a delete is about to do.
        /// </summary>
        public static IReadOnlyList<ProtocolBase> FindServersUsing(string proxyName)
        {
            if (string.IsNullOrEmpty(proxyName))
                return Array.Empty<ProtocolBase>();
            return IoC.TryGet<GlobalData>()?.VmItemList
                       .Select(x => x.Server)
                       .Where(x => string.Equals(x.ProxyName, proxyName, StringComparison.Ordinal))
                       .ToList()
                   ?? (IReadOnlyList<ProtocolBase>)Array.Empty<ProtocolBase>();
        }

        /// <summary>
        /// Moves every server that pointed at <paramref name="oldName"/> over to <paramref name="newName"/>,
        /// so renaming a proxy does not silently orphan the servers using it. Returns how many moved.
        /// </summary>
        public static int RenameInServers(string oldName, string newName)
        {
            var affected = FindServersUsing(oldName);
            if (affected.Count == 0) return 0;
            foreach (var server in affected)
                server.ProxyName = newName;
            IoC.Get<GlobalData>().UpdateServer(affected);
            SimpleLogHelper.Info($"ProxyService: proxy renamed '{oldName}' -> '{newName}', {affected.Count} server(s) followed");
            return affected.Count;
        }

        /// <summary>
        /// Points <paramref name="protocol"/> at a loopback endpoint that tunnels to its real address through
        /// the proxy it selected. Call it on the decrypted clone, after any credential has been applied and
        /// before the protocol reaches a runner.
        /// </summary>
        public EProxyApplyResult ApplyTo(ProtocolBase protocol)
        {
            if (protocol is not ProtocolBaseWithAddressPort target)
                return EProxyApplyResult.Direct;

            var proxy = Find(protocol.ProxyName);
            if (proxy == null)
            {
                return string.IsNullOrEmpty(protocol.ProxyName)
                    ? EProxyApplyResult.Direct
                    : AskToFallBackToDirect(protocol, IoC.Translate("proxy_gone_hint", protocol.ProxyName));
            }
            if (!proxy.IsUsable)
                return AskToFallBackToDirect(protocol, IoC.Translate("proxy_incomplete_hint", proxy.Name));

            var host = target.Address?.Trim() ?? "";
            var port = target.GetPort();
            if (string.IsNullOrEmpty(host) || port <= 0)
                return EProxyApplyResult.Direct;

            if (proxy.BypassForLocalAddress && ProxyTunnelPool.IsLocalAddress(host))
            {
                SimpleLogHelper.Info($"ProxyService: '{host}' is this machine, bypassing proxy '{proxy.Name}'");
                return EProxyApplyResult.Direct;
            }

            try
            {
                var tunnel = _pool.GetOrCreate(proxy, host, port);
                target.RedirectThroughTunnel(ProxyTunnel.LOCAL_HOST, tunnel.LocalPort);

                SimpleLogHelper.Info($"ProxyService: {protocol.DisplayName} -> {host}:{port} through proxy '{proxy.Name}' at {ProxyTunnel.LOCAL_HOST}:{tunnel.LocalPort}");
                return EProxyApplyResult.Tunnelled;
            }
            catch (Exception e)
            {
                SimpleLogHelper.Error(e);
                return AskToFallBackToDirect(protocol, IoC.Translate("proxy_tunnel_failed_hint", proxy.Name, e.Message));
            }
        }

        /// <summary>
        /// A session that asked for a proxy is about to go out without one. Silently connecting anyway would
        /// send traffic straight to the target while the user believes it is being relayed, so the choice is
        /// theirs to make.
        /// </summary>
        private static EProxyApplyResult AskToFallBackToDirect(ProtocolBase protocol, string reason)
        {
            SimpleLogHelper.Warning($"ProxyService: {protocol.DisplayName} - {reason}");
            var message = reason
                          + Environment.NewLine + Environment.NewLine
                          + IoC.Translate("proxy_fall_back_to_direct_question");
            return MessageBoxHelper.Confirm(message, IoC.Translate("proxy_unavailable_title"))
                ? EProxyApplyResult.Direct
                : EProxyApplyResult.Abort;
        }

        public void Dispose()
        {
            _pool.Dispose();
        }
    }
}
