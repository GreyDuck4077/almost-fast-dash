using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;

namespace AlmostFastOverlays
{
    /// <summary>How each widget type draws. All painters work in widget bounds (c.B).</summary>
    internal static class WidgetPainters
    {
        // ================================================================== Basics

        public static void Panel(WidgetContext c)
        {
            Graphics g = c.G;
            RectangleF b = c.B;
            float r = (float)Math.Max(0, c.Num("radius"));
            string style = c.Choice("style");
            float glow = (float)c.Num("glow");
            string corners = c.Choice("corners");
            using (GraphicsPath path = Paint.Box(b, r, corners))
            {
                if (glow > 0) Paint.Glow(g, path, c.Color("glowColor"), glow);
                Color fill = c.Color("fill"), fill2 = c.Color("fill2"), tint = c.Color("tint"), border = c.Color("border");
                float bw = (float)c.Num("borderWidth");
                switch (style)
                {
                    case "Liquid":
                    {
                        // Frosted glass: a soft shadow below, a translucent body, a sheen that
                        // fades down from the top, a bright inner top edge and a rim that is
                        // brightest at the top, as light catches the edge of a glass pane.
                        using (GraphicsPath shadow = Paint.Box(Paint.Offset(b, 0, 4), r, corners)) Paint.Glow(g, shadow, Color.FromArgb(150, 0, 0, 0), 10, 0.7f);
                        using (var body = new SolidBrush(Color.FromArgb(Math.Min(fill.A, (byte)150), fill))) g.FillPath(body, path);
                        using (var sheen = new LinearGradientBrush(Paint.Grow(b, 0, 1), Color.FromArgb(34, 255, 255, 255), Color.FromArgb(3, 255, 255, 255), 90f))
                        {
                            sheen.Blend = new Blend { Positions = new[] { 0f, 0.5f, 1f }, Factors = new[] { 0f, 0.85f, 1f } };
                            g.FillPath(sheen, path);
                        }
                        GraphicsState state = g.Save();
                        g.SetClip(path);
                        using (var highlight = new Pen(Color.FromArgb(70, 255, 255, 255), 1.2f))
                            g.DrawLine(highlight, b.X + Math.Max(r, 6), b.Y + 1.2f, b.Right - Math.Max(r, 6), b.Y + 1.2f);
                        g.Restore(state);
                        using (var rim = new LinearGradientBrush(Paint.Grow(b, 0, 1), Color.FromArgb(105, 255, 255, 255), Color.FromArgb(18, 255, 255, 255), 90f))
                        using (var pen = new Pen(rim, Math.Max(1f, bw)))
                            g.DrawPath(pen, path);
                        bw = 0;
                        break;
                    }
                    case "Soft":
                        using (var brush = new LinearGradientBrush(Paint.Grow(b, 0, 1), fill2, fill, 90f)) g.FillPath(brush, path);
                        break;
                    case "Glass":
                        using (var brush = new SolidBrush(Color.FromArgb(Math.Min(fill.A, (byte)185), fill))) g.FillPath(brush, path);
                        using (var sheen = new LinearGradientBrush(Paint.Grow(b, 0, 1), Color.FromArgb(26, 255, 255, 255), Color.FromArgb(4, 255, 255, 255), 90f)) g.FillPath(sheen, path);
                        using (var highlight = new Pen(Color.FromArgb(46, 255, 255, 255), 1))
                            g.DrawLine(highlight, b.X + r, b.Y + 1, b.Right - (corners == "Cut diagonal" ? 1 : r), b.Y + 1);
                        border = Color.FromArgb(Math.Min(border.A, (byte)200), border);
                        break;
                    case "Solid":
                        using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
                        break;
                    case "Tinted":
                        using (var brush = new LinearGradientBrush(Paint.Grow(b, 0, 1), Color.FromArgb(70, tint), Color.FromArgb(22, tint), 90f)) g.FillPath(brush, path);
                        border = Color.FromArgb(150, tint);
                        break;
                }
                if (style != "None" && bw > 0)
                    using (var pen = new Pen(border, bw)) g.DrawPath(pen, path);
            }

            string edge = c.Choice("edge");
            if (edge != "None")
            {
                Color ec = c.Color("edgeColor");
                float t = 3, inset = Math.Max(8, r);
                RectangleF e =
                    edge == "Left" ? new RectangleF(b.X, b.Y + inset, t, b.Height - inset * 2) :
                    edge == "Right" ? new RectangleF(b.Right - t, b.Y + inset, t, b.Height - inset * 2) :
                    edge == "Top" ? new RectangleF(b.X + inset, b.Y, b.Width - inset * 2, t) :
                    new RectangleF(b.X + inset, b.Bottom - t, b.Width - inset * 2, t);
                if (e.Width > 0 && e.Height > 0) DashKit.Fill(g, e, ec, 1.5f);
            }

            string title = c.Text("title");
            if (!string.IsNullOrEmpty(title))
            {
                float size = (float)c.Num("titleSize");
                Paint.Text(g, c, title, size, false, c.Color("titleColor"), new RectangleF(b.X + 14, b.Y + 6, b.Width - 28, size + 8), StringAlignment.Near, "Top", false, 0, Color.Empty, false);
            }
        }

        public static void Text(WidgetContext c)
        {
            string text = c.Text("text");
            if (c.Choice("case") == "UPPER") text = text.ToUpperInvariant();
            float glow = (float)c.Num("glow");
            Paint.Text(c.G, c, text, (float)c.Num("size"), c.Choice("weight") != "Regular", c.Color("color"), c.B,
                Paint.Align(c.Choice("align")), c.Choice("valign"), c.Bool("fit"), glow, c.OptionalColor("glowColor") ?? c.Color("color"), c.Bool("shadow"),
                (float)c.Num("tracking"));
        }

