namespace AlmostFastDash.Dashboards
{
    internal static partial class DesignTemplates
    {
        // ------------------------------------------------------------------ Race Display

        /// <summary>
        /// A pro race display: a row of LEDs, a huge gear, timing on the left, car and fuel on the right,
        /// tyres and sectors underneath. A wheel button pages to the standings (built from numbered
        /// driver-row groups) and to an engine page.
        /// </summary>
        private static DashLayout RaceDisplay()
        {
            var b = new Builder(t =>
            {
                t.Background = "#000000";
                t.Background2 = "#000000";
                t.BackgroundStyle = "Solid";
                t.Panel = "#FF0C0F12";
                t.PanelTop = "#FF12161B";
                t.Border = "#1E252D";
                t.Track = "#14191F";
            });
            b.Screen("Race");
            b.Add("shiftlights", "LEDs", 120, 8, 614, 30, "count", "15", "shape", "Dot", "fill", FillOrder.EdgesToCenter, "glow", "12");
            b.Add("gear", "Gear", 292, 44, 270, 256, "size", "236", "neighbors", "false", "color", "text", "flashColor", "info");
            b.Text("Speed", 327, 298, 200, 48, "=format(Speed, \"0\")", "size", "40");
            b.Text("Speed unit", 327, 342, 200, 16, "=SpeedUnit", "size", "11", "tracking", "3", "weight", "Regular", "color", "muted");

            string[] leftLabels = { "LAST", "BEST", "PREDICTED" };
            string[] leftValues = { "=laptime(LastLap)", "=laptime(BestLap)", "=laptime(PredictedLap)" };
            string[] leftTones = { "=LastLapIsBest ? $purple : $text", "purple",
                "=PredictedLap > 0 && BestLap > 0 && PredictedLap <= BestLap ? $good : $text" };
            for (int i = 0; i < 3; i++)
            {
                b.Panel(leftLabels[i] + " box", 10, 52 + i * 78, 272, 70, "style", "Solid", "radius", "6", "edge", "Left",
                    "edgeColor", "accent");
                b.Metric(leftLabels[i], 10, 52 + i * 78, 272, 70, leftLabels[i], leftValues[i], "layout", "Label left", "valueSize", "30",
                    "valueColor", leftTones[i]);
            }
            b.Panel("Delta box", 10, 286, 272, 70, "style", "Solid", "radius", "6", "edge", "Left", "edgeColor", "=deltacolor(Delta)");
            b.Metric("Delta", 10, 286, 272, 56, "DELTA", "=signed(Delta, 2)", "layout", "Label left", "valueSize", "30",
                "valueColor", "=deltacolor(Delta)");
            b.Add("bar", "Delta bar", 24, 340, 244, 6, "value", "=Delta", "min", "-1", "max", "1", "mode", "Center", "color", "good",
                "negColor", "bad");

            string[] rightLabels = { "FUEL", "LAPS LEFT", "POSITION", "TC · ABS · BIAS" };
            string[] rightValues = { "=fuel(Fuel)", FuelLaps, PositionText,
                "=(TractionControl < 0 ? \"-\" : format(TractionControl, \"0\")) + \" · \" + (Abs < 0 ? \"-\" : format(Abs, \"0\")) + \" · \" + format(BrakeBias, \"0.0\")" };
            string[] rightTones = { "text", FuelTone, "text", "info" };
            for (int i = 0; i < 4; i++)
            {
                b.Panel(rightLabels[i] + " box", 572, 52 + i * 78, 272, 70, "style", "Solid", "radius", "6", "edge", "Right",
                    "edgeColor", "accent");
                b.Metric(rightLabels[i], 572, 52 + i * 78, 272, 70, rightLabels[i], rightValues[i], "layout", "Label left",
                    "valueSize", i == 3 ? "22" : "30", "valueColor", rightTones[i]);
            }
            b.Panel("Sector box", 10, 364, 534, 106, "style", "Solid", "radius", "6");
            for (int s = 1; s <= 3; s++)
            {
                string time = "sectortime(" + s + ")";
                b.Metric("S" + s, 10 + (s - 1) * 178, 368, 178, 98, "S" + s + " · LAST LAP", "=seconds(lastsectortime(" + s + "))",
                    "align", "Center", "valueSize", "26",
                    "valueColor",
                    "=lastsectortime(" + s + ") > 0 && lastsectortime(" + s + ") <= bestsectortime(" + s + ") + 0.0005 ? $purple : $text",
                    "visible", s == 3 ? "SectorCount >= 3" : "");
            }
            b.Panel("Tire box", 552, 364, 292, 106, "style", "Solid", "radius", "6");
            b.Add("tyres", "Tires", 560, 370, 276, 94, "size", "17", "showPressure", "false", "panelColor", "#FF101418");
            Notice(b, 292, 44, 270, 30);

            // Page 2: the standings as twelve numbered driver rows (one group, copied with Ctrl+D for each place).
            b.Screen("Standings");
            b.Label("Standings label", 16, 10, 300, "STANDINGS");
            b.Text("Session best", 438, 6, 400, 18, "=\"SESSION BEST \" + laptime(SessionBestLap)", "size", "13", "align", "Right",
                "color", "purple");
            for (int p = 1; p <= 12; p++)
            {
                double y = 30 + (p - 1) * 37;
                string S(string field) => "standing(Slot, \"" + field + "\")";
                DashWidget row = b.Panel("Row", 10, y, 834, 33, "style", "Solid", "radius", "5",
                    "fill", "=" + S("IsPlayer") + " ? alpha($accent, 0.25) : $panel",
                    "edge", "Left", "edgeColor", "=" + S("ClassColor"));
                DashWidget place = b.Text("Place", 22, y, 44, 33, "=" + S("Position"), "size", "18", "color",
                    "=" + S("IsPlayer") + " ? $accent : $text");
                DashWidget number = b.Text("Number", 70, y, 54, 33, "=\"#\" + " + S("Number"), "size", "15", "color", "muted");
                DashWidget name = b.Text("Name", 130, y, 330, 33, "=" + S("Name"), "size", "17", "align", "Left",
                    "color", "=" + S("InPits") + " ? $muted : $text");
                DashWidget best = b.Text("Best", 470, y, 140, 33, "=laptime(" + S("BestLap") + ")", "size", "16", "color",
                    "=" + S("BestLap") + " > 0 && abs(" + S("BestLap") + " - SessionBestLap) < 0.0005 ? $purple : $muted");
                DashWidget last = b.Text("Last", 616, y, 120, 33, "=laptime(" + S("LastLap") + ")", "size", "16", "color", "muted");
                DashWidget gap = b.Text("Gap", 740, y, 96, 33, "=" + S("GapText"), "size", "16", "align", "Right");
                b.Visible(S("Exists"), row, place, number, name, best, last, gap);
                b.Group("Driver row", p, row, place, number, name, best, last, gap);
            }

            // Page 3: the engine.
            b.Screen("Engine");
            b.Label("Engine label", 16, 10, 300, "ENGINE");
            b.Tile("Oil temp", 10, 34, 205, 110, "OIL TEMP", "=format(OilTemp, \"0\")", "Solid", "unit", "=TempUnit", "valueSize", "40");
            b.Tile("Water temp", 223, 34, 205, 110, "WATER", "=format(WaterTemp, \"0\")", "Solid", "unit", "=TempUnit", "valueSize", "40");
            b.Tile("Oil pressure", 436, 34, 205, 110, "OIL PRESSURE", "=format(OilPressure, \"0.0\")", "Solid", "unit", "=PressureUnit",
                "valueSize", "40");
            b.Tile("Voltage", 649, 34, 195, 110, "BATTERY", "=format(Voltage, \"0.0\")", "Solid", "unit", "V", "valueSize", "40");
            b.Panel("Temp graph box", 10, 154, 834, 200, "style", "Solid", "radius", "6");
            b.Label("Temp graph label", 26, 166, 400, "WATER AND OIL · LAST 2 MINUTES");
            b.Add("graph", "Temps", 22, 186, 810, 158, "value", "=WaterTemp", "value2", "=OilTemp", "seconds", "120", "style", "Line",
                "lineWidth", "2.5", "color", "info", "color2", "warn");
            b.Tile("Rpm", 10, 364, 274, 106, "RPM", "=format(Rpm, \"0\")", "Solid", "valueSize", "40");
            b.Tile("Fuel pressure", 292, 364, 274, 106, "FUEL PRESSURE", "=format(FuelPressure, \"0.0\")", "Solid", "unit",
                "=PressureUnit", "valueSize", "40");
            b.Tile("Warnings", 574, 364, 270, 106, "WARNINGS", "=EngineWarning ? \"CHECK\" : \"OK\"", "Solid", "valueSize", "40",
                "valueColor", "=EngineWarning ? $bad : $good");

            PitLane(b, "Solid");
            return b.Layout;
        }
    }
}
