using System;
using System.IO;
using System.Linq;

namespace _1RM.Utils.SessionRecording
{
    /// <summary>
    /// Names the file a terminal session is recorded into.
    ///
    /// The recording itself is PuTTY's own <c>-sessionlog</c>: it owns the terminal and already writes
    /// everything it renders, so intercepting the stream from outside would only produce a worse copy of a
    /// file it will write for free.
    /// </summary>
    public static class SessionLogPath
    {
        /// <summary>
        /// Milliseconds are in the stamp on purpose. Two tabs onto the same server open within the same
        /// second often enough, and PuTTY would otherwise stop to ask whether to overwrite the log.
        /// </summary>
        private const string TIMESTAMP_FORMAT = "yyyyMMdd_HHmmss_fff";

        public static string Build(string folder, string sessionName, DateTime now)
        {
            var safeName = Sanitize(sessionName);
            if (safeName.Length == 0) safeName = "session";
            return Path.Combine(folder, $"{safeName}_{now.ToString(TIMESTAMP_FORMAT)}.log");
        }

        /// <summary>
        /// Strips what a file name cannot hold and clips the length, so a server named after a long
        /// description or a URL still produces a path Windows will accept.
        /// </summary>
        public static string Sanitize(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";

            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(name!.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray());

            // Also collapse the characters that are legal but make a log folder unpleasant to work in.
            cleaned = cleaned.Replace(' ', '_');

            return cleaned.Length > 48 ? cleaned.Substring(0, 48).TrimEnd('_', '.') : cleaned.TrimEnd('_', '.');
        }
    }
}
