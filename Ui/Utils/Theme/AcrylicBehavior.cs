using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
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
        private static readonly HashSet<Window> Registered = new HashSet<Window>();

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
                if (!Registered.Add(window)) return;
                window.SourceInitialized += OnSourceInitialized;
                window.Closed += OnClosed;
                // a window that already has a handle never raises SourceInitialized again
                Apply(window);
            }
            else
            {
                if (!Registered.Remove(window)) return;
                window.SourceInitialized -= OnSourceInitialized;
                window.Closed -= OnClosed;
                AcrylicHelper.Clear(window);
            }
        }

        private static void OnSourceInitialized(object? sender, EventArgs e) => Apply(sender as Window);

        private static void OnClosed(object? sender, EventArgs e)
        {
            if (sender is not Window window) return;
            window.SourceInitialized -= OnSourceInitialized;
            window.Closed -= OnClosed;
            Registered.Remove(window);
        }

        /// <summary>
        /// Restains every registered window. Cheap enough to call on each theme change.
        /// </summary>
        public static void RefreshAll()
        {
            foreach (var window in Registered.ToArray())
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
