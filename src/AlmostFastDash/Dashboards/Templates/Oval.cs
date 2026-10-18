namespace AlmostFastDash.Dashboards
{
    internal static partial class DesignTemplates
    {
        // ------------------------------------------------------------------ Oval

        /// <summary>
        /// Pack racing, after the top views of modern driver-assist screens: a live radar fills the middle, with
        /// every car near you drawn where it is and sensor waves that run up to the car in line ahead and behind.
        /// The gaps to those two cars are on the left, above and below your speed, in car lengths and how fast
        /// they're closing. Laps to go and position, the last lap and lap speed, and fuel are on the right (lap
        /// and fuel have a second view on a tap). A wheel button pages to a zoomed-out radar with the cars
        /// around you and the race order.
        /// </summary>
        private static DashLayout OvalRadar()
        {
            var b = new Builder(t =>
            {
                t.Background = "#020814";
                t.Background2 = "#0A2152";
                t.BackgroundStyle = "Bloom";
                t.Panel = "#A0081530";
                t.PanelTop = "#B00E1F42";
                t.Border = "#1B3463";
                t.Track = "#0D1B33";
                t.Muted = "#B6C5DD";
                t.Dim = "#3B4E6E";
            });
            b.Screen("Race");
            b.Add("gradient", "Radar light", 207, 40, 440, 400, "style", "Radial", "color", "#2B63C9", "strength", "16");
            b.Add("radar", "Radar", 246, 0, 362, 480, "range", "3");

            // Left: the car in line ahead, your speed, the car in line behind.
            Gap(b, 14, 14, true);
            b.Text("Speed", 14, 166, 222, 88, "=format(Speed, \"0\")", "size", "86", "fxShadow", "10", "fxShadowY", "4");
            b.Text("Speed unit", 14, 252, 222, 18, "=SpeedUnit", "size", "14", "tracking", "3", "weight", "Regular", "color", "muted");
            b.Add("shiftlights", "Shift lights", 44, 282, 162, 10, "count", "12", "shape", "Pill", "lowColor", "accent",
                "midColor", "warn", "highColor", "bad", "glow", "6",
                "visible", "Notice == \"\"");
            Notice(b, 22, 276, 206, 30);
            Gap(b, 14, 326, false);

            // Right: the race, the last lap (tap for the falloff), fuel (tap for more).
            b.Panel("Race panel", 618, 14, 222, 140, "style", "Liquid", "radius", "18");
            b.Metric("To go", 620, 18, 110, 100, "=TotalLaps > 0 ? \"TO GO\" : \"LEFT\"",
                "=TotalLaps > 0 && LapsRemaining >= 0 ? format(LapsRemaining, \"0\") : RemainingText",
                "valueSize", "46", "labelSize", "14", "labelTracking", "1.5");
            b.Metric("Position", 726, 18, 110, 100, "POSITION", PositionText, "align", "Right", "valueSize", "46", "labelSize", "14",
                "labelTracking", "1.5",
                "unit", "=Starters > 0 ? \"/\" + Starters : \"\"", "unitColor", "muted", "unitSize", "16");
            b.Text("Gained", 632, 114, 194, 18,
                "=StartPosition <= 0 ? \"\" : PositionsGained > 0 ? \"+\" + PositionsGained + \" FROM P\" + StartPosition : " +
                    "PositionsGained < 0 ? PositionsGained + \" FROM P\" + StartPosition : \"STARTED P\" + StartPosition",
                "size", "14", "weight", "Regular", "tracking", "1", "align", "Right",
                "color", "=PositionsGained > 0 ? $good : PositionsGained < 0 ? $bad : $muted");
            b.Add("progress", "Race progress", 634, 140, 190, 4, "gap", "1");

            b.Panel("Lap panel", 618, 164, 222, 152, "style", "Liquid", "radius", "18", "tapName", "lap");
            b.Visible("tap(\"lap\") == 0",
                b.Text("Last lap label", 634, 174, 190, 18, "LAST LAP", "size", "14", "tracking", "1.5", "weight", "Regular",
                "align", "Left", "color", "muted"),
                b.Text("Last lap", 632, 192, 196, 46, "=laptime(LastLap)", "size", "36", "align", "Left",
                "color", "=LastLapIsBest ? $purple : $text"),
                b.Metric("Lap speed", 622, 240, 108, 64, "LAP SPEED", "=LastLapSpeed > 0 ? format(LastLapSpeed, \"0.0\") : \"--\"",
                "inset", "8", "valueSize", "22",
                    "labelSize", "14", "labelTracking", "1", "valueColor", "info"),
                b.Metric("Best lap", 726, 240, 110, 64, "BEST", "=laptime(BestLap)", "align", "Right", "inset", "10", "valueSize", "22",
                "labelSize", "14",
                    "labelTracking", "1", "valueColor", "purple"));
            b.Visible("tap(\"lap\") == 1",
                b.Text("Falloff label", 634, 174, 190, 18, "LAP FALLOFF", "size", "14", "tracking", "1.5", "weight", "Regular",
                "align", "Left", "color", "muted"),
                b.Text("Falloff", 632, 192, 112, 46, "=Falloff < 0 ? \"--\" : \"+\" + format(Falloff, \"0.00\")", "size", "34", "align",
                "Left",
                    "color", "=Falloff < 0 ? $text : Falloff < 0.15 ? $good : Falloff < 0.4 ? $warn : $bad"),
                b.Text("Falloff trend", 634, 242, 112, 18, "=StintGreenLaps < 3 ? \"\" : signed(FalloffPerLap, 2) + \" / LAP\"", "size",
                "14",
                    "weight", "Regular", "align", "Left", "color", "muted"),
                b.Text("Stint best", 634, 266, 112, 18, "=StintBestLap > 0 ? laptime(StintBestLap) : \"\"", "size", "14",
                    "weight", "Regular", "align", "Left", "color", "purple"),
                b.Add("laphistory", "Falloff chart", 750, 184, 80, 114, "source", "This stint", "laps", "8", "style", "Columns",
                "range", "0", "showDelta", "false",
                    "gap", "28", "radius", "2", "color", "#2F7FE0", "slowColor", "#A8DBFF", "track", "#0C1626"));

            b.Panel("Fuel panel", 618, 326, 222, 140, "style", "Liquid", "radius", "18", "tapName", "fuel");
            b.Visible("tap(\"fuel\") == 0",
                b.Text("Fuel label", 634, 336, 60, 18, "FUEL", "size", "14", "tracking", "1.5", "weight", "Regular", "align", "Left",
                "color", "muted"),
                b.Text("Fuel status", 680, 336, 146, 20,
                    "=(FuelToFinish < 0 ? \"MEASURING\" : FuelToFinish <= 0.05 ? \"ENOUGH\" : \"ADD \" + fuel(FuelToFinish) + \" \" + " +
                        "FuelUnit) + (FuelEstimate && FuelToFinish >= 0 ? \" · EST\" : \"\")",
                    "size", "14", "tracking", "0.5", "align", "Right",
                    "color", "=FuelToFinish < 0 ? $muted : FuelToFinish <= 0.05 ? $good : $warn"),
                b.Text("Fuel laps", 632, 356, 110, 54, FuelLaps, "size", "46", "align", "Left", "color", FuelTone),
                b.Text("Fuel laps caption", 634, 408, 120, 18, "LAPS OF FUEL", "size", "14", "tracking", "1", "weight", "Regular",
                "align", "Left", "color", "muted"),
                b.Metric("In tank", 728, 358, 108, 64, "IN TANK", "=fuel(Fuel)", "align", "Right", "inset", "10", "valueSize", "22",
                "labelSize", "14", "labelTracking", "1",
                    "unit", "=FuelUnit", "unitSize", "14"),
                b.Add("bar", "Fuel level", 634, 436, 190, 4, "value", "=FuelPct",
                "color", "=FuelLapsLeft >= 0 && FuelLapsLeft < 2 ? $bad : accent", "track", "#0F1E36"));
            b.Visible("tap(\"fuel\") == 1",
                b.Metric("Fuel per lap", 622, 332, 108, 64, "=FuelEstimate ? \"LAP · EST\" : \"PER LAP\"",
                "=FuelPerLap > 0 ? fuel(FuelPerLap) : \"--\"", "inset", "12",
                    "valueSize", "22", "labelSize", "14", "labelTracking", "1"),
                b.Metric("Fuel last lap", 726, 332, 110, 64, "LAST LAP", "=FuelLastLap > 0 ? fuel(FuelLastLap) : \"--\"", "align", "Right",
                "inset", "10", "valueSize", "22",
                    "labelSize", "14", "labelTracking", "1"),
                b.Metric("Fuel to add", 622, 394, 214, 62, "TO ADD", FuelToAdd, "inset", "12", "valueSize", "22", "labelSize", "14",
                "labelTracking", "1",
                    "unit", FuelToAddUnit, "unitSize", "14", "valueColor", FuelToAddTone));

            b.Add("blindspot", "Car left", 2, 120, 6, 240, "side", "Left", "color", "bad", "offColor", "#00000000", "glow", "14");
            b.Add("blindspot", "Car right", 846, 120, 6, 240, "side", "Right", "color", "bad", "offColor", "#00000000", "glow", "14");

            // Page 2 on a wheel button: the pack zoomed out, the cars around you and the race order.
            b.Screen("Pack");
            b.Add("gradient", "Pack light", 0, 40, 360, 400, "style", "Radial", "color", "#2B63C9", "strength", "14");
            b.Add("radar", "Pack radar", 14, 0, 330, 480, "range", "6");
            b.Panel("Around panel", 354, 14, 486, 236, "style", "Liquid", "radius", "18");
            b.Text("Around label", 372, 26, 300, 18, "AROUND YOU", "size", "14", "tracking", "1.5", "weight", "Regular", "align", "Left",
                "color", "muted");
            b.Add("relative", "Relative", 364, 50, 466, 192, "rows", "5", "size", "16");
            b.Panel("Order panel", 354, 260, 486, 206, "style", "Liquid", "radius", "18");
            b.Text("Order label", 372, 272, 300, 18, "RACE ORDER", "size", "14", "tracking", "1.5", "weight", "Regular", "align", "Left",
                "color", "muted");
            b.Add("standings", "Leaderboard", 364, 296, 466, 162, "rows", "5", "focus", "Around me", "column", "Interval", "size", "15",
                "rowColor", "#80101C30", "radius", "6");

            PitLane(b, "Liquid");
            return b.Layout;
        }

        /// <summary>
        /// The car in line ahead or behind: car lengths of clear track to it, the gap in seconds, who it is and
        /// whether the gap is closing (green when you're catching the car ahead, amber when the one behind is
        /// catching you).
        /// </summary>
        private static void Gap(Builder b, double x, double y, bool ahead)
        {
            string side = ahead ? "Ahead" : "Behind", lengths = side + "Lengths", closing = side + "Closing";
            b.Panel(side + " panel", x, y, 222, 140, "style", "Liquid", "radius", "18");
            b.Text(side + " label", x + 16, y + 12, 70, 18, ahead ? "AHEAD" : "BEHIND", "size", "14", "tracking", "1.5",
                "weight", "Regular", "align", "Left", "color", "muted");
            b.Text(side + " driver", x + 80, y + 12, 126, 18,
                "=" + side + "Name == \"---\" ? \"\" : \"#\" + " + side + "Number + \" \" + " + side + "Name",
                "size", "14", "weight", "Regular", "align", "Right", "color", "muted");
            b.Text(side + " lengths", x + 14, y + 32, 120, 62, "=" + lengths + " < 0 ? \"--\" : format(" + lengths + ", \"0.0\")", "size",
                "56", "align", "Left");
            b.Text(side + " lengths caption", x + 16, y + 92, 190, 18, "CAR LENGTHS", "size", "14", "tracking", "1.5", "weight", "Regular",
                "align", "Left", "color", "muted");
            b.Text(side + " seconds", x + 120, y + 48, 86, 30,
                "=" + side + "Name == \"---\" ? \"\" : format(" + side + "Gap, \"0.00\") + \"s\"", "size", "22", "align", "Right");
            string catching = ahead ? "$good" : "$warn", opening = ahead ? "$warn" : "$good";
            b.Text(side + " trend", x + 16, y + 114, 196, 18,
                "=" + lengths + " < 0 ? \"\" : " + closing + " > 0.3 ? \"CLOSING \" + format(" + closing +
                ", \"0.0\") + \" \" + SpeedUnit : " + closing +
                " < -0.3 ? \"OPENING \" + format(-" + closing + ", \"0.0\") + \" \" + SpeedUnit : \"HOLDING\"",
                "size", "14", "tracking", "1", "weight", "Regular", "align", "Left",
                "color", "=" + closing + " > 0.3 ? " + catching + " : " + closing + " < -0.3 ? " + opening + " : $muted");
        }
    }
}
