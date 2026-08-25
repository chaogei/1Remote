using System;
using System.Collections.Generic;
using System.IO;
using Renci.SshNet;

namespace _1RM.Utils.Proxy
{
    /// <summary>
    /// Turns an <see cref="EProxyType.SshJump"/> entry into something SSH.NET can dial.
    ///
    /// Both the per-session jump tunnel and the standing port forwards authenticate against the same kind of
    /// host, and getting the method order or the keyboard-interactive fallback wrong in only one of them
    /// would produce a login that works in one feature and fails in the other.
    /// </summary>
    public static class SshConnectionFactory
    {
        public const int CONNECT_TIMEOUT_SECONDS = 15;

        /// <summary>
        /// Idle SSH sessions are exactly the ones a firewall or an sshd <c>ClientAliveInterval</c> reaps, and
        /// a tab left open overnight is idle from SSH's point of view even while the user is working in it.
        /// </summary>
        public static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(30);

        public static ConnectionInfo Build(ProxyConfig jump)
        {
            if (jump == null) throw new ArgumentNullException(nameof(jump));

            var methods = new List<AuthenticationMethod>();

            if (!string.IsNullOrEmpty(jump.PrivateKeyPath))
            {
                if (!File.Exists(jump.PrivateKeyPath))
                    throw new FileNotFoundException($"the private key '{jump.PrivateKeyPath}' does not exist");

                var passphrase = jump.PrivateKeyPassphrase;
                var keyFile = string.IsNullOrEmpty(passphrase)
                    ? new PrivateKeyFile(jump.PrivateKeyPath)
                    : new PrivateKeyFile(jump.PrivateKeyPath, passphrase);
                methods.Add(new PrivateKeyAuthenticationMethod(jump.UserName, keyFile));
            }

            if (!string.IsNullOrEmpty(jump.Password))
            {
                methods.Add(new PasswordAuthenticationMethod(jump.UserName, jump.Password));
                // Plenty of servers advertise only keyboard-interactive and reject the "password" method
                // outright, even though the credential they are asking for is the same one.
                methods.Add(BuildKeyboardInteractive(jump.UserName, jump.Password));
            }

            if (methods.Count == 0)
                throw new InvalidOperationException("the SSH host has neither a password nor a private key configured");

            return new ConnectionInfo(jump.Address, jump.Port, jump.UserName, methods.ToArray())
            {
                Timeout = TimeSpan.FromSeconds(CONNECT_TIMEOUT_SECONDS),
            };
        }

        /// <summary>An authenticated, connected client. The caller owns it.</summary>
        public static SshClient Connect(ProxyConfig jump)
        {
            var client = new SshClient(Build(jump))
            {
                KeepAliveInterval = KeepAliveInterval,
            };
            try
            {
                client.Connect();
                return client;
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }

        private static KeyboardInteractiveAuthenticationMethod BuildKeyboardInteractive(string userName, string password)
        {
            var method = new KeyboardInteractiveAuthenticationMethod(userName);
            method.AuthenticationPrompt += (_, e) =>
            {
                foreach (var prompt in e.Prompts)
                {
                    // Anything else in a prompt list is a second factor we have no answer for; leaving it
                    // blank lets the server reject it with its own message rather than us sending a password
                    // to a field that did not ask for one.
                    if (prompt.Request.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0)
                        prompt.Response = password;
                }
            };
            return method;
        }
    }
}
