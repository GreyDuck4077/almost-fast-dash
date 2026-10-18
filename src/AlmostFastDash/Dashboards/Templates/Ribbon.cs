namespace AlmostFastDash.Dashboards
{
    internal static partial class DesignTemplates
    {
        // ------------------------------------------------------------------ Ribbon

        /// <summary>
        /// A wide three-panel cluster: lap timing and a pedal graph on the left; in the middle two
        /// half-dials (one a mirrored copy of the other) meeting around the gear and speed; the cars
        /// around you and the gaps on the right.
        /// </summary>
        private static DashLayout Ribbon()
        {
            var b = new Builder(t =>
            {
                t.Background = "#03070D";
                t.Background2 = "#0B2236";
                t.BackgroundStyle = "Gradient";
            });
            b.Screen("Race");
            b.Panel("Left panel", 10, 24, 270, 432, "style", "Liquid", "radius", "24");
            b.Panel("Center panel", 290, 24, 274, 432, "style", "Liquid", "radius", "24");
            b.Panel("Right panel", 574, 24, 270, 432, "style", "Liquid", "radius", "24");

            b.Metric("Last lap", 18, 36, 254, 70, "LAST LAP", "=laptime(LastLap)", "valueSize", "34", "valueColor", "=LastLapIsBest ? $purple : $text");
            b.Metric("Best lap", 18, 108, 126, 56, "BEST", "=laptime(BestLap)", "valueSize", "20", "valueColor", "purple");
            b.Metric("Predicted", 146, 108, 126, 56, "PREDICTED", "=laptime(PredictedLap)", "valueSize", "20", "align", "Right");
            b.Label("Delta label", 34, 176, 120, "DELTA");
            b.Text("Delta", 120, 168, 136, 28, "=signed(Delta, 2)", "size", "24", "align", "Right", "color", "=deltacolor(Delta)");
            b.Add("bar", "Delta bar", 34, 204, 222, 6, "value", "=Delta", "min", "-1", "max", "1", "mode", "Center", "color", "good", "negColor", "bad", "glow", "4");
            b.Label("Pedals label", 34, 232, 200, "THROTTLE · BRAKE");
            b.Add("graph", "Pedals", 26, 250, 238, 120, "value", "=Throttle", "value2", "=Brake", "min", "0", "max", "1", "seconds", "6", "style", "Line",
                "lineWidth", "2.5", "color", "good", "color2", "bad", "gridColor", "#18222F");
            b.Metric("Fuel", 18, 382, 126, 66, "FUEL", "=fuel(Fuel)", "valueSize", "24", "unit", "=FuelUnit");
            b.Metric("Fuel laps", 146, 382, 126, 66, "LAPS", FuelLaps, "valueSize", "24", "align", "Right", "valueColor", FuelTone);

            // Two half-dials: the rev counter on the left, and the same widget flipped to read the speed on the right.
            b.Add("arc", "Rev half", 296, 60, 262, 262, "value", "=RevPct", "style", "Segmented", "segments", "18", "start", "100", "sweep", "160", "thickness", "14",
                "colorMode", "Gradient", "warnAt", "0.6", "dangerAt", "0.88", "flash", "=ShiftFlash", "glow", "10");
            b.Add("arc", "Speed half", 296, 60, 262, 262, "value", "=Speed", "max", SpeedTop, "style", "Segmented", "segments", "18", "start", "100", "sweep", "160",
                "thickness", "14", "colorMode", "Solid", "color", "info", "glow", "8", "flipX", "true");
            b.Add("gear", "Gear", 377, 110, 100, 110, "size", "92", "neighbors", "false", "glow", "8", "fxShadow", "8");
            b.Text("Speed", 362, 220, 130, 46, "=format(Speed, \"0\")", "size", "40");
            b.Text("Speed unit", 362, 264, 130, 16, "=SpeedUnit", "size", "11", "tracking", "3", "weight", "Regular", "color", "muted");
            b.Metric("Position", 298, 342, 128, 100, "POSITION", PositionText, "align", "Center", "valueSize", "34");
            b.Metric("Lap", 428, 342, 128, 100, "LAP", "=LapText", "align", "Center", "valueSize", "30");
            b.Add("progress", "Race progress", 306, 440, 242, 4, "gap", "1");

            b.Label("Around label", 592, 40, 200, "AROUND YOU");
            b.Add("standings", "Around me", 582, 58, 254, 250, "rows", "5", "focus", "Around me", "column", "Interval", "showClass", "true", "size", "15", "rowColor", "#70101826");
            b.Metric("Ahead", 582, 324, 254, 56, "GAP AHEAD", "=AheadName == \"---\" ? \"--\" : format(AheadGap, \"0.0\")", "layout", "Label left", "valueSize", "26", "valueColor", "good");
            b.Metric("Behind", 582, 384, 254, 56, "GAP BEHIND", "=BehindName == \"---\" ? \"--\" : format(BehindGap, \"0.0\")", "layout", "Label left", "valueSize", "26", "valueColor", "warn");
            b.Add("blindspot", "Car left", 2, 80, 6, 320, "side", "Left", "glow", "12");
            b.Add("blindspot", "Car right", 846, 80, 6, 320, "side", "Right", "glow", "12");
            Notice(b, 300, 2, 254, 22);

            PitLane(b, "Liquid");
            return b.Layout;
        }
    }
}
