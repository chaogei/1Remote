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

        private void TimerInitOnLoaded()
        {
            _timer4CheckForegroundWindow.Interval = 100;
            _timer4CheckForegroundWindow.AutoReset = false;
            _timer4CheckForegroundWindow.Elapsed += Timer4CheckForegroundWindowOnElapsed;
            _timer4CheckForegroundWindow.Start();
        }

        private void TimerDispose()
        {
            try
            {
                _timer4CheckForegroundWindow?.Dispose();
            }
            finally
            {
            }
        }

        private IntPtr _lastActivatedWindowHandle = IntPtr.Zero;

        private void Timer4CheckForegroundWindowOnElapsed(object? sender, ElapsedEventArgs e)
        {
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
                _timer4CheckForegroundWindow.Start();
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