using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;

namespace AlmostFastDash.Rendering
{
    internal static partial class WidgetPainters
    {
        // ================================================================== Graphs

        public static void Trace(WidgetContext c)
        {
            Graphics g = c.G;
            RectangleF b = c.B;
            if (c.Bool("grid")) Paint.Grid(g, b, c.Theme("border"));
            float width = (float)c.Num("lineWidth");
            bool fill = c.Bool("fill");
            Paint.Series(g, b, c.T.ThrottleTrace, v => v, c.Color("throttleColor"), width, fill);
            Paint.Series(g, b, c.T.BrakeTrace, v => v, c.Color("brakeColor"), width, fill);
        }

        public static void Graph(WidgetContext c)
        {
            Graphics g = c.G;
            RectangleF b = c.B;
            GraphHistory history = c.Renderer.Graph(c);
            if (c.Bool("grid")) Paint.Grid(g, b, c.Color("gridColor"));
            int n = history.Count;
            bool second = !string.IsNullOrWhiteSpace(c.Raw("value2"));
            Func<int, double> firstAt = history.First, secondAt = history.Second;
            if (n < GraphHistory.Points && c.T.IsPreview)
            {
                // A preview hasn't seen the whole span yet: the part it hasn't seen shows a representative trace
                // around the current value, and the real samples follow on as they arrive.
                double now1 = c.Num("value"), now2 = second ? c.Num("value2") : 0;
                int missing = GraphHistory.Points - n;
                Func<int, double> realFirst = history.First, realSecond = history.Second;
                // A value at zero now (a pedal at rest) gets occasional presses; anything else drifts around its value.
                Func<int, double> madeSecond = Math.Abs(now2) < 1e-6
                    ? (Func<int, double>)(i => 0.9 * Math.Pow(Math.Max(0, Math.Sin(i / 9.0 + 2)), 6))
                    : i => now2 * (0.9 + 0.1 * Math.Sin(i / 13.0 + 1));
                n = GraphHistory.Points;
                firstAt = i => i >= missing ? realFirst(i - missing) : now1 * (0.8 + 0.2 * Math.Sin(i / 10.0) * Math.Cos(i / 27.0));
                secondAt = i => i >= missing ? realSecond(i - missing) : madeSecond(i);
            }
            double lo = c.Num("min"), hi = c.Num("max");
            if (hi <= lo)
            {
                // Automatic range: what's on the graph, with a little room above and below.
                lo = double.MaxValue;
                hi = double.MinValue;
                for (int i = 0; i < n; i++)
                    foreach (double v in second ? new[] { firstAt(i), secondAt(i) } : new[] { firstAt(i) })
                        if (!double.IsNaN(v) && !double.IsInfinity(v)) { lo = Math.Min(lo, v); hi = Math.Max(hi, v); }
                if (lo > hi) { lo = 0; hi = 1; }
                double room = Math.Max(1e-6, (hi - lo) * 0.1);
                if (hi - lo < 1e-6) room = Math.Max(1, Math.Abs(hi) * 0.1);
                lo -= room;
                hi += room;
            }
            string style = c.Choice("style");
            float lw = (float)c.Num("lineWidth"), step = b.Width / (GraphHistory.Points - 1);
            float Y(double v) => b.Bottom - (float)(Math.Max(0, Math.Min(1, (v - lo) / (hi - lo))) * b.Height);

            void Line(Func<int, double> at, Color color)
            {
                if (n < 2) return;
                // Newest on the right; a gap in the data (an unknown value) breaks the line.
                var run = new List<PointF>();
                void Flush()
                {
                    if (run.Count >= 2)
                    {
                        if (style == "Area")
                            using (var area = new GraphicsPath())
                            {
                                area.AddLines(run.ToArray());
                                area.AddLine(run[run.Count - 1], new PointF(run[run.Count - 1].X, b.Bottom));
                                area.AddLine(new PointF(run[run.Count - 1].X, b.Bottom), new PointF(run[0].X, b.Bottom));
                                area.CloseFigure();
                                using (var brush = new LinearGradientBrush(Paint.Grow(b, 0, 1), Color.FromArgb(90 * color.A / 255, color), Color.FromArgb(6 * color.A / 255, color), 90f))
                                    g.FillPath(brush, area);
                            }
                        using (var pen = new Pen(color, lw) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLines(pen, run.ToArray());
                    }
                    run.Clear();
                }
                for (int i = 0; i < n; i++)
                {
                    double v = at(i);
                    if (double.IsNaN(v) || double.IsInfinity(v)) { Flush(); continue; }
                    run.Add(new PointF(b.Right - (n - 1 - i) * step, Y(v)));
                }
                Flush();
            }

            void Bars(Func<int, double> at, Color color, float offset, float width)
            {
                using (var brush = new SolidBrush(color))
                    for (int i = 0; i < n; i++)
                    {
                        double v = at(i);
                        if (double.IsNaN(v)) continue;
                        float x = b.Right - (n - i) * step + offset, y = Y(v);
                        if (b.Bottom - y >= 0.5f) g.FillRectangle(brush, x, y, Math.Max(0.5f, width), b.Bottom - y);
                    }
            }

            Color first = c.Color("color"), other = c.Color("color2");
            if (style == "Bars")
            {
                float w = step * (second ? 0.42f : 0.8f);
                Bars(firstAt, first, step * 0.1f, w);
                if (second) Bars(secondAt, other, step * 0.52f, w);
            }
            else
            {
                if (second) Line(secondAt, other);
                Line(firstAt, first);
            }

            string label = c.Text("label");
            if (!string.IsNullOrEmpty(label))
            {
                float size = (float)c.Num("labelSize");
                Paint.Text(g, c, label, size, true, c.Color("labelColor"), new RectangleF(b.X + 6, b.Y + 4, b.Width - 12, size * 1.3f), StringAlignment.Near, "Top", true, 0, Color.Empty, false);
            }
        }

        public static void DeltaTrace(WidgetContext c)
        {
            Graphics g = c.G;
            RectangleF b = c.B;
            float[] data = c.T.DeltaTrace ?? new float[0];
            float range = (float)Math.Max(0.05, c.Num("range"));
            float mid = b.Y + b.Height / 2;
            using (var pen = new Pen(c.Theme("border"), 1)) g.DrawLine(pen, b.X, mid, b.Right, mid);
            if (data.Length < 2) return;
            var points = new PointF[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                float v = Math.Max(-1, Math.Min(1, data[i] / range));
                points[i] = new PointF(b.X + b.Width * i / (data.Length - 1), mid - v * b.Height / 2);
            }
            Color good = c.Color("goodColor"), bad = c.Color("badColor");
            float lw = (float)c.Num("lineWidth");
            foreach (bool upper in new[] { true, false })
            {
                GraphicsState state = g.Save();
                g.SetClip(upper ? new RectangleF(b.X, b.Y - lw, b.Width, b.Height / 2 + lw) : new RectangleF(b.X, mid, b.Width, b.Height / 2 + lw), CombineMode.Intersect);
                Color tone = upper ? good : bad;
                if (c.Bool("fill"))
                    using (var area = new GraphicsPath())
                    {
                        area.AddLines(points);
                        area.AddLine(points[points.Length - 1], new PointF(b.Right, mid));
                        area.AddLine(new PointF(b.Right, mid), new PointF(b.X, mid));
                        area.CloseFigure();
                        using (var brush = new SolidBrush(Color.FromArgb(50, tone))) g.FillPath(brush, area);
                    }
                using (var pen = new Pen(tone, lw) { LineJoin = LineJoin.Round }) g.DrawLines(pen, points);
                g.Restore(state);
            }
        }

        public static void LapHistory(WidgetContext c)
        {
            Graphics g = c.G;
            RectangleF b = c.B;
            bool stint = c.Choice("source") == "This stint";
            int count = Math.Max(2, Math.Min(40, (int)Math.Round(c.Num("laps"))));
            double[] laps = ((stint ? c.T.StintLapSeconds : c.T.RecentLapSeconds) ?? new double[0]).Where(l => l > 0).ToArray();
            if (laps.Length > count) laps = laps.Skip(laps.Length - count).ToArray();
            if (laps.Length == 0)
            {
                Paint.Text(g, c, stint ? "No green laps yet" : "No laps yet", 13, false, c.Theme("muted"), b, StringAlignment.Center, "Middle", true, 0, Color.Empty, false);
                return;
            }
            if (c.Choice("style") == "Columns")
            {
                LapColumns(c, laps, count, stint);
                return;
            }
            double fastest = laps.Min(), slowest = laps.Max();
            float rowH = b.Height / count;
            bool times = c.Bool("times");
            float textW = times ? Math.Min(b.Width * 0.42f, rowH * 4.6f) : 0;
            for (int i = 0; i < laps.Length; i++)
            {
                var row = new RectangleF(b.X, b.Y + i * rowH, b.Width, rowH);
                bool best = c.T.BestLapSeconds > 0 && Math.Abs(laps[i] - c.T.BestLapSeconds) < 0.0015;
                Color color = best ? c.Color("bestColor") : c.Color("color");
                if (times)
                    Paint.Text(g, c, Fmt.LapTime(laps[i]), rowH * 0.55f, true, best ? color : c.Color("textColor"), new RectangleF(row.X, row.Y, textW - 8, row.Height), StringAlignment.Near, "Middle", true, 0, Color.Empty, false);
                double share = slowest - fastest < 0.001 ? 1 : 1 - (laps[i] - fastest) / (slowest - fastest) * 0.6;
                float barH = Math.Max(3, rowH * 0.32f);
                var track = new RectangleF(row.X + textW, row.Y + (rowH - barH) / 2, row.Width - textW, barH);
                DashKit.Fill(g, track, c.Theme("track"), barH / 2);
                DashKit.Fill(g, new RectangleF(track.X, track.Y, (float)(track.Width * share), barH), color, barH / 2);
            }
        }

        /// <summary>
        /// Lap falloff as columns, oldest on the left: each column rises with how much slower the lap was
        /// than the best shown, blending toward the slow color; the best lap is a short column in its own color.
        /// </summary>
        private static void LapColumns(WidgetContext c, double[] laps, int count, bool stint)
        {
            Graphics g = c.G;
            RectangleF b = c.B;
            double best = laps.Min();
            double range = c.Num("range");
            // The automatic scale fits the laps from the best onward: the ones before it were still coming
            // up to speed and simply stand full height.
            if (range <= 0) range = Math.Max(0.3, (laps.Skip(Array.IndexOf(laps, best)).Max() - best) * 1.15);
            bool labels = c.Bool("showDelta");
            float size = (float)c.Num("labelSize");
            float labelH = labels ? size * 1.5f : 0;
            float slot = b.Width / count, gap = Math.Max(1.5f, slot * (float)c.Num("gap") / 100f), barW = Math.Max(1, slot - gap);
            float top = b.Y + labelH, height = Math.Max(4, b.Bottom - top), stub = Math.Max(3, Math.Min(height * 0.06f, barW * 0.5f));
            float radius = Math.Min(barW / 2, (float)c.Num("radius"));
            Color color = c.Color("color"), slow = c.Color("slowColor"), bestColor = c.Color("bestColor"), track = c.Color("track");
            bool everyLabel = slot >= size * 3.1f;
            int bestAt = Array.IndexOf(laps, best);
            Color warmUp = c.Theme("dim");
            for (int i = 0; i < laps.Length; i++)
            {
                float x = b.X + i * slot + gap / 2;
                if (track.A > 0) Paint.FillTop(g, new RectangleF(x, top, barW, height), track, radius);
                double over = Math.Max(0, laps[i] - best);
                bool isBest = over < 0.0005;
                double share = Math.Min(1, over / range);
                float h = isBest ? stub : stub + (height - stub) * (float)share;
                // Laps before the best were still coming up to speed: shown, but dimmed, as they aren't falloff.
                Color tone = isBest ? bestColor : stint && i < bestAt ? warmUp : Mix(color, slow, share);
                Paint.FillTop(g, new RectangleF(x, b.Bottom - h, barW, h), tone, radius);
                if (labels && (everyLabel || i == laps.Length - 1))
                    Paint.Text(g, c, isBest ? (stint ? "BEST" : Fmt.LapTime(laps[i])) : "+" + over.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), size, true,
                        isBest ? bestColor : c.Color("textColor"), new RectangleF(x + barW / 2 - slot, b.Bottom - h - labelH, slot * 2, labelH), StringAlignment.Center, "Middle", true, 0, Color.Empty, false);
            }
        }
    }
}
