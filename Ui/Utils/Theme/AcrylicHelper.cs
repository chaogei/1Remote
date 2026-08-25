using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Shawn.Utils;

namespace _1RM.Utils.Theme
{
    /// <summary>
    /// Frosted-glass window backdrop via the undocumented but long-stable
    /// <c>SetWindowCompositionAttribute</c> accent policy.
    ///
    /// This path was chosen over the Windows 11 <c>DWMWA_SYSTEMBACKDROP_TYPE</c> API because the accent
    /// policy is the only one that composites correctly on a layered window, and every window in this app is
    /// <c>WindowStyle=None</c> with <c>AllowsTransparency=True</c>. Switching to the DWM backdrop would mean
    /// rebuilding the drop shadows and rounded corners of a dozen windows that currently rely on WPF
    /// transparency. It also keeps Windows 10 working, which the DWM backdrop does not.
    ///
    /// Everything degrades to "no backdrop, plain opaque window" if the call fails, so an unexpected OS build
    /// can never leave a window unreadable.
    /// </summary>
    public static class AcrylicHelper
    {
        #region interop

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public AccentState AccentState;
            public uint AccentFlags;
            /// <summary>Tint colour, in 0xAABBGGRR.</summary>
            public uint GradientColor;
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public WindowCompositionAttribute Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        private enum AccentState
        {
            Disabled = 0,
            EnableGradient = 1,
            EnableTransparentGradient = 2,
            EnableBlurBehind = 3,
            EnableAcrylicBlurBehind = 4,
        }

        private enum WindowCompositionAttribute
        {
            AccentPolicy = 19,
        }

        /// <summary>Draw the accent on all four edges instead of leaving a hairline gap.</summary>
        private const uint DRAW_ALL_BORDERS = 0x20 | 0x40 | 0x80 | 0x100;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);

        private const uint RDW_INVALIDATE = 0x0001;
        private const uint RDW_ERASE = 0x0004;
        private const uint RDW_ALLCHILDREN = 0x0080;
        private const uint RDW_UPDATENOW = 0x0100;
        private const uint RDW_FRAME = 0x0400;

        #endregion

        /// <summary>
        /// Applies the backdrop to a window that already has a handle. Safe to call repeatedly.
        ///
        /// Windows 11 (build 22000+) first tries <c>EnableAcrylicBlurBehind</c>; if the OS rejects it, the
        /// call falls back to <c>EnableBlurBehind</c>. Windows 10 goes directly to <c>EnableBlurBehind</c>.
        ///
        /// Capability is probed by calling the API rather than by comparing OS versions: under net48
        /// <c>Environment.OSVersion</c> is shimmed to 6.2 unless the manifest opts in, so a version check
        /// would silently disable the backdrop on the very machines that support it.
        /// </summary>
        /// <param name="window">Target window.</param>
        /// <param name="tint">Tint colour; its alpha channel controls how opaque the glass reads.</param>
        /// <returns>True when the OS accepted the backdrop.</returns>
        public static bool Apply(Window? window, Color tint)
        {
            if (window == null) return false;
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return false;

            return (SupportsSmoothAcrylic && Apply(handle, tint, AccentState.EnableAcrylicBlurBehind))
                   || Apply(handle, tint, AccentState.EnableBlurBehind);
        }

        /// <summary>
        /// Windows 11 composites acrylic without re-sampling the desktop on every frame. Windows 10 does,
        /// which is what made dragging the window stutter.
        ///
        /// The earlier attempt swapped acrylic for the cheap blur only for the duration of a drag, but the
        /// two look different, so the window visibly changed appearance the moment the drag started and
        /// again when it ended. The normal OS-specific choice now stays in place for the window's lifetime,
        /// which is both consistent and still frosted — Windows 10 gets the lighter Aero-style blur, while
        /// Windows 11 uses acrylic unless that accent state is rejected and the call falls back to blur.
        ///
        /// Under net48 Environment.OSVersion is shimmed to 6.2 unless the manifest opts in, so that target
        /// falls through to blur-behind. That is the safe direction to be wrong in.
        /// </summary>
        private static bool SupportsSmoothAcrylic => Environment.OSVersion.Version.Build >= 22000;

        /// <summary>
        /// Repaints the window, children and frame included.
        ///
        /// With a transparent composition target WPF only redraws what it believes is dirty, and on the way
        /// back from the tray it believes almost nothing is: the window reappears as a bare translucent
        /// panel, and controls only surface one at a time as the mouse passes over them and invalidates
        /// them. Asking the window manager for a full repaint is what actually clears that.
        /// </summary>
        public static void ForceRedraw(Window? window)
        {
            if (window == null) return;
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            RedrawWindow(handle, IntPtr.Zero, IntPtr.Zero,
                RDW_INVALIDATE | RDW_ERASE | RDW_ALLCHILDREN | RDW_UPDATENOW | RDW_FRAME);
        }

        public static void Clear(Window? window)
        {
            if (window == null) return;
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            Apply(handle, Colors.Transparent, AccentState.Disabled);
        }

        private static bool Apply(IntPtr handle, Color tint, AccentState state)
        {
            var policy = new AccentPolicy
            {
                AccentState = state,
                AccentFlags = state == AccentState.Disabled ? 0 : DRAW_ALL_BORDERS,
                // the struct wants 0xAABBGGRR, which is byte-reversed from WPF's ARGB. Each byte is widened to
                // uint before it is shifted: packing in int instead makes any alpha of 0x80 or more negative,
                // and this assembly's checked arithmetic rejects that on the way back to uint.
                GradientColor = ((uint)tint.A << 24) | ((uint)tint.B << 16) | ((uint)tint.G << 8) | tint.R,
                AnimationId = 0,
            };

            var size = Marshal.SizeOf(policy);
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(policy, buffer, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WindowCompositionAttribute.AccentPolicy,
                    SizeOfData = size,
                    Data = buffer,
                };
                return SetWindowCompositionAttribute(handle, ref data) != 0;
            }
            catch (Exception e)
            {
                SimpleLogHelper.Warning($"AcrylicHelper: {e.Message}");
                return false;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }
}
