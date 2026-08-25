using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using _1RM.Model;
using _1RM.Model.Protocol.Base;
using _1RM.Service;
using _1RM.Service.DataSource;
using _1RM.Service.DataSource.DAO;
using _1RM.Service.DataSource.Model;
using _1RM.Utils;
using _1RM.View.Editor;
using _1RM.View.Editor.Forms.AlternativeCredential;
using _1RM.View.Utils;
using Shawn.Utils;
using Shawn.Utils.Wpf;
using Stylet;

namespace _1RM.View.Settings.CredentialVault
{
    public class CredentialItem
    {
        public CredentialItem(DataSourceBase dataSource, Credential credential)
        {
            DataSource = dataSource;
            Credential = credential;
        }

        public DataSourceBase DataSource { get; }
        public Credential Credential { get; }
    }

    public class CredentialVaultViewModel : NotifyPropertyChangedBase
    {
        private readonly DataSourceService _sourceService;

        private ObservableCollection<CredentialItem> _credentials = new ObservableCollection<CredentialItem>();
        public ObservableCollection<CredentialItem> Credentials
        {
            get => _credentials;
            set => SetAndNotifyIfChanged(ref _credentials, value);
        }

        public CredentialVaultViewModel(DataSourceService sourceService, GlobalData appData)
        {
            _sourceService = sourceService;
            InitCredentials();
            IoC.Get<GlobalData>().OnReloadAll -= InitCredentials;
            IoC.Get<GlobalData>().OnReloadAll += InitCredentials;
        }

        private void InitCredentials()
        {
            // Reading credentials is a round trip to the data source, which may be a remote database or a
            // file on a network share. This stays subscribed to OnReloadAll for the lifetime of the app, so
            // doing the read on the dispatcher froze every window mid-session — the 1Remote chrome and the
            // hosted remote sessions with it — until the query answered or timed out. Only the collection
            // swap needs the UI thread.
            Task.Run(() =>
            {
                try
                {
                    var items = _sourceService.GetSourceCredentials(false)
                        .Select(tuple => new CredentialItem(tuple.Item1, tuple.Item2))
                        .ToList();
                    Execute.OnUIThread(() => Credentials = new ObservableCollection<CredentialItem>(items));
                }
                catch (Exception e)
                {
                    SimpleLogHelper.Warning($"CredentialVaultViewModel: could not read the credentials, {e.Message}");
                }
            });
        }


        /// <summary>
        /// Credential writes go to SQLite / a remote server, and update/delete also walk every protocol to
        /// find inherited names. That used to run on the dispatcher (save button, delete confirm), which
        /// froze the chrome and every hosted session until the round trip finished. ReloadAll stays on
        /// this worker too: it is already safe from the database-check timer, and GetServers must not
        /// return to the UI thread.
        /// </summary>
        private static async Task<Result> PersistCredentialAsync(Func<Result> persist)
        {
            try
            {
                return await Task.Run(() =>
                {
                    var ret = persist();
                    if (ret.IsSuccess && ret.NeedReloadUI)
                        IoC.Get<GlobalData>().ReloadAll();
                    return ret;
                });
            }
            catch (Exception e)
            {
                SimpleLogHelper.Error(e);
                return Result.Fail(e.Message);
            }
        }


        private RelayCommand? _cmdAdd;
        public RelayCommand CmdAdd
        {
            get
            {
                return _cmdAdd ??= new RelayCommand(async (o) =>
                {
                    var source = await DataSourceSelectorViewModel.SelectDataSourceAsync();
                    if (source == null) return;
                    var existedNames = Credentials.Where(x => x.DataSource == source).Select(x => x.Credential.Name).ToList();
                    var vm = new AlternativeCredentialEditViewModel(existedNames, showHost: false, title: IoC.Translate("Add") + " " + IoC.Translate("Credentials"))
                    {
                        RequireUserName = true,
                        RequirePassword = true,
                        RequirePrivateKey = true,
                    };
                    vm.OnSave += async () =>
                    {
                        var ret = await PersistCredentialAsync(() => source.Database_InsertCredential(vm.New));
                        if (ret.IsSuccess)
                        {
                            if (!ret.NeedReloadUI)
                                Credentials.Add(new CredentialItem(source, vm.New));
                            return true; // close the dialog
                        }

                        MessageBoxHelper.ErrorAlert(ret.ErrorInfo);
                        return false; // do not close the dialog
                    };
                    MaskLayerController.ShowWindowWithMask(vm);
                });
            }
        }


        private RelayCommand? _cmdEdit;
        public RelayCommand CmdEdit
        {
            get
            {
                return _cmdEdit ??= new RelayCommand((o) =>
                {
                    if (o is CredentialItem item)
                    {
                        var source = item.DataSource;
                        item.Credential.DecryptToConnectLevel();
                        var name = item.Credential.Name;
                        var existedNames = Credentials.Where(x => x != item).Select(x => x.Credential.Name).ToList();
                        var vm = new AlternativeCredentialEditViewModel(existedNames, org: item.Credential, showHost: false, title: IoC.Translate("Edit") + " " + IoC.Translate("Credentials"))
                        {
                            RequireUserName = true,
                            RequirePassword = true,
                            RequirePrivateKey = true,
                        };
                        vm.OnSave += async () =>
                        {
                            var ret = await PersistCredentialAsync(() => source.Database_UpdateCredential(vm.New, name));
                            if (ret.IsSuccess)
                            {
                                // A reload rebuilds the whole collection; without one the grid would keep
                                // showing the row as it was before the edit, so swap it in place here.
                                if (!ret.NeedReloadUI)
                                {
                                    var index = Credentials.IndexOf(item);
                                    if (index >= 0)
                                        Credentials[index] = new CredentialItem(source, vm.New);
                                }
                                return true; // close the dialog
                            }

                            MessageBoxHelper.ErrorAlert(ret.ErrorInfo);
                            return false; // do not close the dialog
                        };
                        MaskLayerController.ShowWindowWithMask(vm);
                    }
                });
            }
        }


        /// <summary>
        /// The rows whose delete is still running. Deleting walks every protocol looking for inherited
        /// names, so the round trip is slow enough for a second click to land on the same row; that click
        /// has to be dropped instead of issuing the delete again. Only ever touched on the UI thread - the
        /// command runs there and PersistCredentialAsync resumes there - so it needs no lock, and the
        /// check itself does no work that would hold up the dispatcher.
        /// </summary>
        private readonly HashSet<CredentialItem> _deleteInFlight = new HashSet<CredentialItem>();

        private RelayCommand? _cmdDelete;
        public RelayCommand CmdDelete
        {
            get
            {
                return _cmdDelete ??= new RelayCommand(async (o) =>
                {
                    if (o is not CredentialItem item)
                        return;
                    if (_deleteInFlight.Add(item) == false)
                        return; // this row is already being deleted

                    try
                    {
                        if (true != MessageBoxHelper.Confirm(IoC.Translate("confirm_to_delete_selected") + " -> " + item.Credential.Name))
                            return;

                        var ret = await PersistCredentialAsync(() => item.DataSource.Database_DeleteCredential(new[] { item.Credential.Name }));
                        if (ret.IsSuccess)
                        {
                            if (!ret.NeedReloadUI)
                                Credentials.Remove(item);
                        }
                        else
                        {
                            MessageBoxHelper.ErrorAlert(ret.ErrorInfo);
                        }
                    }
                    finally
                    {
                        _deleteInFlight.Remove(item);
                    }
                });
            }
        }
    }
}
