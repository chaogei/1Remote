using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using _1RM.Model;
using _1RM.Model.Protocol;
using _1RM.Model.Protocol.Base;
using _1RM.Service;
using _1RM.Service.DataSource;
using _1RM.Utils;
using Shawn.Utils;
using Shawn.Utils.Wpf;
using Stylet;
using VncSharpCore;

namespace _1RM.View.Host.ProtocolHosts
{
    public sealed partial class VncHost : HostBase
    {
        private readonly VNC _vncBase;

        public static VncHost Create(VNC protocolServer)
        {
            VncHost? view = null;
            Execute.OnUIThreadSync(() =>
            {
                view = new VncHost(protocolServer);
            });
            return view!;
        }

        private VncHost(VNC vnc) : base(vnc, false)
        {
            InitializeComponent();
            GridMessageBox.Visibility = Visibility.Collapsed;
            GridLoading.Visibility = Visibility.Visible;


            Vnc.ConnectComplete += OnConnected;
            Vnc.ConnectionLost += OnConnectionLost;

            _vncBase = vnc;

            MenuItems.Add(new System.Windows.Controls.Separator());
            MenuItems.Add(new System.Windows.Controls.MenuItem()
            {
                Header = "Ctrl + Alt + Del",
                Command = new RelayCommand((o) =>
                {
                    Vnc.SendSpecialKeys(SpecialKeys.CtrlAltDel);
                }, o => Status == ProtocolHostStatus.Connected)
            });
            MenuItems.Add(new System.Windows.Controls.MenuItem()
            {
                Header = "Ctrl + Esc",
                Command = new RelayCommand((o) =>
                {
                    Vnc.SendSpecialKeys(SpecialKeys.CtrlEsc);
                }, o => Status == ProtocolHostStatus.Connected)
            });
            MenuItems.Add(new System.Windows.Controls.MenuItem()
            {
                Header = "Alt + F4",
                Command = new RelayCommand((o) =>
                {
                    Vnc.SendSpecialKeys(SpecialKeys.AltF4);
                }, o => Status == ProtocolHostStatus.Connected)
            });
            {
                var tb = new TextBlock();
                tb.SetResourceReference(TextBlock.TextProperty, "Reconnect");
                MenuItems.Add(new System.Windows.Controls.MenuItem()
                {
                    Header = tb,
                    Command = new RelayCommand((o) => { ReConn(); })
                });
            }
            {
                var tb = new TextBlock();
                tb.SetResourceReference(TextBlock.TextProperty, "Close");
                MenuItems.Add(new System.Windows.Controls.MenuItem()
                {
                    Header = tb,
                    Command = new RelayCommand((o) => { Close(); })
                });
            }
        }

        #region Base Interface

        private const int DEFAULT_VNC_PORT = 5900;
        private const int REACHABILITY_TIMEOUT_MS = 10 * 1000;

        /// <summary>0 = idle, 1 = a connect attempt is in flight.</summary>
        private int _connectGuard;
        private bool _isClosed;

        public override void Conn() => StartConnect();

        public override void ReConn() => StartConnect();

        private void StartConnect()
        {
            if (_isClosed) return;
            // Reconnect is reachable from the context menu, the error panel and the session activation path
            // at the same time; without this the second caller would trip RemoteDesktop's "already
            // connected" guard.
            if (Interlocked.Exchange(ref _connectGuard, 1) != 0) return;

            // ActivateOrReConnIfServerSessionIsOpened calls ReConn from a background thread, and everything
            // below touches WPF elements and the hosted WinForms control.
            Execute.OnUIThread(() =>
            {
                try
                {
                    // suppress OnClosed while we tear the previous session down, otherwise dropping the old
                    // connection reads as "the user closed the tab" and the tab disappears mid-reconnect
                    _invokeOnClosedWhenDisconnected = false;
                    if (Vnc.IsConnected)
                        Vnc.Disconnect();

                    Status = ProtocolHostStatus.Connecting;
                    VncFormsHost.Visibility = Visibility.Collapsed;
                    GridLoading.Visibility = Visibility.Visible;
                    GridMessageBox.Visibility = Visibility.Collapsed;

                    var port = _vncBase.GetPort();
                    Vnc.VncPort = port > 0 ? port : DEFAULT_VNC_PORT;
                    Vnc.GetPassword = () => UnSafeStringEncipher.DecryptOrReturnOriginalString(_vncBase.Password);

                    _ = ConnectAfterReachableAsync(_vncBase.Address,
                                                   Vnc.VncPort,
                                                   _vncBase.VncWindowResizeMode == VNC.EVncWindowResizeMode.Stretch);
                }
                catch (Exception e)
                {
                    SimpleLogHelper.Error(e);
                    FinishConnect(e.Message);
                }
            });
        }

