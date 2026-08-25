using Newtonsoft.Json;
using Shawn.Utils;

namespace _1RM.Utils.PortForward
{
    public enum EPortForwardStatus
    {
        Stopped = 0,
        Running = 1,
        Failed = 2,
    }

    /// <summary>
    /// One standing port forward, kept independently of any session.
    ///
    /// The SSH host is referenced by the <see cref="Utils.Proxy.ProxyConfig.Name"/> of an entry on the proxy
    /// page rather than duplicating its address and credentials here — a bastion is usually both the jump
    /// host for sessions and the endpoint for forwards, and it should only be configured once.
    /// </summary>
    public class PortForwardConfig : NotifyPropertyChangedBase
    {
        public const string DEFAULT_BOUND_ADDRESS = "127.0.0.1";

        /// <summary>Binding here rather than on loopback publishes the forward to the whole network.</summary>
        public const string ANY_ADDRESS = "0.0.0.0";

        private string _name = "";
        public string Name
        {
            get => _name;
            set
            {
                if (SetAndNotifyIfChanged(ref _name, value.Length > 64 ? value.Substring(0, 64) : value))
                    RaisePropertyChanged(nameof(Summary));
            }
        }

        private EPortForwardType _type = EPortForwardType.Local;
        public EPortForwardType Type
        {
            get => _type;
            set
            {
                if (SetAndNotifyIfChanged(ref _type, value))
                {
                    RaisePropertyChanged(nameof(Summary));
                    RaisePropertyChanged(nameof(NeedsDestination));
                }
            }
        }

        private string _sshHostName = "";
        /// <summary>
        /// Name of the <see cref="Utils.Proxy.EProxyType.SshJump"/> entry to tunnel through. Empty means the
        /// forward has not been pointed at a host yet.
        /// </summary>
        public string SshHostName
        {
            get => _sshHostName;
            set
            {
                if (SetAndNotifyIfChanged(ref _sshHostName, value ?? ""))
                    RaisePropertyChanged(nameof(Summary));
            }
        }

        private string _boundAddress = DEFAULT_BOUND_ADDRESS;
        /// <summary>
        /// Where the listening socket goes: on this machine for <see cref="EPortForwardType.Local"/> and
        /// <see cref="EPortForwardType.Dynamic"/>, on the SSH host for <see cref="EPortForwardType.Remote"/>.
        /// </summary>
        public string BoundAddress
        {
            get => _boundAddress;
            set
            {
                if (SetAndNotifyIfChanged(ref _boundAddress, value?.Trim() ?? ""))
                {
                    RaisePropertyChanged(nameof(Summary));
                    RaisePropertyChanged(nameof(IsExposedToNetwork));
                }
            }
        }

        private int _boundPort = 8080;
        public int BoundPort
        {
            get => _boundPort;
            set
            {
                if (SetAndNotifyIfChanged(ref _boundPort, value))
                    RaisePropertyChanged(nameof(Summary));
            }
        }

        private string _destinationHost = "";
        public string DestinationHost
        {
            get => _destinationHost;
            set
            {
                if (SetAndNotifyIfChanged(ref _destinationHost, value?.Trim() ?? ""))
                    RaisePropertyChanged(nameof(Summary));
            }
        }

        private int _destinationPort = 80;
        public int DestinationPort
        {
            get => _destinationPort;
            set
            {
                if (SetAndNotifyIfChanged(ref _destinationPort, value))
                    RaisePropertyChanged(nameof(Summary));
            }
        }

        private bool _autoStart;
        /// <summary>Brought up in the background once the app has finished starting.</summary>
        public bool AutoStart
        {
            get => _autoStart;
            set => SetAndNotifyIfChanged(ref _autoStart, value);
        }

        /// <summary>A dynamic forward has no fixed destination; the caller names one per connection.</summary>
        [JsonIgnore]
        public bool NeedsDestination => Type != EPortForwardType.Dynamic;

        /// <summary>
        /// True when the listening socket accepts from anywhere rather than just this machine, which is
        /// worth saying out loud in the editor: it hands the tunnel to everyone on the network.
        /// </summary>
        [JsonIgnore]
        public bool IsExposedToNetwork => BoundAddress == ANY_ADDRESS || BoundAddress == "*" || BoundAddress == "::";

        [JsonIgnore]
        public string Summary
        {
            get
            {
                var via = string.IsNullOrWhiteSpace(SshHostName) ? "?" : SshHostName;
                var bound = $"{BoundAddress}:{BoundPort}";
                return Type switch
                {
                    EPortForwardType.Dynamic => $"SOCKS {bound} · {via}",
                    EPortForwardType.Remote => $"{via}:{BoundPort} -> {DestinationHost}:{DestinationPort}",
                    _ => $"{bound} -> {DestinationHost}:{DestinationPort} · {via}",
                };
            }
        }

        /// <summary>
        /// Why this forward cannot be started, or null when it can. Returned as text rather than a bool so
        /// the editor can say which field is the problem instead of just greying out the start button.
        /// </summary>
        public string? Validate()
        {
            if (string.IsNullOrWhiteSpace(SshHostName))
                return IoC.Translate("port_forward_invalid_no_host");
            if (string.IsNullOrWhiteSpace(BoundAddress))
                return IoC.Translate("port_forward_invalid_bound_address");
            if (BoundPort <= 0 || BoundPort > 65535)
                return IoC.Translate("port_forward_invalid_bound_port");
            if (NeedsDestination)
            {
                if (string.IsNullOrWhiteSpace(DestinationHost))
                    return IoC.Translate("port_forward_invalid_destination");
                if (DestinationPort <= 0 || DestinationPort > 65535)
                    return IoC.Translate("port_forward_invalid_destination_port");
            }
            return null;
        }

        [JsonIgnore]
        public bool IsUsable => Validate() == null;

        private EPortForwardStatus _status = EPortForwardStatus.Stopped;
        /// <summary>Runtime only: what the service is currently doing with this entry.</summary>
        [JsonIgnore]
        public EPortForwardStatus Status
        {
            get => _status;
            set
            {
                if (SetAndNotifyIfChanged(ref _status, value))
                {
                    RaisePropertyChanged(nameof(IsRunning));
                    RaisePropertyChanged(nameof(IsFailed));
                }
            }
        }

        [JsonIgnore] public bool IsRunning => Status == EPortForwardStatus.Running;
        [JsonIgnore] public bool IsFailed => Status == EPortForwardStatus.Failed;

        private string _lastError = "";
        /// <summary>Runtime only: why the last start attempt failed, shown next to the entry.</summary>
        [JsonIgnore]
        public string LastError
        {
            get => _lastError;
            set
            {
                if (SetAndNotifyIfChanged(ref _lastError, value ?? ""))
                    RaisePropertyChanged(nameof(HasLastError));
            }
        }

        [JsonIgnore] public bool HasLastError => !string.IsNullOrEmpty(LastError);
    }
}
