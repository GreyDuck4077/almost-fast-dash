using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;

namespace AlmostFastOverlays
{
    /// <summary>Everything a formula can see while a frame is drawn.</summary>
    internal sealed class EvalScope
    {
        public TelemetrySnapshot T;
        public UnitSystem Units = UnitSystem.Default;
        public LayoutTheme Theme = new LayoutTheme();
        public string Accent = "#2DD4BF";
        public bool ShiftFlashOn;
        public DateTime Now = DateTime.Now;
        /// <summary>The page shown (from 1) and how many pages the design has.</summary>
        public int Page = 1, PageCount = 1;
        /// <summary>The number of the group a widget belongs to (such as a driver card's position), 0 outside a group.</summary>
        public double Slot;
        /// <summary>Which view each tap zone is on, by tap name (0 for the first).</summary>
        public Dictionary<string, int> Taps;

        /// <summary>The same scope for a widget in a numbered group.</summary>
        public EvalScope WithSlot(double slot) => slot == Slot ? this : new EvalScope
        {
            T = T, Units = Units, Theme = Theme, Accent = Accent, ShiftFlashOn = ShiftFlashOn, Now = Now, Page = Page, PageCount = PageCount, Slot = slot, Taps = Taps
        };
    }

    /// <summary>
    /// A small formula language for widget properties, compiled once to delegates.
    ///
    ///   numbers 1.5, strings "text", colors #FF3040, theme colors $accent $good ...
    ///   + - * / %   == != &lt; &gt; &lt;= &gt;=   &amp;&amp; || !   cond ? a : b
    ///   data fields by name (Speed, Gear, FuelLapsLeft ...) and functions (format, if, clamp ...)
    ///
    /// "+" joins text when either side is text. Errors never throw while drawing;
    /// a failing formula yields null and the widget falls back to its default.
    /// </summary>
    internal static class Expr
    {
        private static readonly Dictionary<string, Compiled> Cache = new Dictionary<string, Compiled>();

        internal sealed class Compiled
        {
            public Func<EvalScope, object> Run;
            public string Error;
            public bool IsConstant;
        }

        public static Compiled Compile(string source)
        {
            source = source ?? "";
            lock (Cache)
            {
                if (Cache.TryGetValue(source, out Compiled cached)) return cached;
                Compiled compiled;
                try
                {
                    var parser = new Parser(source);
                    Node node = parser.ParseAll();
                    compiled = new Compiled { Run = node.Build(), IsConstant = node.IsConstant };
                }
                catch (Exception e)
                {
                    compiled = new Compiled { Run = s => null, Error = e.Message };
                }
                if (Cache.Count > 4000) Cache.Clear();
                Cache[source] = compiled;
                return compiled;
            }
        }

        public static object Evaluate(string source, EvalScope scope)
        {
            try { return Compile(source).Run(scope); }
            catch { return null; }
        }

        // ------------------------------------------------------------------ conversions

        public static double Num(object value)
        {
            switch (value)
            {
                case null: return 0;
                case double d: return d;
                case bool b: return b ? 1 : 0;
                case int i: return i;
                case float f: return f;
                case string s: return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double r) ? r : 0;
                default: try { return Convert.ToDouble(value, CultureInfo.InvariantCulture); } catch { return 0; }
            }
        }

        public static bool Bool(object value)
        {
            switch (value)
            {
                case null: return false;
                case bool b: return b;
                case double d: return d != 0 && !double.IsNaN(d);
                case string s: return s.Length > 0 && !s.Equals("false", StringComparison.OrdinalIgnoreCase) && s != "0";
                default: return true;
            }
        }

        public static string Text(object value)
        {
            switch (value)
            {
                case null: return "";
                case string s: return s;
                case bool b: return b ? "true" : "false";
                case double d: return double.IsNaN(d) ? "--" : Math.Abs(d - Math.Round(d)) < 1e-9 ? Math.Round(d).ToString("0", CultureInfo.InvariantCulture) : d.ToString("0.###", CultureInfo.InvariantCulture);
                case Color c: return SettingsStore.ToHex(c);
                default: return Convert.ToString(value, CultureInfo.InvariantCulture);
            }
        }

