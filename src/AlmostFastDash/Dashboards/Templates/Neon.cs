namespace AlmostFastDash.Dashboards
{
    internal static partial class DesignTemplates
    {
        // ------------------------------------------------------------------ Neon

        /// <summary>
        /// A neon cockpit. A rev dial runs cyan to violet to magenta round a violet glass face, its scale lit as
        /// the revs pass it, with glowing speed in the middle and the gear in a neon frame below. Glow behind
        /// the dial warms toward magenta as the revs climb. Two tall glass wings over the aurora hold the lap
        /// (left) and the fuel and the race (right). A shift horizon along the bottom closes in from both
        /// edges.
        /// </summary>
        private static DashLayout Neon()
        {
            var b = new Builder(t =>
            {
                t.Background = "#06021A";
                t.Background2 = "#1E0B44";
                t.BackgroundStyle = "Aurora";
                t.Panel = "#9A1A0E3C";
                t.PanelTop = "#A8241452";
                t.Border = "#3A2470";
                t.Track = "#1A1036";
                t.Text = "#F7F3FF";
                t.Muted = "#C2B6EC";
                t.Dim = "#5C4D8E";
                t.Info = "#22D3EE";
                t.Purple = "#F0ABFC";
                t.Bad = "#FF3D7F";
                t.Warn = "#FFB547";
                t.Good = "#3CF2A0";
            });
            b.Screen("Race");
            const string top = "ceil(Redline / 1000)";

            // Light behind the dial that warms from violet to magenta as the revs climb, and flares at the shift.
            b.Add("gradient", "Shift glow", 187, -40, 480, 480, "style", "Radial", "strength", "20",
                "color", "=ShiftFlash ? $bad : mix($accent, $bad, round(clamp((RevPct - 0.55) / 0.45, 0, 1) * 8) / 8)");
            b.Add("shape", "Dial face", 277, 50, 300, 300, "shape", "Ellipse", "fill", "#120A2C", "stroke", "#2E1D5C", "strokeWidth", "1",
                "fxFill", "Gradient", "fxFillColor", "#1C0F42", "fxFillColor2", "#07031A", "fxFillAngle", "90", "fxFillAmount", "100");
            b.Add("arc", "Rev dial", 247, 20, 360, 360, "value", "=Rpm / 1000", "max", "=Redline > 0 ? " + top + " : 8", "style", "Smooth",
                "start", "135", "sweep", "270", "thickness", "9", "colorMode", "Gradient", "color", "info", "warnColor", "#B47CFF", "dangerColor", "bad",
                "warnAt", "=ShiftRpm > 0 && Redline > 0 ? ShiftRpm * 0.8 / (" + top + " * 1000) : 0.65",
                "dangerAt", "=ShiftRpm > 0 && Redline > 0 ? ShiftRpm / (" + top + " * 1000) : 0.9",
                "zones", "true", "track", "#170D33", "rim", "#33226A", "rimWidth", "1",
                "ticks", "=Redline > 0 ? " + top + " : 8", "minorTicks", "4", "tickLength", "10", "tickColor", "#8C7CC8", "litScale", "true",
                "numbers", "true", "numbersTo", "=Redline > 0 ? " + top + " : 8", "numberSize", "17", "numberColor", "text",
                "needle", "Marker", "needleColor", "#FFFFFF", "needleLength", "130", "needleWidth", "3", "glow", "14", "flash", "=ShiftFlash", "flashColor", "bad");
            b.Text("Rpm", 302, 98, 250, 22, "=format(Rpm, \"0\") + \"  RPM\"", "size", "15", "tracking", "2", "color", "info", "fxGlow", "6", "fxGlowColor", "info");
            b.Text("Speed", 302, 122, 250, 108, "=format(Speed, \"0\")", "size", "100", "fxGlow", "12", "fxGlowColor", "=alpha($accent, 0.65)",
                "fxFill", "Gradient", "fxFillColor", "#FFFFFF", "fxFillColor2", "#DCC8FF", "fxFillAngle", "90");
            b.Text("Speed unit", 302, 230, 250, 18, "=SpeedUnit", "size", "13", "tracking", "5", "weight", "Regular", "color", "muted");
            b.Panel("Gear frame", 387, 300, 80, 64, "style", "Outline", "border", "accent", "borderWidth", "2", "radius", "16", "glow", "14", "glowColor", "=alpha($accent, 0.55)",
                "fill", "#40160A30");
            b.Add("gear", "Gear", 387, 300, 80, 64, "size", "46", "neighbors", "false", "color", "text", "flashColor", "bad");

            // Left wing: the lap.
            b.Panel("Lap wing", 16, 26, 226, 410, "style", "Liquid", "radius", "24");
            b.Label("Lap label", 36, 42, 186, "LAP");
            b.Text("Lap", 36, 56, 186, 44, "=LapText", "size", "36", "align", "Left");
            b.Text("To go", 36, 100, 186, 18, "=RemainingText", "size", "12", "tracking", "1.5", "weight", "Regular", "align", "Left", "color", "muted");
            b.Add("shape", "Lap rule 1", 36, 126, 186, 2, "shape", "Line", "stroke", "#2C1D58", "strokeWidth", "1");
            b.Label("Delta label", 36, 138, 186, "DELTA");
            b.Text("Delta", 36, 152, 186, 42, "=signed(Delta, 2)", "size", "34", "align", "Left", "color", "=deltacolor(Delta)");
            b.Add("bar", "Delta bar", 36, 198, 186, 5, "value", "=Delta", "min", "-1", "max", "1", "mode", "Center", "color", "good", "negColor", "bad", "track", "#1D1242", "glow", "6");
            b.Add("shape", "Lap rule 2", 36, 218, 186, 2, "shape", "Line", "stroke", "#2C1D58", "strokeWidth", "1");
            b.Label("Last label", 36, 230, 186, "LAST LAP");
            b.Text("Last lap", 36, 244, 186, 32, "=laptime(LastLap)", "size", "26", "align", "Left", "color", "=LastLapIsBest ? $purple : $text");
            b.Label("Best label", 36, 284, 186, "BEST LAP");
            b.Text("Best lap", 36, 298, 186, 32, "=laptime(BestLap)", "size", "26", "align", "Left", "color", "purple");
            b.Add("shape", "Lap rule 3", 36, 342, 186, 2, "shape", "Line", "stroke", "#2C1D58", "strokeWidth", "1");
            string[] car = { "TC", "ABS", "BIAS" };
            string[] carValues = { "=TractionControl < 0 ? \"--\" : format(TractionControl, \"0\")", "=Abs < 0 ? \"--\" : format(Abs, \"0\")", "=format(BrakeBias, \"0.0\")" };
            for (int i = 0; i < 3; i++)
                b.Metric(car[i], 26 + i * 69, 352, 69, 74, car[i], carValues[i], "align", "Center", "valueSize", "20", "labelSize", "11", "labelTracking", "2",
                    "valueColor", i == 1 ? "=AbsActive ? $accent : $text" : "text");

            // Right wing: the fuel and the race, set to the right as the left wing is set to the left.
            b.Panel("Race wing", 612, 26, 226, 410, "style", "Liquid", "radius", "24");
            b.Label("Position label", 632, 42, 186, "POSITION", "Right");
            b.Metric("Position", 628, 56, 194, 44, "", PositionText, "layout", "Value only", "align", "Right", "inset", "4", "valueSize", "36",
                "unit", "=Starters > 0 ? \"/\" + Starters : \"\"", "unitColor", "muted");
            b.Text("Gained", 632, 100, 186, 18,
                "=StartPosition <= 0 ? \"\" : PositionsGained > 0 ? \"+\" + PositionsGained + \" FROM P\" + StartPosition : PositionsGained < 0 ? PositionsGained + \" FROM P\" + StartPosition : \"STARTED P\" + StartPosition",
                "size", "12", "tracking", "1.5", "weight", "Regular", "align", "Right", "color", "=PositionsGained > 0 ? $good : PositionsGained < 0 ? $bad : $muted");
            b.Add("shape", "Race rule 1", 632, 126, 186, 2, "shape", "Line", "stroke", "#2C1D58", "strokeWidth", "1");
            b.Label("Fuel label", 632, 138, 186, "FUEL", "Right");
            b.Metric("Fuel", 628, 152, 194, 42, "", "=fuel(Fuel)", "layout", "Value only", "align", "Right", "inset", "4", "valueSize", "34", "unit", "=FuelUnit");
            b.Add("bar", "Fuel level", 632, 198, 186, 5, "value", "=FuelPct", "fill", FillOrder.EndToStart, "track", "#1D1242", "glow", "6",
                "color", "=FuelLapsLeft >= 0 && FuelLapsLeft < 2 ? $bad : $info");
            b.Add("shape", "Race rule 2", 632, 218, 186, 2, "shape", "Line", "stroke", "#2C1D58", "strokeWidth", "1");
            b.Label("Fuel laps label", 632, 230, 186, "LAPS OF FUEL", "Right");
            b.Text("Fuel laps", 632, 244, 186, 32, FuelLaps, "size", "26", "align", "Right", "color", FuelTone);
            b.Text("Refuel label", 632, 284, 186, 14, FuelToAddLabel, "size", "11", "tracking", "2.2", "weight", "Regular", "color", "muted", "align", "Right");
            b.Metric("Refuel", 628, 296, 194, 36, "", FuelToAdd, "layout", "Value only", "align", "Right", "inset", "4", "valueSize", "26",
                "unit", FuelToAddUnit, "valueColor", FuelToAddTone);
            b.Add("shape", "Race rule 3", 632, 342, 186, 2, "shape", "Line", "stroke", "#2C1D58", "strokeWidth", "1");
            b.Metric("Ahead", 628, 352, 194, 36, "AHEAD", "=AheadName == \"---\" ? \"--\" : \"-\" + format(AheadGap, \"0.0\")", "layout", "Label left", "inset", "4",
                "valueSize", "22", "labelSize", "11", "labelTracking", "2", "valueColor", "good");
            b.Metric("Behind", 628, 392, 194, 36, "BEHIND", "=BehindName == \"---\" ? \"--\" : \"+\" + format(BehindGap, \"0.0\")", "layout", "Label left", "inset", "4",
                "valueSize", "22", "labelSize", "11", "labelTracking", "2", "valueColor", "warn");

            // The shift horizon: LEDs along the bottom closing in from both edges to the shift point.
            b.Add("bar", "Shift horizon", 40, 448, 774, 20, "value", "=RevPct", "mode", "Segments", "segments", "40", "fill", FillOrder.EdgesToCenter,
                "bulge", "-55", "radius", "2", "colorMode", "Gradient", "color", "info", "warnColor", "#B47CFF", "dangerColor", "bad", "warnAt", "0.55", "dangerAt", "0.9",
                "track", "#160C30", "flash", "=ShiftFlash", "flashColor", "bad", "glow", "8");

            b.Add("blindspot", "Car left", 4, 90, 6, 280, "side", "Left", "color", "bad", "offColor", "#00000000", "glow", "14");
            b.Add("blindspot", "Car right", 844, 90, 6, 280, "side", "Right", "color", "bad", "offColor", "#00000000", "glow", "14");
            Notice(b, 322, 258, 210, 32);

            PitLane(b, "Liquid");
            return b.Layout;
        }
    }
}
