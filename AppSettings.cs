using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace DV5Direct
{
    internal sealed class AppSettings
    {
        public string DefaultDashboardId { get; set; } = "dashboard-1";
        public List<DashboardSettings> Dashboards { get; set; } = new List<DashboardSettings>();

        public static AppSettings CreateDefault() => new AppSettings
        {
            Dashboards = new List<DashboardSettings> { DashboardSettings.CreateDefault() }
        };

        public DashboardSettings GetDefaultDashboard()
        {
            EnsureBuiltInDashboards();
            DashboardSettings dashboard = Dashboards?.FirstOrDefault(d => d.Id == DefaultDashboardId)
                ?? Dashboards?.FirstOrDefault();
            if (dashboard == null)
            {
                dashboard = DashboardSettings.CreateDefault();
                Dashboards = new List<DashboardSettings> { dashboard };
                DefaultDashboardId = dashboard.Id;
            }
            return dashboard;
        }

        public void EnsureBuiltInDashboards()
        {
            if (Dashboards == null) Dashboards = new List<DashboardSettings>();
            if (!Dashboards.Any(d => d.Id == "dashboard-1")) Dashboards.Add(DashboardSettings.CreateDefault());
            if (!Dashboards.Any(d => d.Id == "dashboard-2")) Dashboards.Add(DashboardSettings.CreateEndurance());
            if (!Dashboards.Any(d => d.Id == "dashboard-3")) Dashboards.Add(DashboardSettings.CreateApex());
        }
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

        public static DashboardSettings CreateDefault() => new DashboardSettings
        {
            RaceTypes = new List<string> { "Oval", "Road", "Dirt Oval", "Dirt Road" },
            CenterLeds = new LedBankSettings
            {
                Function = "RPM",
                Pattern = "Edges to center",
                Brightness = 55,
                ColorProfile = "Green / Yellow / Red"
            },
            LeftLeds = SideLedSettings.CreateDefault(),
            RightLeds = SideLedSettings.CreateDefault()
        };

        public static DashboardSettings CreateEndurance() => new DashboardSettings
        {
            Id = "dashboard-2",
            Name = "Endurance",
            Layout = "Endurance",
            AccentColor = "#27C7D9",
            RaceTypes = new List<string> { "Road" },
            CenterLeds = new LedBankSettings
            {
                Function = "RPM", Pattern = "Left to right", Brightness = 55,
                ColorProfile = "Green / Yellow / Red"
            },
            LeftLeds = SideLedSettings.CreateDefault(),
            RightLeds = SideLedSettings.CreateDefault()
        };

        public static DashboardSettings CreateApex() => new DashboardSettings
        {
            Id = "dashboard-3",
            Name = "Apex",
            Layout = "Apex",
            AccentColor = "#35D0C5",
            RaceTypes = new List<string>(),
            CenterLeds = new LedBankSettings
            {
                Function = "RPM", Pattern = "Center outward", Brightness = 55,
                ColorProfile = "Green / Yellow / Red"
            },
            LeftLeds = SideLedSettings.CreateDefault(),
            RightLeds = SideLedSettings.CreateDefault()
        };

        [JsonIgnore]
        public Color Accent => SettingsStore.ParseColor(AccentColor, Color.FromArgb(0, 155, 210));
    }

    internal class LedBankSettings
    {
        public string Function { get; set; } = "RPM";
        public string Pattern { get; set; } = "Edges to center";
        public int Brightness { get; set; } = 55;
        public string ColorProfile { get; set; } = "Green / Yellow / Red";
        public string LowColor { get; set; } = "#00B85A";
        public string MidColor { get; set; } = "#FFB800";
        public string HighColor { get; set; } = "#FF202F";
    }

    internal sealed class SideLedSettings : LedBankSettings
    {
        public static SideLedSettings CreateDefault() => new SideLedSettings
        {
            Function = "Blind spot",
            Pattern = "Position",
            Brightness = 45,
            ColorProfile = "Red",
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
                        Save(loaded);
                        return loaded;
                    }
                }
            }
            catch { }
            var defaults = AppSettings.CreateDefault();
            try { Save(defaults); } catch { }
            return defaults;
        }

        public static void Save(AppSettings settings)
        {
            string temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, JsonConvert.SerializeObject(settings, Formatting.Indented));
            if (File.Exists(FilePath)) File.Replace(temporary, FilePath, null);
            else File.Move(temporary, FilePath);
        }

        public static Color ParseColor(string value, Color fallback)
        {
            try { return ColorTranslator.FromHtml(value); }
            catch { return fallback; }
        }

        public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }
}
