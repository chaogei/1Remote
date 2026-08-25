using _1RM.Utils.ExternalSecret;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.Utils.ExternalSecret
{
    /// <summary>
    /// The resolver shells out for real here, using cmd.exe built-ins as the stand-in password manager.
    /// What matters is the behaviour around the command — trimming, failure handling, caching — and a fake
    /// process would test none of it.
    /// </summary>
    [TestClass]
    public class ExternalSecretResolverTests
    {
        [TestInitialize]
        public void Setup()
        {
            TestInit.Init();
            ExternalSecretResolver.ClearCache();
        }

        [DataTestMethod]
        [DataRow("cmd://bw get password x", true)]
        [DataRow("CMD://bw get password x", true)]
        [DataRow("hunter2", false)]
        [DataRow("", false)]
        [DataRow(null, false)]
        public void OnlyAPrefixedValueIsTreatedAsACommand(string? value, bool expected)
        {
            Assert.AreEqual(expected, ExternalSecretResolver.IsReference(value));
        }

        [TestMethod]
        public void APlainPasswordPassesThroughUntouched()
        {
            Assert.AreEqual("hunter2", ExternalSecretResolver.Resolve("hunter2"));
        }

        [TestMethod]
        public void TheCommandIsRunAndItsOutputBecomesTheSecret()
        {
            var secret = ExternalSecretResolver.Resolve("cmd://echo hunter2");

            Assert.AreEqual("hunter2", secret);
        }

        [TestMethod]
        public void TheTrailingNewlineACliPrintsIsStripped()
        {
            // Sending that newline as part of a password submits the login early or fails the handshake.
            var secret = ExternalSecretResolver.Resolve("cmd://echo hunter2");

            Assert.IsFalse(secret.EndsWith("\n"));
            Assert.IsFalse(secret.EndsWith("\r"));
        }

        [TestMethod]
        public void AFailingCommandResolvesToNothingRatherThanThrowing()
        {
            // This runs on the connect path for every protocol; an exception here would take down the
            // session rather than surface as a rejected login.
            var secret = ExternalSecretResolver.Resolve("cmd://exit 1");

            Assert.AreEqual("", secret);
        }

        [TestMethod]
        public void AnEmptyCommandResolvesToNothing()
        {
            Assert.AreEqual("", ExternalSecretResolver.Resolve("cmd://   "));
        }

        [TestMethod]
        public void TestReportsTheLengthWithoutRevealingTheSecret()
        {
            var (ok, message, length) = ExternalSecretResolver.Test("cmd://echo hunter2");

            Assert.IsTrue(ok, message);
            Assert.AreEqual(7, length);
        }

        [TestMethod]
        public void TestExplainsWhyACommandFailed()
        {
            var (ok, message, _) = ExternalSecretResolver.Test("cmd://exit 3");

            Assert.IsFalse(ok);
            Assert.AreNotEqual("", message);
        }

        [TestMethod]
        public void TestRejectsSomethingThatIsNotAReference()
        {
            var (ok, _, _) = ExternalSecretResolver.Test("hunter2");

            Assert.IsFalse(ok);
        }

        [TestMethod]
        public void TestSaysSoWhenTheCommandPrintsNothing()
        {
            var (ok, message, _) = ExternalSecretResolver.Test("cmd://rem nothing");

            Assert.IsFalse(ok);
            Assert.AreNotEqual("", message);
        }

        [TestMethod]
        public void ASecretIsFetchedOnceAndReusedForTheRestOfTheRun()
        {
            // A vault that prompts for a fingerprint would otherwise ask once per field per connection.
            var marker = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"1rm-secret-{System.Guid.NewGuid():N}.txt");
            var command = $"cmd://echo first> \"{marker}\" & echo secret";

            try
            {
                Assert.AreEqual("secret", ExternalSecretResolver.Resolve(command));
                System.IO.File.Delete(marker);

                Assert.AreEqual("secret", ExternalSecretResolver.Resolve(command));
                Assert.IsFalse(System.IO.File.Exists(marker), "the command should not have run a second time");
            }
            finally
            {
                if (System.IO.File.Exists(marker)) System.IO.File.Delete(marker);
            }
        }

        [TestMethod]
        public void ClearingTheCacheMakesTheVaultBeConsultedAgain()
        {
            var command = "cmd://echo secret";
            Assert.AreEqual("secret", ExternalSecretResolver.Resolve(command));

            ExternalSecretResolver.ClearCache();

            Assert.AreEqual("secret", ExternalSecretResolver.Resolve(command), "still resolvable after a clear");
        }
    }
}
