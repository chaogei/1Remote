namespace _1RM.Utils.Proxy
{
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
    }
}
