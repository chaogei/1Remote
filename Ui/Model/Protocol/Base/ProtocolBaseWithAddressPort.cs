using System;
using System.Collections.ObjectModel;
using Newtonsoft.Json;
using System.ComponentModel;
using _1RM.Service;
using Shawn.Utils;

namespace _1RM.Model.Protocol.Base
{
    public abstract class ProtocolBaseWithAddressPort : ProtocolBase
    {
        protected ProtocolBaseWithAddressPort(string protocol, string classVersion, string protocolDisplayName) : base(protocol, classVersion, protocolDisplayName)
        {
        }

        #region Conn

        public const string MACRO_HOST_NAME = "%1RM_HOSTNAME%";
        private string _address = "";
        [OtherName(Name = "1RM_HOSTNAME")]
        public string Address
        {
            get => _address;
            set
            {
                var old = _address;
                if (SetAndNotifyIfChanged(ref _address, value))
                {
                    if (string.IsNullOrEmpty(DisplayName) || DisplayName == old)
                    {
                        DisplayName = value;
                    }
                    RaisePropertyChanged(nameof(SubTitle));
                }
            }
        }

        public int GetPort()
        {
            if (int.TryParse(Port, out var p))
                return p;
            return 1;
        }

        public const string MACRO_PORT = "%1RM_PORT%";
        private string _port = "3389";
        [OtherName(Name = "1RM_PORT")]
        public string Port
        {
            get => _port;
            set
            {
                if (SetAndNotifyIfChanged(ref _port, value))
                    RaisePropertyChanged(nameof(SubTitle));
            }
        }

        /// <summary>
        /// Where this session was really headed before it was pointed at a proxy tunnel, or null when it
        /// connects straight out.
        ///
        /// A tunnelled session has <see cref="Address"/> rewritten to loopback, which is what the protocol
        /// runner must dial. Everything that identifies or describes the session instead has to keep using
        /// <see cref="RealAddress"/>, or a proxied session looks like it is connected to this machine.
        /// </summary>
        [JsonIgnore] public string? TunnelledFromAddress { get; private set; }
        [JsonIgnore] public string? TunnelledFromPort { get; private set; }

        [JsonIgnore] public bool IsTunnelled => TunnelledFromAddress != null;

        /// <summary>The endpoint the user picked, whether or not the session goes through a proxy.</summary>
        [JsonIgnore] public string RealAddress => TunnelledFromAddress ?? Address;
        [JsonIgnore] public string RealPort => TunnelledFromPort ?? Port;

        /// <summary>
        /// Sends this session to a loopback relay while remembering where it was really going.
        /// </summary>
        public void RedirectThroughTunnel(string loopbackHost, int loopbackPort)
        {
            TunnelledFromAddress ??= Address;
            TunnelledFromPort ??= Port;
            // The Address setter renames the server when the display name still mirrors the old address,
            // which would retitle a session named after its IP to the loopback address.
            var displayName = DisplayName;
            Address = loopbackHost;
            Port = loopbackPort.ToString();
            DisplayName = displayName;
        }

        protected override string GetSubTitle()
        {
            return $"{RealAddress}:{RealPort}";
        }


        private string _macAddress = "";
        /// <summary>
        /// Optional hardware address, used only to wake the machine. Stored as typed and interpreted
        /// leniently, so pasting from an ARP table, a DHCP lease or a NIC properties dialog all work.
        /// </summary>
        public string MacAddress
        {
            get => _macAddress;
            set => SetAndNotifyIfChanged(ref _macAddress, value?.Trim() ?? "");
        }

        /// <summary>Whether there is an address good enough to send a magic packet to.</summary>
        [JsonIgnore]
        public bool CanWakeOnLan => Utils.WakeOnLan.WakeOnLan.TryParseMac(MacAddress, out _);


        private ObservableCollection<Credential>? _alternateCredentials = new ObservableCollection<Credential>();
        public ObservableCollection<Credential> AlternateCredentials
        {
            get => _alternateCredentials ??= new ObservableCollection<Credential>();
            set => SetAndNotifyIfChanged(ref _alternateCredentials, value);
        }


        private bool? _isPingBeforeConnect = true;
        [DefaultValue(true)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate, NullValueHandling = NullValueHandling.Ignore)]
        public bool? IsPingBeforeConnect
        {
            get => _isPingBeforeConnect;
            set => SetAndNotifyIfChanged(ref _isPingBeforeConnect, value);
        }


        private bool? _isAutoAlternateAddressSwitching = true;
        [DefaultValue(true)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate, NullValueHandling = NullValueHandling.Ignore)]
        public bool? IsAutoAlternateAddressSwitching
        {
            get => _isAutoAlternateAddressSwitching;
            set => SetAndNotifyIfChanged(ref _isAutoAlternateAddressSwitching, value);
        }

        public virtual Credential GetCredential()
        {
            var c = new Credential()
            {
                Address = Address,
                Port = Port,
            };
            return c;
        }

        public virtual void SetCredential(in Credential credential, bool ignoreEmptyString)
        {
            if (!ignoreEmptyString || !string.IsNullOrEmpty(credential.Address))
            {
                Address = credential.Address;
            }

            if (!ignoreEmptyString || !string.IsNullOrEmpty(credential.Port))
            {
                Port = credential.Port;
            }
        }

        #endregion Conn


        /// <summary>
        /// return true if show address input
        /// </summary>
        public virtual bool ShowAddressInput()
        {
            return true;
        }

        /// <summary>
        /// return true if show port input
        /// </summary>
        public virtual bool ShowPortInput()
        {
            return true;
        }

        /// <summary>
        /// build the id for host, TODO: DO WE STILL NEED BOTH ConnectionId and SESSIONID?
        /// </summary>
        /// <returns></returns>
        public override string BuildConnectionId()
        {
            // the real endpoint, not the tunnel: this identifies which server the session is talking to, and
            // the caller that looks a running session up by it has not been through the proxy yet
            return $"{Id}_{RealAddress}:{RealPort}";
        }

        #region IDataErrorInfo
        [JsonIgnore]
        public override string this[string columnName]
        {
            get
            {
                switch (columnName)
                {
                    case nameof(Address):
                        {
                            if (this.ShowAddressInput() && string.IsNullOrWhiteSpace(Address))
                            {
                                return IoC.Translate(LanguageService.CAN_NOT_BE_EMPTY);
                            }
                            break;
                        }
                    case nameof(Port):
                        {
                            if (this.ShowPortInput())
                            {
                                if (string.IsNullOrWhiteSpace(Port))
                                    return IoC.Translate(LanguageService.CAN_NOT_BE_EMPTY);
                                if (!long.TryParse(Port, out _) && Port != ServerEditorDifferentOptions)
                                    return IoC.Translate("Not a number");
                            }
                            break;
                        }
                    case nameof(MacAddress):
                        {
                            // Optional, so only a value that was typed and cannot be read is an error.
                            if (!string.IsNullOrWhiteSpace(MacAddress)
                                && MacAddress != ServerEditorDifferentOptions
                                && !CanWakeOnLan)
                                return IoC.Translate("wol_invalid_mac");
                            break;
                        }
                    default:
                        return base[columnName];
                }
                return "";
            }
        }
        #endregion
    }
}