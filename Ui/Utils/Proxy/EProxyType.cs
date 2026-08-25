namespace _1RM.Utils.Proxy
{
    public static class ProxyTypeName
    {
        /// <summary>
        /// How a type is spelled in the UI. These are protocol names rather than prose, so they are not
        /// translated — "SOCKS5" is written the same way in every locale this app ships.
        /// </summary>
        public static string Of(EProxyType type) => type switch
        {
            EProxyType.Socks5 => "SOCKS5",
            EProxyType.Socks4 => "SOCKS4",
            EProxyType.Socks4A => "SOCKS4a",
            EProxyType.Http => "HTTP CONNECT",
            EProxyType.SshJump => "SSH jump host",
            _ => type.ToString(),
        };

        /// <summary>
        /// The port to offer when a type is picked. Only ever applied over a port the user has not edited.
        /// </summary>
        public static int DefaultPortOf(EProxyType type) => type == EProxyType.SshJump ? 22 : 1080;
    }

    public enum EProxyType
    {
        /// <summary>
        /// Connect straight to the target.
        /// </summary>
        None = 0,

        /// <summary>
        /// RFC 1928. Supports IPv4, IPv6 and remote name resolution, and username/password auth (RFC 1929).
        /// </summary>
        Socks5 = 1,

        /// <summary>
        /// The target name is resolved locally, so the proxy only ever sees an IPv4 address.
        /// </summary>
        Socks4 = 2,

        /// <summary>
        /// SOCKS4 with the remote name resolution extension.
        /// </summary>
        Socks4A = 3,

        /// <summary>
        /// HTTP CONNECT tunnelling, with optional Basic proxy authentication.
        /// </summary>
        Http = 4,

        /// <summary>
        /// An SSH server used as a jump host: the session travels inside a channel that server opens to the
        /// target on our behalf, the equivalent of OpenSSH's <c>-J</c> / <c>ProxyJump</c>.
        ///
        /// Unlike the SOCKS and HTTP types this one authenticates as a user, so it also reads
        /// <see cref="ProxyConfig.UserName"/>, and optionally a private key.
        /// </summary>
        SshJump = 5,
    }
}
