using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using _1RM.Model.Protocol.Base;
using _1RM.Utils.Proxy;

namespace _1RM.Utils.Reachability
{
    public enum EReachState
    {
        /// <summary>Never probed, or probing is switched off.</summary>
        Unknown = 0,

        /// <summary>The port accepted a connection.</summary>
        Online = 1,

        /// <summary>Nothing answered on that port within the timeout.</summary>
        Offline = 2,

        /// <summary>Deliberately not probed — see <see cref="ProbeResult.Reason"/>.</summary>
        Skipped = 3,
    }

    public readonly struct ProbeResult
    {
        public EReachState State { get; }
        public int LatencyMs { get; }

        /// <summary>Why a probe was skipped, for the tooltip. Empty otherwise.</summary>
        public string Reason { get; }

        private ProbeResult(EReachState state, int latencyMs, string reason)
        {
            State = state;
            LatencyMs = latencyMs;
            Reason = reason;
        }

        public static ProbeResult Online(int latencyMs) => new ProbeResult(EReachState.Online, latencyMs, "");
        public static ProbeResult Offline() => new ProbeResult(EReachState.Offline, 0, "");
        public static ProbeResult Skipped(string reason) => new ProbeResult(EReachState.Skipped, 0, reason);
    }

    /// <summary>
    /// Answers "would a connection to this server succeed right now" by opening one and dropping it.
    ///
    /// A TCP connect to the port the session actually uses, rather than ICMP: a host can answer pings with
    /// RDP switched off, a firewall can pass ping and drop 3389, and plenty of hosts drop ICMP entirely
    /// while serving happily. It also means the answer accounts for the proxy the session would take.
    /// </summary>
    public static class ServerProbe
    {
        public static async Task<ProbeResult> ProbeAsync(ProtocolBase server, ProxyConfig? proxy, int timeoutMs, CancellationToken ct)
        {
            if (server is not ProtocolBaseWithAddressPort target)
                return ProbeResult.Skipped("this protocol has no address to probe");

            var host = target.RealAddress?.Trim() ?? "";
            var port = ParsePort(target.RealPort);
            if (host.Length == 0 || port <= 0)
                return ProbeResult.Skipped("no address or port is configured");

            // A jump host would have to authenticate before it could tell us anything, which is far too
            // much to spend every sweep. Sessions through one are left unprobed rather than shown as down.
            if (proxy is { Type: EProxyType.SshJump })
                return ProbeResult.Skipped("servers reached through an SSH jump host are not probed");

            var stopwatch = Stopwatch.StartNew();
            try
            {
                if (proxy is { IsUsable: true } relay && !(relay.BypassForLocalAddress && ProxyTunnelPool.IsLocalAddress(host)))
                    await ProbeThroughProxyAsync(relay, host, port, timeoutMs, ct).ConfigureAwait(false);
                else
                    await ProbeDirectAsync(host, port, timeoutMs, ct).ConfigureAwait(false);

                stopwatch.Stop();
                return ProbeResult.Online((int)Math.Min(stopwatch.ElapsedMilliseconds, int.MaxValue));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Every failure mode reads the same to a user: nothing is listening there right now.
                return ProbeResult.Offline();
            }
        }

        private static async Task ProbeDirectAsync(string host, int port, int timeoutMs, CancellationToken ct)
        {
            using var client = new TcpClient { NoDelay = true };
            await ConnectAsync(client, host, port, timeoutMs, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Runs the same handshake a real session would, so the answer covers the proxy as well as the
        /// target — a dead proxy and an unreachable host are both things the user needs to see.
        /// </summary>
        private static async Task ProbeThroughProxyAsync(ProxyConfig proxy, string host, int port, int timeoutMs, CancellationToken ct)
        {
            using var client = new TcpClient { NoDelay = true };
            await ConnectAsync(client, proxy.Address, proxy.Port, timeoutMs, ct).ConfigureAwait(false);

            var stream = client.GetStream();
            stream.ReadTimeout = timeoutMs;
            stream.WriteTimeout = timeoutMs;
            // ProxyHandshake is synchronous; the stream timeouts above are what bound it.
            await Task.Run(() => ProxyHandshake.Perform(stream, proxy.Type, host, port, proxy.UserName, proxy.Password), ct)
                .ConfigureAwait(false);
        }

        private static async Task ConnectAsync(TcpClient client, string host, int port, int timeoutMs, CancellationToken ct)
        {
            var connecting = client.ConnectAsync(host, port);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var delay = Task.Delay(timeoutMs, timeout.Token);

            if (await Task.WhenAny(connecting, delay).ConfigureAwait(false) != connecting)
            {
                ct.ThrowIfCancellationRequested();
                throw new TimeoutException($"no answer from {host}:{port} within {timeoutMs}ms");
            }

            // stop the delay so its timer is not left running for the rest of the timeout
            timeout.Cancel();
            await connecting.ConfigureAwait(false); // observe the connect exception, if any
        }

        private static int ParsePort(string? port) =>
            int.TryParse((port ?? "").Trim(), out var parsed) && parsed > 0 && parsed <= 65535 ? parsed : 0;
    }
}
