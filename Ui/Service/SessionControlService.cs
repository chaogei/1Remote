using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using _1RM.Model;
using _1RM.Model.Protocol.Base;
using _1RM.Utils;
using _1RM.View.Host;
using _1RM.View.Host.ProtocolHosts;
using Shawn.Utils;
using Shawn.Utils.WpfResources.Theme.Styles;
using Stylet;
using ProtocolHostStatus = _1RM.View.Host.ProtocolHosts.ProtocolHostStatus;
using _1RM.Service.DataSource;
using _1RM.Service.Locality;
using _1RM.Service.DataSource.DAO.Dapper;

namespace _1RM.Service
{
    public partial class SessionControlService
    {
        private readonly DataSourceService _sourceService;
        private readonly ConfigurationService _configurationService;
        private readonly GlobalData _appData;

        public SessionControlService(DataSourceService sourceService, ConfigurationService configurationService, GlobalData appData)
        {
            _sourceService = sourceService;
            _configurationService = configurationService;
            _appData = appData;
            GlobalEventHelper.OnRequestServerConnect += this.OnRequestOpenConnection;
            GlobalEventHelper.OnRequestQuickConnect += this.OnRequestOpenConnection;
            GlobalEventHelper.OnRequestServersConnect += this.OnRequestOpenConnection;
        }

        public void Release()
        {
            // Unsubscribe from static events to prevent memory leaks
            GlobalEventHelper.OnRequestServerConnect -= this.OnRequestOpenConnection;
            GlobalEventHelper.OnRequestQuickConnect -= this.OnRequestOpenConnection;
            GlobalEventHelper.OnRequestServersConnect -= this.OnRequestOpenConnection;

            WindowBase[] windowsToHide;
            lock (_dictLock)
            {
                windowsToHide = _token2TabWindows.Values.Cast<WindowBase>()
                    .Concat(_connectionId2FullScreenWindows.Values)
                    .ToArray();
            }
            HideWindows(windowsToHide);
            this.CloseProtocolHostAsync(_connectionId2Hosts.Keys.ToArray());
        }

        private string _lastTabToken = "";

        /// <summary>
        /// Guards compound reads/writes over the session dictionaries below.
        ///
        /// INVARIANT: never block on the UI thread while holding this lock — no Execute.OnUIThreadSync,
        /// no Dispatcher.Invoke, no Task.Wait, no external process. ConnectWithTab enters this lock from
        /// the UI thread, so a holder that waits for the UI thread deadlocks against it. Collect the UI
        /// work into a local list inside the lock and run it afterwards instead.
        /// </summary>
        private readonly object _dictLock = new object();
        private readonly ConcurrentDictionary<string, TabWindowView> _token2TabWindows = new ConcurrentDictionary<string, TabWindowView>();
        private readonly ConcurrentDictionary<string, HostBase> _connectionId2Hosts = new ConcurrentDictionary<string, HostBase>();
        private readonly ConcurrentDictionary<string, FullScreenWindowView> _connectionId2FullScreenWindows = new ConcurrentDictionary<string, FullScreenWindowView>();
        private readonly ConcurrentQueue<HostBase> _hostToBeDispose = new ConcurrentQueue<HostBase>();
        private readonly ConcurrentQueue<Window> _windowToBeDispose = new ConcurrentQueue<Window>();

        public int TabWindowCount
        {
            get
            {
                lock (_dictLock)
                {
                    return _token2TabWindows.Count;
                }
            }
        }

        public ConcurrentDictionary<string, HostBase> ConnectionId2Hosts => _connectionId2Hosts;

        /// <summary>
        /// Caller must not hold <see cref="_dictLock"/>.
        /// Typed as <see cref="WindowBase"/> and not <see cref="Window"/> on purpose: WindowBase hides Hide()
        /// with an IsClosing guard, and method hiding resolves on the static type.
        /// </summary>
        private static void HideWindows(IReadOnlyList<WindowBase> windows)
        {
            if (windows.Count == 0) return;
            Execute.OnUIThreadSync(() =>
            {
                foreach (var window in windows)
                {
                    try
                    {
                        window.Hide();
                    }
                    catch (Exception e)
                    {
                        SimpleLogHelper.Error(e);
                    }
                }
            });
        }


