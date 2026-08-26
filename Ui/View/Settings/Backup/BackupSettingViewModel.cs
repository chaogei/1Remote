using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using _1RM.Service;
using _1RM.Service.Backup;
using _1RM.Utils;
using Shawn.Utils;
using Shawn.Utils.Wpf;
using Shawn.Utils.Wpf.FileSystem;

namespace _1RM.View.Settings.Backup
{
    public class BackupSettingViewModel : NotifyPropertyChangedBaseScreen
    {
        private const string FILE_FILTER = Assert.APP_DISPLAY_NAME + " backup|*" + BackupService.FILE_EXTENSION;

        private ConfigurationService Configuration => IoC.Get<ConfigurationService>();

        public WebDavConfig WebDav => Configuration.WebDav;

        private string _lastResult = "";
        /// <summary>What the most recent backup or restore did, shown under the buttons.</summary>
        public string LastResult
        {
            get => _lastResult;
            private set => SetAndNotifyIfChanged(ref _lastResult, value);
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            private set => SetAndNotifyIfChanged(ref _isBusy, value);
        }

        /// <summary>Archives found on the WebDAV destination, newest first.</summary>
        public ObservableCollection<string> RemoteBackups { get; } = new ObservableCollection<string>();

        private string? _selectedRemoteBackup;
        public string? SelectedRemoteBackup
        {
            get => _selectedRemoteBackup;
            set => SetAndNotifyIfChanged(ref _selectedRemoteBackup, value);
        }

