using System.Collections.Generic;

namespace AlmostFastOverlays
{
    /// <summary>
    /// One-click live values for the designer's Add panel, grouped like a race engineer would look
    /// for them. Each drops in an ordinary widget already wired to its data, so it can be restyled
    /// or re-pointed like anything else.
    /// </summary>
    internal static class QuickValues
    {
        public const string Section = "Quick values";

        public static readonly string[] Folders = { "Engine", "Lap", "Sectors", "Tyres", "Fuel", "Race", "Leaderboard", "Notification", "System" };

        public static IEnumerable<DesignBlock> All()
        {
            // ---------------- Engine
            yield return Value("Engine", "Oil pressure", "OIL PRESS", "=format(OilPressure, \"0.0\")", "=PressureUnit");
            yield return Value("Engine", "Oil temperature", "OIL TEMP", "=format(OilTemp, \"0\")", "=TempUnit");
            yield return Value("Engine", "Water temperature", "WATER", "=format(WaterTemp, \"0\")", "=TempUnit");
            yield return Value("Engine", "Fuel pressure", "FUEL PRESS", "=format(FuelPressure, \"0.0\")", "=PressureUnit");
            yield return Value("Engine", "Battery voltage", "VOLTS", "=format(Voltage, \"0.0\")", "V");
            yield return Block("Engine", "Current gear", "The big gear readout.", () => new List<DashWidget> { W("gear", "Gear", 0, 0, 180, 170) });
            yield return Value("Engine", "Engine RPM", "RPM", "=format(Rpm, \"0\")");
            yield return Value("Engine", "Current speed", "SPEED", "=format(Speed, \"0\")", "=SpeedUnit");
            yield return Value("Engine", "DRS status", "DRS", "=DrsText == \"\" ? \"--\" : DrsText", "", "valueColor", "=Drs == 3 ? $good : Drs == 2 ? $warn : $muted");

            // ---------------- Lap
            yield return Value("Lap", "Personal best lap", "BEST", "=laptime(BestLap)", "", "valueColor", "purple");
            yield return Value("Lap", "Session best lap", "SESSION BEST", "=laptime(SessionBestLap)", "", "valueColor", "purple");
            yield return Value("Lap", "Class best lap", "CLASS BEST", "=laptime(ClassBestLap)");
            yield return Value("Lap", "Current lap time", "CURRENT", "=laptime(CurrentLap)");
            yield return Value("Lap", "Last lap time", "LAST", "=laptime(LastLap)", "", "valueColor", "=LastLapIsBest ? $purple : $text");
            yield return Value("Lap", "Predicted lap", "PREDICTED", "=laptime(PredictedLap)");
            yield return Value("Lap", "Live delta to best", "DELTA", "=signed(Delta, 2)", "", "valueColor", "=deltacolor(Delta)");
            yield return Value("Lap", "Current lap number", "LAP", "=LapText");

            // ---------------- Sectors
            yield return Value("Sectors", "Current sector", "SECTOR", "=Sector > 0 ? Sector + \"/\" + SectorCount : \"--\"");
            yield return Value("Sectors", "Current sector time", "SECTOR TIME", "=seconds(SectorTime)");
            yield return Value("Sectors", "Last sector time", "LAST SECTOR", "=seconds(LastSectorTime)");
            yield return Sectors("Sector times this lap", "Each sector of the lap in progress, purple when it's your best.", "sectortime");
            yield return Sectors("Sector times last lap", "Each sector of your last lap, purple when it's your best.", "lastsectortime");
            yield return Sectors("Best sector times", "Your best time in each sector, and the ideal lap they add up to.", "bestsectortime");

            // ---------------- Tyres
            yield return Block("Tyres", "Tyres: all readings", "Temperatures, wear and pressures for all four tyres.", () => new List<DashWidget> { W("tyres", "Tyres", 0, 0, 260, 220) });
            yield return Block("Tyres", "Tyre temperatures", "All four tyres' temperatures across the tread.", () => new List<DashWidget> { W("tyres", "Tyre temperatures", 0, 0, 240, 170, "showWear", "false", "showPressure", "false") });
            yield return Block("Tyres", "Tyre wear", "Tread left on each tyre.", () => new List<DashWidget> { W("tyres", "Tyre wear", 0, 0, 220, 130, "showTemps", "false", "showPressure", "false", "size", "26") });
            yield return Block("Tyres", "Tyre pressures", "Each tyre's pressure.", () => new List<DashWidget> { W("tyres", "Tyre pressures", 0, 0, 220, 130, "showTemps", "false", "showWear", "false", "size", "26") });

            // ---------------- Fuel
            yield return Value("Fuel", "Fuel capacity", "TANK", "=fuel(FuelCapacity)", "=FuelUnit");
            yield return Value("Fuel", "Remaining fuel", "FUEL", "=fuel(Fuel)", "=FuelUnit");
            yield return Value("Fuel", "Remaining laps of fuel", "EST LAPS", "=FuelLapsLeft < 0 ? \"--.-\" : format(FuelLapsLeft, \"0.0\")", "",
                "valueColor", "=steps(FuelLapsLeft, $text, 0, $bad, 2, $warn, 5, $text)");
            yield return Value("Fuel", "Last lap consumption", "LAST LAP", "=fuel(FuelLastLap, true)", "=FuelUnit");
            yield return Value("Fuel", "Fuel per lap", "AVG / LAP", "=fuel(FuelPerLap, true)", "=FuelUnit");
            yield return Value("Fuel", "Fuel to the finish", "REFUEL", "=FuelToFinish < 0 ? \"--.-\" : FuelToFinish <= 0.05 ? \"NONE\" : \"+\" + fuel(FuelToFinish)", "",
                "valueColor", "=FuelToFinish < 0 ? $text : FuelToFinish <= 0.05 ? $good : $warn");

            // ---------------- Race
            yield return Value("Race", "Race position", "POSITION", "=Position > 0 ? \"P\" + Position : \"P--\"", "=Starters > 0 ? \"/\" + Starters : \"\"");
            yield return Value("Race", "Class position", "CLASS", "=ClassPosition > 0 ? \"P\" + ClassPosition : \"P--\"");
            yield return Value("Race", "Laps to go", "=SessionType", "=RemainingText");
            yield return Value("Race", "Gap to the leader", "TO LEADER", "=GapToLeader == \"\" ? \"--\" : GapToLeader");
            yield return Value("Race", "Places gained", "GAINED", "=signed(PositionsGained, 0)", "", "valueColor", "=PositionsGained > 0 ? $good : PositionsGained < 0 ? $bad : $text");
            yield return Value("Race", "Incidents", "INC", "=Incidents + \"x\"");

            // ---------------- Leaderboard
            yield return Block("Leaderboard", "Leaderboard", "The top of the field with your row kept in view.", () => new List<DashWidget> { W("standings", "Leaderboard", 0, 0, 320, 300) });
            yield return Block("Leaderboard", "Leaderboard around me", "The cars just ahead of and behind you in the race, with the interval to each.", () => new List<DashWidget>
                { W("standings", "Around me", 0, 0, 320, 200, "rows", "5", "focus", "Around me", "column", "Interval") });
            yield return Block("Leaderboard", "Class leaderboard", "Your class only, with best laps; the fastest in purple.", () => new List<DashWidget>
                { W("standings", "Class leaderboard", 0, 0, 340, 300, "classOnly", "true", "column", "Best lap") });
            yield return Opponent("Race leader", "The leader's name, number, gap to you and last lap.", "1");
            yield return Opponent("Car ahead in the race", "The car one place ahead: name, number, gap and last lap.", "Position - 1");
            yield return Opponent("Car behind in the race", "The car one place behind: name, number, gap and last lap.", "Position + 1");

            // ---------------- Notification
            yield return Block("Notification", "Race notices", "Shows flags, penalties, a personal best and low fuel for a few seconds, coloured by how urgent they are.", () =>
            {
                const string tone = "NoticeKind == \"Danger\" ? $bad : NoticeKind == \"Warning\" ? $warn : NoticeKind == \"Good\" ? $good : $accent";
                return new List<DashWidget>
                {
                    W("panel", "Notice", 0, 0, 420, 52, "style", "Tinted", "tint", "=" + tone, "radius", "14", "visible", "Notice != \"\""),
                    W("text", "Notice text", 0, 0, 420, 52, "text", "=Notice", "size", "20", "color", "=" + tone, "visible", "Notice != \"\"")
                };
            });

            // ---------------- System
            yield return Value("System", "CPU load", "CPU", "=format(CpuLoad, \"0\")", "%");
            yield return Value("System", "Memory load", "MEMORY", "=format(MemoryLoad, \"0\")", "%");
            yield return Value("System", "Computer time", "TIME", "=Clock");
        }