        private void OnRequestOpenConnection(in ProtocolBase serverOrg, in string fromView, in string assignTabToken = "", in string assignRunnerName = "", in string assignCredentialName = "")
        {
            CleanupProtocolsAndWindows();

            var org = serverOrg;
            var view = fromView;
            var tabToken = assignTabToken;
            var runnerName = assignRunnerName;
            var credentialName = assignCredentialName;
            Task.Factory.StartNew(async () =>
            {
                await Connect(org, view, tabToken, runnerName, credentialName);
            }).ContinueWith(t =>
            {
                if (t.Exception != null)
                {
                    SimpleLogHelper.Fatal(t.Exception);
                }
            });
        }

        private void OnRequestOpenConnection(IEnumerable<ProtocolBase> protocolBases, in string fromView, in string assignTabToken = "", in string assignRunnerName = "", in string assignCredentialName = "")
        {
            CleanupProtocolsAndWindows();
            var view = fromView;
            var tabToken = assignTabToken;
            var runnerName = assignRunnerName;
            var credentialName = assignCredentialName;
            Task.Factory.StartNew(async () =>
            {
                foreach (var org in protocolBases)
                {
                    tabToken = await Connect(org, view, tabToken, runnerName, credentialName);
                }
            }).ContinueWith(t =>
            {
                if (t.Exception != null)
                {
                    SimpleLogHelper.Fatal(t.Exception);
                }
            });
        }


        private void OnRequestCloseConnection(string connectionId)
        {
            this.CloseProtocolHostAsync(connectionId);
        }


        private bool ActivateOrReConnIfServerSessionIsOpened(in ProtocolBase server)
        {
            if (!server.IsOnlyOneInstance()) return false;
            var connectionId = server.BuildConnectionId();
            // if is `OnlyOneInstance Protocol`, and it is connected, activate it and return.
            if (!_connectionId2Hosts.ContainsKey(connectionId))
                return false;

            SimpleLogHelper.Debug($"_connectionId2Hosts ContainsKey {connectionId}");
            // Find activate
            if (_connectionId2Hosts[connectionId].ParentWindow is { } win)
            {
                if (win is TabWindowView tab)
                {
                    var serverId = server.Id;
                    var s = tab.GetViewModel().Items.FirstOrDefault(x => x.Content?.ProtocolServer?.BuildConnectionId() == connectionId);
                    if (s != null)
                        tab.GetViewModel().SelectedItem = s;
                }

                if (win.IsClosed)
                {
                    MarkProtocolHostToClose(new string[] { connectionId });
                    CleanupProtocolsAndWindows();
                    return false;
                }

                try
                {
                    Execute.OnUIThreadSync(() =>
                    {
                        if (win.IsClosing != false) return;
                        win.WindowState = win.WindowState == WindowState.Minimized ? WindowState.Normal : win.WindowState;
                        win.Show();
                        win.ShowInTaskbar = true;
                        win.Activate();
                    });

                    var vmServer = _appData.GetItemById(server.DataSource?.DataSourceName ?? "", server.Id);
                    vmServer?.ConnectTimeAddOrUpdate();
                }
                catch (Exception e)
                {
                    SimpleLogHelper.Error(e);
                    MarkProtocolHostToClose(new string[] { connectionId });
                    CleanupProtocolsAndWindows();
                }
            }

            // Reconnect
            if (_connectionId2Hosts[connectionId].ParentWindow != null)
            {
                if (_connectionId2Hosts[connectionId].Status != ProtocolHostStatus.Connected)
                    _connectionId2Hosts[connectionId].ReConn();
            }
            return true;
        }




        #region CloseProtocol

