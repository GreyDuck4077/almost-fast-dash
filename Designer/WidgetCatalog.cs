using System;
using System.Collections.Generic;
using System.Linq;

namespace AlmostFastOverlays
{
    internal enum PropKind
    {
        Text,
        Number,
        Color,
        Bool,
        Choice,
        Font,
        Image,
        /// <summary>Always a formula, such as the value a gauge shows or when a widget is visible.</summary>
        Formula
    }

    internal sealed class PropDef
    {
        public string Key, Label, Default, Group, Help;
        public PropKind Kind;
        public string[] Choices;
        public double Min = double.NaN, Max = double.NaN, Step = 1;
    }

    internal sealed class WidgetType
    {
        public string Id, Name, Category, Icon, Description;
        public double DefaultW, DefaultH;
        public readonly List<PropDef> Props = new List<PropDef>();
        public Action<WidgetContext> Draw;
        /// <summary>Redraw on every pedal or delta trace sample.</summary>
        public bool UsesTraces;

        public PropDef Prop(string key) => Props.FirstOrDefault(p => p.Key == key);
    }

    /// <summary>
    /// Every widget the designer offers. A widget type is a list of editable
    /// properties (which also drive the inspector) plus a painter.
    /// </summary>
    internal static class WidgetCatalog
    {
        public static readonly List<WidgetType> All = new List<WidgetType>();
        public static readonly string[] Categories = { "Basics", "Values", "Gauges", "Racing", "Graphs", "Decor" };

        private static readonly string[] Aligns = { "Left", "Center", "Right" };
        private static readonly string[] VAligns = { "Top", "Middle", "Bottom" };
        private static readonly string[] Weights = { "Bold", "Regular" };

