using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Shawn.Utils.Wpf;
using _1RM.Utils;
using _1RM.Utils.Theme;

namespace _1RM.Service
{
    public class ThemeService
    {
        private readonly ResourceDictionary _appResourceDictionary;
        public ThemeConfig CurrentTheme;
        public Dictionary<string, ThemeConfig> Themes { get; } = new Dictionary<string, ThemeConfig>();
        public ThemeService(ResourceDictionary appResourceDictionary, ThemeConfig defaultTheme)
        {
            _appResourceDictionary = appResourceDictionary;

            // === Modern Frosted Glass / Acrylic Optimized Palettes ===
            Themes.Add("Dark", new ThemeConfig()
            {
                ThemeName = "Dark",
                PrimaryMidColor = "#323233",
                PrimaryLightColor = "#474748",
                PrimaryDarkColor = "#2d2d2d",
                PrimaryTextColor = "#cccccc",
                AccentMidColor = "#FF007ACC",
                AccentLightColor = "#FF32A7F4",
                AccentDarkColor = "#FF0061A3",
                AccentTextColor = "#FFFFFFFF",
                BackgroundColor = "#1e1e1e",
                BackgroundTextColor = "#cccccc",
            });
            Themes.Add("Mica Slate", new ThemeConfig()
            {
                ThemeName = "Mica Slate",
                PrimaryMidColor = "#20232A",
                PrimaryLightColor = "#2C313B",
                PrimaryDarkColor = "#181B20",
                PrimaryTextColor = "#E1E4EA",
                AccentMidColor = "#0078D4",
                AccentLightColor = "#2B88D8",
                AccentDarkColor = "#005A9E",
                AccentTextColor = "#FFFFFFFF",
                BackgroundColor = "#16191E",
                BackgroundTextColor = "#E1E4EA",
            });
            Themes.Add("Nord Frost", new ThemeConfig()
            {
                ThemeName = "Nord Frost",
                PrimaryMidColor = "#2E3440",
                PrimaryLightColor = "#3B4252",
                PrimaryDarkColor = "#242933",
                PrimaryTextColor = "#ECEFF4",
                AccentMidColor = "#88C0D0",
                AccentLightColor = "#8FBCBB",
                AccentDarkColor = "#5E81AC",
                AccentTextColor = "#242933",
                BackgroundColor = "#242933",
                BackgroundTextColor = "#ECEFF4",
            });
            Themes.Add("Tokyo Night", new ThemeConfig()
            {
                ThemeName = "Tokyo Night",
                PrimaryMidColor = "#1F2335",
                PrimaryLightColor = "#292E42",
                PrimaryDarkColor = "#1A1B26",
                PrimaryTextColor = "#C0CAF5",
                AccentMidColor = "#7AA2F7",
                AccentLightColor = "#BB9AF7",
                AccentDarkColor = "#3D59A1",
                AccentTextColor = "#FFFFFFFF",
                BackgroundColor = "#1A1B26",
                BackgroundTextColor = "#C0CAF5",
            });
            Themes.Add("Catppuccin Mocha", new ThemeConfig()
            {
                ThemeName = "Catppuccin Mocha",
                PrimaryMidColor = "#1E1E2E",
                PrimaryLightColor = "#313244",
                PrimaryDarkColor = "#181825",
                PrimaryTextColor = "#CDD6F4",
                AccentMidColor = "#CBA6F7",
                AccentLightColor = "#F5C2E7",
                AccentDarkColor = "#B4BEFE",
                AccentTextColor = "#11111B",
                BackgroundColor = "#181825",
                BackgroundTextColor = "#CDD6F4",
            });
            Themes.Add("Emerald Glass", new ThemeConfig()
            {
                ThemeName = "Emerald Glass",
                PrimaryMidColor = "#14231E",
                PrimaryLightColor = "#1E352E",
                PrimaryDarkColor = "#0D1714",
                PrimaryTextColor = "#E2ECE8",
                AccentMidColor = "#10B981",
                AccentLightColor = "#34D399",
                AccentDarkColor = "#059669",
                AccentTextColor = "#FFFFFFFF",
                BackgroundColor = "#0D1714",
                BackgroundTextColor = "#E2ECE8",
            });
            Themes.Add("Cyber Neon", new ThemeConfig()
            {
                ThemeName = "Cyber Neon",
                PrimaryMidColor = "#181926",
                PrimaryLightColor = "#24273A",
                PrimaryDarkColor = "#12131D",
                PrimaryTextColor = "#CAD3F5",
                AccentMidColor = "#00E5FF",
                AccentLightColor = "#70EFFF",
                AccentDarkColor = "#00B4D8",
                AccentTextColor = "#0B0C10",
                BackgroundColor = "#12131D",
                BackgroundTextColor = "#CAD3F5",
            });
            Themes.Add("Rose Pine", new ThemeConfig()
            {
                ThemeName = "Rose Pine",
                PrimaryMidColor = "#1F1D2E",
                PrimaryLightColor = "#26233A",
                PrimaryDarkColor = "#191724",
                PrimaryTextColor = "#E0DEF4",
                AccentMidColor = "#EB6F92",
                AccentLightColor = "#F6C177",
                AccentDarkColor = "#B4637A",
                AccentTextColor = "#FFFFFFFF",
                BackgroundColor = "#191724",
                BackgroundTextColor = "#E0DEF4",
            });
            Themes.Add("macOS Light Glass", new ThemeConfig()
            {
                ThemeName = "macOS Light Glass",
                PrimaryMidColor = "#E8EEF5",
                PrimaryLightColor = "#F4F8FC",
                PrimaryDarkColor = "#D9E2EC",
                PrimaryTextColor = "#1E293B",
                AccentMidColor = "#007AFF",
                AccentLightColor = "#388BFD",
                AccentDarkColor = "#0056B3",
                AccentTextColor = "#FFFFFFFF",
                BackgroundColor = "#F8FAFC",
                BackgroundTextColor = "#0F172A",
            });

            // === Classic Palettes ===
            Themes.Add("Light", new ThemeConfig()
            {
                ThemeName = "Light",
                PrimaryMidColor = "#FFF2F3F5",
                PrimaryLightColor = "#FFFFFFFF",
                PrimaryDarkColor = "#FFE4E7EB",
                PrimaryTextColor = "#FF232323",
                AccentMidColor = "#FFE83D61",
                AccentLightColor = "#FFED6884",
                AccentDarkColor = "#FFB5304C",
                AccentTextColor = "#FFFFFFFF",
                BackgroundColor = "#FFFFFFFF",
                BackgroundTextColor = "#000000",
            });
            Themes.Add("PRemoteM", new ThemeConfig()
            {
                ThemeName = "PRemoteM",
                PrimaryMidColor = "#102b3e",
                PrimaryLightColor = "#445a68",
                PrimaryDarkColor = "#0c2230",
                PrimaryTextColor = "#FFFFFFFF",
                AccentMidColor = "#FFE83D61",
                AccentLightColor = "#FFED6884",
                AccentDarkColor = "#FFB5304C",
                AccentTextColor = "#FFFFFFFF",
                BackgroundColor = "#ced8e1",
                BackgroundTextColor = "#000000",
            });
            Themes.Add("SecretKey", new ThemeConfig()
            {
                ThemeName = "SecretKey",
                PrimaryMidColor = "#FF473368",
                PrimaryLightColor = "#796090",
                PrimaryDarkColor = "#382853",
                PrimaryTextColor = "#FFFFFFFF",
                AccentMidColor = "#FFEF6D3B",
                AccentLightColor = "#FF9A63",
                AccentDarkColor = "#BF572F",
                AccentTextColor = "#FFFFFFFF",
                BackgroundColor = "#FFF2F1EC",
                BackgroundTextColor = "#000000",
            });
            Themes.Add("Greystone", new ThemeConfig()
            {
                ThemeName = "Greystone",
                PrimaryMidColor = "#FFC7D0D5",
                PrimaryLightColor = "#F9FDFD",
                PrimaryDarkColor = "#9FA6AA",
                PrimaryTextColor = "#FF1B2C3F",
                AccentMidColor = "#FFFF7247",
                AccentLightColor = "#FFED583A",
                AccentDarkColor = "#CC5B38",
                AccentTextColor = "#FFFFFFFF",
                BackgroundColor = "#FFF5F5F5",
                BackgroundTextColor = "#000000",
            });
            Themes.Add("Asphalt", new ThemeConfig()
            {
                ThemeName = "Asphalt",
                PrimaryMidColor = "#FF393939",
                PrimaryLightColor = "#6B6661",
                PrimaryDarkColor = "#2D2D2D",
                PrimaryTextColor = "#FFFFFFFF",
                AccentMidColor = "#FFFF7247",
                AccentLightColor = "#FFED583A",
                AccentDarkColor = "#CC5B38",
                AccentTextColor = "#FFFFFFFF",
                BackgroundColor = "#FFF5F5F5",
                BackgroundTextColor = "#000000",
            });
            Themes.Add("Wine", new ThemeConfig()
            {
                ThemeName = "Wine",
                PrimaryMidColor = "#FF57112D",
                PrimaryLightColor = "#893E55",
                PrimaryDarkColor = "#450D24",
                PrimaryTextColor = "#FFFFFFFF",
                AccentMidColor = "#FFA82159",
                AccentLightColor = "#DA4E81",
                AccentDarkColor = "#861A47",
                AccentTextColor = "#FFFFFFFF",
                BackgroundColor = "#FFFDEAD9",
                BackgroundTextColor = "#FF450D24",
            });
            Themes.Add("Forest", new ThemeConfig()
            {
                ThemeName = "Forest",
                PrimaryMidColor = "#FF253938",
                PrimaryLightColor = "#576660",
                PrimaryDarkColor = "#1D2D2C",
                PrimaryTextColor = "#FFFFFFFF",
                AccentMidColor = "#FF5FA291",
                AccentLightColor = "#91CFB9",
                AccentDarkColor = "#4C8174",
                AccentTextColor = "#FFFFFFFF",
                BackgroundColor = "#FFF5F5F5",
                BackgroundTextColor = "#FF303030",
            });
            Themes.Add("Soil", new ThemeConfig()
            {
                ThemeName = "Soil",
                PrimaryMidColor = "#FF776245",
                PrimaryLightColor = "#A98F6D",
                PrimaryDarkColor = "#FF735E41",
                PrimaryTextColor = "#FFFFFFFF",
                AccentMidColor = "#FF0193B8",
                AccentLightColor = "#33C0E0",
                AccentDarkColor = "#007593",
                AccentTextColor = "#FFFFFFFF",
                BackgroundColor = "#FFCFC3B5",
                BackgroundTextColor = "#FF080000",
            });

            CurrentTheme = defaultTheme;
            ApplyTheme(defaultTheme);
        }

