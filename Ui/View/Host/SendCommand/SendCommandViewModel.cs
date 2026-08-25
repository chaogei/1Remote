using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using _1RM.Service;
using _1RM.Utils;
using _1RM.Utils.SessionInput;
using _1RM.View.Host.ProtocolHosts;
using Shawn.Utils;
using Shawn.Utils.Wpf;

namespace _1RM.View.Host.SendCommand
{
    /// <summary>
    /// Types a command into one or more running terminal sessions, and keeps the ones worth keeping.
    ///
    /// This is the practical form of what other tools call broadcast input. Intercepting keystrokes as they
    /// are typed would need a global keyboard hook, because the terminal is a separate process that owns
    /// the focus — but the reason people reach for that feature is running the same command on a row of
    /// machines, and that does not need interception.
    /// </summary>
    public class SendCommandViewModel : NotifyPropertyChangedBaseScreen
    {
        private readonly ConfigurationService _configurationService;

        public sealed class SessionTarget : NotifyPropertyChangedBase
        {
            public SessionTarget(HostBase host)
            {
                Host = host;
                DisplayName = host.ProtocolServer.DisplayName;
                SubTitle = host.ProtocolServer.SubTitle;
            }

            public HostBase Host { get; }
            public string DisplayName { get; }
            public string SubTitle { get; }

            private bool _isSelected;
            public bool IsSelected
            {
                get => _isSelected;
                set => SetAndNotifyIfChanged(ref _isSelected, value);
            }
        }

        /// <param name="preselectedConnectionId">
        /// The session the user was looking at when they opened this, ticked so the common case of "run it
        /// here" needs no selecting at all.
        /// </param>
        public SendCommandViewModel(ConfigurationService configurationService, SessionControlService sessionControlService, string? preselectedConnectionId = null)
        {
            _configurationService = configurationService;

            Snippets = new ObservableCollection<CommandSnippet>(_configurationService.CommandSnippets);

            var targets = sessionControlService.ConnectionId2Hosts
                .Where(pair => SessionTextSender.CanSendTo(pair.Value))
                .OrderBy(pair => pair.Value.ProtocolServer.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .Select(pair =>
                {
                    var target = new SessionTarget(pair.Value)
                    {
                        IsSelected = string.Equals(pair.Key, preselectedConnectionId, StringComparison.Ordinal),
                    };
                    target.PropertyChanged += (_, _) => RaisePropertyChanged(nameof(SelectedTargetCount));
                    return target;
                })
                .ToList();

            Targets = new ObservableCollection<SessionTarget>(targets);
        }

        public ObservableCollection<CommandSnippet> Snippets { get; }
        public ObservableCollection<SessionTarget> Targets { get; }

        public bool HasTargets => Targets.Count > 0;
        public bool HasNoTargets => Targets.Count == 0;

        private CommandSnippet? _selectedSnippet;
        public CommandSnippet? SelectedSnippet
        {
            get => _selectedSnippet;
            set
            {
                if (!SetAndNotifyIfChanged(ref _selectedSnippet, value)) return;
                if (value == null) return;
                // Picking a snippet loads it for editing rather than sending it, so a near-miss can be
                // adjusted before it reaches a dozen machines.
                SnippetName = value.Name;
                CommandText = value.Content;
                AppendEnter = value.AppendEnter;
            }
        }

        private string _snippetName = "";
        public string SnippetName
        {
            get => _snippetName;
            set => SetAndNotifyIfChanged(ref _snippetName, value);
        }

        private string _commandText = "";
        public string CommandText
        {
            get => _commandText;
            set
            {
                if (SetAndNotifyIfChanged(ref _commandText, value))
                    RaisePropertyChanged(nameof(CanSend));
            }
        }

        private bool _appendEnter = true;
        public bool AppendEnter
        {
            get => _appendEnter;
            set => SetAndNotifyIfChanged(ref _appendEnter, value);
        }

        public int SelectedTargetCount => Targets.Count(x => x.IsSelected);

        public bool CanSend => CommandText.Length > 0 && SelectedTargetCount > 0;

        private string _result = "";
        public string Result
        {
            get => _result;
            private set
            {
                if (SetAndNotifyIfChanged(ref _result, value))
                    RaisePropertyChanged(nameof(HasResult));
            }
        }

        public bool HasResult => Result.Length > 0;

        private void PersistSnippets()
        {
            _configurationService.CommandSnippets.Clear();
            _configurationService.CommandSnippets.AddRange(Snippets);
            _configurationService.Save();
        }

        private RelayCommand? _cmdSaveSnippet;
        public RelayCommand CmdSaveSnippet => _cmdSaveSnippet ??= new RelayCommand(_ =>
        {
            if (CommandText.Length == 0) return;

            var name = SnippetName.Trim();
            if (name.Length == 0)
                name = new CommandSnippet { Content = CommandText }.Preview;

            // Saving under a name that already exists updates it, which is what "save" means everywhere
            // else; a second entry with the same name would just be confusing in the list.
            var existing = Snippets.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.Ordinal));
            if (existing != null)
            {
                existing.Content = CommandText;
                existing.AppendEnter = AppendEnter;
                SelectedSnippet = existing;
            }
            else
            {
                var snippet = new CommandSnippet { Name = name, Content = CommandText, AppendEnter = AppendEnter };
                Snippets.Add(snippet);
                SelectedSnippet = snippet;
            }

            PersistSnippets();
        });

        private RelayCommand? _cmdDeleteSnippet;
        public RelayCommand CmdDeleteSnippet => _cmdDeleteSnippet ??= new RelayCommand(_ =>
        {
            var snippet = SelectedSnippet;
            if (snippet == null) return;

            var index = Snippets.IndexOf(snippet);
            Snippets.Remove(snippet);
            SelectedSnippet = Snippets.ElementAtOrDefault(Math.Min(index, Snippets.Count - 1));
            PersistSnippets();
        }, _ => SelectedSnippet != null);

        private RelayCommand? _cmdSelectAll;
        public RelayCommand CmdSelectAll => _cmdSelectAll ??= new RelayCommand(_ =>
        {
            foreach (var target in Targets)
                target.IsSelected = true;
        });

        private RelayCommand? _cmdSelectNone;
        public RelayCommand CmdSelectNone => _cmdSelectNone ??= new RelayCommand(_ =>
        {
            foreach (var target in Targets)
                target.IsSelected = false;
        });

        private RelayCommand? _cmdSend;
        public RelayCommand CmdSend => _cmdSend ??= new RelayCommand(_ =>
        {
            var chosen = Targets.Where(x => x.IsSelected).ToList();
            if (chosen.Count == 0 || CommandText.Length == 0) return;

            var failed = new List<string>();
            foreach (var target in chosen)
            {
                if (!SessionTextSender.Send(target.Host, CommandText, AppendEnter))
                    failed.Add(target.DisplayName);
            }

            Result = failed.Count == 0
                ? IoC.Translate("send_command_sent", (chosen.Count - failed.Count).ToString())
                : IoC.Translate("send_command_partially_sent", (chosen.Count - failed.Count).ToString(), string.Join(", ", failed));
            SimpleLogHelper.Info($"SendCommand: sent to {chosen.Count - failed.Count}/{chosen.Count} session(s)");
        }, _ => CanSend);

        private RelayCommand? _cmdClose;
        public RelayCommand CmdClose => _cmdClose ??= new RelayCommand(_ => RequestClose());
    }
}
