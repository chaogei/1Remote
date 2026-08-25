using System.Linq;
using _1RM.Utils.Proxy;
using _1RM.Utils.SshConfig;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.Utils.SshConfig
{
    [TestClass]
    public class SshConfigParserTests
    {
        [TestInitialize]
        public void Setup() => TestInit.Init();

        private static string[] Lines(params string[] lines) => lines;

        [TestMethod]
        public void ABlockIsReadIntoAConnectableEntry()
        {
            var entries = SshConfigParser.Parse(Lines(
                "Host web",
                "    HostName 10.0.0.5",
                "    User deploy",
                "    Port 2222"));

            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual("web", entries[0].Alias);
            Assert.AreEqual("10.0.0.5", entries[0].HostName);
            Assert.AreEqual("deploy", entries[0].User);
            Assert.AreEqual(2222, entries[0].Port);
        }

        [TestMethod]
        public void AnAliasWithNoHostNameConnectsToItself()
        {
            var entries = SshConfigParser.Parse(Lines("Host build.example.com"));

            Assert.AreEqual("build.example.com", entries[0].HostName);
            Assert.AreEqual(22, entries[0].Port, "the ssh default applies when no port is given");
        }

        [TestMethod]
        public void WildcardBlocksAreDefaultsRatherThanMachines()
        {
            var entries = SshConfigParser.Parse(Lines(
                "Host *",
                "    User someone",
                "Host web?",
                "    HostName 10.0.0.5",
                "Host real",
                "    HostName 10.0.0.9"));

            Assert.AreEqual(1, entries.Count, "only the block naming an actual host survives");
            Assert.AreEqual("real", entries[0].Alias);
        }

        [TestMethod]
        public void OneHostLineCanNameSeveralAliasesAndAllOfThemGetTheSettings()
        {
            var entries = SshConfigParser.Parse(Lines(
                "Host a b",
                "    User shared",
                "    Port 2200"));

            Assert.AreEqual(2, entries.Count);
            CollectionAssert.AreEquivalent(new[] { "a", "b" }, entries.Select(x => x.Alias).ToList());
            Assert.IsTrue(entries.All(x => x.User == "shared" && x.Port == 2200));
        }

        [TestMethod]
        public void TheFirstValueWinsAsItDoesInSsh()
        {
            var entries = SshConfigParser.Parse(Lines(
                "Host web",
                "    Port 2222",
                "    Port 9999"));

            Assert.AreEqual(2222, entries[0].Port);
        }

        [TestMethod]
        public void CommentsBlankLinesAndEqualsSeparatorsAreAllHandled()
        {
            var entries = SshConfigParser.Parse(Lines(
                "# a comment",
                "",
                "Host web",
                "   # indented comment",
                "   HostName=10.0.0.5",
                "   Port = 2222"));

            Assert.AreEqual("10.0.0.5", entries[0].HostName);
            Assert.AreEqual(2222, entries[0].Port);
        }

        [TestMethod]
        public void MatchSectionsAreSkippedBecauseTheirConditionsCannotBeEvaluatedHere()
        {
            var entries = SshConfigParser.Parse(Lines(
                "Host web",
                "    HostName 10.0.0.5",
                "Match host *.internal",
                "    User ghost",
                "    Port 1234"));

            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual("", entries[0].User, "the Match block must not leak onto the previous host");
            Assert.AreEqual(22, entries[0].Port);
        }

        [TestMethod]
        public void ASingleProxyJumpHopIsCaptured()
        {
            var entries = SshConfigParser.Parse(Lines(
                "Host internal",
                "    HostName 10.0.0.5",
                "    ProxyJump bastion"));

            Assert.AreEqual("bastion", entries[0].ProxyJump);
        }

        [TestMethod]
        public void AProxyJumpHopKeepsOnlyTheAliasThatNamesTheBlock()
        {
            var entries = SshConfigParser.Parse(Lines(
                "Host internal",
                "    ProxyJump ops@bastion:2222"));

            Assert.AreEqual("bastion", entries[0].ProxyJump);
        }

        [TestMethod]
        public void AChainOfJumpsIsLeftAloneBecauseOnlyOneHopCanBeRepresented()
        {
            var entries = SshConfigParser.Parse(Lines(
                "Host internal",
                "    ProxyJump first,second"));

            Assert.AreEqual("", entries[0].ProxyJump);
        }

        [TestMethod]
        public void ProxyJumpNoneIsNotAHost()
        {
            var entries = SshConfigParser.Parse(Lines(
                "Host internal",
                "    ProxyJump none"));

            Assert.AreEqual("", entries[0].ProxyJump);
        }

        [TestMethod]
        public void ImportingBuildsServersAndTheJumpHostTheyNeed()
        {
            var entries = SshConfigParser.Parse(Lines(
                "Host bastion",
                "    HostName jump.example.com",
                "    User ops",
                "    Port 2222",
                "Host internal",
                "    HostName 10.0.0.5",
                "    User deploy",
                "    ProxyJump bastion"));

            var result = SshConfigImporter.Build(entries);

            Assert.AreEqual(2, result.Servers.Count);
            Assert.AreEqual(1, result.CreatedProxies.Count);

            var proxy = result.CreatedProxies[0];
            Assert.AreEqual(EProxyType.SshJump, proxy.Type);
            Assert.AreEqual("jump.example.com", proxy.Address);
            Assert.AreEqual(2222, proxy.Port);
            Assert.AreEqual("ops", proxy.UserName);

            var internalServer = result.Servers.Single(x => x.DisplayName == "internal");
            Assert.AreEqual(proxy.Name, internalServer.ProxyName, "the server should route through it");
        }

        [TestMethod]
        public void ReimportingReusesAJumpHostAlreadyOnThePage()
        {
            var entries = SshConfigParser.Parse(Lines(
                "Host bastion",
                "    HostName jump.example.com",
                "Host internal",
                "    HostName 10.0.0.5",
                "    ProxyJump bastion"));

            var already = new ProxyConfig
            {
                Name = SshConfigImporter.PROXY_NAME_PREFIX + "bastion",
                Type = EProxyType.SshJump,
                Address = "jump.example.com",
                UserName = "ops",
            };

            var result = SshConfigImporter.Build(entries, existingProxies: new[] { already });

            Assert.AreEqual(0, result.CreatedProxies.Count, "importing twice should not pile up duplicates");
            Assert.AreEqual(already.Name, result.Servers.Single(x => x.DisplayName == "internal").ProxyName);
        }

        [TestMethod]
        public void TheAliasBecomesTheDisplayNameAndTheHostNameTheAddress()
        {
            var entries = SshConfigParser.Parse(Lines(
                "Host prod-db",
                "    HostName 10.0.0.7"));

            var server = SshConfigImporter.Build(entries).Servers.Single();

            Assert.AreEqual("prod-db", server.DisplayName, "the alias is what the user recognises");
            Assert.AreEqual("10.0.0.7", ((_1RM.Model.Protocol.SSH)server).Address);
        }
    }
}
