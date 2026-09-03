using _1RM.Model;
using _1RM.Model.Protocol;
using _1RM.Service;
using _1RM.Service.Locality;
using _1RM.Utils;
using _1RM.Utils.Rdp;
using _1RM.Utils.RdpFile;
using _1RM.Utils.WindowsApi;
using MSTSCLib;
using Shawn.Utils;
using Shawn.Utils.Wpf;
using Shawn.Utils.WpfResources.Theme.Styles;
using Stylet;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using Color = System.Drawing.Color;
using Timer = System.Timers.Timer;

namespace _1RM.View.Host.ProtocolHosts
{
    internal static class AxMsRdpClient9NotSafeForScriptingExAdd
    {
        public static void SetExtendedProperty(this AxHost axHost, string propertyName, object value)
        {
            try
            {
                ((IMsRdpExtendedSettings)axHost.GetOcx()).set_Property(propertyName, ref value);
            }
            catch (Exception e)
            {
                SimpleLogHelper.Error(e);
            }
        }
    }

    internal class AxMsRdpClient9NotSafeForScriptingEx : AxMSTSCLib.AxMsRdpClient9NotSafeForScripting
    {
        protected override void WndProc(ref System.Windows.Forms.Message m)
        {
            // Falsifying the response to WM_GETOBJECT to resolve issue #1053 that the RDP client to crash when using the word capture feature
            if (m.Msg == Win32Api.WM_GETOBJECT)
            {
                m.Result = -1; // Setting it to IntPtr.Zero (or 0) did not resolve the issue.
                               // Setting it experimentally to 1 or -1 solved the problem, though I cannot explain why.
                return;
            }
            // Fix for the missing focus issue on the rdp client component
            if (m.Msg == Win32Api.WM_MOUSEACTIVATE)
            {
                if (!this.ContainsFocus)
                {
                    SimpleLogHelper.Debug("AxMsRdpClient9NotSafeForScriptingEx.WndProc: Focus");
                    this.Focus();
                }
            }
            base.WndProc(ref m);
        }
    }


    public sealed partial class AxMsRdpClient09Host : HostBase, IDisposable
    {
        private AxMsRdpClient9NotSafeForScriptingEx? _rdpClient = null;
        //private readonly DataSourceBase? _dataSource;
        private readonly RDP _rdpSettings;
        /// <summary>
        /// system scale factor, 100 = 100%, 200 = 200%
        /// </summary>
        private uint _primaryScaleFactor = 100;
        /// <summary>
        /// if has connected, then rdp can resize
        /// </summary>
        private bool _flagHasConnected = false;
        /// <summary>
        /// if has ever connected successfully, then enabled auto reconnect feature
        /// </summary>
        private bool _flagHasEverConnected = false;


        private readonly System.Timers.Timer _loginResizeTimer; // timer for login resize, to fix the issue that the rdp client size is not correct when login
        private DateTime _lastLoginTime = DateTime.MinValue;

        private readonly object _rdpClientDisposeLock = new object();
        /// <summary>Bumps on each Conn()/Dispose so a delayed Connect() from a previous wait cannot fire into a disposed or replaced ActiveX control.</summary>
        private int _connectEpoch;


        public static AxMsRdpClient09Host Create(RDP rdp, int width = 0, int height = 0)
        {
            AxMsRdpClient09Host? view = null;
            Execute.OnUIThreadSync(() =>
            {
                view = new AxMsRdpClient09Host(rdp, width, height);
            });
            return view!;
        }

        private AxMsRdpClient09Host(RDP rdp, int width = 0, int height = 0) : base(rdp, true)
        {
            InitializeComponent();


            MenuItems.Add(new System.Windows.Controls.Separator());
            MenuItems.Add(new System.Windows.Controls.MenuItem()
            {
                Header = "Ctrl + Alt + Del",
                Command = new RelayCommand((o) =>
                {
                    if (_rdpClient != null)
                    {
                        _rdpClient.Focus();
                        new MsRdpClientNonScriptableWrapper(_rdpClient.GetOcx()).SendKeys(
                            new int[] { 0x1d, 0x38, 0x53, 0x53, 0x38, 0x1d },
                            new bool[] { false, false, false, true, true, true, });
                    }
                }, o => HasConnected)
            });

            GridMessageBox.Visibility = Visibility.Collapsed;
            GridLoading.Visibility = Visibility.Visible;

            _rdpSettings = rdp;

            _loginResizeTimer = new Timer(300) { Enabled = false, AutoReset = false };
            _loginResizeTimer.Elapsed += (sender, args) =>
            {
                _loginResizeTimer.Stop();
                try
                {
                    var nw = (uint)(_rdpClient?.Width ?? 0);
                    var nh = (uint)(_rdpClient?.Height ?? 0);
                    // tip: the control default width is 288
                    if (_rdpClient?.DesktopWidth > nw
                        || _rdpClient?.DesktopHeight > nh)
                    {
                        SimpleLogHelper.DebugInfo($@"_loginResizeTimer start run... {_rdpClient?.DesktopWidth}, {nw}, {_rdpClient?.DesktopHeight}, {nh}");
                        ReSizeRdpToControlSize();
                    }
                    else
                    {
                        _lastLoginTime = DateTime.MinValue;
                    }
                }
                finally
                {
                    if (DateTime.Now < _lastLoginTime.AddMinutes(1))
                    {
                        _loginResizeTimer.Start();
                    }
                    else
                    {
                        SimpleLogHelper.DebugWarning($@"_loginResizeTimer stop");
                    }
                }
            };

            InitRdp(width, height);
            GlobalEventHelper.OnScreenResolutionChanged += OnScreenResolutionChanged;
        }

        ~AxMsRdpClient09Host()
        {
            SimpleLogHelper.Debug($"Release {this.GetType().Name}({this.GetHashCode()})");
            Dispose();
        }

        public void Dispose()
        {
            SimpleLogHelper.Debug($"Disposing {this.GetType().Name}({this.GetHashCode()})");
            _resizeEndTimer?.Dispose();
            _loginResizeTimer?.Dispose();
            System.Threading.Interlocked.Increment(ref _connectEpoch);
            RdpClientDispose();
            SimpleLogHelper.Debug($"Dispose done {this.GetType().Name}({this.GetHashCode()})");
        }

        private void OnScreenResolutionChanged()
        {
            lock (_rdpClientDisposeLock)
            {
                // 全屏模式下客户端机器发生了屏幕分辨率改变，则将RDP还原到窗口模式（仿照 MSTSC 的逻辑）
                if (_rdpClient?.FullScreen == true)
                {
                    Execute.OnUIThread(() =>
                    {
                        _rdpClient.FullScreen = false;
                    });
                }
            }
        }

