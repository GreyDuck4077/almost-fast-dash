using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace AlmostFastOverlays
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

    internal sealed class AppSettings
    {
        private const int CurrentSchema = 5;

        public int SchemaVersion { get; set; }
        public string DefaultDashboardId { get; set; } = "dashboard-halo";
        public GeneralSettings General { get; set; } = new GeneralSettings();
        public List<DashboardSettings> Dashboards { get; set; } = new List<DashboardSettings>();

        public static AppSettings CreateDefault()
        {
            var settings = new AppSettings();
            settings.EnsureBuiltInDashboards();
            return settings;
        }

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

        /// <summary>Makes sure there is at least one dashboard and upgrades settings from earlier versions.</summary>
        public void EnsureBuiltInDashboards()
        {
            if (Dashboards == null) Dashboards = new List<DashboardSettings>();
            if (General == null) General = new GeneralSettings();
            if (General.LedDevices == null) General.LedDevices = new List<LedDeviceSettings>();
            General.LedDevices.RemoveAll(d => d == null);
            foreach (LedDeviceSettings device in General.LedDevices) device.Normalize();
            Dashboards.RemoveAll(d => d == null);

            if (SchemaVersion < CurrentSchema)
            {
                // Two LB-10 bars daisy-chain on one controller, 10 LEDs each.
                if (SchemaVersion < 4 && General.BrowLedCount == 10) General.BrowLedCount = 20;
                if (SchemaVersion > 0) ReplaceCodeLayouts();
                SchemaVersion = CurrentSchema;
            }

            if (Dashboards.Count == 0)
            {
                Dashboards.Add(DashboardSettings.CreateHalo());
                Dashboards.Add(DashboardSettings.CreateOval());
            }
            foreach (DashboardSettings dashboard in Dashboards) dashboard.Normalize();
            if (!Dashboards.Any(d => d.Id == DefaultDashboardId)) DefaultDashboardId = Dashboards[0].Id;
        }

        /// <summary>
        /// Schema 5: the code-drawn dashboards are gone. Their track types move to the
        /// Halo and Oval designs, which are added when the user has no design of that name.
        /// </summary>
        private void ReplaceCodeLayouts()
        {
            var retired = Dashboards.Where(d => d.Layout != Layouts.Custom).ToList();
            if (retired.Count == 0) return;
            var orphanTypes = retired.SelectMany(d => d.RaceTypes ?? new List<string>()).Distinct().ToList();
            bool defaultRetired = retired.Any(d => d.Id == DefaultDashboardId);
            foreach (DashboardSettings d in retired) Dashboards.Remove(d);

            DashboardSettings Ensure(string name, Func<DashboardSettings> create)
            {
                DashboardSettings found = Dashboards.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));
                if (found != null) return found;
                found = create();
                found.RaceTypes = new List<string>();
                if (Dashboards.Any(d => d.Id == found.Id)) found.Id = NextId();
                Dashboards.Add(found);
                return found;
            }

            // A new Oval design takes over the oval tracks, as the old Oval layout did.
            if (!Dashboards.Any(d => string.Equals(d.Name, "Oval", StringComparison.OrdinalIgnoreCase)))
            {
                foreach (DashboardSettings d in Dashboards)
                    d.RaceTypes?.RemoveAll(type => type == Disciplines.Oval || type == Disciplines.DirtOval);
                Ensure("Oval", DashboardSettings.CreateOval).RaceTypes.AddRange(new[] { Disciplines.Oval, Disciplines.DirtOval });
            }

            foreach (string type in orphanTypes.Where(type => !Dashboards.Any(d => d.RaceTypes != null && d.RaceTypes.Contains(type))))
            {
                bool oval = type == Disciplines.Oval || type == Disciplines.DirtOval;
                DashboardSettings target = oval ? Ensure("Oval", DashboardSettings.CreateOval) : Ensure("Halo", DashboardSettings.CreateHalo);
                target.RaceTypes.Add(type);
            }
            if (Dashboards.Count == 0) return;
            if (defaultRetired) DefaultDashboardId = (Dashboards.FirstOrDefault(d => d.Name == "Halo") ?? Dashboards[0]).Id;
        }

        /// <summary>Adds a copy of a dashboard, or a new one from a template.</summary>
        public DashboardSettings AddDashboard(DashboardSettings copyFrom)
        {
            DashboardSettings created = SettingsStore.Clone(copyFrom);
            created.Id = NextId();
            created.Name = UniqueName(copyFrom.Name + " copy");
            created.RaceTypes = new List<string>();
            Dashboards.Add(created);
            return created;
        }

        /// <summary>Adds a designed dashboard started from a template.</summary>
        public DashboardSettings AddDesignedDashboard(DesignTemplate template, string name = null)
        {
            DashboardSettings created = DashboardSettings.CreateDesigned(template);
            created.Id = NextId();
            created.Name = UniqueName(name ?? template.Name);
            created.RaceTypes = new List<string>();
            Dashboards.Add(created);
            return created;
        }

        /// <summary>Puts a dashboard's classic light settings back to their defaults.</summary>
        public void ResetDashboard(DashboardSettings dashboard)
        {
            DashboardSettings factory = dashboard.Name == "Oval" ? DashboardSettings.CreateOval() : DashboardSettings.CreateHalo();
            dashboard.CenterLeds = factory.CenterLeds;
            dashboard.LeftLeds = factory.LeftLeds;
            dashboard.RightLeds = factory.RightLeds;
            dashboard.BrowLeds = factory.BrowLeds;
            dashboard.Alerts = factory.Alerts;
        }

        /// <summary>Removes a dashboard; the last one can't be removed.</summary>
        public bool RemoveDashboard(DashboardSettings dashboard)
        {
            if (Dashboards.Count <= 1 || !Dashboards.Remove(dashboard)) return false;
            if (DefaultDashboardId == dashboard.Id) DefaultDashboardId = Dashboards[0].Id;
            return true;
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
        /// <summary>Total LEDs across all chained bars; two LB-10s make 20 (left bar first).</summary>
        public int BrowLedCount { get; set; } = 20;
        /// <summary>
        /// Sends each LED frame as one packet instead of one packet per colour run.
        /// Much faster, but off until confirmed on the hardware with the LED test.
        /// </summary>
        public bool FastLedUpdates { get; set; }

        /// <summary>LED devices besides the DV-5 and LB-10: Arduino boards, Adalight strips, WLED controllers.</summary>
        public List<LedDeviceSettings> LedDevices { get; set; } = new List<LedDeviceSettings>();
    }

    internal sealed class DashboardSettings
    {
        public string Id { get; set; } = "dashboard-new";
        public string Name { get; set; } = "Dashboard";
        public string Layout { get; set; } = Layouts.Custom;
        public List<string> RaceTypes { get; set; } = new List<string>();
        public string AccentColor { get; set; } = "#22D3EE";
        public LedBankSettings CenterLeds { get; set; } = new LedBankSettings();
        public SideLedSettings LeftLeds { get; set; } = new SideLedSettings();
        public SideLedSettings RightLeds { get; set; } = new SideLedSettings();
        public BrowLedSettings BrowLeds { get; set; }
        public AlertSettings Alerts { get; set; } = new AlertSettings();
        /// <summary>The dashboard's design: screens of widgets, drawn by <see cref="LayoutRenderer"/>.</summary>
        public DashLayout Design { get; set; }
        /// <summary>Layered light profile; null keeps the classic light settings above.</summary>
        public LightProfile Lights { get; set; }

        /// <summary>A new dashboard started from a template.</summary>
        public static DashboardSettings CreateDesigned(DesignTemplate template)
        {
            DashboardSettings created = Create("dashboard-new", template.Name, template.Accent, "Edges to center", BrowFunctions.Shift);
            created.Design = template.Create();
            return created;
        }

        /// <summary>The default road dashboard.</summary>
        public static DashboardSettings CreateHalo()
        {
            DashboardSettings halo = Create("dashboard-halo", "Halo", "#22D3EE", "Edges to center", BrowFunctions.Shift, Disciplines.Road, Disciplines.DirtRoad);
            halo.Design = DesignTemplates.Find("halo").Create();
            halo.Lights = LightPresets.Refined();
            return halo;
        }

        /// <summary>The default oval dashboard.</summary>
        public static DashboardSettings CreateOval()
        {
            DashboardSettings oval = Create("dashboard-oval", "Oval", "#FFB020", "Edges to center", BrowFunctions.BlindSpot, Disciplines.Oval, Disciplines.DirtOval);
            oval.Design = DesignTemplates.Find("oval").Create();
            oval.Lights = LightPresets.SpotterFocus();
            return oval;
        }

        private static DashboardSettings Create(string id, string name, string accent, string pattern, string brow, params string[] raceTypes) => new DashboardSettings
        {
            Id = id,
            Name = name,
            Layout = Layouts.Custom,
            AccentColor = accent,
            RaceTypes = raceTypes.ToList(),
            CenterLeds = LedBankSettings.CreateShiftLights(pattern),
            LeftLeds = SideLedSettings.CreateDefault(),
            RightLeds = SideLedSettings.CreateDefault(),
            BrowLeds = BrowLedSettings.CreateDefault(brow)
        };

        public void Normalize()
        {
            Layout = Layouts.Custom;
            if (string.IsNullOrWhiteSpace(Name)) Name = "Dashboard";
            if (RaceTypes == null) RaceTypes = new List<string>();
            if (CenterLeds == null) CenterLeds = LedBankSettings.CreateShiftLights("Edges to center");
            if (LeftLeds == null) LeftLeds = SideLedSettings.CreateDefault();
            if (RightLeds == null) RightLeds = SideLedSettings.CreateDefault();
            if (BrowLeds == null) BrowLeds = BrowLedSettings.CreateDefault(BrowFunctions.Shift);
            if (!BrowFunctions.All.Contains(BrowLeds.Function)) BrowLeds.Function = BrowFunctions.Shift;
            if (Alerts == null) Alerts = new AlertSettings();
            if (Design == null) Design = DesignTemplates.Blank();
            Design.Normalize();
            Lights?.Normalize();
            // "Red" was an early side-light profile; it has always meant one color.
            foreach (LedBankSettings bank in new LedBankSettings[] { CenterLeds, LeftLeds, RightLeds, BrowLeds })
                if (bank.ColorProfile == "Red") bank.ColorProfile = LedProfiles.Single;
        }

        [ScriptIgnore]
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
                    var loaded = Json.Deserialize<AppSettings>(File.ReadAllText(FilePath));
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
            File.WriteAllText(temporary, Indent(Json.Serialize(settings)));
            if (File.Exists(FilePath)) File.Replace(temporary, FilePath, null);
            else File.Move(temporary, FilePath);
        }

        public static DateTime LastWriteUtc()
        {
            try { return File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : DateTime.MinValue; }
            catch { return DateTime.MinValue; }
        }

        public static T Clone<T>(T value) => Json.Deserialize<T>(Json.Serialize(value));

        public static string ToJson(object value) => Json.Serialize(value);

        // Designs can embed images, so allow large settings files.
        private static JavaScriptSerializer Json => new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 256 };

        /// <summary>Lays out compact JSON with two-space indents so settings.json stays readable.</summary>
        private static string Indent(string json)
        {
            var output = new System.Text.StringBuilder(json.Length * 2);
            int depth = 0;
            bool quoted = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (quoted)
                {
                    output.Append(c);
                    if (c == '\\' && i + 1 < json.Length) output.Append(json[++i]);
                    else if (c == '"') quoted = false;
                    continue;
                }
                switch (c)
                {
                    case '"': quoted = true; output.Append(c); break;
                    case '{':
                    case '[':
                        output.Append(c);
                        if (i + 1 < json.Length && (json[i + 1] == '}' || json[i + 1] == ']')) { output.Append(json[++i]); break; }
                        output.AppendLine().Append(' ', ++depth * 2);
                        break;
                    case '}':
                    case ']': output.AppendLine().Append(' ', --depth * 2).Append(c); break;
                    case ',': output.Append(c).AppendLine().Append(' ', depth * 2); break;
                    case ':': output.Append(": "); break;
                    default: output.Append(c); break;
                }
            }
            return output.ToString();
        }

        public static Color ParseColor(string value, Color fallback)
        {
            try { return ColorTranslator.FromHtml(value); }
            catch { return fallback; }
        }

        public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }
}