        public static void Image(WidgetContext c)
        {
            Bitmap image = c.Renderer.Image(c.Text("image"));
            RectangleF b = c.B;
            if (image == null)
            {
                using (var pen = new Pen(Color.FromArgb(90, 255, 255, 255), 1) { DashStyle = DashStyle.Dash }) c.G.DrawRectangle(pen, b.X, b.Y, b.Width, b.Height);
                return;
            }
            RectangleF target = b;
            string fit = c.Choice("fit");
            if (fit != "Stretch")
            {
                float scale = fit == "Cover"
                    ? Math.Max(b.Width / image.Width, b.Height / image.Height)
                    : Math.Min(b.Width / image.Width, b.Height / image.Height);
                float w = image.Width * scale, h = image.Height * scale;
                target = new RectangleF(b.X + (b.Width - w) / 2, b.Y + (b.Height - h) / 2, w, h);
            }
            GraphicsState state = c.G.Save();
            c.G.SetClip(b);
            c.G.InterpolationMode = InterpolationMode.HighQualityBicubic;
            c.G.DrawImage(image, target);
            c.G.Restore(state);
        }

        // ================================================================== Values

        public static void Metric(WidgetContext c)
        {
            Graphics g = c.G;
            RectangleF b = c.B;
            string layout = c.Choice("layout");
            float inset = (float)c.Num("inset");
            float valueSize = (float)c.Num("valueSize"), labelSize = (float)c.Num("labelSize");
            StringAlignment align = Paint.Align(c.Choice("align"));
            if (align == StringAlignment.Center && layout != "Label left") inset = Math.Min(inset, 4);
            var inner = new RectangleF(b.X + inset, b.Y, Math.Max(1, b.Width - inset * 2), b.Height);
            string label = c.Text("label"), value = c.Text("value"), unit = c.Text("unit");
            Color labelColor = c.Color("labelColor");
            float glow = (float)c.Num("glow");
            Color valueColor = c.Color("valueColor");
            float tracking = (float)c.Num("labelTracking");

            void DrawValue(RectangleF area, StringAlignment a, string valign)
            {
                var runs = new List<Paint.Run> { new Paint.Run(value, c.Font(valueSize, true), valueColor) };
                if (!string.IsNullOrEmpty(unit))
                {
                    float unitSize = (float)c.Num("unitSize");
                    runs.Add(new Paint.Run(" " + unit, c.Font(unitSize > 0 ? unitSize : Math.Max(6, valueSize * 0.42f), unitSize > 0), c.Color("unitColor")));
                }
                Paint.Runs(g, c, runs, area, a, valign, true, glow, valueColor);
            }

            switch (layout)
            {
                case "Value only":
                    DrawValue(inner, align, "Middle");
                    break;
                case "Label left":
                    Paint.Text(g, c, label, labelSize, false, labelColor, inner, StringAlignment.Near, "Middle", true, 0, Color.Empty, false, tracking);
                    DrawValue(inner, StringAlignment.Far, "Middle");
                    break;
                case "Label below":
                {
                    float labelH = labelSize + 10;
                    DrawValue(new RectangleF(inner.X, inner.Y + 4, inner.Width, inner.Height - labelH - 4), align, "Middle");
                    Paint.Text(g, c, label, labelSize, false, labelColor, new RectangleF(inner.X, inner.Bottom - labelH, inner.Width, labelH - 4), align, "Middle", true, 0, Color.Empty, false, tracking);
                    break;
                }
                default:
                {
                    float labelH = labelSize + 10;
                    Paint.Text(g, c, label, labelSize, false, labelColor, new RectangleF(inner.X, inner.Y + 4, inner.Width, labelH), align, "Middle", true, 0, Color.Empty, false, tracking);
                    DrawValue(new RectangleF(inner.X, inner.Y + 4 + labelH, inner.Width, Math.Max(4, inner.Height - labelH - 8)), align, "Middle");
                    break;
                }
            }
        }

        public static void Gear(WidgetContext c)
        {
            Graphics g = c.G;
            RectangleF b = c.B;
            int gear = (int)Math.Round(c.Num("value"));
            bool flash = c.Bool("flash");
            Color color = flash ? c.Color("flashColor") : c.Color("color");
            float size = (float)c.Num("size"), glow = (float)c.Num("glow");
            bool connected = c.T.Connected;
            bool bindsLiveGear = c.Raw("value").Replace(" ", "") == "=Gear";
            float progress = c.GearProgress;
            if (c.Bool("animate") && bindsLiveGear && c.PreviousGear != gear && progress >= 0 && progress < 1)
            {
                float dir = gear > c.PreviousGear ? -1 : 1, travel = b.Height * 0.2f;
                Paint.Text(g, c, Fmt.Gear(c.PreviousGear), size, true, Color.FromArgb((int)(255 * (1 - progress)), color),
                    Paint.Offset(b, 0, -dir * travel * progress), StringAlignment.Center, "Middle", true, 0, color, false);
                Paint.Text(g, c, Fmt.Gear(gear), size, true, Color.FromArgb((int)(255 * progress), color),
                    Paint.Offset(b, 0, dir * travel * (1 - progress)), StringAlignment.Center, "Middle", true, 0, color, false);
                return;
            }
            Paint.Text(g, c, connected ? Fmt.Gear(gear) : "–", size, true, color, b, StringAlignment.Center, "Middle", true, glow,
                flash ? color : c.OptionalColor("glowColor") ?? color, false);
            if (!c.Bool("neighbors") || !connected) return;
            string prev = gear < 0 ? "" : gear == 0 ? "R" : gear == 1 ? "N" : (gear - 1).ToString();
            string next = gear < 0 ? "N" : gear == 0 ? "1" : c.T.MaxForwardGear > 0 && gear >= c.T.MaxForwardGear ? "" : (gear + 1).ToString();
            float ns = (float)c.Num("neighborSize");
            Color nc = c.Color("neighborColor");
            Paint.Text(g, c, prev, ns, true, nc, new RectangleF(b.X - ns * 1.2f, b.Y, ns * 1.4f, b.Height), StringAlignment.Center, "Middle", false, 0, Color.Empty, false);
            Paint.Text(g, c, next, ns, true, nc, new RectangleF(b.Right - ns * 0.2f, b.Y, ns * 1.4f, b.Height), StringAlignment.Center, "Middle", false, 0, Color.Empty, false);
        }

        // ================================================================== Gauges

        private static double Fraction(WidgetContext c, double value)
        {
            double min = c.Num("min"), max = c.Num("max");
            if (Math.Abs(max - min) < 1e-12) return 0;
            return Math.Max(0, Math.Min(1, (value - min) / (max - min)));
        }

