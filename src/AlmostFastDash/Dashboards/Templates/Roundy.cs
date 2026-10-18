namespace AlmostFastDash.Dashboards
{
    internal static partial class DesignTemplates
    {
        // ------------------------------------------------------------------ Roundy

        /// <summary>
        /// The oval flagship, after the round clusters of the Mini and Bentley. Curved blind-spot arcs hug the
        /// edges. A numbered rev dial holds the gear, the speed and the lap speed, flanked by curved fuel and
        /// delta gauges. Position and the cars either side are on the left, and lap falloff over the stint is
        /// on the right. One glass strip carries laps to go, the last lap, fuel to the end and the leader.
        /// </summary>
        private static DashLayout Roundy()
        {
            var b = new Builder(t =>
            {
                t.Background = "#03060B";
                t.Background2 = "#0A1A30";
                t.BackgroundStyle = "Bloom";
                t.Panel = "#A00A111C";
                t.PanelTop = "#B0101A28";
                t.Border = "#1C2A3C";
                t.Track = "#121B27";
            });
            b.Screen("Race");
            // Blind-spot arcs hug both edges, well clear of everything else.
            b.Add("blindspot", "Car left", 8, 24, 26, 432, "side", "Left", "bend", "60", "thickness", "34", "color", "#FF3B30", "offColor", "#141E2A", "glow", "12");
            b.Add("blindspot", "Car right", 820, 24, 26, 432, "side", "Right", "bend", "60", "thickness", "34", "color", "#FF3B30", "offColor", "#141E2A", "glow", "12", "flipX", "true");

            // The dial: revs round a numbered scale, the gear in a ring above the speed.
            b.Add("gradient", "Dial light", 247, 0, 360, 360, "style", "Radial", "color", "accent", "strength", "14");
            // The dial's face: a disc a shade lighter than the screen, darkening toward the bottom like glass under a light.
            b.Add("shape", "Dial face", 282, 34, 290, 290, "shape", "Ellipse", "fill", "#0B1525", "stroke", "#172539", "strokeWidth", "1",
                "fxFill", "Gradient", "fxFillColor", "#0F1D31", "fxFillColor2", "#05090F", "fxFillAngle", "90", "fxFillAmount", "100");
            b.Add("arc", "Rev dial", 262, 14, 330, 330, "value", "=Rpm / 1000", "max", RpmTop, "style", "Smooth", "start", "135", "sweep", "270", "thickness", "10",
                "colorMode", "Shift colors", "color", "accent", "warnColor", "warn", "dangerColor", "bad",
                "warnAt", "=ShiftRpm > 0 && Redline > 0 ? ShiftRpm * 0.93 / (ceil(Redline / 1000) * 1000) : 0.8",
                "dangerAt", "=ShiftRpm > 0 && Redline > 0 ? ShiftRpm / (ceil(Redline / 1000) * 1000) : 0.9",
                "zones", "true", "track", "#101A27", "rim", "#1E2D44", "rimWidth", "1",
                "ticks", RpmTop, "minorTicks", "4", "tickLength", "9", "tickColor", "#6D84A6", "litScale", "true",
                "numbers", "true", "numbersTo", RpmTop, "numberSize", "17", "numberColor", "#DCE6F2",
                "needle", "Marker", "needleColor", "#FFFFFF", "needleLength", "130", "needleWidth", "3", "glow", "8", "flash", "=ShiftFlash");
            b.Add("shape", "Gear ring", 402, 86, 50, 50, "shape", "Ellipse", "fill", "#401E6FD0", "stroke", "accent", "strokeWidth", "2", "glow", "8");
            b.Add("gear", "Gear", 402, 86, 50, 50, "size", "28", "neighbors", "false");
            b.Text("Speed", 317, 136, 220, 86, "=format(Speed, \"0\")", "size", "90", "fxShadow", "10", "fxShadowY", "4");
            b.Text("Speed unit", 317, 220, 220, 16, "=SpeedUnit", "size", "12", "tracking", "4", "weight", "Regular", "color", "muted");
            b.Text("Lap speed", 317, 244, 220, 18, "=LastLapSpeed > 0 ? \"LAP \" + format(LastLapSpeed, \"0.0\") : \"\"", "size", "14", "tracking", "2", "color", "info");
            b.Text("Lap", 337, 300, 180, 30, "=\"LAP \" + LapText", "size", "18", "tracking", "3", "color", "text", "visible", "Notice == \"\" && !Caution");

            // Curved gauges either side of the dial: fuel on the left, the live delta on the right.
            b.Add("bar", "Fuel bar", 226, 70, 18, 220, "value", "=FuelPct", "orientation", "Vertical", "bend", "70", "radius", "-1",
                "color", "=FuelLapsLeft >= 0 && FuelLapsLeft < 2 ? $bad : #D7E6F7", "track", "#121C29");
            b.Label("Fuel bar label", 196, 296, 80, "FUEL", "Center");
            b.Add("bar", "Delta bar", 610, 70, 18, 220, "value", "=Delta", "min", "-1", "max", "1", "mode", "Center", "orientation", "Vertical", "bend", "-70", "radius", "-1",
                "color", "good", "negColor", "bad", "track", "#121C29");
            b.Label("Delta bar label", 579, 296, 80, "DELTA", "Center");

            // Left: where you are, and the cars either side of you in the order.
            b.Label("Position label", 70, 26, 160, "POSITION");
            b.Metric("Position", 66, 38, 158, 54, "", PositionText, "layout", "Value only", "inset", "4", "valueSize", "44", "unit", "=Starters > 0 ? \"/\" + Starters : \"\"", "unitColor", "muted");
            b.Text("Gained", 70, 92, 160, 18,
                "=StartPosition <= 0 ? \"\" : PositionsGained > 0 ? \"+\" + PositionsGained + \" FROM P\" + StartPosition : PositionsGained < 0 ? PositionsGained + \" FROM P\" + StartPosition : \"STARTED P\" + StartPosition",
                "size", "12", "weight", "Regular", "tracking", "1", "align", "Left", "color", "=PositionsGained > 0 ? $good : PositionsGained < 0 ? $bad : $muted");
            b.Add("shape", "Left rule 1", 70, 124, 140, 2, "shape", "Line", "stroke", "#1C2A3C", "strokeWidth", "1");
            b.Label("Ahead label", 70, 138, 160, "AHEAD");
            b.Text("Ahead gap", 70, 152, 160, 38, "=AheadName == \"---\" ? \"--\" : \"-\" + format(AheadGap, \"0.0\")", "size", "32", "align", "Left", "color", "good");
            b.Text("Ahead name", 70, 190, 160, 18, "=AheadName == \"---\" ? \"\" : AheadName", "size", "13", "weight", "Regular", "align", "Left", "color", "muted");
            b.Add("shape", "Left rule 2", 70, 222, 140, 2, "shape", "Line", "stroke", "#1C2A3C", "strokeWidth", "1");
            b.Label("Behind label", 70, 236, 160, "BEHIND");
            b.Text("Behind gap", 70, 250, 160, 38, "=BehindName == \"---\" ? \"--\" : \"+\" + format(BehindGap, \"0.0\")", "size", "32", "align", "Left", "color", "warn");
            b.Text("Behind name", 70, 288, 160, 18, "=BehindName == \"---\" ? \"\" : BehindName", "size", "13", "weight", "Regular", "align", "Left", "color", "muted");

            // Right: lap falloff over the stint.
            b.Label("Falloff label", 624, 26, 160, "LAP FALLOFF", "Right");
            b.Text("Falloff", 624, 38, 160, 50, "=Falloff < 0 ? \"--\" : \"+\" + format(Falloff, \"0.00\")", "size", "40", "align", "Right",
                "color", "=Falloff < 0 ? $text : Falloff < 0.15 ? $good : Falloff < 0.4 ? $warn : $bad");
            b.Text("Falloff trend", 624, 92, 160, 18, "=StintGreenLaps < 3 ? \"\" : signed(FalloffPerLap, 2) + \" PER LAP\"", "size", "12", "weight", "Regular", "tracking", "1",
                "align", "Right", "color", "muted");
            b.Add("laphistory", "Falloff chart", 632, 130, 152, 150, "source", "This stint", "laps", "8", "style", "Columns", "range", "0", "showDelta", "false",
                "gap", "30", "radius", "3", "color", "#2F7FE0", "slowColor", "#A8DBFF", "track", "#0C141E");
            b.Text("Stint best", 624, 288, 160, 18, "=StintBestLap > 0 ? \"BEST \" + laptime(StintBestLap) : \"BEST --\"", "size", "12", "tracking", "1",
                "weight", "Regular", "align", "Right", "color", "purple");

            // Along the bottom: one glass strip with the race, the last lap, fuel and the leader.
            b.Panel("Strip", 66, 356, 722, 110, "style", "Liquid", "radius", "20");
            string[] names = { "SessionType", "LAST LAP", "FUEL TO END", "TO THE LEADER" };
            string[] labels = { "=SessionType", "LAST LAP", FuelToAddLabel, "TO THE LEADER" };
            string[] values = { "=RemainingText", "=laptime(LastLap)", FuelToAdd, "=GapToLeader == \"\" ? \"--\" : GapToLeader" };
            string[] tones = { "text", "=LastLapIsBest ? $purple : $text", FuelToAddTone, "text" };
            for (int i = 0; i < 4; i++)
            {
                double x = 66 + i * 180.5;
                DashWidget value = b.Metric(names[i], x, 364, 180.5, 76, labels[i], values[i], "align", "Center", "valueSize", "26", "labelSize", "11", "labelTracking", "2.5", "valueColor", tones[i]);
                if (i == 2) value.Set("unit", FuelToAddUnit);
                if (i > 0) b.Add("shape", "Strip rule " + i, x, 378, 2, 50, "shape", "Line", "stroke", "#22324A", "strokeWidth", "1");
            }
            b.Add("progress", "Race progress", 88, 448, 678, 5, "gap", "2");

            b.Panel("Caution banner", 342, 298, 170, 36, "style", "Tinted", "tint", "warn", "radius", "18", "visible", "Caution");
            b.Text("Caution", 342, 298, 170, 36, "CAUTION", "size", "18", "tracking", "4", "color", "warn", "visible", "Caution");
            Notice(b, 322, 298, 210, 36);

            // Page 2 on a wheel button: the race order and the stint.
            b.Screen("Race order");
            b.Panel("Order panel", 14, 14, 472, 452, "style", "Liquid", "radius", "18");
            b.Label("Order label", 32, 28, 300, "RACE ORDER");
            b.Add("standings", "Leaderboard", 24, 48, 452, 408, "rows", "11", "focus", "Top and me", "column", "Gap to leader", "showIRating", "true", "size", "16",
                "rowColor", "#80101826", "radius", "8");
            b.Panel("Stint panel", 496, 14, 344, 452, "style", "Liquid", "radius", "18");
            b.Label("Stint label", 514, 28, 300, "THIS STINT");
            b.Metric("Stint laps", 504, 44, 164, 70, "GREEN LAPS", "=StintGreenLaps", "valueSize", "28", "labelSize", "11", "labelTracking", "2");
            b.Metric("Stint best", 668, 44, 164, 70, "STINT BEST", "=StintBestLap > 0 ? laptime(StintBestLap) : \"--\"", "align", "Right", "valueSize", "24",
                "labelSize", "11", "labelTracking", "2", "valueColor", "purple");
            b.Add("laphistory", "Stint laps", 514, 126, 308, 210, "source", "This stint", "laps", "16", "style", "Columns", "showDelta", "true", "labelSize", "11",
                "gap", "34", "color", "#2F7FE0", "slowColor", "#A8DBFF", "track", "#0E1621");
            b.Metric("Falloff", 504, 350, 164, 70, "FALLOFF", "=Falloff < 0 ? \"--\" : \"+\" + format(Falloff, \"0.00\")", "valueSize", "26", "labelSize", "11", "labelTracking", "2",
                "valueColor", "=Falloff < 0 ? $text : Falloff < 0.15 ? $good : Falloff < 0.4 ? $warn : $bad");
            b.Metric("Trend", 668, 350, 164, 70, "PER LAP", "=StintGreenLaps < 3 ? \"--\" : signed(FalloffPerLap, 2)", "align", "Right", "valueSize", "26", "labelSize", "11", "labelTracking", "2");
            b.Text("Average", 514, 428, 308, 22, "=AverageLap > 0 ? \"AVERAGE LAP \" + laptime(AverageLap) : \"\"", "size", "12", "tracking", "1.5", "weight", "Regular", "align", "Left", "color", "muted");

            PitLane(b, "Liquid");
            TouchViews.Roundy(b.Layout);
            return b.Layout;
        }
    }
}