        /// <summary>
        /// init server connection info: user name\ psw \ port \ LoadBalanceInfo...
        /// </summary>
        private void RdpInitServerInfo()
        {
            #region server info
            Debug.Assert(_rdpClient != null); if (_rdpClient == null) return;
            // server connection info: user name\ psw \ port ...
            _rdpClient.Server = _rdpSettings.Address;
            _rdpClient.Domain = _rdpSettings.Domain;
            _rdpClient.UserName = _rdpSettings.UserName;
            _rdpClient.AdvancedSettings2.RDPPort = _rdpSettings.GetPort();


            if (string.IsNullOrWhiteSpace(_rdpSettings.LoadBalanceInfo) == false)
            {
                var loadBalanceInfo = _rdpSettings.LoadBalanceInfo;
                if (loadBalanceInfo.Length % 2 == 1)
                    loadBalanceInfo += " ";
                loadBalanceInfo += "\r\n";
                var bytes = Encoding.UTF8.GetBytes(loadBalanceInfo);
                _rdpClient.AdvancedSettings2.LoadBalanceInfo = Encoding.Unicode.GetString(bytes);
            }



            var secured = (MSTSCLib.IMsTscNonScriptable)_rdpClient.GetOcx();
            secured.ClearTextPassword = UnSafeStringEncipher.DecryptOrReturnOriginalString(_rdpSettings.Password);
            _rdpClient.FullScreenTitle = _rdpSettings.DisplayName + " - " + _rdpSettings.SubTitle;

            #endregion server info
        }

        private void RdpInitStatic()
        {
            Debug.Assert(_rdpClient != null); if (_rdpClient == null) return;
            SimpleLogHelper.Debug("RDP Host: init Static");
            _rdpClient.AdvancedSettings2.EncryptionEnabled = 1;
            _rdpClient.AdvancedSettings5.EnableAutoReconnect = true;
            // setting PublicMode to false allows the saving of credentials, which prevents
            _rdpClient.AdvancedSettings6.PublicMode = false;
            _rdpClient.AdvancedSettings5.EnableWindowsKey = 1;
            _rdpClient.AdvancedSettings5.GrabFocusOnConnect = true;
            _rdpClient.AdvancedSettings2.keepAliveInterval = 1000 * 60 * 1; // 1000 = 1000 ms
            _rdpClient.AdvancedSettings2.overallConnectionTimeout = 600; // The new time, in seconds. The maximum value is 600, which represents 10 minutes.

            // enable CredSSP, will use CredSsp if the client supports.
            _rdpClient.AdvancedSettings9.EnableCredSspSupport = true;

            //- 0: If server authentication fails, connect to the computer without warning (Connect and don't warn me)
            //- 1: If server authentication fails, don't establish a connection (Don't connect)
            //- 2: If server authentication fails, show a warning and allow me to connect or refuse the connection (Warn me)
            //- 3: No authentication requirement specified.
            // This was hardcoded to 0, which silently accepted any server identity — the exact warning mstsc
            // shows for an untrusted certificate was suppressed for every connection. Self-signed
            // certificates are normal on internal networks, so the escape hatch is per server rather than
            // global.
            // uint literals: the property is uint, and a conditional over plain 0/2 has the natural type int,
            // which has no implicit conversion to uint
            _rdpClient.AdvancedSettings9.AuthenticationLevel = _rdpSettings.TrustUnverifiedHost ? 0u : 2u;

            // ref: https://docs.microsoft.com/en-us/windows/win32/termserv/imsrdpclientadvancedsettings6-connecttoadministerserver
            _rdpClient.AdvancedSettings7.ConnectToAdministerServer = _rdpSettings.IsAdministrativePurposes == true;
        }

        private void CreateRdpClient()
        {
            lock (_rdpClientDisposeLock)
            {
                _rdpClient = new AxMsRdpClient9NotSafeForScriptingEx();

                SimpleLogHelper.Debug("RDP Host: init new AxMsRdpClient9NotSafeForScriptingEx()");

                ((System.ComponentModel.ISupportInitialize)(_rdpClient)).BeginInit();
                _rdpClient.Dock = DockStyle.Fill;
                _rdpClient.Enabled = true;
                _rdpClient.BackColor = Color.Black;
                // set call back
                _rdpClient.OnRequestGoFullScreen += (sender, args) =>
                {
                    SimpleLogHelper.Debug("RDP Host:  OnRequestGoFullScreen");
                    OnGoToFullScreenRequested();
                };
                _rdpClient.OnRequestLeaveFullScreen += (sender, args) =>
                {
                    SimpleLogHelper.Debug("RDP Host:  OnRequestLeaveFullScreen");
                    OnConnectionBarRestoreWindowCall();
                };
                _rdpClient.OnRequestContainerMinimize += (sender, args) =>
                {
                    SimpleLogHelper.Debug("RDP Host:  OnRequestContainerMinimize");
                    if (ParentWindow is FullScreenWindowView)
                    {
                        ParentWindow.WindowState = WindowState.Minimized;
                    }
                };
                _rdpClient.OnDisconnected += OnRdpClientDisconnected;
                _rdpClient.OnConfirmClose += (sender, args) =>
                {
                    // invoke in the full screen mode.
                    SimpleLogHelper.Debug("RDP Host:  RdpOnConfirmClose");
                    base.OnClosed?.Invoke(base.ConnectionId);
                };
                _rdpClient.OnConnected += OnRdpClientConnected;
                _rdpClient.OnLoginComplete += OnRdpClientLoginComplete;
                ((System.ComponentModel.ISupportInitialize)(_rdpClient)).EndInit();
                RdpHost.Child = _rdpClient;

                SimpleLogHelper.Debug("RDP Host: init CreateControl();");
                _rdpClient.CreateControl();
            }
        }

        private void RdpInitConnBar()
        {
            Debug.Assert(_rdpClient != null); if (_rdpClient == null) return;
            SimpleLogHelper.Debug("RDP Host: init conn bar");
            _rdpClient.AdvancedSettings6.DisplayConnectionBar = _rdpSettings.IsFullScreenWithConnectionBar == true;
            if (_rdpClient.AdvancedSettings6.DisplayConnectionBar)
            {
                _rdpClient.AdvancedSettings6.ConnectionBarShowPinButton = true;
                _rdpClient.AdvancedSettings6.PinConnectionBar = _rdpSettings.IsPinTheConnectionBarByDefault == true;
                _rdpClient.AdvancedSettings6.ConnectionBarShowMinimizeButton = true;
                _rdpClient.AdvancedSettings6.ConnectionBarShowRestoreButton = true;
            }
            _rdpClient.AdvancedSettings6.BitmapVirtualCache32BppSize = 48;
        }

        public void NotifyRedirectDeviceChange(int msg, IntPtr wParam, IntPtr lParam)
        {
            const int WM_DEVICECHANGE = 0x0219;

            /* from https://docs.microsoft.com/en-us/windows/win32/termserv/imsrdpclientnonscriptable-notifyredirectdevicechange
             *      https://learn.microsoft.com/en-us/windows/win32/devio/wm-devicechange
             * wParam case when msg == WM_DEVICECHANGE:
             * DBT_DEVNODES_CHANGED     0x0007      A device has been added to or removed from the system. param = 0
             * DBT_DEVICEARRIVAL        0x8000      A device or piece of media has been inserted and is now available. param = A pointer to a structure identifying the device inserted. 
             */
            SimpleLogHelper.Debug($"RDP: NotifyRedirectDeviceChange Receive(0x{msg:X}, 0x{wParam:X}, 0x{lParam:X})");
            if (msg == WM_DEVICECHANGE
                && _rdpClient != null
                && ((IMsRdpClientNonScriptable3)_rdpClient.GetOcx()).RedirectDynamicDevices)
            {
                new MsRdpClientNonScriptableWrapper(_rdpClient.GetOcx()).NotifyRedirectDeviceChange(wParam, lParam);
            }
        }

