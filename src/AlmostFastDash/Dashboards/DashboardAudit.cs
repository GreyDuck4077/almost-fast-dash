using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;

namespace AlmostFastDash.Dashboards
{
    /// <summary>A piece of text as a widget drew it: its size after any shrinking to fit, its color and where its letters sit.</summary>
    internal sealed class DrawnText
    {
        public DashWidget Widget;
        public string Text;
        public float Size;
        public Color Color;
        public RectangleF Ink;
        /// <summary>The box the text was laid out in: its letters should stay inside it.</summary>
        public RectangleF Box;
    }

    /// <summary>
    /// Checks that a dashboard can be read at a glance. Every screen (and every tap view of it) is drawn twice: once
    /// noting each piece of text as it's actually drawn, inside every widget and after any shrinking to fit, and once
    /// with all the text left out, to see what is behind it. Each piece of text must be:
    /// - at least the readable floor (dense rows in tables and gauges may be a little under it);
    /// - in contrast with what is behind it by at least 4.5 to 1 (3 to 1 for large text), the WCAG level for text.
    /// </summary>
    internal static class DashboardAudit
    {
        public const double Contrast = 4.5, LargeContrast = 3.0, LargeText = 24;

        internal sealed class Finding
        {
            public string Screen, Widget, Text, Problem;
            public override string ToString() => Screen + " / " + Widget + " \"" + Text + "\": " + Problem;
        }

        /// <summary>The widgets whose own rows of text may sit a little under the floor (see <see cref="WidgetContext.MinDenseText"/>).</summary>
        private static readonly HashSet<string> Dense = new HashSet<string> { "relative", "standings", "tyres", "trackmap", "traffic", "radar", "laphistory", "arc" };

