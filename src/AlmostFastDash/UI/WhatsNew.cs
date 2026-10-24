using System.Reflection;

namespace AlmostFastDash.UI
{
    /// <summary>
    /// The "What's new" card shown once on the Dashboards page after an update: the highlights since the designer
    /// was rebuilt, and a link to every change. When a release is worth a mention, update the highlights and set
    /// <see cref="HighlightsVersion"/> to it; a release without new highlights doesn't show the card again.
    /// </summary>
    internal static class WhatsNew
    {
        /// <summary>The version the highlights were last written for. The card shows to anyone who hasn't seen it.</summary>
        public const string HighlightsVersion = "3.75";

        /// <summary>Whether someone who last saw <paramref name="seen"/> (major.minor, or empty) has new highlights to see.</summary>
        public static bool IsNewTo(string seen) =>
            !System.Version.TryParse(seen, out System.Version last) || last < System.Version.Parse(HighlightsVersion);

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
            "Roundy is now Rowdy, in tribute to Kyle Busch, and Oval is now Intimidator, in tribute to Dale Earnhardt.",
            "Questions or problems? Ask on Discord: the link is at the bottom of the sidebar.",
            "The radar keeps the cars behind you on the right sides more often.",
            "A screen designer laid out like an image editor, with modules you can save and share."
        };
    }
}
