using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace _1RM.Utils.SshConfig
{
    /// <summary>
    /// One connectable <c>Host</c> block from an OpenSSH client config.
    /// </summary>
    public sealed class SshConfigEntry
    {
        /// <summary>The name after <c>Host</c>, which is what the user types after <c>ssh</c>.</summary>
        public string Alias { get; set; } = "";

        /// <summary>The <c>HostName</c>, or the alias when the block does not override it.</summary>
        public string HostName { get; set; } = "";

        public string User { get; set; } = "";
        public int Port { get; set; } = 22;
        public string IdentityFile { get; set; } = "";

        /// <summary>The single <c>ProxyJump</c> hop, when there is exactly one. Empty otherwise.</summary>
        public string ProxyJump { get; set; } = "";
    }

    /// <summary>
    /// Reads the subset of <c>~/.ssh/config</c> that maps onto a stored connection.
    ///
    /// Deliberately not a full implementation: <c>Match</c> blocks are conditional on things we cannot
    /// evaluate here, and the vast majority of the file is options about how to connect rather than what to
    /// connect to. Everything not understood is ignored rather than guessed at.
    /// </summary>
    public static class SshConfigParser
    {
        public static string DefaultConfigPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "config");

        public static List<SshConfigEntry> Parse(IEnumerable<string> lines)
        {
            var entries = new List<SshConfigEntry>();

            // The blocks currently being filled. One Host line can name several aliases, and every setting
            // that follows applies to all of them.
            var current = new List<SshConfigEntry>();

            // Set once per block per keyword: OpenSSH takes the first value it sees, not the last.
            var assigned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var inIgnoredSection = false;

            foreach (var raw in lines)
            {
                if (!TrySplit(raw, out var keyword, out var value)) continue;

                if (string.Equals(keyword, "Host", StringComparison.OrdinalIgnoreCase))
                {
                    inIgnoredSection = false;
                    current = StartBlock(value, entries);
                    assigned.Clear();
                    continue;
                }

                if (string.Equals(keyword, "Match", StringComparison.OrdinalIgnoreCase))
                {
                    // Conditional on the user, the network or a command's exit code. Anything inside would
                    // be a guess, so the section is skipped until the next Host line.
                    inIgnoredSection = true;
                    current = new List<SshConfigEntry>();
                    continue;
                }

                if (inIgnoredSection || current.Count == 0) continue;
                if (!assigned.Add(keyword)) continue;

                foreach (var entry in current)
                    Apply(entry, keyword, value);
            }

            // A block whose HostName was never given connects to the alias itself.
            foreach (var entry in entries.Where(x => x.HostName.Length == 0))
                entry.HostName = entry.Alias;

            return entries;
        }

        public static List<SshConfigEntry> ParseFile(string path) => Parse(File.ReadAllLines(path));

        private static List<SshConfigEntry> StartBlock(string patterns, List<SshConfigEntry> entries)
        {
            var started = new List<SshConfigEntry>();
            foreach (var pattern in patterns.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var alias = Unquote(pattern);
                // "Host *" and friends carry defaults for other blocks; there is no machine to connect to.
                if (alias.Length == 0 || alias.Contains('*') || alias.Contains('?') || alias.StartsWith("!", StringComparison.Ordinal))
                    continue;

                var entry = new SshConfigEntry { Alias = alias };
                entries.Add(entry);
                started.Add(entry);
            }
            return started;
        }

        private static void Apply(SshConfigEntry entry, string keyword, string value)
        {
            switch (keyword.ToLowerInvariant())
            {
                case "hostname":
                    entry.HostName = Unquote(value);
                    break;
                case "user":
                    entry.User = Unquote(value);
                    break;
                case "port":
                    if (int.TryParse(Unquote(value), out var port) && port > 0 && port <= 65535)
                        entry.Port = port;
                    break;
                case "identityfile":
                    entry.IdentityFile = ExpandHome(Unquote(value));
                    break;
                case "proxyjump":
                    var hops = Unquote(value).Split(',');
                    // Only a single hop maps onto one jump host; a chain would need tunnels through tunnels.
                    if (hops.Length == 1 && !string.Equals(hops[0].Trim(), "none", StringComparison.OrdinalIgnoreCase))
                        entry.ProxyJump = StripUserAndPort(hops[0].Trim());
                    break;
            }
        }

        /// <summary>
        /// Splits "Keyword value", "Keyword=value" and any amount of leading whitespace. Returns false for
        /// blank lines and comments.
        /// </summary>
        private static bool TrySplit(string raw, out string keyword, out string value)
        {
            keyword = "";
            value = "";

            var line = (raw ?? "").Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) return false;

            var separator = line.IndexOfAny(new[] { ' ', '\t', '=' });
            if (separator <= 0) return false;

            keyword = line.Substring(0, separator);
            value = line.Substring(separator + 1).TrimStart(' ', '\t', '=').Trim();
            return value.Length > 0;
        }

        private static string Unquote(string value)
        {
            var trimmed = value.Trim();
            if (trimmed.Length >= 2 && trimmed.StartsWith("\"", StringComparison.Ordinal) && trimmed.EndsWith("\"", StringComparison.Ordinal))
                return trimmed.Substring(1, trimmed.Length - 2);
            return trimmed;
        }

        /// <summary>A ProxyJump hop may carry its own user and port; the alias is what names the block.</summary>
        private static string StripUserAndPort(string hop)
        {
            var at = hop.LastIndexOf('@');
            if (at >= 0) hop = hop.Substring(at + 1);
            var colon = hop.LastIndexOf(':');
            if (colon > 0) hop = hop.Substring(0, colon);
            return hop.Trim();
        }

        private static string ExpandHome(string path)
        {
            if (!path.StartsWith("~", StringComparison.Ordinal)) return path;
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, path.TrimStart('~').TrimStart('/', '\\'));
        }
    }
}
