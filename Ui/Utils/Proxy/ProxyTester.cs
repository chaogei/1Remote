using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace _1RM.Utils.Proxy
{
    /// <summary>
    /// Verifies a proxy end to end by asking it to reach a target, then dropping the connection. Used by the
    /// settings page so a misconfigured proxy is caught there instead of surfacing as a failed session.
    /// </summary>
    public static class ProxyTester
    {
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
            if (!proxy.IsUsable) return Result.Fail("the proxy address, port or type is incomplete");
            if (string.IsNullOrWhiteSpace(targetHost)) return Result.Fail("the test target is empty");
            if (targetPort <= 0 || targetPort > 65535) return Result.Fail("the test target port is out of range");

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            return await Task.Run(() =>
            {
                using var client = new TcpClient { NoDelay = true };
                try
                {
                    if (!client.ConnectAsync(proxy.Address, proxy.Port).Wait(timeoutMs))
                        return Result.Fail($"cannot reach the proxy at {proxy.Address}:{proxy.Port} within {timeoutMs}ms");

                    var stream = client.GetStream();
                    stream.ReadTimeout = timeoutMs;
                    stream.WriteTimeout = timeoutMs;
                    ProxyHandshake.Perform(stream, proxy.Type, targetHost.Trim(), targetPort, proxy.UserName, proxy.GetPlainPassword());

                    stopwatch.Stop();
                    return Result.Ok(stopwatch.ElapsedMilliseconds);
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
    }
}