        private void RdpInitRedirect()
        {
            Debug.Assert(_rdpClient != null); if (_rdpClient == null) return;
            SimpleLogHelper.Debug("RDP Host: init Redirect");


            #region Redirect

            // purpose is not clear
            ((IMsRdpClientNonScriptable3)_rdpClient.GetOcx()).RedirectDynamicDrives = true; // Specifies or retrieves whether dynamically attached Plug and Play (PnP) drives that are enumerated while in a session are available for redirection. https://docs.microsoft.com/en-us/windows/win32/termserv/imsrdpclientnonscriptable3-redirectdynamicdrives

            if (_rdpSettings.EnableDiskDrives == true || _rdpSettings.EnableRedirectDrivesPlugIn == true)
            {
                _rdpClient.AdvancedSettings9.RedirectDrives = true;
                // you must redirect disk drive if you want to redirect usb disk
                if (_rdpSettings.EnableRedirectDrivesPlugIn == true)
                {
                    ((IMsRdpClientNonScriptable3)_rdpClient.GetOcx()).RedirectDynamicDevices = true; // Specifies whether dynamically attached PnP devices that are enumerated while in a session are available for redirection. https://docs.microsoft.com/en-us/windows/win32/termserv/imsrdpclientnonscriptable3-redirectdynamicdevices
                    RedirectDevice();
                }
            }

            // disable local disk
            if (_rdpSettings.EnableDiskDrives == false)
            {
                var ocx = (MSTSCLib.IMsRdpClientNonScriptable7)_rdpClient.GetOcx();
                ocx.DriveCollection.RescanDrives(false);
                for (int i = 0; i < ocx.DriveCollection.DriveCount; i++)
                {
                    ocx.DriveCollection.DriveByIndex[(uint)i].RedirectionState = false;
                }
            }


            _rdpClient.AdvancedSettings9.RedirectClipboard = _rdpSettings.EnableClipboard == true;
            _rdpClient.AdvancedSettings9.RedirectPrinters = _rdpSettings.EnablePrinters == true;
            _rdpClient.AdvancedSettings9.RedirectPOSDevices = _rdpSettings.EnablePorts == true;
            _rdpClient.AdvancedSettings9.RedirectSmartCards = _rdpSettings.EnableSmartCardsAndWinHello == true;


            if (_rdpSettings.EnableKeyCombinations == true)
            {
                // - 0 Apply key combinations only locally at the client computer.
                // - 1 Apply key combinations at the remote server.
                // - 2 Apply key combinations to the remote server only when the client is running in full-screen mode. This is the default value.
                _rdpClient.SecuredSettings3.KeyboardHookMode = 1;
            }
            else
            {
                _rdpClient.SecuredSettings3.KeyboardHookMode = 0;
            }

            if (_rdpSettings.AudioRedirectionMode == EAudioRedirectionMode.RedirectToLocal)
            {
                // - 0 (Audio redirection is enabled and the option for redirection is "Bring to this computer". This is the default mode.)
                // - 1 (Audio redirection is enabled and the option is "Leave at remote computer". The "Leave at remote computer" option is supported only when connecting remotely to a host computer that is running Windows Vista. If the connection is to a host computer that is running Windows Server 2008, the option "Leave at remote computer" is changed to "Do not play".)
                // - 2 (Audio redirection is enabled and the mode is "Do not play".)
                _rdpClient.SecuredSettings3.AudioRedirectionMode = 0;

                // Only set AudioQuality Moode when AudioRedirectionMode == RedirectToLocal
                if (_rdpSettings.AudioQualityMode == EAudioQualityMode.Dynamic)
                {
                    // - 0 Dynamic audio quality. This is the default audio quality setting. The server dynamically adjusts audio output quality in response to network conditions and the client and server capabilities.
                    // - 1 Medium audio quality. The server uses a fixed but compressed format for audio output.
                    // - 2 High audio quality. The server provides audio output in uncompressed PCM format with lower processing overhead for latency.
                    _rdpClient.AdvancedSettings8.AudioQualityMode = 0;
                }
                else if (_rdpSettings.AudioQualityMode == EAudioQualityMode.Medium)
                {
                    // - 1 Medium audio quality. The server uses a fixed but compressed format for audio output.
                    _rdpClient.AdvancedSettings8.AudioQualityMode = 1;
                }
                else if (_rdpSettings.AudioQualityMode == EAudioQualityMode.High)
                {
                    // - 2 High audio quality. The server provides audio output in uncompressed PCM format with lower processing overhead for latency.
                    _rdpClient.AdvancedSettings8.AudioQualityMode = 2;
                }

            }
            else if (_rdpSettings.AudioRedirectionMode == EAudioRedirectionMode.LeaveOnRemote)
            {
                // - 1 (Audio redirection is enabled and the option is "Leave at remote computer". The "Leave at remote computer" option is supported only when connecting remotely to a host computer that is running Windows Vista. If the connection is to a host computer that is running Windows Server 2008, the option "Leave at remote computer" is changed to "Do not play".)
                _rdpClient.SecuredSettings3.AudioRedirectionMode = 1;
            }
            else if (_rdpSettings.AudioRedirectionMode == EAudioRedirectionMode.Disabled)
            {
                // - 2 Disable sound redirection; do not play sounds at the server.
                _rdpClient.SecuredSettings3.AudioRedirectionMode = 2;
            }

            if (_rdpSettings.EnableAudioCapture == true)
            {
                // indicates whether the default audio input device is redirected from the client to the remote session
                _rdpClient.AdvancedSettings8.AudioCaptureRedirectionMode = true;
            }
            else
            {
                _rdpClient.AdvancedSettings8.AudioCaptureRedirectionMode = false;
            }
            #endregion Redirect
        }


        public void RedirectDevice()
        {
            var ocx = _rdpClient?.GetOcx() as MSTSCLib.IMsRdpClientNonScriptable7;
            if (ocx == null)
                return;

            // Collect FriendlyNames of cameras redirected via the RDPECCAM channel so that
            // the same physical device is not also claimed by the USB DeviceCollection channel,
            // which would cause a server-side double-redirect conflict. ref: https://github.com/1Remote/1Remote/issues/1071
            var cameraFriendlyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // redirect camera
            {
                ocx.CameraRedirConfigCollection.RedirectByDefault = false;
                if (_rdpSettings.EnableRedirectCameras == true)
                {
                    ocx.CameraRedirConfigCollection.Rescan(); // enumerates connected camera devices
                    for (int i = 0; i < ocx.CameraRedirConfigCollection.Count; i++)
                    {
                        var camera = ocx.CameraRedirConfigCollection.ByIndex[(uint) i];
                        camera.Redirected = true;
                        cameraFriendlyNames.Add(ocx.CameraRedirConfigCollection.ByIndex[(uint)i].FriendlyName ?? "");
                        SimpleLogHelper.Debug($"Redirect camera: {camera.FriendlyName}");
                    }
                }
            }
            // redirect device
            {
                ocx.DeviceCollection.RescanDevices(false);
                for (uint i = 0; i < ocx.DeviceCollection.DeviceCount; i++)
                {
                    var d = ocx.DeviceCollection.DeviceByIndex[i];
                    if (!string.IsNullOrEmpty(d.FriendlyName) && cameraFriendlyNames.Contains(d.FriendlyName))
                    {
                        // Skip cameras already handled by `redirect camera` to avoid double-redirect conflict
                        SimpleLogHelper.Debug($"Redirect device: skip {d.FriendlyName}({d.DeviceDescription}) for being already redirected as camera");
                        continue;
                    }

                    SimpleLogHelper.Debug($"Redirect device: {d.FriendlyName}({d.DeviceDescription})");
                    d.RedirectionState = true;
                }
            }
        }


