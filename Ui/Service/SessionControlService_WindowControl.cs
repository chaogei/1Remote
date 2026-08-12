using System;
using System.Diagnostics;
using System.Linq;
using System.Windows.Threading;
using _1RM.View.Host;
using _1RM.View.Host.ProtocolHosts;
using Shawn.Utils;
using Shawn.Utils.Wpf;
using Stylet;

namespace _1RM.Service
{
    public partial class SessionControlService
    {
        public void AddTab(TabWindowView tab)
        {
            lock (_dictLock)
            {
                var token = tab.Token;
                Debug.Assert(!_token2TabWindows.ContainsKey(token));
                Debug.Assert(!string.IsNullOrEmpty(token));
                _token2TabWindows.TryAdd(token, tab);
                tab.Activated += (sender, args) =>
                    _lastTabToken = tab.Token;
            }
        }

        private FullScreenWindowView MoveToExistedFullScreenWindow(HostBase host, TabWindowView? fromTab)
        {
            // restore from tab to full
            var full = _connectionId2FullScreenWindows[host.ConnectionId];
            full.LastTabToken = "";
            // full screen placement. This can be reached from an RDP callback thread, and Top/Left/Height
            // are dependency properties, so the placement has to be dispatched.
            if (fromTab != null)
            {
                var lastTabToken = _lastTabToken;
                Execute.OnUIThreadSync(() =>
                {
                    var screenEx = ScreenInfoEx.GetCurrentScreen(fromTab);
                    full.Top = screenEx.VirtualWorkingAreaCenter.Y - full.Height / 2;
                    full.Left = screenEx.VirtualWorkingAreaCenter.X - full.Width / 2;
                });
                full.LastTabToken = lastTabToken;
            }
            full.ShowOrHide(host);
            return full;
        }

        private FullScreenWindowView MoveToNewFullScreenWindow(HostBase host, TabWindowView? fromTab)
        {
            // first time to full
            var full = FullScreenWindowView.Create(fromTab?.Token ?? "", host, fromTab);
            full.ShowOrHide(host);
            _connectionId2FullScreenWindows.TryAdd(host.ConnectionId, full);
            return full;
        }


        public void MoveSessionToFullScreen(string connectionId)
        {
            if (!_connectionId2Hosts.ContainsKey(connectionId))
                throw new NullReferenceException($"can not find host by connectionId = `{connectionId}`");

            var host = _connectionId2Hosts[connectionId];

            // remove from old parent
            var tab = GetTabByConnectionId(connectionId);
            if (tab != null)
            {
                // if tab is not loaded, do not allow move to full-screen, 防止 loaded 事件中的逻辑覆盖
                if (tab.IsLoaded == false)
                    return;

                tab.GetViewModel().TryRemoveItem(connectionId);
                SimpleLogHelper.Debug($@"MoveSessionToFullScreen: remove connectionId = {connectionId} from tab({tab.GetHashCode()}) ");
            }

            // move to full-screen-window
            var full = _connectionId2FullScreenWindows.ContainsKey(connectionId) ?
                this.MoveToExistedFullScreenWindow(host, tab) :
                this.MoveToNewFullScreenWindow(host, tab);

            this.CleanupProtocolsAndWindows();

            SimpleLogHelper.Debug($@"Move host({host.GetHashCode()}) to full({full.GetHashCode()})");
            PrintCacheCount();
        }

        public void MoveSessionToTabWindow(string connectionId)
        {
            if (!_connectionId2Hosts.TryGetValue(connectionId, out var host))
            {
                SimpleLogHelper.Warning($@"MoveSessionToTabWindow: no host for connectionId = {connectionId}");
                return;
            }
            SimpleLogHelper.Debug($@"MoveSessionToTabWindow: Moving host({host.GetHashCode()}) to any tab");

            var fullToHide = host.ParentWindow as FullScreenWindowView;
            if (fullToHide?.IsLoaded == false)
            {
                // if FullScreenWindowView is not loaded, do not allow move to tab, 防止 loaded 事件中的逻辑覆盖
                return;
            }

            var tab = this.GetOrCreateTabWindow(fullToHide?.LastTabToken ?? "");
            if (tab.IsClosed)
            {
                tab = this.GetOrCreateTabWindow();
            }

            // This runs on the RDP callback thread, so the window work has to be dispatched. It must not
            // happen under _dictLock either — see the INVARIANT on _dictLock.
            Execute.OnUIThreadSync(() =>
            {
                if (fullToHide != null)
                {
                    SimpleLogHelper.Debug($@"Hide full({fullToHide.GetHashCode()})");
                    // !importance: do not close old FullScreenWindowView, or RDP will lose conn bar when restore from tab to fullscreen.
                    fullToHide.ShowOrHide(null);
                }

                var vm = tab.GetViewModel();
                var existed = vm.Items.FirstOrDefault(x => x.Content == host);
                if (existed == null)
                    vm.AddItem(new TabItemViewModel(host, host.ProtocolServer.DisplayName));
                else
                    vm.SelectedItem = existed;
                tab.Activate();
            });

            SimpleLogHelper.Debug($@"MoveSessionToTabWindow: Moved host({host.GetHashCode()}) to tab({tab.GetHashCode()})");
            PrintCacheCount();
        }


        /// <summary>
        /// get a tab for server,
        /// if assignTabToken == null, create a new tab
        /// if assignTabToken != null, find _token2tabWindows[assignTabToken], if _token2tabWindows[assignTabToken] is null, then create a new tab
        /// </summary>
        /// <param name="assignTabToken"></param>
        /// <returns></returns>
        private TabWindowView GetOrCreateTabWindow(string assignTabToken = "")
        {
            lock (_dictLock)
            {
                var existed = FindTabWindow(assignTabToken);
                if (existed != null)
                    return existed;
            }

            // Creating and showing a window needs the UI thread. It must not happen under _dictLock,
            // see the INVARIANT on _dictLock.
            TabWindowView? ret = null;
            Execute.OnUIThreadSync(() =>
            {
                TabWindowView? fresh = null;
                lock (_dictLock)
                {
                    // another thread may have created one while we were hopping threads
                    ret = FindTabWindow(assignTabToken);
                    if (ret == null)
                    {
                        fresh = new TabWindowView();
                        AddTab(fresh);
                        _lastTabToken = fresh.Token;
                        ret = fresh;
                    }
                }

                if (fresh == null) return;
                fresh.Show();
                fresh.ShowInTaskbar = true;
                // Show() queues the load pass on the dispatcher; draining it at Loaded priority lets callers
                // rely on IsLoaded. The Thread.Sleep spin this replaces blocked the very message pump that
                // raises Loaded, so it always burned its full 5s budget and froze the UI with it.
                fresh.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
            });

            return ret ?? throw new InvalidOperationException("failed to create a tab window");
        }

        /// <summary>
        /// Caller must hold <see cref="_dictLock"/>.
        /// </summary>
        private TabWindowView? FindTabWindow(string assignTabToken)
        {
            if (!string.IsNullOrEmpty(assignTabToken))
                return _token2TabWindows.TryGetValue(assignTabToken, out var assigned) ? assigned : null;
            if (_token2TabWindows.TryGetValue(_lastTabToken, out var last))
                return last;
            return _token2TabWindows.IsEmpty ? null : _token2TabWindows.Last().Value;
        }

        public TabWindowView? GetTabByConnectionId(string connectionId)
        {
            lock (_dictLock)
                return _token2TabWindows.Values.FirstOrDefault(x => x.GetViewModel().Items.Any(y => y.Content.ConnectionId == connectionId));
        }
    }
}