        public static List<Finding> Run(DashboardSettings dashboard, UnitSystem units, TelemetrySnapshot snapshot = null)
        {
            var findings = new List<Finding>();
            DashLayout layout = dashboard.Design;
            if (layout?.Screens == null) return findings;
            snapshot = snapshot ?? IRacingTelemetrySource.CreatePreviewSnapshot();
            double floor = ReadableText.FloorFor(layout), dense = Math.Round(floor * 0.86);
            foreach (DashScreen screen in layout.Screens)
            {
                // Each tap view of the screen, one at a time (the default views first).
                var views = new List<KeyValuePair<string, int>> { new KeyValuePair<string, int>("", 0) };
                foreach (DashWidget w in screen.Widgets.Where(w => !string.IsNullOrWhiteSpace(w.Get("tapName", ""))))
                    for (int v = 1; v < LayoutRenderer.TapViews(w); v++) views.Add(new KeyValuePair<string, int>(w.Get("tapName", "").Trim(), v));
                foreach (KeyValuePair<string, int> view in views)
                {
                    string where = screen.Name + (view.Key.Length > 0 ? " (" + view.Key + " view " + (view.Value + 1) + ")" : "");
                    List<DrawnText> drawn;
                    Bitmap behind;
                    using (LayoutRenderer withText = Renderer(dashboard, units, view), withoutText = Renderer(dashboard, units, view))
                    {
                        withText.TextLog = new List<DrawnText>();
                        using (withText.Render(snapshot, false, snapshot.Gear, 1, screen)) { }
                        drawn = withText.TextLog;
                        withoutText.SkipText = true;
                        behind = withoutText.Render(snapshot, false, snapshot.Gear, 1, screen);
                    }
                    using (behind)
                    {
                        int[] pixels = Pixels(behind);
                        foreach (DrawnText text in drawn)
                        {
                            if (text.Color.A < 40) continue;
                            string name = text.Widget?.Name ?? "?";
                            bool isDense = text.Widget != null && Dense.Contains(text.Widget.Type);
                            double least = isDense ? dense : floor;
                            if (text.Size < least - 0.25)
                                findings.Add(new Finding { Screen = where, Widget = name, Text = text.Text, Problem = "drawn at " + text.Size.ToString("0.#") + " px, under " + least + " px" });
                            Color back = Behind(pixels, behind.Width, behind.Height, text.Ink);
                            double ratio = Ratio(Over(text.Color, back), back), needed = text.Size >= LargeText ? LargeContrast : Contrast;
                            if (ratio < needed)
                                findings.Add(new Finding { Screen = where, Widget = name, Text = text.Text, Problem = "contrast " + ratio.ToString("0.0") + ":1, needs " + needed + ":1" });
                            // Letters past the widget's own box are cut off or drawn under its neighbors (a turned widget's
                            // text is placed before turning, so it can't be judged this way).
                            if (text.Widget != null && (!double.TryParse(text.Widget.Get("rotate", "0"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double turn) || Math.Abs(turn) < 0.01) &&
                                !RectangleF.Inflate(text.Widget.Bounds, 2, 2).Contains(text.Ink))
                                findings.Add(new Finding { Screen = where, Widget = name, Text = text.Text, Problem = "reaches past the widget's edge" });
                            // Letters below or above the box they were laid out in: cut off, or covered by what's next to them.
                            else if (!text.Box.IsEmpty && (text.Ink.Bottom > text.Box.Bottom + 2 || text.Ink.Top < text.Box.Top - 2))
                                findings.Add(new Finding { Screen = where, Widget = name, Text = text.Text, Problem = "taller than the space it was given" });
                        }
                        // Two pieces of text whose letters run into each other (a few pixels' grace for the space round letters).
                        var pieces = drawn.Where(d => d.Color.A >= 40).GroupBy(d => d.Ink.ToString() + d.Text).Select(g => g.First()).ToList();
                        for (int i = 0; i < pieces.Count; i++)
                            for (int j = i + 1; j < pieces.Count; j++)
                            {
                                RectangleF a = RectangleF.Inflate(pieces[i].Ink, -1.5f, -1.5f), b = RectangleF.Inflate(pieces[j].Ink, -1.5f, -1.5f);
                                if (!a.IntersectsWith(b)) continue;
                                findings.Add(new Finding
                                {
                                    Screen = where, Widget = pieces[i].Widget?.Name ?? "?", Text = pieces[i].Text,
                                    Problem = "runs into \"" + pieces[j].Text + "\" (" + (pieces[j].Widget?.Name ?? "?") + ")"
                                });
                            }
                    }
                }
            }
            // The same text found in several views or states is one problem.
            return findings.GroupBy(f => f.Widget + "|" + f.Problem.Split(',')[0] + "|" + f.Text).Select(g => g.First()).ToList();
        }

        private static LayoutRenderer Renderer(DashboardSettings dashboard, UnitSystem units, KeyValuePair<string, int> view)
        {
            var renderer = new LayoutRenderer(dashboard.Design.Width, dashboard.Design.Height, dashboard, units)
            {
                CacheStatic = false, RedrawChangesOnly = false, FlagClock = 0, FixedNow = new DateTime(2026, 6, 6, 18, 4, 0)
            };
            if (view.Key.Length > 0) renderer.Taps[view.Key] = view.Value;
            return renderer;
        }

        private static int[] Pixels(Bitmap bitmap)
        {
            var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var pixels = new int[bitmap.Width * bitmap.Height];
                Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                return pixels;
            }
            finally { bitmap.UnlockBits(data); }
        }

        /// <summary>The typical color behind a piece of text: the median brightness of the pixels its letters cover.</summary>
        private static Color Behind(int[] pixels, int width, int height, RectangleF ink)
        {
            int x0 = Math.Max(0, (int)ink.Left), x1 = Math.Min(width - 1, (int)Math.Ceiling(ink.Right));
            int y0 = Math.Max(0, (int)ink.Top), y1 = Math.Min(height - 1, (int)Math.Ceiling(ink.Bottom));
            var samples = new List<int>();
            for (int y = y0; y <= y1; y += 2)
                for (int x = x0; x <= x1; x += 2) samples.Add(pixels[y * width + x]);
            if (samples.Count == 0) return Color.Black;
            samples.Sort((a, b) => Luminance(Color.FromArgb(a)).CompareTo(Luminance(Color.FromArgb(b))));
            return Color.FromArgb(samples[samples.Count / 2] | unchecked((int)0xFF000000));
        }

