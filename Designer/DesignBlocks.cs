using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AlmostFastOverlays
{
    /// <summary>
    /// A ready-made group of widgets (a card, a cluster, a strip) dropped in as one,
    /// so a premium layout is a handful of drags instead of dozens of widgets.
    /// </summary>
    internal sealed class DesignBlock
    {
        public string Name, Description, Icon;
        /// <summary>The folder it is listed under in the designer's Add panel.</summary>
        public string Folder = "Other";
        public bool UserMade;
        public string FilePath;
        public Func<List<DashWidget>> Create;
    }

    internal static class DesignBlocks
    {
        public static readonly string Folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Blocks");

        private static DashWidget W(string type, string name, double x, double y, double w, double h, params string[] props)
        {
            var widget = new DashWidget { Type = type, Name = name, X = x, Y = y, W = w, H = h };
            for (int i = 0; i + 1 < props.Length; i += 2) widget.Set(props[i], props[i + 1]);
            return widget;
        }

        public static IEnumerable<DesignBlock> BuiltIn()
        {
            yield return new DesignBlock
            {
                Folder = "Cards", Name = "Stat card", Icon = "", Description = "A soft panel with a labelled live value.",
                Create = () => new List<DashWidget>
                {
                    W("panel", "Card", 0, 0, 170, 74),
                    W("metric", "Value", 0, 0, 170, 74, "label", "SPEED", "value", "=format(Speed, \"0\")", "unit", "=SpeedUnit", "valueSize", "32")
                }
            };
            yield return new DesignBlock
            {
                Folder = "Cards", Name = "Glass card", Icon = "", Description = "Frosted glass with a soft accent glow and a big value.",
                Create = () => new List<DashWidget>
                {
                    W("panel", "Glass", 0, 0, 200, 90, "style", "Glass", "radius", "16", "glow", "10", "glowColor", "=alpha($accent, 0.35)"),
                    W("metric", "Value", 0, 4, 200, 82, "label", "LAST LAP", "value", "=laptime(LastLap)", "valueSize", "32", "inset", "18", "valueColor", "=LastLapIsBest ? $purple : $text")
                }
            };
            yield return new DesignBlock
            {
                Folder = "Rev gauges", Name = "Gear cluster", Icon = "", Description = "Segmented rev arc around a big animated gear, with speed and RPM.",
                Create = () => new List<DashWidget>
                {
                    W("arc", "Rev arc", 0, 0, 300, 300, "start", "135", "sweep", "270"),
                    W("gear", "Gear", 60, 68, 180, 170),
                    W("text", "Speed", 70, 228, 160, 40, "text", "=format(Speed, \"0\")", "size", "34"),
                    W("text", "Speed unit", 90, 262, 120, 16, "text", "=SpeedUnit", "size", "12", "weight", "Regular", "color", "muted")
                }
            };
            yield return new DesignBlock
            {
                Folder = "Rev gauges", Name = "Tach dial", Icon = "", Description = "A needle tachometer with numbered thousands, a red zone and the RPM underneath.",
                Create = () =>
                {
                    const string top = "=Redline > 0 ? ceil(Redline / 1000) : 8";
                    return new List<DashWidget>
                    {
                        W("arc", "Tach", 0, 0, 300, 300, "value", "=Rpm / 1000", "max", top, "style", "Smooth", "thickness", "10", "zones", "true", "warnAt", "0.75", "dangerAt", "0.9",
                            "ticks", top, "numbers", "true", "numbersTo", top, "needle", "Needle", "glow", "6"),
                        W("text", "RPM", 90, 196, 120, 30, "text", "=format(Rpm, \"0\")", "size", "22"),
                        W("text", "RPM unit", 90, 224, 120, 16, "text", "x1000 RPM", "size", "12", "weight", "Regular", "color", "muted")
                    };
                }
            };
            yield return new DesignBlock
            {
                Folder = "Rev gauges", Name = "Tapered rev ring", Icon = "", Description = "A smooth rev ring that grows thicker toward the redline, around a big gear.",
                Create = () => new List<DashWidget>
                {
                    W("arc", "Rev ring", 0, 0, 300, 300, "value", "=RevPct", "style", "Smooth", "thickness", "28", "taper", "85", "start", "150", "sweep", "240", "colorMode", "Gradient", "glow", "8"),
                    W("gear", "Gear", 60, 68, 180, 170)
                }
            };
            yield return new DesignBlock
            {
                Folder = "Rev gauges", Name = "Swoosh rev bar", Icon = "", Description = "A rev bar that sweeps up and widens toward the shift point.",
                Create = () => new List<DashWidget>
                {
                    W("bar", "Swoosh rev bar", 0, 0, 440, 120, "value", "=RevPct", "bend", "70", "peak", "100", "taper", "85", "thickness", "45", "radius", "3",
                        "colorMode", "Gradient", "flash", "=ShiftFlash", "glow", "8")
                }
            };
            yield return new DesignBlock
            {
                Folder = "Rev gauges", Name = "Arched rev bar", Icon = "", Description = "Segmented revs with a flat base and an arched top, blending to red.",
                Create = () => new List<DashWidget>
                {
                    W("bar", "Arched rev bar", 0, 0, 600, 60, "value", "=RevPct", "mode", "Segments", "segments", "30", "bulge", "100", "grow", "Bottom edge", "radius", "2",
                        "colorMode", "Gradient", "flash", "=ShiftFlash")
                }
            };
            yield return new DesignBlock
            {
                Folder = "Rev gauges", Name = "Slanted rev bar", Icon = "", Description = "Angled segments in shift colors, like a race display.",
                Create = () => new List<DashWidget>
                {
                    W("bar", "Slanted rev bar", 0, 0, 560, 36, "value", "=RevPct", "mode", "Segments", "segments", "24", "skew", "25", "radius", "0",
                        "colorMode", "Shift colors", "flash", "=ShiftFlash", "glow", "6")
                }
            };
            yield return new DesignBlock
            {
                Folder = "Rev gauges", Name = "Mirrored rev bars", Icon = "", Description = "Two sweeping rev bars that fill toward each other and meet in the middle.",
                Create = () =>
                {
                    DashWidget Half(string name, double x, bool flip) =>
                        W("bar", name, x, 0, 400, 90, "value", "=RevPct", "bend", "50", "peak", "100", "taper", "85", "thickness", "45", "radius", "3",
                            "colorMode", "Gradient", "flash", "=ShiftFlash", "glow", "8", "flipX", flip ? "true" : "false");
                    return new List<DashWidget> { Half("Revs left", 0, false), Half("Revs right", 412, true) };
                }
            };
            yield return new DesignBlock
            {
                Folder = "Cards", Name = "Delta card", Icon = "", Description = "Delta to best that tints green or red, with a centered bar.",
                Create = () => new List<DashWidget>
                {
                    W("panel", "Delta panel", 0, 0, 446, 74, "style", "Tinted", "tint", "=Delta > 0.005 ? $good : Delta < -0.005 ? $bad : $accent"),
                    W("text", "Delta label", 16, 6, 80, 18, "text", "DELTA", "size", "13", "weight", "Regular", "color", "muted", "align", "Left"),
                    W("text", "Delta", 0, 2, 446, 46, "text", "=signed(Delta, 2)", "size", "36", "color", "=deltacolor(Delta)"),
                    W("bar", "Delta bar", 20, 54, 406, 8, "value", "=Delta", "min", "-1", "max", "1", "mode", "Center", "color", "good", "negColor", "bad")
                }
            };
            yield return new DesignBlock
            {
                Folder = "Cards", Name = "Fuel card", Icon = "", Description = "Fuel, laps of fuel, a range bar with the finish marked, and refuel.",
                Create = () =>
                {
                    const string scale = "max(1, max(FuelLapsLeft, LapsRemaining) * 1.05)";
                    return new List<DashWidget>
                    {
                        W("panel", "Fuel panel", 0, 0, 268, 170),
                        W("metric", "Fuel", 0, 2, 134, 62, "label", "=\"FUEL \" + FuelUnit", "value", "=fuel(Fuel)", "valueSize", "30"),
                        W("metric", "Fuel laps", 134, 2, 134, 62, "label", "EST LAPS", "value", "=FuelLapsLeft < 0 ? \"--.-\" : format(FuelLapsLeft, \"0.0\")", "align", "Right", "valueSize", "30",
                            "valueColor", "=steps(FuelLapsLeft, $text, 0, $bad, 2, $warn, 5, $text)"),
                        W("bar", "Fuel range", 14, 74, 240, 8, "value", "=FuelLapsLeft > 0 ? FuelLapsLeft / " + scale + " : FuelPct",
                            "marker", "=LapsRemaining > 0 && FuelLapsLeft > 0 ? LapsRemaining / " + scale + " : 0", "color", "=FuelLapsLeft >= 0 && FuelLapsLeft < LapsRemaining ? $warn : $accent"),
                        W("metric", "Fuel per lap", 0, 94, 134, 70, "label", "AVG / LAP", "value", "=fuel(FuelPerLap, true)", "valueSize", "26"),
                        W("metric", "Refuel", 134, 94, 134, 70, "label", "REFUEL", "value", "=FuelToFinish < 0 ? \"--.-\" : FuelToFinish <= 0.05 ? \"NONE\" : \"+\" + fuel(FuelToFinish)",
                            "align", "Right", "valueSize", "26", "valueColor", "=FuelToFinish < 0 ? $text : FuelToFinish <= 0.05 ? $good : $warn")
                    };
                }
            };
            yield return new DesignBlock
            {
                Folder = "Cards", Name = "Lap times", Icon = "", Description = "Last, best and predicted laps, color coded.",
                Create = () => new List<DashWidget>
                {
                    W("panel", "Lap times panel", 0, 0, 268, 150),
                    W("metric", "Last lap", 0, 4, 268, 47, "label", "LAST", "value", "=laptime(LastLap)", "layout", "Label left", "valueSize", "25", "valueColor", "=LastLapIsBest ? $purple : $text"),
                    W("shape", "Divider", 14, 50, 240, 2, "shape", "Line", "stroke", "border", "strokeWidth", "1"),
                    W("metric", "Best lap", 0, 51, 268, 47, "label", "BEST", "value", "=laptime(BestLap)", "layout", "Label left", "valueSize", "25", "valueColor", "purple"),
                    W("shape", "Divider", 14, 97, 240, 2, "shape", "Line", "stroke", "border", "strokeWidth", "1"),
                    W("metric", "Predicted lap", 0, 98, 268, 47, "label", "PREDICTED", "value", "=laptime(PredictedLap)", "layout", "Label left", "valueSize", "25",
                        "valueColor", "=PredictedLap > 0 && BestLap > 0 ? (PredictedLap <= BestLap ? $good : $warn) : $text")
                }
            };
            yield return new DesignBlock
            {
                Folder = "Cards", Name = "Relative card", Icon = "", Description = "A titled panel with the five-car relative.",
                Create = () => new List<DashWidget>
                {
                    W("panel", "Relative panel", 0, 0, 268, 326, "title", "RELATIVE"),
                    W("relative", "Relative", 6, 28, 256, 294)
                }
            };
            yield return new DesignBlock
            {
                Folder = "Cards", Name = "Car settings strip", Icon = "", Description = "TC, ABS, brake bias and incidents in color-coded cells.",
                Create = () =>
                {
                    string[] labels = { "TC", "ABS", "BIAS", "INC" }, tones = { "info", "warn", "bad", "muted" };
                    string[] values = { "=TractionControl < 0 ? \"--\" : format(TractionControl, \"0\")", "=Abs < 0 ? \"--\" : format(Abs, \"0\")", "=BrakeBias > 0 ? format(BrakeBias, \"0.0\") : \"--\"", "=Incidents + \"x\"" };
                    var list = new List<DashWidget>();
                    for (int i = 0; i < 4; i++)
                    {
                        list.Add(W("panel", labels[i] + " panel", i * 94, 0, 88, 50));
                        list.Add(W("metric", labels[i], i * 94, 0, 88, 50, "label", labels[i], "value", values[i], "align", "Center", "valueSize", "24", "labelSize", "12", "labelColor", tones[i]));
                    }
                    return list;
                }
            };
            yield return new DesignBlock
            {
                Folder = "Rev gauges", Name = "LED strip", Icon = "", Description = "Glowing on-screen shift lights that fill from the edges and flash at the shift point.",
                Create = () => new List<DashWidget> { W("shiftlights", "LED strip", 0, 0, 520, 26, "count", "15", "shape", "Pill", "pattern", "Edges to center", "glow", "12") }
            };
            yield return new DesignBlock
            {
                Folder = "Warnings and spotter", Name = "Spotter bars", Icon = "", Description = "Blind-spot bars down both edges of the screen.",
                Create = () => new List<DashWidget>
                {
                    W("blindspot", "Car left", 0, 0, 10, 300, "side", "Left"),
                    W("blindspot", "Car right", 834, 0, 10, 300, "side", "Right")
                }
            };
            yield return new DesignBlock
            {
                Folder = "Warnings and spotter", Name = "Low fuel warning", Icon = "", Description = "A blinking banner when fewer than two laps of fuel remain.",
                Create = () => new List<DashWidget>
                {
                    W("panel", "Low fuel banner", 0, 0, 260, 44, "style", "Tinted", "tint", "bad", "radius", "22", "visible", "FuelLapsLeft >= 0 && FuelLapsLeft < 2 && blink(400)"),
                    W("text", "Low fuel", 0, 0, 260, 44, "text", "BOX THIS LAP · LOW FUEL", "size", "17", "color", "bad", "visible", "FuelLapsLeft >= 0 && FuelLapsLeft < 2 && blink(400)")
                }
            };
            yield return new DesignBlock
            {
                Folder = "Warnings and spotter", Name = "Pit limiter banner", Icon = "", Description = "Shows on pit road: limiter state, colored by whether you are over the limit.",
                Create = () => new List<DashWidget>
                {
                    W("panel", "Pit banner", 0, 0, 300, 54, "style", "Tinted", "tint", "=OverPitLimit ? $bad : PitLimiter ? $good : $warn", "radius", "14", "visible", "OnPitRoad"),
                    W("text", "Pit state", 0, 0, 300, 54, "text", "=OverPitLimit ? \"SLOW DOWN\" : PitLimiter ? \"LIMITER ON\" : \"LIMITER OFF\"", "size", "24",
                        "color", "=OverPitLimit ? $bad : PitLimiter ? $good : $warn", "visible", "OnPitRoad")
                }
            };
            yield return new DesignBlock
            {
                Folder = "Cards", Name = "Session header", Icon = "", Description = "Session, laps to go and current lap.",
                Create = () => new List<DashWidget>
                {
                    W("panel", "Session panel", 0, 0, 228, 50),
                    W("metric", "Session", 0, 0, 228, 50, "label", "=SessionType + \"   \" + RemainingText", "value", "=\"LAP \" + LapText", "valueSize", "24")
                }
            };
        }

        public static IEnumerable<DesignBlock> UserMade()
        {
            if (!Directory.Exists(Folder)) yield break;
            foreach (string path in Directory.GetFiles(Folder, "*.afoblock").OrderBy(p => p))
            {
                string json;
                try { json = File.ReadAllText(path); }
                catch { continue; }
                string file = path;
                yield return new DesignBlock
                {
                    Name = Path.GetFileNameWithoutExtension(path), Icon = "", UserMade = true, FilePath = file, Description = "Your saved block.",
                    Create = () =>
                    {
                        var widgets = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<List<DashWidget>>(json);
                        foreach (DashWidget w in widgets) { w.Id = DashScreen.NewId("w"); w.Normalize(); }
                        return widgets;
                    }
                };
            }
        }

        public static void Save(string name, List<DashWidget> widgets)
        {
            Directory.CreateDirectory(Folder);
            double x = widgets.Min(w => w.X), y = widgets.Min(w => w.Y);
            List<DashWidget> copy = widgets.Select(SettingsStore.Clone).ToList();
            foreach (DashWidget w in copy) { w.X -= x; w.Y -= y; }
            File.WriteAllText(Path.Combine(Folder, DesignerWindow.SafeFileName(name) + ".afoblock"), SettingsStore.ToJson(copy));
        }
    }
}
