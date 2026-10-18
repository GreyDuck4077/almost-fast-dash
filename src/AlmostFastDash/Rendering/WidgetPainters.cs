using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace AlmostFastDash.Rendering
{
    /// <summary>How each widget type draws. All painters work in widget bounds (c.B).</summary>
    internal static partial class WidgetPainters
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
            var tool = new ShapeTool(c);
            using (GraphicsPath box = Paint.Box(b, r, corners))
            using (GraphicsPath path = tool.Apply(box))
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
                        using (GraphicsPath plainShadow = Paint.Box(Paint.Offset(b, 0, 4), r, corners))
                        using (GraphicsPath shadow = tool.Apply(plainShadow))
                            Paint.Glow(g, shadow, Color.FromArgb(150, 0, 0, 0), 10, 0.7f);
                        // Real glass: the backdrop frosted and bent by a lens-like rim that catches the light.
                        RectangleF extent = path.GetBounds();
                        var at = Rectangle.FromLTRB((int)Math.Floor(extent.Left) - 1, (int)Math.Floor(extent.Top) - 1, (int)Math.Ceiling(extent.Right) + 1, (int)Math.Ceiling(extent.Bottom) + 1);
                        if (at.Width * at.Height <= 2_000_000)
                        {
                            Bitmap pane = c.Renderer.Glass(c.Widget, c.Scope, at, path, new GlassSettings
                            {
                                Frost = (float)c.Num("glassFrost"), Depth = (float)c.Num("glassDepth"), Refraction = (float)c.Num("glassRefraction") / 100,
                                Shine = (float)c.Num("glassShine") / 100, Dispersion = (float)c.Num("glassDispersion") / 100, Light = (float)c.Num("glassLight"), Tint = fill
                            });
                            g.DrawImageUnscaled(pane, at.X, at.Y);
                            bw = 0;
                            break;
                        }
                        using (var body = new SolidBrush(Color.FromArgb(Math.Min(fill.A, (byte)150), fill))) g.FillPath(body, path);
                        RectangleF shaped = path.GetBounds();
                        using (var sheen = new LinearGradientBrush(Paint.Grow(shaped, 0, 1), Color.FromArgb(34, 255, 255, 255), Color.FromArgb(3, 255, 255, 255), 90f))
                        {
                            sheen.Blend = new Blend { Positions = new[] { 0f, 0.5f, 1f }, Factors = new[] { 0f, 0.85f, 1f } };
                            g.FillPath(sheen, path);
                        }
                        GraphicsState state = g.Save();
                        g.SetClip(path, CombineMode.Intersect);
                        // The bright inner top edge follows the shape's top.
                        using (var highlight = new Pen(Color.FromArgb(70, 255, 255, 255), 1.2f))
                        using (var rim = new GraphicsPath())
                        {
                            rim.AddLine(b.X + Math.Max(r, 6), b.Y + 1.2f, b.Right - Math.Max(r, 6), b.Y + 1.2f);
                            using (GraphicsPath top = tool.Apply(rim)) g.DrawPath(highlight, top);
                        }
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
                if (e.Width > 0 && e.Height > 0) tool.Fill(g, e, ec, 1.5f);
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
            c.G.SetClip(b, CombineMode.Intersect);
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
                    runs.Add(new Paint.Run(" " + unit, c.Font(unitSize > 0 ? unitSize : Math.Max(c.MinText, valueSize * 0.42f), unitSize > 0), c.Color("unitColor")));
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
        /// A color along a gauge (0 at its start, 1 at its end): one color, stepped shift colors,
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
    }
}
