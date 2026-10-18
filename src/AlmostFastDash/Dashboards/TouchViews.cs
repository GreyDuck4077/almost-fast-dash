using System;
using System.Linq;

namespace AlmostFastDash.Dashboards
{
    /// <summary>
    /// The flagships' touch views: tapping a glass panel on a touch screen swaps what it shows. Each is
    /// an ordinary designer setup: the panel gets a tap name, its first view's widgets show while
    /// tap("name") == 0, and the second view's widgets while it is 1. The steps work by widget name,
    /// so they also upgrade a saved copy of the dashboard without touching anything else in it.
    /// </summary>
    internal static class TouchViews
    {
        public static void Roady(DashLayout layout)
        {
            DashScreen race = layout.Screens.FirstOrDefault(s => s.Name == "Race");
            if (race == null || Done(race)) return;

            // Throttle and brake, or the incidents and the time.
            View(race, "Pedals panel", "pedals", new[] { "Pedals label", "Pedals" }, (add, p, s) =>
            {
                add(Metric("Incidents", p.X + 8 * s, p.Y + 8 * s, 140 * s, 82 * s, "INCIDENTS", "=Incidents + \"x\"", s,
                    "valueColor", "=Incidents > 0 ? $warn : $text"));
                add(Metric("Time", p.X + p.W - 148 * s, p.Y + 8 * s, 140 * s, 82 * s, "TIME", "=Clock", s, "align", "Right"));
            });

            // The car's settings, or its temperatures.
            View(race, "Car panel", "car", new[] { "Bias", "TC", "ABS" }, (add, p, s) =>
            {
                add(Metric("Water", p.X + 8 * s, p.Y + 8 * s, p.W / 2 - 8 * s, 82 * s, "WATER", "=format(WaterTemp, \"0\") + \"°\"", s,
                    "align", "Center"));
                add(Metric("Oil", p.X + p.W / 2, p.Y + 8 * s, p.W / 2 - 8 * s, 82 * s, "OIL", "=format(OilTemp, \"0\") + \"°\"", s,
                    "align", "Center"));
            });

            // Fuel and laps left, or the car ahead and its driver.
            View(race, "Fuel panel", "fuel", new[] { "Fuel", "Fuel laps", "Fuel level" }, (add, p, s) =>
            {
                add(Text("Ahead label", p.X + 16 * s, p.Y + 10 * s, 200 * s, 14 * s, "CAR AHEAD", 10 * s, "tracking", Num(2.5 * s),
                    "weight", "Regular", "color", "muted", "align", "Left"));
                add(Text("Ahead driver", p.X + 16 * s, p.Y + 28 * s, p.W - 150 * s, 36 * s,
                    "=AheadName == \"---\" ? \"Nobody ahead\" : AheadName", 22 * s, "align", "Left"));
                add(Text("Ahead gap", p.X + p.W - 130 * s, p.Y + 28 * s, 114 * s, 36 * s,
                    "=AheadName == \"---\" ? \"--\" : \"-\" + format(AheadGap, \"0.0\")", 26 * s,
                    "align", "Right", "color", "good"));
                add(Text("Ahead detail", p.X + 16 * s, p.Y + 66 * s, p.W - 32 * s, 18 * s,
                    "=AheadName == \"---\" ? \"\" : \"#\" + AheadNumber + (AheadIRating > 0 ? \"  ·  \" + format(AheadIRating / 1000, " +
                        "\"0.0\") + \"k iRATING\" : \"\")",
                    12 * s, "weight", "Regular", "tracking", "1", "align", "Left", "color", "muted"));
            });
        }