        public void ApplyTheme(ThemeConfig theme)
        {
            if (theme == null) return;
            CurrentTheme = theme;
            const string resourceTypeKey = "__Resource_Type_Key";
            const string resourceTypeValue = "__Resource_Type_Value=theme";
            void SetKey(IDictionary rd, string key, object value)
            {
                if (!rd.Contains(key))
                    rd.Add(key, value);
                else
                    rd[key] = value;
            }

            // create new theme resources
            var rd = new ResourceDictionary();
            SetKey(rd, resourceTypeKey, resourceTypeValue);
            SetKey(rd, "PrimaryMidColor", ColorAndBrushHelper.HexColorToMediaColor(theme.PrimaryMidColor));
            SetKey(rd, "PrimaryLightColor", ColorAndBrushHelper.HexColorToMediaColor(theme.PrimaryLightColor));
            SetKey(rd, "PrimaryDarkColor", ColorAndBrushHelper.HexColorToMediaColor(theme.PrimaryDarkColor));
            SetKey(rd, "PrimaryTextColor", ColorAndBrushHelper.HexColorToMediaColor(theme.PrimaryTextColor));
            SetKey(rd, "AccentMidColor", ColorAndBrushHelper.HexColorToMediaColor(theme.AccentMidColor));
            SetKey(rd, "AccentLightColor", ColorAndBrushHelper.HexColorToMediaColor(theme.AccentLightColor));
            SetKey(rd, "AccentDarkColor", ColorAndBrushHelper.HexColorToMediaColor(theme.AccentDarkColor));
            SetKey(rd, "AccentTextColor", ColorAndBrushHelper.HexColorToMediaColor(theme.AccentTextColor));
            SetKey(rd, "BackgroundColor", ColorAndBrushHelper.HexColorToMediaColor(theme.BackgroundColor));
            SetKey(rd, "BackgroundTextColor", ColorAndBrushHelper.HexColorToMediaColor(theme.BackgroundTextColor));


            SetKey(rd, "PrimaryMidBrush", ColorAndBrushHelper.ColorToMediaBrush(theme.PrimaryMidColor));
            SetKey(rd, "PrimaryLightBrush", ColorAndBrushHelper.ColorToMediaBrush(theme.PrimaryLightColor));
            SetKey(rd, "PrimaryDarkBrush", ColorAndBrushHelper.ColorToMediaBrush(theme.PrimaryDarkColor));
            SetKey(rd, "PrimaryTextBrush", ColorAndBrushHelper.ColorToMediaBrush(theme.PrimaryTextColor));
            SetKey(rd, "AccentMidBrush", ColorAndBrushHelper.ColorToMediaBrush(theme.AccentMidColor));
            SetKey(rd, "AccentLightBrush", ColorAndBrushHelper.ColorToMediaBrush(theme.AccentLightColor));
            SetKey(rd, "AccentDarkBrush", ColorAndBrushHelper.ColorToMediaBrush(theme.AccentDarkColor));
            SetKey(rd, "AccentTextBrush", ColorAndBrushHelper.ColorToMediaBrush(theme.AccentTextColor));
            SetKey(rd, "BackgroundBrush", ColorAndBrushHelper.ColorToMediaBrush(theme.BackgroundColor));
            SetKey(rd, "BackgroundTextBrush", ColorAndBrushHelper.ColorToMediaBrush(theme.BackgroundTextColor));

            SetKey(rd, "PrimaryColor", ColorAndBrushHelper.HexColorToMediaColor(theme.AccentMidColor));
            SetKey(rd, "DarkPrimaryColor", ColorAndBrushHelper.HexColorToMediaColor(theme.AccentDarkColor));
            //SetKey(rd, "PrimaryDarkColor", ColorAndBrushHelper.HexColorToMediaColor(theme.AccentTextColor));

            var font = InstalledFonts.Resolve(theme.FontFamily);
            SetKey(rd, "GlobalFontFamily", font);
            theme.FontSize = Math.Max(10, theme.FontSize);
            double globalFontSizeSmall = Math.Min(20.0, theme.FontSize - 2.0);
            double globalFontSizeBody = Math.Min(20.0, theme.FontSize);
            double globalFontSizeSubtitle = Math.Min(20.0, theme.FontSize + 2.0);
            double globalFontSizeTitle = Math.Min(20.0, theme.FontSize + 6.0);
            // One step above Title, for the empty-state headings that used to hard-code 22 and 24 and so
            // ignored the user's font size entirely. Capped higher than the others because it is display text.
            double globalFontSizeLarge = Math.Min(28.0, theme.FontSize + 10.0);
            SetKey(rd, "GlobalFontSizeLarge", globalFontSizeLarge);
            SetKey(rd, "GlobalFontSizeTitle", globalFontSizeTitle);
            SetKey(rd, "GlobalFontSizeSubtitle", globalFontSizeSubtitle);
            SetKey(rd, "GlobalFontSizeBody", globalFontSizeBody);
            SetKey(rd, "GlobalFontSizeSmall", globalFontSizeSmall);

            ApplyGlassLayers(rd, theme, SetKey);

            // remove old theme resources
            var rs = _appResourceDictionary.MergedDictionaries.Where(o =>
                (o?.Source?.IsAbsoluteUri == true && o.Source.AbsolutePath.ToLower().IndexOf("Default.xaml", StringComparison.OrdinalIgnoreCase) >= 0)
                || o?[resourceTypeKey]?.ToString() == resourceTypeValue).ToArray();
            foreach (var r in rs)
            {
                _appResourceDictionary.MergedDictionaries.Remove(r);
            }

            // add new theme resources
            _appResourceDictionary.MergedDictionaries.Add(rd);

            // windows that are already open keep their old tint until they are restained
            AcrylicBehavior.RefreshAll();
        }

