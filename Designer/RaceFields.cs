using System;
using System.Globalization;
using System.Linq;

namespace AlmostFastOverlays
{
    /// <summary>How formulas reach the leaderboard, sector times and tyres, in the user's units.</summary>
    internal static class RaceFields
    {
        public static readonly string[] StandingFields =
        {
            "Name", "Number", "Car", "Class", "ClassColor", "Position", "ClassPosition", "LastLap", "BestLap", "Laps", "LapsBehind",
            "Gap", "GapText", "Interval", "Delta", "IRating", "License", "LicenseColor", "InPits", "IsPlayer", "Exists"
        };

        public static readonly string[] TyreFields = { "Temp", "TempInner", "TempMiddle", "TempOuter", "Wear", "WearInner", "WearMiddle", "WearOuter", "Pressure" };

        /// <summary>The car in an overall (or class) position, or null when there is none.</summary>
        public static StandingsEntry AtPosition(TelemetrySnapshot t, int position, bool myClass)
        {
            StandingsEntry[] all = t.Standings ?? new StandingsEntry[0];
            if (!myClass) return all.FirstOrDefault(r => r.Position == position);
            StandingsEntry me = all.FirstOrDefault(r => r.IsPlayer);
            string cls = me?.ClassName;
            return all.FirstOrDefault(r => r.ClassPosition == position && (cls == null || r.ClassName == cls));
        }

        public static object Standing(EvalScope s, StandingsEntry r, string field)
        {
            if (string.Equals(field, "Exists", StringComparison.OrdinalIgnoreCase)) return r != null;
            if (r == null)
                return field.EndsWith("Color", StringComparison.OrdinalIgnoreCase) ? (object)System.Drawing.Color.Transparent
                    : field == "InPits" || field == "IsPlayer" ? (object)false : "";
            switch (field.ToLowerInvariant())
            {
                case "name": return r.Name ?? "";
                case "number": return r.CarNumber ?? "";
                case "car": return r.CarName ?? "";
                case "class": return r.ClassName ?? "";
                case "classcolor": return r.ClassColor;
                case "position": return (double)r.Position;
                case "classposition": return (double)r.ClassPosition;
                case "lastlap": return r.LastLap;
                case "bestlap": return r.BestLap;
                case "laps": return (double)r.Laps;
                case "lapsbehind": return (double)r.LapsBehind;
                case "gap": return r.GapToLeader;
                case "gaptext": return GapText(r);
                case "interval": return r.Interval;
                case "delta": return r.DeltaToPlayer;
                case "irating": return (double)r.IRating;
                case "license": return r.License ?? "";
                case "licensecolor": return r.LicenseColor;
                case "inpits": return r.InPits;
                case "isplayer": return r.IsPlayer;
                default: return "";
            }
        }

        /// <summary>"LEADER", "+5.48" or "+1 LAP": how far a car is behind the leader.</summary>
        public static string GapText(StandingsEntry r)
        {
            if (r == null) return "";
            if (r.Position == 1) return "LEADER";
            if (r.LapsBehind > 0) return "+" + r.LapsBehind + (r.LapsBehind == 1 ? " LAP" : " LAPS");
            return r.GapToLeader > 0 ? "+" + r.GapToLeader.ToString(r.GapToLeader >= 100 ? "0.0" : "0.00", CultureInfo.InvariantCulture) : "";
        }

        public static object Sector(double[] times, object sector)
        {
            int n = (int)Expr.Num(sector);
            return times != null && n >= 1 && n <= times.Length ? times[n - 1] : 0.0;
        }

        public static object Tyre(EvalScope s, string corner, string reading)
        {
            TyreCorner tyre = (s.T.Tyres ?? new TyreCorner[0]).FirstOrDefault(c => string.Equals(c.Name, (corner ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
            if (tyre == null) return double.NaN;
            switch ((reading ?? "").ToLowerInvariant())
            {
                case "temp": return s.Units.Temperature(tyre.Temp);
                case "tempinner": return s.Units.Temperature(tyre.TempInner);
                case "tempmiddle": return s.Units.Temperature(tyre.TempMiddle);
                case "tempouter": return s.Units.Temperature(tyre.TempOuter);
                case "wear": return tyre.Wear * 100;
                case "wearinner": return tyre.WearInner * 100;
                case "wearmiddle": return tyre.WearMiddle * 100;
                case "wearouter": return tyre.WearOuter * 100;
                case "pressure": return Pressure(s.Units, tyre.PressureKpa / 100);
                default: return double.NaN;
            }
        }

        /// <summary>A pressure in bar shown in bar (metric) or PSI.</summary>
        public static double Pressure(UnitSystem units, double bar) => double.IsNaN(bar) ? double.NaN : units.Metric ? bar : bar * 14.5038;

        public static string PressureLabel(UnitSystem units) => units.Metric ? "BAR" : "PSI";

        public static string DrsText(int state)
        {
            switch (state)
            {
                case 0: return "OFF";
                case 1: return "SOON";
                case 2: return "READY";
                case 3: return "OPEN";
                default: return "";
            }
        }
    }
}
