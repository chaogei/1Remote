using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Shawn.Utils;

namespace _1RM.Utils.Proxy
{
    /// <summary>
    /// A loopback listener that forwards everything it accepts to <c>TargetHost:TargetPort</c> through a
    /// proxy server.
    ///
    /// This exists because the two protocols that need a proxy most cannot be given one directly: RDP runs
    /// on the MSTSC ActiveX control and VNC on a pre-built package, and neither exposes its socket. Handing
    /// every protocol a plain TCP endpoint on 127.0.0.1 instead means one implementation covers all of them,
    /// including any protocol added later.
    /// </summary>
    public sealed class ProxyTunnel : ITunnel
    {
        public const string LOCAL_HOST = "127.0.0.1";
        private const int PROXY_CONNECT_TIMEOUT_MS = 15 * 1000;
        private const int HANDSHAKE_TIMEOUT_MS = 15 * 1000;
        private const int RELAY_BUFFER_SIZE = 32 * 1024;

        private readonly EProxyType _proxyType;
        private readonly string _proxyAddress;
        private readonly int _proxyPort;
        private readonly string _proxyUserName;
        private string _proxyPassword;

        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private int _disposed;

        public string TargetHost { get; }
        public int TargetPort { get; }
        public int LocalPort { get; }

        public bool IsAlive => Volatile.Read(ref _disposed) == 0;

        private ProxyTunnel(ProxyConfig proxy, string targetHost, int targetPort, TcpListener listener)
        {
            _proxyType = proxy.Type;
            _proxyAddress = proxy.Address;
            _proxyPort = proxy.Port;
            _proxyUserName = proxy.UserName;
            _proxyPassword = proxy.Password;

            TargetHost = targetHost;
            TargetPort = targetPort;
            _listener = listener;
            LocalPort = ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        /// <summary>
        /// Binds a loopback port and starts accepting. Only the bind can fail here; reaching the proxy is
        /// deferred to the first accepted connection, so a dead proxy surfaces as a normal connection
        /// failure in the protocol host rather than an error at configuration time.
        /// </summary>
        public static ProxyTunnel Start(ProxyConfig proxy, string targetHost, int targetPort, int preferredLocalPort)
        {
            var listener = Bind(preferredLocalPort);
            var tunnel = new ProxyTunnel(proxy, targetHost, targetPort, listener);
            SimpleLogHelper.Info($"ProxyTunnel: {LOCAL_HOST}:{tunnel.LocalPort} -> {targetHost}:{targetPort} via {proxy.Type} {proxy.Address}:{proxy.Port}");
            _ = Task.Run(tunnel.AcceptLoopAsync);
            return tunnel;
        }

        private static TcpListener Bind(int preferredLocalPort)
        {
            if (preferredLocalPort > 0)
            {
                try
                {
                    var preferred = new TcpListener(IPAddress.Loopback, preferredLocalPort);
                    preferred.Start();
                    return preferred;
                }
                catch (SocketException)
                {
                    // the preferred port is taken, fall through to an OS assigned one
                }
            }

            var any = new TcpListener(IPAddress.Loopback, 0);
            any.Start();
            return any;
        }

        /// <summary>
        /// Takes the password from the current configuration, for connections opened from now on.
        ///
        /// Everything else that identifies a tunnel is part of its pool key and so cannot have changed
        /// under it; the password is not. Without this, correcting a mistyped proxy password would have no
        /// effect until the app was restarted, because the pool would keep handing back the tunnel that
        /// still held the wrong one.
        /// </summary>
        public void RefreshCredentials(ProxyConfig proxy)
        {
            Volatile.Write(ref _proxyPassword, proxy.Password);
        }

        private async Task AcceptLoopAsync()
        {
            while (IsAlive)
            {
                TcpClient inbound;
                try
                {
                    inbound = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    return; // disposed
                }
                catch (SocketException e)
                {
                    if (!IsAlive) return;
                    SimpleLogHelper.Warning($"ProxyTunnel({LocalPort}): accept failed, {e.Message}");
                    continue;
                }

                _ = Task.Run(() => RelayAsync(inbound));
            }
        }

        private async Task RelayAsync(TcpClient inbound)
        {
            TcpClient? outbound = null;
            try
            {
                inbound.NoDelay = true;
                outbound = new TcpClient { NoDelay = true };

                var connecting = outbound.ConnectAsync(_proxyAddress, _proxyPort);
                if (await Task.WhenAny(connecting, Task.Delay(PROXY_CONNECT_TIMEOUT_MS, _cts.Token)).ConfigureAwait(false) != connecting)
                    throw new IOException($"timed out connecting to the proxy at {_proxyAddress}:{_proxyPort}");
                await connecting.ConfigureAwait(false); // observe the connect exception, if any

                var proxyStream = outbound.GetStream();
                // the handshake is synchronous, so these timeouts actually bound it
                proxyStream.ReadTimeout = HANDSHAKE_TIMEOUT_MS;
                proxyStream.WriteTimeout = HANDSHAKE_TIMEOUT_MS;
                ProxyHandshake.Perform(proxyStream, _proxyType, TargetHost, TargetPort, _proxyUserName, Volatile.Read(ref _proxyPassword));
                // an interactive session can idle for hours, it must not be torn down for being quiet
                proxyStream.ReadTimeout = Timeout.Infinite;
                proxyStream.WriteTimeout = Timeout.Infinite;

                var inboundStream = inbound.GetStream();
                var upstream = PumpAsync(inboundStream, proxyStream, _cts.Token);
                var downstream = PumpAsync(proxyStream, inboundStream, _cts.Token);
                // either half closing ends the session; the other pump unblocks when its socket is disposed
                await Task.WhenAny(upstream, downstream).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // tunnel disposed
            }
            catch (Exception e)
            {
                SimpleLogHelper.Warning($"ProxyTunnel({LocalPort} -> {TargetHost}:{TargetPort}): {e.Message}");
            }
            finally
            {
                Close(inbound);
                Close(outbound);
            }
        }

        private static async Task PumpAsync(Stream from, Stream to, CancellationToken ct)
        {
            var buffer = new byte[RELAY_BUFFER_SIZE];
            try
            {
                int read;
                while ((read = await from.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
                {
                    await to.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
                    await to.FlushAsync(ct).ConfigureAwait(false);
                }
            }
            catch
            {
                // the peer went away, which is the normal way a session ends
            }
        }

        private static void Close(TcpClient? client)
        {
            try
            {
                client?.Close();
            }
            catch
            {
                // ignored
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try
            {
                // deliberately not disposing _cts: in-flight pumps still register on its token, and racing
                // a dispose against them buys nothing — a cancelled source holds no unmanaged resource
                _cts.Cancel();
                _listener.Stop();
            }
            catch (Exception e)
            {
                SimpleLogHelper.Warning($"ProxyTunnel({LocalPort}): dispose failed, {e.Message}");
            }
        }
    }
}
