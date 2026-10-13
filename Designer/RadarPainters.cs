using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;

namespace AlmostFastOverlays
{
    /// <summary>
    /// The Radar and Traffic widgets. The radar is a top view of your car on a road of five lanes, the cars
    /// around you drawn to scale where they are, range rings a car length apart, and sensor waves ahead and
    /// behind that run up to the next car and turn amber, then red, as it gets close. The traffic strip lays
    /// the cars within a few seconds either way along a line, in class colours.
    /// </summary>
    internal static class RadarPainters
    {
        public static void Radar(WidgetContext c)
        {
            Graphics g = c.G;
            RectangleF b = c.B;
            TelemetrySnapshot t = c.T;
            if (b.Width < 24 || b.Height < 24) return;
            const double L = RadarTracker.CarLength;
            double range = Math.Max(1, Math.Min(16, c.Num("range")));
            float half = b.Height / 2;
            // Range car lengths each way, with room for the whole of a car at the very edge.
            float ppm = (float)(half / ((range + 0.5) * L));
            float len = (float)(L * ppm), wid = len * 0.42f, lane = wid * 1.55f;
            if (lane * 5.2f > b.Width)
            {
                lane = b.Width / 5.2f;
                wid = lane / 1.55f;
            }
            float cx = b.X + b.Width / 2, cy = b.Y + half;
            float Y(double meters) => cy - (float)(meters * ppm);
            // Everything fades out toward the top and bottom edges.
            float Fade(float y)
            {
                float d = Math.Abs(y - cy) / half;
                return d < 0.7f ? 1 : Math.Max(0, 1 - (d - 0.7f) / 0.3f);
            }

            GraphicsState state = g.Save();
            g.SetClip(b, CombineMode.Intersect);

            Color road = c.Color("roadColor");
            if (road.A > 0)
            {
                var area = new RectangleF(cx - lane * 2.5f, b.Y, lane * 5, b.Height);
                using (var brush = new LinearGradientBrush(new RectangleF(area.X, area.Y - 1, area.Width, area.Height + 2), road, road, 90f))
                {
                    brush.InterpolationColors = new ColorBlend
                    {
                        Colors = new[] { Color.FromArgb(0, road), road, road, Color.FromArgb(0, road) },
                        Positions = new[] { 0f, 0.18f, 0.82f, 1f }
                    };
                    g.FillRectangle(brush, area);
                }
            }

            if (c.Bool("rings"))
            {
                Color ring = c.Color("ringColor");
                for (int n = 1; n <= Math.Ceiling(range); n++)
                {
                    float r = (float)(n * L * ppm);
                    // Only whole rings: one cut off by the box's sides would look broken.
                    if (r * 1.12f > b.Width / 2 - 2) break;
                    int a = (int)(ring.A * (1 - 0.55 * (n - 1) / Math.Max(1, range)));
                    using (var pen = new Pen(Color.FromArgb(Math.Max(0, a), ring), Math.Max(1f, wid * 0.045f)))
                        g.DrawEllipse(pen, cx - r * 1.12f, cy - r, r * 2.24f, r * 2);
                }
            }

            if (c.Bool("lanes"))
            {
                Color lines = c.Color("laneColor");
                float width = Math.Max(1.5f, wid * 0.07f);
                // Lane dashes slide past as you drive (by your distance round the lap).
                const double Dash = 3.0, Period = 7.5;
                double travelled = c.Bool("moving") && t.TrackLengthMiles > 0 ? t.LapFraction * t.TrackLengthMiles * 1609.344 : 0;
                double phase = travelled % Period, reach = (range + 1) * L;
                foreach (float k in new[] { -1.5f, -0.5f, 0.5f, 1.5f })
                {
                    float x = cx + k * lane;
                    for (double m = -reach - Period - phase; m < reach + Period; m += Period)
                    {
                        float y1 = Y(m), y2 = Y(m + Dash), f = Fade((y1 + y2) / 2);
                        if (f <= 0) continue;
                        using (var pen = new Pen(Color.FromArgb((int)(lines.A * f), lines), width) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                            g.DrawLine(pen, x, y1, x, y2);
                    }
                }
                foreach (float k in new[] { -2.5f, 2.5f }) FadedLine(g, cx + k * lane, b, lines, width * 1.4f);
            }

            Color clear = c.Color("clearColor"), near = c.Color("nearColor"), close = c.Color("closeColor"), beside = c.Color("alongsideColor");
            double nearAt = c.Num("nearAt"), closeAt = c.Num("closeAt");
            RadarCar[] cars = t.Connected ? t.Radar ?? new RadarCar[0] : new RadarCar[0];

            // A wash of colour in the lane of a car alongside.
            foreach (RadarCar car in cars.Where(x => x.Alongside))
            {
                var wash = new RectangleF(cx + car.Lane * lane - lane / 2, Y(car.Meters) - len * 0.8f, lane, len * 1.6f);
                using (GraphicsPath path = Paint.Rounded(wash, lane * 0.35f))
                using (var brush = new PathGradientBrush(path) { CenterColor = Color.FromArgb(70, beside), SurroundColors = new[] { Color.FromArgb(0, beside) } })
                    g.FillPath(brush, path);
            }

            if (c.Bool("sensors") && t.Connected)
            {
                Sensor(g, cx, cy - len / 2, -1, t.AheadMeters, ppm, len, wid, half, clear, near, close, nearAt, closeAt);
                Sensor(g, cx, cy + len / 2, 1, t.BehindMeters, ppm, len, wid, half, clear, near, close, nearAt, closeAt);
            }

            Color others = c.Color("otherColor"), pace = c.Color("paceColor");
            bool classColors = c.Bool("classColors"), numbers = c.Bool("numbers");
            foreach (RadarCar car in cars)
            {
                float y = Y(car.Meters);
                if (y < b.Y - len || y > b.Bottom + len) continue;
                Color body = car.PaceCar ? pace : classColors && car.ClassColor.A > 0 ? car.ClassColor : others;
                if (car.Alongside) body = Mix(body, beside, 0.6f);
                GraphicsState placed = g.Save();
                g.TranslateTransform(cx + car.Lane * lane, y);
                DrawCar(g, c, len, wid, body, car.Alongside ? beside : Color.Empty, car.Alongside ? wid * 0.6f : 0, numbers ? car.Number : "", car.PaceCar);
                g.Restore(placed);
            }

            // Your car, in the middle.
            GraphicsState mine = g.Save();
            g.TranslateTransform(cx, cy);
            Color own = c.Color("carColor");
            DrawCar(g, c, len, wid, own, own, (float)c.Num("glow"), "", false);
            g.Restore(mine);

            // The next car in line beyond the edge: how many car lengths away.
            if (c.Bool("beyond") && t.Connected)
            {
                double edge = (half - len * 0.5f) / ppm;
                Color muted = c.Theme("muted");
                float size = Math.Max(10, Math.Min(16, wid * 0.62f));
                if (t.AheadMeters > edge) Beyond(g, c, cx, b.Y + size * 0.4f, -1, RadarTracker.Lengths(t.AheadMeters), size, muted);
                if (t.BehindMeters > edge) Beyond(g, c, cx, b.Bottom - size * 0.4f, 1, RadarTracker.Lengths(t.BehindMeters), size, muted);
            }
            g.Restore(state);
        }

        /// <summary>
        /// A sensor cone from your car's nose or tail with waves running up to the next car in line. It is
        /// the clear colour with room, the near colour within a few lengths and the close colour when tight.
        /// </summary>
        private static void Sensor(Graphics g, float cx, float edgeY, int dir, double meters, float ppm, float len, float wid, float half,
            Color clear, Color near, Color close, double nearAt, double closeAt)
        {
            double gap = RadarTracker.Lengths(meters);
            Color color = gap < 0 ? clear : gap < closeAt ? close : gap < nearAt ? near : clear;
            float maxReach = half - len / 2;
            float reach = meters < 0 ? maxReach : Math.Min(maxReach, (float)((meters - RadarTracker.CarLength) * ppm));
            reach = Math.Max(reach, len * 0.08f);
            const float Spread = 24;
            float inset = len * 0.18f, apexY = edgeY - dir * inset, radius = reach + inset;
            float start = (dir < 0 ? 270 : 90) - Spread;
            using (var cone = new GraphicsPath())
            {
                cone.AddPie(cx - radius, apexY - radius, radius * 2, radius * 2, start, Spread * 2);
                using (var brush = new LinearGradientBrush(new PointF(cx, apexY), new PointF(cx, apexY + dir * (radius + 1)), Color.FromArgb(110, color), Color.FromArgb(0, color)))
                    g.FillPath(brush, cone);
            }
            float spacing = Math.Max(7, (float)(RadarTracker.CarLength * 0.4 * ppm)), stroke = Math.Max(2f, wid * 0.13f);
            for (float r = inset + spacing * 0.75f; r < radius - stroke; r += spacing)
            {
                float f = 1 - 0.6f * r / radius;
                using (var pen = new Pen(Color.FromArgb((int)(235 * f), color), stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawArc(pen, cx - r, apexY - r, r * 2, r * 2, start + 5, Spread * 2 - 10);
            }
        }

        private static void Beyond(Graphics g, WidgetContext c, float x, float y, int dir, double lengths, float size, Color color)
        {
            float w = size * 0.55f, h = size * 0.3f;
            using (var pen = new Pen(color, Math.Max(1.5f, size * 0.12f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(pen, new[] { new PointF(x - w, y - dir * h), new PointF(x, y + dir * h * 0.2f), new PointF(x + w, y - dir * h) });
            var box = new RectangleF(x - size * 3, dir < 0 ? y + h : y - h - size * 1.3f, size * 6, size * 1.3f);
            Paint.Text(g, c, lengths.ToString("0.0"), size, true, color, box, StringAlignment.Center, "Middle", false, 0, Color.Empty, false);
        }

        /// <summary>A line down the box that fades out at both ends.</summary>
        private static void FadedLine(Graphics g, float x, RectangleF b, Color color, float width)
        {
            using (var brush = new LinearGradientBrush(new RectangleF(x - width, b.Y - 1, width * 2, b.Height + 2), color, color, 90f))
            {
                brush.InterpolationColors = new ColorBlend
                {
                    Colors = new[] { Color.FromArgb(0, color), color, color, Color.FromArgb(0, color) },
                    Positions = new[] { 0f, 0.15f, 0.85f, 1f }
                };
                using (var pen = new Pen(brush, width)) g.DrawLine(pen, x, b.Y, x, b.Bottom);
            }
        }

        /// <summary>
        /// A car seen from above, nose up, centred on the origin: a rounded body shaded across its width,
        /// tinted windows, mirrors, head and tail lights, and its number on the roof.
        /// </summary>
        internal static void DrawCar(Graphics g, WidgetContext c, float len, float wid, Color body, Color glow, float glowSize, string number, bool paceCar)
        {
            float hl = len / 2, hw = wid / 2, front = hw * 0.8f, rear = hw * 0.5f;
            using (var shape = new GraphicsPath())
            {
                shape.AddArc(-hw, -hl, front * 2, front * 2, 180, 90);
                shape.AddArc(hw - front * 2, -hl, front * 2, front * 2, 270, 90);
                shape.AddArc(hw - rear * 2, hl - rear * 2, rear * 2, rear * 2, 0, 90);
                shape.AddArc(-hw, hl - rear * 2, rear * 2, rear * 2, 90, 90);
                shape.CloseFigure();

                if (glowSize > 0 && glow.A > 0) Paint.Glow(g, shape, glow, glowSize, 0.7f);
                GraphicsState lifted = g.Save();
                g.TranslateTransform(wid * 0.06f, wid * 0.1f);
                using (var shadow = new SolidBrush(Color.FromArgb(120, 0, 0, 0))) g.FillPath(shadow, shape);
                g.Restore(lifted);

                // Mirrors, then the body over their roots.
                using (var mirror = new SolidBrush(Shade(body, -0.15f)))
                {
                    float my = -hl + len * 0.31f, mw = wid * 0.2f, mh = wid * 0.11f;
                    g.FillEllipse(mirror, -hw - mw * 0.7f, my, mw, mh);
                    g.FillEllipse(mirror, hw - mw * 0.3f, my, mw, mh);
                }
                using (var brush = new LinearGradientBrush(new RectangleF(-hw - 1, -hl, wid + 2, len), body, body, 0f))
                {
                    brush.InterpolationColors = new ColorBlend
                    {
                        Colors = new[] { Shade(body, -0.38f), Shade(body, 0.08f), Shade(body, 0.2f), Shade(body, 0.08f), Shade(body, -0.38f) },
                        Positions = new[] { 0f, 0.28f, 0.5f, 0.72f, 1f }
                    };
                    g.FillPath(brush, shape);
                }

                // Windscreen and rear window: dark glass, a little lighter at the top edge.
                Color glass = Color.FromArgb(235, 12, 18, 28);
                Window(g, -hl + len * 0.25f, -hl + len * 0.41f, hw * 0.7f, hw * 0.84f, glass);
                Window(g, -hl + len * 0.68f, -hl + len * 0.8f, hw * 0.82f, hw * 0.68f, glass);

                // Head and tail lights.
                using (var head = new SolidBrush(Color.FromArgb(235, 255, 252, 236)))
                using (var tail = new SolidBrush(Color.FromArgb(235, 255, 60, 52)))
                {
                    float lw = wid * 0.24f, lh = Math.Max(1.5f, len * 0.035f);
                    g.FillEllipse(head, -hw * 0.62f - lw / 2, -hl + len * 0.02f, lw, lh);
                    g.FillEllipse(head, hw * 0.62f - lw / 2, -hl + len * 0.02f, lw, lh);
                    g.FillEllipse(tail, -hw * 0.62f - lw / 2, hl - len * 0.02f - lh, lw, lh);
                    g.FillEllipse(tail, hw * 0.62f - lw / 2, hl - len * 0.02f - lh, lw, lh);
                }
                if (paceCar)
                    using (var bar = new SolidBrush(Color.FromArgb(240, 255, 176, 32)))
                        g.FillRectangle(bar, -hw * 0.66f, -hl + len * 0.45f, wid * 0.66f, Math.Max(2, len * 0.04f));

                float roof = -hl + len * 0.545f;
                if (!string.IsNullOrEmpty(number) && wid >= 14)
                {
                    float size = Math.Min(wid * 0.48f, len * 0.16f);
                    Paint.Text(g, c, number, size, true, Color.FromArgb(220, Shade(body, -0.78f)), new RectangleF(-hw, roof - size, wid, size * 2),
                        StringAlignment.Center, "Middle", true, 0, Color.Empty, false);
                }
                using (var pen = new Pen(Color.FromArgb(140, Shade(body, -0.6f)), Math.Max(1f, wid * 0.035f))) g.DrawPath(pen, shape);
            }
        }

        private static void Window(Graphics g, float top, float bottom, float topHalf, float bottomHalf, Color glass)
        {
            var points = new[] { new PointF(-topHalf, top), new PointF(topHalf, top), new PointF(bottomHalf, bottom), new PointF(-bottomHalf, bottom) };
            using (var path = new GraphicsPath())
            {
                path.AddPolygon(points);
                using (var brush = new LinearGradientBrush(new PointF(0, top - 1), new PointF(0, bottom + 1), Shade(glass, 0.22f), glass))
                    g.FillPath(brush, path);
            }
        }

        private static Color Shade(Color c, float amount) => amount >= 0
            ? Color.FromArgb(c.A, c.R + (int)((255 - c.R) * amount), c.G + (int)((255 - c.G) * amount), c.B + (int)((255 - c.B) * amount))
            : Color.FromArgb(c.A, (int)(c.R * (1 + amount)), (int)(c.G * (1 + amount)), (int)(c.B * (1 + amount)));

        private static Color Mix(Color a, Color b, float f) =>
            Color.FromArgb(a.A, (int)(a.R + (b.R - a.R) * f), (int)(a.G + (b.G - a.G) * f), (int)(a.B + (b.B - a.B) * f));

        // ------------------------------------------------------------------ traffic

        /// <summary>One car on the traffic strip: seconds ahead (positive) or behind along the track.</summary>
        internal struct TrafficCar
        {
            public StandingsEntry Row;
            public double Seconds;
            public bool FasterClass;
        }

        /// <summary>
        /// The cars within <paramref name="window"/> seconds either way by track position. Seconds are distance
        /// at your usual lap pace. A class is faster when its best lap beats your class's by more than 1.5%.
        /// </summary>
        internal static List<TrafficCar> Traffic(TelemetrySnapshot t, double window)
        {
            var cars = new List<TrafficCar>();
            float[] dist = t.CarLapDist ?? new float[0];
            StandingsEntry[] rows = t.Standings ?? new StandingsEntry[0];
            if (t.PlayerCarIdx < 0 || t.PlayerCarIdx >= dist.Length || dist[t.PlayerCarIdx] < 0) return cars;
            double lap = t.ReferenceLapSeconds;
            if (lap <= 0) lap = t.TrackLengthMiles > 0 && t.SpeedMph > 20 ? t.TrackLengthMiles / t.SpeedMph * 3600 : 90;
            var pace = rows.Where(r => r.BestLap > 0).GroupBy(r => r.ClassName ?? "").ToDictionary(x => x.Key, x => x.Min(r => r.BestLap));
            StandingsEntry me = rows.FirstOrDefault(r => r.IsPlayer);
            double mine = me != null && pace.TryGetValue(me.ClassName ?? "", out double p) ? p : 0;
            double here = dist[t.PlayerCarIdx];
            foreach (StandingsEntry row in rows)
            {
                if (row.IsPlayer || row.CarIdx < 0 || row.CarIdx >= dist.Length || dist[row.CarIdx] < 0) continue;
                double pct = dist[row.CarIdx] - here;
                if (pct >= 0.5) pct -= 1;
                if (pct < -0.5) pct += 1;
                double seconds = pct * lap;
                if (Math.Abs(seconds) > window) continue;
                bool faster = mine > 0 && me != null && row.ClassName != me.ClassName && pace.TryGetValue(row.ClassName ?? "", out double theirs) && theirs < mine * 0.985;
                cars.Add(new TrafficCar { Row = row, Seconds = seconds, FasterClass = faster });
            }
            return cars;
        }

        /// <summary>Seconds to the nearest car of a faster class behind you (within ten seconds, not in the pits); -1 when there is none.</summary>
        internal static double FasterClassBehind(TelemetrySnapshot t, out string className)
        {
            className = "";
            double best = -1;
            foreach (TrafficCar car in Traffic(t, 10))
                if (car.FasterClass && car.Seconds < 0 && !car.Row.InPits && (best < 0 || -car.Seconds < best))
                {
                    best = -car.Seconds;
                    className = car.Row.ClassName ?? "";
                }
            return best;
        }

        public static void TrafficStrip(WidgetContext c)
        {
            Graphics g = c.G;
            RectangleF b = c.B;
            TelemetrySnapshot t = c.T;
            if (b.Width < 40 || b.Height < 12) return;
            double window = Math.Max(2, c.Num("seconds"));
            float mid = b.Y + b.Height / 2, cx = b.X + b.Width / 2;
            float capH = Math.Max(10, b.Height * 0.62f), capW = capH * 1.9f;
            float span = b.Width / 2 - capW / 2 - 2;
            float X(double seconds) => cx + (float)(seconds / window * span);

            // The strip, with a tick every two seconds.
            Color track = c.Color("trackColor");
            float th = Math.Max(3, b.Height * 0.12f);
            using (GraphicsPath strip = Paint.Rounded(new RectangleF(b.X, mid - th / 2, b.Width, th), th / 2))
            using (var brush = new SolidBrush(track)) g.FillPath(brush, strip);
            using (var tick = new Pen(Color.FromArgb(Math.Min(255, track.A + 60), Shade(track, 0.5f)), 1))
                for (int s = 2; s <= window; s += 2)
                    foreach (int side in new[] { -1, 1 })
                    {
                        float x = X(s * side);
                        g.DrawLine(tick, x, mid - th * 1.6f, x, mid + th * 1.6f);
                    }

            List<TrafficCar> cars = t.Connected ? Traffic(t, window) : new List<TrafficCar>();
            bool classColors = c.Bool("classColors"), numbers = c.Bool("numbers"), warn = c.Bool("warn");
            Color others = c.Color("carColor"), pits = c.Color("pitColor"), danger = c.Color("warnColor");
            // A faster class closing from behind: the strip glows from behind up to you.
            TrafficCar[] closing = cars.Where(x => warn && x.FasterClass && x.Seconds < 0 && !x.Row.InPits).ToArray();
            if (closing.Length > 0)
            {
                float from = X(-window);
                var glow = new RectangleF(from, mid - th * 1.5f, cx - from, th * 3);
                using (var brush = new LinearGradientBrush(new RectangleF(glow.X - 1, glow.Y, glow.Width + 2, glow.Height), Color.FromArgb(0, danger), Color.FromArgb(150, danger), 0f))
                using (GraphicsPath path = Paint.Rounded(glow, th * 1.5f))
                    g.FillPath(brush, path);
            }
            // Furthest first, so the cars nearest you sit on top.
            foreach (TrafficCar car in cars.OrderByDescending(x => Math.Abs(x.Seconds)))
            {
                Color fill = car.Row.InPits ? pits : classColors ? car.Row.ClassColor : others;
                var box = new RectangleF(X(car.Seconds) - capW / 2, mid - capH / 2, capW, capH);
                using (GraphicsPath cap = Paint.Rounded(box, capH / 2))
                {
                    if (warn && car.FasterClass && car.Seconds < 0) Paint.Glow(g, cap, danger, capH * 0.5f);
                    using (var brush = new SolidBrush(fill)) g.FillPath(brush, cap);
                    using (var pen = new Pen(Color.FromArgb(200, c.Theme("background")), 1.5f)) g.DrawPath(pen, cap);
                }
                if (numbers)
                    Paint.Text(g, c, car.Row.CarNumber ?? "", capH * 0.62f, true, Ink(fill), box, StringAlignment.Center, "Middle", true, 0, Color.Empty, false);
            }
            // You, in the middle.
            Color own = c.Color("playerColor");
            var you = new RectangleF(cx - capW / 2, mid - capH / 2, capW, capH);
            using (GraphicsPath cap = Paint.Rounded(you, capH / 2))
            {
                Paint.Glow(g, cap, own, capH * 0.4f);
                using (var brush = new SolidBrush(own)) g.FillPath(brush, cap);
            }
            Paint.Text(g, c, "YOU", capH * 0.5f, true, Ink(own), you, StringAlignment.Center, "Middle", true, 0, Color.Empty, false, 1);
        }

        /// <summary>Dark text on a light fill, light on a dark one.</summary>
        private static Color Ink(Color fill) => fill.GetBrightness() > 0.55f || (fill.R * 0.3 + fill.G * 0.59 + fill.B * 0.11) > 150 ? Color.FromArgb(10, 12, 16) : Color.White;
    }
}
