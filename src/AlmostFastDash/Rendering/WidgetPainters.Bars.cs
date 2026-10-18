using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;

namespace AlmostFastDash.Rendering
{
    internal static partial class WidgetPainters
    {
        public static void Bar(WidgetContext c)
        {
            Graphics g = c.G;
            RectangleF b = c.B;
            string order = FillOrder.Normalize(c.Choice("fill"));
            bool vertical = c.Choice("orientation") == "Vertical", reverse = order == FillOrder.EndToStart;
            var warp = new BarWarp(b, vertical, c.Num("bend") / 100, c.Num("peak") / 100, c.Num("taper") / 100, c.Num("bulge") / 100,
                c.Num("skew"), c.Num("thickness") / 100, c.Choice("grow"));
            if (!warp.IsStraight || c.Choice("colorMode") != "Solid" || !string.IsNullOrWhiteSpace(c.Raw("flash")) || FillOrder.Symmetric(order))
            {
                ShapedBar(c, warp);
                return;
            }
            string mode = c.Choice("mode");
            double value = c.Num("value");
            float radius = (float)c.Num("radius");
            if (radius < 0) radius = Math.Min(b.Width, b.Height) / 2;
            Color color = c.Color("color"), track = c.Color("track");
            float glow = (float)c.Num("glow");

            if (mode == "Segments")
            {
                int n = Math.Max(2, (int)c.Num("segments"));
                double f = Fraction(c, value);
                int lit = (int)Math.Round(f * n);
                float gap = 3;
                float len = ((vertical ? b.Height : b.Width) - gap * (n - 1)) / n;
                for (int i = 0; i < n; i++)
                {
                    int slot = reverse ? n - 1 - i : i;
                    RectangleF seg = vertical
                        ? new RectangleF(b.X, b.Bottom - (slot + 1) * len - slot * gap, b.Width, len)
                        : new RectangleF(b.X + slot * (len + gap), b.Y, len, b.Height);
                    bool on = i < lit;
                    if (on && glow > 0) using (GraphicsPath p = Paint.Rounded(seg, Math.Min(radius, Math.Min(seg.Width, seg.Height) / 2))) Paint.Glow(g, p, color, glow);
                    DashKit.Fill(g, seg, on ? color : track, Math.Min(radius, Math.Min(seg.Width, seg.Height) / 2));
                }
            }
            else if (mode == "Center")
            {
                DashKit.Fill(g, b, track, radius);
                double scale = Math.Max(Math.Abs(c.Num("min")), Math.Abs(c.Num("max")));
                double f = scale <= 0 ? 0 : Math.Max(-1, Math.Min(1, value / scale));
                if (reverse) f = -f;
                Color tone = value >= 0 ? color : c.Color("negColor");
                RectangleF fill;
                if (vertical)
                {
                    float half = b.Height / 2, mid = b.Y + half, len = (float)(Math.Abs(f) * half);
                    fill = f >= 0 ? new RectangleF(b.X, mid - len, b.Width, len) : new RectangleF(b.X, mid, b.Width, len);
                }
                else
                {
                    float half = b.Width / 2, mid = b.X + half, len = (float)(Math.Abs(f) * half);
                    fill = f >= 0 ? new RectangleF(mid, b.Y, len, b.Height) : new RectangleF(mid - len, b.Y, len, b.Height);
                }
                if (fill.Width >= 1 && fill.Height >= 1)
                {
                    float fr = Math.Min(radius, Math.Min(fill.Width, fill.Height) / 2);
                    if (glow > 0) using (GraphicsPath p = Paint.Rounded(fill, fr)) Paint.Glow(g, p, tone, glow);
                    DashKit.Fill(g, fill, tone, fr);
                }
                using (var tick = new Pen(c.Theme("text"), 2))
                {
                    if (vertical) g.DrawLine(tick, b.X - 3, b.Y + b.Height / 2, b.Right + 3, b.Y + b.Height / 2);
                    else g.DrawLine(tick, b.X + b.Width / 2, b.Y - 3, b.X + b.Width / 2, b.Bottom + 3);
                }
            }
            else
            {
                DashKit.Fill(g, b, track, radius);
                double f = Fraction(c, value);
                // A sliver is drawn at least as long as the bar is thick so rounded ends stay round.
                float thick = Math.Min(b.Width, b.Height);
                float len = (float)(f * (vertical ? b.Height : b.Width));
                if (len > 0 && len < thick) len = thick;
                RectangleF fill = vertical
                    ? (reverse ? new RectangleF(b.X, b.Y, b.Width, len) : new RectangleF(b.X, b.Bottom - len, b.Width, len))
                    : (reverse ? new RectangleF(b.Right - len, b.Y, len, b.Height) : new RectangleF(b.X, b.Y, len, b.Height));
                if (f > 0 && fill.Width >= 1 && fill.Height >= 1)
                {
                    float fr = Math.Min(radius, Math.Min(fill.Width, fill.Height) / 2);
                    if (glow > 0) using (GraphicsPath p = Paint.Rounded(fill, fr)) Paint.Glow(g, p, color, glow);
                    DashKit.Fill(g, fill, color, fr);
                }
            }

            if (!string.IsNullOrWhiteSpace(c.Raw("marker")))
            {
                double m = Fraction(c, c.Num("marker"));
                if (m > 0)
                    using (var pen = new Pen(c.Color("markerColor"), 2))
                    {
                        if (vertical)
                        {
                            float y = reverse ? b.Y + (float)(m * b.Height) : b.Bottom - (float)(m * b.Height);
                            g.DrawLine(pen, b.X - 5, y, b.Right + 5, y);
                        }
                        else
                        {
                            float x = reverse ? b.Right - (float)(m * b.Width) : b.X + (float)(m * b.Width);
                            g.DrawLine(pen, x, b.Y - 5, x, b.Bottom + 5);
                        }
                    }
            }
        }

