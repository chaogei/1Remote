using System;
using System.Diagnostics;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;

namespace _1RM.Utils.Rdp
{
    /// <summary>The identity an RDP server presented, as seen by <see cref="RdpCertificateProbe"/>.</summary>
    public sealed class RdpServerCertificate
    {
        public RdpServerCertificate(byte[] rawData, string subject)
        {
            RawData = rawData;
            Subject = subject;
        }

        /// <summary>DER bytes of the server certificate, to be fingerprinted.</summary>
        public byte[] RawData { get; }

        /// <summary>Distinguished name, e.g. "CN=WIN-SRV01". Shown beside the fingerprint so the user has something to recognise.</summary>
        public string Subject { get; }
    }

    /// <summary>
    /// Reads the certificate an RDP server presents, so the app can verify the host identity itself rather
    /// than leaving it to the ActiveX control.
    ///
    /// Windows remembers an accepted certificate under
    /// HKCU\Software\Microsoft\Terminal Server Client\Servers\&lt;address&gt;\CertHash, keyed by address alone.
    /// Port forwarding — one hostname with a port per machine, which is how frp and most NAT setups look —
    /// puts several unrelated hosts on that single value, so each one overwrites the last and the warning
    /// comes back on every switch, no matter how often "don't ask me again" is ticked. Verifying here keys
    /// the trust on address *and* port, and shows the fingerprint instead of hiding it behind a checkbox.
    ///
    /// [MS-RDPBCGR] 2.2.1.1: a connection opens with an X.224 Connection Request listing the security
    /// protocols the client speaks. If the server answers TLS or CredSSP, the TLS handshake follows
    /// immediately, which puts the certificate on the table before any credential leaves this machine.
    /// </summary>
    public static class RdpCertificateProbe
    {
        /// <summary>Legacy RDP security layer: no TLS, so no certificate to pin.</summary>
        public const uint ProtocolRdp = 0x00000000;
        public const uint ProtocolSsl = 0x00000001;
        public const uint ProtocolHybrid = 0x00000002;

        private const int TpktHeaderLength = 4;
        private const int X224HeaderLength = 7;
        private const int NegotiationLength = 8;
        private const int ConnectionRequestLength = TpktHeaderLength + X224HeaderLength + NegotiationLength;

        private const byte TpktVersion = 0x03;
        private const byte X224ConnectionRequest = 0xE0;
        private const byte X224ConnectionConfirm = 0xD0;
        private const byte NegotiationResponse = 0x02;
        private const byte NegotiationFailure = 0x03;

        public static byte[] BuildConnectionRequest(uint requestedProtocols = ProtocolSsl | ProtocolHybrid)
        {
            var pdu = new byte[ConnectionRequestLength];

            pdu[0] = TpktVersion;
            pdu[1] = 0x00;
            pdu[2] = 0x00;
            pdu[3] = (byte)ConnectionRequestLength;

            // X.224: length indicator (everything after itself), CR TPDU, dst-ref, src-ref, class 0
            pdu[4] = (byte)(ConnectionRequestLength - 5);
            pdu[5] = X224ConnectionRequest;

            pdu[11] = 0x01; // TYPE_RDP_NEG_REQ
            pdu[12] = 0x00; // flags
            pdu[13] = NegotiationLength;
            pdu[14] = 0x00;
            pdu[15] = (byte)(requestedProtocols & 0xFF);
            pdu[16] = (byte)((requestedProtocols >> 8) & 0xFF);
            pdu[17] = (byte)((requestedProtocols >> 16) & 0xFF);
            pdu[18] = (byte)((requestedProtocols >> 24) & 0xFF);
            return pdu;
        }

