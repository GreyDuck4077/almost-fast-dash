using System.Reflection;

namespace AlmostFastOverlays
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
            "A new screen designer, laid out like an image editor: tools on the left, Properties and Layers always in view, rulers and guides.",
            "Draw and mask: a pen, polygons and stars, gradient and pattern fills, clipping masks, fades and blend modes.",
            "Modules: keep anything you build, place it anywhere and share it as a file, with options that make it easy to adapt.",
            "Repeat in rows or around a circle, an eyedropper, and every setting explains itself when you point at it.",
            "Point at a dashboard below and it plays a lap."
        };
    }
}
