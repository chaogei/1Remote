using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;

namespace _1RM.Utils
{
    /// <summary>
    /// The fonts installed on this machine, and the rule for picking one when the requested name is not
    /// among them.
    ///
    /// It lives on its own because both the theme and the configuration need it, and because
    /// <see cref="Fonts.SystemFontFamilies"/> is expensive: it walks the system and user font directories to
    /// build its collection, and scanning that collection linearly for each fallback candidate meant paying
    /// for the whole list several times over during startup.
    /// </summary>
    public static class InstalledFonts
    {
        private static readonly Lazy<Dictionary<string, FontFamily>> LazyByName = new Lazy<Dictionary<string, FontFamily>>(() =>
        {
            var map = new Dictionary<string, FontFamily>(StringComparer.OrdinalIgnoreCase);
            foreach (var family in Fonts.SystemFontFamilies)
                map[family.Source] = family;
            return map;
        });

        /// <summary>
        /// The named family if it is installed, otherwise the closest CJK-capable substitute, otherwise
        /// whatever the machine has.
        /// </summary>
        public static FontFamily Resolve(string name)
        {
            var byName = LazyByName.Value;
            if (!string.IsNullOrEmpty(name) && byName.TryGetValue(name, out var exact))
                return exact;
            if (byName.TryGetValue("Microsoft YaHei", out var yahei))
                return yahei;

            // one scan covering every remaining candidate, in preference order
            FontFamily? endsWithYaHei = null, containsYaHei = null, containsCjkName = null;
            foreach (var family in byName.Values)
            {
                var source = family.Source;
                if (endsWithYaHei == null && source.EndsWith("YaHei", StringComparison.OrdinalIgnoreCase))
                    endsWithYaHei = family;
                if (containsYaHei == null && source.IndexOf("YaHei", StringComparison.OrdinalIgnoreCase) >= 0)
                    containsYaHei = family;
                if (containsCjkName == null && source.IndexOf("雅黑", StringComparison.Ordinal) >= 0)
                    containsCjkName = family;
            }

            return endsWithYaHei ?? containsYaHei ?? containsCjkName ?? Fonts.SystemFontFamilies.First();
        }
    }
}
