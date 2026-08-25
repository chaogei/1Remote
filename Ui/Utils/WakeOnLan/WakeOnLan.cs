using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Shawn.Utils;

namespace _1RM.Utils.WakeOnLan
{
    /// <summary>
    /// Sends the AMD magic packet that brings a sleeping machine back up.
    ///
    /// The packet has no acknowledgement and no reply, so there is nothing to wait for and no way to tell
    /// whether it worked other than watching the host come back — which the reachability dot already does.
    /// </summary>
    public static class WakeOnLan
    {
        /// <summary>
        /// Both ports are sent to. 9 (discard) is the convention, but plenty of NICs and BIOS
        /// implementations only ever learned 7 (echo), and a duplicate packet costs nothing.
        /// </summary>
        private static readonly int[] Ports = { 9, 7 };

        private const int MAC_LENGTH = 6;

        /// <summary>
        /// Accepts every separator people actually paste: colons, hyphens, Cisco's dotted quads, or nothing
        /// at all. Anything that does not resolve to exactly six bytes is rejected.
        /// </summary>
        public static bool TryParseMac(string? text, out byte[] mac)
        {
            mac = Array.Empty<byte>();
            if (string.IsNullOrWhiteSpace(text)) return false;

            var hex = new string(text.Where(Uri.IsHexDigit).ToArray());
            if (hex.Length != MAC_LENGTH * 2) return false;

            // Reject separators we do not recognise rather than silently accepting "AA:BB:CC:DD:EE:FFxyz",
            // whose stripped form would otherwise look perfectly valid.
            if (text.Any(c => !Uri.IsHexDigit(c) && c != ':' && c != '-' && c != '.' && c != ' '))
                return false;

            var parsed = new byte[MAC_LENGTH];
            for (var i = 0; i < MAC_LENGTH; i++)
                parsed[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);

            mac = parsed;
            return true;
        }

        /// <summary>The canonical spelling, or the input unchanged when it is not a MAC.</summary>
        public static string Normalize(string? text) =>
            TryParseMac(text, out var mac) ? string.Join(":", mac.Select(b => b.ToString("X2"))) : (text ?? "").Trim();

        /// <summary>
        /// Six 0xFF bytes followed by the address repeated sixteen times, which is the whole specification.
        /// </summary>
        public static byte[] BuildMagicPacket(byte[] mac)
        {
            if (mac == null || mac.Length != MAC_LENGTH)
                throw new ArgumentException("a MAC address is six bytes", nameof(mac));

            var packet = new byte[6 + 16 * MAC_LENGTH];
            for (var i = 0; i < 6; i++)
                packet[i] = 0xFF;
            for (var repeat = 0; repeat < 16; repeat++)
                Buffer.BlockCopy(mac, 0, packet, 6 + repeat * MAC_LENGTH, MAC_LENGTH);
            return packet;
        }

        /// <summary>
        /// Broadcasts the packet. Returns how many datagrams left the machine, which is only a sign that
        /// something was sent — a woken host cannot confirm anything back.
        /// </summary>
        public static int Send(string? macText)
        {
            if (!TryParseMac(macText, out var mac))
                throw new ArgumentException($"'{macText}' is not a MAC address", nameof(macText));

            var packet = BuildMagicPacket(mac);
            var sent = 0;

            using var socket = new UdpClient { EnableBroadcast = true };
            foreach (var address in BroadcastTargets())
            {
                foreach (var port in Ports)
                {
                    try
                    {
                        socket.Send(packet, packet.Length, new IPEndPoint(address, port));
                        sent++;
                    }
                    catch (Exception e)
                    {
                        SimpleLogHelper.Warning($"WakeOnLan: {address}:{port} failed, {e.Message}");
                    }
                }
            }

            SimpleLogHelper.Info($"WakeOnLan: {Normalize(macText)} woken with {sent} datagram(s)");
            return sent;
        }

        /// <summary>
        /// 255.255.255.255 plus the directed broadcast of every IPv4 subnet this machine sits on.
        ///
        /// The limited broadcast alone is not enough in practice: it is not forwarded, and on a host with
        /// several adapters the OS picks one route for it. Addressing each subnet explicitly is what makes
        /// this work on a machine with a VPN or a second NIC attached.
        /// </summary>
        private static IEnumerable<IPAddress> BroadcastTargets()
        {
            var targets = new List<IPAddress> { IPAddress.Broadcast };

            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    foreach (var unicast in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        if (unicast.IPv4Mask == null) continue;

                        var directed = DirectedBroadcast(unicast.Address, unicast.IPv4Mask);
                        if (directed != null && !targets.Contains(directed))
                            targets.Add(directed);
                    }
                }
            }
            catch (Exception e)
            {
                SimpleLogHelper.Warning($"WakeOnLan: could not enumerate adapters, {e.Message}");
            }

            return targets;
        }

        private static IPAddress? DirectedBroadcast(IPAddress address, IPAddress mask)
        {
            var a = address.GetAddressBytes();
            var m = mask.GetAddressBytes();
            if (a.Length != 4 || m.Length != 4) return null;

            var broadcast = new byte[4];
            for (var i = 0; i < 4; i++)
            {
                // ~m[i] promotes to int and goes negative, and this assembly is compiled with
                // CheckForOverflowUnderflow, so the cast would throw without masking back to a byte first.
                broadcast[i] = (byte)((a[i] | ~m[i]) & 0xFF);
            }
            return new IPAddress(broadcast);
        }
    }
}
