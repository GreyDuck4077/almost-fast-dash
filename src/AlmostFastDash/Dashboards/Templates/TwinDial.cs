using System;
using System.Collections.Generic;
using System.Linq;

namespace AlmostFastDash.Dashboards
{
    internal static partial class DesignTemplates
    {
        // ------------------------------------------------------------------ Twin Dial

        /// <summary>
        /// After the Ford and Audi clusters: a rev counter and a speedometer as real needle dials with
        /// numbered scales and a red zone, the gear inside the rev counter, and the map, lap and delta
        /// between them; fuel and temperatures along the bottom.
        /// </summary>
        private static DashLayout TwinDial()
        {
            var b = new Builder(t =>
            {
                t.Background = "#02040A";
                t.Background2 = "#0A1630";
                t.BackgroundStyle = "Vignette";
                t.Track = "#1A2436";
            });
            b.Screen("Race");
            // Faint discs behind the dials give them a face.
            b.Add("shape", "Tach face", 26, 30, 312, 312, "shape", "Ellipse", "fill", "#60080F1C", "stroke", "#1B2840", "strokeWidth", "1.5");
            b.Add("shape", "Speedo face", 516, 30, 312, 312, "shape", "Ellipse", "fill", "#60080F1C", "stroke", "#1B2840", "strokeWidth", "1.5");
            b.Add("arc", "Rev counter", 16, 20, 332, 332, "value", "=Rpm / 1000", "max", RpmTop, "style", "Smooth", "thickness", "6", "start", "135", "sweep", "270",
                "colorMode", "Shift colors", "color", "accent", "warnAt", "0.8", "dangerAt", "0.9", "zones", "true", "ticks", RpmTop, "minorTicks", "4",
                "tickLength", "14", "tickColor", "#8EA2BE", "numbers", "true", "numbersTo", RpmTop, "numberSize", "24", "needle", "Needle", "needleColor", "#7DD3FC",
                "needleLength", "88", "needleWidth", "7", "glow", "10", "flash", "=ShiftFlash");
            b.Text("Rev unit", 107, 232, 150, 16, "x1000 RPM", "size", "11", "tracking", "2", "weight", "Regular", "color", "muted");
            b.Add("gear", "Gear", 132, 250, 100, 70, "size", "56", "neighbors", "false", "glow", "6");

            b.Add("arc", "Speedometer", 506, 20, 332, 332, "value", "=Speed", "max", SpeedTop, "style", "Smooth", "thickness", "6", "start", "135", "sweep", "270",
                "colorMode", "Solid", "color", "accent", "ticks", "=SpeedUnit == \"KM/H\" ? 8 : 10", "minorTicks", "3", "tickLength", "14", "tickColor", "#8EA2BE",
                "numbers", "true", "numbersTo", SpeedTop, "numberSize", "22", "needle", "Needle", "needleColor", "#7DD3FC", "needleLength", "88", "needleWidth", "7", "glow", "8");
            b.Text("Speed", 582, 226, 180, 60, "=format(Speed, \"0\")", "size", "52");
            b.Text("Speed unit", 582, 282, 180, 16, "=SpeedUnit", "size", "11", "tracking", "3", "weight", "Regular", "color", "muted");

            b.Panel("Center panel", 352, 34, 150, 300, "style", "Glass", "radius", "18");
            b.Add("trackmap", "Track map", 358, 44, 138, 120, "lineWidth", "5", "carSize", "7");
            b.Metric("Lap", 352, 170, 150, 50, "LAP", "=LapText", "align", "Center", "valueSize", "22", "labelSize", "11", "labelTracking", "2");
            b.Metric("Delta", 352, 224, 150, 50, "DELTA", "=signed(Delta, 2)", "align", "Center", "valueSize", "22", "labelSize", "11", "labelTracking", "2", "valueColor", "=deltacolor(Delta)");
            b.Metric("Last lap", 352, 278, 150, 50, "LAST", "=laptime(LastLap)", "align", "Center", "valueSize", "20", "labelSize", "11", "labelTracking", "2", "valueColor", "=LastLapIsBest ? $purple : $text");

            b.Panel("Bottom strip", 16, 378, 822, 88, "style", "Glass", "radius", "18");
            b.Metric("Position", 20, 382, 150, 80, "POSITION", PositionText, "valueSize", "30", "unit", "=Starters > 0 ? \"/\" + Starters : \"\"", "unitColor", "muted");
            b.Metric("Fuel", 180, 382, 160, 80, "FUEL", "=fuel(Fuel)", "valueSize", "30", "unit", "=FuelUnit");
            b.Add("bar", "Fuel level", 196, 448, 130, 5, "value", "=FuelPct", "color", "=FuelLapsLeft >= 0 && FuelLapsLeft < 2 ? $bad : $accent");
            b.Metric("Fuel laps", 350, 382, 140, 80, "LAPS LEFT", FuelLaps, "valueSize", "30", "valueColor", FuelTone);
            b.Metric("Water", 500, 382, 110, 80, "WATER", "=format(WaterTemp, \"0\") + \"°\"", "valueSize", "26");
            b.Metric("Oil", 610, 382, 110, 80, "OIL", "=format(OilTemp, \"0\") + \"°\"", "valueSize", "26");
            b.Metric("Clock", 720, 382, 114, 80, "TIME", "=Clock", "valueSize", "26", "align", "Right");
            b.Add("blindspot", "Car left", 2, 80, 6, 240, "side", "Left", "glow", "12");
            b.Add("blindspot", "Car right", 846, 80, 6, 240, "side", "Right", "glow", "12");
            Notice(b, 287, 342, 280, 30);

            PitLane(b, "Glass");
            return b.Layout;
        }
    }
}
