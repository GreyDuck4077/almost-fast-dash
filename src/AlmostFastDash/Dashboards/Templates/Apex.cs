namespace AlmostFastDash.Dashboards
{
    internal static partial class DesignTemplates
    {
        // ------------------------------------------------------------------ Apex

        /// <summary>
        /// Road and GT, after the BMW digital cluster: an arched row of rev LEDs across the top filling
        /// from both ends, speed and gear inside a rev ring, the track map on the left and a live speed
        /// graph on the right, and five timing tiles along the bottom. A second page shows the race order.
        /// </summary>
        private static DashLayout Apex()
        {
            var b = new Builder(t =>
            {
                t.Background = "#03060B";
                t.Background2 = "#0B1A33";
                t.BackgroundStyle = "Bloom";
            });
            b.Screen("Race");
            b.Add("bar", "Rev LEDs", 70, 8, 714, 50, "value", "=RevPct", "mode", "Segments", "segments", "34", "bulge", "70",
                "grow", "Bottom edge", "radius", "3",
                "fill", FillOrder.EdgesToCenter, "colorMode", "Gradient", "warnAt", "0.55", "dangerAt", "0.85", "track", "#0F1724",
                "flash", "=ShiftFlash", "flashColor", "bad", "glow", "6");

            b.Panel("Map panel", 14, 70, 268, 254, "style", "Liquid", "radius", "20");
            b.Label("Map label", 32, 84, 200, "TRACK");
            b.Add("trackmap", "Track map", 26, 100, 244, 214, "lineWidth", "6", "carSize", "9");

            b.Panel("Graph panel", 572, 70, 268, 254, "style", "Liquid", "radius", "20");
            b.Label("Graph label", 590, 84, 200, "SPEED · LAST 30 S");
            b.Text("Top speed", 590, 98, 230, 26, "=\"TOP \" + format(LapMaxSpeed, \"0\") + \"  ·  LOW \" + format(LapMinSpeed, \"0\")",
                "size", "15", "align", "Left", "color", "muted");
            b.Add("graph", "Speed graph", 584, 130, 244, 182, "value", "=Speed", "min", "0", "max", SpeedTop, "seconds", "30",
                "style", "Area", "lineWidth", "2.5", "gridColor", "#1A2433");

            b.Add("arc", "Rev ring", 302, 70, 250, 250, "value", "=RevPct", "style", "Smooth", "start", "135", "sweep", "270",
                "thickness", "9",
                "colorMode", "Gradient", "warnAt", "0.55", "dangerAt", "0.85", "zones", "true", "track", "#121A26", "flash", "=ShiftFlash",
                "ticks", "9", "minorTicks", "1", "tickLength", "7", "tickColor", "dim", "glow", "12");
            b.Text("Speed", 327, 122, 200, 88, "=format(Speed, \"0\")", "size", "84", "fxShadow", "10", "fxShadowY", "4");
            b.Text("Speed unit", 327, 206, 200, 16, "=SpeedUnit", "size", "12", "tracking", "3", "weight", "Regular", "color", "muted");
            b.Panel("Gear pill", 384, 234, 86, 62, "style", "Tinted", "tint", "accent", "radius", "18");
            b.Add("gear", "Gear", 384, 234, 86, 62, "size", "48", "neighbors", "false", "glow", "8");

            const double tileY = 340, tileH = 128, tileW = 158;
            double X(int i) => 14 + i * (tileW + 9.5);
            b.Tile("Position", X(0), tileY, tileW, tileH, "POSITION", PositionText, "Liquid", "unit",
                "=Starters > 0 ? \"/\" + Starters : \"\"", "unitColor", "muted");
            b.Text("Lap", X(0) + 16, tileY + 96, tileW - 32, 18, "=\"LAP \" + LapText", "size", "13", "align", "Left", "color", "muted");
            b.Tile("Last lap", X(1), tileY, tileW, tileH, "LAST LAP", "=laptime(LastLap)", "Liquid", "valueSize", "27", "valueColor",
                "=LastLapIsBest ? $purple : $text");
            b.Text("Best lap", X(1) + 16, tileY + 96, tileW - 32, 18, "=\"BEST \" + laptime(BestLap)", "size", "13", "align", "Left",
                "color", "purple");
            b.Tile("Delta", X(2), tileY, tileW, tileH, "DELTA", "=signed(Delta, 2)", "Liquid", "valueColor", "=deltacolor(Delta)");
            b.Add("bar", "Delta bar", X(2) + 16, tileY + 102, tileW - 32, 6, "value", "=Delta", "min", "-1", "max", "1", "mode", "Center",
                "color", "good", "negColor", "bad", "glow", "4");
            b.Tile("Fuel", X(3), tileY, tileW, tileH, "FUEL", "=fuel(Fuel)", "Liquid", "unit", "=FuelUnit");
            b.Text("Fuel laps", X(3) + 16, tileY + 96, tileW - 32, 18,
                "=FuelLapsLeft < 0 ? \"-- LAPS\" : format(FuelLapsLeft, \"0.0\") + \" LAPS\"", "size", "13", "align", "Left",
                "color", FuelTone);
            b.Panel("Gaps tile", X(4), tileY, tileW, tileH, "style", "Liquid", "radius", "16");
            b.Metric("Ahead", X(4), tileY + 4, tileW, 60, "AHEAD", "=AheadName == \"---\" ? \"--\" : \"-\" + format(AheadGap, \"0.0\")",
                "inset", "16", "layout", "Label left",
                "valueSize", "24", "labelSize", "11", "labelTracking", "2", "valueColor", "good");
            b.Metric("Behind", X(4), tileY + 64, tileW, 60, "BEHIND",
                "=BehindName == \"---\" ? \"--\" : \"+\" + format(BehindGap, \"0.0\")", "inset", "16", "layout", "Label left",
                "valueSize", "24", "labelSize", "11", "labelTracking", "2", "valueColor", "warn");
            b.Add("blindspot", "Car left", 2, 120, 6, 200, "side", "Left", "glow", "12");
            b.Add("blindspot", "Car right", 846, 120, 6, 200, "side", "Right", "glow", "12");
            Notice(b, 287, 300, 280, 34);

            // Page 2 on a wheel button: the race order and the map.
            b.Screen("Standings");
            b.Panel("Standings panel", 14, 14, 528, 452, "style", "Liquid", "radius", "20");
            b.Label("Standings label", 32, 28, 300, "RACE ORDER");
            b.Add("standings", "Leaderboard", 24, 48, 508, 408, "rows", "11", "focus", "Top and me", "showIRating", "true", "size", "16",
                "rowColor", "#80101826", "radius", "8");
            b.Panel("Map panel 2", 552, 14, 288, 288, "style", "Liquid", "radius", "20");
            b.Add("trackmap", "Track map 2", 562, 24, 268, 268, "lineWidth", "7", "carSize", "11", "numbers", "true");
            b.Tile("Gap to leader", 552, 312, 288, 74, "TO THE LEADER", "=GapToLeader == \"\" ? \"--\" : GapToLeader", "Liquid", "layout",
                "Label left", "valueSize", "24");
            b.Tile("Session best", 552, 394, 288, 72, "SESSION BEST", "=laptime(SessionBestLap)", "Liquid", "layout", "Label left",
                "valueSize", "24", "valueColor", "purple");

            PitLane(b, "Liquid");
            return b.Layout;
        }
    }
}
