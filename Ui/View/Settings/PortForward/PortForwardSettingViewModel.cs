using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using _1RM.Service;
using _1RM.Utils;
using _1RM.Utils.PortForward;
using Shawn.Utils.Wpf;

namespace _1RM.View.Settings.PortForward
{
    public class PortForwardSettingViewModel : NotifyPropertyChangedBaseScreen
    {
        private readonly PortForwardService _service;

        public PortForwardSettingViewModel(PortForwardService service)
        {
            _service = service;
            Forwards = new ObservableCollection<PortForwardConfig>(_service.Forwards);
            SelectedForward = Forwards.FirstOrDefault();
        }

        protected override void OnViewLoaded()
        {
            // A session can be dropped while the page is closed, which takes its forwards down without
            // anything here noticing, so the list is reconciled every time it comes back into view.
            // Reconciling closes the forwards it finds dead, and closing them talks to the bastion, so it
            // runs off the dispatcher — the badges update through binding when it lands.
            _ = _service.RefreshStatusesAsync();
            RefreshHosts();
        }

        public ObservableCollection<PortForwardConfig> Forwards { get; }

        public sealed class ForwardTypeOption
        {
            public EPortForwardType Value { get; init; }
            public string Display { get; init; } = "";
        }

        public List<ForwardTypeOption> ForwardTypes { get; } = Enum.GetValues(typeof(EPortForwardType))
            .Cast<EPortForwardType>()
            .Select(x => new ForwardTypeOption { Value = x, Display = PortForwardTypeName.Of(x) })
            .ToList();

        /// <summary>Names of the SSH entries on the proxy page — the only hosts a forward can run through.</summary>
        public ObservableCollection<string> HostNames { get; } = new ObservableCollection<string>();

        private void RefreshHosts()
        {
            var names = _service.AvailableHosts.Select(x => x.Name).ToList();
            HostNames.Clear();
            foreach (var name in names)
                HostNames.Add(name);
            RaisePropertyChanged(nameof(NoHostsHintVisibility));
        }

        /// <summary>
        /// Shown when there is nothing to forward through yet. Without it the host dropdown is simply empty
        /// and the page gives no clue that the missing piece lives on another page.
        /// </summary>
        public Visibility NoHostsHintVisibility => HostNames.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        private PortForwardConfig? _selectedForward;
        public PortForwardConfig? SelectedForward
        {
            get => _selectedForward;
            set
            {
                if (!SetAndNotifyIfChanged(ref _selectedForward, value)) return;
                RaisePropertyChanged(nameof(EditorVisibility));
                RaisePropertyChanged(nameof(EmptyPlaceholderVisibility));
            }
        }

        public Visibility EditorVisibility => SelectedForward == null ? Visibility.Collapsed : Visibility.Visible;
        public Visibility EmptyPlaceholderVisibility => SelectedForward == null ? Visibility.Visible : Visibility.Collapsed;

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            private set => SetAndNotifyIfChanged(ref _isBusy, value);
        }

        private void Persist()
        {
            _service.Forwards.Clear();
            _service.Forwards.AddRange(Forwards);
            _service.Save();
        }

        private string BuildUniqueName()
        {
            var baseName = IoC.Translate("port_forward_new_name");
            var name = baseName;
            var i = 2;
            while (Forwards.Any(x => string.Equals(x.Name, name, StringComparison.Ordinal)))
                name = $"{baseName} {i++}";
            return name;
        }

        private RelayCommand? _cmdAdd;
        public RelayCommand CmdAdd => _cmdAdd ??= new RelayCommand(_ =>
        {
            var forward = new PortForwardConfig
            {
                Name = BuildUniqueName(),
                SshHostName = HostNames.FirstOrDefault() ?? "",
            };
            Forwards.Add(forward);
            SelectedForward = forward;
            Persist();
        });

        private RelayCommand? _cmdRemove;
        public RelayCommand CmdRemove => _cmdRemove ??= new RelayCommand(_ =>
        {
            var forward = SelectedForward;
            if (forward == null) return;
            if (!MessageBoxHelper.Confirm(IoC.Translate("confirm_to_delete_selected"), ownerViewModel: this)) return;

            // Not awaited: tearing the forward down means closing its port and possibly its session, which
            // is network work. The entry is going away either way, so the list updates now and the bastion
            // is dealt with on a pool thread instead of freezing the app until it answers.
            _ = _service.StopAsync(forward);
            var index = Forwards.IndexOf(forward);
            Forwards.Remove(forward);
            SelectedForward = Forwards.ElementAtOrDefault(Math.Min(index, Forwards.Count - 1));
            Persist();
        }, _ => SelectedForward != null);

        private RelayCommand? _cmdSave;
        public RelayCommand CmdSave => _cmdSave ??= new RelayCommand(_ => Persist());

        /// <summary>
        /// One button for both directions: a forward is either up or it is not, and a separate start and
        /// stop pair would leave one of them disabled and meaningless at all times.
        /// </summary>
        private RelayCommand? _cmdToggle;
        public RelayCommand CmdToggle => _cmdToggle ??= new RelayCommand(async _ =>
        {
            var forward = SelectedForward;
            if (forward == null || IsBusy) return;

            // Editing then starting without saving would run something the profile does not describe.
            Persist();

            IsBusy = true;
            try
            {
                if (forward.IsRunning)
                    await _service.StopAsync(forward);
                else
                    await _service.StartAsync(forward);
            }
            catch (Exception e)
            {
                // the command body is async void, an escaping exception would take the process down
                Shawn.Utils.SimpleLogHelper.Error(e);
                forward.LastError = e.Message;
                forward.Status = EPortForwardStatus.Failed;
            }
            finally
            {
                IsBusy = false;
            }
        }, _ => SelectedForward != null && !IsBusy);

        private RelayCommand? _cmdStopAll;
        public RelayCommand CmdStopAll => _cmdStopAll ??= new RelayCommand(async _ =>
        {
            if (IsBusy) return;
            IsBusy = true;
            try
            {
                // Every session gets disconnected here, and a bastion that has gone unreachable takes its
                // timeout to say so. On the dispatcher that is the whole app frozen, hosted sessions too.
                await _service.StopAllAsync();
            }
            catch (Exception e)
            {
                // the command body is async void, an escaping exception would take the process down
                Shawn.Utils.SimpleLogHelper.Error(e);
            }
            finally
            {
                IsBusy = false;
            }
        }, _ => Forwards.Any(x => x.IsRunning) && !IsBusy);
    }
}
