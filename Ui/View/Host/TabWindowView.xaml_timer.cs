using System.Runtime.InteropServices;
using System.Timers;
using System;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using _1RM.Model.Protocol;
using _1RM.Service;
using _1RM.View.Host.ProtocolHosts;
using Shawn.Utils;
using Stylet;
using ProtocolHostType = _1RM.View.Host.ProtocolHosts.ProtocolHostType;
using Timer = System.Timers.Timer;

namespace _1RM.View.Host
{
    public partial class TabWindowView
    {
        private readonly Timer _timer4CheckForegroundWindow = new Timer();
        private bool _isForegroundWatchHooked;
        /// <summary>Mirrors window visibility for the timer thread, which cannot read a dependency property.</summary>
        private volatile bool _isForegroundWatchWanted = true;
        /// <summary>
        /// Set by TimerDispose before the timer is torn down. A tick that is already running on a thread pool
        /// thread must see this and not re-arm the timer: Start() on a disposed timer throws
        /// ObjectDisposedException, which System.Timers.Timer swallows on the way out of the handler, so
        /// closing a tab window quietly burned an exception on every single teardown.
        /// </summary>
        private volatile bool _isTimerDisposing;

        private void TimerInitOnLoaded()
        {
            _timer4CheckForegroundWindow.Interval = 100;
            _timer4CheckForegroundWindow.AutoReset = false;
            if (!_isForegroundWatchHooked)
            {
                // Loaded can fire again when the view is re-attached or a tab is dragged out and back;
                // subscribing each time would stack handlers and do the work twice per tick.
                _timer4CheckForegroundWindow.Elapsed += Timer4CheckForegroundWindowOnElapsed;
                IsVisibleChanged += OnVisibleChangedForForegroundWatch;
                _isForegroundWatchHooked = true;
            }
            TimerStartIfAlive();
        }

        /// <summary>
        /// The only place allowed to arm the timer. Reading the flag and calling Start() cannot be made atomic
        /// — the window can close on the UI thread in between while a tick runs on a thread pool thread — so
        /// the flag removes the common case and the catch covers the remaining window.
        /// </summary>
        private void TimerStartIfAlive()
        {
            if (_isTimerDisposing) return;
            try
            {
                _timer4CheckForegroundWindow.Start();
            }
            catch (ObjectDisposedException)
            {
                // Disposed between the check above and the call; the window is going away, nothing to do.
            }
        }

        /// <summary>
        /// A tab window with no sessions left is hidden, not closed, so it can be reused. Without this it
        /// went on polling the foreground window ten times a second forever.
        /// </summary>
        private void OnVisibleChangedForForegroundWatch(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsClosing) return;
            _isForegroundWatchWanted = e.NewValue is true;
            if (_isForegroundWatchWanted)
                TimerStartIfAlive();
            else
                _timer4CheckForegroundWindow.Stop();
        }

        private void TimerDispose()
        {
            // Order matters. The flag goes up first so a tick already in flight skips its re-arm, then Stop()
            // makes sure no further tick is scheduled, and only then is the timer disposed. Stop() on an
            // already disposed timer is harmless — only the enabling path throws.
            _isTimerDisposing = true;
            _isForegroundWatchWanted = false;
            IsVisibleChanged -= OnVisibleChangedForForegroundWatch;
            _timer4CheckForegroundWindow.Elapsed -= Timer4CheckForegroundWindowOnElapsed;
            _timer4CheckForegroundWindow.Stop();
            _timer4CheckForegroundWindow.Dispose();
        }

        private IntPtr _lastActivatedWindowHandle = IntPtr.Zero;

        private void Timer4CheckForegroundWindowOnElapsed(object? sender, ElapsedEventArgs e)
        {
            // AutoReset is false, so this tick owns the re-arm; the timer stays stopped if we bail out.
            if (_isTimerDisposing) return;
            _timer4CheckForegroundWindow.Stop();
            try
            {
                RunForRdpV2();
                RunForIntegrate();
            }
            catch (Exception ex)
            {
                SimpleLogHelper.Warning(ex);
            }
            finally
            {
                if (_isForegroundWatchWanted && !_isTimerDisposing)
                    TimerStartIfAlive();
            }
        }


        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        /// <summary>
        /// 0. Record the current ActivatedWindowHandle every time
        /// 1. If the current ActivatedWindowHandle is the integrated exe, move the Tab to the foreground one time (BringWindowToTop(_myHandle);, achieving that after clicking the integrated exe, the tab is brought to the front and not obscured by other programs.
        /// 2. If isTimer is False and the current focus is on the Tab, then set the focus on the integrated exe. (To ensure that the focus is not lost after clicking on the tab label)
        /// </summary>
        private void RunForIntegrate()
        {
            bool isIntegrate = Vm?.SelectedItem?.Content?.GetProtocolHostType() == ProtocolHostType.Integrate;
            IntPtr hWnd = IntPtr.Zero;
            if (isIntegrate)
            {
                try
                {
                    hWnd = this.Vm.SelectedItem.Content.GetHostHwnd();
                }
                catch (Exception ex)
                {
                    SimpleLogHelper.Warning($"Failed to get host hwnd: {ex.Message}");
                }
            }

            var nowActivatedWindowHandle = GetForegroundWindow();
            if (hWnd != IntPtr.Zero)
            {
                //SimpleLogHelper.Debug($"TabWindowView: isTimer = {isTimer}, nowActivatedWindowHandle = {nowActivatedWindowHandle}, _lastActivatedWindowHandle = {_lastActivatedWindowHandle}, _myHandle = {_myHandle}");
                // bring Tab window to top, when the host content is Integrate.
                if (nowActivatedWindowHandle == hWnd && _lastActivatedWindowHandle != hWnd)
                {
                    SimpleLogHelper.Debug($@"TabWindowView.RunForIntegrate: BringWindowToTop({_myHandle})");
                    BringWindowToTop(_myHandle);
                }
            }

            // focus content when tab is focused when the focus is back to tab window
            if (nowActivatedWindowHandle == _myHandle && _lastActivatedWindowHandle != _myHandle
                                                      && !(isIntegrate && System.Windows.Forms.Control.MouseButtons == MouseButtons.Left))
            {
                SimpleLogHelper.Debug($@"TabWindowView.RunForIntegrate: Vm?.SelectedItem?.Content?.FocusOnMe()");
                Vm?.SelectedItem?.Content?.FocusOnMe();
            }
            _lastActivatedWindowHandle = nowActivatedWindowHandle;
        }

