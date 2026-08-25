using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using _1RM.Model.Protocol;
using _1RM.Utils.Proxy;
using _1RM.Utils.Reachability;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.Utils.Reachability
{
    /// <summary>
    /// These drive the real probe against a real socket on loopback rather than a stub. The whole point of
    /// the probe is what a TCP connect does in practice — refused, accepted, timed out — and a fake would
    /// only assert that the mock was called.
    /// </summary>
    [TestClass]
    public class ServerProbeTests
    {
        private const int TIMEOUT_MS = 3000;

        [TestInitialize]
        public void Setup() => TestInit.Init();

        private static SSH ServerAt(string address, int port) => new SSH
        {
            Address = address,
            Port = port.ToString(),
        };

        /// <summary>Binds a loopback port and hands it back listening. Dispose stops it.</summary>
        private static TcpListener StartListener(out int port)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            port = ((IPEndPoint)listener.LocalEndpoint).Port;
            return listener;
        }

        [TestMethod]
        public async Task APortThatAcceptsIsReportedReachable()
        {
            var listener = StartListener(out var port);
            try
            {
                var result = await ServerProbe.ProbeAsync(ServerAt("127.0.0.1", port), null, TIMEOUT_MS, CancellationToken.None);

                Assert.AreEqual(EReachState.Online, result.State);
                Assert.IsTrue(result.LatencyMs >= 0);
            }
            finally
            {
                listener.Stop();
            }
        }

        [TestMethod]
        public async Task APortWithNothingBehindItIsReportedUnreachable()
        {
            // Bind then release, so the port is almost certainly free and refusing rather than firewalled.
            var listener = StartListener(out var port);
            listener.Stop();

            var result = await ServerProbe.ProbeAsync(ServerAt("127.0.0.1", port), null, TIMEOUT_MS, CancellationToken.None);

            Assert.AreEqual(EReachState.Offline, result.State);
        }

        [TestMethod]
        public async Task AServerWithNoAddressIsSkippedRatherThanCalledDown()
        {
            var result = await ServerProbe.ProbeAsync(ServerAt("", 22), null, TIMEOUT_MS, CancellationToken.None);

            Assert.AreEqual(EReachState.Skipped, result.State);
            Assert.AreNotEqual("", result.Reason, "a skip needs a reason to put in the tooltip");
        }

        [TestMethod]
        public async Task AServerWithAnUnusablePortIsSkipped()
        {
            var result = await ServerProbe.ProbeAsync(ServerAt("127.0.0.1", 0), null, TIMEOUT_MS, CancellationToken.None);

            Assert.AreEqual(EReachState.Skipped, result.State);
        }

        [TestMethod]
        public async Task AServerBehindAJumpHostIsSkippedRatherThanAuthenticatedEverySweep()
        {
            var jump = new ProxyConfig
            {
                Name = "bastion",
                Type = EProxyType.SshJump,
                Address = "jump.example.com",
                UserName = "ops",
            };

            var result = await ServerProbe.ProbeAsync(ServerAt("10.0.0.5", 22), jump, TIMEOUT_MS, CancellationToken.None);

            Assert.AreEqual(EReachState.Skipped, result.State);
            Assert.AreNotEqual("", result.Reason);
        }

        [TestMethod]
        public async Task ABypassedProxyIsNotConsultedForALocalTarget()
        {
            var listener = StartListener(out var port);
            try
            {
                // A proxy that could never be reached. If the bypass rule were not honoured the probe would
                // try to go through it and report the server down, even though it is right here.
                var proxy = new ProxyConfig
                {
                    Name = "dead",
                    Type = EProxyType.Socks5,
                    Address = "192.0.2.1", // TEST-NET-1, guaranteed unroutable
                    Port = 1080,
                    BypassForLocalAddress = true,
                };

                var result = await ServerProbe.ProbeAsync(ServerAt("127.0.0.1", port), proxy, TIMEOUT_MS, CancellationToken.None);

                Assert.AreEqual(EReachState.Online, result.State);
            }
            finally
            {
                listener.Stop();
            }
        }

        [TestMethod]
        public async Task AnUnreachableProxyMakesTheServerBehindItUnreachable()
        {
            var proxy = new ProxyConfig
            {
                Name = "dead",
                Type = EProxyType.Socks5,
                Address = "192.0.2.1",
                Port = 1080,
                BypassForLocalAddress = false,
            };

            // 1s so the test does not sit on an unroutable address for the full timeout
            var result = await ServerProbe.ProbeAsync(ServerAt("10.0.0.5", 22), proxy, 1000, CancellationToken.None);

            Assert.AreEqual(EReachState.Offline, result.State,
                "a proxy that cannot be reached means the session could not be made either");
        }

        [TestMethod]
        public async Task CancellingASweepStopsTheProbeRatherThanReportingDown()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // Cancellation has to travel out rather than be swallowed into "offline": switching the feature
            // off mid sweep must not leave a screen full of red dots behind.
            await Assert.ThrowsExceptionAsync<OperationCanceledException>(async () =>
                await ServerProbe.ProbeAsync(ServerAt("192.0.2.1", 22), null, TIMEOUT_MS, cts.Token));
        }
    }
}