        /// <summary>A color from a value: Color, "#RRGGBB", "#AARRGGBB" or a theme token such as "accent".</summary>
        public static Color? Color(object value, EvalScope scope)
        {
            if (value is Color c) return c;
            string text = value as string;
            if (string.IsNullOrWhiteSpace(text)) return null;
            text = text.Trim();
            if (text.StartsWith("$")) text = text.Substring(1);
            string token = scope?.Theme?.Token(text, scope.Accent);
            if (token != null) text = token;
            return ParseHex(text);
        }

        public static Color? ParseHex(string text)
        {
            if (string.IsNullOrEmpty(text) || text[0] != '#') return null;
            string hex = text.Substring(1);
            if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v)) return null;
            if (hex.Length == 6) return System.Drawing.Color.FromArgb((int)(0xFF000000 | v));
            if (hex.Length == 8) return System.Drawing.Color.FromArgb((int)v);
            if (hex.Length == 3)
                return System.Drawing.Color.FromArgb((int)((v >> 8) & 0xF) * 17, (int)((v >> 4) & 0xF) * 17, (int)(v & 0xF) * 17);
            return null;
        }

        // ------------------------------------------------------------------ syntax tree

        private abstract class Node
        {
            public virtual bool IsConstant => false;
            public abstract Func<EvalScope, object> Build();
        }

        private sealed class Literal : Node
        {
            private readonly object _value;
            public Literal(object value) { _value = value; }
            public override bool IsConstant => true;
            public override Func<EvalScope, object> Build() { object v = _value; return s => v; }
        }

        private sealed class Variable : Node
        {
            private readonly string _name;
            public Variable(string name) { _name = name; }
            public override Func<EvalScope, object> Build()
            {
                if (_name.StartsWith("$"))
                {
                    string token = _name.Substring(1);
                    return s => Color(token, s);
                }
                DataField field = DataCatalog.Find(_name) ?? throw new FormatException("Unknown data field '" + _name + "'");
                Func<EvalScope, object> get = field.Get;
                return s => s.T == null ? null : get(s);
            }
        }

        private sealed class Unary : Node
        {
            private readonly char _op; private readonly Node _a;
            public Unary(char op, Node a) { _op = op; _a = a; }
            public override bool IsConstant => _a.IsConstant;
            public override Func<EvalScope, object> Build()
            {
                Func<EvalScope, object> a = _a.Build();
                if (_op == '!') return s => !Bool(a(s));
                return s => -Num(a(s));
            }
        }

        private sealed class Binary : Node
        {
            private readonly string _op; private readonly Node _a, _b;
            public Binary(string op, Node a, Node b) { _op = op; _a = a; _b = b; }
            public override bool IsConstant => _a.IsConstant && _b.IsConstant;
            public override Func<EvalScope, object> Build()
            {
                Func<EvalScope, object> a = _a.Build(), b = _b.Build();
                switch (_op)
                {
                    case "+": return s => { object x = a(s), y = b(s); return x is string || y is string ? (object)(Text(x) + Text(y)) : Num(x) + Num(y); };
                    case "-": return s => Num(a(s)) - Num(b(s));
                    case "*": return s => Num(a(s)) * Num(b(s));
                    case "/": return s => { double d = Num(b(s)); return d == 0 ? 0 : Num(a(s)) / d; };
                    case "%": return s => { double d = Num(b(s)); return d == 0 ? 0 : Num(a(s)) % d; };
                    case "=":
                    case "==": return s => Same(a(s), b(s));
                    case "!=": return s => !Same(a(s), b(s));
                    case "<": return s => Num(a(s)) < Num(b(s));
                    case ">": return s => Num(a(s)) > Num(b(s));
                    case "<=": return s => Num(a(s)) <= Num(b(s));
                    case ">=": return s => Num(a(s)) >= Num(b(s));
                    case "&&": return s => Bool(a(s)) && Bool(b(s));
                    case "||": return s => Bool(a(s)) || Bool(b(s));
                    default: throw new FormatException("Unknown operator " + _op);
                }
            }

            private static bool Same(object x, object y)
            {
                if (x is string || y is string) return string.Equals(Text(x), Text(y), StringComparison.OrdinalIgnoreCase);
                if (x is Color cx && y is Color cy) return cx.ToArgb() == cy.ToArgb();
                return Math.Abs(Num(x) - Num(y)) < 1e-9;
            }
        }

        private sealed class Conditional : Node
        {
            private readonly Node _c, _a, _b;
            public Conditional(Node c, Node a, Node b) { _c = c; _a = a; _b = b; }
            public override Func<EvalScope, object> Build()
            {
                Func<EvalScope, object> c = _c.Build(), a = _a.Build(), b = _b.Build();
                return s => Bool(c(s)) ? a(s) : b(s);
            }
        }

        private sealed class Call : Node
        {
            private readonly string _name; private readonly List<Node> _args;
            public Call(string name, List<Node> args) { _name = name; _args = args; }
            public override Func<EvalScope, object> Build()
            {
                FunctionInfo fn = Functions.Find(_name) ?? throw new FormatException("Unknown function '" + _name + "'");
                if (_args.Count < fn.MinArgs || _args.Count > fn.MaxArgs)
                    throw new FormatException(_name + " takes " + (fn.MinArgs == fn.MaxArgs ? fn.MinArgs.ToString() : fn.MinArgs + "-" + fn.MaxArgs) + " values");
                var args = new Func<EvalScope, object>[_args.Count];
                for (int i = 0; i < args.Length; i++) args[i] = _args[i].Build();
                Func<EvalScope, object[], object> run = fn.Run;
                if (_name.Equals("if", StringComparison.OrdinalIgnoreCase))
                    return s => Bool(args[0](s)) ? args[1](s) : args.Length > 2 ? args[2](s) : null;
                return s =>
                {
                    var values = new object[args.Length];
                    for (int i = 0; i < args.Length; i++) values[i] = args[i](s);
                    return run(s, values);
                };
            }
        }

        // ------------------------------------------------------------------ parser

        private sealed class Parser
        {
            private readonly string _s;
            private int _i;

            public Parser(string source) { _s = source; }

            public Node ParseAll()
            {
                Skip();
                if (_i >= _s.Length) return new Literal(null);
                Node node = ParseConditional();
                Skip();
                if (_i < _s.Length) throw new FormatException("Unexpected '" + _s[_i] + "' at " + (_i + 1));
                return node;
            }

            private Node ParseConditional()
            {
                Node c = ParseBinary(0);
                Skip();
                if (Peek('?'))
                {
                    _i++;
                    Node a = ParseConditional();
                    Expect(':');
                    Node b = ParseConditional();
                    return new Conditional(c, a, b);
                }
                return c;
            }

            private static readonly string[][] Levels =
            {
                // A single "=" is accepted as "==" because that is how people write it.
                new[] { "||" }, new[] { "&&" }, new[] { "==", "!=", "=" }, new[] { "<=", ">=", "<", ">" }, new[] { "+", "-" }, new[] { "*", "/", "%" }
            };

            private Node ParseBinary(int level)
            {
                if (level >= Levels.Length) return ParseUnary();
                Node left = ParseBinary(level + 1);
                while (true)
                {
                    Skip();
                    string op = null;
                    foreach (string candidate in Levels[level])
                        if (string.CompareOrdinal(_s, _i, candidate, 0, candidate.Length) == 0 &&
                            !(candidate.Length == 1 && (candidate == "<" || candidate == ">") && _i + 1 < _s.Length && _s[_i + 1] == '='))
                        {
                            op = candidate;
                            break;
                        }
                    // "and"/"or" read naturally in formulas too.
                    if (op == null && level == 0 && Word("or")) op = "||";
                    else if (op == null && level == 1 && Word("and")) op = "&&";
                    else if (op != null) _i += op.Length;
                    if (op == null) return left;
                    left = new Binary(op, left, ParseBinary(level + 1));
                }
            }

            private bool Word(string word)
            {
                if (string.Compare(_s, _i, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) != 0) return false;
                int end = _i + word.Length;
                if (end < _s.Length && (char.IsLetterOrDigit(_s[end]) || _s[end] == '_')) return false;
                _i = end;
                return true;
            }

            private Node ParseUnary()
            {
                Skip();
                if (Peek('!')) { _i++; return new Unary('!', ParseUnary()); }
                if (Word("not")) return new Unary('!', ParseUnary());
                if (Peek('-')) { _i++; return new Unary('-', ParseUnary()); }
                if (Peek('+')) { _i++; return ParseUnary(); }
                return ParsePrimary();
            }

            private Node ParsePrimary()
            {
                Skip();
                if (_i >= _s.Length) throw new FormatException("Formula ends too early");
                char c = _s[_i];
                if (c == '(')
                {
                    _i++;
                    Node inner = ParseConditional();
                    Expect(')');
                    return inner;
                }
                if (c == '"' || c == '\'') return new Literal(ReadString(c));
                if (c == '#')
                {
                    int start = _i++;
                    while (_i < _s.Length && Uri.IsHexDigit(_s[_i])) _i++;
                    string hex = _s.Substring(start, _i - start);
                    Color? color = ParseHex(hex) ?? throw new FormatException("Bad color " + hex);
                    return new Literal(color.Value);
                }
                if (char.IsDigit(c) || (c == '.' && _i + 1 < _s.Length && char.IsDigit(_s[_i + 1])))
                {
                    int start = _i;
                    while (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.')) _i++;
                    return new Literal(double.Parse(_s.Substring(start, _i - start), CultureInfo.InvariantCulture));
                }
                if (char.IsLetter(c) || c == '_' || c == '$')
                {
                    int start = _i++;
                    while (_i < _s.Length && (char.IsLetterOrDigit(_s[_i]) || _s[_i] == '_' || _s[_i] == '.')) _i++;
                    string name = _s.Substring(start, _i - start);
                    if (name.Equals("true", StringComparison.OrdinalIgnoreCase)) return new Literal(true);
                    if (name.Equals("false", StringComparison.OrdinalIgnoreCase)) return new Literal(false);
                    Skip();
                    if (Peek('('))
                    {
                        _i++;
                        var args = new List<Node>();
                        Skip();
                        if (!Peek(')'))
                        {
                            do { args.Add(ParseConditional()); Skip(); }
                            while (Peek(',') && ++_i > 0);
                        }
                        Expect(')');
                        return new Call(name, args);
                    }
                    return new Variable(name);
                }
                throw new FormatException("Unexpected '" + c + "' at " + (_i + 1));
            }

            private string ReadString(char quote)
            {
                var sb = new StringBuilder();
                _i++;
                while (_i < _s.Length && _s[_i] != quote)
                {
                    if (_s[_i] == '\\' && _i + 1 < _s.Length) _i++;
                    sb.Append(_s[_i++]);
                }
                if (_i >= _s.Length) throw new FormatException("Text is missing its closing quote");
                _i++;
                return sb.ToString();
            }

            private void Skip() { while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++; }
            private bool Peek(char c) => _i < _s.Length && _s[_i] == c;

            private void Expect(char c)
            {
                Skip();
                if (!Peek(c)) throw new FormatException("Expected '" + c + "'" + (_i < _s.Length ? " at " + (_i + 1) : " at the end"));
                _i++;
            }
        }
    }

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
                (s, a) => RaceFields.Standing(s, RaceFields.AtPosition(s.T, (int)Expr.Num(a[0]), false), Expr.Text(a[1])));
            Add("classstanding", "classstanding(position, \"Name\")", "Like standing(), counting only your class.", 2, 2,
                (s, a) => RaceFields.Standing(s, RaceFields.AtPosition(s.T, (int)Expr.Num(a[0]), true), Expr.Text(a[1])));
            Add("sectortime", "sectortime(sector)", "Your time in a sector of the lap in progress, 0 until you finish it.", 1, 1, (s, a) => RaceFields.Sector(s.T.SectorTimes, a[0]));
            Add("lastsectortime", "lastsectortime(sector)", "Your time in a sector on your last lap.", 1, 1, (s, a) => RaceFields.Sector(s.T.LastLapSectorTimes, a[0]));
            Add("bestsectortime", "bestsectortime(sector)", "Your best time in a sector this session.", 1, 1, (s, a) => RaceFields.Sector(s.T.BestSectorTimes, a[0]));
            Add("tap", "tap(\"name\")", "Which view a tap zone is on: 0 at first, then 1, 2... each time it's tapped on a touch screen. Use it in Show when, such as tap(\"fuel\") == 1.", 1, 1,
                (s, a) => s.Taps != null && s.Taps.TryGetValue(Expr.Text(a[0]).Trim(), out int view) ? (double)view : 0.0);
            Add("tyre", "tyre(\"LF\", \"Temp\")", "A tyre reading in your units. Corner: LF, RF, LR or RR. Reading: " + string.Join(", ", RaceFields.TyreFields) + ". iRacing updates these in the pits.", 2, 2,
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