        static WidgetCatalog()
        {
            // ------------------------------------------------------------ Basics
            Define("panel", "Panel", "Basics", "", "A card behind other widgets: soft, glass, tinted or outlined, with optional glow and accent edge.", 220, 120, WidgetPainters.Panel)
                .Choice("style", "Style", "Liquid", "Look", "Liquid", "Soft", "Glass", "Solid", "Tinted", "Outline", "None")
                .Choice("corners", "Corners", "Round", "Look", "Round", "Cut", "Cut diagonal")
                .Number("radius", "Corner size", "10", "Look", 0, 60)
                .Color("fill", "Fill", "panel", "Look")
                .Color("fill2", "Top fill (Soft)", "paneltop", "Look")
                .Color("tint", "Tint (Tinted)", "accent", "Look")
                .Color("border", "Border", "border", "Border")
                .Number("borderWidth", "Border width", "1.5", "Border", 0, 8, 0.5)
                .Choice("edge", "Accent edge", "None", "Border", "None", "Left", "Top", "Bottom", "Right")
                .Color("edgeColor", "Edge color", "accent", "Border")
                .Number("glow", "Glow", "0", "Glow", 0, 40)
                .Color("glowColor", "Glow color", "accent", "Glow")
                .Text("title", "Title", "", "Title")
                .Color("titleColor", "Title color", "muted", "Title")
                .Number("titleSize", "Title size", "13", "Title", 6, 40);

            Define("text", "Text", "Basics", "", "Any text, fixed or from a formula, with auto-fit, glow and shadow.", 160, 40, WidgetPainters.Text)
                .Text("text", "Text", "TEXT", "Content")
                .Choice("case", "Letter case", "As typed", "Content", "As typed", "UPPER")
                .Font()
                .Number("size", "Size", "28", "Type", 4, 300)
                .Choice("weight", "Weight", "Bold", "Type", Weights)
                .Number("tracking", "Letter spacing", "0", "Type", 0, 20, 0.5)
                .Color("color", "Color", "text", "Type")
                .Choice("align", "Align", "Center", "Type", Aligns)
                .Choice("valign", "Vertical align", "Middle", "Type", VAligns)
                .Bool("fit", "Shrink to fit", "true", "Type")
                .Number("glow", "Glow", "0", "Effects", 0, 30)
                .Color("glowColor", "Glow color", "", "Effects", "Empty uses the text color.")
                .Bool("shadow", "Drop shadow", "false", "Effects");

            Define("image", "Image", "Basics", "", "A PNG or JPG embedded in the design, such as a team logo.", 120, 120, WidgetPainters.Image)
                .Image()
                .Choice("fit", "Fit", "Contain", "Image", "Contain", "Cover", "Stretch");

            // ------------------------------------------------------------ Values
            Define("metric", "Value", "Values", "", "A label with a live value and unit: the building block of most dashboards.", 160, 70, WidgetPainters.Metric)
                .Text("label", "Label", "SPEED", "Content")
                .Text("value", "Value", "=format(Speed, \"0\")", "Content")
                .Text("unit", "Unit", "", "Content")
                .Choice("layout", "Layout", "Label above", "Layout", "Label above", "Label below", "Label left", "Value only")
                .Choice("align", "Align", "Left", "Layout", Aligns)
                .Number("inset", "Inner padding", "14", "Layout", 0, 60)
                .Font()
                .Number("valueSize", "Value size", "32", "Type", 4, 300)
                .Number("labelSize", "Label size", "13", "Type", 4, 80)
                .Number("unitSize", "Unit size (0 auto)", "0", "Type", 0, 120)
                .Number("labelTracking", "Label letter spacing", "0", "Type", 0, 20, 0.5)
                .Color("valueColor", "Value color", "text", "Colors")
                .Color("labelColor", "Label color", "muted", "Colors")
                .Color("unitColor", "Unit color", "muted", "Colors")
                .Number("glow", "Value glow", "0", "Effects", 0, 30);

            Define("gear", "Gear", "Values", "", "The big gear readout with shift flash, neighbours and a rolling change animation.", 180, 170, WidgetPainters.Gear)
                .Formula("value", "Gear", "=Gear", "Content")
                .Font()
                .Number("size", "Size", "150", "Type", 10, 400)
                .Color("color", "Color", "text", "Type")
                .Formula("flash", "Flash when", "=ShiftFlash", "Shift")
                .Color("flashColor", "Flash color", "bad", "Shift")
                .Bool("neighbors", "Show next and previous gear", "true", "Neighbours")
                .Color("neighborColor", "Neighbour color", "dim", "Neighbours")
                .Number("neighborSize", "Neighbour size", "30", "Neighbours", 4, 120)
                .Bool("animate", "Animate gear changes", "true", "Effects")
                .Number("glow", "Glow", "0", "Effects", 0, 40)
                .Color("glowColor", "Glow color", "", "Effects", "Empty uses the gear color.");

            // ------------------------------------------------------------ Gauges
            Define("bar", "Bar", "Gauges", "", "A horizontal or vertical bar: fill, centered (for delta) or segmented.", 220, 14, WidgetPainters.Bar)
                .Formula("value", "Value", "=Throttle", "Value")
                .Number("min", "Minimum", "0", "Value", double.NaN, double.NaN, 0.1)
                .Number("max", "Maximum", "1", "Value", double.NaN, double.NaN, 0.1)
                .Choice("mode", "Mode", "Fill", "Look", "Fill", "Center", "Segments")
                .Choice("orientation", "Direction", "Horizontal", "Look", "Horizontal", "Vertical")
                .Bool("reverse", "Fill from the other end", "false", "Look")
                .Number("segments", "Segments", "20", "Look", 2, 80)
                .Number("radius", "Corner radius (-1 round)", "-1", "Look", -1, 40)
                .Color("color", "Color", "accent", "Colors")
                .Color("negColor", "Negative color (Center)", "bad", "Colors")
                .Color("track", "Track", "track", "Colors")
                .Formula("marker", "Marker at", "", "Marker", "Optional: draws a tick at this value, such as laps to go.")
                .Color("markerColor", "Marker color", "text", "Marker")
                .Number("glow", "Glow", "0", "Effects", 0, 30);

            Define("arc", "Arc gauge", "Gauges", "", "A round gauge: smooth or segmented, with shift colors, glow and flash.", 260, 260, WidgetPainters.Arc)
                .Formula("value", "Value", "=ShiftPct", "Value")
                .Number("min", "Minimum", "0", "Value", double.NaN, double.NaN, 0.1)
                .Number("max", "Maximum", "1", "Value", double.NaN, double.NaN, 0.1)
                .Choice("style", "Style", "Segmented", "Look", "Segmented", "Smooth")
                .Number("start", "Start angle", "135", "Look", -360, 360, 5)
                .Number("sweep", "Sweep", "270", "Look", -360, 360, 5)
                .Number("thickness", "Thickness", "14", "Look", 1, 80)
                .Number("segments", "Segments", "24", "Look", 2, 90)
                .Choice("colorMode", "Colors", "Shift colors", "Colors", "Shift colors", "Solid")
                .Color("color", "Color", "accent", "Colors")
                .Color("warnColor", "Warning color", "warn", "Colors")
                .Color("dangerColor", "Danger color", "bad", "Colors")
                .Number("warnAt", "Warning from", "0.66", "Colors", 0, 1, 0.01)
                .Number("dangerAt", "Danger from", "0.86", "Colors", 0, 1, 0.01)
                .Color("track", "Track", "track", "Colors")
                .Formula("flash", "Flash when", "=ShiftFlash", "Flash")
                .Color("flashColor", "Flash color", "bad", "Flash")
                .Number("glow", "Glow", "9", "Effects", 0, 40);

            Define("shiftlights", "Shift lights", "Gauges", "", "An on-screen LED strip that fills with revs and flashes at the shift point.", 360, 28, WidgetPainters.ShiftLights)
                .Formula("value", "Value", "=ShiftPct", "Value")
                .Number("count", "Lights", "10", "Look", 2, 40)
                .Choice("shape", "Shape", "Dot", "Look", "Dot", "Pill", "Bar")
                .Choice("pattern", "Fill pattern", "Left to right", "Look", "Left to right", "Edges to center", "Center out")
                .Color("lowColor", "Low", "good", "Colors")
                .Color("midColor", "Middle", "warn", "Colors")
                .Color("highColor", "High", "bad", "Colors")
                .Color("offColor", "Off", "track", "Colors")
                .Formula("flash", "Flash when", "=ShiftFlash", "Flash")
                .Color("flashColor", "Flash color", "info", "Flash")
                .Number("glow", "Glow", "10", "Effects", 0, 40);

            // ------------------------------------------------------------ Racing
            Define("relative", "Relative", "Racing", "", "Cars around you on track with position, number, license and gap.", 268, 300, WidgetPainters.Relative)
                .Choice("rows", "Rows", "5", "Content", "3", "5")
                .Bool("showPosition", "Position", "true", "Content")
                .Bool("showNumber", "Car number", "true", "Content")
                .Bool("showLicense", "License and iRating", "true", "Content")
                .Font()
                .Number("size", "Name size", "16", "Type", 6, 60)
                .Color("highlight", "Your row", "accent", "Colors")
                .Color("aheadColor", "Gap ahead", "good", "Colors")
                .Color("behindColor", "Gap behind", "warn", "Colors")
                .Color("lappingColor", "Lapping you", "#FF7864", "Colors")
                .Color("lappedColor", "Lapped", "info", "Colors");

            Define("blindspot", "Blind spot", "Racing", "", "Lights up while a car is alongside, brighter toward where it is.", 26, 220, WidgetPainters.BlindSpot)
                .Choice("side", "Side", "Left", "Content", "Left", "Right")
                .Choice("shape", "Shape", "Bar", "Look", "Bar", "Zones")
                .Color("color", "Color", "bad", "Look")
                .Color("offColor", "Off color", "track", "Look")
                .Number("glow", "Glow", "14", "Effects", 0, 40);

            Define("flag", "Flag", "Racing", "", "Shows the current flag inside its own area. Use it when full-screen flags are off.", 260, 90, WidgetPainters.Flag)
                .Choice("style", "Style", "Fill", "Look", "Fill", "Badge")
                .Bool("label", "Flag name", "true", "Look")
                .Number("radius", "Corner radius", "10", "Look", 0, 60)
                .Font();

            Define("progress", "Session progress", "Racing", "", "Lap blocks in lap races (caution laps in yellow) or a time bar in timed sessions.", 838, 8, WidgetPainters.Progress)
                .Color("color", "Color", "accent", "Colors")
                .Color("cautionColor", "Caution laps", "warn", "Colors")
                .Color("track", "Track", "track", "Colors")
                .Number("gap", "Gap between laps", "2", "Look", 0, 10);

            // ------------------------------------------------------------ Graphs
            Define("trace", "Pedal trace", "Graphs", "", "Scrolling throttle and brake history, about six seconds long.", 300, 90, WidgetPainters.Trace)
                .Color("throttleColor", "Throttle", "good", "Colors")
                .Color("brakeColor", "Brake", "bad", "Colors")
                .Number("lineWidth", "Line width", "2.5", "Look", 0.5, 10, 0.5)
                .Bool("fill", "Fill under lines", "true", "Look")
                .Bool("grid", "Grid lines", "true", "Look")
                .Uses(traces: true);

            Define("deltatrace", "Delta trace", "Graphs", "", "Scrolling history of your lap delta: green above the line when faster.", 300, 90, WidgetPainters.DeltaTrace)
                .Number("range", "Range (seconds)", "1", "Look", 0.1, 10, 0.1)
                .Color("goodColor", "Faster", "good", "Colors")
                .Color("badColor", "Slower", "bad", "Colors")
                .Number("lineWidth", "Line width", "2.5", "Look", 0.5, 10, 0.5)
                .Bool("fill", "Fill", "true", "Look")
                .Uses(traces: true);

            Define("laphistory", "Lap history", "Graphs", "", "Your last five laps as bars, best lap in purple.", 260, 120, WidgetPainters.LapHistory)
                .Color("color", "Bars", "accent", "Colors")
                .Color("bestColor", "Best lap", "purple", "Colors")
                .Color("textColor", "Times", "text", "Colors")
                .Bool("times", "Show times", "true", "Look")
                .Font();

            // ------------------------------------------------------------ Decor
            Define("shape", "Shape", "Decor", "", "Rectangles, circles, lines, brackets and chevrons for structure and accents.", 120, 80, WidgetPainters.Shape)
                .Choice("shape", "Shape", "Rectangle", "Shape", "Rectangle", "Ellipse", "Line", "Diagonal down", "Diagonal up", "Brackets", "Chevron", "Triangle")
                .Color("fill", "Fill", "", "Colors", "Empty for no fill.")
                .Color("stroke", "Line color", "accent", "Colors")
                .Number("strokeWidth", "Line width", "2", "Look", 0, 30, 0.5)
                .Number("radius", "Corner radius", "6", "Look", 0, 80)
                .Number("glow", "Glow", "0", "Effects", 0, 40);

            Define("gradient", "Gradient", "Decor", "", "A soft color wash or light bloom to give a layout depth.", 300, 200, WidgetPainters.Gradient)
                .Choice("style", "Style", "Radial", "Look", "Radial", "Vertical", "Horizontal")
                .Color("color", "Color", "accent", "Look")
                .Color("color2", "Fade to", "", "Look", "Empty fades to transparent.")
                .Number("strength", "Strength", "25", "Look", 0, 100);

            foreach (WidgetType type in All)
            {
                type.Props.Add(new PropDef { Key = "visible", Label = "Show when", Kind = PropKind.Formula, Default = "", Group = "Visibility", Help = "A condition such as OnPitRoad or Fuel < 5. Empty means always." });
                type.Props.Add(new PropDef { Key = "opacity", Label = "Opacity %", Kind = PropKind.Number, Default = "100", Group = "Visibility", Min = 0, Max = 100 });
            }
        }