        public void CloseProtocolHostAsync(string connectionId)
        {
            CloseProtocolHostAsync(new[] { connectionId });
        }
        public void CloseProtocolHostAsync(string[] connectionIds)
        {
            Task.Factory.StartNew(() =>
            {
                MarkProtocolHostToClose(connectionIds);
                CleanupProtocolsAndWindows();
            });
        }
        private void MarkProtocolHostToClose(string[] connectionIds)
        {
            // Decided under _dictLock, executed after it is released — see the INVARIANT on _dictLock.
            var detachedHosts = new List<HostBase>();
            var itemsToRemove = new List<(TabWindowView tab, string connectionId)>();
            var windowsToHide = new List<WindowBase>();

            lock (_dictLock)
            {
                // 1. detach the hosts being closed
                var closedIds = new HashSet<string>();
                foreach (var connectionId in connectionIds)
                {
                    if (!_connectionId2Hosts.TryRemove(connectionId, out var host)) continue;
                    SimpleLogHelper.Debug($@"MarkProtocolHostToClose: marking to close: {host.GetType().Name}(id = {connectionId}, hash = {host.GetHashCode()})");
                    closedIds.Add(connectionId);
                    DetachHost(host, detachedHosts);
                }

                // 2. detach hosts that no window owns any more
                foreach (var kv in _connectionId2Hosts.ToArray())
                {
                    var id = kv.Key;
                    if (_connectionId2FullScreenWindows.ContainsKey(id)) continue;
                    if (_token2TabWindows.Values.Any(tab => tab.GetViewModel().Items.ToArray().Any(x => x?.Content?.ConnectionId == id))) continue;
                    if (!_connectionId2Hosts.TryRemove(id, out var host)) continue;
                    SimpleLogHelper.Warning($@"MarkUnhandledProtocolToClose: marking to close: {host.GetType().Name}(id = {id}, hash = {host.GetHashCode()})");
                    DetachHost(host, detachedHosts);
                }

                // 3. tab windows: drop the closed items, retire the windows that end up empty.
                //    Emptiness is computed against the whole closed set at once, because the items are
                //    only actually removed later, on the UI thread.
                foreach (var kv in _token2TabWindows.ToArray())
                {
                    var items = kv.Value.GetViewModel().Items.ToArray().Where(x => x?.Content != null).ToArray();
                    var closedItems = items.Where(x => closedIds.Contains(x.Content.ConnectionId)).ToArray();
                    if (closedItems.Length == 0) continue;

                    foreach (var item in closedItems)
                        itemsToRemove.Add((kv.Value, item.Content.ConnectionId));

                    if (closedItems.Length != items.Length) continue;
                    _token2TabWindows.TryRemove(kv.Key, out _);
                    _windowToBeDispose.Enqueue(kv.Value);
                    windowsToHide.Add(kv.Value);
                }

                // 4. full-screen windows of the closed sessions
                foreach (var kv in _connectionId2FullScreenWindows.ToArray())
                {
                    if (!closedIds.Contains(kv.Key)) continue;
                    var full = kv.Value;
                    if (full.Host != null && _connectionId2Hosts.ContainsKey(full.Host.ConnectionId)) continue;
                    _connectionId2FullScreenWindows.TryRemove(kv.Key, out _);
                    _windowToBeDispose.Enqueue(full);
                    windowsToHide.Add(full);
                }

                PrintCacheCount();
            }

            // UI work, strictly outside the lock
            if (itemsToRemove.Count > 0)
            {
                Execute.OnUIThreadSync(() =>
                {
                    foreach (var (tab, connectionId) in itemsToRemove)
                        tab.GetViewModel().TryRemoveItem(connectionId);
                });
            }
            HideWindows(windowsToHide);

            // the disconnect script spawns an external process, it must never run under the lock
            foreach (var host in detachedHosts)
            {
                try
                {
                    host.ProtocolServer.RunScriptAfterDisconnected();
                }
                catch (Exception e)
                {
                    SimpleLogHelper.Error(e);
                }
            }
        }

