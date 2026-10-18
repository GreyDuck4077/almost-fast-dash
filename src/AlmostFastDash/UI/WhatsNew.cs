using System.Reflection;

namespace AlmostFastDash.UI
{
    /// <summary>
    /// The "What's new" card shown once on the Dashboards page after an update: the highlights since the designer
    /// was rebuilt, and a link to every change. Update the highlights with each release that's worth a mention.
    /// </summary>
    internal static class WhatsNew
    {
        /// <summary>This version, as major.minor.</summary>
        public static string Version
        {
            get
            {
                System.Version v = Assembly.GetExecutingAssembly().GetName().Version;
                return v.Major + "." + v.Minor;
            }
        }

        public const string Changelog = "https://github.com/Sota4077/almost-fast-dash/blob/main/CHANGELOG.md";

        public static readonly string[] Highlights =
        {
            "A new screen designer, laid out like an image editor.",
            "A pen, shapes, gradient and pattern fills, masks and blend modes.",
            "Modules: save anything you build and share it as a file.",
            "Repeat in rows or around a circle, and an eyedropper.",
            "Point at a dashboard below and it plays a lap."
        };
    }
}
