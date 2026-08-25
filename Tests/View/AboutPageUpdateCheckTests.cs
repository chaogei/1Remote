using System;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shawn.Utils;

namespace Tests.View
{
    /// <summary>
    /// Covers how this fork reads its own releases page.
    ///
    /// The check scrapes HTML rather than calling an API, and the releases index carries only tag links —
    /// asset file names are loaded lazily and never appear in it. Getting the tag pattern subtly wrong is
    /// invisible in normal use and shows up as a permanent "an update is available" nag for the version the
    /// user already has, so the current-version case is the one worth pinning down.
    /// </summary>
    [TestClass]
    public class AboutPageUpdateCheckTests
    {
        /// <summary>
        /// The check method is private and the view model cannot be constructed without the whole IoC graph,
        /// but the method is static, so reflection reaches it on its own.
        /// </summary>
        private static VersionHelper.CheckUpdateResult Check(string html, VersionHelper.Version current)
        {
            var method = typeof(_1RM.View.AboutPageViewModel)
                .GetMethod("CustomCheckMethod", BindingFlags.NonPublic | BindingFlags.Static);
            if (method == null)
                throw new InvalidOperationException("CustomCheckMethod not found on AboutPageViewModel");

            return (VersionHelper.CheckUpdateResult)method.Invoke(null, new object?[] { html, "https://github.com/chaogei/1Remote/releases", current, null })!;
        }

        /// <summary>Shaped like the real page: newest release first, each linking to its tag.</summary>
        private static string ReleasesPage(params string[] tags)
        {
            var html = "<html><body>";
            foreach (var tag in tags)
                html += $"<a href=\"/chaogei/1remote/releases/tag/v{tag}\">release v{tag}</a>";
            return html + "</body></html>";
        }

        [TestMethod]
        public void ANewerTagIsReportedAsAnUpdate()
        {
            var current = VersionHelper.Version.FromString("1.3.0.3-beta");

            var result = Check(ReleasesPage("1.3.0.4-beta", "1.3.0.3-beta"), current);

            Assert.IsTrue(result.NewerPublished);
            Assert.AreEqual("1.3.0.4-beta", result.NewerVersion);
        }

        [TestMethod]
        public void TheVersionAlreadyRunningIsNotReportedAsAnUpdate()
        {
            var current = VersionHelper.Version.FromString("1.3.0.3-beta");

            var result = Check(ReleasesPage("1.3.0.3-beta", "1.3.0.2-beta"), current);

            Assert.IsFalse(result.NewerPublished, "the newest tag is the build we are running");
        }

        [TestMethod]
        public void AStableTagOutranksThePreReleaseOfTheSameBuild()
        {
            var current = VersionHelper.Version.FromString("1.3.0.3-beta");

            var result = Check(ReleasesPage("1.3.0.3"), current);

            Assert.IsTrue(result.NewerPublished, "leaving beta is an update");
            Assert.AreEqual("1.3.0.3", result.NewerVersion);
        }

        [TestMethod]
        public void AssetFileNamesAreStillUnderstood()
        {
            // A single release page does list its assets, and they carry the version too.
            var current = VersionHelper.Version.FromString("1.3.0.3-beta");
            const string html = "<html><body>1remote-1.3.0.4-beta-net9-x64.zip</body></html>";

            var result = Check(html, current);

            Assert.IsTrue(result.NewerPublished);
        }

        [TestMethod]
        public void APageWithNoVersionAtAllIsNotAnUpdate()
        {
            var current = VersionHelper.Version.FromString("1.3.0.3-beta");

            var result = Check("<html><body>no releases yet</body></html>", current);

            Assert.IsFalse(result.NewerPublished);
        }
    }
}
