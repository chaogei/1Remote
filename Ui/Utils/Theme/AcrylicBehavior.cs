using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Shawn.Utils;

namespace _1RM.Utils.Theme
{
    /// <summary>
    /// Opt a window into the frosted backdrop from XAML:
    /// <code>theme:AcrylicBehavior.IsEnabled="True"</code>
    ///
    /// WPF implicit styles match the exact runtime type, so a setter on <c>WindowChromeBaseBaseStyle</c>
    /// only reaches windows that reference that style by key (MainWindow, ErrorReport). Every other
    /// <c>WindowChromeBase</c> dialog is opted in from a <c>Window.Loaded</c> class handler. Session
    /// hosts and the crash reporter stay on the denylist — a window added there needs no change on
    /// the view side.
    ///
    /// The tint is read from the <c>AcrylicTintColor</c> application resource, so it follows whatever the
    /// user picked in the theme settings. Call <see cref="RefreshAll"/> after swapping the theme dictionary
    /// to restain the windows that are already open.
    /// </summary>
    public static class AcrylicBehavior
    {
        private const string TINT_RESOURCE_KEY = "AcrylicTintColor";
        private const string BACKDROP_RESOURCE_KEY = "WindowBackdropBrush";
        private const string GLASS_PANEL_KEY = "GlassPanelBrush";
        private const string GLASS_CONTENT_KEY = "GlassContentBrush";
        private const string ACCENT_GLASS_KEY = "AccentGlassBrush";
        private const int SM_REMOTESESSION = 0x1000;

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        private static readonly HashSet<Window> Registered = new HashSet<Window>();

        /// <summary>Last outcome per window, so the log records transitions rather than every attempt.</summary>
        private static readonly Dictionary<Window, bool> LastApplied = new Dictionary<Window, bool>();

