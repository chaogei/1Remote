using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Shawn.Utils;

namespace _1RM.Utils.ExternalSecret
{
    /// <summary>
    /// Fetches a secret by running a command and taking what it prints.
    ///
    /// Every password manager worth integrating with already ships a CLI that does exactly this — Bitwarden's
    /// <c>bw get password</c>, <c>keepassxc-cli show -a Password</c>, <c>pass show</c>, <c>op read</c> — so
    /// one implementation covers all of them, and any future one, instead of a provider per vendor that has
    /// to be written and maintained separately.
    /// </summary>
    public static class ExternalSecretResolver
    {
        /// <summary>
        /// Marks a stored value as a command to run rather than the secret itself. Chosen to be something
        /// nobody would type as an actual password by accident.
        /// </summary>
        public const string PREFIX = "cmd://";

        private const int TIMEOUT_MS = 20 * 1000;

        /// <summary>
        /// Resolved secrets for this run of the app, keyed by the command.
        ///
        /// Without it, a vault that prompts for a fingerprint or a master password would do so once per
        /// field per connection — and a server with a password and a key passphrase would ask twice before
        /// it even started connecting.
        /// </summary>
        private static readonly ConcurrentDictionary<string, string> Cache = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        public static bool IsReference(string? value) =>
            value?.StartsWith(PREFIX, StringComparison.OrdinalIgnoreCase) == true;

        /// <summary>The command part of a reference, without the marker.</summary>
        public static string CommandOf(string? value) =>
            IsReference(value) ? value!.Substring(PREFIX.Length).Trim() : "";

        /// <summary>
        /// Returns the secret for a reference, or the value unchanged when it is not one. Never throws:
        /// a vault that is locked or missing must surface as a failed login, not as a crash on the connect
        /// path, which runs for every protocol.
        /// </summary>
        public static string Resolve(string? value)
        {
            if (!IsReference(value)) return value ?? "";

            var command = CommandOf(value);
            if (command.Length == 0) return "";

            if (Cache.TryGetValue(command, out var cached))
                return cached;

            try
            {
                var secret = Run(command);
                if (secret.Length > 0)
                    Cache[command] = secret;
                return secret;
            }
            catch (Exception e)
            {
                SimpleLogHelper.Warning($"ExternalSecretResolver: '{command}' failed, {e.Message}");
                return "";
            }
        }

        /// <summary>
        /// Runs a reference and reports what happened, for the test button. Unlike <see cref="Resolve"/>
        /// this does not cache, so a fix can be verified without restarting.
        /// </summary>
        public static (bool IsSuccess, string Message, int SecretLength) Test(string? value)
        {
            if (!IsReference(value))
                return (false, $"a reference has to start with {PREFIX}", 0);

            var command = CommandOf(value);
            if (command.Length == 0)
                return (false, "the command is empty", 0);

            try
            {
                var secret = Run(command);
                return secret.Length == 0
                    ? (false, "the command printed nothing", 0)
                    : (true, "", secret.Length);
            }
            catch (Exception e)
            {
                return (false, e.Message, 0);
            }
        }

        /// <summary>Forgets everything fetched so far, so a re-locked vault is consulted again.</summary>
        public static void ClearCache() => Cache.Clear();

        private static string Run(string command)
        {
            var startInfo = new ProcessStartInfo
            {
                // Through cmd.exe rather than parsed here: the point of this feature is to paste the same
                // line that works in a shell, pipes, quoting and all.
                FileName = "cmd.exe",
                Arguments = "/c " + command,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            using var process = Process.Start(startInfo)
                                ?? throw new InvalidOperationException("the command could not be started");

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();

            if (!process.WaitForExit(TIMEOUT_MS))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
                throw new TimeoutException($"the command did not finish within {TIMEOUT_MS / 1000}s");
            }

            if (process.ExitCode != 0)
            {
                var reason = stderr.Trim();
                if (reason.Length == 0) reason = $"exit code {process.ExitCode}";
                throw new InvalidOperationException(reason);
            }

            // A CLI prints the secret with a trailing newline; sending that as part of a password would
            // submit the login form early, or fail the handshake outright.
            return stdout.Trim('\r', '\n', ' ', '\t');
        }
    }
}
