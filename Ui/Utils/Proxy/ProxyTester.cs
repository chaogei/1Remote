using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace _1RM.Utils.Proxy
{
    /// <summary>
    /// Verifies a proxy end to end by asking it to reach a target, then dropping the connection. Used by the
    /// settings page so a misconfigured proxy is caught there instead of surfacing as a failed session.
    /// </summary>
    public static class ProxyTester
    {
        /// <summary>
        /// How long a jump-host probe waits for the target to say something before calling the channel open.
        /// Long enough for a refusal to travel back from the jump host, short enough not to stall the page.
        /// </summary>
        private const int SSH_PROBE_QUIET_MS = 2500;

        public sealed class Result
        {
            public bool IsSuccess { get; }
            public string Message { get; }
            public long ElapsedMilliseconds { get; }

            private Result(bool isSuccess, string message, long elapsedMilliseconds)
            {
                IsSuccess = isSuccess;
                Message = message;
                ElapsedMilliseconds = elapsedMilliseconds;
            }

            public static Result Ok(long elapsed) => new Result(true, "", elapsed);
            public static Result Fail(string message) => new Result(false, message, 0);
        }

        public static async Task<Result> TestAsync(ProxyConfig proxy, string targetHost, int targetPort, int timeoutMs = 10 * 1000)
        {
            if (proxy == null) return Result.Fail("no proxy selected");
            if (!proxy.IsUsable)
                return Result.Fail(proxy.Type == EProxyType.SshJump
                    ? "the jump host address, port or user name is incomplete"
                    : "the proxy address, port or type is incomplete");
            if (string.IsNullOrWhiteSpace(targetHost)) return Result.Fail("the test target is empty");
            if (targetPort <= 0 || targetPort > 65535) return Result.Fail("the test target port is out of range");

            var stopwatch = Stopwatch.StartNew();
            var host = targetHost.Trim();
            return await Task.Run(() =>
            {
                try
                {
                    return proxy.Type == EProxyType.SshJump
                        ? TestSshJump(proxy, host, targetPort, timeoutMs, stopwatch)
                        : TestRelay(proxy, host, targetPort, timeoutMs, stopwatch);
                }
                catch (AggregateException e)
                {
                    return Result.Fail(e.GetBaseException().Message);
                }
                catch (Exception e)
                {
                    return Result.Fail(e.Message);
                }
            }).ConfigureAwait(false);
        }

        private static Result TestRelay(ProxyConfig proxy, string targetHost, int targetPort, int timeoutMs, Stopwatch stopwatch)
        {
            using var client = new TcpClient { NoDelay = true };
            if (!client.ConnectAsync(proxy.Address, proxy.Port).Wait(timeoutMs))
                return Result.Fail($"cannot reach the proxy at {proxy.Address}:{proxy.Port} within {timeoutMs}ms");

            var stream = client.GetStream();
            stream.ReadTimeout = timeoutMs;
            stream.WriteTimeout = timeoutMs;
            ProxyHandshake.Perform(stream, proxy.Type, targetHost, targetPort, proxy.UserName, proxy.Password);

            stopwatch.Stop();
            return Result.Ok(stopwatch.ElapsedMilliseconds);
        }

        /// <summary>
        /// Builds a throwaway jump tunnel and pushes a connection through it. Testing with the very code a
        /// session will use means a passing test actually predicts a working session — an authentication
        /// check on its own would report success for a jump host that cannot see the target at all.
        /// </summary>
        private static Result TestSshJump(ProxyConfig jump, string targetHost, int targetPort, int timeoutMs, Stopwatch stopwatch)
        {
            // Port 0: a test must never take the deterministic port a real session may already be holding.
            using var tunnel = SshJumpTunnel.Start(jump, targetHost, targetPort, 0);

            using var probe = new TcpClient { NoDelay = true };
            if (!probe.ConnectAsync(ProxyTunnel.LOCAL_HOST, tunnel.LocalPort).Wait(timeoutMs))
                return Result.Fail($"the local end of the tunnel did not accept a connection within {timeoutMs}ms");

            // Connecting to the loopback listener proves nothing by itself: SSH.NET accepts first and only
            // then asks the jump host for a channel to the target. A refusal comes back as that local socket
            // closing, which a read reports as zero bytes. A target that is up either greets us (SSH, SMTP)
            // or stays quiet until spoken to (RDP, VNC) — so both data and a timeout mean success.
            var stream = probe.GetStream();
            stream.ReadTimeout = Math.Min(SSH_PROBE_QUIET_MS, timeoutMs);
            try
            {
                if (stream.Read(new byte[1], 0, 1) == 0)
                    return Result.Fail($"the jump host could not open a connection to {targetHost}:{targetPort}");
            }
            catch (IOException e) when (e.InnerException is SocketException { SocketErrorCode: SocketError.TimedOut })
            {
                // quiet and still open, which is the normal shape of a healthy tunnel
            }

            stopwatch.Stop();
            return Result.Ok(stopwatch.ElapsedMilliseconds);
        }
    }
}
