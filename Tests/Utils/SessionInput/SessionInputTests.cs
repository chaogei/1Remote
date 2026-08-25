using _1RM.Utils.SessionInput;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace Tests.Utils.SessionInput
{
    [TestClass]
    public class SessionInputTests
    {
        [TestInitialize]
        public void Setup() => TestInit.Init();

        [TestMethod]
        public void SendingToNothingIsRefusedRatherThanThrowing()
        {
            Assert.IsFalse(SessionTextSender.CanSendTo(null));
            Assert.IsFalse(SessionTextSender.Send(null, "whoami", true));
        }

        [DataTestMethod]
        [DataRow("a\r\nb", "a\rb")]
        [DataRow("a\nb", "a\rb")]
        [DataRow("a\rb", "a\rb")]
        [DataRow("a\r\nb\nc", "a\rb\rc")]
        public void EveryFlavourOfNewlineBecomesTheCarriageReturnATerminalReads(string input, string expected)
        {
            // A line feed on its own moves the cursor down without submitting anything, so text pasted from
            // an editor would sit there looking typed but never run.
            Assert.AreEqual(expected, SessionTextSender.NormalizeNewLines(input));
        }

        [TestMethod]
        public void NormalizingNothingGivesNothing()
        {
            Assert.AreEqual("", SessionTextSender.NormalizeNewLines(null));
            Assert.AreEqual("", SessionTextSender.NormalizeNewLines(""));
        }

        [TestMethod]
        public void TextWithoutLineBreaksIsLeftAlone()
        {
            Assert.AreEqual("systemctl status nginx", SessionTextSender.NormalizeNewLines("systemctl status nginx"));
        }

        [TestMethod]
        public void ASnippetIsListedByItsNameWhenItHasOne()
        {
            var snippet = new CommandSnippet { Name = "restart nginx", Content = "systemctl restart nginx" };

            Assert.AreEqual("restart nginx", snippet.DisplayName);
        }

        [TestMethod]
        public void AnUnnamedSnippetFallsBackToTheCommandItself()
        {
            var snippet = new CommandSnippet { Content = "df -h" };

            Assert.AreEqual("df -h", snippet.DisplayName, "an empty row would be useless in the list");
        }

        [TestMethod]
        public void ThePreviewIsTheFirstLineOnly()
        {
            var snippet = new CommandSnippet { Content = "cd /var/log\ntail -f syslog" };

            Assert.AreEqual("cd /var/log", snippet.Preview);
        }

        [TestMethod]
        public void ALongPreviewIsShortenedSoTheListStaysScannable()
        {
            var snippet = new CommandSnippet { Content = new string('x', 200) };

            Assert.AreEqual(60, snippet.Preview.Length);
            StringAssert.EndsWith(snippet.Preview, "...");
        }

        [TestMethod]
        public void ASnippetSurvivesARoundTripThroughTheProfile()
        {
            var snippet = new CommandSnippet
            {
                Name = "tail syslog",
                Content = "tail -f /var/log/syslog",
                AppendEnter = false,
            };

            var restored = JsonConvert.DeserializeObject<CommandSnippet>(JsonConvert.SerializeObject(snippet))!;

            Assert.AreEqual("tail syslog", restored.Name);
            Assert.AreEqual("tail -f /var/log/syslog", restored.Content);
            Assert.IsFalse(restored.AppendEnter);
        }

        [TestMethod]
        public void AnOverlongNameIsTrimmedRatherThanBreakingTheList()
        {
            var snippet = new CommandSnippet { Name = new string('n', 200) };

            Assert.AreEqual(64, snippet.Name.Length);
        }
    }
}
