using System;
using System.Collections.Generic;
using System.Linq;

namespace AlmostFastOverlays
{
    /// <summary>
    /// A dashboard described as data: a theme plus one or more screens of widgets.
    /// Stored inside <see cref="DashboardSettings.Design"/> in settings.json and drawn by
    /// <see cref="LayoutRenderer"/>. Widget properties are strings: a plain value
    /// ("24", "#FF0000", "accent") or a live formula starting with "=" ("=Speed").
    /// </summary>
    internal sealed class DashLayout
    {
        public const int CurrentVersion = 1;

        public int Version { get; set; } = CurrentVersion;
        public LayoutTheme Theme { get; set; } = new LayoutTheme();

        /// <summary>When on, racing flags take over the whole screen like the built-in dashboards.</summary>
        public bool FullScreenFlags { get; set; } = true;

        /// <summary>Full-screen flags move like cloth in a soft breeze instead of standing still.</summary>
        public bool WavingFlags { get; set; } = true;

        /// <summary>
        /// Screens are checked top to bottom; the first whose condition is true is shown.
        /// A screen with an empty condition always matches, so the last one is the fallback.
        /// </summary>
        public List<DashScreen> Screens { get; set; } = new List<DashScreen>();

        /// <summary>Embedded PNG images by id, base64 encoded, so a design is one self-contained file.</summary>
        public Dictionary<string, string> Images { get; set; } = new Dictionary<string, string>();

        public void Normalize()
        {
            if (Theme == null) Theme = new LayoutTheme();
            Theme.Normalize();
            if (Screens == null) Screens = new List<DashScreen>();
            if (Screens.Count == 0) Screens.Add(new DashScreen { Name = "Main" });
            if (Images == null) Images = new Dictionary<string, string>();
            foreach (DashScreen screen in Screens) screen.Normalize();
        }

        public IEnumerable<DashWidget> AllWidgets() => Screens.SelectMany(s => s.Widgets);
    }

    internal sealed class LayoutTheme
    {
        /// <summary>
        /// The colour names as LEDs show them. The screen's red and green read pink and minty on an LED,
        /// so lights use a deeper red and a true green; warn and the rest match the screen.
        /// </summary>
        public static LayoutTheme ForLights() => new LayoutTheme { Bad = "#890711", Good = "#008000" };

        public string Background { get; set; } = "#05070A";
        public string Background2 { get; set; } = "#0E1622";
        /// <summary>Bloom, Solid, Gradient or Vignette.</summary>
        public string BackgroundStyle { get; set; } = "Bloom";
        public string Font { get; set; } = "Bahnschrift";
        public string Text { get; set; } = "#ECF1F7";
        public string Muted { get; set; } = "#808EA0";
        public string Dim { get; set; } = "#4E5A6A";
        public string Panel { get; set; } = "#0E131A";
        public string PanelTop { get; set; } = "#141B24";
        public string Border { get; set; } = "#202935";
        public string Track { get; set; } = "#1B232E";
        public string Good { get; set; } = "#2EDE7C";
        public string Bad { get; set; } = "#F43F4E";
        public string Warn { get; set; } = "#FFBE2E";
        public string Info { get; set; } = "#4C9EFF";
        public string Purple { get; set; } = "#BA80FF";

        public static readonly string[] BackgroundStyles = { "Aurora", "Bloom", "Solid", "Gradient", "Vignette" };

        /// <summary>Theme colors widgets can reference by name, so restyling a design is one change.</summary>
        public static readonly string[] Tokens = { "accent", "text", "muted", "dim", "panel", "paneltop", "border", "track", "good", "bad", "warn", "info", "purple", "background" };

        public string Token(string name, string accent)
        {
            switch ((name ?? "").ToLowerInvariant())
            {
                case "accent": return accent;
                case "text": return Text;
                case "muted": return Muted;
                case "dim": return Dim;
                case "panel": return Panel;
                case "paneltop": return PanelTop;
                case "border": return Border;
                case "track": return Track;
                case "good": return Good;
                case "bad": return Bad;
                case "warn": return Warn;
                case "info": return Info;
                case "purple": return Purple;
                case "background": return Background;
                default: return null;
            }
        }

        public void Normalize()
        {
            if (!BackgroundStyles.Contains(BackgroundStyle)) BackgroundStyle = "Bloom";
            if (string.IsNullOrWhiteSpace(Font)) Font = "Bahnschrift";
        }
    }

    internal sealed class DashScreen
    {
        public string Id { get; set; } = NewId("screen");
        public string Name { get; set; } = "Main";
        /// <summary>Formula that must be true for this screen to show, such as "OnPitRoad". Empty means always.</summary>
        public string Condition { get; set; } = "";
        public List<DashWidget> Widgets { get; set; } = new List<DashWidget>();

        public void Normalize()
        {
            if (string.IsNullOrWhiteSpace(Id)) Id = NewId("screen");
            if (string.IsNullOrWhiteSpace(Name)) Name = "Screen";
            if (Condition == null) Condition = "";
            if (Widgets == null) Widgets = new List<DashWidget>();
            Widgets.RemoveAll(w => w == null);
            foreach (DashWidget widget in Widgets) widget.Normalize();
        }

        internal static string NewId(string prefix) => prefix + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
    }

    internal sealed class DashWidget
    {
        public string Id { get; set; } = DashScreen.NewId("w");
        public string Type { get; set; } = "text";
        public string Name { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double W { get; set; } = 120;
        public double H { get; set; } = 40;
        /// <summary>Hidden in the designer and on the device; handy while building.</summary>
        public bool Hidden { get; set; }
        /// <summary>Locked widgets cannot be selected on the canvas.</summary>
        public bool Locked { get; set; }
        public Dictionary<string, string> Props { get; set; } = new Dictionary<string, string>();

        public string Get(string key, string fallback = null) =>
            Props != null && Props.TryGetValue(key, out string value) && value != null ? value : fallback;

        public void Set(string key, string value)
        {
            if (Props == null) Props = new Dictionary<string, string>();
            Props[key] = value;
        }

        public void Normalize()
        {
            if (string.IsNullOrWhiteSpace(Id)) Id = DashScreen.NewId("w");
            if (Props == null) Props = new Dictionary<string, string>();
            if (string.IsNullOrWhiteSpace(Type)) Type = "text";
            W = Math.Max(2, W);
            H = Math.Max(2, H);
        }

        public System.Drawing.RectangleF Bounds => new System.Drawing.RectangleF((float)X, (float)Y, (float)W, (float)H);
    }
}
