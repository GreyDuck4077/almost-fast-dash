using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;

namespace AlmostFastDash.Rendering
{
    internal static partial class WidgetPainters
    {
        public static void Arc(WidgetContext c)
        {
            Graphics g = c.G;
            RectangleF b = c.B;
            float thickness = (float)c.Num("thickness"), glow = (float)c.Num("glow");
            float start = (float)c.Num("start"), sweep = (float)c.Num("sweep");
            var center = new PointF(b.X + b.Width / 2, b.Y + b.Height / 2);
            float margin = thickness / 2 + glow / 2 + 1;
            float radius = Math.Max(4, Math.Min(b.Width, b.Height) / 2 - margin);
            bool oval = c.Bool("oval");
            var ring = new ArcRing
            {
                Center = center, Thickness = thickness, Start = start, Sweep = sweep,
                RadiusX = oval ? Math.Max(4, b.Width / 2 - margin) : radius, RadiusY = oval ? Math.Max(4, b.Height / 2 - margin) : radius,
                Taper = (float)Math.Max(-1, Math.Min(1, c.Num("taper") / 100))
            };
            double f = Fraction(c, c.Num("value"));
            bool flash = c.Bool("flash");
            Color flashColor = c.Color("flashColor"), track = c.Color("track");
            string colorMode = c.Choice("colorMode"), style = c.Choice("style");
            Color baseColor = c.Color("color"), warn = c.Color("warnColor"), danger = c.Color("dangerColor");
            double warnAt = c.Num("warnAt"), dangerAt = c.Num("dangerAt");
            bool zones = c.Bool("zones");
            Color ColorAt(double at) => Tone(colorMode, at, baseColor, warn, danger, warnAt, dangerAt);
            Color TrackAt(double at) => !zones ? track : at >= dangerAt ? Color.FromArgb(120, danger) : at >= warnAt ? Color.FromArgb(95, warn) : track;

            // One stretch of the ring: pens for a plain circle (as it has always been drawn), outlines for ovals and tapers.
            void Band(float from, float span, Color color, bool round, bool lit, bool softGlow)
            {
                if (Math.Abs(span) < 0.01f) return;
                if (ring.Classic)
                {
                    LineCap cap = round ? LineCap.Round : LineCap.Flat;
                    if (lit && glow > 0)
                    {
                        if (softGlow)
                            for (int k = 3; k >= 1; k--)
                                using (var pen = new Pen(Color.FromArgb(28, color), thickness + glow * k / 3f) { StartCap = cap, EndCap = cap }) g.DrawArc(pen, ring.Rect, from, span);
                        else using (var pen = new Pen(Color.FromArgb(55, color), thickness + glow) { StartCap = cap, EndCap = cap }) g.DrawArc(pen, ring.Rect, from, span);
                    }
                    using (var pen = new Pen(color, thickness) { StartCap = cap, EndCap = cap }) g.DrawArc(pen, ring.Rect, from, span);
                    return;
                }
                using (GraphicsPath path = ring.Band(from, span, round))
                {
                    if (lit && glow > 0) Paint.Glow(g, path, color, glow);
                    using (var brush = new SolidBrush(color)) g.FillPath(brush, path);
                }
            }

            if (style == "Segmented")
            {
                int n = Math.Max(2, (int)c.Num("segments"));
                int lit = (int)Math.Ceiling(f * n - 1e-9);
                float step = sweep / n;
                for (int i = 0; i < n; i++)
                {
                    bool on = flash || i < lit;
                    double at = (i + 0.5) / n;
                    Band(start + i * step + step * 0.12f, step * 0.76f, on ? (flash ? flashColor : ColorAt(at)) : TrackAt(at), false, on, false);
                }
            }
            else
            {
                Band(start, sweep, track, true, false, true);
                // The zones as a fine stripe along the outer rim of the track, as a printed redline is.
                void Rim(double from, double to, Color color)
                {
                    if (to - from < 1e-4) return;
                    float t = Math.Max(2, thickness * 0.3f);
                    var rim = new ArcRing
                    {
                        Center = ring.Center, Thickness = t, Start = start + (float)(from * sweep), Sweep = (float)((to - from) * sweep),
                        RadiusX = ring.RadiusX + (thickness - t) / 2, RadiusY = ring.RadiusY + (thickness - t) / 2
                    };
                    if (rim.Classic)
                        using (var pen = new Pen(color, t)) g.DrawArc(pen, rim.Rect, rim.Start, rim.Sweep);
                    else
                        using (GraphicsPath path = rim.Band(rim.Start, rim.Sweep, false))
                        using (var brush = new SolidBrush(color)) g.FillPath(brush, path);
                }
                if (zones)
                {
                    double warnFrom = Math.Max(0, Math.Min(1, warnAt)), dangerFrom = Math.Max(0, Math.Min(1, dangerAt));
                    Rim(warnFrom, Math.Max(warnFrom, dangerFrom), Color.FromArgb(150, warn));
                    Rim(dangerFrom, 1, Color.FromArgb(230, danger));
                }
                if (style == "Smooth")
                {
                    float s = flash ? sweep : (float)(f * sweep);
                    if (Math.Abs(s) >= 0.5f) Band(start, s, flash ? flashColor : ColorAt(f), true, true, true);
                }
            }
            ArcBezel(c, ring);

            ArcScale(c, ring, colorMode, danger, dangerAt, c.Bool("litScale") ? f : -1, ColorAt);
            ArcNeedle(c, ring, start + (float)(f * sweep), glow);
        }