        private static DashWidget W(string type, string name, double x, double y, double w, double h, params string[] props)
        {
            var widget = new DashWidget { Type = type, Name = name, X = x, Y = y, W = w, H = h };
            for (int i = 0; i + 1 < props.Length; i += 2) widget.Set(props[i], props[i + 1]);
            return widget;
        }

        private static DesignBlock Block(string folder, string name, string description, System.Func<List<DashWidget>> create) =>
            new DesignBlock { Section = Section, Folder = folder, Name = name, Description = description, Icon = "", Create = create };

        /// <summary>A labelled live value (the Value widget), optionally with more properties.</summary>
        private static DesignBlock Value(string folder, string name, string label, string value, string unit = "", params string[] more)
        {
            var props = new List<string> { "label", label, "value", value, "unit", unit, "valueSize", "30" };
            props.AddRange(more);
            return Block(folder, name, "A live value: " + name.ToLowerInvariant() + ".", () => new List<DashWidget> { W("metric", name, 0, 0, 170, 74, props.ToArray()) });
        }

        /// <summary>Three sector times side by side, purple where a time is your best.</summary>
        private static DesignBlock Sectors(string name, string description, string function) => Block("Sectors", name, description, () =>
        {
            var list = new List<DashWidget>();
            for (int i = 1; i <= 3; i++)
            {
                string time = function + "(" + i + ")";
                list.Add(W("metric", "S" + i, (i - 1) * 120, 0, 116, 64, "label", "S" + i, "value", "=seconds(" + time + ")", "valueSize", "22", "align", "Center",
                    "valueColor", function == "bestsectortime" ? "purple" : "=" + time + " > 0 && " + time + " <= bestsectortime(" + i + ") + 0.0005 ? $purple : $text",
                    "visible", i == 3 ? "SectorCount >= 3" : ""));
            }
            if (function == "bestsectortime")
                list.Add(W("metric", "Ideal lap", 360, 0, 150, 64, "label", "IDEAL", "value", "=laptime(IdealLap)",
                    "valueSize", "22", "align", "Center", "valueColor", "purple"));
            return list;
        });

        /// <summary>A card for the car in one race position, which can be any formula such as Position - 1.</summary>
        private static DesignBlock Opponent(string name, string description, string position) => Block("Leaderboard", name, description, () =>
        {
            string S(string field) => "standing(" + position + ", \"" + field + "\")";
            string shown = "=" + S("Exists");
            return new List<DashWidget>
            {
                W("panel", name, 0, 0, 300, 96, "visible", shown),
                W("text", "Number", 12, 12, 46, 30, "text", "=" + S("Number"), "size", "18", "visible", shown),
                W("text", "Name", 62, 10, 226, 34, "text", "=" + S("Name"), "size", "22", "align", "Left", "visible", shown),
                W("metric", "Gap", 0, 46, 150, 48, "label", "GAP", "value", "=signed(" + S("Delta") + ", 2)", "valueSize", "20", "labelSize", "11",
                    "valueColor", "=" + S("Delta") + " < 0 ? $warn : $good", "visible", shown),
                W("metric", "Last lap", 150, 46, 150, 48, "label", "LAST", "value", "=laptime(" + S("LastLap") + ")", "valueSize", "20", "labelSize", "11", "align", "Right", "visible", shown)
            };
        });
    }
}
