using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace DV5Direct
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
        public const string Summit = "Summit";
        public const string Formula = "Formula";
        public const string Oval = "Oval";
        public const string Endurance = "Endurance";
        public static readonly string[] All = { Summit, Formula, Oval, Endurance };

        // Layouts from earlier versions; dashboards that used them move to Summit.
        public static readonly string[] Retired = { "Velocity", "Apex", "Telemetry" };

        public static string Describe(string layout)
        {
            switch (layout)
            {
                case Formula: return "Wheel-style grid with a big gear and full-width delta bar";
                case Oval: return "Stock-car ovals: spotter, gaps, lap speed, cautions and fuel to the end";
                case Endurance: return "Fuel time, stops, stint and lap consistency for long races";
                default: return "Flagship: shift arc, relative, fuel plan, lap times and pit mode";
            }
        }
    }

    internal sealed class AppSettings
    {
        private static readonly string[] BuiltInIds = { "dashboard-summit", "dashboard-formula", "dashboard-oval", "dashboard-2" };
        private static readonly string[] RetiredIds = { "dashboard-1", "dashboard-3", "dashboard-telemetry" };
        private const int CurrentSchema = 3;

        public int SchemaVersion { get; set; }
        public string DefaultDashboardId { get; set; } = "dashboard-summit";
        public GeneralSettings General { get; set; } = new GeneralSettings();
        public List<DashboardSettings> Dashboards { get; set; } = new List<DashboardSettings>();

        public static AppSettings CreateDefault()
        {
            var settings = new AppSettings();
            settings.EnsureBuiltInDashboards();
            return settings;
        }

        public static bool IsBuiltIn(DashboardSettings dashboard) => BuiltInIds.Contains(dashboard?.Id);

        public DashboardSettings GetDefaultDashboard()
        {
            EnsureBuiltInDashboards();
            return Dashboards.FirstOrDefault(d => d.Id == DefaultDashboardId) ?? Dashboards.First();
        }

        /// <summary>
        /// Picks the dashboard for a session. The default dashboard wins when it is
        /// assigned to the discipline; otherwise the first assigned dashboard is used.
        /// </summary>
        public DashboardSettings ResolveDashboard(string discipline)
        {
            DashboardSettings fallback = GetDefaultDashboard();
            if (!General.AutoSwitchByDiscipline || string.IsNullOrEmpty(discipline)) return fallback;
            if (fallback.RaceTypes.Contains(discipline)) return fallback;
            return Dashboards.FirstOrDefault(d => d.RaceTypes != null && d.RaceTypes.Contains(discipline)) ?? fallback;
        }

        public void EnsureBuiltInDashboards()
        {
            if (Dashboards == null) Dashboards = new List<DashboardSettings>();
            if (General == null) General = new GeneralSettings();
            bool upgrading = SchemaVersion < CurrentSchema;

            if (upgrading)
            {
                // Version 3 trims the lineup to four dashboards. Track-type
                // assignments from retired built-ins carry over to their successors.
                var retired = Dashboards.Where(d => RetiredIds.Contains(d.Id)).ToList();
                var carriedTypes = retired.SelectMany(d => d.RaceTypes ?? new List<string>()).Distinct().ToList();
                foreach (DashboardSettings d in retired) Dashboards.Remove(d);
                foreach (DashboardSettings d in Dashboards.Where(d => Layouts.Retired.Contains(d.Layout))) d.Layout = Layouts.Summit;
                if (RetiredIds.Contains(DefaultDashboardId)) DefaultDashboardId = "dashboard-summit";

                if (!Dashboards.Any(d => d.Id == "dashboard-summit")) Dashboards.Insert(0, DashboardSettings.CreateSummit());
                if (!Dashboards.Any(d => d.Id == "dashboard-oval"))
                {
                    // New oval dashboard takes over oval tracks from every other dashboard.
                    foreach (DashboardSettings d in Dashboards)
                        d.RaceTypes?.RemoveAll(type => type == Disciplines.Oval || type == Disciplines.DirtOval);
                    Dashboards.Add(DashboardSettings.CreateOval());
                }
                DashboardSettings summit = Dashboards.First(d => d.Id == "dashboard-summit");
                // Summit replaces the old road dashboards, so it inherits their road assignments.
                foreach (string type in carriedTypes.Where(type => type == Disciplines.Road || type == Disciplines.DirtRoad))
                    if (!summit.RaceTypes.Contains(type)) summit.RaceTypes.Add(type);
                SchemaVersion = CurrentSchema;
            }

            if (!Dashboards.Any(d => d.Id == "dashboard-summit")) Dashboards.Insert(0, DashboardSettings.CreateSummit());
            if (!Dashboards.Any(d => d.Id == "dashboard-formula")) Dashboards.Insert(Math.Min(1, Dashboards.Count), DashboardSettings.CreateFormula());
            if (!Dashboards.Any(d => d.Id == "dashboard-oval")) Dashboards.Insert(Math.Min(2, Dashboards.Count), DashboardSettings.CreateOval());
            if (!Dashboards.Any(d => d.Id == "dashboard-2")) Dashboards.Insert(Math.Min(3, Dashboards.Count), DashboardSettings.CreateEndurance());

            // Built-ins first in a fixed order, then the user's own dashboards.
            Dashboards = Dashboards.OrderBy(d => IsBuiltIn(d) ? Array.IndexOf(BuiltInIds, d.Id) : BuiltInIds.Length).ToList();
            foreach (DashboardSettings dashboard in Dashboards) dashboard.Normalize();
            if (!Dashboards.Any(d => d.Id == DefaultDashboardId)) DefaultDashboardId = Dashboards[0].Id;
        }

        public DashboardSettings AddDashboard(string layout, DashboardSettings copyFrom = null)
        {
            DashboardSettings created = copyFrom != null
                ? SettingsStore.Clone(copyFrom)
                : DashboardSettings.CreateForLayout(layout);
            created.Id = NextId();
            created.Name = UniqueName(copyFrom != null ? copyFrom.Name + " copy" : created.Name);
            created.RaceTypes = new List<string>();
            Dashboards.Add(created);
            return created;
        }

        public void ResetDashboard(DashboardSettings dashboard)
        {
            DashboardSettings factory = BuiltInFactory(dashboard.Id) ?? DashboardSettings.CreateForLayout(dashboard.Layout);
            dashboard.AccentColor = factory.AccentColor;
            dashboard.CenterLeds = factory.CenterLeds;
            dashboard.LeftLeds = factory.LeftLeds;
            dashboard.RightLeds = factory.RightLeds;
            dashboard.BrowLeds = factory.BrowLeds;
            dashboard.Alerts = factory.Alerts;
        }

        public bool RemoveDashboard(DashboardSettings dashboard)
        {
            if (IsBuiltIn(dashboard) || !Dashboards.Remove(dashboard)) return false;
            if (DefaultDashboardId == dashboard.Id) DefaultDashboardId = Dashboards[0].Id;
            return true;
        }

        private static DashboardSettings BuiltInFactory(string id)
        {
            switch (id)
            {
                case "dashboard-summit": return DashboardSettings.CreateSummit();
                case "dashboard-formula": return DashboardSettings.CreateFormula();
                case "dashboard-oval": return DashboardSettings.CreateOval();
                case "dashboard-2": return DashboardSettings.CreateEndurance();
                default: return null;
            }
        }

        private string NextId()
        {
            int next = 4;
            while (Dashboards.Any(d => d.Id == "dashboard-" + next)) next++;
            return "dashboard-" + next;
        }

        private string UniqueName(string name)
        {
            string candidate = name;
            for (int n = 2; Dashboards.Any(d => string.Equals(d.Name, candidate, StringComparison.OrdinalIgnoreCase)); n++)
                candidate = name + " " + n;
            return candidate;
        }
    }

    internal sealed class GeneralSettings
    {
        public string LedPort { get; set; } = "COM7";
        public int ScreenBrightness { get; set; } = 100;
        public string SpeedUnit { get; set; } = "MPH";
        public string FuelUnit { get; set; } = "Liters";
        public bool AutoSwitchByDiscipline { get; set; } = true;
        public bool DiagnosticsEnabled { get; set; } = true;
        /// <summary>SSG LB-10 brow light bar.</summary>
        public bool BrowEnabled { get; set; } = true;
        public string BrowPort { get; set; } = "COM6";
        public int BrowLedCount { get; set; } = 10;
    }

    internal sealed class DashboardSettings
    {
        public string Id { get; set; } = "dashboard-summit";
        public string Name { get; set; } = "Summit";
        public string Layout { get; set; } = Layouts.Summit;
        public List<string> RaceTypes { get; set; } = new List<string>();
        public string AccentColor { get; set; } = "#2DD4BF";
        public LedBankSettings CenterLeds { get; set; } = new LedBankSettings();
        public SideLedSettings LeftLeds { get; set; } = new SideLedSettings();
        public SideLedSettings RightLeds { get; set; } = new SideLedSettings();
        public BrowLedSettings BrowLeds { get; set; }
        public AlertSettings Alerts { get; set; } = new AlertSettings();

        public static DashboardSettings CreateForLayout(string layout)
        {
            switch (layout)
            {
                case Layouts.Formula: return CreateFormula();
                case Layouts.Oval: return CreateOval();
                case Layouts.Endurance: return CreateEndurance();
                default: return CreateSummit();
            }
        }

        private static DashboardSettings Create(string id, string name, string layout, string accent, string pattern, string brow, params string[] raceTypes) => new DashboardSettings
        {
            Id = id,
            Name = name,
            Layout = layout,
            AccentColor = accent,
            RaceTypes = raceTypes.ToList(),
            CenterLeds = LedBankSettings.CreateShiftLights(pattern),
            LeftLeds = SideLedSettings.CreateDefault(),
            RightLeds = SideLedSettings.CreateDefault(),
            BrowLeds = BrowLedSettings.CreateDefault(brow)
        };

        public static DashboardSettings CreateSummit() =>
            Create("dashboard-summit", "Summit", Layouts.Summit, "#2DD4BF", "Edges to center", BrowFunctions.Shift, Disciplines.Road, Disciplines.DirtRoad);

        public static DashboardSettings CreateFormula() =>
            Create("dashboard-formula", "Formula", Layouts.Formula, "#38BDF8", "Left to right", BrowFunctions.Shift);

        public static DashboardSettings CreateOval() =>
            Create("dashboard-oval", "Oval", Layouts.Oval, "#FFB020", "Edges to center", BrowFunctions.BlindSpot, Disciplines.Oval, Disciplines.DirtOval);

        public static DashboardSettings CreateEndurance() =>
            Create("dashboard-2", "Endurance", Layouts.Endurance, "#27C7D9", "Left to right", BrowFunctions.Delta);

        public void Normalize()
        {
            if (!Layouts.All.Contains(Layout)) Layout = Layouts.Summit;
            if (string.IsNullOrWhiteSpace(Name)) Name = Layout;
            if (RaceTypes == null) RaceTypes = new List<string>();
            if (CenterLeds == null) CenterLeds = LedBankSettings.CreateShiftLights("Edges to center");
            if (LeftLeds == null) LeftLeds = SideLedSettings.CreateDefault();
            if (RightLeds == null) RightLeds = SideLedSettings.CreateDefault();
            if (BrowLeds == null) BrowLeds = BrowLedSettings.CreateDefault(Layout == Layouts.Oval ? BrowFunctions.BlindSpot : BrowFunctions.Shift);
            if (!BrowFunctions.All.Contains(BrowLeds.Function)) BrowLeds.Function = BrowFunctions.Shift;
            if (Alerts == null) Alerts = new AlertSettings();
            // "Red" was an early side-light profile; it has always meant one color.
            foreach (LedBankSettings bank in new LedBankSettings[] { CenterLeds, LeftLeds, RightLeds, BrowLeds })
                if (bank.ColorProfile == "Red") bank.ColorProfile = LedProfiles.Single;
        }

        [JsonIgnore]
        public Color Accent => SettingsStore.ParseColor(AccentColor, Color.FromArgb(0, 155, 210));
    }

    internal static class BrowFunctions
    {
        public const string Shift = "Shift lights";
        public const string BlindSpot = "Blind spot";
        public const string Flags = "Flags only";
        public const string Pedals = "Brake and throttle";
        public const string Brake = "Brake pressure";
        public const string Throttle = "Throttle";
        public const string Delta = "Lap delta";
        public const string Fuel = "Fuel level";
        public const string Off = "Off";
        public static readonly string[] All = { Shift, BlindSpot, Flags, Pedals, Brake, Throttle, Delta, Fuel, Off };

        public static string Describe(string function)
        {
            switch (function)
            {
                case Shift: return "Fills from both ends toward the middle as revs rise and flashes red at the shift point.";
                case BlindSpot: return "Left half lights for a car on your left, right half for your right; brighter toward the front when it's ahead.";
                case Flags: return "Dark until a flag is shown, then flashes that flag's color.";
                case Pedals: return "Brake fills the left half in red, throttle fills the right half in green.";
                case Brake: return "Fills from the middle outward with brake pressure.";
                case Throttle: return "Fills from the middle outward with throttle.";
                case Delta: return "Grows right in green when you're faster than your best lap, left in red when slower.";
                case Fuel: return "Shows the fuel tank level, turning amber and red as it runs low.";
                default: return "The light bar stays dark.";
            }
        }
    }

    internal sealed class BrowLedSettings : LedBankSettings
    {
        /// <summary>When on, any racing flag takes over the light bar and flashes its color.</summary>
        public bool FlagOverride { get; set; } = true;

        public static BrowLedSettings CreateDefault(string function) => new BrowLedSettings
        {
            Function = function,
            Pattern = "Edges to center",
            Brightness = 60,
            ColorProfile = LedProfiles.GreenYellowRed,
            FlagOverride = true
        };
    }

    internal static class LedProfiles
    {
        public const string GreenYellowRed = "Green / Yellow / Red";
        public const string BluePurpleRed = "Blue / Purple / Red";
        public const string Single = "Single color";
        public const string Custom = "Custom";
        public static readonly string[] All = { GreenYellowRed, BluePurpleRed, Single, Custom };

        /// <summary>Returns the preset stage colors, or null when the profile keeps the user's colors.</summary>
        public static string[] PresetColors(string profile)
        {
            switch (profile)
            {
                case GreenYellowRed: return new[] { "#00B85A", "#FFB800", "#FF202F" };
                case BluePurpleRed: return new[] { "#00BFFF", "#9370DB", "#FF0000" };
                default: return null;
            }
        }
    }

    internal class LedBankSettings
    {
        public string Function { get; set; } = "RPM";
        public string Pattern { get; set; } = "Edges to center";
        public int Brightness { get; set; } = 55;
        public string ColorProfile { get; set; } = LedProfiles.GreenYellowRed;
        public string LowColor { get; set; } = "#00B85A";
        public string MidColor { get; set; } = "#FFB800";
        public string HighColor { get; set; } = "#FF202F";

        public static LedBankSettings CreateShiftLights(string pattern) => new LedBankSettings
        {
            Function = "RPM",
            Pattern = pattern,
            Brightness = 55,
            ColorProfile = LedProfiles.GreenYellowRed
        };
    }

    internal sealed class SideLedSettings : LedBankSettings
    {
        public static SideLedSettings CreateDefault() => new SideLedSettings
        {
            Function = "Blind spot",
            Pattern = "Position",
            Brightness = 45,
            ColorProfile = LedProfiles.Single,
            LowColor = "#FF202F",
            MidColor = "#FF202F",
            HighColor = "#FF202F"
        };
    }

    internal sealed class AlertSettings
    {
        public int BlinkCount { get; set; } = 6;
        public int BlinkMilliseconds { get; set; } = 150;
        public int HoldMilliseconds { get; set; } = 0;
    }

    internal static class SettingsStore
    {
        public static readonly string FilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var loaded = JsonConvert.DeserializeObject<AppSettings>(File.ReadAllText(FilePath));
                    if (loaded != null)
                    {
                        int schema = loaded.SchemaVersion;
                        loaded.EnsureBuiltInDashboards();
                        // Persist one-time upgrades so every process sees the same lineup.
                        if (loaded.SchemaVersion != schema) try { Save(loaded); } catch { }
                        return loaded;
                    }
                }
            }
            catch { }
            var defaults = AppSettings.CreateDefault();
            try { if (!File.Exists(FilePath)) Save(defaults); } catch { }
            return defaults;
        }

        public static void Save(AppSettings settings)
        {
            string temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, JsonConvert.SerializeObject(settings, Formatting.Indented));
            if (File.Exists(FilePath)) File.Replace(temporary, FilePath, null);
            else File.Move(temporary, FilePath);
        }

        public static DateTime LastWriteUtc()
        {
            try { return File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : DateTime.MinValue; }
            catch { return DateTime.MinValue; }
        }

        public static T Clone<T>(T value) => JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value));

        public static Color ParseColor(string value, Color fallback)
        {
            try { return ColorTranslator.FromHtml(value); }
            catch { return fallback; }
        }

        public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }
}