        /// <summary>The gap between the band and its bezel lines, in pixels.</summary>
        internal const float BezelGap = 2.5f;

        /// <summary>Hairline bezels along the outer and inner edges of the whole scale, as on a printed instrument.</summary>
        private static void ArcBezel(WidgetContext c, ArcRing ring)
        {
            Color? rim = c.OptionalColor("rim");
            if (rim == null || rim.Value.A == 0) return;
            float width = (float)Math.Max(0.5, Math.Min(4, c.Num("rimWidth")));
            int steps = Math.Max(8, (int)Math.Ceiling(Math.Abs(ring.Sweep) / 1.5f));
            Pen Stroke() => new Pen(rim.Value, width) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
            foreach (float side in new[] { 1f, -1f })
            {
                PointF[] Line()
                {
                    var points = new PointF[steps + 1];
                    for (int i = 0; i <= steps; i++)
                    {
                        float a = ring.Start + ring.Sweep * i / steps;
                        points[i] = ring.At(a, side * (ring.ThicknessAt(a) / 2 + BezelGap + width / 2));
                    }
                    return points;
                }
                if (c.Renderer == null)
                {
                    using (Pen pen = Stroke()) c.G.DrawLines(pen, Line());
                    continue;
                }
                // The line's outline is worked out once (its shape only changes with the ring's) and filled each frame.
                string key = string.Join("|", "bezel", ring.Center, ring.RadiusX, ring.RadiusY, ring.Thickness, ring.Taper, ring.Start, ring.Sweep, width, side);
                GraphicsPath outline = c.Renderer.Shape(key, () =>
                {
                    var path = new GraphicsPath();
                    path.AddLines(Line());
                    using (Pen pen = Stroke()) path.Widen(pen);
                    path.FillMode = FillMode.Winding;
                    return path;
                });
                using (var brush = new SolidBrush(rim.Value)) c.G.FillPath(brush, outline);
            }
        }

        /// <summary>
        /// Tick marks and numbers just inside the ring. With <paramref name="litTo"/> at 0 or more, the scale
        /// lights up as the value passes it: marks already reached take the color the gauge has there and
        /// the numbers show full; those still ahead stay dim.
        /// </summary>
        private static void ArcScale(WidgetContext c, ArcRing ring, string colorMode, Color danger, double dangerAt, double litTo, Func<double, Color> colorAt)
        {
            int major = (int)Math.Round(c.Num("ticks"));
            if (major <= 0) return;
            major = Math.Min(major, 60);
            Graphics g = c.G;
            int minor = Math.Max(0, Math.Min(9, (int)Math.Round(c.Num("minorTicks"))));
            int total = major * (minor + 1);
            float tickLength = (float)c.Num("tickLength");
            Color tickColor = c.Color("tickColor");
            // With a bezel, the marks start inside its inner line.
            float bezel = c.OptionalColor("rim")?.A > 0 ? BezelGap + (float)Math.Max(0.5, c.Num("rimWidth")) + 1.5f : 0;
            float Inside(float angle) => -ring.ThicknessAt(angle) / 2 - 4 - bezel;
            bool lighting = litTo >= 0;
            Color Dim(Color color, int share) => Color.FromArgb(color.A * share / 100, color);
            // Marks of the same color and weight go in one shape and are drawn together, not one at a time.
            var marks = new Dictionary<long, GraphicsPath>();
            for (int i = 0; i <= total; i++)
            {
                float a = ring.Start + ring.Sweep * i / total;
                double at = (double)i / total;
                bool isMajor = i % (minor + 1) == 0;
                Color color = !lighting ? tickColor : at <= litTo + 1e-6 ? colorAt(at) : Dim(tickColor, 55);
                if (!isMajor) color = Dim(color, 70);
                float edge = Inside(a), len = isMajor ? tickLength : tickLength * 0.55f;
                long kind = (long)(uint)color.ToArgb() << 1 | (isMajor ? 1L : 0L);
                if (!marks.TryGetValue(kind, out GraphicsPath path)) marks[kind] = path = new GraphicsPath();
                path.StartFigure();
                path.AddLine(ring.At(a, edge), ring.At(a, edge - len));
            }
            foreach (KeyValuePair<long, GraphicsPath> kind in marks)
                using (GraphicsPath path = kind.Value)
                using (var pen = new Pen(Color.FromArgb((int)(kind.Key >> 1)), (kind.Key & 1) == 1 ? 2 : 1))
                    g.DrawPath(pen, path);
            if (!c.Bool("numbers")) return;
            double from = c.Num("numbersFrom"), to = c.Num("numbersTo");
            float size = (float)c.Num("numberSize");
            Color numberColor = c.Color("numberColor");
            for (int k = 0; k <= major; k++)
            {
                float a = ring.Start + ring.Sweep * k / major;
                PointF p = ring.At(a, Inside(a) - tickLength - size * 0.85f);
                double number = from + (to - from) * k / major, at = (double)k / major;
                bool red = colorMode != "Solid" && at >= dangerAt;
                // Numbers stay readable on the face: the red zone in a lighter red, numbers not yet reached dimmed only so far.
                Color face = c.Theme("background");
                Color color = DashboardAudit.Readable(red ? danger : numberColor, face, 6);
                if (lighting && at > litTo + 1e-6) color = DashboardAudit.Dimmed(color, face, 0.55f, 6);
                Paint.Text(g, c, number.ToString("0.##"), size, true, color, new RectangleF(p.X - size * 1.6f, p.Y - size * 0.7f, size * 3.2f, size * 1.4f),
                    StringAlignment.Center, "Middle", false, 0, Color.Empty, false);
            }
        }

