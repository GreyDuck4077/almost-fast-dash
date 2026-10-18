using System;
using System.Collections.Generic;
using System.Linq;

namespace AlmostFastDash.Dashboards
{
    internal static partial class DesignTemplates
    {
        // ------------------------------------------------------------------ Roady

        /// <summary>
        /// The road flagship. A wide sweep of revs arches over the whole screen with the scale inside it and
        /// a bright cursor at the revs. It turns amber at the car's shift point and red at the redline, and
        /// flashes to shift. The gear stands huge under the arch. Delta and laps sit on the left, the race
        /// on the right, and pedals, car settings and fuel along the bottom.
        /// </summary>
        private static DashLayout Roady()
        {
            var b = new Builder(t =>
            {
                t.Background = "#050506";
                t.Background2 = "#1A0D06";
                t.BackgroundStyle = "Bloom";
                t.Panel = "#A00E0D0C";
                t.PanelTop = "#B0161412";
                t.Border = "#2A2420";
                t.Track = "#1A1716";
                t.Text = "#F5F2EE";
                t.Muted = "#BDB3AA";
                t.Dim = "#4A433E";
            });
            b.Screen("Race");
            const string top = "ceil(Redline / 1000)";
            // A soft pool of warm light behind the gear gives the screen depth.
            b.Add("gradient", "Gear light", 197, 40, 460, 340, "style", "Radial", "color", "accent", "strength", "9");
            b.Add("arc", "Rev sweep", 17, 14, 820, 600, "value", "=Rpm / 1000", "max", "=Redline > 0 ? " + top + " : 8", "oval", "true", "style", "Smooth",
                "start", "197", "sweep", "146", "thickness", "10", "colorMode", "Shift colors", "color", "#EDE8E2", "warnColor", "#FFB23F", "dangerColor", "accent",
                "warnAt", "=ShiftRpm > 0 && Redline > 0 ? ShiftRpm * 0.93 / (" + top + " * 1000) : 0.8",
                "dangerAt", "=ShiftRpm > 0 && Redline > 0 ? ShiftRpm / (" + top + " * 1000) : 0.9",
                "zones", "true", "track", "#1C1714", "rim", "#2E2722", "rimWidth", "1",
                "ticks", "=Redline > 0 ? " + top + " : 8", "minorTicks", "4", "tickLength", "12", "tickColor", "#8A8078", "litScale", "true",
                "numbers", "true", "numbersTo", "=Redline > 0 ? " + top + " : 8", "numberSize", "20", "numberColor", "text",
                "needle", "Marker", "needleColor", "#FFFFFF", "needleLength", "130", "needleWidth", "3", "glow", "6", "flash", "=ShiftFlash", "flashColor", "accent");
            b.Text("Rpm unit", 30, 262, 110, 14, "RPM x1000", "size", "10", "tracking", "2.5", "weight", "Regular", "color", "muted", "align", "Left");
            b.Text("Clock", 24, 12, 120, 22, "=Clock", "size", "16", "align", "Left", "color", "muted");
            b.Text("Incidents", 710, 12, 120, 22, "=Incidents + \"x\"", "size", "16", "align", "Right", "color", "=Incidents > 0 ? $warn : $muted");

            b.Add("gear", "Gear", 332, 82, 190, 190, "size", "176", "neighbors", "false", "flashColor", "accent", "fxShadow", "6", "fxShadowY", "3",
                "fxFill", "Gradient", "fxFillColor", "#FFFFFF", "fxFillColor2", "#C9C1B8", "fxFillAngle", "90", "fxFillAmount", "=ShiftFlash ? 0 : 100");
            b.Text("Speed", 337, 278, 180, 58, "=format(Speed, \"0\")", "size", "52");
            b.Text("Speed unit", 337, 332, 180, 14, "=SpeedUnit", "size", "11", "tracking", "4", "weight", "Regular", "color", "muted");

            // Left of the gear: the lap.
            b.Label("Delta label", 140, 196, 180, "DELTA");
            b.Text("Delta", 140, 210, 180, 50, "=signed(Delta, 2)", "size", "44", "align", "Left", "color", "=deltacolor(Delta)");
            b.Add("bar", "Delta bar", 140, 264, 180, 4, "value", "=Delta", "min", "-1", "max", "1", "mode", "Center", "color", "good", "negColor", "bad", "track", "#221D19");
            b.Metric("Last lap", 132, 280, 104, 56, "LAST", "=laptime(LastLap)", "inset", "8", "valueSize", "19", "labelSize", "11", "labelTracking", "2.5",
                "valueColor", "=LastLapIsBest ? $purple : $text");
            b.Metric("Best lap", 228, 280, 104, 56, "BEST", "=laptime(BestLap)", "inset", "8", "valueSize", "19", "labelSize", "11", "labelTracking", "2.5", "valueColor", "purple");

            // Right of the gear: the race.
            b.Label("Position label", 534, 196, 180, "POSITION", "Right");
            b.Metric("Position", 534, 206, 180, 58, "", PositionText, "layout", "Value only", "align", "Right", "inset", "0", "valueSize", "44",
                "unit", "=Starters > 0 ? \"/\" + Starters : \"\"", "unitColor", "muted");
            b.Add("progress", "Race progress", 534, 264, 180, 4, "gap", "1", "color", "#E9E4DE");
            b.Metric("Lap", 522, 280, 104, 56, "LAP", "=LapText", "inset", "8", "align", "Right", "valueSize", "19", "labelSize", "11", "labelTracking", "2.5");
            b.Metric("Ahead", 618, 280, 104, 56, "AHEAD", "=AheadName == \"---\" ? \"--\" : \"-\" + format(AheadGap, \"0.0\")", "inset", "8", "align", "Right",
                "valueSize", "19", "labelSize", "11", "labelTracking", "2.5", "valueColor", "good");

            b.Add("shape", "Rule", 60, 352, 734, 2, "shape", "Line", "stroke", "#2E2722", "strokeWidth", "1");

            // Along the bottom: pedals, the car's settings, fuel.
            b.Panel("Pedals panel", 18, 366, 300, 104, "style", "Liquid", "radius", "14");
            b.Label("Pedals label", 34, 376, 200, "THROTTLE · BRAKE");
            b.Add("graph", "Pedals", 28, 392, 280, 70, "value", "=Throttle", "value2", "=Brake", "min", "0", "max", "1", "seconds", "6", "style", "Area",
                "lineWidth", "2", "color", "#E9E4DE", "color2", "accent", "gridColor", "#221D19");

            b.Panel("Car panel", 328, 366, 198, 104, "style", "Liquid", "radius", "14");
            b.Metric("Bias", 334, 372, 62, 92, "BIAS", "=format(BrakeBias, \"0.0\")", "align", "Center", "valueSize", "20", "labelSize", "11", "labelTracking", "2");
            b.Metric("TC", 396, 372, 62, 92, "TC", "=TractionControl < 0 ? \"--\" : format(TractionControl, \"0\")", "align", "Center", "valueSize", "20", "labelSize", "11", "labelTracking", "2");
            b.Metric("ABS", 458, 372, 62, 92, "ABS", "=Abs < 0 ? \"--\" : format(Abs, \"0\")", "align", "Center", "valueSize", "20", "labelSize", "11", "labelTracking", "2",
                "valueColor", "=AbsActive ? $accent : $text");

            b.Panel("Fuel panel", 536, 366, 300, 104, "style", "Liquid", "radius", "14");
            b.Metric("Fuel", 540, 370, 150, 84, "FUEL", "=fuel(Fuel)", "valueSize", "28", "labelSize", "11", "labelTracking", "2.5", "unit", "=FuelUnit");
            b.Metric("Fuel laps", 682, 370, 150, 84, "LAPS LEFT", FuelLaps, "align", "Right", "valueSize", "28", "labelSize", "11", "labelTracking", "2.5", "valueColor", FuelTone);
            b.Add("bar", "Fuel level", 556, 454, 260, 4, "value", "=FuelPct", "color", "=FuelLapsLeft >= 0 && FuelLapsLeft < 2 ? $bad : #E9E4DE", "track", "#221D19");

            b.Add("blindspot", "Car left", 2, 120, 6, 220, "side", "Left", "color", "accent", "offColor", "#00000000", "glow", "14");
            b.Add("blindspot", "Car right", 846, 120, 6, 220, "side", "Right", "color", "accent", "offColor", "#00000000", "glow", "14");
            Notice(b, 287, 312, 280, 34);

            // Page 2 on a wheel button: the race order, the map and the sectors.
            b.Screen("Timing");
            b.Panel("Order panel", 18, 14, 410, 452, "style", "Liquid", "radius", "14");
            b.Label("Order label", 34, 26, 300, "RACE ORDER");
            b.Add("standings", "Leaderboard", 26, 46, 394, 410, "rows", "10", "focus", "Top and me", "column", "Interval", "size", "16", "rowColor", "#80141210",
                "highlight", "accent", "radius", "6");
            b.Panel("Map panel", 438, 14, 398, 236, "style", "Liquid", "radius", "14");
            b.Label("Map label", 454, 26, 200, "TRACK");
            b.Text("Map lap", 620, 22, 200, 20, "=\"LAP \" + LapText", "size", "14", "align", "Right", "color", "muted");
            b.Add("trackmap", "Track map", 448, 44, 378, 198, "lineWidth", "6", "carSize", "9", "trackColor", "#2A2420", "edgeColor", "#00000000");
            b.Panel("Sector panel", 438, 260, 398, 206, "style", "Liquid", "radius", "14");
            b.Label("Sector label", 454, 272, 300, "SECTORS · LAST LAP");
            for (int s = 1; s <= 3; s++)
            {
                string last = "lastsectortime(" + s + ")";
                b.Metric("S" + s, 446 + (s - 1) * 128, 290, 126, 84, "S" + s, "=seconds(" + last + ")", "align", "Center", "valueSize", "24", "labelSize", "11", "labelTracking", "2",
                    "valueColor", "=" + last + " > 0 && " + last + " <= bestsectortime(" + s + ") + 0.0005 ? $purple : $text", "visible", s == 3 ? "SectorCount >= 3" : "");
            }
            b.Add("shape", "Sector rule", 458, 380, 358, 2, "shape", "Line", "stroke", "#2A2420", "strokeWidth", "1");
            b.Metric("Ideal lap", 446, 388, 190, 70, "IDEAL LAP", "=IdealLap > 0 ? laptime(IdealLap) : \"--\"", "valueSize", "24", "labelSize", "11", "labelTracking", "2", "valueColor", "purple");
            b.Metric("Session best", 640, 388, 190, 70, "SESSION BEST", "=laptime(SessionBestLap)", "align", "Right", "valueSize", "24", "labelSize", "11", "labelTracking", "2");

            PitLane(b, "Liquid");
            TouchViews.Roady(b.Layout);
            return b.Layout;
        }
    }
}
