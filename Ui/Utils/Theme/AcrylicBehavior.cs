using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Shawn.Utils;

namespace _1RM.Utils.Theme
{
    /// <summary>
    /// Opt a window into the frosted backdrop from XAML:
    /// <code>theme:AcrylicBehavior.IsEnabled="True"</code>
    ///
    /// The tint is read from the <c>AcrylicTintColor</c> application resource, so it follows whatever the
    /// user picked in the theme settings. Call <see cref="RefreshAll"/> after swapping the theme dictionary
    /// to restain the windows that are already open.
    /// </summary>
    public static class AcrylicBehavior
    {
        private const string TINT_RESOURCE_KEY = "AcrylicTintColor";
        private const string BACKDROP_RESOURCE_KEY = "WindowBackdropBrush";

        private const int WM_ENTERSIZEMOVE = 0x0231;
        private const int WM_EXITSIZEMOVE = 0x0232;

        private static readonly Dictionary<Window, HwndSource?> Registered = new Dictionary<Window, HwndSource?>();

        public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
            "IsEnabled", typeof(bool), typeof(AcrylicBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

        public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);
        public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not Window window) return;

            if (e.NewValue is true)
            {
                if (Registered.ContainsKey(window)) return;
                Registered[window] = null;
                window.SourceInitialized += OnSourceInitialized;
                window.Closed += OnClosed;
                // a window that already has a handle never raises SourceInitialized again
                Attach(window);
            }
            else
            {
                Detach(window);
                AcrylicHelper.Clear(window);
            }
        }

        private static void OnSourceInitialized(object? sender, EventArgs e) => Attach(sender as Window);

        private static void OnClosed(object? sender, EventArgs e) => Detach(sender as Window);

        private static void Attach(Window? window)
        {
            if (window == null || !Registered.ContainsKey(window)) return;

            if (Registered[window] == null)
            {
                var handle = new WindowInteropHelper(window).Handle;
                if (handle != IntPtr.Zero && HwndSource.FromHwnd(handle) is { } source)
                {
                    source.AddHook(SizeMoveHook);
                    Registered[window] = source;
                }
            }

            Apply(window);
        }

        private static void Detach(Window? window)
        {
            if (window == null || !Registered.TryGetValue(window, out var source)) return;
            window.SourceInitialized -= OnSourceInitialized;
            window.Closed -= OnClosed;
            source?.RemoveHook(SizeMoveHook);
            Registered.Remove(window);
        }

        /// <summary>
        /// Swaps acrylic for the cheap blur while the window is being moved or resized. Acrylic re-samples
        /// the desktop behind the window on every frame, which on Windows 10 drops a drag to a few frames a
        /// second; plain blur does not.
        /// </summary>
        private static IntPtr SizeMoveHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != WM_ENTERSIZEMOVE && msg != WM_EXITSIZEMOVE)
                return IntPtr.Zero;

            var window = Registered.Keys.FirstOrDefault(w => new WindowInteropHelper(w).Handle == hwnd);
            if (window == null)
                return IntPtr.Zero;

            var tint = ResolveTint();
            if (tint.A > 0)
                AcrylicHelper.Apply(window, tint, preferAcrylic: msg == WM_EXITSIZEMOVE);
            return IntPtr.Zero;
        }

        /// <summary>
        /// Restains every registered window. Cheap enough to call on each theme change.
        /// </summary>
        public static void RefreshAll()
        {
            foreach (var window in Registered.Keys.ToArray())
            {
                Apply(window);
            }
        }

        private static void Apply(Window? window)
        {
            if (window == null) return;
            try
            {
                var tint = ResolveTint();
                var applied = tint.A > 0 && AcrylicHelper.Apply(window, tint);

                // DWM now paints the tint behind this window, so its own surface has to get out of the way.
                // Scoped to Window.Resources rather than the app dictionary on purpose: if the OS refused the
                // call, or the theme has the backdrop switched off, the window keeps the opaque app-level
                // brush and stays readable.
                if (applied)
                    window.Resources[BACKDROP_RESOURCE_KEY] = Brushes.Transparent;
                else
                    window.Resources.Remove(BACKDROP_RESOURCE_KEY);

                SimpleLogHelper.Info($"AcrylicBehavior: backdrop {(applied ? "applied" : "not applied")} to {window.GetType().Name}, tint = {tint}");
            }
            catch (Exception ex)
            {
                SimpleLogHelper.Warning($"AcrylicBehavior: {ex.Message}");
            }
        }

        private static Color ResolveTint()
        {
            if (Application.Current?.TryFindResource(TINT_RESOURCE_KEY) is Color tint)
                return tint;
            // a fully transparent tint means "no backdrop", which is the safe default
            return Color.FromArgb(0x00, 0x00, 0x00, 0x00);
        }
    }
}
