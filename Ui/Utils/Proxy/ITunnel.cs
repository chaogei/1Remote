using System;

namespace _1RM.Utils.Proxy
{
    /// <summary>
    /// A loopback endpoint that carries a session to its real target by some indirect route.
    ///
    /// Callers only ever need the port: <see cref="Service.ProxyService.ApplyTo"/> points the session at
    /// <c>127.0.0.1:LocalPort</c> and knows nothing about how the bytes get the rest of the way. That is what
    /// lets a SOCKS relay and an SSH jump host be the same thing as far as every protocol is concerned.
    /// </summary>
    public interface ITunnel : IDisposable
    {
        /// <summary>The loopback port a session should be pointed at.</summary>
        int LocalPort { get; }

        /// <summary>
        /// False once the tunnel can no longer carry traffic, so the pool drops it and builds a new one.
        /// </summary>
        bool IsAlive { get; }

        /// <summary>
        /// Takes the credentials from the current configuration. The pool key covers the route but not the
        /// secret, so without this a corrected password would not take effect until the app restarted.
        /// </summary>
        void RefreshCredentials(ProxyConfig proxy);
    }
}
