using System.Collections.Generic;

namespace AlmostFastDash.Dashboards
{
    internal static partial class DesignTemplates
    {
        // ------------------------------------------------------------------ Halo

        /// <summary>
        /// Endurance and multiclass. One ring round the gear does two jobs: revs across the top with the car's
        /// shift colors, and the live delta across the bottom, growing green to the right as you gain and red
        /// to the left as you lose. Your laps and stint are on the left, the race and fuel on the right (each
        /// panel has a second view on a tap), and along the bottom the cars within eight seconds either way,
        /// in class colors, glowing when a faster class is coming up behind.
        /// </summary>
        private static DashLayout HaloRing()
        {
            var b = new Builder(t =>
            {
                t.Background = "#020705";
                t.Background2 = "#0A2216";
                t.BackgroundStyle = "Bloom";
                t.Panel = "#A0081410";
                t.PanelTop = "#B00E1E17";
                t.Border = "#1B3327";
                t.Track = "#0E1B15";
                t.Muted = "#B2C7BB";
                t.Dim = "#3A5145";
            });
            b.Screen("Race");
            const string ring = "#0F1D17", bezel = "#1E3528";
            b.Text("Session", 20, 12, 300, 22, "=\"LAP \" + LapText + \"   ·   \" + RemainingText", "size", "15", "tracking", "1.5", "align", "Left", "color", "muted");
            b.Text("Incidents", 600, 12, 150, 22, "=Incidents + \"x\"", "size", "15", "tracking", "1.5", "align", "Right", "color", "=Incidents > 0 ? $warn : $muted");
            b.Text("Clock", 760, 12, 74, 22, "=Clock", "size", "15", "tracking", "1.5", "align", "Right", "color", "muted");

            // The ring: revs over the top, the delta under it.
            b.Add("gradient", "Halo light", 207, 0, 440, 420, "style", "Radial", "color", "accent", "strength", "12");
            b.Add("shape", "Ring face", 309, 92, 236, 236, "shape", "Ellipse", "fill", "#0A1712", "stroke", "#163024", "strokeWidth", "1",
                "fxFill", "Gradient", "fxFillColor", "#0E1F18", "fxFillColor2", "#040A07", "fxFillAngle", "90", "fxFillAmount", "100");
            b.Add("arc", "Rev ring", 287, 70, 280, 280, "value", "=Rpm / 1000", "max", RpmTop, "style", "Smooth", "start", "150", "sweep", "240", "thickness", "12",
                "colorMode", "Shift colors", "color", "accent", "warnColor", "warn", "dangerColor", "bad",
                "warnAt", "=ShiftRpm > 0 && Redline > 0 ? ShiftRpm * 0.93 / (ceil(Redline / 1000) * 1000) : 0.8",
                "dangerAt", "=ShiftRpm > 0 && Redline > 0 ? ShiftRpm / (ceil(Redline / 1000) * 1000) : 0.9",
                "zones", "true", "track", ring, "rim", bezel, "rimWidth", "1", "ticks", RpmTop, "minorTicks", "1", "tickLength", "7", "tickColor", "#5E7A6C", "litScale", "true",
                "needle", "Marker", "needleColor", "#FFFFFF", "needleLength", "130", "needleWidth", "3", "glow", "8", "flash", "=ShiftFlash");
            b.Add("arc", "Delta losing", 287, 70, 280, 280, "value", "=-Delta", "min", "0", "max", "1", "style", "Smooth", "start", "94", "sweep", "52", "thickness", "12",
                "colorMode", "Solid", "color", "bad", "track", ring, "rim", bezel, "rimWidth", "1", "flash", "", "glow", "6");
            b.Add("arc", "Delta gaining", 287, 70, 280, 280, "value", "=Delta", "min", "0", "max", "1", "style", "Smooth", "start", "86", "sweep", "-52", "thickness", "12",
                "colorMode", "Solid", "color", "good", "track", ring, "rim", bezel, "rimWidth", "1", "flash", "", "glow", "6");
            b.Add("shape", "Delta zero", 426, 334, 2, 22, "shape", "Line", "stroke", "#5E7A6C", "strokeWidth", "2");
            b.Text("Speed", 347, 104, 160, 36, "=format(Speed, \"0\")", "size", "32");
            b.Text("Speed unit", 347, 138, 160, 12, "=SpeedUnit", "size", "10", "tracking", "3", "weight", "Regular", "color", "muted");
            b.Add("gear", "Gear", 342, 150, 170, 140, "size", "128", "neighbors", "false", "flashColor", "accent", "fxShadow", "8", "fxShadowY", "3");
            b.Text("Delta", 347, 290, 160, 30, "=signed(Delta, 2)", "size", "24", "color", "=deltacolor(Delta)", "visible", "Notice == \"\"");
            Notice(b, 317, 290, 220, 30);

            // Left: your laps (tap for the sectors), then the stint (tap for the tyres).
            b.Panel("Laps panel", 16, 52, 252, 150, "style", "Liquid", "radius", "18", "tapName", "laps");
            b.Visible("tap(\"laps\") == 0",
                b.Label("Last lap label", 34, 66, 200, "LAST LAP"),
                b.Text("Last lap", 32, 82, 220, 44, "=laptime(LastLap)", "size", "38", "align", "Left", "color", "=LastLapIsBest ? $purple : $text"),
                b.Metric("Best lap", 24, 130, 118, 60, "BEST", "=laptime(BestLap)", "inset", "10", "valueSize", "20", "labelSize", "10", "labelTracking", "2", "valueColor", "purple"),
                b.Metric("Predicted", 142, 130, 118, 60, "PREDICTED", "=PredictedLap > 0 ? laptime(PredictedLap) : \"--\"", "align", "Right", "inset", "10", "valueSize", "20",
                    "labelSize", "10", "labelTracking", "2", "valueColor", "=PredictedLap > 0 && PredictedLap < BestLap ? $good : $text"));
            var sectors = new List<DashWidget> { b.Label("Sectors label", 34, 66, 220, "SECTORS · LAST LAP") };
            for (int s = 1; s <= 3; s++)
            {
                string last = "lastsectortime(" + s + ")";
                sectors.Add(b.Metric("S" + s, 24 + (s - 1) * 79, 80, 79, 58, "S" + s, "=seconds(" + last + ")", "align", "Center", "inset", "4", "valueSize", "18", "labelSize", "10",
                    "labelTracking", "2", "valueColor", "=" + last + " > 0 && " + last + " <= bestsectortime(" + s + ") + 0.0005 ? $purple : $text"));
            }
            sectors.Add(b.Metric("Ideal lap", 24, 136, 236, 56, "IDEAL LAP", "=IdealLap > 0 ? laptime(IdealLap) : \"--\"", "inset", "10", "valueSize", "20", "labelSize", "10",
                "labelTracking", "2", "valueColor", "purple"));
            b.Visible("tap(\"laps\") == 1", sectors.ToArray());

            b.Panel("Stint panel", 16, 214, 252, 150, "style", "Liquid", "radius", "18", "tapName", "stint");
            b.Visible("tap(\"stint\") == 0",
                b.Text("Stint label", 34, 226, 220, 16, "=\"STINT  ·  \" + StintLaps + (StintLaps == 1 ? \" LAP\" : \" LAPS\")", "size", "11", "tracking", "2.2", "weight", "Regular",
                    "align", "Left", "color", "muted"),
                b.Text("Falloff", 32, 244, 130, 44, "=Falloff < 0 ? \"--\" : \"+\" + format(Falloff, \"0.00\")", "size", "36", "align", "Left",
                    "color", "=Falloff < 0 ? $text : Falloff < 0.15 ? $good : Falloff < 0.4 ? $warn : $bad"),
                b.Text("Falloff caption", 34, 288, 130, 14, "LAP FALLOFF", "size", "11", "tracking", "2.2", "weight", "Regular", "align", "Left", "color", "muted"),
                b.Text("Falloff trend", 34, 310, 130, 18, "=StintGreenLaps < 3 ? \"\" : signed(FalloffPerLap, 2) + \" PER LAP\"", "size", "12", "tracking", "1",
                    "weight", "Regular", "align", "Left", "color", "muted"),
                b.Text("Stint best", 34, 330, 130, 18, "=StintBestLap > 0 ? \"BEST \" + laptime(StintBestLap) : \"\"", "size", "12", "tracking", "1",
                    "weight", "Regular", "align", "Left", "color", "purple"),
                b.Add("laphistory", "Falloff chart", 168, 244, 86, 104, "source", "This stint", "laps", "8", "style", "Columns", "range", "0", "showDelta", "false",
                    "gap", "28", "radius", "2", "color", "#1FA463", "slowColor", "#9BF0C4", "track", "#0B1611"));
            b.Visible("tap(\"stint\") == 1",
                b.Label("Tires label", 34, 226, 220, "TIRES · LAST STOP"),
                b.Add("tyres", "Tires", 28, 244, 228, 108, "showWear", "false", "showPressure", "false", "size", "16"));

            // Right: the race (tap for the order in your class), then fuel (tap for more).
            b.Panel("Race panel", 586, 52, 252, 150, "style", "Liquid", "radius", "18", "tapName", "race");
            b.Visible("tap(\"race\") == 0",
                b.Label("Position label", 604, 66, 200, "POSITION"),
                b.Metric("Position", 598, 76, 130, 52, "", PositionText, "layout", "Value only", "inset", "6", "valueSize", "40",
                    "unit", "=Starters > 0 ? \"/\" + Starters : \"\"", "unitColor", "muted"),
                b.Text("Class", 716, 88, 106, 28, "=ClassName != \"\" && ClassPosition > 0 ? \"P\" + ClassPosition + \" \" + ClassName : \"\"", "size", "16",
                    "align", "Right", "color", "accent"),
                b.Text("Ahead name", 604, 136, 150, 20, "=AheadName == \"---\" ? \"AHEAD  --\" : AheadName", "size", "14", "weight", "Regular", "align", "Left", "color", "text"),
                b.Text("Ahead gap", 754, 136, 68, 20, "=AheadName == \"---\" ? \"\" : \"-\" + format(AheadGap, \"0.0\")", "size", "16", "align", "Right", "color", "good"),
                b.Text("Behind name", 604, 162, 150, 20, "=BehindName == \"---\" ? \"BEHIND  --\" : BehindName", "size", "14", "weight", "Regular", "align", "Left", "color", "muted"),
                b.Text("Behind gap", 754, 162, 68, 20, "=BehindName == \"---\" ? \"\" : \"+\" + format(BehindGap, \"0.0\")", "size", "16", "align", "Right", "color", "warn"));
            b.Visible("tap(\"race\") == 1",
                b.Label("Class order label", 604, 66, 200, "=ClassName != \"\" ? ClassName + \" ORDER\" : \"RACE ORDER\""),
                b.Add("standings", "Class order", 596, 84, 232, 106, "rows", "4", "focus", "Around me", "classOnly", "true", "column", "Interval", "size", "13",
                    "showClass", "false", "rowColor", "#80101E17", "radius", "5", "gap", "2"));

            b.Panel("Fuel panel", 586, 214, 252, 150, "style", "Liquid", "radius", "18", "tapName", "fuel");
            b.Visible("tap(\"fuel\") == 0",
                b.Label("Fuel label", 604, 226, 200, "FUEL"),
                b.Text("Fuel laps", 602, 242, 110, 48, FuelLaps, "size", "42", "align", "Left", "color", FuelTone),
                b.Text("Fuel laps caption", 604, 290, 110, 14, "LAPS LEFT", "size", "11", "tracking", "2.2", "weight", "Regular", "align", "Left", "color", "muted"),
                b.Metric("Fuel to add", 712, 230, 118, 60, "TO ADD", FuelToAdd, "align", "Right", "inset", "8", "valueSize", "20", "labelSize", "10", "labelTracking", "2",
                    "unit", FuelToAddUnit, "unitSize", "11", "valueColor", FuelToAddTone),
                b.Metric("Fuel per lap", 712, 284, 118, 56, "=FuelEstimate ? \"PER LAP · EST\" : \"PER LAP\"", "=FuelPerLap > 0 ? fuel(FuelPerLap) : \"--\"", "align", "Right",
                    "inset", "8", "valueSize", "18", "labelSize", "10", "labelTracking", "2"),
                b.Add("bar", "Fuel level", 604, 346, 216, 4, "value", "=FuelPct", "color", "=FuelLapsLeft >= 0 && FuelLapsLeft < 2 ? $bad : accent", "track", "#13241B"));
            b.Visible("tap(\"fuel\") == 1",
                b.Metric("In tank", 594, 228, 236, 60, "IN THE TANK", "=fuel(Fuel)", "inset", "10", "valueSize", "26", "labelSize", "10", "labelTracking", "2", "unit", "=FuelUnit"),
                b.Metric("Fuel last lap", 594, 288, 118, 58, "LAST LAP", "=FuelLastLap > 0 ? fuel(FuelLastLap) : \"--\"", "inset", "10", "valueSize", "18", "labelSize", "10",
                    "labelTracking", "2"),
                b.Metric("Tank size", 712, 288, 118, 58, "TANK", "=FuelCapacity > 0 ? fuel(FuelCapacity) : \"--\"", "align", "Right", "inset", "8", "valueSize", "18",
                    "labelSize", "10", "labelTracking", "2"));

            // Along the bottom: the traffic either side of you.
            b.Panel("Traffic panel", 16, 376, 822, 90, "style", "Liquid", "radius", "18");
            b.Label("Behind label", 34, 388, 200, "BEHIND");
            b.Label("Ahead label", 620, 388, 200, "AHEAD", "Right");
            b.Label("Traffic label", 252, 386, 350, "TRAFFIC  ·  8 S EACH WAY", "Center", "muted").Set("visible", "FasterClassBehind < 0");
            b.Text("Faster class", 277, 384, 300, 20, "=(FasterClass != \"\" ? FasterClass : \"FASTER CLASS\") + \" CLOSING  ·  \" + format(FasterClassBehind, \"0.0\") + \" S\"",
                "size", "13", "tracking", "2", "color", "warn", "visible", "FasterClassBehind >= 0");
            b.Add("traffic", "Traffic", 30, 410, 794, 44, "seconds", "8");

            b.Add("blindspot", "Car left", 2, 120, 6, 220, "side", "Left", "color", "bad", "offColor", "#00000000", "glow", "14");
            b.Add("blindspot", "Car right", 846, 120, 6, 220, "side", "Right", "color", "bad", "offColor", "#00000000", "glow", "14");

            // Page 2 on a wheel button: the race order by class, the map and the class's times.
            b.Screen("Standings");
            b.Panel("Order panel", 16, 14, 470, 452, "style", "Liquid", "radius", "18");
            b.Label("Order label", 34, 28, 300, "RACE ORDER");
            b.Add("standings", "Leaderboard", 26, 48, 450, 408, "rows", "11", "focus", "Top and me", "column", "Gap to leader", "showClass", "true", "size", "16",
                "rowColor", "#80101E17", "radius", "7");
            b.Panel("Map panel", 496, 14, 342, 228, "style", "Liquid", "radius", "18");
            b.Label("Map label", 514, 28, 200, "TRACK");
            b.Add("trackmap", "Track map", 508, 46, 318, 186, "lineWidth", "6", "carSize", "9", "trackColor", "#1B3327", "edgeColor", "#00000000");
            b.Panel("Class panel", 496, 252, 342, 214, "style", "Liquid", "radius", "18");
            b.Label("Class label", 514, 266, 300, "=ClassName != \"\" ? \"CLASS  ·  \" + ClassName : \"CLASS\"");
            b.Metric("Class position", 504, 282, 160, 80, "IN CLASS", "=ClassPosition > 0 ? \"P\" + ClassPosition : \"P--\"", "valueSize", "34", "labelSize", "10", "labelTracking", "2",
                "valueColor", "accent");
            b.Metric("Overall", 670, 282, 160, 80, "OVERALL", PositionText, "align", "Right", "valueSize", "34", "labelSize", "10", "labelTracking", "2");
            b.Metric("Class best", 504, 372, 160, 80, "CLASS BEST", "=laptime(ClassBestLap)", "valueSize", "22", "labelSize", "10", "labelTracking", "2", "valueColor", "purple");
            b.Metric("Your best", 670, 372, 160, 80, "YOUR BEST", "=laptime(BestLap)", "align", "Right", "valueSize", "22", "labelSize", "10", "labelTracking", "2");

            PitLane(b, "Liquid");
            return b.Layout;
        }
    }
}