        private void DetachHost(HostBase host, List<HostBase> detachedHosts)
        {
            host.OnClosed -= OnRequestCloseConnection;
            host.OnFullScreen2Window -= this.MoveSessionToTabWindow;
            _hostToBeDispose.Enqueue(host);
            detachedHosts.Add(host);
        }

        #endregion

        #region Clean up CloseProtocol
        private void CloseMarkedProtocolHost()
        {
            while (_hostToBeDispose.TryDequeue(out var host))
            {
                PrintCacheCount();
                host.OnClosed -= OnRequestCloseConnection;
                host.OnFullScreen2Window -= this.MoveSessionToTabWindow;
                // Dispose
                try
                {
                    if (host is IDisposable d)
                    {
                        d.Dispose();
                    }
                    else
                    {
                        host.Close();
                    }
                }
                catch (Exception e)
                {
                    SimpleLogHelper.Error(e);
                }
            }
        }

        /// <summary>
        /// Moves windows that no longer host a live session into the dispose queue.
        /// Caller must hold <see cref="_dictLock"/>. Pure bookkeeping — no UI dispatch.
        /// </summary>
        private void RetireEmptyWindows()
        {
            int closeCount = 0;
            foreach (var kv in _token2TabWindows.ToArray())
            {
                var tab = kv.Value;
                var items = tab.GetViewModel().Items.ToArray().Where(x => x != null).ToArray();
                if (items.Length == 0 || items.All(x => _connectionId2Hosts.ContainsKey(x?.Content?.ConnectionId ?? "****") == false))
                {
                    SimpleLogHelper.Debug($@"RetireEmptyWindows: closing tab({tab.GetHashCode()})");
                    ++closeCount;
                    _token2TabWindows.TryRemove(kv.Key, out _);
                    _windowToBeDispose.Enqueue(tab);
                }
            }

            foreach (var kv in _connectionId2FullScreenWindows.ToArray())
            {
                var full = kv.Value;
                if (full.Host == null || _connectionId2Hosts.ContainsKey(full.Host.ConnectionId) == false)
                {
                    SimpleLogHelper.Debug($@"RetireEmptyWindows: closing full(hash = {full.GetHashCode()})");
                    ++closeCount;
                    _connectionId2FullScreenWindows.TryRemove(kv.Key, out _);
                    _windowToBeDispose.Enqueue(full);
                }
            }

            PrintCacheCount();
            // 在正常的逻辑中，在关闭session时就应该把空窗体移除，不应该有空窗体的存在
            if (closeCount > 0)
                SimpleLogHelper.DebugWarning($@"RetireEmptyWindows: {closeCount} Empty Host closed");
        }

        /// <summary>
        /// Caller must not hold <see cref="_dictLock"/>.
        /// </summary>
        private void CloseRetiredWindows()
        {
            if (_windowToBeDispose.IsEmpty) return;
            SimpleLogHelper.Debug($@"Closing: {_windowToBeDispose.Count} Empty Host.");
            Execute.OnUIThread(() =>
            {
                while (_windowToBeDispose.TryDequeue(out var window))
                {
                    try
                    {
                        window.Close();
                    }
                    catch (Exception e)
                    {
                        SimpleLogHelper.Error(e);
                    }
                }
            });
        }

        public void CleanupProtocolsAndWindows()
        {
            lock (_dictLock)
            {
                this.RetireEmptyWindows();
            }
            this.CloseRetiredWindows();
            this.CloseMarkedProtocolHost();
        }
        #endregion

        private void PrintCacheCount([CallerMemberName] string callMember = "")
        {
            SimpleLogHelper.Debug($@"{callMember}: Current: Host = {_connectionId2Hosts.Count}, Full = {_connectionId2FullScreenWindows.Count}, Tab = {_token2TabWindows.Count}, HostToBeDispose = {_hostToBeDispose.Count}, WindowToBeDispose = {_windowToBeDispose.Count}");
        }
    }
}