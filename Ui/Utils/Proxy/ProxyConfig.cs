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
            set => SetAndNotifyIfChanged(ref _name, value.Length > 64 ? value.Substring(0, 64) : value);
        }

        private EProxyType _type = EProxyType.Socks5;
        public EProxyType Type
        {
            get => _type;
            set => SetAndNotifyIfChanged(ref _type, value);
        }

        private string _address = "";
        public string Address
        {
            get => _address;
            set => SetAndNotifyIfChanged(ref _address, value);
        }

        private int _port = 1080;
        public int Port
        {
            get => _port;
            set => SetAndNotifyIfChanged(ref _port, value);
        }

        private string _userName = "";
        public string UserName
        {
            get => _userName;
            set => SetAndNotifyIfChanged(ref _userName, value);
        }

        /// <summary>
        /// Stored encrypted in the profile, exactly like the other secrets. Read it through
        /// <see cref="GetPlainPassword"/> rather than directly.
        /// </summary>
        private string _password = "";
        public string Password
        {
            get => _password;
            set => SetAndNotifyIfChanged(ref _password, value);
        }

        /// <summary>
        /// Targets that resolve to the local machine or a private range skip the proxy entirely.
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

        public string GetPlainPassword() => UnSafeStringEncipher.DecryptOrReturnOriginalString(Password);

        public void EncryptPassword() => Password = UnSafeStringEncipher.EncryptOnce(Password);

        public ProxyConfig CloneMe() => (ProxyConfig)MemberwiseClone();

        /// <summary>
        /// Identifies the proxy endpoint for tunnel reuse. Deliberately excludes <see cref="Name"/>: two
        /// entries pointing at the same proxy should share one tunnel.
        /// </summary>
        public string GetEndPointKey() => $"{Type}://{UserName}@{Address}:{Port}";
    }
}
