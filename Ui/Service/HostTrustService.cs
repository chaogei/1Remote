using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using _1RM.Utils;
using Newtonsoft.Json;
using Shawn.Utils;

namespace _1RM.Service
{
    /// <summary>
    /// Remembers which host identities the user has accepted, so SFTP and FTPS can behave the way SSH
    /// already does through PuTTY: verify on every connection, ask once when the identity is new, refuse
    /// silently changing behind the user's back.
    ///
    /// Both of those transports previously accepted anything — SFTP never subscribed to HostKeyReceived and
    /// FTP's certificate callback was the unmodified <c>e.Accept = true</c> sample — which left the password
    /// readable to anyone able to intercept the connection.
    /// </summary>
    public class HostTrustService
    {
        /// <summary>Fingerprint by "kind|host:port". Kind separates an SSH host key from a TLS certificate.</summary>
        private Dictionary<string, string> _trusted = new Dictionary<string, string>();
        private readonly object _lock = new object();
        private bool _loaded;

        public delegate bool ConfirmDelegate(string title, string message);

        /// <summary>
        /// Replaced in tests; by default this is wired to the normal confirmation dialog.
        /// </summary>
        public ConfirmDelegate Confirm { get; set; } =
            (title, message) => MessageBoxHelper.Confirm(message, title: title);

        public static string Fingerprint(byte[] data)
        {
            using var sha = SHA256.Create();
            return "SHA256:" + Convert.ToBase64String(sha.ComputeHash(data)).TrimEnd('=');
        }

        private static string BuildKey(string kind, string host, int port) => $"{kind}|{host}:{port}";

        /// <summary>
        /// Verifies the identity, prompting once when it is unknown. Returns false when the user declines or
        /// when a previously accepted identity has changed.
        /// </summary>
        /// <param name="kind">"ssh" or "tls".</param>
        /// <param name="detail">Extra context shown to the user, e.g. the TLS policy errors.</param>
        public bool VerifyOrAsk(string kind, string host, int port, string fingerprint, string detail = "")
        {
            Load();
            var key = BuildKey(kind, host, port);

            string? known;
            lock (_lock)
            {
                _trusted.TryGetValue(key, out known);
            }

            if (known == fingerprint)
                return true;

            var isChanged = known != null;
            var message = isChanged
                ? IoC.Translate("host_trust_changed", $"{host}:{port}", known!, fingerprint)
                : IoC.Translate("host_trust_new", $"{host}:{port}", fingerprint);
            if (!string.IsNullOrEmpty(detail))
                message += Environment.NewLine + detail;

            if (!Confirm(IoC.Translate("host_trust_title"), message))
            {
                SimpleLogHelper.Warning($"HostTrustService: user rejected {key} ({fingerprint})");
                return false;
            }

            lock (_lock)
            {
                _trusted[key] = fingerprint;
            }
            Save();
            return true;
        }

        private void Load()
        {
            lock (_lock)
            {
                if (_loaded) return;
                _loaded = true;
                try
                {
                    var path = AppPathHelper.Instance.HostTrustJsonPath;
                    if (!File.Exists(path)) return;
                    _trusted = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path, Encoding.UTF8))
                               ?? new Dictionary<string, string>();
                }
                catch (Exception e)
                {
                    SimpleLogHelper.Warning($"HostTrustService: cannot read the trust store, {e.Message}");
                }
            }
        }

        private void Save()
        {
            try
            {
                var path = AppPathHelper.Instance.HostTrustJsonPath;
                var dir = new FileInfo(path).Directory;
                if (dir?.Exists == false)
                    dir.Create();

                string json;
                lock (_lock)
                {
                    json = JsonConvert.SerializeObject(_trusted, Formatting.Indented);
                }
                File.WriteAllText(path, json, Encoding.UTF8);
            }
            catch (Exception e)
            {
                SimpleLogHelper.Error($"HostTrustService: cannot write the trust store, {e.Message}");
            }
        }
    }
}
