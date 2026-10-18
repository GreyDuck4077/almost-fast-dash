using System;
using System.Collections.Generic;
using System.Drawing;

namespace AlmostFastDash.Rendering
{
    /// <summary>One widget being drawn: its bounds and typed access to its resolved properties.</summary>
    internal sealed class WidgetContext
    {
        private readonly Dictionary<string, object> _resolved = new Dictionary<string, object>();

        public WidgetContext(LayoutRenderer renderer, Graphics g, EvalScope scope, DashWidget widget, WidgetType type)
        {
            Renderer = renderer;
            G = g;
            Scope = scope;
            Widget = widget;
            Type = type;
            B = widget.Bounds;
        }

        public LayoutRenderer Renderer { get; }
        /// <summary>Draw a sample of what it shows (the designer's ghost of a widget with nothing to show now).</summary>
        public bool Sample { get; set; }
        public Graphics G { get; }
        public EvalScope Scope { get; }
        public DashWidget Widget { get; }
        public WidgetType Type { get; }
        public RectangleF B { get; }
        public TelemetrySnapshot T => Scope.T;
        public int PreviousGear { get; set; }
        public float GearProgress { get; set; } = 1;
        /// <summary>The widget is mirrored; its text is turned back so it still reads the right way round.</summary>
        public bool FlipX { get; set; }
        public bool FlipY { get; set; }

        /// <summary>The smallest text this design can show readably (see <see cref="ReadableText"/>); dense rows may go a little under it.</summary>
        public float MinText => (float)ReadableText.FloorFor(Renderer?.Layout ?? new DashLayout());
        public float MinDenseText => (float)Math.Round(MinText * 0.86);

        public string Raw(string key) => Widget.Get(key, Type.Prop(key)?.Default ?? "");

        public bool IsFormula(string key)
        {
            PropDef def = Type.Prop(key);
            return def != null && LayoutRenderer.IsFormula(Raw(key), def);
        }

        /// <summary>The property's value: a formula result, or the literal text.</summary>
        public object Value(string key)
        {
            if (_resolved.TryGetValue(key, out object cached)) return cached;
            string raw = Raw(key);
            object value = IsFormula(key) ? Expr.Evaluate(LayoutRenderer.Strip(raw), Scope) : raw;
            _resolved[key] = value;
            return value;
        }

        public double Num(string key)
        {
            object value = Value(key);
            if (value is string s && string.IsNullOrWhiteSpace(s)) value = Type.Prop(key)?.Default;
            double d = Expr.Num(value);
            return double.IsNaN(d) || double.IsInfinity(d) ? 0 : d;
        }

        public bool Bool(string key) => Expr.Bool(Value(key));
        public string Text(string key) => Expr.Text(Value(key));
        public string Choice(string key) => Text(key);

        public Color Color(string key, Color? fallback = null)
        {
            object value = Value(key);
            Color? color = Expr.Color(value, Scope);
            if (color.HasValue) return color.Value;
            if (fallback.HasValue) return fallback.Value;
            return Expr.Color(Type.Prop(key)?.Default, Scope) ?? System.Drawing.Color.White;
        }

        /// <summary>Null when the property is empty, for optional colors such as fills.</summary>
        public Color? OptionalColor(string key)
        {
            object value = Value(key);
            if (value is string s && string.IsNullOrWhiteSpace(s)) return null;
            return Expr.Color(value, Scope);
        }

        public Color Theme(string token) => Expr.Color(token, Scope) ?? System.Drawing.Color.White;

        public Font Font(float px, bool bold) => Renderer.Font(Text("font"), px, bold);
    }
}
