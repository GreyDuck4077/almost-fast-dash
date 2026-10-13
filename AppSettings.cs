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
        private const int CurrentSchema = 11;

        public int SchemaVersion { get; set; }
        public string DefaultDashboardId { get; set; } = DashboardSettings.RoadId;
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
            if (General.Devices == null) General.Devices = new List<DeviceSettings>();
            General.Devices.RemoveAll(d => d == null);
            Dashboards.RemoveAll(d => d == null);

            if (SchemaVersion < CurrentSchema)
            {
                // Two LB-10 bars daisy-chain on one controller, 10 LEDs each.
                if (SchemaVersion < 4 && General.BrowLedCount == 10) General.BrowLedCount = 20;
                if (SchemaVersion > 0 && SchemaVersion < 5) ReplaceCodeLayouts();
                if (SchemaVersion > 0 && SchemaVersion < 6) DevicesFromFixedSettings();
                if (SchemaVersion > 0 && SchemaVersion < 7) RenameBrowLayers();
                if (SchemaVersion > 0 && SchemaVersion < 8) ClassicLightsToLayers();
                if (SchemaVersion > 0 && SchemaVersion < 9) AddFlagships();
                if (SchemaVersion == 9) RedesignRoundy();
                if (SchemaVersion >= 9 && SchemaVersion < 11) GlassRoady();
                SchemaVersion = CurrentSchema;
            }
            foreach (DeviceSettings device in General.Devices) device.Normalize();

            if (Dashboards.Count == 0)
            {
                Dashboards.Add(DashboardSettings.CreateRoad());
                Dashboards.Add(DashboardSettings.CreateOval());
            }
            foreach (DashboardSettings dashboard in Dashboards) dashboard.Normalize();
            if (!Dashboards.Any(d => d.Id == DefaultDashboardId)) DefaultDashboardId = Dashboards[0].Id;
        }

        /// <summary>
        /// Schema 9: the flagship dashboards, Roady and Roundy, arrive and take over road and oval tracks.
        /// Every existing dashboard stays as it was, apart from handing those track types over.
        /// </summary>
        private void AddFlagships()
        {
            if (Dashboards.Count == 0) return;
            void Take(DashboardSettings flagship, params string[] types)
            {
                if (Dashboards.Any(d => d.Id == flagship.Id)) return;
                foreach (DashboardSettings d in Dashboards) d.RaceTypes?.RemoveAll(types.Contains);
                Dashboards.Insert(0, flagship);
            }
            Take(DashboardSettings.CreateOval(), Disciplines.Oval, Disciplines.DirtOval);
            Take(DashboardSettings.CreateRoad(), Disciplines.Road, Disciplines.DirtRoad);
            DefaultDashboardId = DashboardSettings.RoadId;
        }

        /// <summary>
        /// Schema 10: Roundy was redrawn round a numbered dial. A Roundy still in its first form (with its
        /// boxed panels) takes the new design. One that has been rebuilt by hand is left alone.
        /// </summary>
        private void RedesignRoundy()
        {
            DashboardSettings roundy = Dashboards.FirstOrDefault(d => d.Id == DashboardSettings.OvalId);
            var widgets = roundy?.Design?.Screens?.FirstOrDefault()?.Widgets;
            if (widgets == null || !widgets.Any(w => w.Name == "Around panel") || !widgets.Any(w => w.Name == "Falloff panel")) return;
            roundy.Design = DesignTemplates.Oval.Create();
        }

        /// <summary>Schema 11: Roady's panels became Liquid glass, as Roundy's are. Nothing else in it changes.</summary>
        private void GlassRoady()
        {
            DashboardSettings roady = Dashboards.FirstOrDefault(d => d.Id == DashboardSettings.RoadId);
            if (roady?.Design?.Screens == null) return;
            foreach (DashScreen screen in roady.Design.Screens)
                foreach (DashWidget widget in screen.Widgets ?? new List<DashWidget>())
                    if (widget.Type == "panel" && widget.Get("style") == "Soft") widget.Set("style", "Liquid");
        }

        /// <summary>
        /// Schema 5: the code-drawn dashboards are gone. Their track types move to the
        /// road and oval designs, which are added when the user has no design of that name.
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
            if (!Dashboards.Any(d => string.Equals(d.Name, DesignTemplates.Oval.Name, StringComparison.OrdinalIgnoreCase)))
            {
                foreach (DashboardSettings d in Dashboards)
                    d.RaceTypes?.RemoveAll(type => type == Disciplines.Oval || type == Disciplines.DirtOval);
                Ensure(DesignTemplates.Oval.Name, DashboardSettings.CreateOval).RaceTypes.AddRange(new[] { Disciplines.Oval, Disciplines.DirtOval });
            }

            foreach (string type in orphanTypes.Where(type => !Dashboards.Any(d => d.RaceTypes != null && d.RaceTypes.Contains(type))))
            {
                bool oval = type == Disciplines.Oval || type == Disciplines.DirtOval;
                DashboardSettings target = oval ? Ensure(DesignTemplates.Oval.Name, DashboardSettings.CreateOval) : Ensure(DesignTemplates.Road.Name, DashboardSettings.CreateRoad);
                target.RaceTypes.Add(type);
            }
            if (Dashboards.Count == 0) return;
            if (defaultRetired) DefaultDashboardId = (Dashboards.FirstOrDefault(d => d.Name == DesignTemplates.Road.Name) ?? Dashboards[0]).Id;
        }

        /// <summary>
        /// Schema 6: the fixed DV-5 and LB-10 settings become set-up devices with their
        /// LED groups, so they work exactly as before and can be renamed or re-mapped.
        /// </summary>
        private void DevicesFromFixedSettings()
        {
            if (General.Devices.Count > 0) return;
            General.Devices.Add(KnownDevices.Create(KnownDevices.Dv5, General.LedPort, KnownDevices.Dv5.LedCount, true));
            General.Devices.Add(KnownDevices.Create(KnownDevices.Lb10, General.BrowPort, Math.Max(1, General.BrowLedCount), General.BrowEnabled));
            General.LedPort = null;
            General.BrowPort = null;
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

        /// <summary>Puts a dashboard's lights and flag alerts back to their defaults.</summary>
        public void ResetDashboard(DashboardSettings dashboard)
        {
            DashboardSettings factory = dashboard.RaceTypes != null && (dashboard.RaceTypes.Contains(Disciplines.Oval) || dashboard.RaceTypes.Contains(Disciplines.DirtOval)) ? DashboardSettings.CreateOval() : DashboardSettings.CreateRoad();
            dashboard.Lights = factory.Lights;
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

        /// <summary>Schema 8: a dashboard still on the pre-layer light settings gets them as layers, once.</summary>
        private void ClassicLightsToLayers()
        {
            foreach (DashboardSettings dashboard in Dashboards)
                if (dashboard.Lights == null) dashboard.Lights = LightPresets.FromClassic(dashboard);
        }

        /// <summary>Schema 7: light layers named after one device ("Brow rev lights") take the generic name ("Light bar rev lights").</summary>
        private void RenameBrowLayers()
        {
            foreach (DashboardSettings dashboard in Dashboards)
                foreach (LightLayer layer in dashboard.Lights?.Layers ?? new List<LightLayer>())
                    if (layer.Name != null && layer.Name.StartsWith("Brow ", StringComparison.Ordinal)) layer.Name = "Light bar " + layer.Name.Substring(5);
        }
    }

    internal sealed class GeneralSettings
    {
        /// <summary>Every LED device that has been set up: the LEDs on a wheel screen, light bars, strips.</summary>
        public List<DeviceSettings> Devices { get; set; } = new List<DeviceSettings>();

        public int ScreenBrightness { get; set; } = 100;
        public string SpeedUnit { get; set; } = "MPH";
        public string FuelUnit { get; set; } = "Liters";
        /// <summary>"Celsius" or "Fahrenheit"; empty follows the speed unit (km/h gives Celsius), as before it could be chosen.</summary>
        public string TemperatureUnit { get; set; }
        /// <summary>The sidebar page the window opens on: Dashboards, Devices or Settings.</summary>
        public string StartPage { get; set; } = "Dashboards";
        /// <summary>Minimizing the window hides it to the notification area instead of the taskbar.</summary>
        public bool MinimizeToTray { get; set; }
        /// <summary>Asks GitHub for the latest release when the window opens. Off unless the user turns it on.</summary>
        public bool CheckForUpdates { get; set; }
        public bool AutoSwitchByDiscipline { get; set; } = true;
        public bool DiagnosticsEnabled { get; set; } = true;

        // Before schema 6 the DV-5 and LB-10 were fixed settings. They are read once to
        // build Devices and then cleared; nothing else uses them.
        public string LedPort { get; set; }
        public bool BrowEnabled { get; set; } = true;
        public string BrowPort { get; set; }
        public int BrowLedCount { get; set; } = 20;
    }

    internal sealed class DashboardSettings
    {
        public string Id { get; set; } = "dashboard-new";
        public string Name { get; set; } = "Dashboard";
        public string Layout { get; set; } = Layouts.Custom;
        public List<string> RaceTypes { get; set; } = new List<string>();
        public string AccentColor { get; set; } = "#22D3EE";
        // Before schema 8: the light settings from before light layers. Read once to build Lights, then cleared.
        public LedBankSettings CenterLeds { get; set; }
        public SideLedSettings LeftLeds { get; set; }
        public SideLedSettings RightLeds { get; set; }
        public BrowLedSettings BrowLeds { get; set; }
        public AlertSettings Alerts { get; set; } = new AlertSettings();
        /// <summary>The dashboard's design: screens of widgets, drawn by <see cref="LayoutRenderer"/>.</summary>
        public DashLayout Design { get; set; }
        /// <summary>The dashboard's lights: layers of effects on the LED devices.</summary>
        public LightProfile Lights { get; set; }

        /// <summary>A new dashboard started from a template.</summary>
        public static DashboardSettings CreateDesigned(DesignTemplate template)
        {
            DashboardSettings created = Create("dashboard-new", template.Name, template.Accent);
            created.Design = template.Create();
            created.Lights = template.OvalLights ? LightPresets.SpotterFocus() : LightPresets.Refined();
            return created;
        }

        public const string RoadId = "dashboard-roady", OvalId = "dashboard-roundy";

        /// <summary>The default road dashboard.</summary>
        public static DashboardSettings CreateRoad()
        {
            DashboardSettings halo = Create(RoadId, DesignTemplates.Road.Name, DesignTemplates.Road.Accent, Disciplines.Road, Disciplines.DirtRoad);
            halo.Design = DesignTemplates.Road.Create();
            halo.Lights = LightPresets.Refined();
            return halo;
        }

        /// <summary>The default oval dashboard.</summary>
        public static DashboardSettings CreateOval()
        {
            DashboardSettings oval = Create(OvalId, DesignTemplates.Oval.Name, DesignTemplates.Oval.Accent, Disciplines.Oval, Disciplines.DirtOval);
            oval.Design = DesignTemplates.Oval.Create();
            oval.Lights = LightPresets.SpotterFocus();
            return oval;
        }

        private static DashboardSettings Create(string id, string name, string accent, params string[] raceTypes) => new DashboardSettings
        {
            Id = id,
            Name = name,
            Layout = Layouts.Custom,
            AccentColor = accent,
            RaceTypes = raceTypes.ToList()
        };

        public void Normalize()
        {
            Layout = Layouts.Custom;
            if (string.IsNullOrWhiteSpace(Name)) Name = "Dashboard";
            if (RaceTypes == null) RaceTypes = new List<string>();
            if (Alerts == null) Alerts = new AlertSettings();
            if (Design == null) Design = DesignTemplates.Blank();
            Design.Normalize();
            if (Lights == null) Lights = LightPresets.Refined();
            Lights.Normalize();
            // The pre-layer light settings have been converted by now.
            CenterLeds = null;
            LeftLeds = RightLeds = null;
            BrowLeds = null;
        }

        [ScriptIgnore]
        public Color Accent => SettingsStore.ParseColor(AccentColor, Color.FromArgb(0, 155, 210));
    }

    // ------------------------------------------------------------------ before schema 8
    // The light settings from before light layers, kept only so old settings can be read and converted.

    internal static class LedFunctions
    {
        public const string BlindSpot = "Blind spot", Rpm = "RPM", Brake = "Brake pressure", Throttle = "Throttle", Disabled = "Disabled";
    }

    internal static class BrowFunctions
    {
        public const string Shift = "Shift lights", BlindSpot = "Blind spot", Flags = "Flags only", Pedals = "Brake and throttle",
            Brake = "Brake pressure", Throttle = "Throttle", Delta = "Lap delta", Fuel = "Fuel level", Off = "Off";
    }

    internal static class LedProfiles
    {
        public const string GreenYellowRed = "Green / Yellow / Red", BluePurpleRed = "Blue / Purple / Red", Single = "Single color";
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
    }

    internal sealed class SideLedSettings : LedBankSettings { }

    internal sealed class BrowLedSettings : LedBankSettings
    {
        /// <summary>When on, any racing flag took over the light bar.</summary>
        public bool FlagOverride { get; set; } = true;

        public static BrowLedSettings CreateDefault(string function) => new BrowLedSettings
        {
            Function = function, Pattern = "Edges to center", Brightness = 60, ColorProfile = LedProfiles.GreenYellowRed, FlagOverride = true
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
            if (TryLoad(out AppSettings loaded)) return loaded;
            // Unreadable even after retrying: keep the file (it may be fixable by hand) and start from defaults.
            try
            {
                if (File.Exists(FilePath)) File.Copy(FilePath, FilePath + ".unreadable-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), true);
            }
            catch { }
            var defaults = AppSettings.CreateDefault();
            try { if (!File.Exists(FilePath)) Save(defaults); } catch { }
            return defaults;
        }

        /// <summary>
        /// Reads settings.json, retrying for a moment: another process may be swapping in a new copy at that
        /// instant. True with defaults when there's no file yet; false when it exists but couldn't be read.
        /// </summary>
        public static bool TryLoad(out AppSettings settings)
        {
            settings = null;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    if (!File.Exists(FilePath))
                    {
                        settings = AppSettings.CreateDefault();
                        try { Save(settings); } catch { }
                        return true;
                    }
                    var loaded = Json.Deserialize<AppSettings>(File.ReadAllText(FilePath));
                    if (loaded == null) return false;
                    int schema = loaded.SchemaVersion;
                    loaded.EnsureBuiltInDashboards();
                    // Persist one-time upgrades so every process sees the same lineup.
                    if (loaded.SchemaVersion != schema) try { Save(loaded); } catch { }
                    settings = loaded;
                    return true;
                }
                catch (IOException) { System.Threading.Thread.Sleep(40); }
                catch (UnauthorizedAccessException) { System.Threading.Thread.Sleep(40); }
                catch (Exception) { return false; }
            }
            return false;
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
