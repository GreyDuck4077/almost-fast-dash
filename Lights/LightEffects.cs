using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace AlmostFastOverlays
{
    internal sealed class LightEffect
    {
        public string Id, Name, Description;
        public readonly List<PropDef> Props = new List<PropDef>();
        /// <summary>Colors for one strip: null leaves an LED to the layers below.</summary>
        public Func<LightContext, Color?[]> Render;
        public PropDef Prop(string key) => Props.FirstOrDefault(p => p.Key == key);
    }

    /// <summary>One layer being drawn on one strip, with typed access to its resolved properties.</summary>
    internal sealed class LightContext
    {
        private readonly Dictionary<string, object> _resolved = new Dictionary<string, object>();

        public EvalScope Scope;
        public LightLayer Layer;
        public LightEffect Effect;
        public LedTarget Target;
        public int Strip, StripCount, Length;
        public int Now;
        public TelemetrySnapshot T => Scope.T;

        public string Raw(string key) => Layer.Get(key, Effect.Prop(key)?.Default ?? "");

        public object Value(string key)
        {
            if (_resolved.TryGetValue(key, out object cached)) return cached;
            string raw = Raw(key);
            PropDef def = Effect.Prop(key);
            bool formula = def != null && LayoutRenderer.IsFormula(raw, def);
            object value = formula ? Expr.Evaluate(LayoutRenderer.Strip(raw), Scope) : raw;
            _resolved[key] = value;
            return value;
        }

        public double Num(string key)
        {
            double d = Expr.Num(Value(key));
            return double.IsNaN(d) || double.IsInfinity(d) ? 0 : d;
        }

        public bool Bool(string key) => Expr.Bool(Value(key));
        public string Text(string key) => Expr.Text(Value(key));

        public Color Color(string key) =>
            Expr.Color(Value(key), Scope) ?? Expr.Color(Effect.Prop(key)?.Default, Scope) ?? System.Drawing.Color.White;

        public Color? OptionalColor(string key)
        {
            object value = Value(key);
            if (value is string s && string.IsNullOrWhiteSpace(s)) return null;
            return Expr.Color(value, Scope);
        }

        public Color?[] Empty() => new Color?[Length];
    }

    /// <summary>Every light effect: its editable properties and how it lights one strip.</summary>
    internal static class LightEffects
    {
        public static readonly List<LightEffect> All = new List<LightEffect>();
        public static readonly string[] Directions = FillOrder.All;

        static LightEffects()
        {
            Define("revs", "Rev lights", "Fills with the revs using the car's own shift-light RPMs, then flashes at the shift point.", Revs)
                .Choice("source", "Revs from", "Car shift lights", "Revs", "Car shift lights", "iRacing shift %", "RPM range", "Formula")
                .Number("fromRpm", "From RPM", "5000", "Revs", 0, 20000, 50)
                .Number("toRpm", "To RPM", "7500", "Revs", 0, 20000, 50)
                .Formula("value", "Value (Formula)", "=RevPct", "Revs", "0 to 1, used when Revs from is Formula.")
                .Formula("flashWhen", "Flash when (Formula)", "=ShiftAlert", "Revs", "Used when Revs from is Formula.")
                .Fill()
                .Colors("good", "warn", "bad", 0.5, 0.8)
                .Bool("flash", "Flash at shift point", "true", "Shift point")
                .Color("flashColor", "Flash color", "bad", "Shift point")
                .Choice("flashStyle", "Flash style", "Flash", "Shift point", "Flash", "Solid", "Over fill")
                .Number("flashMs", "Flash speed (ms)", "110", "Shift point", 40, 1000, 10);

            Define("meter", "Meter", "A bar from any value: throttle, brake, fuel, tyre wear or a formula of your own.", Meter)
                .Formula("value", "Value", "=Throttle", "Value")
                .Number("min", "Minimum", "0", "Value", double.NaN, double.NaN, 0.1)
                .Number("max", "Maximum", "1", "Value", double.NaN, double.NaN, 0.1)
                .Fill()
                .Colors("good", "warn", "bad", 0.5, 0.8);

            Define("center", "Center meter", "Grows one way for positive and the other for negative, such as lap delta. On a mirrored target the left half shows negative.", CenterMeter)
                .Formula("value", "Value", "=Delta", "Value")
                .Number("range", "Full scale", "1", "Value", 0.01, 1000, 0.1)
                .Color("positive", "Positive color", "good", "Colors")
                .Color("negative", "Negative color", "bad", "Colors")
                .Bool("smooth", "Smooth leading LED", "true", "Fill");

            Define("solid", "Solid", "One steady color. Pair with Show when for state lights such as the pit limiter.", c =>
                {
                    var result = c.Empty();
                    Color color = c.Color("color");
                    for (int i = 0; i < result.Length; i++) result[i] = color;
                    return result;
                })
                .Color("color", "Color", "accent", "Color");

            Define("blink", "Blink", "Flashes between two colors (or off). Good for warnings.", c =>
                {
                    var result = c.Empty();
                    int period = Math.Max(40, (int)c.Num("rateMs")) * 2;
                    bool on = (c.Now % period + period) % period < period * Math.Max(5, Math.Min(95, c.Num("duty"))) / 100.0;
                    Color? color = on ? c.Color("color") : c.OptionalColor("offColor");
                    for (int i = 0; i < result.Length; i++) result[i] = color;
                    return result;
                })
                .Color("color", "Color", "warn", "Color")
                .Color("offColor", "Off color", "", "Color", "Empty shows the layers below between flashes.")
                .Number("rateMs", "Speed (ms)", "250", "Timing", 40, 2000, 10)
                .Number("duty", "On time %", "50", "Timing", 5, 95, 5);

            Define("breathe", "Breathe", "Slowly fades a color up and down: a calm status light.", c =>
                {
                    var result = c.Empty();
                    double period = Math.Max(200, c.Num("periodMs"));
                    double min = Math.Max(0, Math.Min(100, c.Num("minLevel"))) / 100;
                    double level = min + (1 - min) * (0.5 - 0.5 * Math.Cos(2 * Math.PI * (c.Now % period) / period));
                    Color color = Scale(c.Color("color"), level);
                    for (int i = 0; i < result.Length; i++) result[i] = color;
                    return result;
                })
                .Color("color", "Color", "accent", "Color")
                .Number("periodMs", "Cycle (ms)", "1600", "Timing", 200, 10000, 50)
                .Number("minLevel", "Lowest level %", "10", "Timing", 0, 100, 5);

            Define("sweep", "Sweep", "A light that runs along the strip with a fading tail, such as a pit-limiter scanner.", Sweep)
                .Color("color", "Color", "info", "Color")
                .Choice("mode", "Motion", "Bounce", "Motion", "Bounce", "Loop")
                .Number("periodMs", "One pass (ms)", "700", "Motion", 100, 5000, 50)
                .Number("width", "Width (LEDs)", "1", "Motion", 1, 10)
                .Number("tail", "Tail (LEDs)", "2", "Motion", 0, 10);

            Define("flag", "Flag colors", "Shows the current flag in its own colors and patterns; dark when there is no flag.", FlagColors)
                .Bool("pattern", "Animated patterns", "true", "Look");

            Define("spotter", "Blind spot", "Lights where a car is alongside: towards the front when it is ahead, the back when it is behind.", Spotter)
                .Choice("side", "Side", "Auto", "Spotter", "Auto", "Left", "Right")
                .Choice("style", "Style", "Position", "Spotter", "Position", "Halves", "Whole strip")
                .Color("color", "Color", "bad", "Color");

            Define("gradient", "Gradient", "A still color gradient along the strip: ambient or idle lighting.", c =>
                {
                    var result = c.Empty();
                    Color a = c.Color("low"), m = c.Color("mid"), b = c.Color("high");
                    for (int i = 0; i < result.Length; i++)
                    {
                        double p = c.Length <= 1 ? 0 : i / (double)(c.Length - 1);
                        result[i] = p < 0.5 ? Functions.Mix(a, m, p * 2) : Functions.Mix(m, b, (p - 0.5) * 2);
                    }
                    return result;
                })
                .Color("low", "Start color", "accent", "Colors")
                .Color("mid", "Middle color", "info", "Colors")
                .Color("high", "End color", "purple", "Colors");

            Define("blank", "Blank", "Turns the target off. Use with Replace and a Show when to black out LEDs, for example in the pits.", c =>
                {
                    var result = c.Empty();
                    for (int i = 0; i < result.Length; i++) result[i] = System.Drawing.Color.Black;
                    return result;
                });
        }

        public static LightEffect Find(string id) => All.FirstOrDefault(e => e.Id == id);

        // ------------------------------------------------------------------ effects

        private static Color?[] Revs(LightContext c)
        {
            TelemetrySnapshot t = c.T;
            double fraction;
            bool shift;
            switch (c.Text("source"))
            {
                case "iRacing shift %":
                    fraction = t.ShiftIndicatorPct;
                    shift = t.ShiftAlert;
                    break;
                case "RPM range":
                    double from = c.Num("fromRpm"), to = c.Num("toRpm");
                    fraction = to > from ? (t.EngineRpm - from) / (to - from) : 0;
                    shift = to > 0 && t.EngineRpm >= to;
                    break;
                case "Formula":
                    fraction = c.Num("value");
                    shift = c.Bool("flashWhen");
                    break;
                default:
                    fraction = DataCatalog.RevFraction(t);
                    shift = DataCatalog.AtShiftPoint(t);
                    break;
            }
            if (!t.Connected) { fraction = 0; shift = false; }

            if (shift && c.Bool("flash"))
            {
                var result = c.Empty();
                int half = Math.Max(40, (int)c.Num("flashMs"));
                string style = c.Text("flashStyle");
                bool on = style == "Solid" || ((c.Now / half) & 1) == 0;
                // Over fill alternates the flash color with the full rev fill, as the classic lights did.
                if (!on && style == "Over fill") return Fill(c, 1);
                Color color = c.Color("flashColor");
                for (int i = 0; i < result.Length; i++) result[i] = on ? color : System.Drawing.Color.Black;
                return result;
            }
            return Fill(c, fraction);
        }

        private static Color?[] Meter(LightContext c)
        {
            double min = c.Num("min"), max = c.Num("max");
            double fraction = Math.Abs(max - min) < 1e-12 ? 0 : (c.Num("value") - min) / (max - min);
            return Fill(c, fraction);
        }

        private static Color?[] CenterMeter(LightContext c)
        {
            var result = c.Empty();
            double value = c.Num("value"), range = Math.Max(0.0001, c.Num("range"));
            double f = Math.Max(-1, Math.Min(1, value / range));
            bool smooth = c.Bool("smooth");
            Color color = f >= 0 ? c.Color("positive") : c.Color("negative");
            int n = c.Length;
            if (c.StripCount == 2)
            {
                // Mirrored halves, each from the middle outward: left half is negative, right positive.
                bool mine = (c.Strip == 0 && f < 0) || (c.Strip == 1 && f > 0);
                if (!mine) return result;
                double lit = Math.Abs(f) * n;
                for (int i = 0; i < n; i++) result[i] = Level(color, lit - i, smooth);
                return result;
            }
            // One strip: grow from its middle towards the end (positive) or the start (negative).
            double half = n / 2.0, steps = Math.Abs(f) * half;
            for (int i = 0; i < n; i++)
            {
                double distance = f >= 0 ? i + 0.5 - half : half - (i + 0.5);
                if (distance > 0) result[i] = Level(color, steps - (distance - 0.5), smooth);
            }
            return result;
        }

        private static Color?[] Sweep(LightContext c)
        {
            var result = c.Empty();
            int n = c.Length;
            if (n == 0) return result;
            double period = Math.Max(100, c.Num("periodMs"));
            double phase = (c.Now % period + period) % period / period;
            bool bounce = c.Text("mode") != "Loop";
            double head;
            if (bounce)
            {
                double cycle = (c.Now % (period * 2) + period * 2) % (period * 2) / period;
                head = cycle <= 1 ? cycle * (n - 1) : (2 - cycle) * (n - 1);
            }
            else head = phase * n;
            bool forward = !bounce || (c.Now % (period * 2) + period * 2) % (period * 2) < period;
            double width = Math.Max(1, c.Num("width")), tail = Math.Max(0, c.Num("tail"));
            Color color = c.Color("color");
            for (int i = 0; i < n; i++)
            {
                double behind = forward ? head - i : i - head;
                if (!bounce && behind < 0) behind += n;
                double level = behind < -0.5 ? 0 : behind < width - 0.5 ? 1 : tail <= 0 ? 0 : Math.Max(0, 1 - (behind - width + 0.5) / (tail + 1));
                if (level > 0.01) result[i] = Scale(color, level);
            }
            return result;
        }

        private static Color?[] FlagColors(LightContext c)
        {
            var result = c.Empty();
            RacingFlag flag = c.T.Flag;
            if (flag == RacingFlag.None) return result;
            bool blink = ((c.Now / 250) & 1) == 0;
            bool pattern = c.Bool("pattern");
            for (int i = 0; i < result.Length; i++) result[i] = FlagColor(flag, i, blink, pattern);
            return result;
        }

        /// <summary>
        /// The color of LED <paramref name="index"/> for a flag. With patterns, two-color
        /// flags alternate on <paramref name="blink"/> and the checkered flag alternates LEDs.
        /// Shared by the flag light layer and LED devices with the Flags role.
        /// </summary>
        internal static Color FlagColor(RacingFlag flag, int index, bool blink, bool pattern)
        {
            switch (flag)
            {
                case RacingFlag.Green: return System.Drawing.Color.Lime;
                case RacingFlag.Yellow: case RacingFlag.SafetyCar: return System.Drawing.Color.FromArgb(255, 190, 0);
                case RacingFlag.White: return System.Drawing.Color.White;
                case RacingFlag.Red: return System.Drawing.Color.Red;
                case RacingFlag.BlueYellowStripe: return !pattern || blink ? System.Drawing.Color.FromArgb(0, 90, 255) : System.Drawing.Color.FromArgb(255, 190, 0);
                case RacingFlag.Black: return !pattern || blink ? System.Drawing.Color.White : System.Drawing.Color.Black;
                case RacingFlag.Meatball: return !pattern || blink ? System.Drawing.Color.FromArgb(255, 90, 0) : System.Drawing.Color.Black;
                case RacingFlag.Surface: return (!pattern || (index % 2 == 0) == blink) ? System.Drawing.Color.FromArgb(255, 190, 0) : System.Drawing.Color.Red;
                case RacingFlag.Checkered: return (!pattern || (index % 2 == 0) == blink) ? System.Drawing.Color.White : System.Drawing.Color.Black;
                default: return System.Drawing.Color.Black;
            }
        }

        private static Color?[] Spotter(LightContext c)
        {
            var result = c.Empty();
            string side = c.Text("side");
            bool left = side == "Left" || (side == "Auto" && !c.Target.IsRight(c.Strip));
            BlindSpotZone zone = left ? c.T.LeftBlindSpot : c.T.RightBlindSpot;
            if (zone == BlindSpotZone.None) return result;
            Color color = c.Color("color");
            int n = c.Length;
            string style = c.Text("style");
            // Halves: front or rear lights that half of the strip, alongside lights all of it.
            bool whole = style == "Whole strip" || (style == "Halves" && zone == BlindSpotZone.Beside);
            bool halves = style == "Halves";
            for (int i = 0; i < n; i++)
            {
                // Position measured from the back of the strip (0) to its front (n - 1).
                double p = c.Target.FrontAtEnd ? i : n - 1 - i;
                bool on = whole
                    || (halves && zone == BlindSpotZone.Front && p >= n - (n + 1) / 2)
                    || (halves && zone == BlindSpotZone.Rear && p < (n + 1) / 2)
                    || (!halves && zone == BlindSpotZone.Front && p >= n * 0.65)
                    || (!halves && zone == BlindSpotZone.Beside && p >= n * 0.2 - 0.01 && p <= n * 0.55)
                    || (!halves && zone == BlindSpotZone.Rear && p < n * 0.5);
                if (on) result[i] = color;
            }
            return result;
        }

        // ------------------------------------------------------------------ shared fill

        /// <summary>Lights a strip to a 0-1 fraction in the chosen direction and color scheme.</summary>
        private static Color?[] Fill(LightContext c, double fraction)
        {
            var result = c.Empty();
            int n = c.Length;
            if (n == 0) return result;
            fraction = double.IsNaN(fraction) ? 0 : Math.Max(0, Math.Min(1, fraction));
            string direction = FillOrder.Normalize(c.Text("direction"));
            int steps = FillOrder.Steps(direction, n);
            double lit = fraction * steps;
            string mode = c.Text("colorMode");
            bool smooth = c.Bool("smooth"), gradient = mode == "Gradient", byLevel = mode == "By level";
            Color low = c.Color("low"), mid = c.Color("mid"), high = c.Color("high");
            double midAt = c.Num("midAt"), highAt = c.Num("highAt");
            // By level: the whole strip takes one color from how full it is, like a fuel gauge.
            Color levelColor = fraction >= highAt ? high : fraction >= midAt ? mid : low;
            for (int i = 0; i < n; i++)
            {
                int rank = FillOrder.Rank(direction, i, n);
                double at = (rank + 0.5) / steps;
                Color color = byLevel ? levelColor : gradient
                    ? (at < midAt ? Functions.Mix(low, mid, at / Math.Max(0.001, midAt)) : Functions.Mix(mid, high, (at - midAt) / Math.Max(0.001, 1 - midAt)))
                    : at >= highAt ? high : at >= midAt ? mid : low;
                result[i] = Level(color, lit - rank, smooth);
            }
            return result;
        }

        /// <summary>An LED at a fill amount: on when amount reaches it, partly lit when smoothing.</summary>
        private static Color? Level(Color color, double amount, bool smooth)
        {
            if (smooth)
            {
                double level = Math.Max(0, Math.Min(1, amount));
                return level <= 0.02 ? (Color?)null : Scale(color, level);
            }
            return amount > 1e-6 ? color : (Color?)null;
        }

        internal static Color Scale(Color color, double level)
        {
            level = Math.Max(0, Math.Min(1, level));
            return System.Drawing.Color.FromArgb((int)(color.R * level), (int)(color.G * level), (int)(color.B * level));
        }

        // ------------------------------------------------------------------ definition helpers

        private static LightEffect Define(string id, string name, string description, Func<LightContext, Color?[]> render)
        {
            var effect = new LightEffect { Id = id, Name = name, Description = description, Render = render };
            All.Add(effect);
            return effect;
        }

        private static LightEffect Add(this LightEffect e, PropDef p) { e.Props.Add(p); return e; }

        private static LightEffect Number(this LightEffect e, string key, string label, string def, string group, double min = double.NaN, double max = double.NaN, double step = 1) =>
            e.Add(new PropDef { Key = key, Label = label, Default = def, Group = group, Kind = PropKind.Number, Min = min, Max = max, Step = step });

        private static LightEffect Color(this LightEffect e, string key, string label, string def, string group, string help = null) =>
            e.Add(new PropDef { Key = key, Label = label, Default = def, Group = group, Kind = PropKind.Color, Help = help });

        private static LightEffect Bool(this LightEffect e, string key, string label, string def, string group) =>
            e.Add(new PropDef { Key = key, Label = label, Default = def, Group = group, Kind = PropKind.Bool });

        private static LightEffect Choice(this LightEffect e, string key, string label, string def, string group, params string[] choices) =>
            e.Add(new PropDef { Key = key, Label = label, Default = def, Group = group, Kind = PropKind.Choice, Choices = choices });

        private static LightEffect Formula(this LightEffect e, string key, string label, string def, string group, string help = null) =>
            e.Add(new PropDef { Key = key, Label = label, Default = def, Group = group, Kind = PropKind.Formula, Help = help });

        private static LightEffect Fill(this LightEffect e) => e
            .Choice("direction", "Direction", "Edges to center", "Fill", Directions)
            .Bool("smooth", "Smooth leading LED", "true", "Fill");

        private static LightEffect Colors(this LightEffect e, string low, string mid, string high, double midAt, double highAt) => e
            .Choice("colorMode", "Color mode", "Bands", "Colors", "Bands", "Gradient", "By level")
            .Color("low", "Low", low, "Colors")
            .Color("mid", "Middle", mid, "Colors")
            .Color("high", "High", high, "Colors")
            .Number("midAt", "Middle from", midAt.ToString(System.Globalization.CultureInfo.InvariantCulture), "Colors", 0, 1, 0.05)
            .Number("highAt", "High from", highAt.ToString(System.Globalization.CultureInfo.InvariantCulture), "Colors", 0, 1, 0.05);
    }
}
