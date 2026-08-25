using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Shawn.Utils;

namespace _1RM.Service.Backup
{
    /// <summary>
    /// The three WebDAV verbs a backup needs: PUT to upload, GET to download, PROPFIND to list.
    ///
    /// Written directly against HttpClient rather than pulling in a WebDAV library, because that is the
    /// whole of the protocol surface used here and a dependency would be almost entirely unused code.
    /// </summary>
    public static class WebDavClient
    {
        private static readonly HttpMethod PropFind = new HttpMethod("PROPFIND");
        private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

        private static HttpClient Create(WebDavConfig config)
        {
            var client = new HttpClient { Timeout = Timeout };
            if (config.UserName.Length > 0 || config.Password.Length > 0)
            {
                var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{config.UserName}:{config.Password}"));
                // Sent up front rather than after a challenge: a server that answers an unauthenticated
                // PROPFIND with 401 and no WWW-Authenticate would otherwise never get the credentials.
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
            }
            return client;
        }

        public static async Task UploadAsync(WebDavConfig config, string localPath, string remoteFileName, CancellationToken ct = default)
        {
            using var client = Create(config);
            using var stream = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var content = new StreamContent(stream);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            using var response = await client.PutAsync(config.UrlOf(remoteFileName), content, ct).ConfigureAwait(false);
            await EnsureSuccess(response, $"uploading {remoteFileName}").ConfigureAwait(false);
            SimpleLogHelper.Info($"WebDavClient: uploaded {remoteFileName}");
        }

        public static async Task DownloadAsync(WebDavConfig config, string remoteFileName, string localPath, CancellationToken ct = default)
        {
            using var client = Create(config);
            using var response = await client.GetAsync(config.UrlOf(remoteFileName), HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            await EnsureSuccess(response, $"downloading {remoteFileName}").ConfigureAwait(false);

            using var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var target = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await source.CopyToAsync(target, 81920, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// The backup archives in the collection, newest name first. Only our own extension is returned, so
        /// pointing this at a folder that holds other things does not fill the list with noise.
        /// </summary>
        public static async Task<List<string>> ListAsync(WebDavConfig config, CancellationToken ct = default)
        {
            using var client = Create(config);
            using var request = new HttpRequestMessage(PropFind, config.NormalizedUrl);
            // Depth 1 is the collection and its direct children; the default of "infinity" would walk an
            // entire cloud drive and is refused outright by most servers.
            request.Headers.Add("Depth", "1");
            request.Content = new StringContent(
                "<?xml version=\"1.0\" encoding=\"utf-8\"?><d:propfind xmlns:d=\"DAV:\"><d:prop><d:displayname/></d:prop></d:propfind>",
                Encoding.UTF8, "application/xml");

            using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
            await EnsureSuccess(response, "listing the collection").ConfigureAwait(false);

            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return ParseFileNames(body);
        }

        /// <summary>
        /// Pulls the file names out of a multistatus response. Exposed for testing: the shape of this XML
        /// varies between servers far more than the verbs do.
        /// </summary>
        public static List<string> ParseFileNames(string multiStatusXml)
        {
            var names = new List<string>();
            try
            {
                XNamespace dav = "DAV:";
                var document = XDocument.Parse(multiStatusXml);

                foreach (var href in document.Descendants(dav + "href"))
                {
                    var value = href.Value?.Trim();
                    if (string.IsNullOrEmpty(value)) continue;

                    // The href is the collection itself when it ends in a slash, and percent-encoded on
                    // every server that has ever been tested against.
                    var lastSegment = value!.TrimEnd('/').Split('/').LastOrDefault();
                    if (string.IsNullOrEmpty(lastSegment)) continue;

                    var name = Uri.UnescapeDataString(lastSegment!);
                    if (name.EndsWith(BackupService.FILE_EXTENSION, StringComparison.OrdinalIgnoreCase)
                        && !names.Contains(name, StringComparer.OrdinalIgnoreCase))
                    {
                        names.Add(name);
                    }
                }
            }
            catch (Exception e)
            {
                SimpleLogHelper.Warning($"WebDavClient: could not read the listing, {e.Message}");
            }

            // The names carry a sortable timestamp, so this is newest first without asking for dates.
            names.Sort((a, b) => string.CompareOrdinal(b, a));
            return names;
        }

        private static async Task EnsureSuccess(HttpResponseMessage response, string what)
        {
            if (response.IsSuccessStatusCode) return;

            var body = "";
            try
            {
                body = (await response.Content.ReadAsStringAsync().ConfigureAwait(false) ?? "").Trim();
                if (body.Length > 200) body = body.Substring(0, 200);
            }
            catch
            {
                // the status code on its own is still worth reporting
            }

            var detail = body.Length > 0 ? $": {body}" : "";
            throw new HttpRequestException($"{what} failed with {(int)response.StatusCode} {response.ReasonPhrase}{detail}");
        }
    }
}