        public static WidgetType Find(string id) => All.FirstOrDefault(t => t.Id == id);

        /// <summary>A new widget of a type with its default size, named after the type.</summary>
        public static DashWidget Create(string typeId, double x, double y)
        {
            WidgetType type = Find(typeId) ?? All[0];
            return new DashWidget { Type = type.Id, Name = type.Name, X = Math.Round(x), Y = Math.Round(y), W = type.DefaultW, H = type.DefaultH };
        }

        // ------------------------------------------------------------------ fluent definition

        private static WidgetType Define(string id, string name, string category, string icon, string description, double w, double h, Action<WidgetContext> draw)
        {
            var type = new WidgetType { Id = id, Name = name, Category = category, Icon = icon, Description = description, DefaultW = w, DefaultH = h, Draw = draw };
            All.Add(type);
            return type;
        }

        private static WidgetType Add(this WidgetType t, PropDef p) { t.Props.Add(p); return t; }

        private static WidgetType Text(this WidgetType t, string key, string label, string def, string group, string help = null) =>
            t.Add(new PropDef { Key = key, Label = label, Default = def, Group = group, Kind = PropKind.Text, Help = help });

        private static WidgetType Formula(this WidgetType t, string key, string label, string def, string group, string help = null) =>
            t.Add(new PropDef { Key = key, Label = label, Default = def, Group = group, Kind = PropKind.Formula, Help = help });