        private RelayCommand? _cmdCreate;
        public RelayCommand CmdCreate => _cmdCreate ??= new RelayCommand(_ =>
        {
            var path = SelectFileHelper.SaveFile(
                title: IoC.Translate("backup_create"),
                filter: FILE_FILTER,
                selectedFileName: BackupService.SuggestedFileName());
            if (string.IsNullOrEmpty(path)) return;

            IsBusy = true;
            try
            {
                // the profile holds settings the user may have changed a moment ago and not saved yet
                Configuration.Save();
                var count = BackupService.Create(path!);
                LastResult = IoC.Translate("backup_create_done", count, path!);
            }
            catch (Exception e)
            {
                SimpleLogHelper.Error(e);
                LastResult = IoC.Translate("backup_failed", e.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }, _ => !IsBusy);

        private RelayCommand? _cmdRestore;
        public RelayCommand CmdRestore => _cmdRestore ??= new RelayCommand(_ =>
        {
            var path = SelectFileHelper.OpenFile(
                title: IoC.Translate("backup_restore"),
                filter: FILE_FILTER,
                checkFileExists: true);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            RestoreFrom(path!);
        }, _ => !IsBusy);

        private void RestoreFrom(string path)
        {
            if (!BackupService.IsBackup(path))
            {
                LastResult = IoC.Translate("backup_not_a_backup");
                return;
            }

            if (!MessageBoxHelper.Confirm(IoC.Translate("backup_restore_confirm"), ownerViewModel: this))
                return;

            IsBusy = true;
            try
            {
                BackupService.Restore(path);
                // Every service that owns one of these files read it once at launch and would write its stale
                // copy straight back over what was just unpacked, so the app has to close. Relaunching it here
                // would not work either: the single-instance pipe would hand the new process to this one.
                MessageBoxHelper.Info(IoC.Translate("backup_restore_done"), ownerViewModel: this);
                App.Close();
            }
            catch (Exception e)
            {
                SimpleLogHelper.Error(e);
                LastResult = IoC.Translate("backup_failed", e.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        #region WebDAV

        public Visibility WebDavActionsVisibility => WebDav.IsUsable ? Visibility.Visible : Visibility.Collapsed;

        private RelayCommand? _cmdSaveWebDav;
        public RelayCommand CmdSaveWebDav => _cmdSaveWebDav ??= new RelayCommand(_ =>
        {
            Configuration.Save();
            RaisePropertyChanged(nameof(WebDavActionsVisibility));
            LastResult = IoC.Translate("webdav_saved");
        });

        private RelayCommand? _cmdWebDavUpload;
        public RelayCommand CmdWebDavUpload => _cmdWebDavUpload ??= new RelayCommand(async _ =>
        {
            if (IsBusy || !WebDav.IsUsable) return;

            IsBusy = true;
            var temporary = Path.Combine(Path.GetTempPath(), BackupService.SuggestedFileName());
            try
            {
                Configuration.Save();
                var count = BackupService.Create(temporary);
                await WebDavClient.UploadAsync(WebDav, temporary, Path.GetFileName(temporary));
                LastResult = IoC.Translate("webdav_upload_done", count.ToString(), Path.GetFileName(temporary));
                await RefreshRemoteAsync();
            }
            catch (Exception e)
            {
                SimpleLogHelper.Error(e);
                LastResult = IoC.Translate("backup_failed", e.Message);
            }
            finally
            {
                // The archive holds the whole configuration in the clear; it has no business staying in temp.
                TryDelete(temporary);
                IsBusy = false;
            }
        }, _ => !IsBusy);

        private RelayCommand? _cmdWebDavRefresh;
        public RelayCommand CmdWebDavRefresh => _cmdWebDavRefresh ??= new RelayCommand(async _ =>
        {
            if (IsBusy || !WebDav.IsUsable) return;
            IsBusy = true;
            try
            {
                await RefreshRemoteAsync();
                LastResult = IoC.Translate("webdav_list_done", RemoteBackups.Count.ToString());
            }
            catch (Exception e)
            {
                SimpleLogHelper.Error(e);
                LastResult = IoC.Translate("backup_failed", e.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }, _ => !IsBusy);

        private RelayCommand? _cmdWebDavRestore;
        public RelayCommand CmdWebDavRestore => _cmdWebDavRestore ??= new RelayCommand(async _ =>
        {
            var name = SelectedRemoteBackup;
            if (IsBusy || string.IsNullOrEmpty(name) || !WebDav.IsUsable) return;

            IsBusy = true;
            var temporary = Path.Combine(Path.GetTempPath(), name!);
            try
            {
                await WebDavClient.DownloadAsync(WebDav, name!, temporary);
            }
            catch (Exception e)
            {
                SimpleLogHelper.Error(e);
                LastResult = IoC.Translate("backup_failed", e.Message);
                TryDelete(temporary);
                IsBusy = false;
                return;
            }

            IsBusy = false;
            try
            {
                // Restore closes the app on success, so the temporary copy is removed first rather than in
                // a finally block that would never run.
                if (!BackupService.IsBackup(temporary))
                {
                    LastResult = IoC.Translate("backup_not_a_backup");
                    return;
                }
                if (!MessageBoxHelper.Confirm(IoC.Translate("backup_restore_confirm"), ownerViewModel: this))
                    return;

                BackupService.Restore(temporary);
                TryDelete(temporary);
                MessageBoxHelper.Info(IoC.Translate("backup_restore_done"), ownerViewModel: this);
                App.Close();
            }
            catch (Exception e)
            {
                SimpleLogHelper.Error(e);
                LastResult = IoC.Translate("backup_failed", e.Message);
            }
            finally
            {
                TryDelete(temporary);
            }
        }, _ => !IsBusy);

        private async System.Threading.Tasks.Task RefreshRemoteAsync()
        {
            var names = await WebDavClient.ListAsync(WebDav);
            var previous = SelectedRemoteBackup;
            RemoteBackups.Clear();
            foreach (var name in names)
                RemoteBackups.Add(name);
            SelectedRemoteBackup = names.FirstOrDefault(x => string.Equals(x, previous, StringComparison.Ordinal))
                                   ?? names.FirstOrDefault();
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception e)
            {
                SimpleLogHelper.Warning($"BackupSetting: could not remove {path}, {e.Message}");
            }
        }

        #endregion
    }
}
