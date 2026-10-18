using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace AlmostFastDash.Rendering
{
    /// <summary>
    /// Fills shared by shapes, paths and the Style tools' recoloring: a solid color, a linear or radial gradient, or a
    /// pattern in two colors (stripes, checks, dots, carbon fibre, honeycomb, grid or brushed metal). A pattern is a
    /// small tile drawn once per look and repeated, turned and scaled over the widget, so it costs about what a
    /// gradient does. The second color left empty is see-through.
    /// </summary>
    internal static class Fills
    {
        public static readonly string[] Styles = { "Solid", "Linear", "Radial", "Pattern" };
        public static readonly string[] Patterns = { "Stripes", "Checks", "Dots", "Carbon", "Honeycomb", "Grid", "Brushed" };

        /// <summary>A pattern tile's side in pixels; at 100% it's shown at half that, so it stays crisp up to 200%.</summary>
        private const int TileSize = 32;

        private static readonly Dictionary<string, Bitmap> Tiles = new Dictionary<string, Bitmap>();

        /// <summary>
        /// A brush for a fill over <paramref name="box"/>. A radial gradient reaches the box's corners, or, for a
        /// <paramref name="round"/> shape (an ellipse, polygon or star, all inside the box's ellipse), its edge.
        /// </summary>
        public static Brush Brush(string style, Color a, Color b, float angle, string pattern, float scalePercent, float patternAngle, RectangleF box, bool round = false)
        {
            switch (style)
            {
                case "Linear":
                    return new LinearGradientBrush(RectangleF.Inflate(box, 1, 1), a, b, angle);
                case "Radial":
                {
                    // An ellipse round the box's corners, so the whole box is inside the gradient; a round shape's own edge.
                    float reach = round ? 0.5f : 0.7072f;
                    float rx = box.Width * reach + 1, ry = box.Height * reach + 1;
                    PointF middle = new PointF(box.X + box.Width / 2, box.Y + box.Height / 2);
                    using (var path = new GraphicsPath())
                    {
                        path.AddEllipse(middle.X - rx, middle.Y - ry, rx * 2, ry * 2);
                        return new PathGradientBrush(path) { CenterColor = a, SurroundColors = new[] { b }, CenterPoint = middle };
                    }
                }
                case "Pattern":
                    return PatternBrush(pattern, a, b, scalePercent, patternAngle, box.Location);
                default:
                    return new SolidBrush(a);
            }
        }

        /// <summary>A pattern laid from <paramref name="origin"/>, at a size (100 is a 16-pixel repeat) and an angle.</summary>
        public static TextureBrush PatternBrush(string pattern, Color a, Color b, float scalePercent, float angle, PointF origin)
        {
            Bitmap tile = Tile(pattern, a, b);
            var brush = new TextureBrush(tile, WrapMode.Tile);
            float scale = Math.Max(0.1f, Math.Min(10, scalePercent / 100f)) * 0.5f;
            brush.TranslateTransform(origin.X, origin.Y);
            brush.RotateTransform(angle);
            brush.ScaleTransform(scale, scale);
            return brush;
        }

        /// <summary>The tile for a pattern in two colors, drawn once and kept.</summary>
        internal static Bitmap Tile(string pattern, Color a, Color b)
        {
            string key = pattern + "|" + a.ToArgb() + "|" + b.ToArgb();
            lock (Tiles)
            {
                if (Tiles.TryGetValue(key, out Bitmap cached)) return cached;
                if (Tiles.Count > 200)
                {
                    foreach (Bitmap old in Tiles.Values) old.Dispose();
                    Tiles.Clear();
                }
                Bitmap tile = DrawTile(pattern, a, b);
                Tiles[key] = tile;
                return tile;
            }
        }

        private static Bitmap DrawTile(string pattern, Color a, Color b)
        {
            const int t = TileSize;
            int width = t, height = t;
            float hex = t / 2f;
            if (pattern == "Honeycomb") { width = (int)Math.Round(Math.Sqrt(3) * hex); height = (int)Math.Round(3 * hex); }
            if (pattern == "Brushed") { width = t * 4; height = t * 2; }
            var tile = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(tile))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(b);
                using (var ink = new SolidBrush(a))
                    switch (pattern)
                    {
                        case "Checks":
                            g.SmoothingMode = SmoothingMode.None;
                            g.FillRectangle(ink, 0, 0, t / 2, t / 2);
                            g.FillRectangle(ink, t / 2, t / 2, t / 2, t / 2);
                            break;
                        case "Dots":
                            g.FillEllipse(ink, t * 0.22f, t * 0.22f, t * 0.56f, t * 0.56f);
                            break;
                        case "Grid":
                            g.SmoothingMode = SmoothingMode.None;
                            g.FillRectangle(ink, 0, 0, t, 3);
                            g.FillRectangle(ink, 0, 0, 3, t);
                            break;
                        case "Honeycomb":
                        {
                            // Pointy-topped hexagons whose outlines meet across the tile's edges.
                            using (var pen = new Pen(a, 3f))
                                foreach (PointF centre in new[] { new PointF(0, 0), new PointF(width, 0), new PointF(width / 2f, height / 2f), new PointF(0, height), new PointF(width, height) })
                                {
                                    var corners = new PointF[6];
                                    for (int i = 0; i < 6; i++)
                                    {
                                        double angle = Math.PI / 180 * (60 * i - 90);
                                        corners[i] = new PointF(centre.X + hex * (float)Math.Cos(angle), centre.Y + hex * (float)Math.Sin(angle));
                                    }
                                    g.DrawPolygon(pen, corners);
                                }
                            break;
                        }
                        case "Carbon":
                        {
                            // A 2×2 twill, as carbon fibre is woven: each strand passes over two and under two, one step
                            // further along on every row, which makes the diagonal bands. Each bundle is lit across its
                            // length, like a rounded strand.
                            int cell = t / 4;
                            for (int cy = 0; cy < 4; cy++)
                                for (int cx = 0; cx < 4; cx++)
                                {
                                    var r = new Rectangle(cx * cell, cy * cell, cell, cell);
                                    bool across = (cx - cy + 4) % 4 < 2;
                                    using (var shade = new LinearGradientBrush(new Rectangle(r.X - 1, r.Y - 1, r.Width + 2, r.Height + 2), b, a, across ? 90f : 0f))
                                    {
                                        shade.SetBlendTriangularShape(0.5f, 1f);
                                        g.FillRectangle(shade, r);
                                    }
                                }
                            break;
                        }
                        case "Brushed":
                        {
                            // Fine streaks of the first color at random strengths, the same every time.
                            g.SmoothingMode = SmoothingMode.None;
                            var random = new Random(7);
                            for (int y = 0; y < height; y++)
                            {
                                int x = 0;
                                while (x < width)
                                {
                                    int run = random.Next(width / 4, width);
                                    using (var streak = new SolidBrush(Color.FromArgb(random.Next(40, 200) * a.A / 255, a)))
                                        g.FillRectangle(streak, x, y, Math.Min(run, width - x), 1);
                                    x += run + random.Next(0, 6);
                                }
                            }
                            break;
                        }
                        default:
                            // Stripes: the first color across the top half; turned, they run any way.
                            g.SmoothingMode = SmoothingMode.None;
                            g.FillRectangle(ink, 0, 0, t, t / 2);
                            break;
                    }
            }
            return tile;
        }

        /// <summary>A pen for a line: one color or a gradient across the box, dashed or not, with its ends and corners.</summary>
        public static Pen LinePen(Color a, Color? gradientTo, float angle, float width, string dash, string ends, string corners, RectangleF box)
        {
            Pen pen;
            if (gradientTo.HasValue)
                // The pen keeps its own copy of the brush.
                using (var brush = new LinearGradientBrush(RectangleF.Inflate(box, width + 1, width + 1), a, gradientTo.Value, angle)) pen = new Pen(brush, width);
            else pen = new Pen(a, width);
            LineCap cap = ends == "Flat" ? LineCap.Flat : ends == "Square" ? LineCap.Square : LineCap.Round;
            pen.StartCap = pen.EndCap = cap;
            pen.LineJoin = corners == "Sharp" ? LineJoin.Miter : corners == "Bevel" ? LineJoin.Bevel : LineJoin.Round;
            switch (dash)
            {
                case "Dashed":
                    pen.DashPattern = new[] { 3f, 2f };
                    pen.DashCap = cap == LineCap.Round ? DashCap.Round : DashCap.Flat;
                    break;
                case "Dotted":
                    // Round dots, spaced by their own size.
                    pen.DashPattern = new[] { 0.01f, 2f };
                    pen.DashCap = DashCap.Round;
                    break;
                case "Dash dot":
                    pen.DashPattern = new[] { 4f, 2f, 0.01f, 2f };
                    pen.DashCap = DashCap.Round;
                    break;
            }
            return pen;
        }
    }
}