        /// <summary>
        /// Derives the translucent elevation layers and the frosted backdrop from the theme's own colours,
        /// so a user-picked palette stays coherent without asking them to choose ten more colours.
        ///
        /// The layers are the foreground colour at low alpha: on a dark theme that lightens the surface, on a
        /// light theme it darkens it, which is the behaviour you want in both cases.
        /// </summary>
        private static void ApplyGlassLayers(ResourceDictionary rd, ThemeConfig theme, Action<IDictionary, string, object> setKey)
        {
            SolidColorBrush Overlay(Color color, byte alpha) =>
                new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));

            var onPrimary = theme.GetPrimaryTextColor;
            setKey(rd, "LayerFillBrush", Overlay(onPrimary, 0x10));
            setKey(rd, "LayerHoverBrush", Overlay(onPrimary, 0x1A));
            setKey(rd, "LayerSelectedBrush", Overlay(onPrimary, 0x2B));
            setKey(rd, "CardStrokeBrush", Overlay(onPrimary, 0x24));

            // The session window is one DWM frost can never reach (AcrylicBehavior's denylist — a hosted
            // RDP/VNC HWND draws with GDI, which zeroes the alpha byte, and the accent policy then reads
            // those pixels as fully transparent and blurs the desktop over the session). Its tab strip
            // therefore has to look like glass on its own: a single flat Layer* tint over PrimaryMid
            // reads as one more slab of the window fill. A gradient does the work instead — a bright edge at
            // the very top falling away to almost nothing at the hairline, which is the specular highlight
            // that makes a sheet read as glass rather than as paint.
            setKey(rd, "TitleBarGlassBrush", new LinearGradientBrush(new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(0x3A, onPrimary.R, onPrimary.G, onPrimary.B), 0.0),
                new GradientStop(Color.FromArgb(0x22, onPrimary.R, onPrimary.G, onPrimary.B), 0.07),
                new GradientStop(Color.FromArgb(0x15, onPrimary.R, onPrimary.G, onPrimary.B), 0.55),
                new GradientStop(Color.FromArgb(0x0E, onPrimary.R, onPrimary.G, onPrimary.B), 1.0),
            }, new Point(0, 0), new Point(0, 1)));

            var onBackground = theme.GetBackgroundTextColor;
            setKey(rd, "ContentLayerFillBrush", Overlay(onBackground, 0x0D));
            setKey(rd, "ContentLayerHoverBrush", Overlay(onBackground, 0x17));
            setKey(rd, "ContentLayerSelectedBrush", Overlay(onBackground, 0x24));
            setKey(rd, "ContentCardStrokeBrush", Overlay(onBackground, 0x20));

            // With the backdrop off every surface stays fully opaque, which is also the fallback when the OS
            // refuses the acrylic call — AcrylicBehavior only overrides WindowBackdropBrush per window once
            // the composition attribute has actually been accepted, so a failure can never leave a window
            // see-through and unreadable. The same opaque snap applies in a remote session or high contrast:
            // DWM would otherwise sample the remote framebuffer and GlassPanelBrush at ~70% would wash out
            // over that unblurred surface.
            var frost = theme.EnableAcrylic && !AcrylicBehavior.ShouldSkipAcrylic();
            var alpha = frost ? (byte)Math.Min(255, Math.Max(0, theme.AcrylicOpacity)) : (byte)0xFF;
            var primaryMid = theme.GetPrimaryMidColor;
            var background = theme.GetBackgroundColor;

            // The DWM tint is only a light veil that deepens the blur. The visible colour comes from the
            // Glass* brushes layered on top, which keeps the result matching the theme exactly instead of
            // tinting twice and ending up muddy.
            setKey(rd, "AcrylicTintColor", Color.FromArgb(frost ? (byte)0x40 : (byte)0x00, primaryMid.R, primaryMid.G, primaryMid.B));
            setKey(rd, "WindowBackdropBrush", new SolidColorBrush(primaryMid));
            setKey(rd, "GlassPanelBrush", new SolidColorBrush(Color.FromArgb(alpha, primaryMid.R, primaryMid.G, primaryMid.B)));
            setKey(rd, "GlassContentBrush", new SolidColorBrush(Color.FromArgb(alpha, background.R, background.G, background.B)));

            // Primary action buttons keep the accent colour — that is how a primary action reads — but a
            // fully opaque accent rectangle on a frosted panel is a brick punched through the glass. Floored
            // well above the panel alpha, and above the user's slider, because AccentTextBrush has to stay
            // legible over whatever the desktop happens to be showing behind the blur.
            // The floor is compared by hand rather than with Math.Max: both operands are bytes, and the
            // .NET 10 SDK added a Math.Max(byte, byte) overload that makes such a call ambiguous against
            // Math.Max(int, int).
            const byte accentFloor = 0xC8;
            var accentMid = theme.GetAccentMidColor;
            byte accentAlpha = 0xFF;
            if (frost)
                accentAlpha = alpha > accentFloor ? alpha : accentFloor;
            setKey(rd, "AccentGlassBrush", new SolidColorBrush(Color.FromArgb(accentAlpha, accentMid.R, accentMid.G, accentMid.B)));

            // BackgroundBrush deliberately stays opaque. It looks like the one lever that would turn every
            // control translucent at once — BaseStyle's ControlBase hands it to all of them — but the
            // ComboBox template also paints its drop-down popup with {TemplateBinding Background}, so the
            // closed control and the floating list share this single brush. Making it translucent turned
            // every drop-down see-through and unreadable.
            //
            // Individual controls are frosted the other way round instead, in Resources/Theme/Glass.xaml:
            // each style is overridden to fill from the Layer* overlays rather than from this brush, which
            // leaves anything that really does float in its own HWND — drop-downs, tooltips, completion
            // popups — still asking for an opaque fill.
            setKey(rd, "SolidSurfaceBrush", new SolidColorBrush(background));
            setKey(rd, "SolidPanelBrush", new SolidColorBrush(primaryMid));
        }

    }
}
