using _1RM.Utils.Rdp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.Utils.Rdp
{
    /// <summary>
    /// After a remote reboot, 3389/CredSSP is often not ready on the first try. mstsc waits; 1Remote used
    /// to give up unless a previous session had already logged on. These cases pin which disconnect codes
    /// are allowed to retry on that first connect, and which must not loop (wrong password).
    /// </summary>
    [TestClass]
    public class RdpDisconnectClassifierTests
    {
        private const int Max = 5;

        [TestMethod]
        public void AFirstConnectRetriesWhenThePortIsNotListeningYet()
        {
            Assert.IsTrue(RdpDisconnectClassifier.ShouldAutoRetry(
                RdpDisconnectClassifier.SocketConnectFailed, hasEverConnected: false, retryCount: 0, maxRetryCount: Max));
            Assert.IsTrue(RdpDisconnectClassifier.ShouldAutoRetry(
                RdpDisconnectClassifier.ConnectionTimedOut, hasEverConnected: false, retryCount: 0, maxRetryCount: Max));
            Assert.IsTrue(RdpDisconnectClassifier.ShouldAutoRetry(
                RdpDisconnectClassifier.WinsockFdClose, hasEverConnected: false, retryCount: 0, maxRetryCount: Max));
        }

        [TestMethod]
        public void AFirstConnectRetriesTheGenericNetworkDropThatFollowsAReboot()
        {
            Assert.IsTrue(RdpDisconnectClassifier.ShouldAutoRetry(
                RdpDisconnectClassifier.NoInfo, hasEverConnected: false, retryCount: 0, maxRetryCount: Max));
            Assert.IsTrue(RdpDisconnectClassifier.ShouldAutoRetry(
                RdpDisconnectClassifier.NetworkDrop, hasEverConnected: false, retryCount: 0, maxRetryCount: Max));
        }

        [TestMethod]
        public void AWrongPasswordOnFirstConnectDoesNotRetry()
        {
            Assert.IsFalse(RdpDisconnectClassifier.ShouldAutoRetry(
                RdpDisconnectClassifier.SslLogonDenied, hasEverConnected: false, retryCount: 0, maxRetryCount: Max));
            Assert.IsFalse(RdpDisconnectClassifier.ShouldAutoRetry(
                RdpDisconnectClassifier.SslNoSuchUser, hasEverConnected: false, retryCount: 0, maxRetryCount: Max));
        }

        [TestMethod]
        public void ADropAfterASuccessfulSessionStillRetries()
        {
            Assert.IsTrue(RdpDisconnectClassifier.ShouldAutoRetry(
                RdpDisconnectClassifier.SocketRecvFailed, hasEverConnected: true, retryCount: 0, maxRetryCount: Max));
            Assert.IsTrue(RdpDisconnectClassifier.ShouldAutoRetry(
                RdpDisconnectClassifier.LocalNotError, hasEverConnected: true, retryCount: 0, maxRetryCount: Max));
        }

        [TestMethod]
        public void TheRetryBudgetStopsTheLoop()
        {
            Assert.IsFalse(RdpDisconnectClassifier.ShouldAutoRetry(
                RdpDisconnectClassifier.SocketConnectFailed, hasEverConnected: false, retryCount: Max, maxRetryCount: Max));
        }

        [TestMethod]
        public void BackoffDoublesThenCaps()
        {
            Assert.AreEqual(1000, RdpDisconnectClassifier.RetryDelayMs(1));
            Assert.AreEqual(2000, RdpDisconnectClassifier.RetryDelayMs(2));
            Assert.AreEqual(4000, RdpDisconnectClassifier.RetryDelayMs(3));
            Assert.AreEqual(8000, RdpDisconnectClassifier.RetryDelayMs(4));
            Assert.AreEqual(8000, RdpDisconnectClassifier.RetryDelayMs(9));
        }
    }
}
