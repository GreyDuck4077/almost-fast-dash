using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;

namespace AlmostFastOverlays
{
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
                    // Oldest first: a moving gauge's passing shapes go, the steady ones (panels, digits) stay.
                    while (GlowCache.Count >= 300 && GlowOrder.Count > 0)
                    {
                        long old = GlowOrder.Dequeue();
                        if (GlowCache.TryGetValue(old, out Bitmap stale)) { stale.Dispose(); GlowCache.Remove(old); }
                    }
                    GlowCache[key] = small;
                    GlowOrder.Enqueue(key);
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
        private static readonly Queue<long> GlowOrder = new Queue<long>();

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

            // The dashboard audit notes every piece of text as drawn (after any shrinking), and can leave the text out
            // to see what's behind it.
            if (c.Renderer?.TextLog != null)
                using (Matrix placed = g.Transform)
                {
                    // In the dashboard's own coordinates: a widget with style effects is drawn on a layer of its own,
                    // shifted (never scaled) to its place, so the text's own coordinates are already the right ones.
                    var ink = RectangleF.FromLTRB(x, baseline - cap, x + total, baseline);
                    foreach (Run run in runs.Where(r => r.Text.Trim().Length > 0))
                        c.Renderer.TextLog.Add(new DrawnText
                        {
                            Widget = c.Widget, Text = string.Concat(runs.Select(r => r.Text)), Size = run.Font.Size * Math.Abs(placed.Elements[3]), Color = run.Color, Ink = ink
                        });
                }
            if (c.Renderer?.SkipText == true) return;

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

        /// <summary>A column with rounded top corners and a square foot, as a bar chart's bars stand on their axis.</summary>
        public static void FillTop(Graphics g, RectangleF b, Color color, float r)
        {
            if (b.Width <= 0 || b.Height <= 0) return;
            r = Math.Max(0, Math.Min(r, Math.Min(b.Width / 2, b.Height)));
            using (var brush = new SolidBrush(color))
            {
                if (r < 0.5f) { g.FillRectangle(brush, b); return; }
                using (var path = new GraphicsPath())
                {
                    path.AddArc(b.X, b.Y, r * 2, r * 2, 180, 90);
                    path.AddArc(b.Right - r * 2, b.Y, r * 2, r * 2, 270, 90);
                    path.AddLine(b.Right, b.Y + r, b.Right, b.Bottom);
                    path.AddLine(b.Right, b.Bottom, b.X, b.Bottom);
                    path.CloseFigure();
                    g.FillPath(brush, path);
                }
            }
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
