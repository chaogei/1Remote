using System;
using System.Threading;
using Renci.SshNet;
using Shawn.Utils;

namespace _1RM.Utils.Proxy
{
    /// <summary>
    /// A loopback listener whose traffic is carried to <c>targetHost:targetPort</c> inside a channel opened
    /// by an SSH server — OpenSSH's <c>-J</c> / <c>ProxyJump</c>, or equivalently <c>ssh -N -L</c>.
    ///
    /// It presents the same plain TCP endpoint as <see cref="ProxyTunnel"/> and is interchangeable with it,
    /// so every protocol gains jump-host support without knowing SSH exists. The forwarding itself is
    /// SSH.NET's: re-implementing direct-tcpip on top of our own listener would duplicate a channel
    /// multiplexer for no gain.
    /// </summary>
    public sealed class SshJumpTunnel : ITunnel
    {
        private readonly SshClient _client;
        private readonly ForwardedPortLocal _forwardedPort;
        private readonly string _credentialKey;

        private int _disposed;
        private int _stale;

        public string TargetHost { get; }
        public int TargetPort { get; }
        public int LocalPort { get; }

        public bool IsAlive
        {
            get
            {
                if (Volatile.Read(ref _disposed) != 0) return false;
                if (Volatile.Read(ref _stale) != 0) return false;
                try
                {
                    return _client.IsConnected && _forwardedPort.IsStarted;
                }
                catch
                {
                    return false;
                }
            }
        }

        private SshJumpTunnel(SshClient client, ForwardedPortLocal forwardedPort, string credentialKey, string targetHost, int targetPort)
        {
            _client = client;
            _forwardedPort = forwardedPort;
            _credentialKey = credentialKey;
            TargetHost = targetHost;
            TargetPort = targetPort;
            LocalPort = (int)forwardedPort.BoundPort;
        }

        /// <summary>
        /// Authenticates against the jump host and binds the loopback port.
        ///
        /// Unlike <see cref="ProxyTunnel"/> this cannot be deferred to the first connection: a forwarded port
        /// only exists on an established session. Connecting here is also the better trade — a bad key or a
        /// wrong user surfaces as an SSH error naming the cause, instead of an unexplained failure inside
        /// whichever protocol host happened to dial first.
        /// </summary>
        public static SshJumpTunnel Start(ProxyConfig jump, string targetHost, int targetPort, int preferredLocalPort)
        {
            var client = SshConnectionFactory.Connect(jump);

            ForwardedPortLocal? forwardedPort = null;
            try
            {
                forwardedPort = StartForwardedPort(client, preferredLocalPort, targetHost, targetPort);

                var tunnel = new SshJumpTunnel(client, forwardedPort, jump.GetCredentialKey(), targetHost, targetPort);
                SimpleLogHelper.Info($"SshJumpTunnel: {ProxyTunnel.LOCAL_HOST}:{tunnel.LocalPort} -> {targetHost}:{targetPort} via ssh {jump.UserName}@{jump.Address}:{jump.Port}");
                return tunnel;
            }
            catch
            {
                Stop(forwardedPort);
                Disconnect(client);
                client.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Binds the deterministic port when it is free, and lets the OS pick one when it is not — the same
        /// fallback <see cref="ProxyTunnel"/> uses, so a clash degrades to a working tunnel on another port
        /// rather than a failed session.
        /// </summary>
        private static ForwardedPortLocal StartForwardedPort(SshClient client, int preferredLocalPort, string targetHost, int targetPort)
        {
            if (preferredLocalPort > 0)
            {
                var preferred = new ForwardedPortLocal(ProxyTunnel.LOCAL_HOST, (uint)preferredLocalPort, targetHost, (uint)targetPort);
                try
                {
                    client.AddForwardedPort(preferred);
                    preferred.Start();
                    Watch(preferred);
                    return preferred;
                }
                catch (Exception e)
                {
                    SimpleLogHelper.Info($"SshJumpTunnel: port {preferredLocalPort} unavailable ({e.Message}), letting the OS choose");
                    Stop(preferred);
                    TryRemove(client, preferred);
                }
            }

            var any = new ForwardedPortLocal(ProxyTunnel.LOCAL_HOST, 0, targetHost, (uint)targetPort);
            client.AddForwardedPort(any);
            any.Start();
            Watch(any);
            return any;
        }

        private static void Watch(ForwardedPortLocal port)
        {
            // A channel that the jump host refuses fails here and nowhere else; without this the session
            // just sees its connection close, with no reason recorded anywhere.
            port.Exception += (_, e) => SimpleLogHelper.Warning($"SshJumpTunnel({port.BoundPort}): {e.Exception.Message}");
        }

        public void RefreshCredentials(ProxyConfig proxy)
        {
            // The secrets are baked into a session that has already authenticated, so unlike a relay tunnel
            // this one cannot swap them in place. Going stale makes the pool drop it and dial again with the
            // corrected credentials, which is the effect the caller is after either way.
            if (!string.Equals(_credentialKey, proxy.GetCredentialKey(), StringComparison.Ordinal))
                Volatile.Write(ref _stale, 1);
        }

        private static void Stop(ForwardedPortLocal? port)
        {
            try
            {
                if (port?.IsStarted == true)
                    port.Stop();
                port?.Dispose();
            }
            catch (Exception e)
            {
                SimpleLogHelper.Warning($"SshJumpTunnel: stopping the forwarded port failed, {e.Message}");
            }
        }

        private static void TryRemove(SshClient client, ForwardedPortLocal port)
        {
            try
            {
                client.RemoveForwardedPort(port);
            }
            catch (Exception e)
            {
                SimpleLogHelper.Warning($"SshJumpTunnel: removing the forwarded port failed, {e.Message}");
            }
        }

        private static void Disconnect(SshClient client)
        {
            try
            {
                if (client.IsConnected)
                    client.Disconnect();
            }
            catch (Exception e)
            {
                SimpleLogHelper.Warning($"SshJumpTunnel: disconnect failed, {e.Message}");
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            Stop(_forwardedPort);
            Disconnect(_client);
            try
            {
                _client.Dispose();
            }
            catch (Exception e)
            {
                SimpleLogHelper.Warning($"SshJumpTunnel: dispose failed, {e.Message}");
            }
        }
    }
}
