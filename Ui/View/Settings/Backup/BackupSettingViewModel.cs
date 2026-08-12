using System;
using System.IO;
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
        private const string FILE_FILTER = "1Remote backup|*" + BackupService.FILE_EXTENSION;

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
                IoC.Get<ConfigurationService>().Save();
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

            if (!BackupService.IsBackup(path!))
            {
                LastResult = IoC.Translate("backup_not_a_backup");
                return;
            }

            if (!MessageBoxHelper.Confirm(IoC.Translate("backup_restore_confirm"), ownerViewModel: this))
                return;

            IsBusy = true;
            try
            {
                BackupService.Restore(path!);
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
        }, _ => !IsBusy);
    }
}
