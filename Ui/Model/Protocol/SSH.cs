using System;
using Newtonsoft.Json;
using _1RM.Model.Protocol.Base;
using _1RM.Utils.PuTTY;
using _1RM.Utils.PuTTY;
using Shawn.Utils;

namespace _1RM.Model.Protocol
{
    // ReSharper disable once InconsistentNaming
    public class SSH : ProtocolBaseWithAddressPortUserPwd, IPuttyConnectable
    {
        public static string ProtocolName = "SSH";
        public SSH() : base(SSH.ProtocolName, $"Putty.{SSH.ProtocolName}.V1", SSH.ProtocolName)
        {
            base.UserName = "root";
            base.Port = "22";
        }

        private int? _sshVersion = 2;

        [OtherName(Name = "SSH_VERSION")]
        public int? SshVersion
        {
            get => _sshVersion;
            set => SetAndNotifyIfChanged(ref _sshVersion, value);
        }

        private string _startupAutoCommand = "";

        [OtherName(Name = "STARTUP_AUTO_COMMAND")]
        public string StartupAutoCommand
        {
            get => _startupAutoCommand;
            set => SetAndNotifyIfChanged(ref _startupAutoCommand, value);
        }

        private string _externalKittySessionConfigPath = "";
        public string ExternalKittySessionConfigPath
        {
            get => _externalKittySessionConfigPath;
            set => SetAndNotifyIfChanged(ref _externalKittySessionConfigPath, value);
        }

        private string _externalSessionConfigPath = "";
        public string ExternalSessionConfigPath
        {
            get => string.IsNullOrEmpty(_externalSessionConfigPath) ? _externalKittySessionConfigPath : _externalSessionConfigPath;
            set => SetAndNotifyIfChanged(ref _externalSessionConfigPath, value);
        }


        private bool _openSftpOnConnected = false;
        public bool OpenSftpOnConnected
        {
            get => _openSftpOnConnected;
            set => SetAndNotifyIfChanged(ref _openSftpOnConnected, value);
        }

        /// <summary>
        /// Forwarding rules, one per line, in the readable form <see cref="SshPortForwardingRules"/> accepts.
        /// PuTTY has always been able to do this - the key was written out as an empty string, so the
        /// capability was there and simply not reachable.
        /// </summary>
        private string _portForwardings = "";
        [OtherName(Name = "SSH_PORT_FORWARDINGS")]
        public string PortForwardings
        {
            get => _portForwardings;
            set => SetAndNotifyIfChanged(ref _portForwardings, value);
        }

        /// <summary>Lets the remote host use the keys held by the local agent (Pageant).</summary>
        private bool _enableAgentForwarding = false;
        [OtherName(Name = "SSH_AGENT_FORWARDING")]
        public bool EnableAgentForwarding
        {
            get => _enableAgentForwarding;
            set => SetAndNotifyIfChanged(ref _enableAgentForwarding, value);
        }

        private bool _enableX11Forwarding = false;
        [OtherName(Name = "SSH_X11_FORWARDING")]
        public bool EnableX11Forwarding
        {
            get => _enableX11Forwarding;
            set => SetAndNotifyIfChanged(ref _enableX11Forwarding, value);
        }

        public override bool IsOnlyOneInstance()
        {
            return false;
        }

        public override ProtocolBase? CreateFromJsonString(string jsonString)
        {
            try
            {
                var ret = JsonConvert.DeserializeObject<SSH>(jsonString);
                return ret;
            }
            catch (Exception e)
            {
                SimpleLogHelper.Debug(e);
                return null;
            }
        }

        public override double GetListOrder()
        {
            // https://github.com/kovidgoyal/kitty
            return 2;
        }

        [JsonIgnore]
        public ProtocolBase ProtocolBase => this;


        public override Credential GetCredential()
        {
            var c = new Credential()
            {
                Address = Address,
                Port = Port,
                Password = Password,
                UserName = UserName,
                PrivateKeyPath = PrivateKey,
            };
            return c;
        }

        public override bool ShowUserNameInput()
        {
            return true;
        }

        public override bool ShowPasswordInput()
        {
            return true;
        }

        public override bool ShowPrivateKeyInput()
        {
            return true;
        }
    }
}