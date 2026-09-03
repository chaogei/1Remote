using System;
using System.IO;
using _1RM.Service;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.Service
{
    /// <summary>
    /// The trust store decides whether a session opens, so the cases that matter are the ones where it must
    /// not ask (a dialog nobody sees blocks the session behind it) and the one where it must (an identity
    /// that changed under the user).
    /// </summary>
    [TestClass]
    public class HostTrustServiceTests
    {
        private string _dir = "";
        private AppPathHelper _originalPaths = AppPathHelper.Instance;

        [TestInitialize]
        public void SetUp()
        {
            _originalPaths = AppPathHelper.Instance;
            _dir = Path.Combine(Path.GetTempPath(), "1rm-trust-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            AppPathHelper.Instance = new AppPathHelper(_dir, _dir);
        }

        [TestCleanup]
        public void TearDown()
        {
            AppPathHelper.Instance = _originalPaths;
            try
            {
                Directory.Delete(_dir, true);
            }
            catch (Exception)
            {
                // a leftover temp directory is not worth failing a test over
            }
        }

        [TestMethod]
        public void AFirstSightingIsRememberedWithoutAsking()
        {
            var asked = 0;
            var service = new HostTrustService { Confirm = (_, _) => { asked++; return true; } };

            Assert.IsTrue(service.VerifyOrAsk("rdp", "host.example", 3390, "SHA256:aaa", trustOnFirstUse: true));
            Assert.AreEqual(0, asked, "trust on first use must not put a dialog in front of the session");

            // and it is remembered, so the next connect is silent for the right reason
            Assert.IsTrue(service.VerifyOrAsk("rdp", "host.example", 3390, "SHA256:aaa", trustOnFirstUse: true));
            Assert.AreEqual(0, asked);
        }

        [TestMethod]
        public void TheSameHostOnAnotherPortIsItsOwnIdentity()
        {
            // The whole point of the app's own store: Windows keys an accepted RDP certificate on the
            // address alone, so one hostname forwarding a port per machine has them overwrite each other.
            var service = new HostTrustService { Confirm = (_, _) => true };
            service.VerifyOrAsk("rdp", "frp.example", 6001, "SHA256:aaa", trustOnFirstUse: true);

            var asked = 0;
            service.Confirm = (_, _) => { asked++; return true; };
            Assert.IsTrue(service.VerifyOrAsk("rdp", "frp.example", 6002, "SHA256:bbb", trustOnFirstUse: true));
            Assert.AreEqual(0, asked, "a different port is a first sighting of its own, not a changed identity");

            // neither entry disturbed the other
            asked = 0;
            Assert.IsTrue(service.VerifyOrAsk("rdp", "frp.example", 6001, "SHA256:aaa", trustOnFirstUse: true));
            Assert.AreEqual(0, asked);
        }

        [TestMethod]
        public void AChangedIdentityIsStillEscalatedToTheUser()
        {
            var service = new HostTrustService { Confirm = (_, _) => true };
            service.VerifyOrAsk("rdp", "host.example", 3389, "SHA256:aaa", trustOnFirstUse: true);

            var asked = 0;
            service.Confirm = (_, _) => { asked++; return true; };
            Assert.IsTrue(service.VerifyOrAsk("rdp", "host.example", 3389, "SHA256:bbb", trustOnFirstUse: true));
            Assert.AreEqual(1, asked, "trust on first use covers the first sighting only");
        }

        [TestMethod]
        public void DecliningAChangedIdentityRefusesTheSessionAndKeepsTheOldFingerprint()
        {
            var service = new HostTrustService { Confirm = (_, _) => true };
            service.VerifyOrAsk("ssh", "host.example", 22, "SHA256:aaa", trustOnFirstUse: true);

            service.Confirm = (_, _) => false;
            Assert.IsFalse(service.VerifyOrAsk("ssh", "host.example", 22, "SHA256:bbb"));

            // the refusal must not have quietly replaced what was known good
            var asked = 0;
            service.Confirm = (_, _) => { asked++; return true; };
            Assert.IsTrue(service.VerifyOrAsk("ssh", "host.example", 22, "SHA256:aaa"));
            Assert.AreEqual(0, asked);
        }

        [TestMethod]
        public void ACallerCanAskThroughItsOwnDialog()
        {
            // The RDP host asks on the session window, because the shared prompt is modal to a main window
            // that is usually hidden behind the tray icon.
            var shared = 0;
            var mine = 0;
            var service = new HostTrustService { Confirm = (_, _) => { shared++; return true; } };

            Assert.IsTrue(service.VerifyOrAsk("rdp", "host.example", 3389, "SHA256:aaa",
                confirm: (_, _) => { mine++; return true; }));

            Assert.AreEqual(1, mine);
            Assert.AreEqual(0, shared);
        }

        [TestMethod]
        public void AnUnknownIdentityStillAsksWhenTrustOnFirstUseIsOff()
        {
            var asked = 0;
            var service = new HostTrustService { Confirm = (_, _) => { asked++; return false; } };

            Assert.IsFalse(service.VerifyOrAsk("tls", "host.example", 990, "SHA256:aaa"));
            Assert.AreEqual(1, asked, "SFTP and FTPS keep asking on first use");
        }
    }
}