        /// <summary>
        /// Probes the host off the UI thread, then performs the real connect on it.
        ///
        /// RemoteDesktop.Connect does its TCP connect inline and then mutates the hosted WinForms control,
        /// so it cannot be moved off the UI thread wholesale. Left as it was, an unreachable host froze the
        /// whole window for the OS connect timeout — around 21 seconds. Proving reachability first bounds
        /// that: once the port answers, the connect inside RemoteDesktop returns immediately and only the
        /// RFB handshake runs on the UI thread.
        /// </summary>
        private async Task ConnectAfterReachableAsync(string address, int port, bool scaled)
        {
            bool isReachable;
            try
            {
                isReachable = await TcpHelper.TestConnectionAsync(address, port, null, REACHABILITY_TIMEOUT_MS) == true;
            }
            catch (Exception e)
            {
                SimpleLogHelper.Warning(e);
                isReachable = false;
            }

            Execute.OnUIThread(() =>
            {
                if (_isClosed)
                {
                    Interlocked.Exchange(ref _connectGuard, 0);
                    return;
                }

                if (!isReachable)
                {
                    FinishConnect(IoC.Translate("vnc_host_unreachable", $"{address}:{port}"));
                    return;
                }

                try
                {
                    Vnc.Connect(address, false, scaled);
                    VncFormsHost.Visibility = Visibility.Visible;
                    GridLoading.Visibility = Visibility.Collapsed;
                    GridMessageBox.Visibility = Visibility.Collapsed;
                    Status = ProtocolHostStatus.Connected;
                    FinishConnect(null);
                }
                catch (Exception e)
                {
                    SimpleLogHelper.Error(e);
                    FinishConnect(e.Message);
                }
            });
        }

        /// <summary>
        /// Ends a connect attempt. Must run on the UI thread.
        /// </summary>
        private void FinishConnect(string? error)
        {
            Interlocked.Exchange(ref _connectGuard, 0);
            if (error == null)
            {
                _invokeOnClosedWhenDisconnected = true;
                return;
            }

            // stay suppressed on failure: the error panel offers a Reconnect button, so the tab has to
            // survive rather than be closed out from under it
            Status = ProtocolHostStatus.Disconnected;
            VncFormsHost.Visibility = Visibility.Collapsed;
            GridLoading.Visibility = Visibility.Collapsed;
            GridMessageBox.Visibility = Visibility.Visible;
            TbMessageTitle.Visibility = Visibility.Collapsed;
            BtnReconn.Visibility = Visibility.Visible;
            TbMessage.Text = error;
        }

        public override void Close()
        {
            _isClosed = true;
            Status = ProtocolHostStatus.Disconnected;
            if (Vnc.IsConnected)
                Vnc.Disconnect();

            // Unsubscribe from events to prevent memory leaks
            Vnc.ConnectComplete -= OnConnected;
            Vnc.ConnectionLost -= OnConnectionLost;

            base.Close();
        }

        public override ProtocolHostType GetProtocolHostType()
        {
            return ProtocolHostType.Native;
        }

        public override IntPtr GetHostHwnd()
        {
            return IntPtr.Zero;
        }

        #endregion Base Interface

        #region event handler

        #region connection

        // Both handlers originate in the VNC client, so they are dispatched rather than assumed to already
        // be on the UI thread. Execute.OnUIThread runs inline when it already is.

        private void OnConnected(object sender, EventArgs e)
        {
            Execute.OnUIThread(() =>
            {
                Status = ProtocolHostStatus.Connected;
                VncFormsHost.Visibility = Visibility.Visible;
                GridLoading.Visibility = Visibility.Collapsed;
                GridMessageBox.Visibility = Visibility.Collapsed;
            });
        }

        private bool _invokeOnClosedWhenDisconnected = true;

        private void OnConnectionLost(object? sender, EventArgs e)
        {
            Execute.OnUIThread(() =>
            {
                Status = ProtocolHostStatus.Disconnected;
                VncFormsHost.Visibility = Visibility.Collapsed;
                GridLoading.Visibility = Visibility.Collapsed;
                GridMessageBox.Visibility = Visibility.Visible;
                TbMessageTitle.Visibility = Visibility.Collapsed;
                BtnReconn.Visibility = Visibility.Visible;
                TbMessage.Text = IoC.Translate("vnc_connection_lost");
                if (_invokeOnClosedWhenDisconnected)
                    base.OnClosed?.Invoke(base.ConnectionId);
            });
        }

        #endregion connection

        #endregion event handler

        private void BtnCancel_OnClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BtnReconn_OnClick(object sender, RoutedEventArgs e)
        {
            ReConn();
        }
    }
}