        public static void Roundy(DashLayout layout)
        {
            DashScreen race = layout.Screens.FirstOrDefault(s => s.Name == "Race");
            if (race == null || Done(race)) return;

            // The race, last lap, fuel and leader; or incidents, best lap, laps in the tank and the time.
            DashWidget progress = race.Widgets.FirstOrDefault(w => w.Name == "Race progress");
            View(race, "Strip", "strip", new[] { "SessionType", "LAST LAP", "FUEL TO END", "TO THE LEADER" }, (add, p, s) =>
            {
                string[] labels = { "INCIDENTS", "BEST LAP", "LAPS IN TANK", "TIME" };
                string[] values = { "=Incidents + \"x\"", "=laptime(BestLap)",
                    "=FuelLapsLeft < 0 ? \"--.-\" : format(FuelLapsLeft, \"0.0\")", "=Clock" };
                string[] tones = { "=Incidents > 0 ? $warn : $text", "purple", "=steps(FuelLapsLeft, $text, 0, $bad, 2, $warn, 5, $text)",
                    "text" };
                double w = p.W / 4;
                for (int i = 0; i < 4; i++)
                    add(Metric(labels[i] == "TIME" ? "Strip time" : labels[i], p.X + i * w, p.Y + 8 * s, w, 76 * s, labels[i], values[i], s,
                        "align", "Center", "valueSize", Num(26 * s), "labelSize", Num(11 * s), "labelTracking", Num(2.5 * s),
                        "valueColor", tones[i]));
            });
            // Room under the lap blocks for the view dots.
            if (progress != null) progress.Y = Math.Round(progress.Y - 6 * progress.H / 5);
        }

        private static bool Done(DashScreen screen) => screen.Widgets.Any(w => !string.IsNullOrWhiteSpace(w.Get("tapName", "")));

        /// <summary>
        /// Makes a panel a two-view tap zone: its own widgets show on the first view, and the ones
        /// <paramref name="second"/> adds (placed just above them) on the second.
        /// </summary>
        private static void View(DashScreen screen, string panelName, string tapName, string[] first,
            Action<Action<DashWidget>, DashWidget, double> second)
        {
            DashWidget panel = screen.Widgets.FirstOrDefault(w => w.Name == panelName);
            if (panel == null) return;
            double s = panel.H / Base(panelName);
            panel.Set("tapName", tapName);
            panel.Set("tapViews", "2");
            string firstView = "tap(\"" + tapName + "\") == 0", secondView = "tap(\"" + tapName + "\") == 1";
            int insertAt = screen.Widgets.IndexOf(panel) + 1;
            foreach (DashWidget widget in screen.Widgets.Where(w => first.Contains(w.Name)))
            {
                Only(widget, firstView);
                insertAt = Math.Max(insertAt, screen.Widgets.IndexOf(widget) + 1);
            }
            // The first view leaves room along the panel's bottom edge for the view dots: a thin bar moves up,
            // anything taller (a graph, a value) ends a little sooner.
            double limit = panel.Y + panel.H - 14 * s;
            foreach (DashWidget widget in screen.Widgets.Where(w => first.Contains(w.Name) && w.Y + w.H > limit))
            {
                if (widget.H <= 10 * s) widget.Y = Math.Round(limit - widget.H - 2 * s);
                else widget.H = Math.Max(4, Math.Round(limit - widget.Y));
            }
            second(widget =>
            {
                Only(widget, secondView);
                screen.Widgets.Insert(insertAt++, widget);
            }, panel, s);
        }

        /// <summary>The height each panel was drawn at in the 854×480 templates, for scaling a saved copy of another size.</summary>
        private static double Base(string panelName) => panelName == "Strip" ? 110 : 104;

        /// <summary>Adds a condition to a widget's Show when, keeping any it already has.</summary>
        internal static void Only(DashWidget widget, string condition)
        {
            string existing = widget.Get("visible", "") ?? "";
            widget.Set("visible", string.IsNullOrWhiteSpace(existing) ? condition : "(" + existing.TrimStart('=') + ") && " + condition);
        }

        private static string Num(double value) => Math.Round(value, 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

        private static DashWidget Make(string type, string name, double x, double y, double w, double h, string[] props)
        {
            var widget = new DashWidget { Type = type, Name = name, X = Math.Round(x), Y = Math.Round(y), W = Math.Round(w),
                H = Math.Round(h) };
            for (int i = 0; i + 1 < props.Length; i += 2) widget.Set(props[i], props[i + 1]);
            return widget;
        }

        private static DashWidget Metric(string name, double x, double y, double w, double h, string label, string value, double s,
            params string[] props) =>
            Make("metric", name, x, y, w, h,
            new[] { "label", label, "value", value, "valueSize", Num(28 * s), "labelSize", Num(10 * s),
            "labelTracking", Num(2.5 * s) }.Concat(props)
            .ToArray());

        private static DashWidget Text(string name, double x, double y, double w, double h, string text, double size,
            params string[] props) =>
            Make("text", name, x, y, w, h, new[] { "text", text, "size", Num(size) }.Concat(props).ToArray());
    }
}
