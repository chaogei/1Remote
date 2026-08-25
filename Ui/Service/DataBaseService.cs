using _1RM.Model.Protocol;
using _1RM.Model.Protocol.Base;
using _1RM.Utils;
using _1RM.Utils.ExternalSecret;

namespace _1RM.Service
{
    /// <summary>
    /// TODO: make it utils
    /// </summary>
    public static class DataService
    {
        public static void EncryptToDatabaseLevel(this ProtocolBase server)
        {
            // encrypt password
            if (server is ProtocolBaseWithAddressPortUserPwd s)
            {
                s.Password = UnSafeStringEncipher.EncryptOnce(s.Password);
                foreach (var credential in s.AlternateCredentials)
                {
                    credential.EncryptToDatabaseLevel();
                }
            }
            switch (server)
            {
                case SSH ssh when !string.IsNullOrWhiteSpace(ssh.PrivateKey):
                    {
                        ssh.PrivateKey = UnSafeStringEncipher.EncryptOnce(ssh.PrivateKey);
                        break;
                    }
                case RDP rdp when !string.IsNullOrWhiteSpace(rdp.GatewayPassword):
                    {
                        rdp.GatewayPassword = UnSafeStringEncipher.EncryptOnce(rdp.GatewayPassword);
                        break;
                    }

                case LocalApp app:
                    foreach (var arg in app.ArgumentList)
                    {
                        if (arg.Type == AppArgumentType.Secret)
                        {
                            arg.Value = UnSafeStringEncipher.EncryptOnce(arg.Value);
                        }
                    }
                    break;
            }
        }

        /// <summary>
        /// Turns what is stored into what can be used: deciphered, and — when the stored value is a
        /// <see cref="ExternalSecretResolver.PREFIX"/> reference rather than a secret — fetched from
        /// whichever password manager the reference names.
        ///
        /// Doing it here rather than at each call site is deliberate: this is the one place a stored
        /// password becomes a usable one, so every protocol gains external secrets at once and none of them
        /// can forget to.
        /// </summary>
        private static string ToUsableSecret(string stored)
        {
            var plain = UnSafeStringEncipher.DecryptOrReturnOriginalString(stored);
            return ExternalSecretResolver.IsReference(plain) ? ExternalSecretResolver.Resolve(plain) : plain;
        }

        public static void DecryptToConnectLevel(this ProtocolBase server)
        {
            if (server is ProtocolBaseWithAddressPortUserPwd s)
            {
                s.Password = ToUsableSecret(s.Password);
                foreach (var credential in s.AlternateCredentials)
                {
                    credential.DecryptToConnectLevel();
                }
            }
            switch (server)
            {
                case SSH ssh when !string.IsNullOrWhiteSpace(ssh.PrivateKey):
                    // A key path, not a secret: deciphered but never handed to a resolver.
                    ssh.PrivateKey = UnSafeStringEncipher.DecryptOrReturnOriginalString(ssh.PrivateKey);
                    break;

                case RDP rdp when !string.IsNullOrWhiteSpace(rdp.GatewayPassword):
                    rdp.GatewayPassword = ToUsableSecret(rdp.GatewayPassword);
                    break;

                case LocalApp app:
                    foreach (var arg in app.ArgumentList)
                    {
                        if (arg.Type == AppArgumentType.Secret)
                        {
                            arg.Value = ToUsableSecret(arg.Value);
                        }
                    }
                    break;
            }
        }


        public static void EncryptToDatabaseLevel(this Credential credential)
        {
            if (!string.IsNullOrEmpty(credential.Password))
                credential.Password = UnSafeStringEncipher.EncryptOnce(credential.Password);
            if (!string.IsNullOrEmpty(credential.PrivateKeyPath))
                credential.PrivateKeyPath = UnSafeStringEncipher.EncryptOnce(credential.PrivateKeyPath);
        }

        public static void DecryptToConnectLevel(this Credential credential)
        {
            if (!string.IsNullOrEmpty(credential.Password))
                credential.Password = ToUsableSecret(credential.Password);
            if (!string.IsNullOrEmpty(credential.PrivateKeyPath))
                credential.PrivateKeyPath = UnSafeStringEncipher.DecryptOrReturnOriginalString(credential.PrivateKeyPath);
        }
    }
}