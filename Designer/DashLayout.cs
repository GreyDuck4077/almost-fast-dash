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

        /// <summary>The screen the dashboard is built for, in pixels. Designs from before sizes were kept are 854×480.</summary>
        public int Width { get; set; } = ScreenSizes.DefaultWidth;
        public int Height { get; set; } = ScreenSizes.DefaultHeight;

        public LayoutTheme Theme { get; set; } = new LayoutTheme();

        /// <summary>When on, a new flag takes over the whole screen for its alert.</summary>
        public bool FullScreenFlags { get; set; } = true;

        /// <summary>Full-screen flags move like cloth in a soft breeze instead of standing still.</summary>
        public bool WavingFlags { get; set; } = true;

        /// <summary>
        /// While a flag stays out (a caution, the final lap, black, repairs, blue), a glow in its colour runs
        /// round the dashboard with a tab saying what it is.
        /// </summary>
        public bool FlagFrame { get; set; } = true;

        /// <summary>
        /// Screens are checked top to bottom; the first whose condition is true is shown.
        /// A screen with an empty condition always matches, so the last one is the fallback.
        /// </summary>
        public List<DashScreen> Screens { get; set; } = new List<DashScreen>();

        /// <summary>
        /// Wheel buttons ("device|number") that step to the next and previous page. Screens without a
        /// Show-when condition are the pages; screens with one still take over when it's true.
        /// </summary>
        public string PageButton { get; set; } = "";
        public string PageBackButton { get; set; } = "";

        /// <summary>Embedded PNG images by id, base64 encoded, so a design is one self-contained file.</summary>
        public Dictionary<string, string> Images { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Moves the design to another screen size, scaling everything on it to match. Positions and sizes
        /// follow the screen's width and height. Text, line widths, corners and glows follow the smaller of
        /// the two scales, so they stay in proportion when the shape of the screen changes.
        /// </summary>
        private const double MinReadableText = 10;
        private static readonly HashSet<string> TextSizes = new HashSet<string> { "size", "valueSize", "labelSize", "unitSize", "numberSize", "titleSize", "neighborSize" };

        public void ScaleTo(int width, int height)
        {
            width = ScreenSizes.Clamp(width);
            height = ScreenSizes.Clamp(height);
            if (width == Width && height == Height) return;
            ScaleWidgets(AllWidgets(), width / (double)Math.Max(1, Width), height / (double)Math.Max(1, Height));
            Width = width;
            Height = height;
        }

        /// <summary>Scales widgets' places and boxes by <paramref name="sx"/> and <paramref name="sy"/>, and their text, lines and glows by the smaller.</summary>
        public static void ScaleWidgets(IEnumerable<DashWidget> widgets, double sx, double sy)
        {
            double s = Math.Min(sx, sy);
            foreach (DashWidget widget in widgets)
            {
                widget.X = Math.Round(widget.X * sx);
                widget.Y = Math.Round(widget.Y * sy);
                widget.W = Math.Max(1, Math.Round(widget.W * sx));
                widget.H = Math.Max(1, Math.Round(widget.H * sy));
                foreach (string key in widget.Props.Keys.ToList())
                {
                    string raw = widget.Props[key];
                    if (!WidgetCatalog.IsPixelSize(widget.Type, key) || raw == null || raw.StartsWith("=")) continue;
                    if (!double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value)) continue;
                    // Negative values are settings such as "round" corners, not sizes.
                    if (value <= 0) continue;
                    double scaled = value * s;
                    // Text stays readable on a small screen: it doesn't shrink below 10 px (smaller text that was
                    // designed that way stays as it was). Shrink to fit still keeps it inside its box.
                    if (TextSizes.Contains(key)) scaled = Math.Max(scaled, Math.Min(value, MinReadableText));
                    widget.Props[key] = (scaled >= 10 ? Math.Round(scaled) : Math.Round(scaled, 1)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
            }
        }

        public void Normalize()
        {
            Width = ScreenSizes.Clamp(Width <= 0 ? ScreenSizes.DefaultWidth : Width);
            Height = ScreenSizes.Clamp(Height <= 0 ? ScreenSizes.DefaultHeight : Height);
            if (Theme == null) Theme = new LayoutTheme();
            Theme.Normalize();
            if (Screens == null) Screens = new List<DashScreen>();
            if (Screens.Count == 0) Screens.Add(new DashScreen { Name = "Main" });
            if (Images == null) Images = new Dictionary<string, string>();
            foreach (DashScreen screen in Screens) screen.Normalize();
            // Images no widget uses any more (removed, or replaced) would otherwise stay in the design for good.
            if (Images.Count > 0)
            {
                var used = new HashSet<string>(AllWidgets().SelectMany(w => w.Props.Values).Where(v => v != null && Images.ContainsKey(v)));
                foreach (string id in Images.Keys.Where(id => !used.Contains(id)).ToList()) Images.Remove(id);
            }
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
        /// <summary>Aurora, Bloom, Solid, Gradient, Vignette, or None: see-through, for a dashboard over the game.</summary>
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

        public static readonly string[] BackgroundStyles = { "Aurora", "Bloom", "Solid", "Gradient", "Vignette", "None" };

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
        /// <summary>
        /// The group this widget belongs to (empty when none), its name, and its number: formulas in the
        /// group read it as Slot, so one driver card can be copied for P1, P2, P3 by changing one number.
        /// Every widget in a group carries the same three values.
        /// </summary>
        public string Group { get; set; } = "";
        public string GroupName { get; set; } = "";
        public double Slot { get; set; }
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
            if (Group == null) Group = "";
            if (GroupName == null) GroupName = "";
            if (string.IsNullOrWhiteSpace(Type)) Type = "text";
            // Older designs: bars had a "fill from the other end" switch and shift lights their own
            // pattern names; both are now the shared fill direction.
            if (Props.TryGetValue("reverse", out string reverse))
            {
                Props.Remove("reverse");
                if (!Props.ContainsKey("fill") && reverse != null)
                    Props["fill"] = reverse.StartsWith("=") ? "=(" + reverse.Substring(1) + ") ? \"" + FillOrder.EndToStart + "\" : \"" + FillOrder.StartToEnd + "\""
                        : Expr.Bool(reverse) ? FillOrder.EndToStart : FillOrder.StartToEnd;
            }
            if (Props.TryGetValue("pattern", out string pattern) && Type == "shiftlights")
            {
                Props.Remove("pattern");
                if (!Props.ContainsKey("fill") && pattern != null) Props["fill"] = pattern.StartsWith("=") ? pattern : FillOrder.Normalize(pattern);
            }
            W = Math.Max(2, W);
            H = Math.Max(2, H);
        }

        public System.Drawing.RectangleF Bounds => new System.Drawing.RectangleF((float)X, (float)Y, (float)W, (float)H);
    }
}
