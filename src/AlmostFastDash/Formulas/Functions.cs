using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;

namespace AlmostFastDash.Formulas
{
    internal sealed class FunctionInfo
    {
        public string Name, Signature, Description;
        public int MinArgs, MaxArgs;
        public Func<EvalScope, object[], object> Run;
    }

    /// <summary>Functions available in formulas; the list also feeds the designer's formula help.</summary>
    internal static class Functions
    {
        public static readonly List<FunctionInfo> All = new List<FunctionInfo>();
        private static readonly Dictionary<string, FunctionInfo> ByName = new Dictionary<string, FunctionInfo>(StringComparer.OrdinalIgnoreCase);

        static Functions()
        {
            Add("if", "if(condition, then, else)", "Picks a value by condition.", 2, 3, (s, a) => null);
            Add("format", "format(value, \"0.0\")", "Formats a number with a .NET pattern such as \"0\", \"0.00\" or \"+0.0;-0.0\".", 2, 2,
                (s, a) => FormatNumber(Expr.Num(a[0]), Expr.Text(a[1])));
            Add("laptime", "laptime(seconds)", "Formats seconds as 1:27.932; shows --:--.--- when unknown.", 1, 1, (s, a) => Fmt.LapTime(Expr.Num(a[0])));
            Add("clock", "clock(seconds)", "Formats seconds as 12:34 or 1:02:03 for time remaining.", 1, 1, (s, a) => Fmt.Remaining(Expr.Num(a[0])));
            Add("signed", "signed(value, decimals)", "Adds a + sign to positive numbers: +0.46.", 1, 2,
                (s, a) => { double v = Expr.Num(a[0]); int d = a.Length > 1 ? (int)Expr.Num(a[1]) : 2; string f = d <= 0 ? "0" : "0." + new string('0', d); return (v >= 0 ? "+" : "") + v.ToString(f, CultureInfo.InvariantCulture); });
            Add("fuel", "fuel(amount, precise)", "Formats fuel for your unit: 0.0 L or 0.00 gal; precise adds a decimal. Negative shows --.-", 1, 2,
                (s, a) =>
                {
                    double v = Expr.Num(a[0]);
                    bool precise = a.Length > 1 && Expr.Bool(a[1]);
                    if (v < 0) return precise ? "--.--" : "--.-";
                    return v.ToString(s.Units.Gallons ? (precise ? "0.000" : "0.00") : (precise ? "0.00" : "0.0"), CultureInfo.InvariantCulture);
                });
            Add("gear", "gear(number)", "Gear number as text: R, N, 1, 2...", 1, 1, (s, a) => Fmt.Gear((int)Expr.Num(a[0])));
            Add("time", "time(\"HH:mm\")", "The PC's local time in a .NET date pattern.", 0, 1, (s, a) => s.Now.ToString(a.Length > 0 ? Expr.Text(a[0]) : "HH:mm", CultureInfo.InvariantCulture));
            Add("round", "round(value, decimals)", "Rounds to a number of decimals (default 0).", 1, 2, (s, a) => Math.Round(Expr.Num(a[0]), a.Length > 1 ? Math.Max(0, Math.Min(10, (int)Expr.Num(a[1]))) : 0, MidpointRounding.AwayFromZero));
            Add("floor", "floor(value)", "Rounds down.", 1, 1, (s, a) => Math.Floor(Expr.Num(a[0])));
            Add("ceil", "ceil(value)", "Rounds up.", 1, 1, (s, a) => Math.Ceiling(Expr.Num(a[0])));
            Add("abs", "abs(value)", "Removes the sign.", 1, 1, (s, a) => Math.Abs(Expr.Num(a[0])));
            Add("min", "min(a, b, ...)", "The smallest value.", 1, 16, (s, a) => { double m = double.MaxValue; foreach (object v in a) m = Math.Min(m, Expr.Num(v)); return m; });
            Add("max", "max(a, b, ...)", "The largest value.", 1, 16, (s, a) => { double m = double.MinValue; foreach (object v in a) m = Math.Max(m, Expr.Num(v)); return m; });
            Add("clamp", "clamp(value, low, high)", "Keeps a value inside a range.", 3, 3, (s, a) => Math.Max(Expr.Num(a[1]), Math.Min(Expr.Num(a[2]), Expr.Num(a[0]))));
            Add("norm", "norm(value, low, high)", "Maps a range to 0-1, for bars and gauges.", 3, 3,
                (s, a) => { double lo = Expr.Num(a[1]), hi = Expr.Num(a[2]); return hi == lo ? 0 : Math.Max(0, Math.Min(1, (Expr.Num(a[0]) - lo) / (hi - lo))); });
            Add("nz", "nz(value, fallback)", "Uses the fallback when the value is zero, negative or unknown.", 2, 2,
                (s, a) => { double v = Expr.Num(a[0]); return v <= 0 || double.IsNaN(v) ? a[1] : a[0]; });
            Add("blink", "blink(milliseconds)", "Alternates true/false at a rate, for flashing warnings.", 0, 1,
                (s, a) => { int ms = Math.Max(40, a.Length > 0 ? (int)Expr.Num(a[0]) : 250); return (((long)s.Now.TimeOfDay.TotalMilliseconds / ms) & 1) == 0; });
            Add("upper", "upper(text)", "Capital letters.", 1, 1, (s, a) => Expr.Text(a[0]).ToUpperInvariant());
            Add("lower", "lower(text)", "Small letters.", 1, 1, (s, a) => Expr.Text(a[0]).ToLowerInvariant());
            Add("left", "left(text, count)", "The first characters of text.", 2, 2, (s, a) => { string t = Expr.Text(a[0]); int n = Math.Max(0, (int)Expr.Num(a[1])); return t.Length <= n ? t : t.Substring(0, n); });
            Add("pad", "pad(value, width)", "Pads with leading zeros: pad(7, 2) is 07.", 2, 2, (s, a) => Expr.Text(a[0]).PadLeft(Math.Max(0, (int)Expr.Num(a[1])), '0'));
            Add("rgb", "rgb(r, g, b)", "A color from 0-255 channels.", 3, 3, (s, a) => Color.FromArgb(Byte(a[0]), Byte(a[1]), Byte(a[2])));
            Add("rgba", "rgba(r, g, b, alpha)", "A color with transparency (alpha 0-1).", 4, 4, (s, a) => Color.FromArgb((int)(Math.Max(0, Math.Min(1, Expr.Num(a[3]))) * 255), Byte(a[0]), Byte(a[1]), Byte(a[2])));
            Add("alpha", "alpha(color, amount)", "Makes a color see-through (amount 0-1).", 2, 2,
                (s, a) => { Color c = Expr.Color(a[0], s) ?? Color.White; return Color.FromArgb((int)(Math.Max(0, Math.Min(1, Expr.Num(a[1]))) * 255), c); });
            Add("mix", "mix(colorA, colorB, amount)", "Blends two colors; 0 is A, 1 is B.", 3, 3,
                (s, a) => Mix(Expr.Color(a[0], s) ?? Color.Black, Expr.Color(a[1], s) ?? Color.White, Expr.Num(a[2])));
            Add("steps", "steps(value, color, at1, color1, at2, color2 ...)", "Picks a color by thresholds: steps(FuelLapsLeft, $bad, 2, $warn, 5, $text).", 2, 21,
                (s, a) =>
                {
                    double v = Expr.Num(a[0]);
                    object pick = a[1];
                    for (int i = 2; i + 1 < a.Length; i += 2) if (v >= Expr.Num(a[i])) pick = a[i + 1];
                    return pick;
                });
            Add("deltacolor", "deltacolor(seconds)", "Green when gaining, red when losing, text color when even.", 1, 1,
                (s, a) => { double d = Expr.Num(a[0]); return Expr.Color(d > 0.005 ? "good" : d < -0.005 ? "bad" : "text", s); });
            Add("temp", "temp(celsius)", "A temperature given in °C, in your temperature unit: temp(100) is 212 in °F.", 1, 1, (s, a) => s.Units.Temperature(Expr.Num(a[0])));
            Add("seconds", "seconds(value)", "Formats a short time such as a sector: 29.904; shows --.--- when unknown.", 1, 1,
                (s, a) => { double v = Expr.Num(a[0]); return v <= 0 || double.IsNaN(v) ? "--.---" : v < 60 ? v.ToString("0.000", CultureInfo.InvariantCulture) : Fmt.LapTime(v); });

            // Leaderboard, sectors and tyres.
            Add("standing", "standing(position, \"Name\")", "A detail of the car in a race position: " + string.Join(", ", RaceFields.StandingFields) + ".", 2, 2,
                (s, a) => RaceFields.Standing(RaceFields.AtPosition(s.T, (int)Expr.Num(a[0]), false), Expr.Text(a[1])));
            Add("classstanding", "classstanding(position, \"Name\")", "Like standing(), counting only your class.", 2, 2,
                (s, a) => RaceFields.Standing(RaceFields.AtPosition(s.T, (int)Expr.Num(a[0]), true), Expr.Text(a[1])));
            Add("sectortime", "sectortime(sector)", "Your time in a sector of the lap in progress, 0 until you finish it.", 1, 1, (s, a) => RaceFields.Sector(s.T.SectorTimes, a[0]));
            Add("lastsectortime", "lastsectortime(sector)", "Your time in a sector on your last lap.", 1, 1, (s, a) => RaceFields.Sector(s.T.LastLapSectorTimes, a[0]));
            Add("bestsectortime", "bestsectortime(sector)", "Your best time in a sector this session.", 1, 1, (s, a) => RaceFields.Sector(s.T.BestSectorTimes, a[0]));
            Add("tap", "tap(\"name\")", "Which view a tap zone is on: 0 at first, then 1, 2... each time it's tapped on a touch screen. Use it in Show when, such as tap(\"fuel\") == 1.", 1, 1,
                (s, a) => s.Taps != null && s.Taps.TryGetValue(Expr.Text(a[0]).Trim(), out int view) ? (double)view : 0.0);
            Add("option", "option(\"Name\")", "A module option: a setting made changeable in one place for a whole group, such as option(\"Accent\"). " +
                "Right-click a setting's name in Properties to make one; set it with the group selected.", 1, 1,
                (s, a) => ModuleOptions.Value(s.Self, Expr.Text(a[0])));
            Add("tire", "tire(\"LF\", \"Temp\")", "A tire reading in your units. Corner: LF, RF, LR or RR. Reading: " + string.Join(", ", RaceFields.TyreFields) + ". iRacing updates these in the pits.", 2, 2,
                (s, a) => RaceFields.Tyre(s, Expr.Text(a[0]), Expr.Text(a[1])));
            Add("tyre", "tyre(\"LF\", \"Temp\")", "The same as tire(), as earlier dashboards wrote it.", 2, 2,
                (s, a) => RaceFields.Tyre(s, Expr.Text(a[0]), Expr.Text(a[1])));
        }

        private static int Byte(object value) => (int)Math.Max(0, Math.Min(255, Expr.Num(value)));

        private static void Add(string name, string signature, string description, int min, int max, Func<EvalScope, object[], object> run)
        {
            var info = new FunctionInfo { Name = name, Signature = signature, Description = description, MinArgs = min, MaxArgs = max, Run = run };
            All.Add(info);
            ByName[name] = info;
        }

        public static FunctionInfo Find(string name) => ByName.TryGetValue(name, out FunctionInfo f) ? f : null;

        public static Color Mix(Color a, Color b, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        public static string FormatNumber(double value, string pattern)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return "--";
            try { return value.ToString(string.IsNullOrEmpty(pattern) ? "0" : pattern, CultureInfo.InvariantCulture); }
            catch { return value.ToString(CultureInfo.InvariantCulture); }
        }
    }
}