        /// <summary>A needle from the centre to the value, with an optional cap over its pivot.</summary>
        private static void ArcNeedle(WidgetContext c, ArcRing ring, float angle, float glow)
        {
            string needle = c.Choice("needle");
            if (needle == "Marker")
            {
                // A bright cursor across the ring at the value, as on a digital cluster's sweep.
                float half = ring.ThicknessAt(angle) * (float)Math.Max(0.2, c.Num("needleLength") / 100) / 2;
                float w = (float)Math.Max(1, c.Num("needleWidth"));
                Color mc = c.Color("needleColor");
                using (var mark = new GraphicsPath())
                {
                    mark.AddLine(ring.At(angle, -half), ring.At(angle, half));
                    using (var pen = new Pen(mc, w) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    {
                        if (glow > 0)
                            using (GraphicsPath wide = (GraphicsPath)mark.Clone())
                            {
                                wide.Widen(pen);
                                Paint.Glow(c.G, wide, mc, glow * 0.8f);
                            }
                        c.G.DrawPath(pen, mark);
                    }
                }
                return;
            }
            if (needle != "Needle" && needle != "Line") return;
            Graphics g = c.G;
            float share = (float)Math.Max(0.2, c.Num("needleLength") / 100), width = (float)Math.Max(1, c.Num("needleWidth"));
            Color color = c.Color("needleColor");
            double a = angle * Math.PI / 180;
            PointF o = ring.Center;
            var tip = new PointF(o.X + ring.RadiusX * share * (float)Math.Cos(a), o.Y + ring.RadiusY * share * (float)Math.Sin(a));
            float dx = tip.X - o.X, dy = tip.Y - o.Y, len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 1) return;
            float ux = dx / len, uy = dy / len, px = -uy, py = ux;
            using (var path = new GraphicsPath())
            {
                if (needle == "Line")
                {
                    path.AddLine(o, tip);
                    using (var outline = (GraphicsPath)path.Clone())
                    {
                        if (glow > 0) using (var wide = new Pen(color, width)) { outline.Widen(wide); Paint.Glow(g, outline, color, glow * 0.6f); }
                    }
                    using (var pen = new Pen(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawPath(pen, path);
                }
                else
                {
                    float tail = width * 2.5f;
                    PointF P(float along, float side) => new PointF(o.X + ux * along + px * side, o.Y + uy * along + py * side);
                    path.AddPolygon(new[] { P(0, width / 2), P(len, width * 0.15f), P(len, -width * 0.15f), P(0, -width / 2), P(-tail, -width * 0.3f), P(-tail, width * 0.3f) });
                    if (glow > 0) Paint.Glow(g, path, color, glow * 0.6f);
                    using (var brush = new SolidBrush(color)) g.FillPath(brush, path);
                }
            }
            if (!c.Bool("hub")) return;
            float d = width * 3.2f;
            using (var brush = new SolidBrush(color)) g.FillEllipse(brush, o.X - d / 2, o.Y - d / 2, d, d);
            float inner = d * 0.45f;
            using (var brush = new SolidBrush(c.Theme("background"))) g.FillEllipse(brush, o.X - inner / 2, o.Y - inner / 2, inner, inner);
        }
    }
}
