using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shawn.Utils;
using static Shawn.Utils.VersionHelper;

namespace Tests.Utils
{
    [TestClass()]
    public class VersionHelperTests
    {

        [TestMethod()]
        public void FromStringTest()
        {
            var v1 = new Version(0, 6, 1, 0);
            var v2 = Version.FromString(v1.ToString());
            Assert.IsTrue(v1 == v2);
        }

        [TestMethod()]
        public void CompareTest()
        {
            var v1 = new Version(0, 6, 1, 0);
            var v2 = new Version(0, 6, 1, 0);
            var v3 = new Version(0, 6, 1, 1);
            var v4 = new Version(0, 6, 2, 0);
            var v5 = new Version(0, 7, 1, 0);
            var v6 = new Version(1, 6, 1, 0);
            var v7 = new Version(0, 6, 1, 0, "alpha");
            var v8 = new Version(0, 6, 1, 0, "beta");
            var v9 = new Version(0, 6, 1, 0, "beta2");
            Assert.IsTrue(v1 == v2);
            Assert.IsTrue(v1 >= v2);
            Assert.IsTrue(v3 > v2);
            Assert.IsTrue(v3 != v2);
            Assert.IsTrue(v2 < v3);
            Assert.IsTrue(v3 >= v2);
            Assert.IsTrue(v4 > v3);
            Assert.IsTrue(v3 < v4);
            Assert.IsTrue(v3 <= v4);
            Assert.IsTrue(v5 > v4);
            Assert.IsTrue(v6 > v5);
            Assert.IsTrue(v6 > v7);
            Assert.IsTrue(v8 > v7);
            Assert.IsTrue(v9 > v8);
            Assert.IsTrue(v1 > v9);
            Assert.IsTrue(v9 != v8);
            Assert.IsTrue(Shawn.Utils.VersionHelper.Version.Compare(v1, v3) == true);
            Assert.IsTrue(Shawn.Utils.VersionHelper.Version.Compare(v9, v1) == true);
        }


        /// <summary>
        /// The scenarios the old test covered, moved onto <see cref="VersionHelper.DefaultCheckMethod"/> —
        /// the fetch and the parse used to sit in one method that took the page content, and only the parse
        /// half can still be exercised without reaching the network.
        /// </summary>
        [TestMethod()]
        public void DefaultCheckMethodTest()
        {
            var current = new Version(0, 6, 1, 0);
            var newer = new Version(0, 6, 2, 0);
            var newest = new Version(0, 7, 1, 0);
            const string publishUrl = "www.xxxx.xx";

            {
                var result = DefaultCheckMethod($"latest version: {newer}", publishUrl, current, null);
                Assert.IsTrue(result.NewerPublished);
                Assert.IsTrue(Version.FromString(result.NewerVersion) == newer);
                Assert.AreEqual(publishUrl, result.NewerUrl);
            }
            {
                // ignoring a version above the published one suppresses the notice
                var result = DefaultCheckMethod($"latest version: {newer}", publishUrl, current, newest);
                Assert.IsFalse(result.NewerPublished);
            }
            {
                // ignoring exactly the published version suppresses it too
                var result = DefaultCheckMethod($"latest version: {newer}", publishUrl, current, newer);
                Assert.IsFalse(result.NewerPublished);
            }
            {
                // but a version above the ignored one still gets through
                var result = DefaultCheckMethod($"latest version: {newest}", publishUrl, current, newer);
                Assert.IsTrue(result.NewerPublished);
            }
            {
                var result = DefaultCheckMethod("nothing that looks like a version here", publishUrl, current, null);
                Assert.IsFalse(result.NewerPublished);
            }
        }
    }
}