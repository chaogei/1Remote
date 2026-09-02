using System.Text;
using _1RM.Utils.Rdp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.Utils.Rdp
{
    /// <summary>
    /// The X.224 negotiation in front of the TLS handshake decides whether there is a certificate to verify
    /// at all. Misreading it would either skip verification without saying so, or refuse servers that never
    /// offered TLS in the first place.
    /// </summary>
    [TestClass]
    public class RdpCertificateProbeTests
    {
        [TestMethod]
        public void TheConnectionRequestOffersTlsAndCredSsp()
        {
            // [MS-RDPBCGR] 2.2.1.1: TPKT(4) + X.224 CR(7) + RDP_NEG_REQ(8), asking for SSL | HYBRID.
            CollectionAssert.AreEqual(
                new byte[]
                {
                    0x03, 0x00, 0x00, 0x13,
                    0x0E, 0xE0, 0x00, 0x00, 0x00, 0x00, 0x00,
                    0x01, 0x00, 0x08, 0x00, 0x03, 0x00, 0x00, 0x00,
                },
                RdpCertificateProbe.BuildConnectionRequest());
        }

        [TestMethod]
        public void AServerThatPicksTlsIsWorthProbing()
        {
            var response = ConnectionConfirm(0x02, RdpCertificateProbe.ProtocolSsl);

            Assert.IsTrue(RdpCertificateProbe.TryParseConnectionConfirm(response, response.Length, out var selected, out var failure));
            Assert.AreEqual(RdpCertificateProbe.ProtocolSsl, selected);
            Assert.AreEqual(0u, failure);
        }

        [TestMethod]
        public void AServerThatPicksCredSspIsWorthProbingToo()
        {
            var response = ConnectionConfirm(0x02, RdpCertificateProbe.ProtocolHybrid);

            Assert.IsTrue(RdpCertificateProbe.TryParseConnectionConfirm(response, response.Length, out var selected, out _));
            Assert.AreEqual(RdpCertificateProbe.ProtocolHybrid, selected);
        }

        [TestMethod]
        public void TheLegacySecurityLayerHasNoCertificateToVerify()
        {
            // Selecting protocol 0 means the connection carries on without TLS. There is nothing to pin, so
            // the session has to be left to the control rather than refused.
            var response = ConnectionConfirm(0x02, RdpCertificateProbe.ProtocolRdp);

            Assert.IsFalse(RdpCertificateProbe.TryParseConnectionConfirm(response, response.Length, out _, out var failure));
            Assert.AreEqual(0u, failure);
        }

        [TestMethod]
        public void AConfirmWithoutANegotiationStructureIsNotTreatedAsTls()
        {
            // What a pre-NLA server answers: the bare 11-byte X.224 confirm, no RDP_NEG_RSP behind it.
            var response = new byte[] { 0x03, 0x00, 0x00, 0x0B, 0x06, 0xD0, 0x00, 0x00, 0x12, 0x34, 0x00 };

            Assert.IsFalse(RdpCertificateProbe.TryParseConnectionConfirm(response, response.Length, out _, out var failure));
            Assert.AreEqual(0u, failure);
        }

        [TestMethod]
        public void ARefusedNegotiationSurfacesTheServerCode()
        {
            // 0x02 = SSL_NOT_ALLOWED_BY_SERVER. Worth reporting: it is the one refusal a user can act on.
            var response = ConnectionConfirm(0x03, 0x00000002);

            Assert.IsFalse(RdpCertificateProbe.TryParseConnectionConfirm(response, response.Length, out _, out var failure));
            Assert.AreEqual(2u, failure);
        }

        [TestMethod]
        public void SomethingOtherThanRdpOnThePortIsNotMistakenForANegotiation()
        {
            var http = Encoding.ASCII.GetBytes("HTTP/1.1 400 Bad Request\r\n\r\n");

            Assert.IsFalse(RdpCertificateProbe.TryParseConnectionConfirm(http, http.Length, out _, out _));
            Assert.IsFalse(RdpCertificateProbe.TryParseConnectionConfirm(null, 0, out _, out _));
            Assert.IsFalse(RdpCertificateProbe.TryParseConnectionConfirm(new byte[] { 0x03, 0x00, 0x00, 0x13 }, 4, out _, out _));
        }

        [TestMethod]
        public void AnAnswerThatStoppedShortIsNotReadFromTheRestOfTheBuffer()
        {
            // The read buffer outlives each read. Trusting its size rather than how much actually arrived
            // would let a half-received answer be parsed out of whatever was already there.
            var response = ConnectionConfirm(0x02, RdpCertificateProbe.ProtocolHybrid);

            Assert.IsFalse(RdpCertificateProbe.TryParseConnectionConfirm(response, response.Length - 1, out _, out _));
        }

        private static byte[] ConnectionConfirm(byte negotiationType, uint value)
        {
            return new byte[]
            {
                0x03, 0x00, 0x00, 0x13,
                0x0E, 0xD0, 0x00, 0x00, 0x12, 0x34, 0x00,
                negotiationType, 0x00, 0x08, 0x00,
                (byte)(value & 0xFF), (byte)((value >> 8) & 0xFF), (byte)((value >> 16) & 0xFF), (byte)((value >> 24) & 0xFF),
            };
        }
    }
}
