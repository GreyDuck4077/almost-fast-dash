using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace AlmostFastOverlays
{
    /// <summary>On-monitor overlays: which are on and how they look. Positions live in overlay-positions.json.</summary>
    internal sealed class OverlaySettings
    {
        /// <summary>Start the overlays whenever the dashboard starts.</summary>
        public bool StartWithDashboard { get; set; }
        /// <summary>Locked overlays let clicks pass through to the game; unlocked ones can be dragged.</summary>
        public bool Locked { get; set; } = true;
        public List<OverlayConfig> Items { get; set; } = new List<OverlayConfig>();

        public void Normalize()
        {
            if (Items == null) Items = new List<OverlayConfig>();
            Items.RemoveAll(i => i == null || OverlayTypes.Find(i.Type) == null);
            foreach (OverlayType type in OverlayTypes.All)
                if (!Items.Any(i => i.Type == type.Id)) Items.Add(new OverlayConfig { Id = type.Id, Type = type.Id, Name = type.Name });
            foreach (OverlayConfig item in Items) item.Normalize();
        }

        public OverlayConfig Find(string id) => Items.FirstOrDefault(i => i.Id == id);
    }

    internal sealed class OverlayConfig
    {
        public string Id { get; set; } = "standings";
        public string Type { get; set; } = "standings";
        public string Name { get; set; } = "Standings";
        public bool Enabled { get; set; }
        public double Scale { get; set; } = 1;
        /// <summary>Background opacity, 0-100.</summary>
        public int Opacity { get; set; } = 88;
        public bool ShowInGarage { get; set; } = true;
        public Dictionary<string, string> Props { get; set; } = new Dictionary<string, string>();

        public void Normalize()
        {
            if (Props == null) Props = new Dictionary<string, string>();
            Scale = Math.Max(0.5, Math.Min(2.5, Scale <= 0 ? 1 : Scale));
            Opacity = Math.Max(0, Math.Min(100, Opacity));
            if (string.IsNullOrWhiteSpace(Name)) Name = OverlayTypes.Find(Type)?.Name ?? "Overlay";
        }

        public string Get(string key) => Props.TryGetValue(key, out string v) && v != null ? v : OverlayTypes.Find(Type)?.Option(key)?.Default ?? "";
        public bool Bool(string key) => Expr.Bool(Get(key));
        public int Int(string key) => int.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;
        public void Set(string key, string value) => Props[key] = value;
    }

    internal sealed class OverlayOption
    {
        public string Key, Label, Group, Default, Help;
        public PropKind Kind;
        public double Min, Max;
        public string[] Choices;
        /// <summary>For a column: the option that shows its label in the column header row.</summary>
        public string HeaderKey;
        /// <summary>A column that is always shown; only its header can be turned on and off.</summary>
        public bool Fixed;
        /// <summary>Drawn inside another option's row (a column's header switch) rather than on its own.</summary>
        public bool Inline;
    }

    internal sealed class OverlayType
    {
        public string Id, Name, Description, Icon;
        public readonly List<OverlayOption> Options = new List<OverlayOption>();
        public OverlayOption Option(string key) => Options.FirstOrDefault(o => o.Key == key);

        /// <summary>The column options in the order the user arranged them ("columnOrder" holds option keys).</summary>
        public List<OverlayOption> OrderedColumns(OverlayConfig config)
        {
            var columns = Options.Where(o => o.HeaderKey != null && !o.Inline).ToList();
            string[] order = (config?.Get("columnOrder") ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            int Rank(OverlayOption o)
            {
                int at = Array.IndexOf(order, o.Key);
                return at >= 0 ? at : order.Length + columns.IndexOf(o);
            }
            return columns.OrderBy(Rank).ToList();
        }
    }

    internal static class OverlayTypes
    {
        public static readonly List<OverlayType> All = new List<OverlayType>();

        static OverlayTypes()
        {
            var standings = new OverlayType
            {
                Id = "standings", Name = "Standings", Icon = "",
                Description = "A multiclass leaderboard: class leaders, the cars around you and every other class, with gaps, lap times, iRating, pit status and animated position changes."
            };
            Number(standings, "driversAtTop", "Leaders shown", "Rows", "3", 0, 20, "Cars always shown at the top of your class.");
            Number(standings, "aheadBehind", "Cars around you", "Rows", "3", 0, 15, "Cars shown ahead of and behind you in your class.");
            Number(standings, "otherClassTop", "Other classes", "Rows", "3", 0, 20, "Cars shown for each other class.");
            Bool(standings, "header", "Session header", "Layout", "true", "Session type, clock and car count.");
            Bool(standings, "classHeaders", "Class headers", "Layout", "true", "Class name, car count and strength of field.");
            Number(standings, "nameWidth", "Name width", "Layout", "190", 110, 320, "Width of the driver name column in pixels.");
            Choice(standings, "nameStyle", "Names", "Layout", "Full", "Full", "First initial", "Last name");
            FixedColumn(standings, "Position", "hdrPos");
            FixedColumn(standings, "Driver", "hdrName");
            Column(standings, "colLicense", "License and iRating", "true", "hdrLicense");
            Column(standings, "colMake", "Car make", "true", "hdrMake");
            Column(standings, "colGained", "Places gained", "false", "hdrGained");
            Column(standings, "colGap", "Gap to class leader", "true", "hdrGap");
            Column(standings, "colInterval", "Interval to car ahead", "false", "hdrInterval");
            Column(standings, "colBest", "Best lap", "true", "hdrBest");
            Column(standings, "colLast", "Last lap", "true", "hdrLast");
            Column(standings, "colStatus", "Status (fastest, pit, out)", "true", "hdrStatus");
            All.Add(standings);

            var relative = new OverlayType
            {
                Id = "relative", Name = "Relative", Icon = "",
                Description = "The cars around you on track, nearest first, with the time to each one. Cars lapping you show in red, cars you're lapping in blue, and rows slide as cars pass."
            };
            Number(relative, "carsAhead", "Cars ahead", "Rows", "3", 0, 8, "Cars shown ahead of you on track.");
            Number(relative, "carsBehind", "Cars behind", "Rows", "3", 0, 8, "Cars shown behind you on track.");
            Bool(relative, "header", "Session header", "Layout", "true", "Session type, clock and car count.");
            Number(relative, "nameWidth", "Name width", "Layout", "170", 110, 320, "Width of the driver name column in pixels.");
            Choice(relative, "nameStyle", "Names", "Layout", "Full", "Full", "First initial", "Last name");
            Column(relative, "colPosition", "Class position", "true", "hdrPos");
            Column(relative, "colNumber", "Car number", "true", "hdrNumber");
            FixedColumn(relative, "Driver", "hdrName");
            Column(relative, "colLicense", "License and iRating", "true", "hdrLicense");
            Column(relative, "colMake", "Car make", "false", "hdrMake");
            Column(relative, "colGap", "Gap on track", "true", "hdrGap");
            Column(relative, "colLast", "Last lap", "false", "hdrLast");
            Column(relative, "colStatus", "Status (pit, out)", "true", "hdrStatus");
            All.Add(relative);

            var toasts = new OverlayType
            {
                Id = "toasts", Name = "Toasts", Icon = "",
                Description = "Race events as glass notifications in the middle of the screen: places gained and lost with your new position, and incident points with the likely cause."
            };
            Bool(toasts, "positions", "Places gained and lost", "Events", "true", "▲ 1  P5 when you pass a car, ▼ when you're passed, with the driver's name.");
            Bool(toasts, "incidents", "Incident points", "Events", "true", "4x Contact with the nearest driver, 2x Loss of control, 1x Off track.");
            Bool(toasts, "practice", "Also outside races", "Events", "false", "Show place changes in practice and qualifying too.");
            Number(toasts, "duration", "Seconds on screen", "Display", "4", 2, 10);
            Number(toasts, "stack", "Most at once", "Display", "3", 1, 5);
            All.Add(toasts);
        }

        /// <summary>A column that can be turned on and off, with a switch for its label in the header row.</summary>
        private static void Column(OverlayType t, string key, string label, string def, string headerKey)
        {
            t.Options.Add(new OverlayOption { Key = key, Label = label, Group = "Columns", Default = def, Kind = PropKind.Bool, HeaderKey = headerKey });
            Header(t, headerKey);
        }

        private static void FixedColumn(OverlayType t, string label, string headerKey)
        {
            t.Options.Add(new OverlayOption { Key = "fixed-" + headerKey, Label = label, Group = "Columns", Default = "true", Kind = PropKind.Bool, HeaderKey = headerKey, Fixed = true });
            Header(t, headerKey);
        }

        private static void Header(OverlayType t, string key) =>
            t.Options.Add(new OverlayOption { Key = key, Label = "Header", Group = "Columns", Default = "false", Kind = PropKind.Bool, Inline = true });

        private static void Number(OverlayType t, string key, string label, string group, string def, double min, double max, string help = null) =>
            t.Options.Add(new OverlayOption { Key = key, Label = label, Group = group, Default = def, Kind = PropKind.Number, Min = min, Max = max, Help = help });

        private static void Bool(OverlayType t, string key, string label, string group, string def, string help = null) =>
            t.Options.Add(new OverlayOption { Key = key, Label = label, Group = group, Default = def, Kind = PropKind.Bool, Help = help });

        private static void Choice(OverlayType t, string key, string label, string group, string def, params string[] choices) =>
            t.Options.Add(new OverlayOption { Key = key, Label = label, Group = group, Default = def, Kind = PropKind.Choice, Choices = choices });

        public static OverlayType Find(string id) => All.FirstOrDefault(t => t.Id == id);
    }

    /// <summary>Where each overlay sits on screen; written only by the overlay process.</summary>
    internal static class OverlayPositions
    {
        public static readonly string FilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "overlay-positions.json");

        public static Dictionary<string, double[]> Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string, double[]>>(File.ReadAllText(FilePath))
                        ?? new Dictionary<string, double[]>();
            }
            catch { }
            return new Dictionary<string, double[]>();
        }

        public static void Save(string id, double left, double top)
        {
            try
            {
                Dictionary<string, double[]> all = Load();
                all[id] = new[] { Math.Round(left), Math.Round(top) };
                File.WriteAllText(FilePath, SettingsStore.ToJson(all));
            }
            catch { }
        }
    }
}
