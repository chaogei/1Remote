using System;
using System.Collections.Generic;
using System.Linq;

namespace _1RM.Utils.PuTTY
{
    /// <summary>
    /// Turns the forwarding rules a person writes into the single packed string PuTTY expects.
    ///
    /// PuTTY stores them as one comma-separated value like <c>L8080=intranet:80,D1080</c>, which is fine for
    /// a registry key and hostile to type into a text box. This accepts a readable line-per-rule form and
    /// also passes PuTTY's own syntax through unchanged, so a rule copied out of PuTTY still works.
    /// </summary>
    public static class SshPortForwardingRules
    {
        /// <summary>
        /// Accepted per line, case-insensitive, with either spaces or PuTTY's punctuation:
        ///   L 8080 intranet:80     forward local 8080 to intranet:80 through the server
        ///   R 9000 localhost:9000  forward the server's 9000 back to localhost:9000
        ///   D 1080                 a SOCKS proxy on local 1080
        /// Lines starting with # are comments. Anything unparseable is skipped rather than failing the
        /// connection - a typo in one rule should not stop the session opening.
        /// </summary>
        public static string ToPuttyValue(string? userText)
        {
            if (string.IsNullOrWhiteSpace(userText)) return "";

            var rules = new List<string>();
            foreach (var raw in userText!.Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var rule = ParseLine(raw);
                if (rule != null && !rules.Contains(rule, StringComparer.OrdinalIgnoreCase))
                    rules.Add(rule);
            }
            return string.Join(",", rules);
        }

        private static string? ParseLine(string raw)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) return null;

            var kind = char.ToUpperInvariant(line[0]);
            if (kind != 'L' && kind != 'R' && kind != 'D') return null;

            // everything after the direction letter, with the separators people actually type
            var rest = line.Substring(1).TrimStart('=', ' ', '\t');
            var parts = rest.Split(new[] { ' ', '\t', '=' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return null;

            var sourcePort = parts[0].Trim();
            if (!IsPort(sourcePort)) return null;

            if (kind == 'D')
                return $"D{sourcePort}"; // a SOCKS proxy has no destination

            if (parts.Length < 2) return null;
            var destination = string.Concat(parts.Skip(1)).Trim();
            var colon = destination.LastIndexOf(':');
            if (colon <= 0 || !IsPort(destination.Substring(colon + 1))) return null;

            return $"{kind}{sourcePort}={destination}";
        }

        private static bool IsPort(string value)
        {
            return int.TryParse(value, out var port) && port > 0 && port <= 65535;
        }
    }
}
