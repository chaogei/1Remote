using Newtonsoft.Json;
using _1RM.Utils;
using Shawn.Utils;

namespace _1RM.Service.Backup
{
    /// <summary>
    /// Where backups are uploaded. One destination is enough: this is an off-machine copy of a personal
    /// configuration, not a fleet backup policy.
    /// </summary>
    public class WebDavConfig : NotifyPropertyChangedBase
    {
        private string _url = "";
        /// <summary>
        /// The collection to put archives in, for example
        /// <c>https://cloud.example.com/remote.php/dav/files/me/1Remote/</c>.
        /// </summary>
        public string Url
        {
            get => _url;
            set
            {
                if (SetAndNotifyIfChanged(ref _url, value?.Trim() ?? ""))
                    RaisePropertyChanged(nameof(IsUsable));
            }
        }

        private string _userName = "";
        public string UserName
        {
            get => _userName;
            set => SetAndNotifyIfChanged(ref _userName, value ?? "");
        }

        [JsonProperty(nameof(Password))]
        private string EncryptedPassword { get; set; } = "";

        /// <summary>
        /// Plain to the rest of the app, enciphered in the profile — the same split every other stored
        /// secret uses, including an empty one staying empty rather than becoming cipher text.
        /// </summary>
        [JsonIgnore]
        public string Password
        {
            get => string.IsNullOrEmpty(EncryptedPassword)
                ? ""
                : UnSafeStringEncipher.DecryptOrReturnOriginalString(EncryptedPassword);
            set
            {
                var plain = value ?? "";
                if (plain == Password) return;
                EncryptedPassword = plain.Length == 0 ? "" : UnSafeStringEncipher.SimpleEncrypt(plain);
                RaisePropertyChanged();
            }
        }

        [JsonIgnore]
        public bool IsUsable => Url.StartsWith("http://", System.StringComparison.OrdinalIgnoreCase)
                                || Url.StartsWith("https://", System.StringComparison.OrdinalIgnoreCase);

        /// <summary>The collection URL with exactly one trailing slash, which is how WebDAV names a folder.</summary>
        public string NormalizedUrl => Url.TrimEnd('/') + "/";

        /// <summary>The absolute URL of one archive inside the collection.</summary>
        public string UrlOf(string fileName) => NormalizedUrl + System.Uri.EscapeDataString(fileName);
    }
}
