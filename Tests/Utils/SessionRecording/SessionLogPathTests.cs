using System;
using System.IO;
using System.Linq;
using _1RM.Utils.SessionRecording;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.Utils.SessionRecording
{
    [TestClass]
    public class SessionLogPathTests
    {
        private static readonly DateTime When = new DateTime(2026, 8, 25, 16, 7, 3, 456);

        [TestMethod]
        public void ThePathCombinesTheFolderTheNameAndAStamp()
        {
            var path = SessionLogPath.Build(@"C:\logs", "web-01", When);

            Assert.AreEqual(@"C:\logs\web-01_20260825_160703_456.log", path);
        }

        [TestMethod]
        public void TheStampCarriesMillisecondsSoTwoTabsAtOnceDoNotCollide()
        {
            // PuTTY stops to ask whether to overwrite an existing log, which would block the second tab
            // behind a dialog nobody expected.
            var first = SessionLogPath.Build(@"C:\logs", "web", When);
            var second = SessionLogPath.Build(@"C:\logs", "web", When.AddMilliseconds(1));

            Assert.AreNotEqual(first, second);
        }

        [DataTestMethod]
        [DataRow("web/01", "web_01")]
        [DataRow("web:01", "web_01")]
        [DataRow("a b", "a_b")]
        [DataRow("  padded  ", "padded")]
        public void CharactersAFileNameCannotHoldAreReplaced(string input, string expected)
        {
            Assert.AreEqual(expected, SessionLogPath.Sanitize(input));
        }

        [TestMethod]
        public void SanitizedNamesNeverContainAnInvalidCharacter()
        {
            var nasty = new string(Path.GetInvalidFileNameChars()) + "ok";

            var cleaned = SessionLogPath.Sanitize(nasty);

            Assert.IsFalse(cleaned.Any(c => Path.GetInvalidFileNameChars().Contains(c)));
        }

        [TestMethod]
        public void AVeryLongNameIsClipped()
        {
            var cleaned = SessionLogPath.Sanitize(new string('x', 300));

            Assert.IsTrue(cleaned.Length <= 48, $"was {cleaned.Length}");
        }

        [TestMethod]
        public void AnEmptyNameStillProducesAUsablePath()
        {
            var path = SessionLogPath.Build(@"C:\logs", "   ", When);

            StringAssert.Contains(path, "session_");
        }
    }
}