        /// <summary>
        /// A color moved just far enough toward white (on a dark background) or black (on a light one) to stand out
        /// from it by <paramref name="ratio"/>; unchanged when it already does. Keeps its hue as far as it can.
        /// </summary>
        public static Color Readable(Color color, Color background, double ratio = Contrast) =>
            Remember(color, background, ratio, -1, () => MakeReadable(color, background, ratio));

        /// <summary>
        /// Painters ask for the same few colors every frame, and each answer takes some searching: answers are kept
        /// (per thread, as renderers on different threads draw at once).
        /// </summary>
        [ThreadStatic] private static Dictionary<Tuple<int, int, double, float>, Color> _answers;

        private static Color Remember(Color color, Color background, double ratio, float share, Func<Color> work)
        {
            if (_answers == null) _answers = new Dictionary<Tuple<int, int, double, float>, Color>();
            var key = Tuple.Create(color.ToArgb(), background.ToArgb(), ratio, share);
            if (_answers.TryGetValue(key, out Color known)) return known;
            if (_answers.Count > 4096) _answers.Clear();
            return _answers[key] = work();
        }

        private static Color MakeReadable(Color color, Color background, double ratio)
        {
            background = Color.FromArgb(255, background);
            if (Ratio(Over(color, background), background) >= ratio) return color;
            Color toward = Luminance(background) < 0.18 ? Color.White : Color.Black;
            for (int step = 1; step <= 20; step++)
            {
                float t = step / 20f;
                Color tried = Color.FromArgb(Math.Max(color.A, (int)(255 * t)), (int)(color.R + (toward.R - color.R) * t), (int)(color.G + (toward.G - color.G) * t), (int)(color.B + (toward.B - color.B) * t));
                if (Ratio(Over(tried, background), background) >= ratio) return tried;
            }
            return toward;
        }

        /// <summary>
        /// A color dimmed toward the background by up to <paramref name="share"/>, as far as it can go while still
        /// standing out from it by <paramref name="ratio"/>: dimmer, but never unreadable.
        /// </summary>
        public static Color Dimmed(Color color, Color background, float share, double ratio = Contrast) =>
            Remember(color, background, ratio, share, () => MakeDimmed(color, background, share, ratio));

        private static Color MakeDimmed(Color color, Color background, float share, double ratio)
        {
            background = Color.FromArgb(255, background);
            color = Readable(color, background, ratio);
            for (float t = share; t > 0; t -= 0.05f)
            {
                Color tried = Color.FromArgb(color.A, (int)(color.R + (background.R - color.R) * t), (int)(color.G + (background.G - color.G) * t), (int)(color.B + (background.B - color.B) * t));
                if (Ratio(Over(tried, background), background) >= ratio) return tried;
            }
            return color;
        }

        /// <summary>The better of dark ink or white for text on a fill.</summary>
        public static Color InkOn(Color fill) => Remember(fill, Color.Empty, 0, -2, () =>
        {
            Color dark = Color.FromArgb(10, 12, 16);
            Color solid = Color.FromArgb(255, fill);
            return Ratio(dark, solid) >= Ratio(Color.White, solid) ? dark : Color.White;
        });

        /// <summary>Text color laid over what is behind it.</summary>
        internal static Color Over(Color fore, Color back)
        {
            double a = fore.A / 255.0;
            return Color.FromArgb(255, (int)(fore.R * a + back.R * (1 - a)), (int)(fore.G * a + back.G * (1 - a)), (int)(fore.B * a + back.B * (1 - a)));
        }

        /// <summary>The WCAG contrast ratio of two colors, 1 to 21.</summary>
        public static double Ratio(Color a, Color b)
        {
            double la = Luminance(a), lb = Luminance(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        private static double Luminance(Color c)
        {
            double Channel(int v)
            {
                double s = v / 255.0;
                return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        }
    }
}
