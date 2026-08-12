using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Shawn.Utils.Wpf;
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
                ThemeName = "Light",
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

            var font = GetFontFamily(theme.FontFamily);
            SetKey(rd, "GlobalFontFamily", font);
            theme.FontSize = Math.Max(10, theme.FontSize);
            double globalFontSizeSmall = Math.Min(20.0, theme.FontSize - 2.0);
            double globalFontSizeBody = Math.Min(20.0, theme.FontSize);
            double globalFontSizeSubtitle = Math.Min(20.0, theme.FontSize + 2.0);
            double globalFontSizeTitle = Math.Min(20.0, theme.FontSize + 6.0);
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

            var onBackground = theme.GetBackgroundTextColor;
            setKey(rd, "ContentLayerFillBrush", Overlay(onBackground, 0x0D));
            setKey(rd, "ContentLayerHoverBrush", Overlay(onBackground, 0x17));
            setKey(rd, "ContentCardStrokeBrush", Overlay(onBackground, 0x20));

            // With the backdrop off every surface stays fully opaque, which is also the fallback when the OS
            // refuses the acrylic call — AcrylicBehavior only overrides WindowBackdropBrush per window once
            // the composition attribute has actually been accepted, so a failure can never leave a window
            // see-through and unreadable.
            var alpha = theme.EnableAcrylic ? (byte)Math.Min(255, Math.Max(0, theme.AcrylicOpacity)) : (byte)0xFF;
            var primaryMid = theme.GetPrimaryMidColor;
            var background = theme.GetBackgroundColor;

            // The DWM tint is only a light veil that deepens the blur. The visible colour comes from the
            // Glass* brushes layered on top, which keeps the result matching the theme exactly instead of
            // tinting twice and ending up muddy.
            setKey(rd, "AcrylicTintColor", Color.FromArgb(theme.EnableAcrylic ? (byte)0x40 : (byte)0x00, primaryMid.R, primaryMid.G, primaryMid.B));
            setKey(rd, "WindowBackdropBrush", new SolidColorBrush(primaryMid));
            setKey(rd, "GlassPanelBrush", new SolidColorBrush(Color.FromArgb(alpha, primaryMid.R, primaryMid.G, primaryMid.B)));
            setKey(rd, "GlassContentBrush", new SolidColorBrush(Color.FromArgb(alpha, background.R, background.G, background.B)));

            // BackgroundBrush deliberately stays opaque. It looks like the one lever that would turn every
            // control translucent at once — BaseStyle's ControlBase hands it to all of them — but the
            // ComboBox template also paints its drop-down popup with {TemplateBinding Background}, so the
            // closed control and the floating list share this single brush. Making it translucent turned
            // every drop-down see-through and unreadable. Separating the two means editing the templates in
            // the Shawn.Utils submodule; until then controls stay solid and only surfaces are glass.
            setKey(rd, "SolidSurfaceBrush", new SolidColorBrush(background));
            setKey(rd, "SolidPanelBrush", new SolidColorBrush(primaryMid));
        }

        private static FontFamily GetFontFamily(string name)
        {
            // set default font family
            var fontFamily = Fonts.SystemFontFamilies.FirstOrDefault(x => string.Equals(x.Source, name, StringComparison.CurrentCultureIgnoreCase));
            fontFamily ??= Fonts.SystemFontFamilies.FirstOrDefault(x => string.Equals(x.Source, "Microsoft YaHei", StringComparison.CurrentCultureIgnoreCase));
            fontFamily ??= Fonts.SystemFontFamilies.FirstOrDefault(x => x.Source.EndsWith("YaHei", StringComparison.OrdinalIgnoreCase));
            fontFamily ??= Fonts.SystemFontFamilies.FirstOrDefault(x => x.Source.IndexOf("YaHei", StringComparison.OrdinalIgnoreCase) >= 0);
            fontFamily ??= Fonts.SystemFontFamilies.FirstOrDefault(x => x.Source.IndexOf("雅黑", StringComparison.OrdinalIgnoreCase) >= 0);
            fontFamily ??= Fonts.SystemFontFamilies.FirstOrDefault(x => x.Source.IndexOf("雅黑", StringComparison.OrdinalIgnoreCase) >= 0);

            return fontFamily ?? Fonts.SystemFontFamilies.First();
        }
    }
}
