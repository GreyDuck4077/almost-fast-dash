namespace AlmostFastDash.Settings
{
    internal static class Disciplines
    {
        public const string Oval = "Oval";
        public const string Road = "Road";
        public const string DirtOval = "Dirt Oval";
        public const string DirtRoad = "Dirt Road";
        public static readonly string[] All = { Oval, Road, DirtOval, DirtRoad };

        // iRacing reports WeekendInfo.Category as "Oval", "Road", "DirtOval",
        // "DirtRoad" and, for newer license splits, "SportsCar"/"FormulaCar".
        // Everything that is not an oval or dirt surface is treated as road.
        public static string FromIRacing(string category, string trackType)
        {
            string text = ((category ?? "") + " " + (trackType ?? "")).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(text)) return null;
            bool dirt = text.Contains("dirt");
            bool oval = text.Contains("oval");
            if (dirt) return oval ? DirtOval : DirtRoad;
            if (oval) return Oval;
            return Road;
        }
    }

    internal static class Layouts
    {
        /// <summary>Every dashboard is a design stored in <see cref="DashboardSettings.Design"/> and drawn by <see cref="LayoutRenderer"/>.</summary>
        public const string Custom = "Custom";

        /// <summary>Code-drawn layouts from earlier versions; schema 5 replaces them with the Liquid Glass designs.</summary>
        public static readonly string[] Retired = { "Summit", "Formula", "Oval", "Endurance", "Velocity", "Apex", "Telemetry" };
    }
}
