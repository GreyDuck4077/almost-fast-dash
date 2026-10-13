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
        /// <summary>The setting this one depends on, and when it applies (given that setting's value). Null: always.</summary>
        public string DependsOn;
        public Func<string, bool> AppliesWhen;

        /// <summary>Whether the setting matters for this widget as set up. A formula in charge could be anything, so it shows.</summary>
        public bool Applies(DashWidget widget, WidgetType type)
        {
            if (DependsOn == null || AppliesWhen == null) return true;
            string raw = widget.Get(DependsOn, type.Prop(DependsOn)?.Default) ?? "";
            return raw.StartsWith("=") || AppliesWhen(raw);
        }
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
                .Number("glassFrost", "Frost", "14", "Glass", 0, 40)
                .Number("glassDepth", "Rim depth", "12", "Glass", 2, 60)
                .Number("glassRefraction", "Refraction %", "60", "Glass", 0, 100)
                .Number("glassShine", "Shine %", "70", "Glass", 0, 100)
                .Number("glassDispersion", "Colour fringe %", "35", "Glass", 0, 100)
                .Number("glassLight", "Light from °", "235", "Glass", 0, 360, 5)
                .ShapeTools()
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
            Define("bar", "Bar", "Gauges", "", "A bar for any value: fill, centered (for delta) or segmented. Bend, taper, bulge and slant it into any rev-bar shape.", 220, 14, WidgetPainters.Bar)
                .Formula("value", "Value", "=Throttle", "Value")
                .Number("min", "Minimum", "0", "Value", double.NaN, double.NaN, 0.1)
                .Number("max", "Maximum", "1", "Value", double.NaN, double.NaN, 0.1)
                .Choice("mode", "Mode", "Fill", "Look", "Fill", "Center", "Segments")
                .Choice("orientation", "Direction", "Horizontal", "Look", "Horizontal", "Vertical")
                .Choice("fill", "Fill direction", FillOrder.StartToEnd, "Look", FillOrder.All)
                .Number("segments", "Segments", "20", "Look", 2, 80)
                .Number("radius", "Corner radius (-1 round)", "-1", "Look", -1, 40)
                .ShapeTools()
                .Choice("colorMode", "Colors", "Solid", "Colors", "Solid", "Shift colors", "Gradient")
                .Color("color", "Color", "accent", "Colors")
                .Color("warnColor", "Warning color", "warn", "Colors")
                .Color("dangerColor", "Danger color", "bad", "Colors")
                .Number("warnAt", "Warning from", "0.66", "Colors", 0, 1, 0.01)
                .Number("dangerAt", "Danger from", "0.86", "Colors", 0, 1, 0.01)
                .Color("negColor", "Negative color (Center)", "bad", "Colors")
                .Color("track", "Track", "track", "Colors")
                .Formula("marker", "Marker at", "", "Marker", "Optional: draws a tick at this value, such as laps to go.")
                .Color("markerColor", "Marker color", "text", "Marker")
                .Formula("flash", "Flash when", "", "Flash", "Optional: fills the whole bar in the flash color, such as ShiftFlash.")
                .Color("flashColor", "Flash color", "bad", "Flash")
                .Number("glow", "Glow", "0", "Effects", 0, 30);

            Define("arc", "Arc gauge", "Gauges", "", "A round or oval gauge: smooth or segmented, tapered, with a needle, ticks, numbers and shift colors.", 260, 260, WidgetPainters.Arc)
                .Formula("value", "Value", "=ShiftPct", "Value")
                .Number("min", "Minimum", "0", "Value", double.NaN, double.NaN, 0.1)
                .Number("max", "Maximum", "1", "Value", double.NaN, double.NaN, 0.1)
                .Choice("style", "Style", "Segmented", "Look", "Segmented", "Smooth", "Track only")
                .Number("start", "Start angle", "135", "Look", -360, 360, 5)
                .Number("sweep", "Sweep", "270", "Look", -360, 360, 5)
                .Number("thickness", "Thickness", "14", "Look", 1, 80)
                .Number("taper", "Taper %", "0", "Look", -100, 100)
                .Bool("oval", "Stretch to fill the box", "false", "Look")
                .Number("segments", "Segments", "24", "Look", 2, 90)
                .Choice("colorMode", "Colors", "Shift colors", "Colors", "Shift colors", "Gradient", "Solid")
                .Color("color", "Color", "accent", "Colors")
                .Color("warnColor", "Warning color", "warn", "Colors")
                .Color("dangerColor", "Danger color", "bad", "Colors")
                .Number("warnAt", "Warning from", "0.66", "Colors", 0, 1, 0.01)
                .Number("dangerAt", "Danger from", "0.86", "Colors", 0, 1, 0.01)
                .Color("track", "Track", "track", "Colors")
                .Bool("zones", "Mark the zones on the track", "false", "Colors")
                .Color("rim", "Bezel lines", "", "Colors", "Hairlines along both edges of the whole scale. Empty for none.")
                .Number("rimWidth", "Bezel width", "1", "Colors", 0.5, 4, 0.5)
                .Choice("needle", "Needle", "None", "Needle", "None", "Needle", "Line", "Marker")
                .Color("needleColor", "Needle color", "accent", "Needle")
                .Number("needleLength", "Needle length %", "100", "Needle", 20, 130)
                .Number("needleWidth", "Needle width", "5", "Needle", 1, 24)
                .Bool("hub", "Center cap", "true", "Needle")
                .Number("ticks", "Major divisions", "0", "Scale", 0, 40)
                .Number("minorTicks", "Minor ticks between", "4", "Scale", 0, 9)
                .Number("tickLength", "Tick length", "12", "Scale", 2, 60)
                .Color("tickColor", "Tick color", "muted", "Scale")
                .Bool("numbers", "Numbers at major ticks", "false", "Scale")
                .Bool("litScale", "Light up the scale as it's passed", "false", "Scale")
                .Number("numbersFrom", "Numbers from", "0", "Scale", double.NaN, double.NaN, 0.5)
                .Number("numbersTo", "Numbers to", "10", "Scale", double.NaN, double.NaN, 0.5)
                .Number("numberSize", "Number size", "16", "Scale", 6, 60)
                .Color("numberColor", "Number color", "text", "Scale")
                .Formula("flash", "Flash when", "=ShiftFlash", "Flash")
                .Color("flashColor", "Flash color", "bad", "Flash")
                .Number("glow", "Glow", "9", "Effects", 0, 40);

            Define("shiftlights", "Shift lights", "Gauges", "", "An on-screen LED strip that fills with revs and flashes at the shift point.", 360, 28, WidgetPainters.ShiftLights)
                .Formula("value", "Value", "=ShiftPct", "Value")
                .Number("count", "Lights", "10", "Look", 2, 40)
                .Choice("shape", "Shape", "Dot", "Look", "Dot", "Pill", "Bar")
                .Choice("fill", "Fill direction", FillOrder.StartToEnd, "Look", FillOrder.All)
                .ShapeTools()
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

            Define("standings", "Leaderboard", "Racing", "", "The race order: any number of rows, the top of the field or the cars around you, overall or your class.", 300, 300, WidgetPainters.Standings)
                .Number("rows", "Rows", "8", "Content", 1, 30)
                .Choice("focus", "Show", "Top and me", "Content", "Top of the field", "Around me", "Top and me")
                .Bool("classOnly", "Only my class", "false", "Content")
                .Choice("column", "Right column", "Gap to leader", "Content", "Gap to leader", "Interval", "Delta to me", "Last lap", "Best lap", "Laps")
                .Bool("showPosition", "Position", "true", "Columns")
                .Bool("showClass", "Class color", "true", "Columns")
                .Bool("showNumber", "Car number", "true", "Columns")
                .Bool("showCar", "Car name", "false", "Columns")
                .Bool("showIRating", "iRating", "false", "Columns")
                .Font()
                .Number("size", "Text size", "15", "Type", 6, 60)
                .Color("textColor", "Text", "text", "Colors")
                .Color("highlight", "Your row", "accent", "Colors")
                .Color("rowColor", "Row background", "track", "Colors")
                .Color("altRowColor", "Every other row", "", "Colors", "Empty uses the row background.")
                .Color("pitColor", "In the pits", "dim", "Colors")
                .Color("bestColor", "Fastest lap", "purple", "Colors")
                .Number("radius", "Row corners", "6", "Look", 0, 20)
                .Number("gap", "Space between rows", "3", "Look", 0, 20);

            Define("tyres", "Tyres", "Racing", "", "All four tyres: temperature across each tread, wear and pressure. iRacing updates these in the pits.", 260, 220, WidgetPainters.Tyres)
                .Bool("showTemps", "Temperatures", "true", "Content")
                .Bool("showWear", "Wear", "true", "Content")
                .Bool("showPressure", "Pressure", "true", "Content")
                .Font()
                .Number("size", "Text size", "22", "Type", 6, 80)
                .Color("textColor", "Text", "text", "Colors")
                .Color("labelColor", "Labels", "muted", "Colors")
                .Color("panelColor", "Tyre background", "track", "Colors")
                .Color("coolColor", "Cool", "info", "Colors")
                .Color("okColor", "In range", "good", "Colors")
                .Color("hotColor", "Hot", "bad", "Colors")
                .Number("coolBelow", "Cool below", "=temp(70)", "Colors", 0, 250)
                .Number("hotAbove", "Hot above", "=temp(105)", "Colors", 0, 250)
                .Number("radius", "Corners", "10", "Look", 0, 40);

            Define("blindspot", "Blind spot", "Racing", "", "Lights up while a car is alongside, brighter toward where it is.", 26, 220, WidgetPainters.BlindSpot)
                .Choice("side", "Side", "Left", "Content", "Left", "Right")
                .Choice("shape", "Shape", "Bar", "Look", "Bar", "Zones")
                .Color("color", "Color", "bad", "Look")
                .Color("offColor", "Off color", "track", "Look")
                .ShapeTools()
                .Number("glow", "Glow", "14", "Effects", 0, 40);

            Define("trackmap", "Track map", "Racing", "", "The track with every car on it. The map traces itself from your first clean lap at a track and is kept for next time.", 300, 220, WidgetPainters.TrackMapWidget)
                .Number("lineWidth", "Track width", "7", "Look", 1, 30, 0.5)
                .Color("trackColor", "Track", "track", "Look")
                .Color("edgeColor", "Track edge", "border", "Look")
                .Bool("showStart", "Start/finish line", "true", "Look")
                .Number("carSize", "Car size", "11", "Cars", 3, 40)
                .Choice("cars", "Show", "All cars", "Cars", "All cars", "My class", "Only me")
                .Bool("classColors", "Class colors", "true", "Cars")
                .Color("carColor", "Other cars", "text", "Cars")
                .Color("playerColor", "Your car", "accent", "Cars")
                .Color("pitColor", "In the pits", "dim", "Cars")
                .Bool("numbers", "Car numbers", "false", "Cars")
                .Font();

            Define("flag", "Flag", "Racing", "", "Shows the current flag inside its own area. Use it when full-screen flags are off.", 260, 90, WidgetPainters.Flag)
                .Choice("style", "Style", "Fill", "Look", "Fill", "Badge")
                .Bool("label", "Flag name", "true", "Look")
                .Number("radius", "Corner radius", "10", "Look", 0, 60)
                .Font();

            Define("progress", "Session progress", "Racing", "", "Lap blocks in lap races (caution laps in yellow) or a time bar in timed sessions.", 838, 8, WidgetPainters.Progress)
                .Color("color", "Color", "accent", "Colors")
                .Color("cautionColor", "Caution laps", "warn", "Colors")
                .Color("track", "Track", "track", "Colors")
                .Number("gap", "Gap between laps", "2", "Look", 0, 10)
                .ShapeTools();

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

            Define("laphistory", "Lap history", "Graphs", "", "Your recent laps, or this stint's green-flag laps as a lap-falloff chart: each column rises with how much slower the lap was than the best.", 260, 120, WidgetPainters.LapHistory)
                .Choice("source", "Laps", "Recent laps", "Content", "Recent laps", "This stint")
                .Number("laps", "How many", "5", "Content", 2, 40)
                .Choice("style", "Style", "Rows", "Look", "Rows", "Columns")
                .Bool("times", "Show times", "true", "Look")
                .Number("range", "Full height (seconds, 0 auto)", "0", "Look", 0, 10, 0.05)
                .Bool("showDelta", "Show time lost", "true", "Look")
                .Number("labelSize", "Label size", "11", "Look", 6, 40)
                .Number("gap", "Space between columns %", "35", "Look", 0, 90)
                .Number("radius", "Column corners", "3", "Look", 0, 20)
                .Color("color", "Bars", "accent", "Colors")
                .Color("slowColor", "Slowest (Columns)", "bad", "Colors")
                .Color("bestColor", "Best lap", "purple", "Colors")
                .Color("textColor", "Times", "text", "Colors")
                .Color("track", "Column track", "#00000000", "Colors")
                .Font();

            Define("graph", "Graph", "Graphs", "", "A scrolling line of any value (or two) over the last few seconds: speed, revs, fuel, delta, pedals.", 300, 110, WidgetPainters.Graph)
                .Formula("value", "Value", "=Speed", "Value")
                .Formula("value2", "Second value", "", "Value", "Optional: a second line, such as Brake beside Throttle.")
                .Number("seconds", "Time span (seconds)", "10", "Value", 1, 120)
                .Number("min", "Minimum", "0", "Value", double.NaN, double.NaN, 0.1)
                .Number("max", "Maximum (same as minimum: automatic)", "0", "Value", double.NaN, double.NaN, 0.1)
                .Choice("style", "Style", "Area", "Look", "Line", "Area", "Bars")
                .Number("lineWidth", "Line width", "2", "Look", 0.5, 10, 0.5)
                .Bool("grid", "Grid lines", "true", "Look")
                .Color("gridColor", "Grid", "border", "Look")
                .Color("color", "Color", "accent", "Colors")
                .Color("color2", "Second color", "warn", "Colors")
                .Text("label", "Label", "", "Label")
                .Number("labelSize", "Label size", "14", "Label", 6, 60)
                .Color("labelColor", "Label color", "text", "Label")
                .Font();

            // ------------------------------------------------------------ Decor
            Define("shape", "Shape", "Decor", "", "Rectangles, circles, lines, brackets and chevrons for structure and accents.", 120, 80, WidgetPainters.Shape)
                .Choice("shape", "Shape", "Rectangle", "Shape", "Rectangle", "Ellipse", "Line", "Diagonal down", "Diagonal up", "Brackets", "Chevron", "Triangle")
                .Color("fill", "Fill", "", "Colors", "Empty for no fill.")
                .Color("stroke", "Line color", "accent", "Colors")
                .Number("strokeWidth", "Line width", "2", "Look", 0, 30, 0.5)
                .Number("radius", "Corner radius", "6", "Look", 0, 80)
                .ShapeTools()
                .Number("glow", "Glow", "0", "Effects", 0, 40);

            Define("gradient", "Gradient", "Decor", "", "A soft color wash or light bloom to give a layout depth.", 300, 200, WidgetPainters.Gradient)
                .Choice("style", "Style", "Radial", "Look", "Radial", "Vertical", "Horizontal")
                .Color("color", "Color", "accent", "Look")
                .Color("color2", "Fade to", "", "Look", "Empty fades to transparent.")
                .Number("strength", "Strength", "25", "Look", 0, 100);

            // Settings that only matter in some setups stay out of the way until they do.
            Func<string, bool> notSolid = v => v != "Solid", set = v => v.Trim().Length > 0, on = v => Expr.Bool(v), nonZero = v => Math.Abs(Expr.Num(v)) > 1e-9;
            Rule("panel", "style", v => v == "Soft", "fill2");
            Rule("panel", "style", v => v == "Tinted", "tint");
            Rule("panel", "style", v => v == "Liquid", "glassFrost", "glassDepth", "glassRefraction", "glassShine", "glassDispersion", "glassLight");
            Rule("panel", "style", v => v != "None", "fill", "border", "borderWidth");
            Rule("panel", "edge", v => v != "None", "edgeColor");
            Rule("panel", "glow", nonZero, "glowColor");
            Rule("panel", "title", set, "titleColor", "titleSize");
            Rule("text", "glow", nonZero, "glowColor");
            Rule("metric", "unit", set, "unitSize", "unitColor");
            Rule("metric", "layout", v => v != "Value only", "labelSize", "labelColor", "labelTracking");
            Rule("gear", "flash", set, "flashColor");
            Rule("gear", "neighbors", on, "neighborColor", "neighborSize");
            Rule("gear", "glow", nonZero, "glowColor");
            Rule("bar", "mode", v => v == "Segments", "segments");
            Rule("bar", "mode", v => v == "Center", "negColor");
            Rule("bar", "colorMode", notSolid, "warnColor", "dangerColor", "warnAt", "dangerAt");
            Rule("bar", "marker", set, "markerColor");
            Rule("bar", "flash", set, "flashColor");
            Rule("arc", "style", v => v == "Segmented", "segments");
            Rule("arc", "colorMode", notSolid, "warnColor", "dangerColor", "warnAt", "dangerAt");
            Rule("arc", "needle", v => v != "None", "needleColor", "needleLength", "needleWidth", "hub");
            Rule("arc", "ticks", nonZero, "minorTicks", "tickLength", "tickColor", "numbers");
            Rule("arc", "numbers", on, "numbersFrom", "numbersTo", "numberSize", "numberColor");
            Rule("arc", "flash", set, "flashColor");
            Rule("arc", "rim", set, "rimWidth");
            Rule("shiftlights", "flash", set, "flashColor");
            Rule("shape", "shape", v => v == "Rectangle", "radius");
            Rule("graph", "value2", set, "color2");
            Rule("graph", "grid", on, "gridColor");
            Rule("graph", "label", set, "labelSize", "labelColor", "font");
            Rule("graph", "style", v => v != "Bars", "lineWidth");
            Rule("laphistory", "style", v => v == "Rows", "times");
            Rule("laphistory", "style", v => v == "Columns", "range", "showDelta", "labelSize", "gap", "radius", "slowColor", "track");
            foreach (WidgetType shaped in All.Where(x => x.Prop("bend") != null)) Rule(shaped.Id, "bend", nonZero, "peak");
            Rule("shape", "shape", v => v != "Line" && v != "Diagonal down" && v != "Diagonal up" && v != "Brackets" && v != "Chevron", "fill");

            foreach (WidgetType type in All)
            {
                // The shared Style tools (see LayerStyle), applied to whatever the widget draws.
                type.Props.Add(new PropDef { Key = "fxFill", Label = "Recolor", Kind = PropKind.Choice, Default = "None", Group = "Style", Choices = LayerStyle.FillModes,
                    Help = "Lays a colour, a gradient or an image over the widget's own pixels, keeping its shape." });
                type.Props.Add(new PropDef { Key = "fxFillColor", Label = "Recolor with", Kind = PropKind.Color, Default = "accent", Group = "Style" });
                type.Props.Add(new PropDef { Key = "fxFillColor2", Label = "Gradient to", Kind = PropKind.Color, Default = "bad", Group = "Style" });
                type.Props.Add(new PropDef { Key = "fxFillAngle", Label = "Gradient angle", Kind = PropKind.Number, Default = "90", Group = "Style", Min = 0, Max = 360, Step = 5,
                    Help = "0 runs left to right, 90 top to bottom." });
                type.Props.Add(new PropDef { Key = "fxImage", Label = "Texture", Kind = PropKind.Image, Default = "", Group = "Style" });
                type.Props.Add(new PropDef { Key = "fxFillAmount", Label = "Recolor strength %", Kind = PropKind.Number, Default = "100", Group = "Style", Min = 0, Max = 100 });
                type.Props.Add(new PropDef { Key = "fxOutline", Label = "Outline", Kind = PropKind.Number, Default = "0", Group = "Style", Min = 0, Max = 20, Step = 0.5,
                    Help = "A stroke around everything the widget draws: text, gauges, needles." });
                type.Props.Add(new PropDef { Key = "fxOutlineColor", Label = "Outline color", Kind = PropKind.Color, Default = "background", Group = "Style" });
                type.Props.Add(new PropDef { Key = "fxShadow", Label = "Drop shadow", Kind = PropKind.Number, Default = "0", Group = "Style", Min = 0, Max = 40,
                    Help = "How soft the shadow is; 0 for none." });
                type.Props.Add(new PropDef { Key = "fxShadowColor", Label = "Shadow color", Kind = PropKind.Color, Default = "#B4000000", Group = "Style" });
                type.Props.Add(new PropDef { Key = "fxShadowX", Label = "Shadow across", Kind = PropKind.Number, Default = "0", Group = "Style", Min = -40, Max = 40 });
                type.Props.Add(new PropDef { Key = "fxShadowY", Label = "Shadow down", Kind = PropKind.Number, Default = "4", Group = "Style", Min = -40, Max = 40 });
                type.Props.Add(new PropDef { Key = "fxGlow", Label = "Outer glow", Kind = PropKind.Number, Default = "0", Group = "Style", Min = 0, Max = 40,
                    Help = "A soft halo around the whole widget. (A gauge's own Glow lights only its lit parts.)" });
                type.Props.Add(new PropDef { Key = "fxGlowColor", Label = "Glow color", Kind = PropKind.Color, Default = "accent", Group = "Style" });
                type.Props.Add(new PropDef { Key = "flipX", Label = "Flip horizontally", Kind = PropKind.Bool, Default = "false", Group = "Transform",
                    Help = "Mirrors the widget left to right; text stays readable. A flipped copy beside the original makes a pair that meets in the middle." });
                type.Props.Add(new PropDef { Key = "flipY", Label = "Flip vertically", Kind = PropKind.Bool, Default = "false", Group = "Transform", Help = "Mirrors the widget top to bottom; text stays readable." });
                type.Props.Add(new PropDef { Key = "rotate", Label = "Rotate °", Kind = PropKind.Number, Default = "0", Group = "Transform", Min = -180, Max = 180,
                    Help = "Turns the widget about its middle." });
                type.Props.Add(new PropDef { Key = "visible", Label = "Show when", Kind = PropKind.Formula, Default = "", Group = "Visibility", Help = "A condition such as OnPitRoad or Fuel < 5. Empty means always." });
                type.Props.Add(new PropDef { Key = "tapName", Label = "Tap name", Kind = PropKind.Text, Default = "", Group = "Touch",
                    Help = "Makes the widget a tap zone on a touch screen. Each tap moves tap(\"name\") to the next view, so other widgets can show for each view with Show when, such as tap(\"fuel\") == 1." });
                type.Props.Add(new PropDef { Key = "tapViews", Label = "Views", Kind = PropKind.Number, Default = "2", Group = "Touch", Min = 2, Max = 9 });
                type.Props.Add(new PropDef { Key = "tapDots", Label = "Show view dots", Kind = PropKind.Bool, Default = "true", Group = "Touch" });
                type.Props.Add(new PropDef { Key = "opacity", Label = "Opacity %", Kind = PropKind.Number, Default = "100", Group = "Visibility", Min = 0, Max = 100 });

                Rule(type.Id, "fxFill", v => v == "Color" || v == "Gradient", "fxFillColor");
                Rule(type.Id, "fxFill", v => v == "Gradient", "fxFillColor2", "fxFillAngle");
                Rule(type.Id, "fxFill", v => v == "Image", "fxImage");
                Rule(type.Id, "fxFill", v => v != "None", "fxFillAmount");
                Rule(type.Id, "fxOutline", nonZero, "fxOutlineColor");
                Rule(type.Id, "fxShadow", nonZero, "fxShadowColor", "fxShadowX", "fxShadowY");
                Rule(type.Id, "fxGlow", nonZero, "fxGlowColor");
                Rule(type.Id, "tapName", set, "tapViews", "tapDots");
            }
        }

        public static WidgetType Find(string id) => All.FirstOrDefault(t => t.Id == id);

        /// <summary>Settings measured in pixels, which scale with the dashboard when it moves to another screen size.</summary>
        private static readonly HashSet<string> PixelKeys = new HashSet<string>
        {
            "size", "valueSize", "labelSize", "unitSize", "neighborSize", "numberSize", "titleSize", "tickLength", "radius", "borderWidth",
            "strokeWidth", "lineWidth", "carSize", "inset", "needleWidth", "glow", "tracking", "labelTracking",
            "fxShadow", "fxShadowX", "fxShadowY", "fxGlow", "fxOutline", "glassDepth", "glassFrost"
        };

        public static bool IsPixelSize(string typeId, string key)
        {
            if (PixelKeys.Contains(key)) return true;
            // Thickness is pixels on an arc but a share of the box for the Shape tools; gaps are pixels between
            // rows and lap blocks, but a share of each column on the lap chart.
            if (key == "thickness") return typeId == "arc";
            if (key == "gap") return typeId == "progress" || typeId == "standings";
            return false;
        }

        /// <summary>Shows some of a widget's settings only while another setting makes them matter.</summary>
        private static void Rule(string typeId, string dependsOn, Func<string, bool> appliesWhen, params string[] keys)
        {
            WidgetType type = Find(typeId);
            foreach (string key in keys)
            {
                PropDef prop = type?.Prop(key);
                if (prop == null) throw new InvalidOperationException("No setting " + typeId + "." + key);
                prop.DependsOn = dependsOn;
                prop.AppliesWhen = appliesWhen;
            }
        }

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

        /// <summary>The shared Shape tools: bend, peak, taper, bulge, slant, thickness and grow from (see <see cref="ShapeTool"/>).</summary>
        private static WidgetType ShapeTools(this WidgetType t) => t
            .Number("bend", "Bend %", "0", "Shape", -100, 100)
            .Number("peak", "Peak position %", "50", "Shape", 0, 100)
            .Number("taper", "Taper %", "0", "Shape", -100, 100)
            .Number("bulge", "Bulge %", "0", "Shape", -100, 100)
            .Number("skew", "Slant °", "0", "Shape", -60, 60)
            .Number("thickness", "Thickness %", "100", "Shape", 2, 100)
            .Choice("grow", "Grow from", "Middle", "Shape", BarWarp.Anchors);

        private static WidgetType Uses(this WidgetType t, bool traces) { t.UsesTraces = traces; return t; }
    }
}