        /// <summary>
        /// A colour along a gauge (0 at its start, 1 at its end): one colour, stepped shift colours,
        /// or a blend through them.
        /// </summary>
        private static Color Tone(string mode, double at, Color low, Color warn, Color danger, double warnAt, double dangerAt)
        {
            switch (mode)
            {
                case "Shift colors":
                    return at >= dangerAt ? danger : at >= warnAt ? warn : low;
                case "Gradient":
                    if (at >= dangerAt) return danger;
                    if (at >= warnAt) return Mix(warn, danger, dangerAt - warnAt < 1e-6 ? 1 : (at - warnAt) / (dangerAt - warnAt));
                    return Mix(low, warn, warnAt < 1e-6 ? 1 : Math.Max(0, at) / warnAt);
                default:
                    return low;
            }
        }

        private static Color Mix(Color a, Color b, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            int Ch(int x, int y) => (int)Math.Round(x + (y - x) * t);
            return Color.FromArgb(Ch(a.A, b.A), Ch(a.R, b.R), Ch(a.G, b.G), Ch(a.B, b.B));
        }

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

            void Draw(RectangleF r, Color fill, bool lit, Brush brush = null)
            {
                if (r.Width < 0.5f || r.Height < 0.5f) return;
                using (GraphicsPath flatPath = Paint.Rounded(r, Math.Min(radius, Math.Min(r.Width, r.Height) / 2)))
                using (GraphicsPath path = warp.Map(flatPath))
                {
                    if (lit && glow > 0) Paint.Glow(g, path, fill, glow);
                    if (brush != null) g.FillPath(brush, path);
                    else using (var solid = new SolidBrush(fill)) g.FillPath(solid, path);
                }
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
                    Draw(new RectangleF(i * (len + gap), 0, len, thick), on ? (flash ? flashColor : ToneAt((rank + 0.5) / steps)) : track, on);
                }
            }
            else if (mode == "Center")
            {
                Draw(flat, track, false);
                double scale = Math.Max(Math.Abs(c.Num("min")), Math.Abs(c.Num("max")));
                double f = scale <= 0 ? 0 : Math.Max(-1, Math.Min(1, value / scale));
                if (reverse) f = -f;
                float half = length / 2, len = (float)(Math.Abs(f) * half);
                Draw(f >= 0 ? new RectangleF(half, 0, len, thick) : new RectangleF(half - len, 0, len, thick), value >= 0 ? color : c.Color("negColor"), true);
                Line(half, 3, c.Theme("text"));
            }
            else
            {
                Draw(flat, track, false);
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
                if (zones && dangerAt > warnAt)
                    Band(start + (float)(warnAt * sweep), (float)((Math.Min(1, dangerAt) - warnAt) * sweep), TrackAt(warnAt), false, false, true);
                if (zones && dangerAt < 1)
                    Band(start + (float)(dangerAt * sweep), (float)((1 - dangerAt) * sweep), TrackAt(1), false, false, true);
                if (style == "Smooth")
                {
                    float s = flash ? sweep : (float)(f * sweep);
                    if (Math.Abs(s) >= 0.5f) Band(start, s, flash ? flashColor : ColorAt(f), true, true, true);
                }
            }