        private void RdpInitDisplay(int width = 0, int height = 0, bool isReconnecting = false)
        {
            Debug.Assert(_rdpClient != null); if (_rdpClient == null) return;
            #region Display

            _primaryScaleFactor = ScreenInfoEx.GetPrimaryScreenScaleFactor();
            SimpleLogHelper.Debug($"RDP Host: init Display with ScaleFactor = {_primaryScaleFactor}, W = {width}, H = {height}, isReconnecting = {isReconnecting}");

            if (this._rdpSettings.IsScaleFactorFollowSystem == false && this._rdpSettings.ScaleFactorCustomValue != null)
            {
                _rdpClient.SetExtendedProperty("DesktopScaleFactor", this._rdpSettings.ScaleFactorCustomValue ?? _primaryScaleFactor);
            }
            else
            {
                _rdpClient.SetExtendedProperty("DesktopScaleFactor", _primaryScaleFactor);
            }
            _rdpClient.SetExtendedProperty("DeviceScaleFactor", (uint)100);
            if (_rdpSettings.RdpWindowResizeMode == ERdpWindowResizeMode.Stretch || _rdpSettings.RdpWindowResizeMode == ERdpWindowResizeMode.StretchFullScreen)
                _rdpClient.AdvancedSettings2.SmartSizing = true;
            // to enhance user experience, i let the form handled full screen
            _rdpClient.AdvancedSettings6.ContainerHandledFullScreen = 1;

            // pre-set the rdp width & height
            switch (_rdpSettings.RdpWindowResizeMode)
            {
                case ERdpWindowResizeMode.Stretch:
                case ERdpWindowResizeMode.Fixed:
                    _rdpClient.DesktopWidth = (int)(_rdpSettings.RdpWidth ?? 800);
                    _rdpClient.DesktopHeight = (int)(_rdpSettings.RdpHeight ?? 600);
                    break;
                case ERdpWindowResizeMode.FixedFullScreen:
                case ERdpWindowResizeMode.StretchFullScreen:
                    {
                        var size = GetScreenSizeIfRdpIsFullScreen();
                        _rdpClient.DesktopWidth = size.Width;
                        _rdpClient.DesktopHeight = size.Height;
                        break;
                    }
                case ERdpWindowResizeMode.AutoResize:
                case null:
                default:
                    {
                        // default case, set rdp size to tab window size.
                        if (width < 100)
                            width = 800;
                        if (height < 100)
                            height = 600;


                        //if (isReconnecting == true)
                        //{
                        //    // if isReconnecting == true, then width is DesktopWidth, ScaleFactor should be 100
                        //    _rdpClient.DesktopWidth = (int)(width);
                        //    _rdpClient.DesktopHeight = (int)(height);
                        //}
                        //else
                        {
                            // if isReconnecting == false, then width is Tab width, true width = Tab width * ScaleFactor
                            if (_rdpSettings.IsThisTimeConnWithFullScreen())
                            {
                                var size = GetScreenSizeIfRdpIsFullScreen();
                                _rdpClient.DesktopWidth = size.Width;
                                _rdpClient.DesktopHeight = size.Height;
                                SimpleLogHelper.DebugInfo($"RDP Host: init Display set FullScreen DesktopWidth = {_rdpClient.DesktopWidth},  DesktopHeight = {_rdpClient.DesktopHeight}");
                            }
                            else
                            {
                                _rdpClient.DesktopWidth = (int)(width * (_primaryScaleFactor / 100.0));
                                _rdpClient.DesktopHeight = (int)(height * (_primaryScaleFactor / 100.0));
                                SimpleLogHelper.DebugInfo(@$"RDP Host: init Display set DesktopWidth = {width} * {(_primaryScaleFactor / 100.0):F3} = {_rdpClient.DesktopWidth},  DesktopHeight = {height} * {(_primaryScaleFactor / 100.0):F3} = {_rdpClient.DesktopHeight},     RdpControl.Width = {_rdpClient.Width}, RdpControl.Height = {_rdpClient.Height}");
                                if (_primaryScaleFactor > 100)
                                {
                                    // size compensation since https://github.com/1Remote/1Remote/issues/537
                                    int c = (_primaryScaleFactor % 100) switch
                                    {
                                        50 => 1,
                                        75 => 2,
                                        _ => 0
                                    };
                                    if (ColorAndBrushHelper.ColorIsTransparent(_rdpSettings.ColorHex) != true)
                                    {
                                        c *= 2;
                                    }
                                    if (c < _rdpClient.DesktopWidth && c < _rdpClient.DesktopHeight)
                                    {
                                        //_rdpClient.DesktopWidth -= c;
                                        _rdpClient.DesktopHeight -= c;
                                    }
                                    SimpleLogHelper.DebugInfo($"RDP Host: init Display set DesktopWidth = {_rdpClient.DesktopWidth},  DesktopHeight = {_rdpClient.DesktopHeight}");
                                }
                            }
                        }

                        break;
                    }
            }



            switch (_rdpSettings.RdpFullScreenFlag)
            {
                case ERdpFullScreenFlag.Disable:
                    base.CanFullScreen = false;
                    break;

                case ERdpFullScreenFlag.EnableFullAllScreens:
                    base.CanFullScreen = true;
                    ((IMsRdpClientNonScriptable5)_rdpClient.GetOcx()).UseMultimon = true;
                    break;
                case ERdpFullScreenFlag.EnableFullScreen:
                default:
                    base.CanFullScreen = true;
                    break;
            }

            #endregion Display

            // 2022.07.23 try to fix the rdp error code 4360, ref: https://forum.asg-rd.com/showthread.php?tid=11016&page=2
            _rdpClient.AdvancedSettings8.BitmapPersistence = 0;
            _rdpClient.AdvancedSettings8.CachePersistenceActive = 0;

            SimpleLogHelper.Debug($"RDP Host: Display init end: RDP.DesktopWidth = {_rdpClient.DesktopWidth}, RDP.DesktopHeight = {_rdpClient.DesktopHeight},");
        }

