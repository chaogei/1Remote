using System;
using System.Collections.Generic;
using System.Linq;
using _1RM.Model.Protocol;
using _1RM.Model.Protocol.Base;
using _1RM.Service;
using _1RM.Utils.Proxy;

namespace _1RM.Utils.SshConfig
{
    public sealed class SshConfigImportResult
    {
        public List<ProtocolBase> Servers { get; } = new List<ProtocolBase>();

        /// <summary>Jump hosts created on the proxy page to satisfy a ProxyJump directive.</summary>
        public List<ProxyConfig> CreatedProxies { get; } = new List<ProxyConfig>();
    }

    /// <summary>
    /// Turns <c>~/.ssh/config</c> into stored connections.
    ///
    /// <c>ProxyJump</c> is carried across rather than dropped: it names another block in the same file, so
    /// everything needed to build the jump host entry is right there, and a config that uses a bastion is
    /// exactly the config that would otherwise import into a list of servers that cannot be reached.
    /// </summary>
    public static class SshConfigImporter
    {
        /// <summary>
        /// Prefix for generated proxy entries, so an import can never overwrite a jump host the user
        /// configured by hand.
        /// </summary>
        public const string PROXY_NAME_PREFIX = "ssh config: ";

        public static SshConfigImportResult Build(
            IReadOnlyList<SshConfigEntry> entries,
            IReadOnlyList<string>? icons = null,
            IReadOnlyList<ProxyConfig>? existingProxies = null)
        {
            var result = new SshConfigImportResult();
            var byAlias = entries.ToDictionary(x => x.Alias, StringComparer.OrdinalIgnoreCase);
            var proxyByAlias = new Dictionary<string, ProxyConfig>(StringComparer.OrdinalIgnoreCase);
            var random = new Random();

            foreach (var entry in entries)
            {
                var ssh = ToSsh(entry);

                if (entry.ProxyJump.Length > 0 && byAlias.TryGetValue(entry.ProxyJump, out var jumpEntry))
                {
                    var proxy = ResolveJumpHost(jumpEntry, proxyByAlias, existingProxies, result);
                    ssh.ProxyName = proxy.Name;
                }

                if (icons?.Count > 0)
                    ssh.IconBase64 = icons[random.Next(0, icons.Count)];

                result.Servers.Add(ssh);
            }

            return result;
        }

        private static SSH ToSsh(SshConfigEntry entry)
        {
            var ssh = new SSH
            {
                // Address first: its setter renames a server whose display name still mirrors the old
                // address, which would undo the alias if it were assigned the other way round.
                Address = entry.HostName,
                Port = entry.Port.ToString(),
                // ssh falls back to the local account name when User is absent, and so should we — the
                // protocol's own default of "root" would be an invention.
                UserName = entry.User.Length > 0 ? entry.User : Environment.UserName,
                PrivateKey = entry.IdentityFile,
            };
            ssh.DisplayName = entry.Alias;
            return ssh;
        }

        private static ProxyConfig ResolveJumpHost(
            SshConfigEntry jumpEntry,
            IDictionary<string, ProxyConfig> created,
            IReadOnlyList<ProxyConfig>? existingProxies,
            SshConfigImportResult result)
        {
            if (created.TryGetValue(jumpEntry.Alias, out var already))
                return already;

            var name = PROXY_NAME_PREFIX + jumpEntry.Alias;

            // Re-importing the same file should point at the entry from last time rather than piling up
            // duplicates named after the same bastion.
            var existing = existingProxies?.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.Ordinal));
            if (existing != null)
            {
                created[jumpEntry.Alias] = existing;
                return existing;
            }

            var proxy = new ProxyConfig
            {
                Name = name,
                Type = EProxyType.SshJump,
                Address = jumpEntry.HostName,
                Port = jumpEntry.Port,
                UserName = jumpEntry.User.Length > 0 ? jumpEntry.User : Environment.UserName,
                PrivateKeyPath = jumpEntry.IdentityFile,
            };

            created[jumpEntry.Alias] = proxy;
            result.CreatedProxies.Add(proxy);
            return proxy;
        }

        /// <summary>
        /// Reads a config file and registers any jump hosts it needs, returning the servers to store.
        /// </summary>
        public static SshConfigImportResult ImportFile(string path, IReadOnlyList<string>? icons = null)
        {
            var entries = SshConfigParser.ParseFile(path);
            var proxyService = IoC.TryGet<ProxyService>();
            var result = Build(entries, icons, proxyService?.Proxies);

            if (proxyService != null && result.CreatedProxies.Count > 0)
            {
                proxyService.Proxies.AddRange(result.CreatedProxies);
                proxyService.Save();
            }

            return result;
        }
    }
}