            ArcScale(c, ring, colorMode, danger, dangerAt);
            ArcNeedle(c, ring, start + (float)(f * sweep), glow);
        }

        /// <summary>Tick marks and numbers just inside the ring.</summary>
        private static void ArcScale(WidgetContext c, ArcRing ring, string colorMode, Color danger, double dangerAt)
        {
            int major = (int)Math.Round(c.Num("ticks"));
            if (major <= 0) return;
            major = Math.Min(major, 60);
            Graphics g = c.G;
            int minor = Math.Max(0, Math.Min(9, (int)Math.Round(c.Num("minorTicks"))));
            int total = major * (minor + 1);
            float tickLength = (float)c.Num("tickLength");
            Color tickColor = c.Color("tickColor");
            float Inside(float angle) => -ring.ThicknessAt(angle) / 2 - 4;
            using (var majorPen = new Pen(tickColor, 2))
            using (var minorPen = new Pen(Color.FromArgb(tickColor.A * 7 / 10, tickColor), 1))
                for (int i = 0; i <= total; i++)
                {
                    float a = ring.Start + ring.Sweep * i / total;
                    bool isMajor = i % (minor + 1) == 0;
                    float edge = Inside(a), len = isMajor ? tickLength : tickLength * 0.55f;
                    g.DrawLine(isMajor ? majorPen : minorPen, ring.At(a, edge), ring.At(a, edge - len));
                }
            if (!c.Bool("numbers")) return;
            double from = c.Num("numbersFrom"), to = c.Num("numbersTo");
            float size = (float)c.Num("numberSize");
            Color numberColor = c.Color("numberColor");
            for (int k = 0; k <= major; k++)
            {
                float a = ring.Start + ring.Sweep * k / major;
                PointF p = ring.At(a, Inside(a) - tickLength - size * 0.85f);
                double number = from + (to - from) * k / major;
                bool red = colorMode != "Solid" && (double)k / major >= dangerAt;
                Paint.Text(g, c, number.ToString("0.##"), size, true, red ? danger : numberColor, new RectangleF(p.X - size * 1.6f, p.Y - size * 0.7f, size * 3.2f, size * 1.4f),
                    StringAlignment.Center, "Middle", false, 0, Color.Empty, false);
            }
        }

        /// <summary>A needle from the centre to the value, with an optional cap over its pivot.</summary>
        private static void ArcNeedle(WidgetContext c, ArcRing ring, float angle, float glow)
        {
            string needle = c.Choice("needle");
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
                if (on && glow > 0) using (GraphicsPath p = Paint.Rounded(r, radius)) Paint.Glow(g, p, color, glow);
                DashKit.Fill(g, r, on ? color : off, radius);
                if (on)
                    using (GraphicsPath p = Paint.Rounded(new RectangleF(r.X + r.Width * 0.2f, r.Y + r.Height * 0.12f, r.Width * 0.6f, r.Height * 0.3f), radius * 0.5f))
                    using (var shine = new SolidBrush(Color.FromArgb(50, 255, 255, 255))) g.FillPath(shine, p);
            }
        }

        // ================================================================== Racing

        public static void Relative(WidgetContext c)
        {
            Graphics g = c.G;
            RectangleF b = c.B;
            int rows = c.Choice("rows") == "3" ? 3 : 5;
            RelativeEntry[] all = c.T.Relatives ?? new RelativeEntry[0];
            int player = Array.FindIndex(all, r => r.IsPlayer);
            float size = (float)c.Num("size");
            if (player < 0)
            {
                Paint.Text(g, c, "Waiting for cars", size * 0.85f, false, c.Theme("muted"), b, StringAlignment.Center, "Middle", true, 0, Color.Empty, false);
                return;
            }
            float rowH = b.Height / rows;
            int half = rows / 2;
            for (int slot = 0; slot < rows; slot++)
            {
                int index = player + slot - half;
                if (index < 0 || index >= all.Length) continue;
                RelativeRow(c, all[index], new RectangleF(b.X, b.Y + slot * rowH + 2, b.Width, rowH - 4), size);
            }
        }

        private static void RelativeRow(WidgetContext c, RelativeEntry r, RectangleF b, float size)
        {
            Graphics g = c.G;
            Color accent = c.Color("highlight"), text = c.Theme("text"), track = c.Theme("track"), muted = c.Theme("dim");
            if (r.IsPlayer)
            {
                DashKit.Fill(g, b, Color.FromArgb(40, accent), 8);
                DashKit.Fill(g, new RectangleF(b.X, b.Y + b.Height * 0.2f, 3, b.Height * 0.6f), accent, 1.5f);
            }
            Color nameColor = r.IsPlayer ? text : r.InPits ? muted
                : r.LapRelation > 0 ? c.Color("lappingColor") : r.LapRelation < 0 ? c.Color("lappedColor") : text;
            float boxH = Math.Min(26, b.Height - 6), x = b.X + 8;
            float boxY = b.Y + (b.Height - boxH) / 2;
            Color background = c.Theme("background");
            if (c.Bool("showPosition"))
            {
                var pos = new RectangleF(x, boxY, boxH * 1.15f, boxH);
                DashKit.Fill(g, pos, r.IsPlayer ? accent : track, 6);
                Paint.Text(g, c, r.Position > 0 ? r.Position.ToString() : "-", boxH * 0.58f, true, r.IsPlayer ? background : text, pos, StringAlignment.Center, "Middle", true, 0, Color.Empty, false);
                x = pos.Right + 6;
            }
            if (c.Bool("showNumber"))
            {
                var number = new RectangleF(x, boxY, boxH * 1.25f, boxH);
                DashKit.Fill(g, number, Color.FromArgb(226, 232, 240), 6);
                Paint.Text(g, c, r.CarNumber ?? "", boxH * 0.5f, true, background, number, StringAlignment.Center, "Middle", true, 0, Color.Empty, false);
                x = number.Right + 8;
            }
            float gapW = size * 3.4f;
            bool license = c.Bool("showLicense") && !string.IsNullOrEmpty(r.License);
            var nameBox = new RectangleF(x, b.Y, Math.Max(10, b.Right - x - gapW), license ? b.Height * 0.58f : b.Height);
            Paint.Text(g, c, r.IsPlayer ? "YOU" : r.Name, size, true, nameColor, nameBox, StringAlignment.Near, license ? "Bottom" : "Middle", true, 0, Color.Empty, false);
            if (license)
            {
                float lh = Math.Max(10, size * 0.9f);
                var lic = new RectangleF(x, b.Y + b.Height * 0.6f, lh * 2.6f, lh);
                DashKit.Fill(g, lic, Color.FromArgb(170, r.LicenseColor), 4);
                Paint.Text(g, c, r.License + " " + DashKit.IRating(r.IRating), lh * 0.68f, true, text, Paint.Grow(lic, -3, 0), StringAlignment.Center, "Middle", true, 0, Color.Empty, false);
                if (r.InPits)
                    Paint.Text(g, c, "PIT", lh * 0.75f, true, c.Theme("warn"), new RectangleF(lic.Right + 6, lic.Y, 40, lic.Height), StringAlignment.Near, "Middle", false, 0, Color.Empty, false);
            }
            if (!r.IsPlayer)
                Paint.Text(g, c, Math.Abs(r.Gap).ToString("0.0"), size * 1.15f, true, r.Gap < 0 ? c.Color("aheadColor") : c.Color("behindColor"),
                    new RectangleF(b.Right - gapW, b.Y, gapW - 8, b.Height), StringAlignment.Far, "Middle", true, 0, Color.Empty, false);
        }

        public static void BlindSpot(WidgetContext c)
        {
            Graphics g = c.G;
            RectangleF b = c.B;
            BlindSpotZone zone = c.Choice("side") == "Right" ? c.T.RightBlindSpot : c.T.LeftBlindSpot;
            Color color = c.Color("color"), off = c.Color("offColor");
            float glow = (float)c.Num("glow");
            float radius = Math.Min(b.Width, b.Height) / 2;
            if (c.Choice("shape") == "Zones")
            {
                float gap = 4, h = (b.Height - gap * 2) / 3;
                for (int i = 0; i < 3; i++)
                {
                    var seg = new RectangleF(b.X, b.Y + i * (h + gap), b.Width, h);
                    // Beside lights all three; front and rear light their end.
                    bool on = zone == BlindSpotZone.Beside || (zone == BlindSpotZone.Front && i == 0) || (zone == BlindSpotZone.Rear && i == 2);
                    float r = Math.Min(seg.Width, seg.Height) / 2;
                    if (on && glow > 0) using (GraphicsPath p = Paint.Rounded(seg, r)) Paint.Glow(g, p, color, glow);
                    DashKit.Fill(g, seg, on ? color : off, r);
                }
                return;
            }
            DashKit.Fill(g, b, off, radius);
            if (zone == BlindSpotZone.None) return;
            float focus = zone == BlindSpotZone.Front ? 0.15f : zone == BlindSpotZone.Rear ? 0.85f : 0.5f;
            using (GraphicsPath path = Paint.Rounded(b, radius))
            {
                if (glow > 0) Paint.Glow(g, path, color, glow);
                using (var brush = new LinearGradientBrush(Paint.Grow(b, 0, 1), color, color, 90f))
                {
                    var blend = new ColorBlend
                    {
                        Positions = new[] { 0f, Math.Max(0.001f, focus - 0.35f), focus, Math.Min(0.999f, focus + 0.35f), 1f },
                        Colors = new[] { Color.FromArgb(60, color), Color.FromArgb(120, color), color, Color.FromArgb(120, color), Color.FromArgb(60, color) }
                    };
                    brush.InterpolationColors = blend;
                    g.FillPath(brush, path);
                }
            }
        }

        public static void Flag(WidgetContext c)
        {
            RacingFlag flag = c.T.Flag;
            if (flag == RacingFlag.None) return;
            Graphics g = c.G;
            RectangleF b = c.B;
            float radius = (float)c.Num("radius");
            string name = flag == RacingFlag.BlueYellowStripe ? "BLUE" : flag == RacingFlag.SafetyCar ? "SAFETY CAR" : flag.ToString().ToUpperInvariant();
            Color main, ink;
            switch (flag)
            {
                case RacingFlag.Green: main = Color.FromArgb(30, 205, 70); ink = Color.Black; break;
                case RacingFlag.White: main = Color.White; ink = Color.Black; break;
                case RacingFlag.Yellow: main = Color.FromArgb(255, 220, 0); ink = Color.Black; break;
                case RacingFlag.Red: main = Color.FromArgb(225, 20, 35); ink = Color.White; break;
                case RacingFlag.Meatball: main = Color.FromArgb(245, 95, 30); ink = Color.White; break;
                case RacingFlag.BlueYellowStripe: main = Color.FromArgb(35, 115, 210); ink = Color.White; break;
                case RacingFlag.Surface: main = Color.FromArgb(255, 220, 0); ink = Color.Black; break;
                case RacingFlag.SafetyCar: main = Color.White; ink = Color.Black; break;
                default: main = Color.Black; ink = Color.White; break;
            }

            if (c.Choice("style") == "Badge")
            {
                DashKit.Fill(g, b, Color.FromArgb(230, 10, 12, 16), radius);
                float d = b.Height * 0.5f;
                var dot = new RectangleF(b.X + b.Height * 0.25f, b.Y + (b.Height - d) / 2, d, d);
                using (GraphicsPath p = Paint.Rounded(dot, d / 2)) Paint.Glow(g, p, main, 10);
                DashKit.Fill(g, dot, flag == RacingFlag.Black ? Color.FromArgb(40, 40, 40) : main, d / 2);
                if (c.Bool("label"))
                    Paint.Text(g, c, name, b.Height * 0.42f, true, Color.White, new RectangleF(dot.Right + 10, b.Y, b.Right - dot.Right - 18, b.Height), StringAlignment.Near, "Middle", true, 0, Color.Empty, false);
                return;
            }

            GraphicsState state = g.Save();
            using (GraphicsPath clip = Paint.Rounded(b, radius))
            {
                g.SetClip(clip);
                if (flag == RacingFlag.Checkered)
                {
                    float cell = Math.Max(6, b.Height / 4);
                    for (float y = b.Y; y < b.Bottom; y += cell)
                        for (float x = b.X; x < b.Right; x += cell)
                            g.FillRectangle((((int)((x - b.X) / cell) + (int)((y - b.Y) / cell)) & 1) == 0 ? Brushes.White : Brushes.Black, x, y, cell + 1, cell + 1);
                }
                else if (flag == RacingFlag.Meatball)
                {
                    g.FillRectangle(Brushes.Black, b);
                    float d = b.Height * 0.7f;
                    using (var orange = new SolidBrush(main)) g.FillEllipse(orange, b.X + (b.Width - d) / 2, b.Y + (b.Height - d) / 2, d, d);
                }
                else
                {
                    using (var brush = new SolidBrush(main)) g.FillRectangle(brush, b);
                    if (flag == RacingFlag.BlueYellowStripe)
                        using (var stripe = new Pen(Color.FromArgb(255, 220, 0), b.Height * 0.3f)) g.DrawLine(stripe, b.X, b.Bottom, b.Right, b.Y);
                    if (flag == RacingFlag.Surface)
                        for (float x = b.X; x < b.Right; x += b.Height * 0.5f)
                            if (((int)((x - b.X) / (b.Height * 0.5f)) & 1) == 1)
                                using (var red = new SolidBrush(Color.FromArgb(220, 25, 35))) g.FillRectangle(red, x, b.Y, b.Height * 0.5f, b.Height);
                    if (flag == RacingFlag.Black)
                        using (var pen = new Pen(Color.White, 3)) g.DrawPath(pen, clip);
                }
            }
            g.Restore(state);
            if (c.Bool("label") && flag != RacingFlag.Meatball)
            {
                bool backdrop = flag == RacingFlag.Checkered || flag == RacingFlag.BlueYellowStripe || flag == RacingFlag.Surface;
                var box = new RectangleF(b.X + b.Width * 0.2f, b.Y + b.Height * 0.22f, b.Width * 0.6f, b.Height * 0.56f);
                if (backdrop) DashKit.Fill(g, box, Color.FromArgb(205, 0, 0, 0), 6);
                Paint.Text(g, c, name, b.Height * 0.36f, true, backdrop ? Color.White : ink, box, StringAlignment.Center, "Middle", true, 0, Color.Empty, false);
            }
        }

        public static void Progress(WidgetContext c)
        {
            Graphics g = c.G;
            RectangleF b = c.B;
            TelemetrySnapshot t = c.T;
            Color color = c.Color("color"), caution = c.Color("cautionColor"), track = c.Color("track");
            float radius = Math.Min(b.Height / 2, 3);
            if (t.ProgressMode == SessionProgressMode.LapRace && t.TotalLaps > 0)
            {
                int laps = Math.Min(t.TotalLaps, 400);
                float gap = laps > 120 ? 0 : (float)c.Num("gap");
                float w = (b.Width - gap * (laps - 1)) / laps;
                for (int i = 0; i < laps; i++)
                {
                    bool done = i < t.LapsCompleted;
                    bool yellow = done && t.YellowCompletedLaps != null && i < t.YellowCompletedLaps.Length && t.YellowCompletedLaps[i];
                    DashKit.Fill(g, new RectangleF(b.X + i * (w + gap), b.Y, Math.Max(0.5f, w), b.Height), done ? (yellow ? caution : color) : track, Math.Min(radius, w / 2));
                }
                return;
            }
            DashKit.Fill(g, b, track, radius);
            double remaining = t.SessionTotalSeconds > 0 ? t.SessionSecondsRemaining / t.SessionTotalSeconds : 0;
            if (t.ProgressMode == SessionProgressMode.TimedRace)
            {
                Color tone = remaining > 0.6 ? color : remaining >= 0.25 ? c.Theme("warn") : c.Theme("bad");
                float w = (float)(Math.Max(0, Math.Min(1, remaining)) * b.Width);
                if (w >= 1) DashKit.Fill(g, new RectangleF(b.X, b.Y, w, b.Height), tone, radius);
            }
            else if (t.SessionTotalSeconds > 0)
            {
                float w = (float)(Math.Max(0, Math.Min(1, 1 - remaining)) * b.Width);
                if (w >= 1) DashKit.Fill(g, new RectangleF(b.X, b.Y, w, b.Height), color, radius);
            }
        }

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
            GraphicsState state = g.Save();
            foreach (bool upper in new[] { true, false })
            {
                g.SetClip(upper ? new RectangleF(b.X, b.Y - lw, b.Width, b.Height / 2 + lw) : new RectangleF(b.X, mid, b.Width, b.Height / 2 + lw));
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
            }
            g.Restore(state);
        }

        public static void LapHistory(WidgetContext c)
        {
            Graphics g = c.G;
            RectangleF b = c.B;
            double[] laps = (c.T.RecentLapSeconds ?? new double[0]).Where(l => l > 0).ToArray();
            if (laps.Length == 0)
            {
                Paint.Text(g, c, "No laps yet", 13, false, c.Theme("muted"), b, StringAlignment.Center, "Middle", true, 0, Color.Empty, false);
                return;
            }
            double fastest = laps.Min(), slowest = laps.Max();
            float rowH = b.Height / 5;
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

        // ================================================================== Decor

        public static void Shape(WidgetContext c)
        {
            Graphics g = c.G;
            RectangleF b = c.B;
            Color? fill = c.OptionalColor("fill");
            Color stroke = c.Color("stroke");
            float sw = (float)c.Num("strokeWidth"), glow = (float)c.Num("glow"), radius = (float)c.Num("radius");
            string shape = c.Choice("shape");
            using (var path = new GraphicsPath())
            {
                bool open = false;
                switch (shape)
                {
                    case "Ellipse": path.AddEllipse(b); break;
                    case "Line":
                        open = true;
                        if (b.Width >= b.Height) path.AddLine(b.X, b.Y + b.Height / 2, b.Right, b.Y + b.Height / 2);
                        else path.AddLine(b.X + b.Width / 2, b.Y, b.X + b.Width / 2, b.Bottom);
                        break;
                    case "Brackets":
                    {
                        open = true;
                        float arm = Math.Min(b.Width / 3, Math.Max(10, radius * 2.5f));
                        path.AddLines(new[] { new PointF(b.X + arm, b.Y), new PointF(b.X, b.Y), new PointF(b.X, b.Bottom), new PointF(b.X + arm, b.Bottom) });
                        path.StartFigure();
                        path.AddLines(new[] { new PointF(b.Right - arm, b.Y), new PointF(b.Right, b.Y), new PointF(b.Right, b.Bottom), new PointF(b.Right - arm, b.Bottom) });
                        break;
                    }
                    case "Diagonal down":
                        open = true;
                        path.AddLine(b.X, b.Y, b.Right, b.Bottom);
                        break;
                    case "Diagonal up":
                        open = true;
                        path.AddLine(b.X, b.Bottom, b.Right, b.Y);
                        break;
                    case "Chevron":
                        open = true;
                        path.AddLines(new[] { new PointF(b.X, b.Y), new PointF(b.Right, b.Y + b.Height / 2), new PointF(b.X, b.Bottom) });
                        break;
                    case "Triangle":
                        path.AddPolygon(new[] { new PointF(b.X + b.Width / 2, b.Y), new PointF(b.Right, b.Bottom), new PointF(b.X, b.Bottom) });
                        break;
                    default:
                        path.AddPath(Paint.Rounded(b, radius), false);
                        break;
                }
                if (glow > 0) Paint.Glow(g, path, fill ?? stroke, glow);
                if (fill.HasValue && !open) using (var brush = new SolidBrush(fill.Value)) g.FillPath(brush, path);
                if (sw > 0)
                    using (var pen = new Pen(stroke, sw) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                        g.DrawPath(pen, path);
            }
        }

        public static void Gradient(WidgetContext c)
        {
            Graphics g = c.G;
            RectangleF b = c.B;
            Color color = c.Color("color");
            int alpha = (int)(Math.Max(0, Math.Min(100, c.Num("strength"))) * 2.55);
            Color from = Color.FromArgb(alpha * color.A / 255, color);
            Color to = c.OptionalColor("color2") ?? Color.FromArgb(0, color);
            string style = c.Choice("style");
            if (b.Width < 1 || b.Height < 1) return;

            // Large soft gradients are slow to fill, so each look is drawn once and reused.
            string key = style + "|" + b.Width + "x" + b.Height + "|" + from.ToArgb() + "|" + to.ToArgb();
            lock (GradientCache)
            {
                if (!GradientCache.TryGetValue(key, out Bitmap image))
                {
                    if (GradientCache.Count >= 64)
                    {
                        foreach (Bitmap old in GradientCache.Values) old.Dispose();
                        GradientCache.Clear();
                    }
                    image = new Bitmap((int)Math.Ceiling(b.Width), (int)Math.Ceiling(b.Height), System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                    using (Graphics ig = Graphics.FromImage(image))
                    {
                        ig.SmoothingMode = SmoothingMode.AntiAlias;
                        var local = new RectangleF(0, 0, b.Width, b.Height);
                        switch (style)
                        {
                            case "Vertical":
                                using (var brush = new LinearGradientBrush(Paint.Grow(local, 0, 1), from, to, 90f)) ig.FillRectangle(brush, local);
                                break;
                            case "Horizontal":
                                using (var brush = new LinearGradientBrush(Paint.Grow(local, 1, 0), from, to, 0f)) ig.FillRectangle(brush, local);
                                break;
                            default:
                                using (var path = new GraphicsPath())
                                {
                                    path.AddEllipse(local);
                                    using (var brush = new PathGradientBrush(path) { CenterColor = from, SurroundColors = new[] { to } }) ig.FillPath(brush, path);
                                }
                                break;
                        }
                    }
                    GradientCache[key] = image;
                }
                g.DrawImageUnscaledAndClipped(image, Rectangle.Round(b));
            }
        }

        private static readonly Dictionary<string, Bitmap> GradientCache = new Dictionary<string, Bitmap>();
    }

    /// <summary>Drawing primitives shared by the widget painters.</summary>
    internal static class Paint
    {
        internal struct Run
        {
            public string Text;
            public Font Font;
            public Color Color;
            public Run(string text, Font font, Color color) { Text = text ?? ""; Font = font; Color = color; }
        }

        private static readonly StringFormat Typographic = CreateTypographic();

        private static StringFormat CreateTypographic()
        {
            var format = (StringFormat)StringFormat.GenericTypographic.Clone();
            format.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;
            return format;
        }

        public static StringAlignment Align(string align) =>
            align == "Center" ? StringAlignment.Center : align == "Right" ? StringAlignment.Far : StringAlignment.Near;

        public static RectangleF Grow(RectangleF r, float x, float y) => new RectangleF(r.X - x, r.Y - y, r.Width + x * 2, r.Height + y * 2);
        public static RectangleF Offset(RectangleF r, float x, float y) => new RectangleF(r.X + x, r.Y + y, r.Width, r.Height);

        public static GraphicsPath Rounded(RectangleF b, float r)
        {
            if (r <= 0.5f || b.Width < 2 || b.Height < 2)
            {
                var p = new GraphicsPath();
                p.AddRectangle(b);
                return p;
            }
            return DashKit.Rounded(b, r);
        }

        /// <summary>
        /// A soft blurred glow behind a shape. The shape is drawn into a tiny bitmap and
        /// scaled back up with bicubic filtering, which blurs it cheaply; a smaller
        /// bitmap gives a wider glow.
        /// </summary>
        public static void Glow(Graphics g, GraphicsPath path, Color color, float size, float intensity = 0.8f)
        {
            if (size <= 0 || color.A == 0 || path.PointCount == 0) return;
            RectangleF bounds = path.GetBounds();
            float spread = size * 1.6f;
            RectangleF area = Grow(bounds, spread, spread);
            float scale = Math.Max(0.04f, Math.Min(0.5f, 2.2f / size));
            float sw = area.Width * scale, sh = area.Height * scale;
            int w = Math.Max(2, (int)Math.Ceiling(sw) + 1), h = Math.Max(2, (int)Math.Ceiling(sh) + 1);
            if (w > 2048 || h > 2048) return;

            // Most glows (panels, lit LEDs, a gear digit) repeat frame after frame, so the
            // blurred bitmap is cached by shape, color and size.
            long key = GlowKey(path, color, size, intensity);
            lock (GlowCache)
            {
                if (!GlowCache.TryGetValue(key, out Bitmap small))
                {
                    small = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                    using (Graphics sg = Graphics.FromImage(small))
                    {
                        sg.SmoothingMode = SmoothingMode.AntiAlias;
                        sg.ScaleTransform(scale, scale);
                        sg.TranslateTransform(-area.X, -area.Y);
                        Color core = Color.FromArgb((int)Math.Min(255, color.A * intensity), color);
                        using (var brush = new SolidBrush(core)) sg.FillPath(brush, path);
                        using (var pen = new Pen(core, size * 0.5f) { LineJoin = LineJoin.Round }) sg.DrawPath(pen, path);
                    }
                    if (GlowCache.Count >= 400)
                    {
                        foreach (Bitmap old in GlowCache.Values) old.Dispose();
                        GlowCache.Clear();
                    }
                    GlowCache[key] = small;
                }
                // Map exactly the drawn part of the small bitmap back onto the area, so the glow stays centered.
                GraphicsState state = g.Save();
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(small, area, new RectangleF(0, 0, sw, sh), GraphicsUnit.Pixel);
                g.Restore(state);
            }
        }

        private static readonly Dictionary<long, Bitmap> GlowCache = new Dictionary<long, Bitmap>();

        private static long GlowKey(GraphicsPath path, Color color, float size, float intensity)
        {
            unchecked
            {
                long hash = 1469598103934665603;
                foreach (PointF p in path.PathPoints)
                {
                    hash = (hash ^ (long)Math.Round(p.X * 4)) * 1099511628211;
                    hash = (hash ^ (long)Math.Round(p.Y * 4)) * 1099511628211;
                }
                hash = (hash ^ color.ToArgb()) * 1099511628211;
                hash = (hash ^ (long)(size * 100)) * 1099511628211;
                return (hash ^ (long)(intensity * 1000)) * 1099511628211;
            }
        }

        /// <summary>Text in a box, optically centered on its capital height, shrinking to fit when asked.</summary>
        public static void Text(Graphics g, WidgetContext c, string text, float size, bool bold, Color color, RectangleF box,
            StringAlignment align, string valign, bool fit, float glow, Color glowColor, bool shadow, float tracking = 0)
        {
            if (string.IsNullOrEmpty(text) || size <= 0) return;
            Runs(g, c, new List<Run> { new Run(text, c.Font(size, bold), color) }, box, align, valign, fit, glow, glowColor, shadow, tracking);
        }

        /// <summary>
        /// Draws text runs on one baseline. Tracking adds space between letters, which
        /// gives small labels the spaced-out instrument look.
        /// </summary>
        public static void Runs(Graphics g, WidgetContext c, List<Run> runs, RectangleF box, StringAlignment align, string valign,
            bool fit, float glow, Color glowColor, bool shadow = false, float tracking = 0)
        {
            if (runs.Count == 0 || runs.All(r => r.Text.Length == 0)) return;
            tracking = Math.Max(0, tracking);
            float total = runs.Sum(r => Width(g, r, tracking));
            if (fit && total > box.Width && total > 0)
            {
                float scale = Math.Max(0.15f, box.Width / total);
                for (int i = 0; i < runs.Count; i++)
                    runs[i] = new Run(runs[i].Text, c.Renderer.Font(c.Text("font"), runs[i].Font.Size * scale, runs[i].Font.Bold), runs[i].Color);
                tracking *= scale;
                total = runs.Sum(r => Width(g, r, tracking));
            }
            float em = runs[0].Font.Size;
            float cap = em * 0.7f;
            float baseline =
                valign == "Top" ? box.Y + cap + em * 0.08f :
                valign == "Bottom" ? box.Bottom - em * 0.06f :
                box.Y + box.Height / 2 + cap / 2;
            float x = align == StringAlignment.Center ? box.X + (box.Width - total) / 2 : align == StringAlignment.Far ? box.Right - total : box.X;

            // In a mirrored widget the text keeps its mirrored place but is turned back to read normally.
            GraphicsState unmirror = null;
            if (c.FlipX || c.FlipY)
            {
                unmirror = g.Save();
                float mx = x + total / 2, my = baseline - cap / 2;
                g.TranslateTransform(mx, my);
                g.ScaleTransform(c.FlipX ? -1 : 1, c.FlipY ? -1 : 1);
                g.TranslateTransform(-mx, -my);
            }

            for (int i = 0; i < runs.Count; i++)
            {
                Run run = runs[i];
                float w = Width(g, run, tracking);
                if (run.Text.Length > 0)
                {
                    float top = baseline - Ascent(run.Font);
                    // With tracking each letter is placed on its own; otherwise the run is one string.
                    string[] pieces = tracking > 0 ? run.Text.Select(ch => ch.ToString()).ToArray() : new[] { run.Text };
                    float px = x;
                    foreach (string piece in pieces)
                    {
                        if (shadow)
                        {
                            // A slight shadow: about a twelfth of the letter height, at least one pixel.
                            float offset = Math.Max(1, run.Font.Size * 0.08f);
                            using (var brush = new SolidBrush(Color.FromArgb(190 * run.Color.A / 255, 0, 0, 0))) g.DrawString(piece, run.Font, brush, px + offset, top + offset, Typographic);
                        }
                        if (glow > 0)
                            using (var path = new GraphicsPath())
                            {
                                path.AddString(piece, run.Font.FontFamily, (int)run.Font.Style, run.Font.Size, new PointF(px, top), Typographic);
                                Glow(g, path, i == 0 ? glowColor : run.Color, glow, 0.55f);
                            }
                        using (var brush = new SolidBrush(run.Color)) g.DrawString(piece, run.Font, brush, px, top, Typographic);
                        px += g.MeasureString(piece, run.Font, int.MaxValue, Typographic).Width + tracking;
                    }
                }
                x += w;
            }
            if (unmirror != null) g.Restore(unmirror);
        }

        private static float Width(Graphics g, Run run, float tracking = 0)
        {
            if (run.Text.Length == 0) return 0;
            if (tracking <= 0) return g.MeasureString(run.Text, run.Font, int.MaxValue, Typographic).Width;
            float total = 0;
            foreach (char ch in run.Text) total += g.MeasureString(ch.ToString(), run.Font, int.MaxValue, Typographic).Width;
            return total + tracking * (run.Text.Length - 1);
        }

        /// <summary>
        /// A box outline: rounded corners, all four corners cut at 45°, or only the
        /// top-left and bottom-right cut (the angled race-display look).
        /// </summary>
        public static GraphicsPath Box(RectangleF b, float r, string corners)
        {
            if (corners == "Cut" || corners == "Cut diagonal")
            {
                float c = Math.Max(0, Math.Min(r, Math.Min(b.Width, b.Height) / 2));
                bool all = corners == "Cut";
                var p = new GraphicsPath();
                p.AddPolygon(new[]
                {
                    new PointF(b.X + c, b.Y), new PointF(b.Right - (all ? c : 0), b.Y), new PointF(b.Right, b.Y + (all ? c : 0)),
                    new PointF(b.Right, b.Bottom - c), new PointF(b.Right - c, b.Bottom), new PointF(b.X + (all ? c : 0), b.Bottom),
                    new PointF(b.X, b.Bottom - (all ? c : 0)), new PointF(b.X, b.Y + c)
                });
                return p;
            }
            return Rounded(b, r);
        }

        private static float Ascent(Font font)
        {
            FontFamily family = font.FontFamily;
            return font.Size * family.GetCellAscent(font.Style) / family.GetEmHeight(font.Style);
        }

        public static void Grid(Graphics g, RectangleF b, Color color)
        {
            using (var pen = new Pen(Color.FromArgb(Math.Min(color.A, (byte)160), color), 1) { DashStyle = DashStyle.Dot })
                for (int i = 1; i < 4; i++)
                {
                    float y = b.Y + b.Height * i / 4;
                    g.DrawLine(pen, b.X, y, b.Right, y);
                }
        }

        public static void Series(Graphics g, RectangleF b, float[] data, Func<float, float> map, Color color, float width, bool fill)
        {
            if (data == null || data.Length < 2) return;
            var points = new PointF[data.Length];
            for (int i = 0; i < data.Length; i++)
                points[i] = new PointF(b.X + b.Width * i / (data.Length - 1), b.Bottom - Math.Max(0, Math.Min(1, map(data[i]))) * b.Height);
            if (fill)
                using (var area = new GraphicsPath())
                {
                    area.AddLines(points);
                    area.AddLine(points[points.Length - 1], new PointF(b.Right, b.Bottom));
                    area.AddLine(new PointF(b.Right, b.Bottom), new PointF(b.X, b.Bottom));
                    area.CloseFigure();
                    using (var brush = new LinearGradientBrush(Grow(b, 0, 1), Color.FromArgb(70, color), Color.FromArgb(4, color), 90f)) g.FillPath(brush, area);
                }
            using (var pen = new Pen(color, width) { LineJoin = LineJoin.Round }) g.DrawLines(pen, points);
        }
    }
}
