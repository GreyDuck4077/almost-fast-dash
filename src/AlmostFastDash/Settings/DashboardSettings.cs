using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Web.Script.Serialization;

namespace AlmostFastDash.Settings
{
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

        /// <summary>The screen size the dashboard is built for.</summary>
        public System.Drawing.Size ScreenSize() => Design != null
            ? new System.Drawing.Size(ScreenSizes.Clamp(Design.Width), ScreenSizes.Clamp(Design.Height))
            : new System.Drawing.Size(ScreenSizes.DefaultWidth, ScreenSizes.DefaultHeight);
        /// <summary>The dashboard's lights: layers of effects on the LED devices.</summary>
        public LightProfile Lights { get; set; }

        /// <summary>A new dashboard started from a template.</summary>
        public static DashboardSettings CreateDesigned(DesignTemplate template, int width = ScreenSizes.DefaultWidth,
            int height = ScreenSizes.DefaultHeight)
        {
            DashboardSettings created = Create("dashboard-new", template.Name, template.Accent);
            created.Design = template.Create();
            // Templates are drawn for 854×480 and scaled to the screen the dashboard is for.
            created.Design.ScaleTo(width, height);
            created.Lights = template.OvalLights ? LightPresets.SpotterFocus() : LightPresets.Refined();
            return created;
        }

        public const string RoadId = "dashboard-roady", OvalId = "dashboard-roundy";

        /// <summary>The default road dashboard.</summary>
        public static DashboardSettings CreateRoad()
        {
            DashboardSettings halo = Create(RoadId, DesignTemplates.Road.Name, DesignTemplates.Road.Accent, Disciplines.Road,
                Disciplines.DirtRoad);
            halo.Design = DesignTemplates.Road.Create();
            halo.Lights = LightPresets.Refined();
            return halo;
        }

        /// <summary>The default oval dashboard.</summary>
        public static DashboardSettings CreateOval()
        {
            DashboardSettings oval = Create(OvalId, DesignTemplates.Oval.Name, DesignTemplates.Oval.Accent, Disciplines.Oval,
                Disciplines.DirtOval);
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

    internal sealed class AlertSettings
    {
        public int BlinkCount { get; set; } = 6;
        public int BlinkMilliseconds { get; set; } = 150;
        public int HoldMilliseconds { get; set; } = 0;
    }
}
