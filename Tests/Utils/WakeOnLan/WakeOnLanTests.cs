using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Wol = _1RM.Utils.WakeOnLan.WakeOnLan;

namespace Tests.Utils.WakeOnLan
{
    [TestClass]
    public class WakeOnLanTests
    {
        private static readonly byte[] Expected = { 0x00, 0x1A, 0x2B, 0x3C, 0x4D, 0x5E };

        [DataTestMethod]
        [DataRow("00:1A:2B:3C:4D:5E")]
        [DataRow("00-1A-2B-3C-4D-5E")]
        [DataRow("001A2B3C4D5E")]
        [DataRow("001a.2b3c.4d5e")]
        [DataRow("00 1A 2B 3C 4D 5E")]
        [DataRow("  00:1a:2b:3c:4d:5e  ")]
        public void EverySeparatorPeopleActuallyPasteIsAccepted(string text)
        {
            Assert.IsTrue(Wol.TryParseMac(text, out var mac), $"'{text}' should parse");
            CollectionAssert.AreEqual(Expected, mac);
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow(null)]
        [DataRow("00:1A:2B:3C:4D")]           // five bytes
        [DataRow("00:1A:2B:3C:4D:5E:6F")]     // seven
        [DataRow("00:1A:2B:3C:4D:GG")]        // not hex
        [DataRow("00:1A:2B:3C:4D:5Exyz")]     // trailing junk that strips down to a valid length
        public void AnythingThatIsNotSixBytesIsRejected(string? text)
        {
            Assert.IsFalse(Wol.TryParseMac(text, out _), $"'{text}' should not parse");
        }

        [TestMethod]
        public void NormalizeProducesTheCanonicalSpelling()
        {
            Assert.AreEqual("00:1A:2B:3C:4D:5E", Wol.Normalize("001a2b3c4d5e"));
        }

        [TestMethod]
        public void NormalizeLeavesSomethingItCannotReadAlone()
        {
            Assert.AreEqual("not a mac", Wol.Normalize("  not a mac  "));
        }

        [TestMethod]
        public void TheMagicPacketIsSixFfBytesThenTheAddressSixteenTimes()
        {
            var packet = Wol.BuildMagicPacket(Expected);

            Assert.AreEqual(102, packet.Length, "6 + 16 * 6");
            Assert.IsTrue(packet.Take(6).All(b => b == 0xFF), "the header is six 0xFF bytes");

            for (var repeat = 0; repeat < 16; repeat++)
            {
                var slice = packet.Skip(6 + repeat * 6).Take(6).ToArray();
                CollectionAssert.AreEqual(Expected, slice, $"repetition {repeat} does not match");
            }
        }

        [TestMethod]
        public void BuildingAPacketFromSomethingOtherThanSixBytesIsRefused()
        {
            Assert.ThrowsException<ArgumentException>(() => Wol.BuildMagicPacket(new byte[] { 1, 2, 3 }));
        }

        [TestMethod]
        public void SendingWithoutAUsableAddressIsRefusedRatherThanBroadcastingNonsense()
        {
            Assert.ThrowsException<ArgumentException>(() => Wol.Send("not a mac"));
        }

        [TestMethod]
        public void BroadcastingReachesAtLeastTheLimitedBroadcastAddress()
        {
            // Computing a subnet's directed broadcast inverts the mask, which goes negative as an int and
            // would throw on the cast back to a byte under this assembly's checked arithmetic. Actually
            // sending is what exercises that path.
            var sent = Wol.Send("00:1A:2B:3C:4D:5E");

            Assert.IsTrue(sent >= 2, "255.255.255.255 on both ports at minimum");
        }
    }
}
