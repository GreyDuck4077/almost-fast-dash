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
        public const string Velocity = "Velocity";
        public const string Endurance = "Endurance";
        public const string Apex = "Apex";
        public static readonly string[] All = { Velocity, Endurance, Apex };

        public static string Describe(string layout)
        {
            switch (layout)
            {
                case Endurance: return "Fuel strategy, stint and pace for long races";
                case Apex: return "Focused gear, speed and RPM with gaps and delta";
                default: return "Gear, speed, gaps and live delta for sprint races";
            }
        }
    }

    internal sealed class AppSettings
    {
        private static readonly string[] BuiltInIds = { "dashboard-1", "dashboard-2", "dashboard-3" };

        public string DefaultDashboardId { get; set; } = "dashboard-1";
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
            if (!Dashboards.Any(d => d.Id == "dashboard-1")) Dashboards.Insert(0, DashboardSettings.CreateDefault());
            if (!Dashboards.Any(d => d.Id == "dashboard-2")) Dashboards.Insert(Math.Min(1, Dashboards.Count), DashboardSettings.CreateEndurance());
            if (!Dashboards.Any(d => d.Id == "dashboard-3")) Dashboards.Insert(Math.Min(2, Dashboards.Count), DashboardSettings.CreateApex());
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
                case "dashboard-1": return DashboardSettings.CreateDefault();
                case "dashboard-2": return DashboardSettings.CreateEndurance();
                case "dashboard-3": return DashboardSettings.CreateApex();
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
    }

    internal sealed class DashboardSettings
    {
        public string Id { get; set; } = "dashboard-1";
        public string Name { get; set; } = "Velocity";
        public string Layout { get; set; } = "Velocity";
        public List<string> RaceTypes { get; set; } = new List<string>();
        public string AccentColor { get; set; } = "#009BD2";
        public LedBankSettings CenterLeds { get; set; } = new LedBankSettings();
        public SideLedSettings LeftLeds { get; set; } = new SideLedSettings();
        public SideLedSettings RightLeds { get; set; } = new SideLedSettings();
        public AlertSettings Alerts { get; set; } = new AlertSettings();

        public static DashboardSettings CreateForLayout(string layout)
        {
            switch (layout)
            {
                case Layouts.Endurance: return CreateEndurance();
                case Layouts.Apex: return CreateApex();
                default: return CreateDefault();
            }
        }

        public static DashboardSettings CreateDefault() => new DashboardSettings
        {
            RaceTypes = new List<string> { "Oval", "Road", "Dirt Oval", "Dirt Road" },
            CenterLeds = LedBankSettings.CreateShiftLights("Edges to center"),
            LeftLeds = SideLedSettings.CreateDefault(),
            RightLeds = SideLedSettings.CreateDefault()
        };

        public static DashboardSettings CreateEndurance() => new DashboardSettings
        {
            Id = "dashboard-2",
            Name = "Endurance",
            Layout = Layouts.Endurance,
            AccentColor = "#27C7D9",
            RaceTypes = new List<string> { "Road" },
            CenterLeds = LedBankSettings.CreateShiftLights("Left to right"),
            LeftLeds = SideLedSettings.CreateDefault(),
            RightLeds = SideLedSettings.CreateDefault()
        };

        public static DashboardSettings CreateApex() => new DashboardSettings
        {
            Id = "dashboard-3",
            Name = "Apex",
            Layout = Layouts.Apex,
            AccentColor = "#35D0C5",
            RaceTypes = new List<string>(),
            CenterLeds = LedBankSettings.CreateShiftLights("Center outward"),
            LeftLeds = SideLedSettings.CreateDefault(),
            RightLeds = SideLedSettings.CreateDefault()
        };

        public void Normalize()
        {
            if (!Layouts.All.Contains(Layout)) Layout = Layouts.Velocity;
            if (string.IsNullOrWhiteSpace(Name)) Name = Layout;
            if (RaceTypes == null) RaceTypes = new List<string>();
            if (CenterLeds == null) CenterLeds = LedBankSettings.CreateShiftLights("Edges to center");
            if (LeftLeds == null) LeftLeds = SideLedSettings.CreateDefault();
            if (RightLeds == null) RightLeds = SideLedSettings.CreateDefault();
            if (Alerts == null) Alerts = new AlertSettings();
            // "Red" was an early side-light profile; it has always meant one color.
            foreach (LedBankSettings bank in new LedBankSettings[] { CenterLeds, LeftLeds, RightLeds })
                if (bank.ColorProfile == "Red") bank.ColorProfile = LedProfiles.Single;
        }

        [JsonIgnore]
        public Color Accent => SettingsStore.ParseColor(AccentColor, Color.FromArgb(0, 155, 210));
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
                        loaded.EnsureBuiltInDashboards();
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