        static AcrylicBehavior()
        {
            // Coming back from a remote session (or plugging in a display, or toggling high contrast) can change whether frost is safe.
            // Re-evaluate rather than leaving a washed-out main window, or leaving acrylic off after logout.
            try
            {
                SystemEvents.SessionSwitch += (_, _) => TryRefreshAll();
                SystemEvents.DisplaySettingsChanged += (_, _) => TryRefreshAll();
                // UserPreferenceChanged fires for many unrelated SPI changes; only contrast / colour /
                // visual-style switches can make acrylic unsafe or restore it.
                SystemEvents.UserPreferenceChanged += (_, e) =>
                {
                    if (e.Category is UserPreferenceCategory.Color
                        or UserPreferenceCategory.Accessibility
                        or UserPreferenceCategory.VisualStyle)
                        TryRefreshAll();
                };
            }
            catch (Exception e)
            {
                SimpleLogHelper.Debug($"AcrylicBehavior: could not subscribe to session events, {e.Message}");
            }

            // Derived WindowChromeBase types do not pick up the implicit style on WindowChromeBase
            // (WPF matches TargetType exactly). Loaded is the one hook that every Window raises.
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnAnyWindowLoaded));
        }

        public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
            "IsEnabled", typeof(bool), typeof(AcrylicBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

        public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);
        public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

        private static void OnAnyWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Window window) return;
            if (!ShouldAttachAcrylic(window)) return;
            SetIsEnabled(window, true);
        }

        /// <summary>
        /// Chrome dialogs and the first-run guide. Session hosts stay opaque because they embed a
        /// remote-desktop HWND; the crash reporter draws a 40px transparent halo around a custom
        /// template, so DWM frost there becomes a square bloom (or a black slab when skip forces
        /// a non-transparent composition target).
        /// </summary>
        private static bool ShouldAttachAcrylic(Window window)
        {
            if (IsAcrylicDeniedWindow(window)) return false;
            var name = window.GetType().Name;
            if (name == "GuidanceWindow") return true;
            for (var t = window.GetType(); t != null; t = t.BaseType)
            {
                if (t.Name == "WindowChromeBase")
                    return true;
            }
            return false;
        }

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not Window window) return;

            if (e.NewValue is true)
            {
                // Remote session windows host MSTSC / VNC / IntegrateHost inside a WindowsFormsHost.
                // DWM acrylic plus a transparent composition target paints a white bloom over that
                // child HWND on some GPUs, nested RDP sessions and HDR displays — skip them here
                // so re-enabling the XAML flag cannot bring the fog back.
                if (IsAcrylicDeniedWindow(window))
                {
                    SimpleLogHelper.Info($"AcrylicBehavior: skipped {window.GetType().Name} (hosted HWND or crash reporter must stay opaque)");
                    return;
                }
                if (!Registered.Add(window)) return;
                Hook(window);
                // a window that already has a handle never raises SourceInitialized again
                Apply(window);
            }
            else
            {
                Detach(window);
                AcrylicHelper.Clear(window);
            }
        }

        /// <summary>
        /// Unsubscribe first so this is safe to call more than once for the same window.
        /// </summary>
        private static void Hook(Window window)
        {
            window.SourceInitialized -= OnSourceInitialized;
            window.SourceInitialized += OnSourceInitialized;
            window.Closed -= OnClosed;
            window.Closed += OnClosed;
            // closing to the tray hides the window rather than closing it, and restoring can come back
            // either as a visibility change or as a state change, so both are watched
            window.IsVisibleChanged -= OnIsVisibleChanged;
            window.IsVisibleChanged += OnIsVisibleChanged;
            window.StateChanged -= OnStateChanged;
            window.StateChanged += OnStateChanged;
        }

        private static void Detach(Window? window)
        {
            if (window == null) return;
            window.SourceInitialized -= OnSourceInitialized;
            window.Closed -= OnClosed;
            window.IsVisibleChanged -= OnIsVisibleChanged;
            window.StateChanged -= OnStateChanged;
            Registered.Remove(window);
            LastApplied.Remove(window);
        }

        private static void OnSourceInitialized(object? sender, EventArgs e) => Apply(sender as Window);

        private static void OnClosed(object? sender, EventArgs e) => Detach(sender as Window);

        private static void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is true)
                Restore(sender as Window);
        }

        private static void OnStateChanged(object? sender, EventArgs e)
        {
            if (sender is Window window && window.WindowState != WindowState.Minimized)
                Restore(window);
        }

        /// <summary>
        /// Re-applies the backdrop and forces a full repaint after the window comes back into view. Queued
        /// at Loaded priority so it runs once WPF has laid the window out again — redrawing before that
        /// would just repaint the same stale surface.
        /// </summary>
        private static void Restore(Window? window)
        {
            if (window == null || !Registered.Contains(window)) return;
            window.Dispatcher.BeginInvoke(new Action(() =>
            {
                Apply(window);
                AcrylicHelper.ForceRedraw(window);
            }), DispatcherPriority.Loaded);
        }

        /// <summary>
        /// Restains every registered window. Cheap enough to call on each theme change.
        /// </summary>
        public static void RefreshAll()
        {
            foreach (var window in Registered.ToArray())
            {
                Apply(window);
                AcrylicHelper.ForceRedraw(window);
            }
        }

        private static void TryRefreshAll()
        {
            try
            {
                Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    // Opaque Glass* first, then restain HWNDs, so a remote/high-contrast flip cannot
                    // leave one frame of 70% cards over an unblurred desktop.
                    // Resolve ThemeService via IoC / AppInit (not a static event) so the extra
                    // ThemeService constructed by first-run guidance cannot leak a subscriber.
                    var themeService = _1RM.IoC.TryGet<_1RM.Service.ThemeService>()
                                       ?? _1RM.AppInitHelper.ThemeServiceObj;
                    if (themeService != null)
                        themeService.ApplyTheme(themeService.CurrentTheme);
                    else
                        RefreshAll();
                }));
            }
            catch (Exception)
            {
                // dispatcher gone during shutdown
            }
        }

        /// <summary>
        /// Windows that must never receive DWM frost, even if a style setter or class handler asks.
        /// Matched by type name to keep this helper from taking a dependency on the host views.
        ///
        /// The two session hosts are here for a reason that no amount of tuning on the view side fixes.
        /// The accent policy honours per-pixel alpha across the entire window, and <see cref="Apply"/> has
        /// to clear the composition target for the tint to reach the client area at all. The remote
        /// control inside the WindowsFormsHost is a child HWND that paints with GDI, and GDI writes its
        /// pixels with the alpha byte left at zero — DWM then reads the whole session rectangle as
        /// transparent and blurs the desktop through it, which is the white fog. Painting an opaque WPF
        /// rectangle underneath does not help: the child overwrites those pixels, alpha and all.
        ///
        /// Frosting only the title strip would mean giving the chrome its own top level HWND and punching
        /// the strip out of this window's region, so that the acrylic samples the desktop rather than this
        /// window's own fill. Until then the strip fakes the material with TitleBarGlassBrush.
        ///
        /// The crash reporter and the launcher are here for the same reason as each other: both are layered
        /// windows that draw a rounded card inside a transparent gutter, and the accent policy paints the
        /// whole HWND rectangle — gutter included, with DRAW_ALL_BORDERS on all four edges. It does not
        /// stop at the card, so the gutter that exists only to give the drop shadow room comes out as a
        /// tinted frame around the card, and the card's own translucent fill then reads as a second,
        /// brighter window nested inside the first. Both draw a single opaque card instead.
        /// </summary>
        private static bool IsAcrylicDeniedWindow(Window window)
        {
            var name = window.GetType().Name;
            return name is "TabWindowView" or "FullScreenWindowView" or "ErrorReportWindow" or "LauncherWindowView";
        }

        /// <summary>
        /// Frost on the main window washes out under nested RDP / Terminal Services: DWM samples a remote
        /// framebuffer instead of the real desktop, and a transparent composition target then composites
        /// that bloom over the chrome. High contrast already supplies its own background.
        ///
        /// WPF's SystemParameters.IsRemoteSession caches CacheSlot.IsRemoteSession indefinitely because
        /// SystemParameters does not invalidate that slot (no InvalidateProperty in SystemParameters.cs).
        /// Therefore, we P/Invoke GetSystemMetrics(SM_REMOTESESSION) directly on each call instead of
        /// using SystemParameters.IsRemoteSession. HighContrast is preserved from SystemParameters since
        /// its cache slot is properly invalidated upon UserPreferenceChanged.
        /// </summary>
        public static bool ShouldSkipAcrylic()
        {
            try
            {
                var isRemoteSession = GetSystemMetrics(SM_REMOTESESSION) != 0;
                return SystemParameters.HighContrast || isRemoteSession;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void Apply(Window? window)
        {
            if (window == null || IsAcrylicDeniedWindow(window)) return;
            try
            {
                if (ShouldSkipAcrylic())
                {
                    AcrylicHelper.Clear(window);
                    window.Resources.Remove(BACKDROP_RESOURCE_KEY);
                    SetLocalGlassOpaque(window, true);
                    SetCompositionTargetTransparent(window, false);
                    RecordApplied(window, false, "skipped (remote session or high contrast)");
                    return;
                }

                var tint = ResolveTint();
                var applied = tint.A > 0 && AcrylicHelper.Apply(window, tint);

                // DWM now paints the tint behind this window, so its own surface has to get out of the way.
                // Scoped to Window.Resources rather than the app dictionary on purpose: if the OS refused the
                // call, or the theme has the backdrop switched off, the window keeps the opaque app-level
                // brush and stays readable. Card dialogs paint with GlassPanelBrush on a transparent root,
                // so a failed accent policy must also snap those brushes opaque or they wash out over the
                // unblurred desktop.
                if (applied)
                {
                    window.Resources[BACKDROP_RESOURCE_KEY] = Brushes.Transparent;
                    SetLocalGlassOpaque(window, false);
                }
                else
                {
                    window.Resources.Remove(BACKDROP_RESOURCE_KEY);
                    SetLocalGlassOpaque(window, true);
                }

                SetCompositionTargetTransparent(window, applied);

                RecordApplied(window, applied, $"backdrop {(applied ? "applied" : "NOT applied")}, tint = {tint}");
            }
            catch (Exception ex)
            {
                try
                {
                    SetLocalGlassOpaque(window, true);
                    window.Resources.Remove(BACKDROP_RESOURCE_KEY);
                    SetCompositionTargetTransparent(window, false);
                }
                catch (Exception)
                {
                    // best-effort fallback
                }
                SimpleLogHelper.Warning($"AcrylicBehavior: {ex.Message}");
            }
        }

        private static void RecordApplied(Window window, bool applied, string detail)
        {
            // Only when the answer changes. Apply runs on every show, every theme switch and every tick of
            // a colour slider, and logging each one at Warning buried the rest of the log - the crash
            // report's "recent log" section was nothing but these lines.
            if (LastApplied.TryGetValue(window, out var previous) && previous == applied) return;
            LastApplied[window] = applied;
            SimpleLogHelper.Info($"AcrylicBehavior: {detail} on {window.GetType().Name}");
        }

        /// <summary>
        /// The step that actually makes the backdrop visible.
        ///
        /// This window is not <c>AllowsTransparency</c>, so WPF renders it onto an opaque surface and a
        /// translucent brush composites against that surface rather than against what DWM painted behind the
        /// window. The backdrop was therefore only visible in the thin glass-frame margin that WindowChrome
        /// extends — the rest of the client area stayed solid no matter what opacity was chosen.
        ///
        /// Clearing the composition target's background colour gives the whole client area an alpha channel,
        /// which is what lets the acrylic show through everywhere.
        /// </summary>
        private static void SetCompositionTargetTransparent(Window window, bool transparent)
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            var target = HwndSource.FromHwnd(handle)?.CompositionTarget;
            if (target == null) return;
            if (transparent)
            {
                target.BackgroundColor = Colors.Transparent;
                return;
            }

            // Layered windows (AllowsTransparency) paint their own alpha. Forcing Black here fills the
            // entire HWND — including the 40–50px shadow gutter around card dialogs — as a solid slab.
            // Leave the target transparent and let the now-opaque GlassPanelBrush cards read as cards.
            target.BackgroundColor = window.AllowsTransparency ? Colors.Transparent : Colors.Black;
        }

        /// <summary>
        /// Card dialogs fill with <c>GlassPanelBrush</c>, whose app-level alpha follows the theme slider
        /// rather than whether this HWND actually received a backdrop. When frost is off for this window,
        /// copy the always-opaque Solid* brushes into the window dictionary so those lookups cannot
        /// composite against the desktop.
        /// </summary>
        private static void SetLocalGlassOpaque(Window window, bool opaque)
        {
            if (!opaque)
            {
                window.Resources.Remove(GLASS_PANEL_KEY);
                window.Resources.Remove(GLASS_CONTENT_KEY);
                window.Resources.Remove(ACCENT_GLASS_KEY);
                return;
            }

            window.Resources[GLASS_PANEL_KEY] = CopyBrush("SolidPanelBrush");
            window.Resources[GLASS_CONTENT_KEY] = CopyBrush("SolidSurfaceBrush");
            // primary buttons carry the same per-window alpha problem as the cards they sit on
            window.Resources[ACCENT_GLASS_KEY] = CopyBrush("AccentMidBrush");
        }

        private static Brush CopyBrush(string key)
        {
            if (Application.Current?.TryFindResource(key) is SolidColorBrush solid)
                return new SolidColorBrush(solid.Color);
            return Brushes.Black;
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
