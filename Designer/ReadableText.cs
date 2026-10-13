using System;
using System.Globalization;
using System.Linq;

namespace AlmostFastOverlays
{
    /// <summary>
    /// The smallest text a wheel screen can show and still be read at a glance: 14 px on an 854×480 screen,
    /// scaled for other sizes. Every template is built to it, and saved dashboards are brought up to it. Text
    /// that was smaller is raised to the floor; its box grows to hold it (about its middle), and wide letter
    /// spacing is tightened so a label doesn't outgrow its place.
    /// </summary>
    internal static class ReadableText
    {
        public const double Floor = 14;
        private const double MaxTracking = 1.6;

        /// <summary>The floor for a design of this size.</summary>
        public static double FloorFor(DashLayout layout) =>
            Math.Max(10, Math.Round(Floor * Math.Min(layout.Width / (double)ScreenSizes.DefaultWidth, layout.Height / (double)ScreenSizes.DefaultHeight)));

        /// <summary>Raises every text size in the design below the floor to it. Returns how many settings changed.</summary>
        public static int Apply(DashLayout layout)
        {
            if (layout?.Screens == null) return 0;
            double floor = FloorFor(layout);
            int changed = 0;
            foreach (DashWidget widget in layout.AllWidgets())
            {
                WidgetType type = WidgetCatalog.Find(widget.Type);
                if (type == null) continue;
                switch (widget.Type)
                {
                    case "text":
                        if (Raise(widget, type, "size", floor, ref changed))
                        {
                            Tighten(widget, type, "tracking");
                            if (widget.H < floor + 4)
                            {
                                double grow = floor + 4 - widget.H;
                                widget.Y = Math.Round(widget.Y - grow / 2, 1);
                                widget.H = floor + 4;
                            }
                        }
                        break;
                    case "metric":
                        if (Raise(widget, type, "labelSize", floor, ref changed)) Tighten(widget, type, "labelTracking");
                        Raise(widget, type, "valueSize", floor, ref changed);
                        // 0 means "sized from the value": leave that alone.
                        if (Number(widget, type, "unitSize") > 0) Raise(widget, type, "unitSize", floor, ref changed);
                        break;
                    case "relative":
                    case "standings":
                    case "tyres":
                        Raise(widget, type, "size", floor, ref changed);
                        break;
                    case "laphistory":
                        Raise(widget, type, "labelSize", floor, ref changed);
                        break;
                    case "arc":
                        if (Expr.Bool(widget.Get("numbers", type.Prop("numbers")?.Default))) Raise(widget, type, "numberSize", floor, ref changed);
                        break;
                    case "gear":
                        if (Expr.Bool(widget.Get("neighbors", type.Prop("neighbors")?.Default))) Raise(widget, type, "neighborSize", floor, ref changed);
                        break;
                }
            }
            return changed;
        }

        /// <summary>The number a setting holds, or NaN when it's a formula or empty.</summary>
        private static double Number(DashWidget widget, WidgetType type, string key)
        {
            string raw = widget.Get(key, type.Prop(key)?.Default ?? "");
            if (string.IsNullOrWhiteSpace(raw) || raw.TrimStart().StartsWith("=")) return double.NaN;
            return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : double.NaN;
        }

        private static bool Raise(DashWidget widget, WidgetType type, string key, double floor, ref int changed)
        {
            double value = Number(widget, type, key);
            if (double.IsNaN(value) || value >= floor) return false;
            widget.Set(key, floor.ToString(CultureInfo.InvariantCulture));
            changed++;
            return true;
        }

        private static void Tighten(DashWidget widget, WidgetType type, string key)
        {
            double tracking = Number(widget, type, key);
            if (!double.IsNaN(tracking) && tracking > MaxTracking) widget.Set(key, MaxTracking.ToString(CultureInfo.InvariantCulture));
        }
    }
}