        /// <summary>
        /// A bar drawn straight in the warp's flat space and carried onto its bent, tapered or slanted
        /// shape; also used for straight bars with shift colors, a gradient or a flash.
        /// </summary>
        private static void ShapedBar(WidgetContext c, BarWarp warp)
        {
            Graphics g = c.G;
            float length = warp.Length, thick = warp.Thickness;
            var flat = new RectangleF(0, 0, length, thick);
            string order = FillOrder.Normalize(c.Choice("fill"));
            bool reverse = order == FillOrder.EndToStart;
            string mode = c.Choice("mode"), colorMode = c.Choice("colorMode");
            double value = c.Num("value");
            float radius = (float)c.Num("radius");
            if (radius < 0) radius = Math.Min(length, thick) / 2;
            Color color = c.Color("color"), track = c.Color("track"), warn = c.Color("warnColor"), danger = c.Color("dangerColor");
            double warnAt = c.Num("warnAt"), dangerAt = c.Num("dangerAt");
            float glow = (float)c.Num("glow");
            bool flash = !string.IsNullOrWhiteSpace(c.Raw("flash")) && c.Bool("flash");
            Color flashColor = c.Color("flashColor");
            Color ToneAt(double at) => Tone(colorMode, at, color, warn, danger, warnAt, dangerAt);

            // The warped shapes of the track and segments stay the same while only the value moves, so they
            // are kept by geometry; a growing fill is warped afresh.
            string geometry = c.B + "|" + c.Raw("bend") + "|" + c.Raw("peak") + "|" + c.Raw("taper") + "|" + c.Raw("bulge") + "|" + c.Raw("skew") + "|" +
                c.Raw("thickness") + "|" + c.Raw("grow") + "|" + c.Raw("orientation") + "|" + radius + "|";
            void Draw(RectangleF r, Color fill, bool lit, Brush brush = null, bool keep = false)
            {
                if (r.Width < 0.5f || r.Height < 0.5f) return;
                GraphicsPath path = keep ? c.Renderer.Shape(geometry + r, () => Warped(r)) : Warped(r);
                try
                {
                    if (lit && glow > 0) Paint.Glow(g, path, fill, glow);
                    if (brush != null) g.FillPath(brush, path);
                    else using (var solid = new SolidBrush(fill)) g.FillPath(solid, path);
                }
                finally { if (!keep) path.Dispose(); }
            }

            GraphicsPath Warped(RectangleF r)
            {
                using (GraphicsPath flatPath = Paint.Rounded(r, Math.Min(radius, Math.Min(r.Width, r.Height) / 2))) return warp.Map(flatPath);
            }

            void Line(float x, float overhang, Color lineColor)
            {
                using (var flatPath = new GraphicsPath())
                {
                    flatPath.AddLine(x, -overhang, x, thick + overhang);
                    using (GraphicsPath path = warp.Map(flatPath))
                    using (var pen = new Pen(lineColor, 2)) g.DrawPath(pen, path);
                }
            }

            if (mode == "Segments")
            {
                int n = Math.Max(2, (int)c.Num("segments"));
                int steps = FillOrder.Steps(order, n);
                int lit = (int)Math.Round(Fraction(c, value) * steps);
                float gap = 3, len = (length - gap * (n - 1)) / n;
                for (int i = 0; i < n; i++)
                {
                    int rank = FillOrder.Rank(order, i, n);
                    bool on = flash || rank < lit;
                    Draw(new RectangleF(i * (len + gap), 0, len, thick), on ? (flash ? flashColor : ToneAt((rank + 0.5) / steps)) : track, on, null, true);
                }
            }
            else if (mode == "Center")
            {
                Draw(flat, track, false, null, true);
                double scale = Math.Max(Math.Abs(c.Num("min")), Math.Abs(c.Num("max")));
                double f = scale <= 0 ? 0 : Math.Max(-1, Math.Min(1, value / scale));
                if (reverse) f = -f;
                float half = length / 2, len = (float)(Math.Abs(f) * half);
                Draw(f >= 0 ? new RectangleF(half, 0, len, thick) : new RectangleF(half - len, 0, len, thick), value >= 0 ? color : c.Color("negColor"), true);
                Line(half, 3, c.Theme("text"));
            }
            else
            {
                Draw(flat, track, false, null, true);
                double f = flash ? 1 : Fraction(c, value);
                if (f > 0)
                {
                    // Each lit stretch grows from its own origin; filling from both ends gives two, each half as long.
                    float reach = FillOrder.Symmetric(order) ? 0.5f : 1f;
                    foreach ((float from, float to) in FillOrder.Spans(order, f))
                    {
                        float dir = to >= from ? 1 : -1;
                        // A sliver is drawn at least as long as the bar is thick so rounded ends stay round.
                        float len = Math.Abs(to - from) * length;
                        if (len < Math.Min(length * reach, thick)) len = Math.Min(length * reach, thick);
                        float start = from * length, end = start + dir * len;
                        var fill = new RectangleF(Math.Min(start, end), 0, len, thick);
                        if (flash) Draw(fill, flashColor, true);
                        else if (colorMode == "Gradient")
                        {
                            // The blend runs the full reach of the stretch, so the fill shows how far into the red it is.
                            PointF a = warp.Map(start, thick / 2), z = warp.Map(start + dir * reach * length, thick / 2);
                            if (Math.Abs(a.X - z.X) + Math.Abs(a.Y - z.Y) < 1) Draw(fill, ToneAt(f), true);
                            else
                            {
                                float w = (float)Math.Max(0.01, Math.Min(0.98, warnAt)), d = (float)Math.Max(w + 0.01, Math.Min(0.99, dangerAt));
                                using (var brush = new LinearGradientBrush(a, z, color, danger)
                                {
                                    InterpolationColors = new ColorBlend { Positions = new[] { 0f, w, d, 1f }, Colors = new[] { color, warn, danger, danger } },
                                    WrapMode = WrapMode.TileFlipX
                                })
                                    Draw(fill, ToneAt(f), true, brush);
                            }
                        }
                        else Draw(fill, ToneAt(f), true);
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(c.Raw("marker")))
            {
                double m = Fraction(c, c.Num("marker"));
                if (m > 0)
                    foreach (float at in FillOrder.Marks(order, m)) Line(at * length, 5, c.Color("markerColor"));
            }
        }

        public static void ShiftLights(WidgetContext c)
        {
            Graphics g = c.G;
            RectangleF b = c.B;
            int n = Math.Max(2, (int)c.Num("count"));
            double f = Math.Max(0, Math.Min(1, c.Num("value")));
            bool flash = c.Bool("flash");
            string order = FillOrder.Normalize(c.Choice("fill")), shape = c.Choice("shape");
            Color low = c.Color("lowColor"), mid = c.Color("midColor"), high = c.Color("highColor"), off = c.Color("offColor"), flashColor = c.Color("flashColor");
            float glow = (float)c.Num("glow");
            float cell = b.Width / n, gap = Math.Min(cell * 0.25f, 8);
            var tool = new ShapeTool(c);

            // Rank: the order a light comes on in (0 first). Steps: how many distinct ranks there are.
            int steps = FillOrder.Steps(order, n);
            int litSteps = (int)Math.Round(f * steps);
            for (int i = 0; i < n; i++)
            {
                int rank = FillOrder.Rank(order, i, n);
                bool on = flash || rank < litSteps;
                double at = (rank + 0.5) / steps;
                Color color = flash ? flashColor : at >= 0.8 ? high : at >= 0.5 ? mid : low;
                RectangleF r;
                float radius;
                if (shape == "Dot")
                {
                    float d = Math.Min(cell - gap, b.Height);
                    r = new RectangleF(b.X + i * cell + (cell - d) / 2, b.Y + (b.Height - d) / 2, d, d);
                    radius = d / 2;
                }
                else
                {
                    r = new RectangleF(b.X + i * cell + gap / 2, b.Y, cell - gap, b.Height);
                    radius = shape == "Pill" ? Math.Min(r.Width, r.Height) / 2 : 2;
                }
                if (on && glow > 0) using (GraphicsPath p = tool.Rect(r, radius)) Paint.Glow(g, p, color, glow);
                tool.Fill(g, r, on ? color : off, radius);
                if (on)
                    using (GraphicsPath p = tool.Rect(new RectangleF(r.X + r.Width * 0.2f, r.Y + r.Height * 0.12f, r.Width * 0.6f, r.Height * 0.3f), radius * 0.5f))
                    using (var shine = new SolidBrush(Color.FromArgb(50, 255, 255, 255))) g.FillPath(shine, p);
            }
        }
    }
}
