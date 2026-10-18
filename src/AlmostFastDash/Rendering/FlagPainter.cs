using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace AlmostFastDash.Rendering
{
    /// <summary>
    /// The full-screen flags: each a cloth in the flag's colors with a title and what to do, set in
    /// tracked capitals. Patterned flags carry their words on a dark glass panel. The finish shows the
    /// result. Drawn to fit any screen size; the last picture is kept until the flag or result changes.
    /// </summary>
    internal sealed class FlagPainter : IDisposable
    {
        private readonly int _w, _h;
        private readonly float _s, _cx, _cy;
        private Bitmap _picture;
        private string _pictureKey;

        public FlagPainter(int width, int height)
        {
            _w = width;
            _h = height;
            _s = Math.Min(width / 854f, height / 480f);
            _cx = width / 2f;
            _cy = height / 2f;
        }

        /// <summary>What a flag's picture depends on: the finish also shows the result.</summary>
        public static string Key(RacingFlag flag, TelemetrySnapshot t) =>
            flag != RacingFlag.Checkered || t == null ? flag.ToString()
                : flag + "|" + t.Position + "|" + t.ClassPosition + "|" + t.StartPosition + "|" + t.BestLapSeconds.ToString("R");

        /// <summary>The flag's picture, drawn once and kept while it shows the same thing. Owned by the painter.</summary>
        public Bitmap Picture(RacingFlag flag, TelemetrySnapshot t)
        {
            string key = Key(flag, t);
            if (_picture != null && key == _pictureKey) return _picture;
            if (_picture == null) _picture = new Bitmap(_w, _h, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(_picture))
            {
                DashKit.Prepare(g);
                _canvas = _picture;
                Draw(g, flag, t);
            }
            _pictureKey = key;
            return _picture;
        }

        /// <summary>The bitmap being drawn, so glass can frost what is already on it.</summary>
        private Bitmap _canvas;

        private void Draw(Graphics g, RacingFlag flag, TelemetrySnapshot t)
        {
            t = t ?? TelemetrySnapshot.Disconnected;
            switch (flag)
            {
                case RacingFlag.Green: Solid(g, Color.FromArgb(34, 196, 84), FlagStyle.Ink, "GREEN", "GO  ·  GO  ·  GO"); break;
                case RacingFlag.Yellow: Solid(g, Color.FromArgb(255, 212, 0), FlagStyle.Ink, "CAUTION",
                    "SLOW DOWN  ·  NO OVERTAKING"); break;
                case RacingFlag.White: Solid(g, Color.FromArgb(244, 244, 240), FlagStyle.Ink, "FINAL LAP", "WHITE FLAG"); break;
                case RacingFlag.Red: Solid(g, Color.FromArgb(222, 30, 38), Color.White, "RED FLAG", "SESSION STOPPED"); break;
                case RacingFlag.SafetyCar: Solid(g, Color.FromArgb(255, 212, 0), FlagStyle.Ink, "SAFETY CAR", "HOLD YOUR POSITION"); break;
                case RacingFlag.Black: Black(g); break;
                case RacingFlag.Meatball: Meatball(g); break;
                case RacingFlag.BlueYellowStripe: Blue(g); break;
                case RacingFlag.Surface: Surface(g); break;
                case RacingFlag.Checkered: Checkered(g, t); break;
                default: g.Clear(Color.Black); break;
            }
        }

        // ------------------------------------------------------------------ the flags

        private void Solid(Graphics g, Color color, Color ink, string title, string subtitle)
        {
            Cloth(g, color);
            Words(g, title, subtitle, ink, _cy, 100, 25);
        }

        private void Black(Graphics g)
        {
            Cloth(g, Color.FromArgb(20, 22, 26));
            float s = _s, inset = 22 * s;
            using (var pen = new Pen(Color.FromArgb(244, 244, 240), 8 * s))
            using (GraphicsPath border = DashKit.Rounded(new RectangleF(inset, inset, _w - inset * 2, _h - inset * 2), 26 * s))
                g.DrawPath(pen, border);
            Words(g, "BLACK FLAG", "PIT TO SERVE YOUR PENALTY", Color.White, _cy, 92, 24);
        }

        private void Meatball(Graphics g)
        {
            Cloth(g, Color.FromArgb(20, 22, 26));
            float s = _s, r = 78 * s;
            var center = new PointF(_cx, _cy - 72 * s);
            // A warm glow round the disc, then the disc lit from the top left.
            using (var brush = new SolidBrush(Color.FromArgb(7, 255, 106, 26)))
                for (int i = 16; i >= 1; i--)
                {
                    float glow = r + i * 2.6f * s;
                    g.FillEllipse(brush, center.X - glow, center.Y - glow, glow * 2, glow * 2);
                }
            using (var disc = new GraphicsPath())
            {
                disc.AddEllipse(center.X - r, center.Y - r, r * 2, r * 2);
                using (var brush = new PathGradientBrush(disc)
                {
                    CenterPoint = new PointF(center.X - r * 0.3f, center.Y - r * 0.35f),
                    CenterColor = Color.FromArgb(255, 150, 72),
                    SurroundColors = new[] { Color.FromArgb(222, 78, 12) }
                })
                    g.FillPath(brush, disc);
            }
            Words(g, "DAMAGE", "PIT FOR REPAIRS", Color.White, _cy + 96 * s, 68, 22);
        }

        private void Blue(Graphics g)
        {
            Cloth(g, Color.FromArgb(28, 98, 232));
            float s = _s;
            GraphicsState state = g.Save();
            g.TranslateTransform(_cx, _cy);
            g.RotateTransform((float)(-Math.Atan2(_h, _w) * 180 / Math.PI));
            float length = (float)Math.Sqrt(_w * _w + _h * _h) + 40, half = 64 * s;
            using (var stripe = new LinearGradientBrush(new RectangleF(-length / 2, -half - 1, length, half * 2 + 2),
                Color.FromArgb(255, 222, 40), Color.FromArgb(240, 186, 0), 90f))
                g.FillRectangle(stripe, -length / 2, -half, length, half * 2);
            g.Restore(state);
            Vignette(g, 90);
            Panel(g, "BLUE FLAG", "LET THE FASTER CAR BY");
        }

        private void Surface(Graphics g)
        {
            // Yellow with red stripes, yellow at both edges.
            const int Stripes = 9;
            float width = _w / (float)Stripes;
            for (int i = 0; i < Stripes; i++)
                using (var brush = new SolidBrush(i % 2 == 0 ? Color.FromArgb(255, 212, 0) : Color.FromArgb(226, 36, 42)))
                    g.FillRectangle(brush, (float)Math.Floor(i * width), 0, (float)Math.Ceiling(width) + 1, _h);
            Shading(g);
            Vignette(g, 100);
            Panel(g, "SURFACE", "SLIPPERY TRACK AHEAD");
        }

        private void Checkered(Graphics g, TelemetrySnapshot t)
        {
            float s = _s, square = _h / 6f;
            int columns = (int)Math.Ceiling(_w / square) + 1;
            float left = (_w - columns * square) / 2;
            using (var light = new SolidBrush(Color.FromArgb(238, 238, 234)))
            using (var dark = new SolidBrush(Color.FromArgb(14, 15, 18)))
                for (int line = 0; line < 6; line++)
                    for (int column = 0; column < columns; column++)
                        g.FillRectangle(((line + column) & 1) == 0 ? light : dark, left + column * square, line * square, square + 0.5f,
                            square + 0.5f);
            Shading(g);
            Vignette(g, 170);

            bool placed = t.Position > 0;
            var stats = new List<Tuple<string, string, Color>>();
            if (placed && t.ClassPosition > 0 && t.ClassPosition != t.Position)
                stats.Add(Tuple.Create("CLASS", "P" + t.ClassPosition, Color.White));
            if (t.BestLapSeconds > 0)
                stats.Add(Tuple.Create("BEST LAP", Fmt.LapTime(t.BestLapSeconds), Color.White));
            if (placed && t.StartPosition > 0)
            {
                int gained = t.StartPosition - t.Position;
                stats.Add(Tuple.Create("PLACES", gained > 0 ? "+" + gained : gained < 0 ? "−" + -gained : "±0",
                    gained > 0 ? DashKit.Good : gained < 0 ? DashKit.Bad : Color.FromArgb(170, 178, 190)));
            }

            float width = 560 * s, height = (stats.Count > 0 ? 296 : 214) * s;
            var box = new RectangleF(_cx - width / 2, _cy - height / 2, width, height);
            Glass(g, box, dense: true);
            var label = Color.FromArgb(150, 160, 174);
            if (placed)
            {
                FlagType.Draw(g, "FINISH", FlagType.Semi, FontStyle.Regular, 19 * s, 7 * s, new PointF(_cx, box.Top + 40 * s), label);
                FlagType.Draw(g, "P" + t.Position, FlagType.Bold, FontStyle.Bold, 112 * s, 3 * s, new PointF(_cx, box.Top + 118 * s),
                    Color.White);
            }
            else
                FlagType.Draw(g, "FINISH", FlagType.Bold, FontStyle.Bold, 88 * s, 8 * s, new PointF(_cx, box.Top + 100 * s), Color.White);
            if (stats.Count == 0) return;

            float rule = box.Top + 190 * s;
            using (var pen = new Pen(Color.FromArgb(40, 255, 255, 255), Math.Max(1, s)))
            {
                g.DrawLine(pen, box.Left + 36 * s, rule, box.Right - 36 * s, rule);
                for (int i = 1; i < stats.Count; i++)
                {
                    float x = box.Left + box.Width * i / stats.Count;
                    g.DrawLine(pen, x, rule + 22 * s, x, box.Bottom - 22 * s);
                }
            }
            float row = (rule + box.Bottom) / 2;
            for (int i = 0; i < stats.Count; i++)
            {
                float x = box.Left + box.Width * (i + 0.5f) / stats.Count;
                FlagType.Draw(g, stats[i].Item1, FlagType.Semi, FontStyle.Regular, 14 * s, 2.5f * s, new PointF(x, row - 19 * s), label);
                FlagType.Draw(g, stats[i].Item2, FlagType.Bold, FontStyle.Bold, 32 * s, 0.5f * s, new PointF(x, row + 15 * s),
                    stats[i].Item3, box.Width / stats.Count - 20 * s);
            }
        }

        // ------------------------------------------------------------------ pieces

        /// <summary>The flag's color, a little lighter at the top, with a soft vignette.</summary>
        private void Cloth(Graphics g, Color color)
        {
            using (var brush = new LinearGradientBrush(new RectangleF(0, -1, _w, _h + 2), Shade(color, 0.12f), Shade(color, -0.12f), 90f))
                g.FillRectangle(brush, 0, 0, _w, _h);
            Vignette(g, color.GetBrightness() > 0.2f ? 80 : 120);
        }

        /// <summary>Light from above on a patterned flag.</summary>
        private void Shading(Graphics g)
        {
            using (var brush = new LinearGradientBrush(new RectangleF(0, -1, _w, _h + 2), Color.FromArgb(34, 255, 255, 255),
                Color.FromArgb(44, 0, 0, 0), 90f))
                g.FillRectangle(brush, 0, 0, _w, _h);
        }

        /// <summary>Darkens toward the edges and corners.</summary>
        private void Vignette(Graphics g, int strength)
        {
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(-_w * 0.3f, -_h * 0.45f, _w * 1.6f, _h * 1.9f);
                using (var brush = new PathGradientBrush(path)
                {
                    CenterColor = Color.FromArgb(0, 0, 0, 0),
                    SurroundColors = new[] { Color.FromArgb(strength, 0, 0, 0) },
                    Blend = new Blend { Positions = new[] { 0f, 0.5f, 1f }, Factors = new[] { 0f, 0.8f, 1f } }
                })
                    g.FillRectangle(brush, 0, 0, _w, _h);
            }
        }

        /// <summary>A title, a short rule and what to do, stacked and centred on <paramref name="middle"/>.</summary>
        private void Words(Graphics g, string title, string subtitle, Color ink, float middle, float titleSize, float subtitleSize)
        {
            float s = _s, t = titleSize * s, u = subtitleSize * s, rule = 3.5f * s;
            float titleCap = t * 0.7f, subtitleCap = u * 0.7f, gap1 = t * 0.25f, gap2 = u * 0.9f;
            float top = middle - (titleCap + gap1 + rule + gap2 + subtitleCap) / 2;
            float maxWidth = _w - 110 * s;
            FlagType.Draw(g, title, FlagType.Bold, FontStyle.Bold, t, t * 0.05f, new PointF(_cx, top + titleCap / 2), ink, maxWidth);
            float ruleY = top + titleCap + gap1;
            using (var brush = new SolidBrush(Color.FromArgb(140, ink)))
                g.FillRoundedRectangle(brush, new RectangleF(_cx - 28 * s, ruleY, 56 * s, rule), rule / 2);
            FlagType.Draw(g, subtitle, FlagType.Semi, FontStyle.Regular, u, u * 0.16f,
                new PointF(_cx, ruleY + rule + gap2 + subtitleCap / 2), Color.FromArgb(210, ink), maxWidth);
        }

        /// <summary>A glass panel sized to its words, centred on the flag.</summary>
        private void Panel(Graphics g, string title, string subtitle)
        {
            float s = _s;
            SizeF a = FlagType.Measure(g, title, FlagType.Bold, FontStyle.Bold, 66 * s, 66 * s * 0.05f);
            SizeF b = FlagType.Measure(g, subtitle, FlagType.Semi, FontStyle.Regular, 21 * s, 21 * s * 0.16f);
            float width = Math.Min(_w - 60 * s, Math.Max(420 * s, Math.Max(a.Width, b.Width) + 104 * s)), height = 168 * s;
            Glass(g, new RectangleF(_cx - width / 2, _cy - height / 2, width, height));
            Words(g, title, subtitle, Color.White, _cy, 66, 21);
        }

        /// <summary>
        /// Dark frosted glass (what's behind it, blurred and smoked) with a soft shadow and a rim lit from above.
        /// Dense glass smokes more, for a busy pattern behind it.
        /// </summary>
        private void Glass(Graphics g, RectangleF box, bool dense = false)
        {
            float s = _s, r = 26 * s;
            // Blur what's behind by shrinking it a long way and stretching it back.
            Rectangle area = Rectangle.Intersect(Rectangle.Round(box), new Rectangle(0, 0, _w, _h));
            Bitmap frost = null;
            if (_canvas != null && area.Width > 8 && area.Height > 8)
            {
                g.Flush();
                frost = new Bitmap(Math.Max(2, area.Width / 16), Math.Max(2, area.Height / 16), PixelFormat.Format32bppArgb);
                using (Graphics small = Graphics.FromImage(frost))
                {
                    small.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    small.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    small.DrawImage(_canvas, new Rectangle(0, 0, frost.Width, frost.Height), area, GraphicsUnit.Pixel);
                }
            }
            for (int i = 4; i >= 1; i--)
            {
                float spread = i * 4 * s;
                using (var shadow = new SolidBrush(Color.FromArgb(22, 0, 0, 0)))
                    g.FillRoundedRectangle(shadow,
                        new RectangleF(box.X - spread, box.Y - spread + 8 * s, box.Width + spread * 2, box.Height + spread * 2),
                        r + spread);
            }
            if (frost != null)
                using (frost)
                using (GraphicsPath clip = DashKit.Rounded(box, r))
                using (var edges = new ImageAttributes())
                {
                    edges.SetWrapMode(WrapMode.TileFlipXY);
                    GraphicsState state = g.Save();
                    g.SetClip(clip, CombineMode.Intersect);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(frost, area, 0, 0, frost.Width, frost.Height, GraphicsUnit.Pixel, edges);
                    g.Restore(state);
                }
            using (var fill = new LinearGradientBrush(new RectangleF(box.X, box.Y - 1, box.Width, box.Height + 2),
                Color.FromArgb(frost == null ? 228 : dense ? 214 : 176, 26, 29, 35),
                Color.FromArgb(frost == null ? 240 : dense ? 232 : 208, 9, 10, 13), 90f))
                g.FillRoundedRectangle(fill, box, r);
            using (GraphicsPath path = DashKit.Rounded(box, r))
            using (var rim = new LinearGradientBrush(new RectangleF(box.X, box.Y - 1, box.Width, box.Height + 2),
                Color.FromArgb(120, 255, 255, 255), Color.FromArgb(16, 255, 255, 255), 90f))
            using (var pen = new Pen(rim, Math.Max(1, 1.5f * s)))
                g.DrawPath(pen, path);
        }

        /// <summary>Lightens toward white (positive) or darkens toward black (negative).</summary>
        private static Color Shade(Color c, float amount) => amount >= 0
            ? Color.FromArgb(c.A, c.R + (int)((255 - c.R) * amount), c.G + (int)((255 - c.G) * amount), c.B + (int)((255 - c.B) * amount))
            : Color.FromArgb(c.A, (int)(c.R * (1 + amount)), (int)(c.G * (1 + amount)), (int)(c.B * (1 + amount)));

        public void Dispose() => _picture?.Dispose();
    }
}
