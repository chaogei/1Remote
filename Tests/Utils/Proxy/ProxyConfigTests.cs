using _1RM.Utils.Proxy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace Tests.Utils.Proxy
{
    [TestClass]
    public class ProxyConfigTests
    {
        [TestInitialize]
        public void Setup() => TestInit.Init();

        private static ProxyConfig NewJumpHost() => new ProxyConfig
        {
            Name = "jump",
            Type = EProxyType.SshJump,
            Address = "jump.example.com",
            UserName = "ops",
        };

        [TestMethod]
        public void SwitchingType_MovesAnUntouchedPortToTheNewDefault()
        {
            var proxy = new ProxyConfig();
            Assert.AreEqual(1080, proxy.Port, "a new entry starts as a SOCKS proxy");

            proxy.Type = EProxyType.SshJump;
            Assert.AreEqual(22, proxy.Port);

            proxy.Type = EProxyType.Http;
            Assert.AreEqual(1080, proxy.Port);
        }

        [TestMethod]
        public void SwitchingType_LeavesAPortTheUserChose()
        {
            var proxy = new ProxyConfig { Port = 2222 };

            proxy.Type = EProxyType.SshJump;

            Assert.AreEqual(2222, proxy.Port, "a port that is not the type default was typed on purpose");
        }

        [TestMethod]
        public void JumpHost_IsNotUsableWithoutAUserName()
        {
            var proxy = NewJumpHost();
            Assert.IsTrue(proxy.IsUsable);

            proxy.UserName = "";

            Assert.IsFalse(proxy.IsUsable, "there is nothing to authenticate as");
        }

        [TestMethod]
        public void RelayProxy_DoesNotNeedAUserName()
        {
            var proxy = new ProxyConfig { Type = EProxyType.Socks5, Address = "127.0.0.1", Port = 1080 };

            Assert.IsTrue(proxy.IsUsable, "an anonymous SOCKS proxy is perfectly normal");
        }

        [TestMethod]
        public void EndPointKey_SeparatesJumpHostsThatDifferOnlyByKey()
        {
            var a = NewJumpHost();
            var b = NewJumpHost();
            a.PrivateKeyPath = @"C:\keys\one";
            b.PrivateKeyPath = @"C:\keys\two";

            Assert.AreNotEqual(a.GetEndPointKey(), b.GetEndPointKey(),
                "two different logins must not be collapsed onto one pooled SSH session");
        }

        [TestMethod]
        public void EndPointKey_IgnoresTheKeyPathForRelayProxies()
        {
            var a = new ProxyConfig { Type = EProxyType.Socks5, Address = "127.0.0.1", PrivateKeyPath = @"C:\keys\one" };
            var b = new ProxyConfig { Type = EProxyType.Socks5, Address = "127.0.0.1" };

            Assert.AreEqual(a.GetEndPointKey(), b.GetEndPointKey(), "a SOCKS proxy never reads the key");
        }

        [TestMethod]
        public void CredentialKey_TracksEverySecretATunnelAuthenticatedWith()
        {
            var proxy = NewJumpHost();
            var original = proxy.GetCredentialKey();

            proxy.Password = "hunter2";
            var afterPassword = proxy.GetCredentialKey();
            Assert.AreNotEqual(original, afterPassword);

            proxy.PrivateKeyPath = @"C:\keys\id_ed25519";
            var afterKey = proxy.GetCredentialKey();
            Assert.AreNotEqual(afterPassword, afterKey);

            proxy.PrivateKeyPassphrase = "open sesame";
            Assert.AreNotEqual(afterKey, proxy.GetCredentialKey());
        }

        [TestMethod]
        public void Secrets_RoundTripAsPlainTextAndAreNotStoredThatWay()
        {
            var proxy = NewJumpHost();
            proxy.Password = "hunter2";
            proxy.PrivateKeyPassphrase = "open sesame";

            var json = JsonConvert.SerializeObject(proxy);
            StringAssert.Contains(json, "\"Password\"");
            Assert.IsFalse(json.Contains("hunter2"), "the profile must not hold the password in the clear");
            Assert.IsFalse(json.Contains("open sesame"), "nor the key passphrase");

            var restored = JsonConvert.DeserializeObject<ProxyConfig>(json)!;
            Assert.AreEqual("hunter2", restored.Password);
            Assert.AreEqual("open sesame", restored.PrivateKeyPassphrase);
        }

        [TestMethod]
        public void AnEmptySecret_StaysEmptyRatherThanBecomingCipherText()
        {
            // The bug this guards: an untouched password used to be enciphered anyway, so the editor showed
            // a block of gibberish where the user had deliberately left the field blank.
            var proxy = NewJumpHost();

            proxy.Password = "";
            proxy.PrivateKeyPassphrase = "";

            Assert.AreEqual("", proxy.Password);
            Assert.AreEqual("", proxy.PrivateKeyPassphrase);

            var restored = JsonConvert.DeserializeObject<ProxyConfig>(JsonConvert.SerializeObject(proxy))!;
            Assert.AreEqual("", restored.Password);
            Assert.AreEqual("", restored.PrivateKeyPassphrase);
        }

        [TestMethod]
        public void Summary_UsesTheSpellingShownInTheTypeDropdown()
        {
            var proxy = NewJumpHost();

            StringAssert.Contains(proxy.Summary, "SSH jump host");
            StringAssert.Contains(proxy.Summary, "jump.example.com:22");
        }

        [TestMethod]
        public void TypeNames_AreDefinedForEveryTypeOfferedInTheEditor()
        {
            foreach (EProxyType type in System.Enum.GetValues(typeof(EProxyType)))
            {
                if (type == EProxyType.None) continue;
                var name = ProxyTypeName.Of(type);
                Assert.AreNotEqual(type.ToString(), name, $"{type} still falls back to its enum name");
            }
        }
    }
}