        /// <summary>
        /// Reads the server's answer to <see cref="BuildConnectionRequest"/>. False means no TLS handshake
        /// will follow: the server refused every protocol offered, or it answered without a negotiation
        /// structure at all, which is the legacy security layer where there is no certificate to verify.
        /// </summary>
        public static bool TryParseConnectionConfirm(byte[]? buffer, int length, out uint selectedProtocol, out uint failureCode)
        {
            selectedProtocol = ProtocolRdp;
            failureCode = 0;

            if (buffer == null || length < TpktHeaderLength + X224HeaderLength)
                return false;
            if (buffer[0] != TpktVersion)
                return false;

            var declared = (buffer[2] << 8) | buffer[3];
            if (declared > length)
                return false;
            if (buffer[5] != X224ConnectionConfirm)
                return false;
            if (declared < ConnectionRequestLength)
                return false;

            const int neg = TpktHeaderLength + X224HeaderLength;
            var value = buffer[neg + 4]
                        | ((uint)buffer[neg + 5] << 8)
                        | ((uint)buffer[neg + 6] << 16)
                        | ((uint)buffer[neg + 7] << 24);
            switch (buffer[neg])
            {
                case NegotiationResponse:
                    selectedProtocol = value;
                    return selectedProtocol != ProtocolRdp;
                case NegotiationFailure:
                    failureCode = value;
                    return false;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Negotiates far enough to complete the TLS handshake and returns the certificate the server
        /// presented. Null when there is nothing to verify — legacy security layer, or a server that
        /// refused the negotiation — in which case the caller has to leave the decision to the control.
        ///
        /// Nothing but the negotiation is sent, so probing exposes no credential. Because the handshake is
        /// completed rather than the certificate merely collected, a server can only pass this by holding
        /// the private key for the certificate it presents.
        /// </summary>
        public static async Task<RdpServerCertificate?> TryGetCertificateAsync(string host, int port, int timeoutMs, CancellationToken ct)
        {
            // One budget for the whole probe rather than one per step: a session should never wait for the
            // sum of a connect, a negotiation and a handshake that each stalled.
            var clock = Stopwatch.StartNew();
            int Remaining() => Math.Max(1, timeoutMs - (int)clock.ElapsedMilliseconds);

            using var client = new TcpClient { NoDelay = true };
            await WaitOrTimeoutAsync(client.ConnectAsync(host, port), Remaining(), ct, $"no answer from {host}:{port}").ConfigureAwait(false);

            var stream = client.GetStream();
            var request = BuildConnectionRequest();
            await WaitOrTimeoutAsync(stream.WriteAsync(request, 0, request.Length, ct), Remaining(), ct, $"{host}:{port} did not accept the negotiation")
                .ConfigureAwait(false);

            var buffer = new byte[64];
            var read = await ReadConnectionConfirmAsync(stream, buffer, Remaining, ct).ConfigureAwait(false);
            if (!TryParseConnectionConfirm(buffer, read, out _, out var failureCode))
            {
                if (failureCode != 0)
                    throw new AuthenticationException($"{host}:{port} refused the security negotiation, code {failureCode}");
                return null;
            }

            byte[]? rawData = null;
            var subject = "";
            using var ssl = new SslStream(stream, leaveInnerStreamOpen: true, (_, certificate, _, _) =>
            {
                // Collecting the identity, not judging it: whether it may be used is the trust store's call.
                if (certificate != null)
                {
                    rawData = certificate.GetRawCertData();
                    subject = certificate.Subject;
                }
                return true;
            });

            await WaitOrTimeoutAsync(
                    ssl.AuthenticateAsClientAsync(host, null, SslProtocols.None, checkCertificateRevocation: false),
                    Remaining(), ct, $"{host}:{port} did not finish the TLS handshake")
                .ConfigureAwait(false);

            return rawData == null ? null : new RdpServerCertificate(rawData, subject);
        }

        private static async Task<int> ReadConnectionConfirmAsync(NetworkStream stream, byte[] buffer, Func<int> remainingMs, CancellationToken ct)
        {
            var read = 0;
            while (read < TpktHeaderLength)
            {
                var n = await ReadAsync(stream, buffer, read, remainingMs(), ct).ConfigureAwait(false);
                if (n <= 0)
                    return read;
                read += n;
            }

            var declared = Math.Min((buffer[2] << 8) | buffer[3], buffer.Length);
            while (read < declared)
            {
                var n = await ReadAsync(stream, buffer, read, remainingMs(), ct).ConfigureAwait(false);
                if (n <= 0)
                    break;
                read += n;
            }
            return read;
        }

        private static Task<int> ReadAsync(NetworkStream stream, byte[] buffer, int offset, int timeoutMs, CancellationToken ct)
        {
            return WaitOrTimeoutAsync(stream.ReadAsync(buffer, offset, buffer.Length - offset, ct),
                timeoutMs, ct, "the server stopped answering mid-negotiation");
        }

        private static async Task<T> WaitOrTimeoutAsync<T>(Task<T> work, int timeoutMs, CancellationToken ct, string what)
        {
            await WaitOrTimeoutAsync((Task)work, timeoutMs, ct, what).ConfigureAwait(false);
            return await work.ConfigureAwait(false);
        }

        /// <summary>
        /// Bounds an operation that has no timeout of its own. Async socket and handshake calls ignore the
        /// stream timeouts, so without this a black-holed endpoint would hold the connection attempt open.
        /// </summary>
        private static async Task WaitOrTimeoutAsync(Task work, int timeoutMs, CancellationToken ct, string what)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var delay = Task.Delay(timeoutMs, timeout.Token);
            if (await Task.WhenAny(work, delay).ConfigureAwait(false) != work)
            {
                ct.ThrowIfCancellationRequested();
                // The socket is disposed on the way out, which is what ends the abandoned operation; observe
                // its failure here so it does not surface as an unobserved task exception.
                _ = work.ContinueWith(t => { _ = t.Exception; },
                    CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                throw new TimeoutException(what);
            }

            timeout.Cancel(); // stop the timer rather than leave it running for the rest of the timeout
            await work.ConfigureAwait(false);
        }
    }
}
