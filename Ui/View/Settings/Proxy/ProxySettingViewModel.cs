using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using _1RM.Service;
using _1RM.Utils;
using _1RM.Utils.Proxy;
using Shawn.Utils;
using Shawn.Utils.Wpf;

namespace _1RM.View.Settings.Proxy
{
    public class ProxySettingViewModel : NotifyPropertyChangedBaseScreen
    {
        private readonly ProxyService _proxyService;

        public ProxySettingViewModel(ProxyService proxyService)
        {
            _proxyService = proxyService;
            Proxies = new ObservableCollection<ProxyConfig>(_proxyService.Proxies);
            SelectedProxy = Proxies.FirstOrDefault();
        }

        public ObservableCollection<ProxyConfig> Proxies { get; }

        /// <summary>
        /// <see cref="EProxyType.None"/> is excluded: a server opts out of proxying by selecting no proxy at
        /// all, not by keeping a proxy entry that does nothing.
        /// </summary>
        public List<EProxyType> ProxyTypes { get; } = Enum.GetValues(typeof(EProxyType))
            .Cast<EProxyType>()
            .Where(x => x != EProxyType.None)
            .ToList();

        private ProxyConfig? _selectedProxy;
        public ProxyConfig? SelectedProxy
        {
            get => _selectedProxy;
            set
            {
                if (!SetAndNotifyIfChanged(ref _selectedProxy, value)) return;
                TestResult = "";
                RaisePropertyChanged(nameof(EditorVisibility));
            }
        }

        public Visibility EditorVisibility => SelectedProxy == null ? Visibility.Collapsed : Visibility.Visible;

        private string _testTarget = "";
        /// <summary>
        /// The <c>host:port</c> the test connection is aimed at.
        /// </summary>
        public string TestTarget
        {
            get => _testTarget;
            set => SetAndNotifyIfChanged(ref _testTarget, value);
        }

        private string _testResult = "";
        public string TestResult
        {
            get => _testResult;
            private set => SetAndNotifyIfChanged(ref _testResult, value);
        }

        private bool _isTesting;
        public bool IsTesting
        {
            get => _isTesting;
            private set => SetAndNotifyIfChanged(ref _isTesting, value);
        }

        private void Persist()
        {
            _proxyService.Proxies.Clear();
            _proxyService.Proxies.AddRange(Proxies);
            _proxyService.Save();
        }

        private string BuildUniqueName()
        {
            var baseName = IoC.Translate("proxy_new_name");
            var name = baseName;
            var i = 2;
            while (Proxies.Any(x => string.Equals(x.Name, name, StringComparison.Ordinal)))
                name = $"{baseName} {i++}";
            return name;
        }

        private RelayCommand? _cmdAdd;
        public RelayCommand CmdAdd => _cmdAdd ??= new RelayCommand(_ =>
        {
            var proxy = new ProxyConfig { Name = BuildUniqueName() };
            Proxies.Add(proxy);
            SelectedProxy = proxy;
            Persist();
        });

        private RelayCommand? _cmdRemove;
        public RelayCommand CmdRemove => _cmdRemove ??= new RelayCommand(_ =>
        {
            var proxy = SelectedProxy;
            if (proxy == null) return;
            if (!MessageBoxHelper.Confirm(IoC.Translate("confirm_to_delete_selected"), ownerViewModel: this)) return;
            var index = Proxies.IndexOf(proxy);
            Proxies.Remove(proxy);
            SelectedProxy = Proxies.ElementAtOrDefault(Math.Min(index, Proxies.Count - 1));
            Persist();
        }, _ => SelectedProxy != null);

        private RelayCommand? _cmdSave;
        public RelayCommand CmdSave => _cmdSave ??= new RelayCommand(_ => Persist());

        private RelayCommand? _cmdTest;
        public RelayCommand CmdTest => _cmdTest ??= new RelayCommand(async _ =>
        {
            var proxy = SelectedProxy;
            if (proxy == null || IsTesting) return;

            if (!TrySplitTarget(TestTarget, out var host, out var port))
            {
                TestResult = IoC.Translate("proxy_test_target_invalid");
                return;
            }

            IsTesting = true;
            TestResult = IoC.Translate("proxy_testing");
            try
            {
                var result = await ProxyTester.TestAsync(proxy, host, port);
                TestResult = result.IsSuccess
                    ? $"{IoC.Translate("proxy_test_ok")} ({result.ElapsedMilliseconds} ms)"
                    : $"{IoC.Translate("proxy_test_failed")}: {result.Message}";
            }
            catch (Exception e)
            {
                // the command body is async void, an escaping exception would take the process down
                SimpleLogHelper.Error(e);
                TestResult = $"{IoC.Translate("proxy_test_failed")}: {e.Message}";
            }
            finally
            {
                IsTesting = false;
            }
        }, _ => SelectedProxy != null && !IsTesting);

        /// <summary>
        /// Accepts "host:port", "host port" and the bracketed IPv6 form "[::1]:port".
        /// </summary>
        internal static bool TrySplitTarget(string? target, out string host, out int port)
        {
            host = "";
            port = 0;
            target = (target ?? "").Trim();
            if (target.Length == 0) return false;

            int separator;
            if (target.StartsWith("[", StringComparison.Ordinal))
            {
                var closing = target.IndexOf(']');
                if (closing < 0) return false;
                separator = target.IndexOf(':', closing);
            }
            else
            {
                separator = target.LastIndexOfAny(new[] { ':', ' ' });
            }
            if (separator <= 0 || separator >= target.Length - 1) return false;

            host = target.Substring(0, separator).Trim().Trim('[', ']');
            return host.Length > 0
                   && int.TryParse(target.Substring(separator + 1).Trim(), out port)
                   && port > 0
                   && port <= 65535;
        }
    }
}
