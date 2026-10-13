using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;

namespace AlmostFastOverlays
{
    /// <summary>
    /// The Radar and Traffic widgets.
    /// - Radar: your car on a five-lane road, seen from a camera above and a little behind (tilted 70° by
    ///   default; 90° looks straight down). The road narrows toward the top, range rings lie on it a car length
    ///   apart, and the cars around you sit where they are as solid little cars, with their numbers on the
    ///   roof. Sensor waves run ahead and behind up to the next car in line, turning amber, then red, as it
    ///   gets close.
    /// - Traffic: the cars within a few seconds either way laid along a line, in class colours.
    /// </summary>
    internal static class RadarPainters
    {
        /// <summary>Car size and lane spacing on the radar, in metres.</summary>
        private const float CarLong = 4.9f, CarWide = 1.95f, LaneWidth = 2.7f;

        /// <summary>
        /// A camera above and behind your car looking down the road. Points on the road are x across (right
        /// positive), y ahead and h up, in metres from your car; it maps them onto the widget's box.
        /// </summary>
        private sealed class Camera
        {
            /// <summary>How far the camera sits from your car, in units of the range shown ahead: smaller is stronger perspective.</summary>
            private const double Depth = 1.6;
            public readonly float Cx, Cy;
            public readonly double F, D, Sin, Cos;
            /// <summary>Metres of road visible ahead of and behind your car.</summary>
            public readonly double Ahead, Behind;

            public Camera(RectangleF b, double range, double tilt, double place)
            {
                double phi = Math.Max(30, Math.Min(90, tilt)) * Math.PI / 180;
                Sin = Math.Sin(phi);
                Cos = Math.Abs(Math.Cos(phi)) < 1e-9 ? 0 : Math.Cos(phi);
                Cx = b.X + b.Width / 2;
                Cy = b.Y + (float)(b.Height * place);
                double reach = range * RadarTracker.CarLength;
                D = reach * Depth;
                // As large as fits: the range ahead up to the top edge, and two lanes either side across.
                double tall = (Cy - b.Y) * (D + reach * Cos) / (reach * Sin);
                double wide = (b.Width / 2 - 2) * D / (2 * LaneWidth + CarWide * 0.6);
                F = Math.Min(tall, wide);
                Ahead = Ground(Cy - b.Y);
                Behind = -Ground(Cy - b.Bottom);
            }

            /// <summary>The distance along the road seen at a screen height above (or below) your car.</summary>
            private double Ground(double pixelsUp)
            {
                double t = pixelsUp / F, den = Sin - t * Cos;
                return den <= 1e-6 ? 1000 : t * D / den;
            }

            private double Z(double y, double h) => Math.Max(0.5, y * Cos + D - h * Sin);

            public PointF At(double x, double y, double h = 0)
            {
                double z = Z(y, h);
                return new PointF((float)(Cx + F * x / z), (float)(Cy - F * (y * Sin + h * Cos) / z));
            }

            /// <summary>Pixels per metre across the road at a distance and height.</summary>
            public float Scale(double y, double h = 0) => (float)(F / Z(y, h));

            /// <summary>
            /// The flat drawing at (x, y, h) seen through the camera, for shapes drawn in metres with the nose up:
            /// exact at their middle, which is plenty for something the size of a car.
            /// </summary>
            public Matrix Local(double x, double y, double h)
            {
                double z = Z(y, h);
                PointF p = At(x, y, h);
                return new Matrix((float)(F / z), 0, (float)(F * x * Cos / (z * z)), (float)(F * (D * Sin - h) / (z * z)), p.X, p.Y);
            }

            public PointF[] Polygon(IEnumerable<PointF> ground) => ground.Select(q => At(q.X, q.Y)).ToArray();
        }

