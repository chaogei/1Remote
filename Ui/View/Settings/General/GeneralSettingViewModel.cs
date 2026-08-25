using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using _1RM.Service;
using _1RM.Utils;
using _1RM.Utils.Tracing;
using Shawn.Utils;
using Shawn.Utils.Wpf;
using Shawn.Utils.Wpf.FileSystem;

namespace _1RM.View.Settings.General
{
    public class GeneralSettingViewModel : NotifyPropertyChangedBase
    {
        private readonly ConfigurationService _configurationService;
        private readonly LanguageService _languageService;

        public GeneralSettingViewModel(ConfigurationService configurationService, LanguageService languageService)
        {
            _configurationService = configurationService;
            _languageService = languageService;
        }


        public Dictionary<string, string> Languages => _languageService.LanguageCode2Name;
        public string Language
        {
            get => _configurationService.General.CurrentLanguageCode;
            set
            {
                Debug.Assert(Languages.ContainsKey(value));
                if (SetAndNotifyIfChanged(ref _configurationService.General.CurrentLanguageCode, value))
                {
                    // reset lang service
                    _languageService.SetLanguage(value);
                    _configurationService.Save();
                }
            }
        }

        public bool DoNotCheckNewVersion
        {
            get => _configurationService.General.DoNotCheckNewVersion;
            set
            {
                if (SetAndNotifyIfChanged(ref _configurationService.General.DoNotCheckNewVersion, value))
                {
                    _configurationService.Save();
                    IoC.Get<AboutPageViewModel>().StartVersionCheckTimer();
                }
            }
        }

        private bool _appStartAutomatically = false;
        public bool AppStartAutomatically
        {
            get => _appStartAutomatically;
            set
            {
                ConfigurationService.SetSelfStart(value);
                _appStartAutomatically = value;
                RaisePropertyChanged();
            }
        }

        public int CloseButtonBehavior
        {
            get => _configurationService.General.CloseButtonBehavior;
            set
            {
                if (SetAndNotifyIfChanged(ref _configurationService.General.CloseButtonBehavior, value))
                {
                    _configurationService.Save();
                }
            }
        }

        public bool ConfirmBeforeClosingSession
        {
            get => _configurationService.General.ConfirmBeforeClosingSession;
            set
            {
                if (SetAndNotifyIfChanged(ref _configurationService.General.ConfirmBeforeClosingSession, value))
                {
                    _configurationService.Save();
                }
            }
        }

        public bool CheckServerReachability
        {
            get => _configurationService.General.CheckServerReachability;
            set
            {
                if (SetAndNotifyIfChanged(ref _configurationService.General.CheckServerReachability, value))
                {
                    _configurationService.Save();
                    // The sweep has to start or stop now; waiting for a restart would make the toggle look
                    // like it did nothing.
                    IoC.TryGet<ServerReachabilityService>()?.ApplyConfiguration();
                    RaisePropertyChanged(nameof(ServerReachabilityIntervalVisibility));
                }
            }
        }

        public Visibility ServerReachabilityIntervalVisibility =>
            CheckServerReachability ? Visibility.Visible : Visibility.Collapsed;

        public int ServerReachabilityIntervalSeconds
        {
            get => _configurationService.General.ServerReachabilityIntervalSeconds;
            set
            {
                var clamped = Math.Clamp(value, ServerReachabilityService.MIN_INTERVAL_SECONDS, ServerReachabilityService.MAX_INTERVAL_SECONDS);
                if (SetAndNotifyIfChanged(ref _configurationService.General.ServerReachabilityIntervalSeconds, clamped))
                {
                    _configurationService.Save();
                    IoC.TryGet<ServerReachabilityService>()?.ApplyConfiguration();
                }
            }
        }

        public bool RecordTerminalSessions
        {
            get => _configurationService.General.RecordTerminalSessions;
            set
            {
                if (SetAndNotifyIfChanged(ref _configurationService.General.RecordTerminalSessions, value))
                {
                    _configurationService.Save();
                    RaisePropertyChanged(nameof(SessionLogFolderVisibility));
                }
            }
        }

