using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;

namespace AlmostFastDash.Rendering
{
    internal static partial class WidgetPainters
    {
        // ================================================================== Decor

        public static void Shape(WidgetContext c)
        {
            Graphics g = c.G;
            RectangleF b = c.B;
            float radius = (float)c.Num("radius");
            string shape = c.Choice("shape");
            using (var path = new GraphicsPath())
            {
                bool open = false;
                switch (shape)
                {
                    case "Ellipse": path.AddEllipse(b); break;
                    case "Polygon":
                    case "Star":
                        path.AddPolygon(PathNodes.Polygon((int)c.Num("sides"), shape == "Star" ? (float)c.Num("inner") / 100f : 1f)
                            .Select(p => new PointF(b.X + p.X * b.Width, b.Y + p.Y * b.Height)).ToArray());
                        break;
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
                        using (GraphicsPath rounded = Paint.Rounded(b, radius)) path.AddPath(rounded, false);
                        break;
                }
                using (GraphicsPath shaped = new ShapeTool(c).Apply(path)) FillAndLine(c, g, shaped, b, open);
            }
        }

        /// <summary>A path drawn with the Pen: its points, fitted to the widget's box, filled and lined like a shape.</summary>
        public static void PathShape(WidgetContext c)
        {
            List<PathNode> nodes = PathNodes.Parse(c.Text("points"));
            bool closed = c.Bool("closed");
            using (GraphicsPath path = PathNodes.Build(nodes, closed, c.B))
            {
                if (path.PointCount == 0) return;
                using (GraphicsPath shaped = new ShapeTool(c).Apply(path)) FillAndLine(c, c.G, shaped, c.B, !closed);
            }
        }

        /// <summary>
        /// A drawn shape's glow, fill (a color, a gradient or a pattern; an empty second color is see-through) and line
        /// (a color or a gradient, dashed or not, with its ends and corners).
        /// </summary>
        private static void FillAndLine(WidgetContext c, Graphics g, GraphicsPath shaped, RectangleF b, bool open)
        {
            Color? fill = c.OptionalColor("fill");
            Color stroke = c.Color("stroke");
            float sw = (float)c.Num("strokeWidth"), glow = (float)c.Num("glow");
            if (glow > 0) Paint.Glow(g, shaped, fill ?? stroke, glow);
            if (fill.HasValue && !open)
            {
                Color second = c.OptionalColor("fill2") ?? Color.FromArgb(0, fill.Value);
                string kind = c.Choice("shape");
                using (Brush brush = Fills.Brush(c.Choice("fillStyle"), fill.Value, second, (float)c.Num("fillAngle"), c.Choice("pattern"),
                    (float)c.Num("patternScale"), (float)c.Num("patternAngle"), b, kind == "Ellipse" || kind == "Polygon" || kind == "Star"))
                    g.FillPath(brush, shaped);
            }
            if (sw > 0)
            {
                Color? to = c.Choice("strokeStyle") == "Gradient" ? c.OptionalColor("stroke2") ?? stroke : (Color?)null;
                using (Pen pen = Fills.LinePen(stroke, to, (float)c.Num("strokeAngle"), sw, c.Choice("dash"), c.Choice("cap"), c.Choice("join"), b))
                    g.DrawPath(pen, shaped);
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
}
