using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Shawn.Utils;

namespace _1RM.Service.Backup
{
    /// <summary>
    /// Packs everything the app keeps on disk into a single archive, and puts it back.
    ///
    /// The configuration is spread over six locations - the profile, the additional data source list, the
    /// SQLite database, the runner definitions and two folders under .locality - so "copy your settings
    /// somewhere safe" was not something a user could realistically do by hand. A remote data source is not
    /// included: it is not ours to copy, and the connection details that reach it are in the profile.
    /// </summary>
    public static class BackupService
    {
        /// <summary>Written into the archive so a restore can refuse a file that is not one of ours.</summary>
        private const string MANIFEST_ENTRY = "1remote-backup.txt";

        public const string FILE_EXTENSION = ".1rbak";

        private sealed class BackupItem
        {
            public BackupItem(string entryName, string fullPath, bool isDirectory = false)
            {
                EntryName = entryName;
                FullPath = fullPath;
                IsDirectory = isDirectory;
            }

            public string EntryName { get; }
            public string FullPath { get; }
            public bool IsDirectory { get; }
        }

        private static IEnumerable<BackupItem> Items()
        {
            var paths = AppPathHelper.Instance;
            yield return new BackupItem("profile.json", paths.ProfileJsonPath);
            yield return new BackupItem("dataSources.json", paths.ProfileAdditionalDataSourceJsonPath);
            yield return new BackupItem("database.db", paths.SqliteDbDefaultPath);
            yield return new BackupItem("Protocols", paths.ProtocolRunnerDirPath, isDirectory: true);
            yield return new BackupItem("locality", paths.LocalityDirPath, isDirectory: true);
            yield return new BackupItem("icons", paths.LocalityIconDirPath, isDirectory: true);
        }

        public static string SuggestedFileName() => $"{Assert.APP_NAME}-{DateTime.Now:yyyyMMdd-HHmmss}{FILE_EXTENSION}";

        /// <summary>
        /// Writes every configured path into <paramref name="archivePath"/>. Returns how many entries were
        /// written; a path that does not exist yet is simply skipped.
        /// </summary>
        public static int Create(string archivePath)
        {
            var directory = Path.GetDirectoryName(archivePath);
            if (!string.IsNullOrEmpty(directory))
                AppPathHelper.CreateDirIfNotExist(directory!, false);
            if (File.Exists(archivePath))
                File.Delete(archivePath);

            var count = 0;
            // ZipArchive over a plain FileStream rather than the ZipFile helpers: those live in
            // System.IO.Compression.FileSystem, which the net48 target would need an extra reference for.
            using var file = new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None);
            using var archive = new ZipArchive(file, ZipArchiveMode.Create);

            var manifest = archive.CreateEntry(MANIFEST_ENTRY);
            using (var writer = new StreamWriter(manifest.Open()))
            {
                writer.WriteLine($"{Assert.APP_NAME} backup");
                writer.WriteLine($"version={AppVersion.Version}");
                writer.WriteLine($"created={DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            }

            foreach (var item in Items())
            {
                if (item.IsDirectory)
                {
                    if (!Directory.Exists(item.FullPath)) continue;
                    foreach (var filePath in Directory.EnumerateFiles(item.FullPath, "*", SearchOption.AllDirectories))
                    {
                        var relative = filePath.Substring(item.FullPath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        CopyInto(archive, filePath, $"{item.EntryName}/{relative.Replace('\\', '/')}");
                        ++count;
                    }
                }
                else if (File.Exists(item.FullPath))
                {
                    CopyInto(archive, item.FullPath, item.EntryName);
                    ++count;
                }
            }

            SimpleLogHelper.Info($"BackupService: wrote {count} entries to {archivePath}");
            return count;
        }

        /// <summary>
        /// Reads the file through a share-allowing handle. The SQLite database and the log are open in this
        /// very process, so the plain ZipArchive.CreateEntryFromFile would fail on them.
        /// </summary>
        private static void CopyInto(ZipArchive archive, string sourcePath, string entryName)
        {
            var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            entry.LastWriteTime = File.GetLastWriteTime(sourcePath);
            using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var target = entry.Open();
            source.CopyTo(target);
        }

        private static ZipArchive OpenForRead(string archivePath, out FileStream file)
        {
            file = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return new ZipArchive(file, ZipArchiveMode.Read);
        }

        public static bool IsBackup(string archivePath)
        {
            try
            {
                using var archive = OpenForRead(archivePath, out var file);
                using (file)
                    return archive.GetEntry(MANIFEST_ENTRY) != null;
            }
            catch (Exception e)
            {
                SimpleLogHelper.Warning($"BackupService: {archivePath} is not readable as a backup, {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Unpacks <paramref name="archivePath"/> over the current configuration. The caller is expected to
        /// restart the app afterwards: the services holding this data were all constructed at launch and none
        /// of them watch their files.
        /// </summary>
        public static void Restore(string archivePath)
        {
            using var archive = OpenForRead(archivePath, out var file);
            using (file)
            {
                if (archive.GetEntry(MANIFEST_ENTRY) == null)
                    throw new InvalidDataException($"{archivePath} is not a {Assert.APP_NAME} backup");

                var byEntryName = Items().ToDictionary(x => x.EntryName, StringComparer.OrdinalIgnoreCase);

                foreach (var entry in archive.Entries)
                {
                    if (string.Equals(entry.FullName, MANIFEST_ENTRY, StringComparison.OrdinalIgnoreCase)) continue;
                    if (entry.FullName.EndsWith("/", StringComparison.Ordinal)) continue;

                    var target = ResolveTarget(byEntryName, entry.FullName);
                    if (target == null)
                    {
                        SimpleLogHelper.Warning($"BackupService: skipping unexpected entry '{entry.FullName}'");
                        continue;
                    }

                    AppPathHelper.CreateDirIfNotExist(target, true);
                    using var source = entry.Open();
                    using var destination = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
                    source.CopyTo(destination);
                }
            }

            SimpleLogHelper.Info($"BackupService: restored from {archivePath}");
        }

        /// <summary>
        /// Maps an archive entry back to a path on disk, refusing anything that would land outside the
        /// folders we own - an archive is untrusted input and "../.." in an entry name is the classic way to
        /// have an extractor overwrite something else.
        /// </summary>
        private static string? ResolveTarget(IReadOnlyDictionary<string, BackupItem> byEntryName, string entryFullName)
        {
            var normalised = entryFullName.Replace('\\', '/');
            var slash = normalised.IndexOf('/');
            var head = slash < 0 ? normalised : normalised.Substring(0, slash);

            if (!byEntryName.TryGetValue(head, out var item)) return null;
            if (!item.IsDirectory) return slash < 0 ? item.FullPath : null;

            var relative = normalised.Substring(slash + 1);
            if (relative.Length == 0) return null;

            var root = Path.GetFullPath(item.FullPath);
            var candidate = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            return candidate.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                ? candidate
                : null;
        }
    }
}
