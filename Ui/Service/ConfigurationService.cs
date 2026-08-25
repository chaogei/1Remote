using _1RM.Model;
using _1RM.Service.Backup;
using _1RM.Service.DataSource;
using _1RM.Service.DataSource.Model;
using _1RM.Utils;
using _1RM.Utils.PortForward;
using _1RM.Utils.Proxy;
using _1RM.Utils.SessionInput;
using _1RM.Utils.Tracing;
using _1RM.View;
using Newtonsoft.Json;
using Shawn.Utils;
using Shawn.Utils.Wpf;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using VariableKeywordMatcher.Provider.DirectMatch;
using SetSelfStartingHelper = _1RM.Utils.SetSelfStartingHelper;

namespace _1RM.Service
{
    public class EngagementSettings
    {
        public DateTime InstallTime = DateTime.Today;
        public bool DoNotShowAgain = false;
        public string DoNotShowAgainVersionString = "";
        public DateTime LastRequestRatingsTime = DateTime.MinValue;
        [Newtonsoft.Json.JsonIgnore]
        public VersionHelper.Version DoNotShowAgainVersion => VersionHelper.Version.FromString(DoNotShowAgainVersionString);


        public string BreakingChangeAlertVersionString = "";
        [Newtonsoft.Json.JsonIgnore]
        public VersionHelper.Version BreakingChangeAlertVersion => VersionHelper.Version.FromString(BreakingChangeAlertVersionString);
        public int ConnectCount = 0;
    }
    public class GeneralConfig
    {
        #region General
        public string CurrentLanguageCode = "en-us";
        public EnumServerViewStatus ServerViewStatus = EnumServerViewStatus.List;
        public enum EnumCloseButtonBehavior
        {
            Exit,
            Minimize,
        };
        public bool DoNotCheckNewVersion = false;
        public int CloseButtonBehavior = (int)EnumCloseButtonBehavior.Minimize;
        public bool ConfirmBeforeClosingSession = false;
        [DefaultValue(true)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool ShowSessionIconInSessionWindow = true;

        [DefaultValue(true)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool TabHeaderShowIconButton = true;
        [DefaultValue(true)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool TabHeaderShowCloseButton = true;
        [DefaultValue(false)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool TabHeaderShowReConnectButton = false;
        [DefaultValue(false)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool ShowRecentlySessionInTray = false;
        public bool ShowNoteFieldInListView = true;

        /// <summary>
        /// Periodically open a connection to each visible server to show whether it is up. Off by default:
        /// it is traffic to every configured host on a timer, which is not something to start unasked.
        /// </summary>
        [DefaultValue(false)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool CheckServerReachability = false;

        [DefaultValue(60)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public int ServerReachabilityIntervalSeconds = 60;

        /// <summary>
        /// Write everything a terminal session prints to a file. Off by default: a session log holds
        /// whatever crossed the screen, which regularly includes output nobody meant to keep.
        /// </summary>
        [DefaultValue(false)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool RecordTerminalSessions = false;

        /// <summary>Empty means <see cref="AppPathHelper.SessionLogDirPath"/>.</summary>
        public string SessionLogFolder = "";

        public int LogLevel = (int)SimpleLogHelper.EnumLogLevel.Warning;
        #endregion

        // Misc
        //[DefaultValue(true)]
        //[JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        //public bool TabAutoFocusContent= true;

        [DefaultValue(true)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool CopyPortWhenCopyAddress = true;


        [DefaultValue(false)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool TabWindowCloseButtonOnLeft = false;


        [DefaultValue(false)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool TabWindowSetFocusToLocalDesktopOnMouseLeaveRdpWindow = false;

        /// <summary>
        /// DateTime format for file transmit host
        /// 0 = yyyy-MM-dd HH:mm:ss (24H)
        /// 1 = yyyy-MM-dd hh:mm:ss tt (12H)
        /// 2 = HH:mm:ss yyyy-MM-dd (Time first, 24H)
        /// 3 = hh:mm:ss tt yyyy-MM-dd (Time first, 12H)
        /// 4 = MM/dd/yyyy HH:mm:ss (24H)
        /// 5 = MM/dd/yyyy hh:mm:ss tt (12H)
        /// </summary>
        [DefaultValue(0)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public int FileTransmitDateTimeFormat = 0;
    }

    public class LauncherConfig
    {
        public bool LauncherEnabled = true;

#if DEBUG
        public HotkeyModifierKeys HotKeyModifiers = HotkeyModifierKeys.Shift;
#else
        public HotkeyModifierKeys HotKeyModifiers = HotkeyModifierKeys.Alt;
#endif

        public Key HotKeyKey = Key.M;

        public bool ShowNoteFieldInLauncher = true;
        public bool ShowCredentials = true;
        public bool IsMatchingCredentials = true;
        [DefaultValue(true)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool AllowSaveInfoInQuickConnect = true;
    }

    public class KeywordMatchConfig
    {
        /// <summary>
        /// name of the matchers
        /// </summary>
        public List<string> EnabledMatchers = new List<string>();
    }

    //public class DataSourcesConfig
    //{
    //    public SqliteSource LocalDataSource { get; set; } = new SqliteSource()
    //    {
    //        DataSourceName = DataSourceService.LOCAL_DATA_SOURCE_NAME,
    //        Path = "./" + Assert.APP_NAME + ".db"
    //    };
    //    public List<DataSourceBase> AdditionalDataSource { get; set; } = new List<DataSourceBase>();
    //}

    public class ThemeConfig
    {
        public string ThemeName = "Dark";

        public string PrimaryMidColor = "#323233";
        public string PrimaryLightColor = "#474748";
        public string PrimaryDarkColor = "#2d2d2d";
        public string PrimaryTextColor = "#cccccc";

        public string AccentMidColor = "#FF007ACC";
        public string AccentLightColor = "#FF32A7F4";
        public string AccentDarkColor = "#FF0061A3";
        public string AccentTextColor = "#FFFFFFFF";

        public string BackgroundColor = "#1e1e1e";
        public string BackgroundTextColor = "#cccccc";

        public string FontFamily = "Microsoft YaHei";
        public int FontSize = 12;

        /// <summary>
        /// Frosted window backdrop. Turning it off falls back to opaque panels, which is what users on
        /// remote desktops or low-end GPUs generally want.
        /// </summary>
        [DefaultValue(true)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool EnableAcrylic = true;

        /// <summary>
        /// How opaque the frosted panels read, 0-255. Below ~120 text starts to lose contrast against a busy
        /// desktop, above ~230 the blur is no longer visible.
        /// </summary>
        [DefaultValue(180)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public int AcrylicOpacity = 180;

        #region GetColor
        public System.Windows.Media.Color GetPrimaryMidColor => ColorAndBrushHelper.HexColorToMediaColor(PrimaryMidColor);
        public System.Windows.Media.Color GetPrimaryLightColor => ColorAndBrushHelper.HexColorToMediaColor(PrimaryLightColor);
        public System.Windows.Media.Color GetPrimaryDarkColor => ColorAndBrushHelper.HexColorToMediaColor(PrimaryDarkColor);
        public System.Windows.Media.Color GetPrimaryTextColor => ColorAndBrushHelper.HexColorToMediaColor(PrimaryTextColor);

        public System.Windows.Media.Color GetAccentMidColor => ColorAndBrushHelper.HexColorToMediaColor(AccentMidColor);
        public System.Windows.Media.Color GetAccentLightColor => ColorAndBrushHelper.HexColorToMediaColor(AccentLightColor);
        public System.Windows.Media.Color GetAccentDarkColor => ColorAndBrushHelper.HexColorToMediaColor(AccentDarkColor);
        public System.Windows.Media.Color GetAccentTextColor => ColorAndBrushHelper.HexColorToMediaColor(AccentTextColor);

        public System.Windows.Media.Color GetBackgroundColor => ColorAndBrushHelper.HexColorToMediaColor(BackgroundColor);
        public System.Windows.Media.Color GetBackgroundTextColor => ColorAndBrushHelper.HexColorToMediaColor(BackgroundTextColor);
        #endregion
    }

    public class Configuration
    {
        public GeneralConfig General { get; set; } = new GeneralConfig();
        public LauncherConfig Launcher { get; set; } = new LauncherConfig();
        public KeywordMatchConfig KeywordMatch { get; set; } = new KeywordMatchConfig();
        public int DatabaseCheckPeriod { get; set; } = 10;
        public int DatabaseReconnectPeriod { get; set; } = 60 * 2;

        private string _sqliteDatabasePath = "./" + Assert.APP_NAME + ".db";
        public string SqliteDatabasePath
        {
            get => _sqliteDatabasePath;
            set => _sqliteDatabasePath = value.Replace(Environment.CurrentDirectory, ".");
        }

        public ThemeConfig Theme { get; set; } = new ThemeConfig();
        public EngagementSettings Engagement { get; set; } = new EngagementSettings();
        public List<string> PinnedTags { get; set; } = new List<string>();
        /// <summary>
        /// The proxies a server can pick from by name. See <c>ProtocolBase.ProxyName</c>.
        /// </summary>
        public List<ProxyConfig> Proxies { get; set; } = new List<ProxyConfig>();
        /// <summary>
        /// Standing port forwards, each pointing at one of <see cref="Proxies"/> by name.
        /// </summary>
        public List<PortForwardConfig> PortForwards { get; set; } = new List<PortForwardConfig>();
        /// <summary>
        /// Saved commands, offered when sending text into running terminal sessions.
        /// </summary>
        public List<CommandSnippet> CommandSnippets { get; set; } = new List<CommandSnippet>();
        /// <summary>Optional off-machine destination for backups.</summary>
        public WebDavConfig WebDav { get; set; } = new WebDavConfig();
        public static Configuration? Load(string path)
        {
            var tmp = JsonConvert.DeserializeObject<Configuration>(File.ReadAllText(path));
            tmp?.RegulateTheme();
            return tmp;
        }

        private void RegulateTheme()
        {
            if (string.IsNullOrEmpty(Theme.ThemeName))
                Theme.ThemeName = "Dark";

            if (Theme.FontSize < 10)
                Theme.FontSize = 10;
            if (Theme.FontSize > 20)
                Theme.FontSize = 20;

            Theme.FontFamily = InstalledFonts.Resolve(Theme.FontFamily).Source;
        }
    }

    public class ConfigurationService
    {
        private readonly KeywordMatchService _keywordMatchService;
        public readonly List<MatchProviderInfo> AvailableMatcherProviders;
        private readonly Configuration _cfg = new Configuration();

        public GeneralConfig General => _cfg.General;
        public LauncherConfig Launcher => _cfg.Launcher;
        public KeywordMatchConfig KeywordMatch => _cfg.KeywordMatch;
        public SqliteSource LocalDataSource { get; } = new SqliteSource("Local");

        public int DatabaseCheckPeriod
        {
            get => _cfg.DatabaseCheckPeriod >= 0 ? (_cfg.DatabaseCheckPeriod > 99 ? 99 : _cfg.DatabaseCheckPeriod) : 0;
            set => _cfg.DatabaseCheckPeriod = value >= 0 ? (value > 99 ? 99 : value) : 0;
        }
        public int DatabaseReconnectPeriod
        {
            get => _cfg.DatabaseReconnectPeriod >= 0 ? (_cfg.DatabaseReconnectPeriod > 60 * 60 ? 60 * 60 : _cfg.DatabaseReconnectPeriod) : 0;
            set => _cfg.DatabaseReconnectPeriod = value >= 0 ? (value > 60 * 60 ? 60 * 60 : value) : 0;
        }

        public ThemeConfig Theme => _cfg.Theme;
        public EngagementSettings Engagement => _cfg.Engagement;
        public List<ProxyConfig> Proxies => _cfg.Proxies;
        public List<PortForwardConfig> PortForwards => _cfg.PortForwards;
        public List<CommandSnippet> CommandSnippets => _cfg.CommandSnippets;
        public WebDavConfig WebDav => _cfg.WebDav;
        /// <summary>
        /// Tags that show on the tab bar of the main window
        /// </summary>
        [Obsolete("this property become useless since 20230524, use LocalityService.TagDict instead")]
        public List<string> PinnedTags
        {
            set => _cfg.PinnedTags = value;
            get => _cfg.PinnedTags;
        }


        public List<DataSourceBase> AdditionalDataSource { get; set; } = new List<DataSourceBase>();



        public ConfigurationService(KeywordMatchService keywordMatchService, Configuration? cfg = null, List<DataSourceBase>? additionalDataSource = null)
        {
            if (cfg != null)
                _cfg = cfg;
            if (additionalDataSource != null)
                AdditionalDataSource = additionalDataSource;
            _keywordMatchService = keywordMatchService;
            AvailableMatcherProviders = KeywordMatchService.GetMatchProviderInfos() ?? new List<MatchProviderInfo>();

            LocalDataSource.Path = _cfg.SqliteDatabasePath;
            LocalDataSource.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == nameof(SqliteSource.Path))
                {
                    _cfg.SqliteDatabasePath = LocalDataSource.Path;
                }
            };

            // init matcher
            if (KeywordMatch.EnabledMatchers.Count > 0)
            {
                foreach (var matcherProvider in AvailableMatcherProviders)
                {
                    matcherProvider.Enabled = false;
                }

                foreach (var enabledName in KeywordMatch.EnabledMatchers)
                {
                    var first = AvailableMatcherProviders.FirstOrDefault(x => x.Name == enabledName);
                    if (first != null)
                        first.Enabled = true;
                }
            }
            AvailableMatcherProviders.First(x => x.Name == DirectMatchProvider.GetName()).Enabled = true;
            AvailableMatcherProviders.First(x => x.Name == DirectMatchProvider.GetName()).IsEditable = false;
            KeywordMatch.EnabledMatchers = AvailableMatcherProviders.Where(x => x.Enabled).Select(x => x.Name).ToList();
            _keywordMatchService.Init(KeywordMatch.EnabledMatchers.ToArray());

            // register matcher change event
            foreach (var info in AvailableMatcherProviders)
            {
                info.PropertyChanged += OnMatchProviderChangedHandler;
            }

            // The additional sources were already read by LoadFromAppPath and handed in above; reading the
            // file a second time here threw that away. Only fall back to disk when nothing was passed.
            if (additionalDataSource == null)
                AdditionalDataSource = DataSourceService.AdditionalSourcesLoadFromProfile(AppPathHelper.Instance.ProfileAdditionalDataSourceJsonPath);
            Save(); // writes only if the regulated config differs from what is on disk
        }

        private void OnMatchProviderChangedHandler(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(MatchProviderInfo.Enabled))
            {
                KeywordMatch.EnabledMatchers = AvailableMatcherProviders.Where(x => x.Enabled).Select(x => x.Name).ToList();
                Save();
                _keywordMatchService.Init(KeywordMatch.EnabledMatchers.ToArray());
            }
        }

        public bool CanSave = true;
        private string _lastSavedJson = "";

        public void Save()
        {
            AdditionalDataSource = AdditionalDataSource.Distinct().ToList();
            if (!CanSave) return;
            lock (this)
            {
                if (!CanSave) return;
                CanSave = false;
                {
                    var fi = new FileInfo(AppPathHelper.Instance.ProfileJsonPath);

                    try
                    {
                        if (fi?.Directory?.Exists == false)
                            fi.Directory.Create();
                    }
                    catch (Exception e)
                    {
                        SimpleLogHelper.Error(e);
                        return;
                    }

                    // Skip the write when nothing actually changed. Save() is called from every settings
                    // setter and once more on construction, so a plain launch used to rewrite this file for
                    // no reason — a synchronous flush that an on-access antivirus scan can stretch out.
                    var json = JsonConvert.SerializeObject(this._cfg, Formatting.Indented);
                    if (json != _lastSavedJson || !File.Exists(AppPathHelper.Instance.ProfileJsonPath))
                    {
                        RetryHelper.Try(() =>
                        {
                            File.WriteAllText(AppPathHelper.Instance.ProfileJsonPath, json, Encoding.UTF8);
                        }, actionOnError: exception => UnifyTracing.Error(exception));
                        _lastSavedJson = json;
                    }
                }

                DataSourceService.AdditionalSourcesSaveToProfile(AppPathHelper.Instance.ProfileAdditionalDataSourceJsonPath, AdditionalDataSource);

                CanSave = true;
            }
        }


        public static void SetSelfStart(bool isInstall)
        {
            SetSelfStartingHelper.SetSelfStart(isInstall, Assert.APP_NAME);
        }

        public static ConfigurationService LoadFromAppPath(KeywordMatchService keywordMatchService)
        {
            var cfg = new Configuration();

            if (File.Exists(AppPathHelper.Instance.ProfileJsonPath))
            {
                var tmp = Configuration.Load(AppPathHelper.Instance.ProfileJsonPath);
                if (tmp != null)
                    cfg = tmp;
            }

            var ads = DataSourceService.AdditionalSourcesLoadFromProfile(AppPathHelper.Instance.ProfileAdditionalDataSourceJsonPath);

            return new ConfigurationService(keywordMatchService, cfg, ads);
        }
    }
}
