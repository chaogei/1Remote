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
                var previous = _type;
                if (SetAndNotifyIfChanged(ref _type, value))
                {
                    // A jump host listens on 22 and a SOCKS/HTTP proxy on 1080, so the sensible default
                    // moves with the type — but only over a port the user has not chosen themselves.
                    if (_port == ProxyTypeName.DefaultPortOf(previous))
                        Port = ProxyTypeName.DefaultPortOf(value);
                    RaisePropertyChanged(nameof(Summary));
                    RaisePropertyChanged(nameof(IsSshJump));
                }
            }
        }

        /// <summary>
        /// Whether this entry is an SSH jump host rather than a SOCKS or HTTP proxy. Drives both the extra
        /// fields in the editor and which tunnel implementation the pool builds.
        /// </summary>
        [JsonIgnore]
        public bool IsSshJump => Type == EProxyType.SshJump;

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
        /// A short formatted summary for UI lists (e.g. "SOCKS5 · 127.0.0.1:1080").
        /// </summary>
        [JsonIgnore]
        public string Summary => string.IsNullOrWhiteSpace(Address)
            ? ProxyTypeName.Of(Type)
            : $"{ProxyTypeName.Of(Type)} · {Address}:{Port}";

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

        private string _privateKeyPath = "";
        /// <summary>
        /// Optional OpenSSH/PuTTY private key for an <see cref="EProxyType.SshJump"/> entry. When set it is
        /// tried before the password, which is the order OpenSSH itself uses.
        /// </summary>
        public string PrivateKeyPath
        {
            get => _privateKeyPath;
            set => SetAndNotifyIfChanged(ref _privateKeyPath, value?.Trim() ?? "");
        }

        [JsonProperty(nameof(PrivateKeyPassphrase))]
        private string EncryptedPrivateKeyPassphrase { get; set; } = "";

        /// <summary>
        /// Passphrase for <see cref="PrivateKeyPath"/>, stored the same way as <see cref="Password"/>.
        /// </summary>
        [JsonIgnore]
        public string PrivateKeyPassphrase
        {
            get => string.IsNullOrEmpty(EncryptedPrivateKeyPassphrase)
                ? ""
                : UnSafeStringEncipher.DecryptOrReturnOriginalString(EncryptedPrivateKeyPassphrase);
            set
            {
                var plain = value ?? "";
                if (plain == PrivateKeyPassphrase) return;
                EncryptedPrivateKeyPassphrase = plain.Length == 0 ? "" : UnSafeStringEncipher.SimpleEncrypt(plain);
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
                                && Port <= 65535
                                // A jump host is an account on a machine: with no user name there is nothing
                                // to authenticate as, and SSH.NET would reject the connection info outright.
                                && (Type != EProxyType.SshJump || !string.IsNullOrWhiteSpace(UserName));

        public ProxyConfig CloneMe() => (ProxyConfig)MemberwiseClone();

        /// <summary>
        /// Identifies the proxy endpoint for tunnel reuse. Deliberately excludes <see cref="Name"/>: two
        /// entries pointing at the same proxy should share one tunnel.
        ///
        /// The key path is part of it for a jump host, because two entries reaching the same account with
        /// different keys are different logins and must not be collapsed onto one SSH session.
        /// </summary>
        public string GetEndPointKey() => Type == EProxyType.SshJump
            ? $"{Type}://{UserName}@{Address}:{Port}#{PrivateKeyPath}"
            : $"{Type}://{UserName}@{Address}:{Port}";

        /// <summary>
        /// Everything a live tunnel authenticated with. A tunnel compares this against the current
        /// configuration to notice that a corrected password or a swapped key means it has to be rebuilt —
        /// unlike the endpoint key, these are not part of the pool key.
        /// </summary>
        public string GetCredentialKey() => $"{Password}\n{PrivateKeyPath}\n{PrivateKeyPassphrase}";
    }
}
