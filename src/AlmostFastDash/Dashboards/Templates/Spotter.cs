namespace AlmostFastDash.Dashboards
{
    internal static partial class DesignTemplates
    {
        // ------------------------------------------------------------------ Spotter

        /// <summary>
        /// Ovals: blind-spot bars down both edges, two rev bars meeting in the middle, the gear and speed,
        /// the relative on the left and the race order around you on the right, lap speed, fuel to the end
        /// and laps to go along the bottom.
        /// </summary>
        private static DashLayout Spotter()
        {
            var b = new Builder(t =>
            {
                t.Background = "#050506";
                t.Background2 = "#1C1408";
                t.BackgroundStyle = "Vignette";
                t.Track = "#1D1A14";
                t.Panel = "#B0141210";
                t.PanelTop = "#C01C1914";
                t.Border = "#2E2716";
            });
            b.Screen("Race");
            b.Add("blindspot", "Car left", 4, 16, 20, 448, "side", "Left", "shape", "Zones", "glow", "18");
            b.Add("blindspot", "Car right", 830, 16, 20, 448, "side", "Right", "shape", "Zones", "glow", "18");
            foreach (bool right in new[] { false, true })
                b.Add("bar", right ? "Revs right" : "Revs left", right ? 431 : 36, 14, 387, 34, "value", "=RevPct", "mode", "Segments",
                    "segments", "16", "taper", "70",
                    "radius", "2", "colorMode", "Shift colors", "warnAt", "0.6", "dangerAt", "0.85", "flash", "=ShiftFlash",
                    "flashColor", "bad", "glow", "5", "flipX", right ? "true" : "false");

            b.Panel("Relative panel", 36, 62, 252, 290, "style", "Soft", "radius", "16");
            b.Label("Relative label", 52, 74, 200, "ON TRACK");
            b.Add("relative", "Relative", 42, 92, 240, 254, "rows", "5", "size", "15");

            b.Add("gear", "Gear", 312, 56, 230, 210, "size", "190", "neighbors", "false", "glow", "10", "fxShadow", "12", "fxShadowY", "6");
            b.Text("Speed", 327, 262, 200, 54, "=format(Speed, \"0\")", "size", "46");
            b.Text("Speed unit", 327, 314, 200, 16, "=SpeedUnit", "size", "12", "tracking", "3", "weight", "Regular", "color", "muted");

            b.Panel("Order panel", 566, 62, 252, 290, "style", "Soft", "radius", "16");
            b.Label("Order label", 582, 74, 200, "RACE ORDER");
            b.Add("standings", "Race order", 572, 92, 240, 254, "rows", "5", "focus", "Around me", "column", "Interval",
                "showNumber", "false", "size", "15", "rowColor", "#70181510");

            const double y = 366, h = 100;
            b.Tile("Position", 36, y, 150, h, "POSITION", PositionText, "Soft", "valueSize", "34",
                "unit", "=Starters > 0 ? \"/\" + Starters : \"\"", "unitColor", "muted");
            b.Tile("To go", 194, y, 150, h, "=SessionType", "=RemainingText", "Soft", "valueSize", "24");
            b.Tile("Lap speed", 352, y, 150, h, "LAP SPEED", "=format(LastLapSpeed, \"0.0\")", "Soft", "valueSize", "30", "unit",
                "=SpeedUnit");
            b.Tile("Refuel", 510, y, 150, h, FuelToAddLabel, FuelToAdd, "Soft", "valueSize", "28", "unit", FuelToAddUnit,
                "valueColor", FuelToAddTone);
            b.Tile("Last lap", 668, y, 150, h, "LAST LAP", "=laptime(LastLap)", "Soft", "valueSize", "24", "valueColor",
                "=LastLapIsBest ? $purple : $text");
            b.Panel("Caution banner", 312, 322, 230, 36, "style", "Tinted", "tint", "warn", "radius", "18", "visible", "Caution");
            b.Text("Caution", 312, 322, 230, 36, "CAUTION", "size", "18", "tracking", "4", "color", "warn", "visible", "Caution");
            Notice(b, 312, 322, 230, 36);

            PitLane(b, "Soft");
            return b.Layout;
        }
    }
}