        /****
         * THE PURPOSE OF THIS FUNCTION IS TO:
         * - LET YOUR LOCAL DESKTOP WINDOW GET FOCUS WHEN YOU MOVE THE CURSOR OUT OF THE RDP WINDOW
         * - LET THE RDP WINDOW GET FOCUS WHEN YOU MOVE THE CURSOR INTO THE RDP WINDOW
         * - CAUTION: PAY ATTENTION TO THE RESIZE OF THE RDP WINDOW, IT MAY CAUSE THE CURSOR TO MOVE OUT OF THE RDP WINDOW, SO WE NEED TO CHECK IF THE LEFT MOUSE BUTTON IS PRESSED OR NOT
         ***/

        #region RunForRdp

        [StructLayout(LayoutKind.Sequential)]
        internal struct Win32Point
        {
            public Int32 X;
            public Int32 Y;
        };

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetCursorPos(ref Win32Point pt);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(Win32Point point);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

        private const uint GaRoot = 2;

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        [StructLayout(LayoutKind.Sequential)]
        internal struct Win32Rect
        {
            public Int32 Left;
            public Int32 Top;
            public Int32 Right;
            public Int32 Bottom;
        };

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out Win32Rect lpRect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        /// <summary>
        /// Runs on the 100ms timer thread. Deliberately pure Win32: the previous version read the bounds
        /// through PointToScreen behind a blocking Execute.OnUIThreadSync, so every single tick waited on
        /// the UI thread once the session was connected. GetWindowRect already reports physical screen
        /// pixels, the same space GetCursorPos uses, so no dispatch and no DPI conversion is needed.
        /// </summary>
        private bool IsMouseInside()
        {
            var myHandle = _myHandle;
            if (myHandle == IntPtr.Zero)
                return false;
            if (!IsWindowVisible(myHandle) || IsIconic(myHandle))
                return false;

            var w32Mouse = new Win32Point();
            if (!GetCursorPos(ref w32Mouse))
                return false;
            if (!GetWindowRect(myHandle, out var rect))
                return false;

            if (w32Mouse.X < rect.Left || w32Mouse.X > rect.Right || w32Mouse.Y < rect.Top || w32Mouse.Y > rect.Bottom)
                return false;

            var hitWindow = WindowFromPoint(w32Mouse);
            if (hitWindow == IntPtr.Zero)
                return false;

            var hitRoot = GetAncestor(hitWindow, GaRoot);
            return hitRoot == IntPtr.Zero || hitRoot == myHandle;
        }


        private void RunForRdpV2()
        {
            if (Vm?.SelectedItem?.Content?.ProtocolServer.Protocol != RDP.ProtocolName)
                return;
            //if (Vm?.SelectedItem?.Content is not IntegrateHostForWinFrom ihfw)
            //    return;
            if (Vm?.SelectedItem?.Content?.Status != ProtocolHosts.ProtocolHostStatus.Connected)
                return;

            if (!IoC.Get<ConfigurationService>().General.TabWindowSetFocusToLocalDesktopOnMouseLeaveRdpWindow)
                return;

            // An RDP session can also be hosted by an external runner, and then there is no ActiveX window
            // to hand the focus to. This used to throw NotImplementedException, which the timer caught and
            // logged 10 times a second — a disk write and a global log lock per tick.
            if (Vm?.SelectedItem?.Content is not AxMsRdpClient09Host)
                return;

            var rdpHandle = _myHandle;
            if (rdpHandle == IntPtr.Zero)
                return;

            var nowActivatedWindowHandle = GetForegroundWindow();
            if (IsMouseInside())
            {
                if (nowActivatedWindowHandle != rdpHandle)
                {
                    SimpleLogHelper.Debug("TabWindowView.RunForRdpV2: SetForegroundWindow(rdpHandle)");
                    SetForegroundWindow(rdpHandle);
                }
            }
            else if (nowActivatedWindowHandle == rdpHandle)
            {
                // !isMousePressed is to fix the resizing bug introduced by #648
                // Stay focused while the mouse is pressed to avoid losing focus when resizing the RDP window,
                // see https://github.com/1Remote/1Remote/issues/797 for more details
                bool isMousePressed = System.Windows.Forms.Control.MouseButtons == MouseButtons.Left
                                      || System.Windows.Forms.Control.MouseButtons == MouseButtons.Right
                                      || System.Windows.Forms.Control.MouseButtons == MouseButtons.Middle;
                if (!isMousePressed)
                {
                    // RDP has focus AND mouse is not inside the tab window, then switch focus to desktop, user input will not be sent to RDP.
                    SimpleLogHelper.Debug("TabWindowView.RunForRdpV2: SetForegroundWindow(desktop)");
                    SetForegroundWindow(GetDesktopWindow());
                }
            }
        }

        #endregion
    }
}