        private void RdpInitPerformance()
        {
            Debug.Assert(_rdpClient != null); if (_rdpClient == null) return;
            SimpleLogHelper.Debug("RDP Host: init Performance");

            #region Performance

            // if win11 disable BandwidthDetection, make a workaround for #437 to hide info button after OS Win11 22H2 to avoid app crash when click the info button on Win11
            // detail: https://github.com/1Remote/1Remote/issues/437
            // 20250126: removed due to https://github.com/1Remote/1Remote/issues/559 is fixed
            //try
            //{
            //    if (_1RM.Utils.WindowsApi.WindowsVersionHelper.IsWindows1122H2OrHigher()) // Win11 22H2
            //    {
            //        _rdpClient.AdvancedSettings9.BandwidthDetection = false;
            //    }
            //}
            //catch (Exception)
            //{
            //    // ignored
            //}

            // ref: https://docs.microsoft.com/en-us/windows/win32/termserv/imsrdpclientadvancedsettings-performanceflags
            int nDisplayPerformanceFlag = 0;
            if (_rdpSettings.DisplayPerformance != EDisplayPerformance.Auto)
            {
                // ref: https://docs.microsoft.com/en-us/windows/win32/termserv/imsrdpclientadvancedsettings7-networkconnectiontype
                // CONNECTION_TYPE_MODEM (1 (0x1)) Modem (56 Kbps)
                // CONNECTION_TYPE_BROADBAND_LOW (2 (0x2)) Low-speed broadband (256 Kbps to 2 Mbps) CONNECTION_TYPE_SATELLITE (3 (0x3)) Satellite (2 Mbps to 16 Mbps, with high latency)
                // CONNECTION_TYPE_BROADBAND_HIGH (4 (0x4)) High-speed broadband (2 Mbps to 10 Mbps) CONNECTION_TYPE_WAN (5 (0x5)) Wide area network (WAN) (10 Mbps or higher, with high latency)
                // CONNECTION_TYPE_LAN (6 (0x6)) Local area network (LAN) (10 Mbps or higher)
                //
                // This used to be hardcoded to 1 for every quality level, so picking "High" still told the
                // server the link was a 56K modem and it compressed and throttled accordingly.
                _rdpClient.AdvancedSettings8.NetworkConnectionType = _rdpSettings.DisplayPerformance switch
                {
                    EDisplayPerformance.Low => 1,     // MODEM
                    EDisplayPerformance.Middle => 4,  // BROADBAND_HIGH
                    EDisplayPerformance.High => 6,    // LAN
                    _ => 6,
                };
                switch (_rdpSettings.DisplayPerformance)
                {
                    case EDisplayPerformance.Auto:
                        break;

                    case EDisplayPerformance.Low:
                        // 8,16,24,32
                        _rdpClient.ColorDepth = 8;
                        nDisplayPerformanceFlag += 0x00000001;//TS_PERF_DISABLE_WALLPAPER;      Wallpaper on the desktop is not displayed.
                        nDisplayPerformanceFlag += 0x00000002;//TS_PERF_DISABLE_FULLWINDOWDRAG; Full-window drag is disabled; only the window outline is displayed when the window is moved.
                        nDisplayPerformanceFlag += 0x00000004;//TS_PERF_DISABLE_MENUANIMATIONS; Menu animations are disabled.
                        nDisplayPerformanceFlag += 0x00000008;//TS_PERF_DISABLE_THEMING ;       Themes are disabled.
                        nDisplayPerformanceFlag += 0x00000020;//TS_PERF_DISABLE_CURSOR_SHADOW;  No shadow is displayed for the cursor.
                        nDisplayPerformanceFlag += 0x00000040;//TS_PERF_DISABLE_CURSORSETTINGS; Cursor blinking is disabled.
                        break;

                    case EDisplayPerformance.Middle:
                        _rdpClient.ColorDepth = 16;
                        nDisplayPerformanceFlag += 0x00000001;//TS_PERF_DISABLE_WALLPAPER;      Wallpaper on the desktop is not displayed.
                        nDisplayPerformanceFlag += 0x00000002;//TS_PERF_DISABLE_FULLWINDOWDRAG; Full-window drag is disabled; only the window outline is displayed when the window is moved.
                        nDisplayPerformanceFlag += 0x00000004;//TS_PERF_DISABLE_MENUANIMATIONS; Menu animations are disabled.
                        nDisplayPerformanceFlag += 0x00000008;//TS_PERF_DISABLE_THEMING ;       Themes are disabled.
                        nDisplayPerformanceFlag += 0x00000020;//TS_PERF_DISABLE_CURSOR_SHADOW;  No shadow is displayed for the cursor.
                        nDisplayPerformanceFlag += 0x00000040;//TS_PERF_DISABLE_CURSORSETTINGS; Cursor blinking is disabled.
                        nDisplayPerformanceFlag += 0x00000080;//TS_PERF_ENABLE_FONT_SMOOTHING;        Enable font smoothing.
                        nDisplayPerformanceFlag += 0x00000100;//TS_PERF_ENABLE_DESKTOP_COMPOSITION ;  Enable desktop composition.

                        break;

                    case EDisplayPerformance.High:
                        _rdpClient.ColorDepth = 32;
                        nDisplayPerformanceFlag += 0x00000080;//TS_PERF_ENABLE_FONT_SMOOTHING;        Enable font smoothing.
                        nDisplayPerformanceFlag += 0x00000100;//TS_PERF_ENABLE_DESKTOP_COMPOSITION ;  Enable desktop composition.
                        break;
                }
            }
            SimpleLogHelper.Debug("RdpInit: DisplayPerformance = " + _rdpSettings.DisplayPerformance + ", flag = " + Convert.ToString(nDisplayPerformanceFlag, 2));
            _rdpClient.AdvancedSettings9.PerformanceFlags = nDisplayPerformanceFlag;

            #endregion Performance
        }

        private void RdpInitGateway()
        {
            Debug.Assert(_rdpClient != null); if (_rdpClient == null) return;
            SimpleLogHelper.Debug("RDP Host: init Gateway");

            #region Gateway

            // Specifies whether Remote Desktop Gateway (RD Gateway) is supported.
            if (_rdpClient.TransportSettings.GatewayIsSupported != 0
                && _rdpSettings.GatewayMode != EGatewayMode.DoNotUseGateway)
            {
                // https://docs.microsoft.com/en-us/windows/win32/termserv/imsrdpclienttransportsettings-gatewayprofileusagemethod
                _rdpClient.TransportSettings2.GatewayProfileUsageMethod = 1; // Use explicit settings, as specified by the user.

                // ref: https://docs.microsoft.com/en-us/windows/win32/termserv/imsrdpclienttransportsettings-gatewayusagemethod
                _rdpClient.TransportSettings.GatewayUsageMethod = _rdpSettings.GatewayMode switch
                {
                    EGatewayMode.UseTheseGatewayServerSettings =>
                    1 // 1 : Always use an RD Gateway server. In the RDC client UI, the Bypass RD Gateway server for local addresses check box is cleared.
                    ,
                    EGatewayMode.AutomaticallyDetectGatewayServerSettings =>
                    2 // 2 : Use an RD Gateway server if a direct connection cannot be made to the RD Session Host server. In the RDC client UI, the Bypass RD Gateway server for local addresses check box is selected.
                    ,
                    _ => throw new ArgumentOutOfRangeException()
                };

                _rdpClient.TransportSettings2.GatewayHostname = _rdpSettings.GatewayHostName;
                //_rdpClient.TransportSettings2.GatewayDomain = "XXXXX";

                // ref: https://docs.microsoft.com/en-us/windows/win32/termserv/imsrdpclienttransportsettings-gatewaycredssource
                // TSC_PROXY_CREDS_MODE_USERPASS (0): Use a password (NTLM) as the authentication method for RD Gateway.
                // TSC_PROXY_CREDS_MODE_SMARTCARD (1): Use a smart card as the authentication method for RD Gateway.
                // TSC_PROXY_CREDS_MODE_ANY (4): Use any authentication method for RD Gateway.
                switch (_rdpSettings.GatewayLogonMethod)
                {
                    case EGatewayLogonMethod.SmartCard:
                        _rdpClient.TransportSettings.GatewayCredsSource = 1; // TSC_PROXY_CREDS_MODE_SMARTCARD
                        break;

                    case EGatewayLogonMethod.Password:
                        _rdpClient.TransportSettings.GatewayCredsSource = 0; // TSC_PROXY_CREDS_MODE_USERPASS
                        _rdpClient.TransportSettings2.GatewayUsername = _rdpSettings.GatewayUserName;
                        _rdpClient.TransportSettings2.GatewayPassword = _rdpSettings.GatewayPassword;
                        break;

                    default:
                        _rdpClient.TransportSettings.GatewayCredsSource = 4; // TSC_PROXY_CREDS_MODE_ANY
                        break;
                }

                _rdpClient.TransportSettings2.GatewayCredSharing = 0;
            }

            #endregion Gateway
        }