        public static void Radar(WidgetContext c)
        {
            Graphics g = c.G;
            RectangleF b = c.B;
            TelemetrySnapshot t = c.T;
            if (b.Width < 24 || b.Height < 24) return;
            const double L = RadarTracker.CarLength;
            double range = Math.Max(1, Math.Min(16, c.Num("range")));
            var cam = new Camera(b, range, c.Num("tilt"), 0.55);
            // Everything fades out toward the top and bottom edges.
            float Fade(float y)
            {
                float d = (y - b.Y) / b.Height;
                return d < 0.12f ? Math.Max(0, d / 0.12f) : d > 0.9f ? Math.Max(0, (1 - d) / 0.1f) : 1;
            }
            double far = cam.Ahead + L, near = -(cam.Behind + L);

            GraphicsState state = g.Save();
            g.SetClip(b, CombineMode.Intersect);

            Color road = c.Color("roadColor");
            if (road.A > 0)
            {
                float edge = LaneWidth * 2.5f;
                PointF[] area = cam.Polygon(new[] { new PointF(-edge, (float)far), new PointF(edge, (float)far), new PointF(edge, (float)near), new PointF(-edge, (float)near) });
                using (var brush = new LinearGradientBrush(new RectangleF(b.X, b.Y - 1, b.Width, b.Height + 2), road, road, 90f))
                {
                    brush.InterpolationColors = new ColorBlend
                    {
                        Colors = new[] { Color.FromArgb(0, road), road, road, Color.FromArgb(0, road) },
                        Positions = new[] { 0f, 0.2f, 0.88f, 1f }
                    };
                    g.FillPolygon(brush, area);
                }
            }

            if (c.Bool("rings"))
            {
                Color ring = c.Color("ringColor");
                for (int n = 1; n <= Math.Ceiling(range); n++)
                {
                    double r = n * L;
                    // Only whole rings: one cut off by the box's sides would look broken.
                    if (cam.F * r / cam.D > b.Width / 2 - 2) break;
                    var points = new PointF[72];
                    for (int i = 0; i < points.Length; i++)
                    {
                        double a = i * 2 * Math.PI / points.Length;
                        points[i] = cam.At(r * Math.Cos(a), r * Math.Sin(a));
                    }
                    int alpha = (int)(ring.A * (1 - 0.5 * (n - 1) / Math.Max(1, range)));
                    using (var pen = new Pen(Color.FromArgb(Math.Max(0, alpha), ring), Math.Max(1f, cam.Scale(0) * 0.06f)))
                        g.DrawPolygon(pen, points);
                }
            }

            if (c.Bool("lanes"))
            {
                Color lines = c.Color("laneColor");
                // Lane dashes slide past as you drive (by your distance round the lap).
                const double Dash = 3.0, Period = 7.5;
                double travelled = c.Bool("moving") && t.TrackLengthMiles > 0 ? t.LapFraction * t.TrackLengthMiles * 1609.344 : 0;
                double phase = travelled % Period;
                foreach (float k in new[] { -1.5f, -0.5f, 0.5f, 1.5f })
                    for (double m = near - Period - phase; m < far + Period; m += Period)
                    {
                        PointF a = cam.At(k * LaneWidth, m), z = cam.At(k * LaneWidth, m + Dash);
                        float f = Fade((a.Y + z.Y) / 2);
                        if (f <= 0) continue;
                        using (var pen = new Pen(Color.FromArgb((int)(lines.A * f), lines), Math.Max(1.2f, cam.Scale(m + Dash / 2) * 0.13f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                            g.DrawLine(pen, a, z);
                    }
                foreach (float k in new[] { -2.5f, 2.5f })
                    for (double m = near; m < far; m += 2)
                    {
                        PointF a = cam.At(k * LaneWidth, m), z = cam.At(k * LaneWidth, m + 2.05);
                        float f = Fade((a.Y + z.Y) / 2);
                        if (f <= 0) continue;
                        using (var pen = new Pen(Color.FromArgb((int)(lines.A * f), lines), Math.Max(1.5f, cam.Scale(m + 1) * 0.16f)))
                            g.DrawLine(pen, a, z);
                    }
            }

            Color clear = c.Color("clearColor"), close = c.Color("closeColor"), getting = c.Color("nearColor"), beside = c.Color("alongsideColor");
            double nearAt = c.Num("nearAt"), closeAt = c.Num("closeAt");
            RadarCar[] cars = t.Connected ? t.Radar ?? new RadarCar[0] : new RadarCar[0];

            // A wash of colour on the road where a car is alongside.
            foreach (RadarCar car in cars.Where(x => x.Alongside))
            {
                float x = car.Lane * LaneWidth, y = (float)car.Meters;
                PointF[] wash = cam.Polygon(new[] { new PointF(x - LaneWidth / 2, y + CarLong * 0.8f), new PointF(x + LaneWidth / 2, y + CarLong * 0.8f),
                    new PointF(x + LaneWidth / 2, y - CarLong * 0.8f), new PointF(x - LaneWidth / 2, y - CarLong * 0.8f) });
                using (var path = new GraphicsPath())
                {
                    path.AddPolygon(wash);
                    using (var brush = new PathGradientBrush(path) { CenterColor = Color.FromArgb(80, beside), SurroundColors = new[] { Color.FromArgb(0, beside) } })
                        g.FillPath(brush, path);
                }
            }

            if (c.Bool("sensors") && t.Connected)
            {
                Sensor(g, cam, 1, t.AheadMeters, clear, getting, close, nearAt, closeAt);
                Sensor(g, cam, -1, t.BehindMeters, clear, getting, close, nearAt, closeAt);
            }

            // Cars, furthest from the camera first so nearer ones sit in front; yours among them.
            Color others = c.Color("otherColor"), pace = c.Color("paceColor"), own = c.Color("carColor");
            bool classColors = c.Bool("classColors"), numbers = c.Bool("numbers");
            float glow = (float)c.Num("glow");
            var drawn = cars.Where(x => x.Meters < far && x.Meters > near).Select(x => Tuple.Create(x, x.Meters)).ToList();
            drawn.Add(Tuple.Create((RadarCar)null, 0.0));
            foreach (Tuple<RadarCar, double> item in drawn.OrderByDescending(x => x.Item2))
            {
                RadarCar car = item.Item1;
                if (car == null)
                {
                    DrawCar(g, c, cam, 0, 0, own, own, glow > 0 ? 0.55f : 0, "", false);
                    continue;
                }
                Color body = car.PaceCar ? pace : classColors && car.ClassColor.A > 0 ? car.ClassColor : others;
                if (car.Alongside) body = Mix(body, beside, 0.6f);
                DrawCar(g, c, cam, car.Lane * LaneWidth, car.Meters, body, car.Alongside ? beside : Color.Empty, car.Alongside ? 0.75f : 0, numbers ? car.Number : "", car.PaceCar);
            }

            // The next car in line beyond the edge: how many car lengths away.
            if (c.Bool("beyond") && t.Connected)
            {
                Color muted = c.Theme("muted");
                float size = Math.Max(14, Math.Min(20, b.Width / 18));
                if (t.AheadMeters > cam.Ahead - CarLong * 0.3) Beyond(g, c, cam.Cx, b.Y + size * 0.45f, -1, RadarTracker.Lengths(t.AheadMeters), size, muted);
                if (t.BehindMeters > cam.Behind - CarLong * 0.3) Beyond(g, c, cam.Cx, b.Bottom - size * 0.45f, 1, RadarTracker.Lengths(t.BehindMeters), size, muted);
            }
            g.Restore(state);
        }

        /// <summary>
        /// A sensor cone on the road from your car's nose (dir 1) or tail (dir -1), with waves running up to the
        /// next car in line. It is the clear colour with room, then the near colour, then the close colour.
        /// </summary>
        private static void Sensor(Graphics g, Camera cam, int dir, double meters, Color clear, Color near, Color close, double nearAt, double closeAt)
        {
            double gap = RadarTracker.Lengths(meters);
            Color color = gap < 0 ? clear : gap < closeAt ? close : gap < nearAt ? near : clear;
            double edge = dir > 0 ? cam.Ahead : cam.Behind;
            double reach = meters < 0 ? edge : Math.Min(edge, meters - RadarTracker.CarLength);
            reach = Math.Max(reach, 0.4);
            const double Spread = 24 * Math.PI / 180, Inset = 0.9;
            double apex = dir * (CarLong / 2 - Inset), radius = reach + Inset;
            PointF Ray(double r, double a) => cam.At(r * Math.Sin(a), apex + dir * r * Math.Cos(a));
            var cone = new List<PointF> { cam.At(0, apex) };
            for (int i = 0; i <= 16; i++) cone.Add(Ray(radius, -Spread + i * Spread * 2 / 16));
            PointF from = cam.At(0, apex), to = cam.At(0, apex + dir * radius);
            if (Math.Abs(to.Y - from.Y) < 1) to.Y = from.Y - dir;
            using (var brush = new LinearGradientBrush(from, to, Color.FromArgb(115, color), Color.FromArgb(0, color)))
                g.FillPolygon(brush, cone.ToArray());
            double spacing = RadarTracker.CarLength * 0.4;
            for (double r = Inset + spacing * 0.75; r < radius - 0.3; r += spacing)
            {
                var arc = new PointF[13];
                for (int i = 0; i < arc.Length; i++) arc[i] = Ray(r, -Spread * 0.82 + i * Spread * 1.64 / (arc.Length - 1));
                float f = (float)(1 - 0.6 * r / radius);
                using (var pen = new Pen(Color.FromArgb((int)(235 * f), color), Math.Max(2f, cam.Scale(apex + dir * r) * 0.28f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    g.DrawLines(pen, arc);
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

        /// <summary>
        /// A car on the road at (x, y), seen through the camera: a soft shadow (or a coloured glow when it's
        /// alongside, or it's yours), solid sides built up in layers so it stands off the road, and a top with
        /// tinted windows, mirrors, head and tail lights and its number on the roof.
        /// </summary>
        private static void DrawCar(Graphics g, WidgetContext c, Camera cam, double x, double y, Color body, Color glow, float glowSize, string number, bool paceCar)
        {
            float hl = CarLong / 2, hw = CarWide / 2;
            using (GraphicsPath shape = CarShape(hl, hw))
            {
                void Layer(double h, Action draw)
                {
                    GraphicsState placed = g.Save();
                    using (Matrix m = cam.Local(x, y, h)) g.MultiplyTransform(m);
                    draw();
                    g.Restore(placed);
                }

                // Glow or shadow on the road beneath it.
                Layer(0, () =>
                {
                    var under = new RectangleF(-hw - glowSize, -hl - glowSize, CarWide + glowSize * 2, CarLong + glowSize * 2);
                    using (GraphicsPath pool = DashKit.Rounded(under, hw + glowSize))
                    using (var brush = new PathGradientBrush(pool)
                    {
                        CenterColor = glowSize > 0 && glow.A > 0 ? Color.FromArgb(150, glow) : Color.FromArgb(130, 0, 0, 0),
                        SurroundColors = new[] { Color.FromArgb(0, glowSize > 0 && glow.A > 0 ? glow : Color.Black) },
                        FocusScales = new PointF(0.55f, 0.75f)
                    })
                        g.FillPath(brush, pool);
                });

                // The sides, built up from the road: darker low down.
                float[] heights = { 0.16f, 0.32f, 0.48f, 0.64f };
                for (int i = 0; i < heights.Length; i++)
                {
                    Color side = Shade(body, -0.62f + i * 0.09f);
                    Layer(heights[i], () => { using (var brush = new SolidBrush(side)) g.FillPath(brush, shape); });
                }

                // The top, with its details.
                Layer(0.8, () =>
                {
                    using (var mirror = new SolidBrush(Shade(body, -0.2f)))
                    {
                        float my = -hl + CarLong * 0.31f, mw = CarWide * 0.2f, mh = CarWide * 0.11f;
                        g.FillEllipse(mirror, -hw - mw * 0.7f, my, mw, mh);
                        g.FillEllipse(mirror, hw - mw * 0.3f, my, mw, mh);
                    }
                    using (var brush = new LinearGradientBrush(new RectangleF(-hw - 0.05f, -hl, CarWide + 0.1f, CarLong), body, body, 0f))
                    {
                        brush.InterpolationColors = new ColorBlend
                        {
                            Colors = new[] { Shade(body, -0.32f), Shade(body, 0.08f), Shade(body, 0.22f), Shade(body, 0.08f), Shade(body, -0.32f) },
                            Positions = new[] { 0f, 0.28f, 0.5f, 0.72f, 1f }
                        };
                        g.FillPath(brush, shape);
                    }
                    Color glass = Color.FromArgb(235, 12, 18, 28);
                    Window(g, -hl + CarLong * 0.25f, -hl + CarLong * 0.41f, hw * 0.7f, hw * 0.84f, glass);
                    Window(g, -hl + CarLong * 0.68f, -hl + CarLong * 0.8f, hw * 0.82f, hw * 0.68f, glass);
                    using (var head = new SolidBrush(Color.FromArgb(235, 255, 252, 236)))
                    using (var tail = new SolidBrush(Color.FromArgb(240, 255, 60, 52)))
                    {
                        float lw = CarWide * 0.24f, lh = CarLong * 0.04f;
                        g.FillEllipse(head, -hw * 0.62f - lw / 2, -hl + CarLong * 0.02f, lw, lh);
                        g.FillEllipse(head, hw * 0.62f - lw / 2, -hl + CarLong * 0.02f, lw, lh);
                        g.FillEllipse(tail, -hw * 0.62f - lw / 2, hl - CarLong * 0.02f - lh, lw, lh);
                        g.FillEllipse(tail, hw * 0.62f - lw / 2, hl - CarLong * 0.02f - lh, lw, lh);
                    }
                    if (paceCar)
                        using (var bar = new SolidBrush(Color.FromArgb(240, 255, 176, 32)))
                            g.FillRectangle(bar, -hw * 0.66f, -hl + CarLong * 0.45f, CarWide * 0.66f, CarLong * 0.045f);
                    using (var pen = new Pen(Color.FromArgb(150, Shade(body, -0.6f)), 0.06f)) g.DrawPath(pen, shape);
                });

                // The number on the roof, drawn upright on the screen at a size you can read.
                if (!string.IsNullOrEmpty(number))
                {
                    PointF[] roof = { new PointF(0, -hl + CarLong * 0.55f) };
                    using (Matrix m = cam.Local(x, y, 0.8)) m.TransformPoints(roof);
                    float across = CarWide * cam.Scale(y, 0.8), size = across * 0.5f;
                    if (size >= 7)
                        Paint.Text(g, c, number, size, true, Color.FromArgb(230, Shade(body, -0.8f)), new RectangleF(roof[0].X - across * 0.48f, roof[0].Y - size, across * 0.96f, size * 2),
                            StringAlignment.Center, "Middle", true, 0, Color.Empty, false);
                }
            }
        }

        /// <summary>A car's outline from above in metres, nose up: rounder at the front than the back.</summary>
        private static GraphicsPath CarShape(float hl, float hw)
        {
            float front = hw * 0.8f, rear = hw * 0.5f;
            var shape = new GraphicsPath();
            shape.AddArc(-hw, -hl, front * 2, front * 2, 180, 90);
            shape.AddArc(hw - front * 2, -hl, front * 2, front * 2, 270, 90);
            shape.AddArc(hw - rear * 2, hl - rear * 2, rear * 2, rear * 2, 0, 90);
            shape.AddArc(-hw, hl - rear * 2, rear * 2, rear * 2, 90, 90);
            shape.CloseFigure();
            return shape;
        }

        private static void Window(Graphics g, float top, float bottom, float topHalf, float bottomHalf, Color glass)
        {
            var points = new[] { new PointF(-topHalf, top), new PointF(topHalf, top), new PointF(bottomHalf, bottom), new PointF(-bottomHalf, bottom) };
            using (var path = new GraphicsPath())
            {
                path.AddPolygon(points);
                using (var brush = new LinearGradientBrush(new PointF(0, top - 0.05f), new PointF(0, bottom + 0.05f), Shade(glass, 0.22f), glass))
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
