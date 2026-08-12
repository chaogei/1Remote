using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Shawn.Utils;
using _1RM.Service.DataSource;
using _1RM.Utils.Tracing;
using _1RM.View;

namespace _1RM.Service.Locality
{
    public class LocalityListViewSettings
    {
        public EnumServerOrderBy ServerOrderBy = EnumServerOrderBy.IdAsc;
        public Dictionary<string, int> ServerCustomOrder = new Dictionary<string, int>();
        public Dictionary<string, int> GroupedOrder = new Dictionary<string, int>();
        public Dictionary<string, bool> GroupedIsExpanded = new Dictionary<string, bool>();
        public double ServerListNameWidth = 300;
        public double ServerListNoteWidth = 100;
    }

    public static class LocalityListViewService
    {
        public static string JsonPath => Path.Combine(AppPathHelper.Instance.LocalityDirPath, ".list_view.json");
        private static LocalityListViewSettings? _settings;
        public static LocalityListViewSettings Settings
        {
            get
            {
                if (_settings == null)
                {
                    Load();
                }
                return _settings!;
            }
            private set => _settings = value;
        }


        /// <summary>
        /// Bumped whenever the group order changes, so anything caching a derived sort key can tell that its
        /// copy is stale without having to be told individually.
        /// </summary>
        public static int GroupedOrderGeneration { get; private set; }

        private static string _lastSavedJson = "";

        public static void Load()
        {
            if (!File.Exists(JsonPath))
            {
                // no early return here used to mean a first run threw and caught FileNotFoundException on
                // every single Load()
                _settings = new LocalityListViewSettings();
                _lastSavedJson = "";
                ++GroupedOrderGeneration;
                return;
            }
            try
            {
                var text = File.ReadAllText(JsonPath);
                var tmp = JsonConvert.DeserializeObject<LocalityListViewSettings>(text);
                tmp ??= new LocalityListViewSettings();
                _settings = tmp;
            }
            catch
            {
                _settings = new LocalityListViewSettings();
            }
            _lastSavedJson = "";
            ++GroupedOrderGeneration;
        }

        public static void Save()
        {
            // Callers reach here on the UI thread from things as ordinary as a group being expanded, and
            // virtualization re-applies the same value as containers recycle. Comparing first turns those
            // repeats into no-ops instead of a synchronous disk round trip each.
            var json = JsonConvert.SerializeObject(Settings, Formatting.Indented);
            if (json == _lastSavedJson) return;

            AppPathHelper.CreateDirIfNotExist(AppPathHelper.Instance.LocalityDirPath, false);
            RetryHelper.Try(() => { File.WriteAllText(JsonPath, json, Encoding.UTF8); }, actionOnError: exception => UnifyTracing.Error(exception));
            _lastSavedJson = json;
        }

        public static void ServerOrderBySet(EnumServerOrderBy value)
        {
            if (Settings.ServerOrderBy == value) return;
            Settings.ServerOrderBy = value;
            Save();
        }




        public static void ServerCustomOrderSave(IEnumerable<ProtocolBaseViewModel> servers)
        {
            int i = 0;
            Settings.ServerCustomOrder.Clear();
            foreach (var server in servers)
            {
                Settings.ServerCustomOrder.Add(server.Id, i);
                server.CustomOrder = i;
                ++i;
            }
            Save();
        }




        public static int GroupedOrderGet(string dataSourceName)
        {
            return Settings.GroupedOrder.GetValueOrDefault(dataSourceName, int.MaxValue);
        }

        public static void GroupedOrderSave(IEnumerable<string> dataSourceNames)
        {
            int i = 0;
            Settings.GroupedOrder.Clear();
            foreach (var str in dataSourceNames.Distinct())
            {
                Settings.GroupedOrder.Add(str, i);
                ++i;
            }
            ++GroupedOrderGeneration;
            Save();
        }


        public static bool GroupedIsExpandedGet(string dataSourceName)
        {
            return Settings.GroupedIsExpanded.GetValueOrDefault(dataSourceName, true);
        }
        public static void GroupedIsExpandedSet(string dataSourceName, bool isExpanded)
        {
            // No Load() first. Settings is the live copy and nothing else writes the file, so re-reading it
            // here only added a synchronous disk read to every expander toggle.
            try
            {
                Settings.GroupedIsExpanded[dataSourceName] = isExpanded;
                var ds = IoC.TryGet<DataSourceService>();
                if (ds != null)
                {
                    foreach (var key in Settings.GroupedIsExpanded.Keys.ToArray())
                    {
                        if (ds.LocalDataSource?.Name != key && ds.AdditionalSources.All(x => x.Key != key))
                        {
                            Settings.GroupedIsExpanded.Remove(key);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                UnifyTracing.Error(e);
                Settings.GroupedIsExpanded = new Dictionary<string, bool>();
            }
            Save();
        }

        public static void ServerListNameWidthSet(double value)
        {
            if (Math.Abs(Settings.ServerListNameWidth - value) < 0.1) return;
            Settings.ServerListNameWidth = value;
            Save();
        }

        public static void ServerListNoteWidthSet(double value)
        {
            if (Math.Abs(Settings.ServerListNoteWidth - value) < 0.1) return;
            Settings.ServerListNoteWidth = value;
            Save();
        }
    }
}