        public Visibility SessionLogFolderVisibility =>
            RecordTerminalSessions ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Shows the effective folder even when none was chosen, so the path is never a mystery.</summary>
        public string SessionLogFolder
        {
            get => string.IsNullOrWhiteSpace(_configurationService.General.SessionLogFolder)
                ? AppPathHelper.Instance.SessionLogDirPath
                : _configurationService.General.SessionLogFolder;
            set
            {
                var folder = (value ?? "").Trim();
                // Storing the default as empty keeps a portable install portable: the folder follows the
                // app instead of being pinned to wherever it happened to live when this was set.
                if (string.Equals(folder, AppPathHelper.Instance.SessionLogDirPath, StringComparison.OrdinalIgnoreCase))
                    folder = "";
                if (SetAndNotifyIfChanged(ref _configurationService.General.SessionLogFolder, folder))
                {
                    _configurationService.Save();
                    RaisePropertyChanged(nameof(SessionLogFolder));
                }
            }
        }

        private RelayCommand? _cmdSelectSessionLogFolder;
        public RelayCommand CmdSelectSessionLogFolder => _cmdSelectSessionLogFolder ??= new RelayCommand(_ =>
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = IoC.Translate("session_log_folder"),
                UseDescriptionForTitle = true,
                SelectedPath = SessionLogFolder,
                ShowNewFolderButton = true,
            };
            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            SessionLogFolder = dialog.SelectedPath;
        });

        private RelayCommand? _cmdOpenSessionLogFolder;
        public RelayCommand CmdOpenSessionLogFolder => _cmdOpenSessionLogFolder ??= new RelayCommand(_ =>
        {
            try
            {
                var folder = SessionLogFolder;
                AppPathHelper.CreateDirIfNotExist(folder, isFile: false);
                Process.Start("explorer.exe", folder);
            }
            catch (Exception e)
            {
                UnifyTracing.Error(e);
            }
        });


        public bool ShowSessionIconInSessionWindow
        {
            get => _configurationService.General.ShowSessionIconInSessionWindow;
            set
            {
                if (SetAndNotifyIfChanged(ref _configurationService.General.ShowSessionIconInSessionWindow, value))
                {
                    _configurationService.Save();
                }
            }
        }

        public string LogPath => SimpleLogHelper.GetFileFullName();

        public SimpleLogHelper.EnumLogLevel LogLevel
        {
            get => SimpleLogHelper.WriteLogLevel;
            set
            {
                if (SimpleLogHelper.WriteLogLevel != value)
                {
                    SimpleLogHelper.WriteLogLevel = value;
                    SimpleLogHelper.PrintLogLevel = value;
                    _configurationService.General.LogLevel = (int)value;
                    RaisePropertyChanged();
                    _configurationService.Save();
                }
            }
        }

        //public bool TabAutoFocusContent
        //{
        //    get => _configurationService.General.TabAutoFocusContent;
        //    set
        //    {
        //        if (SetAndNotifyIfChanged(ref _configurationService.General.TabAutoFocusContent, value))
        //        {
        //            _configurationService.Save();
        //        }
        //    }
        //}

        public bool CopyPortWhenCopyAddress
        {
            get => _configurationService.General.CopyPortWhenCopyAddress;
            set
            {
                if (SetAndNotifyIfChanged(ref _configurationService.General.CopyPortWhenCopyAddress, value))
                {
                    _configurationService.Save();
                }
            }
        }

        public bool TabWindowCloseButtonOnLeft
        {
            get => _configurationService.General.TabWindowCloseButtonOnLeft;
            set
            {
                if (SetAndNotifyIfChanged(ref _configurationService.General.TabWindowCloseButtonOnLeft, value))
                {
                    _configurationService.Save();
                }
            }
        }

        public bool TabWindowSetFocusToLocalDesktopOnMouseLeaveRdpWindow
        {
            get => _configurationService.General.TabWindowSetFocusToLocalDesktopOnMouseLeaveRdpWindow;
            set
            {
                if (SetAndNotifyIfChanged(ref _configurationService.General.TabWindowSetFocusToLocalDesktopOnMouseLeaveRdpWindow, value))
                {
                    _configurationService.Save();
                }
            }
        }

        private RelayCommand? _cmdExploreTo = null;
        public RelayCommand CmdExploreTo
        {
            get
            {
                return _cmdExploreTo ??= new RelayCommand((o) =>
                {
                    try
                    {
                        SelectFileHelper.OpenInExplorerAndSelect(LogPath);
                    }
                    catch (Exception e)
                    {
                        UnifyTracing.Error(e);
                    }
                });
            }
        }
    }
}
