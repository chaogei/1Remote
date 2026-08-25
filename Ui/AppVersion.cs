using static Shawn.Utils.VersionHelper;

namespace _1RM
{
    public static class AppVersion
    {
        public const uint Major = 1;
        public const uint Minor = 3;
        public const uint Patch = 0;
        public const uint Build = 10;
        public const string BuildDate = "";
        public const string PreRelease = "beta"; // e.g. "alpha" "beta.2"

        public static readonly Version VersionData = new Version(Major, Minor, Patch, Build, PreRelease);
        public static string Version => VersionData.ToString();


        public static string[] UpdateCheckUrls =>
            string.IsNullOrEmpty(PreRelease)
                ? new[]
                {
                    // The "latest" page resolves to the newest non-prerelease, so a stable build never
                    // gets nagged about a beta tag.
                    "https://github.com/chaogei/1Remote/releases/latest",
                }
                : new[]
                {
                    "https://github.com/chaogei/1Remote/releases",
                };

        public static string[] UpdatePublishUrls =>
            string.IsNullOrEmpty(PreRelease)
                ? new[]
                {
                    "https://github.com/chaogei/1Remote/releases/latest",
                }
                : new[]
                {
                    "https://github.com/chaogei/1Remote/releases",
                };
    }
}
