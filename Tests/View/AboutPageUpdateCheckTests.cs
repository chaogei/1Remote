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
    ///
    /// The other trap is ordering: GitHub sorts the list by tag name as a string, so the first link on the
    /// page is not the newest build once the build number reaches two digits.
    /// </summary>
    [TestClass]
    public class AboutPageUpdateCheckTests
    {
        /// <summary>
        /// The check method is private and the view model cannot be constructed without the whole IoC graph,
        /// but the method is static, so reflection reaches it on its own.
        /// </summary>
        private static VersionHelper.CheckUpdateResult Check(string html, VersionHelper.Version current, VersionHelper.Version? ignore = null)
        {
            var method = typeof(_1RM.View.AboutPageViewModel)
                .GetMethod("CustomCheckMethod", BindingFlags.NonPublic | BindingFlags.Static);
            if (method == null)
                throw new InvalidOperationException("CustomCheckMethod not found on AboutPageViewModel");

            return (VersionHelper.CheckUpdateResult)method.Invoke(null, new object?[] { html, "https://github.com/chaogei/1Remote-Plus/releases", current, ignore })!;
        }

        /// <summary>
        /// Shaped like the real page: one tag link per release, listed in the order given.
        /// The order is deliberately a parameter — see <see cref="GitHubOrder"/>, GitHub does not
        /// hand the releases out newest first.
        /// </summary>
        private static string ReleasesPage(params string[] tags)
        {
            var html = "<html><body>";
            foreach (var tag in tags)
                html += $"<a href=\"/chaogei/1remote-plus/releases/tag/v{tag}\">release v{tag}</a>";
            return html + "</body></html>";
        }

        /// <summary>
        /// The order github.com/chaogei/1Remote-Plus/releases actually serves, verified against the live page.
        /// Both the HTML index and GET /repos/:owner/:repo/releases sort by tag name as a string rather
        /// than by date, so after the shared "v1.3.0." prefix '9' beats '2' beats '1', which drops the
        /// newest build — 1.3.0.10-beta — down to second-from-last.
        /// </summary>
        private static readonly string[] GitHubOrder =
        {
            "1.3.0.9-beta", "1.3.0.8-beta", "1.3.0.7-beta", "1.3.0.6-beta", "1.3.0.5-beta",
            "1.3.0.4-beta", "1.3.0.3-beta", "1.3.0.2-beta", "1.3.0.10-beta", "1.3.0.1-beta",
        };

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

        [TestMethod]
        public void TheHighestBuildIsFoundEvenThoughGitHubListsItNearTheBottom()
        {
            var current = VersionHelper.Version.FromString("1.3.0.9-beta");

            var result = Check(ReleasesPage(GitHubOrder), current);

            Assert.IsTrue(result.NewerPublished, "1.3.0.10-beta is newer than the build we are running");
            Assert.AreEqual("1.3.0.10-beta", result.NewerVersion);
        }

        [TestMethod]
        public void AnOlderBuildIsOfferedTheHighestReleaseNotTheFirstOneListed()
        {
            var current = VersionHelper.Version.FromString("1.3.0.8-beta");

            var result = Check(ReleasesPage(GitHubOrder), current);

            Assert.IsTrue(result.NewerPublished);
            Assert.AreEqual("1.3.0.10-beta", result.NewerVersion, "1.3.0.9-beta merely sorts first");
        }

        [TestMethod]
        public void TheHighestBuildIsNotNaggedByTheTagsThatSortAboveIt()
        {
            var current = VersionHelper.Version.FromString("1.3.0.10-beta");

            var result = Check(ReleasesPage(GitHubOrder), current);

            Assert.IsFalse(result.NewerPublished, "1.3.0.9-beta heads the list but is an older build");
        }

        [TestMethod]
        public void IgnoringOneBuildDoesNotSuppressTheBuildAboveIt()
        {
            var current = VersionHelper.Version.FromString("1.3.0.8-beta");
            var ignore = VersionHelper.Version.FromString("1.3.0.9-beta");

            var result = Check(ReleasesPage(GitHubOrder), current, ignore);

            Assert.IsTrue(result.NewerPublished);
            Assert.AreEqual("1.3.0.10-beta", result.NewerVersion);
        }

        [TestMethod]
        public void IgnoringTheHighestBuildStillSuppressesTheNotice()
        {
            var current = VersionHelper.Version.FromString("1.3.0.8-beta");
            var ignore = VersionHelper.Version.FromString("1.3.0.10-beta");

            var result = Check(ReleasesPage(GitHubOrder), current, ignore);

            Assert.IsFalse(result.NewerPublished);
        }
    }
}
