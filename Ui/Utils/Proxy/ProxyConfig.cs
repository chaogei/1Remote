using Newtonsoft.Json;
using Shawn.Utils;

namespace _1RM.Utils.Proxy
{
    /// <summary>
    /// One entry of the global proxy list. Servers reference it by <see cref="Name"/>, so renaming an entry
    /// detaches every server that used it — <see cref="ProxyConfig"/> has no id of its own on purpose, the
    /// name is what the user picks in the editor.
    /// </summary>
    public class ProxyConfig : NotifyPropertyChangedBase
    {
        /// <summary>
        /// Value of <c>ProtocolBase.ProxyName</c> that means "connect directly".
        /// </summary>
        public const string NO_PROXY = "";

        private string _name = "";
        public string Name
        {
            get => _name;
            set
            {
                if (SetAndNotifyIfChanged(ref _name, value.Length > 64 ? value.Substring(0, 64) : value))
                {
                    RaisePropertyChanged(nameof(Summary));
                }
            }
        }

        private EProxyType _type = EProxyType.Socks5;
        public EProxyType Type
        {
            get => _type;
            set
            {
                if (SetAndNotifyIfChanged(ref _type, value))
                {
                    RaisePropertyChanged(nameof(Summary));
                }
            }
        }

        private string _address = "";
        /// <summary>
        /// Trimmed on the way in: an address is usually pasted, a stray space around it is invisible in the
        /// editor, and it would otherwise reach both the connect call and the tunnel pool key.
        /// </summary>
        public string Address
        {
            get => _address;
            set
            {
                if (SetAndNotifyIfChanged(ref _address, value?.Trim() ?? ""))
                {
                    RaisePropertyChanged(nameof(Summary));
                }
            }
        }

        private int _port = 1080;
        public int Port
        {
            get => _port;
            set
            {
                if (SetAndNotifyIfChanged(ref _port, value))
                {
                    RaisePropertyChanged(nameof(Summary));
                }
            }
        }

        /// <summary>
        /// A short formatted summary for UI lists (e.g. "Socks5 · 127.0.0.1:1080").
        /// </summary>
        [JsonIgnore]
        public string Summary => string.IsNullOrWhiteSpace(Address)
            ? Type.ToString()
            : $"{Type} · {Address}:{Port}";

        private string _userName = "";
        public string UserName
        {
            get => _userName;
            set => SetAndNotifyIfChanged(ref _userName, value);
        }

        [JsonProperty(nameof(Password))]
        private string EncryptedPassword { get; set; } = "";

        /// <summary>
        /// Plain text to the rest of the app, enciphered in the profile — the same split the database
        /// sources use. Exposing the enciphered form directly is what used to put a block of cipher text in
        /// the settings editor, for an empty password as much as for a real one.
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

        /// <summary>
        /// Targets that are this machine itself skip the proxy entirely. Private ranges do not count as
        /// local here, see <see cref="ProxyTunnelPool.IsLocalAddress"/>.
        /// </summary>
        private bool _bypassForLocalAddress = true;
        public bool BypassForLocalAddress
        {
            get => _bypassForLocalAddress;
            set => SetAndNotifyIfChanged(ref _bypassForLocalAddress, value);
        }

        [JsonIgnore]
        public bool IsUsable => Type != EProxyType.None
                                && !string.IsNullOrWhiteSpace(Address)
                                && Port > 0
                                && Port <= 65535;

        public ProxyConfig CloneMe() => (ProxyConfig)MemberwiseClone();

        /// <summary>
        /// Identifies the proxy endpoint for tunnel reuse. Deliberately excludes <see cref="Name"/>: two
        /// entries pointing at the same proxy should share one tunnel.
        /// </summary>
        public string GetEndPointKey() => $"{Type}://{UserName}@{Address}:{Port}";
    }
}
