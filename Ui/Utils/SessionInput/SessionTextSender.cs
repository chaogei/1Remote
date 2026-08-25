using System;
using System.Runtime.InteropServices;
using _1RM.Utils.PuTTY;
using _1RM.View.Host.ProtocolHosts;
using Shawn.Utils;

namespace _1RM.Utils.SessionInput
{
    /// <summary>
    /// Types text into a running terminal session.
    ///
    /// The terminal is PuTTY running as its own process, reparented into our panel, so there is no stream to
    /// write to — the only way in is the one the keyboard uses. Posting WM_CHAR to its window is exactly
    /// what a keystroke delivers, which is why an external terminal can be driven at all.
    /// </summary>
    public static class SessionTextSender
    {
        private const uint WM_CHAR = 0x0102;

        /// <summary>Carriage return is what a terminal reads as Enter; a line feed alone does nothing.</summary>
        private const char ENTER = '\r';

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        /// <summary>
        /// Whether this session is a terminal we can type into. RDP and VNC hosts also have a window, but
        /// posting characters at them would go to whatever happens to be focused inside the remote desktop.
        /// </summary>
        public static bool CanSendTo(HostBase? host)
        {
            if (host?.ProtocolServer is not IPuttyConnectable) return false;
            var hwnd = SafeHandleOf(host);
            return hwnd != IntPtr.Zero && IsWindow(hwnd);
        }

        /// <summary>
        /// Normalises the line endings a terminal actually understands. Text pasted from an editor or a wiki
        /// carries CRLF or LF, and sending those raw produces either doubled prompts or nothing at all.
        /// </summary>
        public static string NormalizeNewLines(string? text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text!.Replace("\r\n", "\r").Replace('\n', ENTER);
        }

        /// <summary>
        /// Posts <paramref name="text"/> to the session one character at a time, optionally followed by
        /// Enter. Returns false when the session is not something that can be typed into.
        /// </summary>
        public static bool Send(HostBase? host, string? text, bool appendEnter)
        {
            if (!CanSendTo(host)) return false;

            var payload = NormalizeNewLines(text);
            if (appendEnter) payload += ENTER;
            if (payload.Length == 0) return true;

            var hwnd = SafeHandleOf(host!);
            foreach (var c in payload)
            {
                // One message per UTF-16 code unit, which is what the keyboard would produce; a surrogate
                // pair therefore arrives as its two halves, in order, exactly as PuTTY expects.
                if (!PostMessage(hwnd, WM_CHAR, (IntPtr)c, IntPtr.Zero))
                {
                    SimpleLogHelper.Warning($"SessionTextSender: posting to {hwnd} failed at '{c}'");
                    return false;
                }
            }

            return true;
        }

        private static IntPtr SafeHandleOf(HostBase host)
        {
            try
            {
                return host.GetHostHwnd();
            }
            catch (Exception e)
            {
                SimpleLogHelper.Warning($"SessionTextSender: could not get the session window, {e.Message}");
                return IntPtr.Zero;
            }
        }
    }
}
