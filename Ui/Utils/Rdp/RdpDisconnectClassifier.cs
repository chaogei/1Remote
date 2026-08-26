using System;

namespace _1RM.Utils.Rdp
{
    /// <summary>
    /// Classifies ActiveX <c>OnDisconnected</c> codes so a first connect after the remote PC reboots can
    /// retry the same way mstsc waits, without looping forever on a bad password.
    ///
    /// Codes: https://learn.microsoft.com/windows/win32/termserv/imstscaxevents-ondisconnected
    /// and the SSL subset on the same page.
    /// </summary>
    public static class RdpDisconnectClassifier
    {
        public const int NoInfo = 0;
        public const int LocalNotError = 1;
        public const int RemoteByUser = 2;
        public const int ByServer = 3;
        /// <summary>Some older clients report a network drop as 4 rather than 0.</summary>
        public const int NetworkDrop = 4;

        public const int DnsLookupFailed = 260;
        public const int ConnectionTimedOut = 264;
        public const int SocketSendFailed = 772;
        public const int SocketRecvFailed = 1028;
        public const int DnsLookupFailed2 = 1288;
        public const int GetHostByNameFailed = 1540;
        public const int SocketConnectFailed = 516;
        public const int HostNotFound = 520;
        public const int WinsockFdClose = 2308;
        public const int InternalSecurityError = 2312;
        public const int InternalSecurityError2 = 2568;

        public const int SslLogonDenied = 2055;
        public const int SslNoSuchUser = 2567;
        public const int SslAccountDisabled = 2823;
        public const int SslAccountRestriction = 3079;
        public const int SslAccountLockedOut = 3335;
        public const int SslAccountExpired = 3591;
        public const int SslPasswordExpired = 3847;
        public const int SslPasswordMustChange = 4615;
        public const int SslDelegationPolicy = 5639;
        public const int SslPolicyNtlmOnly = 5895;
        public const int SslNoAuthenticatingAuthority = 6151;
        public const int SslCertExpired = 6919;
        public const int SslFreshCredRequired = 8455;

        public const int LicensingFailed = 2056;
        public const int LicensingTimeout = 2310;

        /// <summary>
        /// The remote side is not ready yet, or the path to it dropped: TermService still starting, 3389
        /// not bound, DNS/DHCP after reboot, CredSSP not accepting yet. Worth another try.
        /// </summary>
        public static bool IsTransient(int discReason)
        {
            switch (discReason)
            {
                case NoInfo:
                case NetworkDrop:
                case DnsLookupFailed:
                case DnsLookupFailed2:
                case GetHostByNameFailed:
                case HostNotFound:
                case ConnectionTimedOut:
                case SocketConnectFailed:
                case SocketSendFailed:
                case SocketRecvFailed:
                case WinsockFdClose:
                case InternalSecurityError:
                case InternalSecurityError2:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Wrong user, locked account, expired password — retrying will not help.</summary>
        public static bool IsAuthenticationFailure(int discReason)
        {
            switch (discReason)
            {
                case SslLogonDenied:
                case SslNoSuchUser:
                case SslAccountDisabled:
                case SslAccountRestriction:
                case SslAccountLockedOut:
                case SslAccountExpired:
                case SslPasswordExpired:
                case SslPasswordMustChange:
                case SslDelegationPolicy:
                case SslPolicyNtlmOnly:
                case SslNoAuthenticatingAuthority:
                case SslCertExpired:
                case SslFreshCredRequired:
                case LicensingFailed:
                case LicensingTimeout:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Whether the RDP host should auto-retry this disconnect, including a first connect that has
        /// never succeeded (the reboot case).
        /// </summary>
        public static bool ShouldAutoRetry(int discReason, bool hasEverConnected, int retryCount, int maxRetryCount)
        {
            if (retryCount >= maxRetryCount)
                return false;
            if (IsAuthenticationFailure(discReason))
                return false;
            // A session that has already logged on retries any remaining disconnect (network drop, sleep,
            // the local-not-error that MSTSC fires when it gives up its own reconnect). A first connect
            // only retries transient "not ready yet" codes, so a wrong password does not loop.
            if (hasEverConnected)
                return true;
            return IsTransient(discReason);
        }

        /// <summary>Backoff after a failed attempt. <paramref name="retryCount"/> is 1 after the first failure.</summary>
        public static int RetryDelayMs(int retryCount)
        {
            if (retryCount < 1)
                retryCount = 1;
            // 1s, 2s, 4s, 8s, then stay at 8s
            var shift = retryCount - 1;
            if (shift > 3)
                shift = 3;
            return 1000 << shift;
        }
    }
}
