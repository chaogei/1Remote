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

        #endregion

        /// <summary>
        /// Applies the backdrop to a window that already has a handle. Safe to call repeatedly.
        ///
        /// Capability is probed by calling the API rather than by comparing OS versions: under net48
        /// <c>Environment.OSVersion</c> is shimmed to 6.2 unless the manifest opts in, so a version check
        /// would silently disable the backdrop on the very machines that support it.
        /// </summary>
        /// <param name="window">Target window. Must be <c>AllowsTransparency=True</c> to composite.</param>
        /// <param name="tint">Tint colour; its alpha channel controls how opaque the glass reads.</param>
        /// <returns>True when the OS accepted the backdrop.</returns>
        public static bool Apply(Window? window, Color tint)
        {
            if (window == null) return false;
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return false;

            // acrylic from Windows 10 1803; older builds still take the cheaper blur-behind
            return Apply(handle, tint, AccentState.EnableAcrylicBlurBehind)
                   || Apply(handle, tint, AccentState.EnableBlurBehind);
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
                // the struct wants 0xAABBGGRR, which is byte-reversed from WPF's ARGB
                GradientColor = (uint)((tint.A << 24) | (tint.B << 16) | (tint.G << 8) | tint.R),
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