        private void InitRdp(int width = 0, int height = 0, bool isReconnecting = false)
        {
            if (Status != ProtocolHostStatus.NotInit)
                return;
            try
            {
                Status = ProtocolHostStatus.Initializing;
                RdpClientDispose();
                CreateRdpClient();
                RdpInitServerInfo();
                RdpInitStatic();
                RdpInitConnBar();
                RdpInitRedirect();
                RdpInitDisplay(width, height, isReconnecting);
                RdpInitPerformance();
                RdpInitGateway();
                _rdpSettings.ApplyRdpControlAdditionalSettings(_rdpClient!);
                Status = ProtocolHostStatus.Initialized;
            }
            catch (Exception e)
            {
                GridMessageBox.Visibility = Visibility.Visible;
                TbMessageTitle.Visibility = Visibility.Collapsed;
                TbMessage.Text = e.Message;

                Status = ProtocolHostStatus.NotInit;
            }
        }

        #region Base Interface
        public override void Conn()
        {
            Debug.Assert(_rdpClient != null); if (_rdpClient == null) return;
            var alreadyUnderway = Dispatcher.Invoke(() =>
            {
                if (Status == ProtocolHostStatus.Connected || Status == ProtocolHostStatus.Connecting)
                {
                    return true;
                }

                Status = ProtocolHostStatus.Connecting;
                GridLoading.Visibility = System.Windows.Visibility.Visible;
                RdpHost.Visibility = System.Windows.Visibility.Collapsed;
                return false;
            });
            // Not just cosmetic: proceeding here would mint a new epoch and, once the wait ended, call
            // Connect() a second time on a control already mid-handshake, which throws E_FAIL and flips a
            // healthy session's UI to the error panel. The pre-probe code returned in this case; keep that.
            if (alreadyUnderway)
                return;

            // Connect() is asynchronous: OnConnected is the only honest "Connected". Marking it here used
            // to make ReConn() think a still-handshaking session was already up. Wait for 3389 off the UI
            // thread first so a machine that just rebooted is given the same grace mstsc gives it.
            var epoch = System.Threading.Interlocked.Increment(ref _connectEpoch);
            _ = ConnectWhenEndpointReadyAsync(epoch);
        }

