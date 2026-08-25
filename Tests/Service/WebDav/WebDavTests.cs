using _1RM.Service.Backup;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace Tests.Service.Backup
{
    [TestClass]
    public class WebDavTests
    {
        [TestInitialize]
        public void Setup() => TestInit.Init();

        [DataTestMethod]
        [DataRow("https://cloud.example.com/dav/1Remote", true)]
        [DataRow("http://nas.local/dav/", true)]
        [DataRow("cloud.example.com/dav", false)]
        [DataRow("ftp://cloud.example.com/dav", false)]
        [DataRow("", false)]
        public void OnlyAnHttpAddressCanBeUsedAsADestination(string url, bool expected)
        {
            Assert.AreEqual(expected, new WebDavConfig { Url = url }.IsUsable);
        }

        [TestMethod]
        public void TheCollectionUrlEndsInExactlyOneSlash()
        {
            Assert.AreEqual("https://x/dav/", new WebDavConfig { Url = "https://x/dav" }.NormalizedUrl);
            Assert.AreEqual("https://x/dav/", new WebDavConfig { Url = "https://x/dav/" }.NormalizedUrl);
            Assert.AreEqual("https://x/dav/", new WebDavConfig { Url = "https://x/dav///" }.NormalizedUrl);
        }

        [TestMethod]
        public void AFileNameIsEscapedIntoTheUrl()
        {
            var config = new WebDavConfig { Url = "https://x/dav" };

            Assert.AreEqual("https://x/dav/1Remote-20260825-160703.1rbak",
                config.UrlOf("1Remote-20260825-160703.1rbak"));
        }

        [TestMethod]
        public void TheDestinationPasswordIsNotStoredInTheClear()
        {
            var config = new WebDavConfig { Url = "https://x/dav", UserName = "me", Password = "hunter2" };

            var json = JsonConvert.SerializeObject(config);

            Assert.IsFalse(json.Contains("hunter2"));
            Assert.AreEqual("hunter2", JsonConvert.DeserializeObject<WebDavConfig>(json)!.Password);
        }

        [TestMethod]
        public void AnEmptyDestinationPasswordStaysEmpty()
        {
            var config = new WebDavConfig { Password = "" };

            Assert.AreEqual("", config.Password);
            Assert.AreEqual("", JsonConvert.DeserializeObject<WebDavConfig>(JsonConvert.SerializeObject(config))!.Password);
        }

        /// <summary>Shaped like Nextcloud's answer, which is the one most people will meet.</summary>
        private const string MULTISTATUS = @"<?xml version=""1.0""?>
<d:multistatus xmlns:d=""DAV:"">
  <d:response><d:href>/remote.php/dav/files/me/1Remote/</d:href></d:response>
  <d:response><d:href>/remote.php/dav/files/me/1Remote/1Remote-20260824-101500.1rbak</d:href></d:response>
  <d:response><d:href>/remote.php/dav/files/me/1Remote/1Remote-20260825-160703.1rbak</d:href></d:response>
  <d:response><d:href>/remote.php/dav/files/me/1Remote/notes.txt</d:href></d:response>
</d:multistatus>";

        [TestMethod]
        public void ListingReturnsOnlyBackupsNewestFirst()
        {
            var names = WebDavClient.ParseFileNames(MULTISTATUS);

            CollectionAssert.AreEqual(
                new[] { "1Remote-20260825-160703.1rbak", "1Remote-20260824-101500.1rbak" },
                names,
                "the collection itself and unrelated files do not belong in the list");
        }

        [TestMethod]
        public void PercentEncodedNamesAreDecoded()
        {
            const string xml = @"<d:multistatus xmlns:d=""DAV:"">
  <d:response><d:href>/dav/my%20backup.1rbak</d:href></d:response>
</d:multistatus>";

            CollectionAssert.AreEqual(new[] { "my backup.1rbak" }, WebDavClient.ParseFileNames(xml));
        }

        [TestMethod]
        public void AnUnreadableAnswerGivesAnEmptyListRatherThanThrowing()
        {
            // Servers answer 401 pages and error documents with a 207 often enough that this must not be
            // allowed to take down the settings page.
            Assert.AreEqual(0, WebDavClient.ParseFileNames("<html>not xml at all").Count);
            Assert.AreEqual(0, WebDavClient.ParseFileNames("").Count);
        }
    }
}