        private static WidgetType Number(this WidgetType t, string key, string label, string def, string group, double min = double.NaN, double max = double.NaN, double step = 1) =>
            t.Add(new PropDef { Key = key, Label = label, Default = def, Group = group, Kind = PropKind.Number, Min = min, Max = max, Step = step });

        private static WidgetType Color(this WidgetType t, string key, string label, string def, string group, string help = null) =>
            t.Add(new PropDef { Key = key, Label = label, Default = def, Group = group, Kind = PropKind.Color, Help = help });

        private static WidgetType Bool(this WidgetType t, string key, string label, string def, string group) =>
            t.Add(new PropDef { Key = key, Label = label, Default = def, Group = group, Kind = PropKind.Bool });

        private static WidgetType Choice(this WidgetType t, string key, string label, string def, string group, params string[] choices) =>
            t.Add(new PropDef { Key = key, Label = label, Default = def, Group = group, Kind = PropKind.Choice, Choices = choices });

        private static WidgetType Font(this WidgetType t) =>
            t.Add(new PropDef { Key = "font", Label = "Font", Default = "theme", Group = "Type", Kind = PropKind.Font, Help = "\"theme\" uses the design's font." });

        private static WidgetType Image(this WidgetType t) =>
            t.Add(new PropDef { Key = "image", Label = "Image", Default = "", Group = "Image", Kind = PropKind.Image });

        private static WidgetType Uses(this WidgetType t, bool traces) { t.UsesTraces = traces; return t; }
    }
}