        private async Task ConnectWhenEndpointReadyAsync(int epoch)
        {
            try
            {
                try
                {
                    await WaitForEndpointReadyAsync().ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    SimpleLogHelper.Debug($"RDP Host: wait-for-endpoint: {e.Message}");
                }

                var identity = await VerifyHostIdentityAsync(epoch).ConfigureAwait(false);
                if (identity == EHostIdentityResult.Rejected)
                {
                    await ShowMessagePanelAsync(epoch, IoC.Translate("host_trust_title"),
                        IoC.Translate("host_identity_error_hint", IoC.Translate("host_trust_skip")));
                    return;
                }

                await Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        if (epoch != _connectEpoch)
                            return;
                        if (_rdpClient == null)
                            return;
                        if (Status != ProtocolHostStatus.Connecting
                            && Status != ProtocolHostStatus.Initialized)
                            return;

                        // Already verified against the app's own trust store, so the control has nothing left to
                        // warn about: 0 is "connect and do not warn me", the same value the per-server opt-out uses.
                        if (identity == EHostIdentityResult.Verified)
                            _rdpClient.AdvancedSettings9.AuthenticationLevel = 0u;

                        Status = ProtocolHostStatus.Connecting;
                        GridLoading.Visibility = System.Windows.Visibility.Visible;
                        RdpHost.Visibility = System.Windows.Visibility.Collapsed;
                        _rdpClient.Connect();
                    }
                    catch (Exception e)
                    {
                        GridMessageBox.Visibility = System.Windows.Visibility.Visible;
                        TbMessageTitle.Visibility = System.Windows.Visibility.Collapsed;
                        TbMessage.Text = e.Message;
                        Status = ProtocolHostStatus.Disconnected;
                    }
                });
            }
            catch (Exception e)
            {
                // Nothing awaits this task. An escape here used to be swallowed whole: Connect() was never
                // reached, no error was drawn, and the session kept its spinner over a black tab forever.
                SimpleLogHelper.Error($"RDP Host: connect pipeline failed: {e}");
                await ShowMessagePanelAsync(epoch, "", e.Message);
            }
        }

        /// <summary>Replaces the spinner with the error panel. Safe to call from any thread.</summary>
        private async Task ShowMessagePanelAsync(int epoch, string title, string message)
        {
            try
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    if (epoch != _connectEpoch)
                        return;
                    RdpHost.Visibility = System.Windows.Visibility.Collapsed;
                    GridLoading.Visibility = System.Windows.Visibility.Collapsed;
                    GridMessageBox.Visibility = System.Windows.Visibility.Visible;
                    TbMessageTitle.Visibility = string.IsNullOrEmpty(title)
                        ? System.Windows.Visibility.Collapsed
                        : System.Windows.Visibility.Visible;
                    TbMessageTitle.Text = title;
                    TbMessage.Visibility = System.Windows.Visibility.Visible;
                    TbMessage.Text = message;
                    BtnReconn.Visibility = System.Windows.Visibility.Visible;
                    Status = ProtocolHostStatus.Disconnected;
                });
            }
            catch (Exception e)
            {
                SimpleLogHelper.Warning($"RDP Host: could not show the error panel: {e.Message}");
            }
        }

        /// <summary>
        /// The whole probe — connect, negotiate, handshake. Long enough for a slow WAN round trip, short
        /// enough that a host which swallows the attempt does not hold up the session behind it.
        /// </summary>
        private const int CERTIFICATE_PROBE_TIMEOUT_MS = 5000;

        /// <summary>What the app made of the server's identity before the control was allowed to dial.</summary>
        private enum EHostIdentityResult
        {
            /// <summary>Known to the trust store, or accepted by the user just now.</summary>
            Verified,
            /// <summary>Nothing was checked; the control keeps its own warning.</summary>
            NotChecked,
            /// <summary>The user refused the identity, so the session must not be opened.</summary>
            Rejected,
        }

        /// <summary>
        /// Verifies the server certificate against the app's own trust store before the ActiveX control
        /// dials, so an identity the user has already accepted stops coming back as a warning.
        ///
        /// Windows keys its memory of an accepted certificate on the address alone
        /// (HKCU\Software\Microsoft\Terminal Server Client\Servers\&lt;address&gt;\CertHash), so a hostname
        /// that forwards a port per machine has all of those hosts sharing one entry and overwriting each
        /// other — which is why "don't ask me again" never sticks on a NAT or frp setup. The store used here
        /// is keyed on address *and* port, and the same one already backs SFTP and FTPS.
        ///
        /// Probing is a separate handshake from the session's, so a host that can only be reached through a
        /// gateway is left to the control. A probe that reaches nothing changes nothing either: the control
        /// still connects with its warning in place.
        /// </summary>
        private async Task<EHostIdentityResult> VerifyHostIdentityAsync(int epoch)
        {
            // This server opted out, so RdpInitStatic already set the control to connect without warning.
            if (_rdpSettings.TrustUnverifiedHost)
                return EHostIdentityResult.NotChecked;

            // An RD Gateway session does not reach the host by dialing it.
            if (_rdpSettings.GatewayMode is EGatewayMode.UseTheseGatewayServerSettings or EGatewayMode.AutomaticallyDetectGatewayServerSettings)
                return EHostIdentityResult.NotChecked;

            // What the control will dial: loopback for a session sent through a proxy tunnel.
            var host = _rdpSettings.Address?.Trim() ?? "";
            var port = _rdpSettings.GetPort();
            if (host.Length == 0 || port <= 0)
                return EHostIdentityResult.NotChecked;

            RdpServerCertificate? certificate;
            try
            {
                certificate = await RdpCertificateProbe
                    .TryGetCertificateAsync(host, port, CERTIFICATE_PROBE_TIMEOUT_MS, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                SimpleLogHelper.Debug($"RDP Host: certificate probe for {host}:{port} failed: {e.Message}");
                return EHostIdentityResult.NotChecked;
            }

            // A server on the legacy security layer has no certificate to pin.
            if (certificate == null)
                return EHostIdentityResult.NotChecked;

            // The probe can take a few seconds; the tab may be gone by now, and asking about a session
            // nobody is waiting for would be a dialog with nothing behind it.
            if (epoch != Volatile.Read(ref _connectEpoch))
                return EHostIdentityResult.NotChecked;

            try
            {
                // Filed under the endpoint the user picked rather than the one being dialed: a proxied
                // session dials loopback, and keying on that would file every proxied host under the same
                // entry.
                var trustPort = int.TryParse((_rdpSettings.RealPort ?? "").Trim(), out var real) && real > 0 ? real : port;
                var trusted = IoC.Get<HostTrustService>().VerifyOrAsk("rdp",
                    _rdpSettings.RealAddress, trustPort, HostTrustService.Fingerprint(certificate.RawData),
                    certificate.Subject, AskOnSessionWindow, trustOnFirstUse: true);
                return trusted ? EHostIdentityResult.Verified : EHostIdentityResult.Rejected;
            }
            catch (Exception e)
            {
                // Never let a fault in the trust store or its dialog decide whether a session opens: the
                // control still has its own warning, which is where this started.
                SimpleLogHelper.Error($"RDP Host: host trust check failed for {host}:{port}: {e}");
                return EHostIdentityResult.NotChecked;
            }
        }

        /// <summary>
        /// Asks on the window the user is looking at.
        ///
        /// The shared prompt is modal to the main window, which spends most of its life hidden behind the
        /// tray icon — and a dialog owned by a hidden window is one nobody sees, while the session behind it
        /// sits on its spinner waiting for an answer that cannot be given. This one is owned by the session's
        /// own window and brings it forward first.
        /// </summary>
        private bool AskOnSessionWindow(string title, string message)
        {
            var accepted = false;
            Execute.OnUIThreadSync(() =>
            {
                var owner = ParentWindow;
                if (owner is { IsLoaded: true })
                {
                    owner.Activate();
                    accepted = System.Windows.MessageBox.Show(owner, message, title,
                        MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
                }
                else
                {
                    accepted = System.Windows.MessageBox.Show(message, title,
                        MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
                }
            });
            return accepted;
        }

        public override void Close()
        {
            this.Dispose();
            base.Close();
        }

        protected override void GoFullScreen()
        {
            if (_rdpSettings.RdpFullScreenFlag == ERdpFullScreenFlag.Disable
                || ParentWindow is not FullScreenWindowView
                || _rdpClient?.FullScreen == true)
            {
                return;
            }
            Debug.Assert(_rdpClient != null); if (_rdpClient == null) return;
            if (_rdpClient.FullScreen != true)
                _rdpClient.FullScreen = true; // this will invoke OnRequestGoFullScreen -> MakeNormal2FullScreen
        }

        public override ProtocolHostType GetProtocolHostType()
        {
            return ProtocolHostType.Native;
        }

        public override IntPtr GetHostHwnd()
        {
            return IntPtr.Zero;
        }

        public override bool CanResizeNow()
        {
            return Status == ProtocolHostStatus.Connected || Status == ProtocolHostStatus.Disconnected;
        }

        #endregion Base Interface


        #region WindowOnResizeEnd

        private readonly Timer _resizeEndTimer = new Timer(500) { Enabled = false, AutoReset = false };
        private readonly object _resizeEndLocker = new object();
        private bool _canAutoResizeByWindowSizeChanged = true;

        /// <summary>
        /// when tab window goes to min from max, base.SizeChanged invoke and size will get bigger, normal to min will not tiger this issue, don't know why.
        /// so stop resize when window status change to min until status restore.
        /// </summary>
        /// <param name="isEnable"></param>
        public override void ToggleAutoResize(bool isEnable)
        {
            lock (_resizeEndLocker)
            {
                _canAutoResizeByWindowSizeChanged = isEnable;
            }
        }

        private void ParentWindowResize_StartWatch()
        {
            lock (_resizeEndLocker)
            {
                _resizeEndTimer.Elapsed -= ResizeEndTimerOnElapsed;
                _resizeEndTimer.Elapsed += ResizeEndTimerOnElapsed;
                base.SizeChanged -= WindowSizeChanged;
                base.SizeChanged += WindowSizeChanged;
            }
        }

        private void ParentWindowResize_StopWatch()
        {
            lock (_resizeEndLocker)
            {
                _resizeEndTimer.Stop();
                _resizeEndTimer.Elapsed -= ResizeEndTimerOnElapsed;
                base.SizeChanged -= WindowSizeChanged;
            }
        }

        private uint _previousWidth = 0;
        private uint _previousHeight = 0;
        private void WindowSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (ParentWindow?.WindowState != WindowState.Minimized
                && _canAutoResizeByWindowSizeChanged
                && this._rdpSettings.RdpWindowResizeMode == ERdpWindowResizeMode.AutoResize)
            {
                // start a timer to resize RDP after 500ms
                var nw = (uint)e.NewSize.Width;
                var nh = (uint)e.NewSize.Height;
                if (nw != _previousWidth || nh != _previousHeight)
                {
                    _previousWidth = (uint)e.NewSize.Width;
                    _previousHeight = (uint)e.NewSize.Height;
                    Execute.OnUIThreadSync(() =>
                    {
                        _loginResizeTimer.Stop();
                        _resizeEndTimer.Stop();
                        _resizeEndTimer.Start();
                    });
                }
            }
        }

        private void ResizeEndTimerOnElapsed(object? sender, ElapsedEventArgs e)
        {
            ReSizeRdpToControlSize();
        }

        #endregion WindowOnResizeEnd

        private void DisposeRdpClient()
        {
            lock (_rdpClientDisposeLock)
            {
                try
                {
                    if (_rdpClient is { IsDisposed: false })
                    {
                        _rdpClient.Dispose();
                    }
                    _rdpClient = null;
                }
                catch (Exception e)
                {
                    SimpleLogHelper.Error($"Error disposing RDP client: {e}");
                }
            }
        }

        private void RdpClientDispose()
        {
            GlobalEventHelper.OnScreenResolutionChanged -= OnScreenResolutionChanged;
            try
            {
                // Use synchronous disposal to ensure the RDP client is fully disposed before continuing
                // This prevents race conditions where the client might be accessed or disposed multiple times
                Execute.OnUIThreadSync(DisposeRdpClient);
            }
            catch (Exception e)
            {
                SimpleLogHelper.Error($"Error scheduling RDP client disposal on UI thread: {e}");
                // 如果UI线程调度失败，直接处理
                DisposeRdpClient();
            }
            SimpleLogHelper.Debug("RDP Host: _rdpClient.Disposed.");
        }




        private const int MOUSE_RELEASE_WAIT_TIMEOUT_MS = 30 * 1000;
        private int _isReSizeRdpToControlSizeRunning = 0;
        /// <summary>
        /// set remote resolution to _rdpClient size if is AutoResize
        /// if focus == false, then set size only if new size != old size
        /// </summary>
        private void ReSizeRdpToControlSize()
        {
            if (!_flagHasConnected
                || _rdpClient?.FullScreen != false
                || _rdpSettings.RdpWindowResizeMode != ERdpWindowResizeMode.AutoResize) return;

            // This used to be a static field guarded by lock(this): the guard was per instance while the
            // flag was shared, so concurrent sessions clobbered each other, and any exception left it stuck
            // at true forever, silently killing auto-resize for every session in the process.
            if (Interlocked.CompareExchange(ref _isReSizeRdpToControlSizeRunning, 1, 0) != 0)
            {
                SimpleLogHelper.Debug($@"ReSizeRdpToControlSize return by isReSizeRdpToControlSizeRunning == true");
                return;
            }

            Task.Factory.StartNew(() =>
            {
                try
                {
                    // Window drag and drop resize only after mouse button release, 当拖动最大化的窗口时，需检测鼠标按键释放后再调整分辨率，详见：https://github.com/1Remote/1Remote/issues/553
                    // Control.MouseButtons reads the input state straight from Win32. The Mouse.LeftButton
                    // check it replaces needed a blocking hop to the UI thread on every iteration, and the
                    // loop had no upper bound.
                    var waitedMs = 0;
                    while ((System.Windows.Forms.Control.MouseButtons & MouseButtons.Left) == MouseButtons.Left)
                    {
                        if (waitedMs >= MOUSE_RELEASE_WAIT_TIMEOUT_MS)
                        {
                            SimpleLogHelper.Warning(@"RDP ReSizeRdpToControlSize: gave up waiting for the mouse button to be released");
                            break;
                        }
                        Thread.Sleep(100);
                        waitedMs += 100;
                    }

                    var nw = (uint)(_rdpClient?.Width ?? 0);
                    var nh = (uint)(_rdpClient?.Height ?? 0);
                    // tip: the control default width is 288
                    if (_rdpClient?.DesktopWidth != nw
                        || _rdpClient?.DesktopHeight != nh)
                    {
                        SetRdpResolution(nw, nh, false);
                    }
                }
                catch (Exception e)
                {
                    SimpleLogHelper.Error(e);
                }
                finally
                {
                    Interlocked.Exchange(ref _isReSizeRdpToControlSizeRunning, 0);
                }
            });
        }


        private uint _lastScaleFactor = 0;
        /// <summary>
        /// if focus == false, then set size only if new size != old size
        /// </summary>
        private void SetRdpResolution(uint w, uint h, bool focus = false)
        {
            if (w <= 0 || h <= 0) return;

            lock (_resizeEndLocker)
            {
                if (_canAutoResizeByWindowSizeChanged == false) return;
            }

            _primaryScaleFactor = ScreenInfoEx.GetPrimaryScreenScaleFactor();
            var newScaleFactor = _primaryScaleFactor;
            if (this._rdpSettings is { IsScaleFactorFollowSystem: false, ScaleFactorCustomValue: { } })
                newScaleFactor = this._rdpSettings.ScaleFactorCustomValue ?? _primaryScaleFactor;
            bool needUpdate = focus
                         || _rdpClient?.DesktopWidth != w
                         || _rdpClient?.DesktopHeight != h
                         || newScaleFactor != _lastScaleFactor;
            if (newScaleFactor != 100)
            {
                // in this case we allow 1pix error
                needUpdate = focus
                        || Math.Abs((int)(_rdpClient?.DesktopWidth ?? 0) - (int)w) > 1
                        || Math.Abs((int)(_rdpClient?.DesktopHeight ?? 0) - (int)h) > 1
                        || newScaleFactor != _lastScaleFactor;
            }
            SimpleLogHelper.Debug($@"SetRdpResolution needUpdate = {needUpdate}, UpdateSessionDisplaySettings, by: W = {_rdpClient?.DesktopWidth} -> {w}, H = {_rdpClient?.DesktopHeight} -> {h}, ScaleFactor = {_lastScaleFactor} -> {newScaleFactor}, focus = {focus}");
            if (needUpdate)
                Execute.OnUIThreadSync(() =>
                {
                    try
                    {
                        _lastScaleFactor = newScaleFactor;
                        _rdpClient?.UpdateSessionDisplaySettings(w, h, w, h, 0, newScaleFactor, 100);
                    }
                    catch (COMException)
                    {
                        // ignore error code 0x8000FFFF
                    }
                    catch (Exception e)
                    {
                        SimpleLogHelper.Error(e);
                    }
                });
        }

        private System.Drawing.Rectangle GetScreenSizeIfRdpIsFullScreen()
        {
            if (_rdpSettings.RdpFullScreenFlag == ERdpFullScreenFlag.EnableFullAllScreens)
            {
                LocalityConnectRecorder.RdpCacheUpdate(_rdpSettings.Id, true, -1);
                return ScreenInfoEx.GetAllScreensSize();
            }

            int screenIndex = LocalityConnectRecorder.RdpCacheGet(_rdpSettings.Id)?.FullScreenLastSessionScreenIndex ?? -1;
            if (screenIndex < 0
                || screenIndex >= System.Windows.Forms.Screen.AllScreens.Length)
            {
                screenIndex = this.ParentWindow != null ? ScreenInfoEx.GetCurrentScreen(this.ParentWindow).Index : ScreenInfoEx.GetCurrentScreenBySystemPosition(ScreenInfoEx.GetMouseSystemPosition()).Index;
            }
            LocalityConnectRecorder.RdpCacheUpdate(_rdpSettings.Id, true, screenIndex);
            return System.Windows.Forms.Screen.AllScreens[screenIndex].Bounds;
        }

        /// <summary>
        /// set the parent window of rdp, if parent window is FullScreenWindowView and it's loaded, go full screen
        /// </summary>
        /// <param name="value"></param>
        public override void SetParentWindow(WindowBase? value)
        {
            base.SetParentWindow(value);
            if (value is FullScreenWindowView && value.IsLoaded && value.IsClosed == false)
            {
                this.GoFullScreen();
            }
        }

        public override void FocusOnMe()
        {
            Execute.OnUIThread(() =>
            {
                // Kill logical focus
                FocusManager.SetFocusedElement(FocusManager.GetFocusScope(RdpHost), null);
                Keyboard.ClearFocus();
                this.Focus();
                RdpHost.Focus();
                if (_rdpClient is { } rdp)
                {
                    // try to fix https://github.com/1Remote/1Remote/issues/530, but failed
                    rdp.Focus();
                    //rdp.Show();
                    //rdp.Update();
                    //rdp.Refresh();
                    //rdp.BringToFront();
                }
            });
        }
    }
}