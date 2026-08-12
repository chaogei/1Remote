using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace _1RM.Utils.Proxy
{
    /// <summary>
    /// Turns an already-connected stream to a proxy server into a transparent tunnel to
    /// <c>targetHost:targetPort</c>. Pure protocol work — no sockets, no threads, no app state — so it can
    /// be unit tested against a MemoryStream and reused outside this project.
    ///
    /// Every method is synchronous by design: the caller sets a read timeout on the stream, which only
    /// applies to synchronous reads on NetworkStream.
    /// </summary>
    internal static class ProxyHandshake
    {
        private const int MAX_HTTP_HEADER_BYTES = 8 * 1024;

        public static void Perform(Stream stream, EProxyType type, string targetHost, int targetPort, string userName, string password)
        {
            if (string.IsNullOrWhiteSpace(targetHost))
                throw new ArgumentException("target host is empty", nameof(targetHost));
            if (targetPort <= 0 || targetPort > 65535)
                throw new ArgumentOutOfRangeException(nameof(targetPort), targetPort, "target port out of range");

            switch (type)
            {
                case EProxyType.Socks5:
                    Socks5(stream, targetHost, targetPort, userName, password);
                    break;
                case EProxyType.Socks4:
                    Socks4(stream, targetHost, targetPort, userName, allowRemoteDns: false);
                    break;
                case EProxyType.Socks4A:
                    Socks4(stream, targetHost, targetPort, userName, allowRemoteDns: true);
                    break;
                case EProxyType.Http:
                    HttpConnect(stream, targetHost, targetPort, userName, password);
                    break;
                default:
                    throw new NotSupportedException($"proxy type {type} is not supported");
            }
        }

        #region SOCKS5 (RFC 1928 / RFC 1929)

        private static void Socks5(Stream stream, string host, int port, string userName, string password)
        {
            var useAuth = !string.IsNullOrEmpty(userName);

            // greeting: version, method count, methods
            Write(stream, useAuth
                ? new byte[] { 0x05, 0x02, 0x00, 0x02 }
                : new byte[] { 0x05, 0x01, 0x00 });

            var selection = ReadExactly(stream, 2);
            if (selection[0] != 0x05)
                throw new IOException($"SOCKS5: unexpected version 0x{selection[0]:X2} in the method selection reply");

            switch (selection[1])
            {
                case 0x00: // no authentication
                    break;
                case 0x02: // username / password
                    if (!useAuth)
                        throw new IOException("SOCKS5: the proxy requires credentials but none are configured");
                    Socks5Authenticate(stream, userName, password);
                    break;
                case 0xFF:
                    throw new IOException(useAuth
                        ? "SOCKS5: the proxy rejected both anonymous and username/password authentication"
                        : "SOCKS5: the proxy rejected anonymous access, configure a username and password");
                default:
                    throw new IOException($"SOCKS5: the proxy selected unsupported authentication method 0x{selection[1]:X2}");
            }

            // connect request: version, CONNECT, reserved, address, port
            var request = new List<byte> { 0x05, 0x01, 0x00 };
            if (IPAddress.TryParse(host, out var ip))
            {
                request.Add(ip.AddressFamily == AddressFamily.InterNetworkV6 ? (byte)0x04 : (byte)0x01);
                request.AddRange(ip.GetAddressBytes());
            }
            else
            {
                var hostBytes = Encoding.UTF8.GetBytes(host);
                if (hostBytes.Length > 255)
                    throw new IOException($"SOCKS5: host name '{host}' is longer than 255 bytes");
                request.Add(0x03);
                request.Add((byte)hostBytes.Length);
                request.AddRange(hostBytes);
            }
            request.Add((byte)(port >> 8));
            request.Add((byte)(port & 0xFF));
            Write(stream, request.ToArray());

            var reply = ReadExactly(stream, 4);
            if (reply[0] != 0x05)
                throw new IOException($"SOCKS5: unexpected version 0x{reply[0]:X2} in the connect reply");
            if (reply[1] != 0x00)
                throw new IOException($"SOCKS5: the proxy refused to reach {host}:{port} — {DescribeSocks5Reply(reply[1])}");

            // drain the bound address so the stream is positioned exactly at the tunnelled payload
            var boundAddressLength = reply[3] switch
            {
                0x01 => 4,
                0x04 => 16,
                0x03 => ReadExactly(stream, 1)[0],
                _ => throw new IOException($"SOCKS5: unknown address type 0x{reply[3]:X2} in the connect reply")
            };
            ReadExactly(stream, boundAddressLength + 2);
        }

        private static void Socks5Authenticate(Stream stream, string userName, string password)
        {
            var user = Encoding.UTF8.GetBytes(userName);
            var pass = Encoding.UTF8.GetBytes(password ?? "");
            if (user.Length > 255)
                throw new IOException("SOCKS5: the user name is longer than 255 bytes");
            if (pass.Length > 255)
                throw new IOException("SOCKS5: the password is longer than 255 bytes");

            var request = new byte[3 + user.Length + pass.Length];
            request[0] = 0x01; // sub-negotiation version
            request[1] = (byte)user.Length;
            Buffer.BlockCopy(user, 0, request, 2, user.Length);
            request[2 + user.Length] = (byte)pass.Length;
            Buffer.BlockCopy(pass, 0, request, 3 + user.Length, pass.Length);
            Write(stream, request);

            var reply = ReadExactly(stream, 2);
            if (reply[1] != 0x00)
                throw new IOException("SOCKS5: the proxy rejected the user name or password");
        }

        private static string DescribeSocks5Reply(byte code) => code switch
        {
            0x01 => "general SOCKS server failure",
            0x02 => "connection not allowed by ruleset",
            0x03 => "network unreachable",
            0x04 => "host unreachable",
            0x05 => "connection refused",
            0x06 => "TTL expired",
            0x07 => "command not supported",
            0x08 => "address type not supported",
            _ => $"unknown error 0x{code:X2}"
        };

        #endregion

        #region SOCKS4 / SOCKS4a

        private static void Socks4(Stream stream, string host, int port, string userName, bool allowRemoteDns)
        {
            byte[] address;
            byte[]? remoteHostName = null;

            if (IPAddress.TryParse(host, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork)
            {
                address = ip.GetAddressBytes();
            }
            else if (allowRemoteDns)
            {
                // 0.0.0.x with x != 0 tells a SOCKS4a proxy that a host name follows the user id
                address = new byte[] { 0x00, 0x00, 0x00, 0x01 };
                remoteHostName = Encoding.UTF8.GetBytes(host);
                if (remoteHostName.Length > 255)
                    throw new IOException($"SOCKS4a: host name '{host}' is longer than 255 bytes");
            }
            else
            {
                // plain SOCKS4 has no name resolution, and no IPv6 either
                var resolved = Dns.GetHostAddresses(host).FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork)
                               ?? throw new IOException($"SOCKS4: '{host}' has no IPv4 address, use SOCKS4a or SOCKS5 instead");
                address = resolved.GetAddressBytes();
            }

            var request = new List<byte> { 0x04, 0x01, (byte)(port >> 8), (byte)(port & 0xFF) };
            request.AddRange(address);
            request.AddRange(Encoding.UTF8.GetBytes(userName ?? ""));
            request.Add(0x00);
            if (remoteHostName != null)
            {
                request.AddRange(remoteHostName);
                request.Add(0x00);
            }
            Write(stream, request.ToArray());

            var reply = ReadExactly(stream, 8);
            if (reply[1] != 0x5A)
                throw new IOException($"SOCKS4: the proxy refused to reach {host}:{port} — {DescribeSocks4Reply(reply[1])}");
        }

        private static string DescribeSocks4Reply(byte code) => code switch
        {
            0x5B => "request rejected or failed",
            0x5C => "the proxy could not reach the identd service on the client",
            0x5D => "the client's identd reported a different user id",
            _ => $"unknown status 0x{code:X2}"
        };

        #endregion

        #region HTTP CONNECT

        private static void HttpConnect(Stream stream, string host, int port, string userName, string password)
        {
            // A bracketed literal is required for IPv6 in the request target
            var authority = IPAddress.TryParse(host, out var ip) && ip.AddressFamily == AddressFamily.InterNetworkV6
                ? $"[{host}]:{port}"
                : $"{host}:{port}";

            var request = new StringBuilder();
            request.Append($"CONNECT {authority} HTTP/1.1\r\n");
            request.Append($"Host: {authority}\r\n");
            if (!string.IsNullOrEmpty(userName))
            {
                var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{userName}:{password}"));
                request.Append($"Proxy-Authorization: Basic {token}\r\n");
            }
            request.Append("Proxy-Connection: Keep-Alive\r\n");
            request.Append("\r\n");
            Write(stream, Encoding.ASCII.GetBytes(request.ToString()));

            var statusLine = ReadHttpHead(stream).Split('\n').FirstOrDefault()?.Trim() ?? "";
            var parts = statusLine.Split(' ');
            if (parts.Length < 2 || !int.TryParse(parts[1], out var status))
                throw new IOException($"HTTP proxy: malformed CONNECT response '{statusLine}'");
            if (status == 407)
                throw new IOException("HTTP proxy: authentication required or rejected (407)");
            if (status is < 200 or > 299)
                throw new IOException($"HTTP proxy: refused to reach {host}:{port} — '{statusLine}'");
        }

        /// <summary>
        /// Reads one byte at a time up to the terminating blank line. Buffering would swallow the first
        /// bytes of the tunnelled payload, which the caller has no way to push back.
        /// </summary>
        private static string ReadHttpHead(Stream stream)
        {
            var head = new StringBuilder();
            var one = new byte[1];
            while (true)
            {
                if (stream.Read(one, 0, 1) <= 0)
                    throw new IOException("HTTP proxy: the connection closed during CONNECT");
                head.Append((char)one[0]);
                var n = head.Length;
                if (n >= 4 && head[n - 4] == '\r' && head[n - 3] == '\n' && head[n - 2] == '\r' && head[n - 1] == '\n')
                    return head.ToString();
                if (n > MAX_HTTP_HEADER_BYTES)
                    throw new IOException("HTTP proxy: the CONNECT response header is unreasonably large");
            }
        }

        #endregion

        private static void Write(Stream stream, byte[] payload)
        {
            stream.Write(payload, 0, payload.Length);
            stream.Flush();
        }

        private static byte[] ReadExactly(Stream stream, int count)
        {
            var buffer = new byte[count];
            var offset = 0;
            while (offset < count)
            {
                var read = stream.Read(buffer, offset, count - offset);
                if (read <= 0)
                    throw new IOException("the proxy closed the connection during the handshake");
                offset += read;
            }
            return buffer;
        }
    }
}
