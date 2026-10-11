using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;

namespace AlmostFastOverlays
{
    /// <summary>
    /// Full-screen racing flag alerts shared by every dashboard layout so flags
    /// look and behave the same whichever dashboard is active.
    /// </summary>
    internal sealed class FlagPainter : IDisposable
    {
        private readonly int _width, _height;
        private readonly Font _flagFont = Fmt.FontOf(68, FontStyle.Bold);
        private readonly Font _flagSmallFont = Fmt.FontOf(34, FontStyle.Bold);
        private readonly Font _scFont = Fmt.FontOf(155, FontStyle.Bold);
        private readonly StringFormat _center = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap
        };

        public FlagPainter(int width, int height)
        {
            _width = width;
            _height = height;
        }

        public void Draw(Graphics g, TelemetrySnapshot telemetry)
        {
            switch (telemetry.Flag)
            {
                case RacingFlag.Green: Solid(g, Color.FromArgb(30, 205, 70), Brushes.Black, "GREEN"); break;
                case RacingFlag.White: Solid(g, Color.White, Brushes.Black, "WHITE"); break;
                case RacingFlag.Yellow: Solid(g, Color.FromArgb(255, 220, 0), Brushes.Black, "YELLOW"); break;
                case RacingFlag.Red: Solid(g, Color.FromArgb(225, 20, 35), Brushes.White, "RED"); break;
                case RacingFlag.Black:
                    g.Clear(Color.Black);
                    using (var pen = new Pen(Color.White, 8)) g.DrawRectangle(pen, 18, 18, _width - 36, _height - 36);
                    Text(g, "BLACK", _flagFont, Brushes.White, new RectangleF(0, 0, _width, _height));
                    break;
                case RacingFlag.Meatball:
                    g.Clear(Color.Black);
                    using (var orange = new SolidBrush(Color.FromArgb(245, 95, 30))) g.FillEllipse(orange, (_width - 245) / 2, 65, 245, 245);
                    Text(g, "MEATBALL", _flagSmallFont, Brushes.White, new RectangleF(0, 340, _width, 80));
                    break;
                case RacingFlag.BlueYellowStripe:
                    g.Clear(Color.FromArgb(35, 115, 210));
                    using (var stripe = new Pen(Color.FromArgb(255, 220, 0), 90)) g.DrawLine(stripe, -40, _height + 40, _width + 40, -40);
                    Backdrop(g, "BLUE");
                    break;
                case RacingFlag.SafetyCar:
                    g.Clear(Color.White);
                    Text(g, "SC", _scFont, Brushes.Black, new RectangleF(0, 0, _width, _height));
                    break;
                case RacingFlag.Surface:
                    for (int x = 0; x < _width; x += 95)
                        using (var brush = new SolidBrush((x / 95) % 2 == 0 ? Color.FromArgb(255, 220, 0) : Color.FromArgb(220, 25, 35)))
                            g.FillRectangle(brush, x, 0, 95, _height);
                    Backdrop(g, "SURFACE");
                    break;
                case RacingFlag.Checkered:
                    float cw = _width / 10f, ch = _height / 6f;
                    for (int r = 0; r < 6; r++)
                        for (int c = 0; c < 10; c++)
                            g.FillRectangle(((r + c) & 1) == 0 ? Brushes.White : Brushes.Black, c * cw, r * ch, cw + 1, ch + 1);
                    using (var backdrop = new SolidBrush(Color.FromArgb(255, 12, 14, 18))) g.FillRoundedRectangle(backdrop, new RectangleF(235, 125, 384, 230), 18);
                    Text(g, "FINISH", _flagSmallFont, Brushes.White, new RectangleF(235, 145, 384, 70));
                    Text(g, telemetry.Position > 0 ? "P" + telemetry.Position : "P--", _flagFont, Brushes.White, new RectangleF(235, 210, 384, 120));
                    break;
            }
        }

        private void Solid(Graphics g, Color background, Brush foreground, string label)
        {
            g.Clear(background);
            Text(g, label, _flagFont, foreground, new RectangleF(0, 0, _width, _height));
        }

        private void Backdrop(Graphics g, string label)
        {
            var box = new RectangleF(205, 182, _width - 410, 116);
            using (var backdrop = new SolidBrush(Color.FromArgb(255, 12, 14, 18))) g.FillRoundedRectangle(backdrop, box, 16);
            Text(g, label, _flagSmallFont, Brushes.White, box);
        }

        private void Text(Graphics g, string text, Font font, Brush brush, RectangleF bounds) =>
            g.DrawString(text, font, brush, bounds, _center);

        public void Dispose()
        {
            _flagFont.Dispose();
            _flagSmallFont.Dispose();
            _scFont.Dispose();
            _center.Dispose();
        }
    }

    /// <summary>
    /// A flag as cloth in a soft breeze. The flat flag is drawn once, then every frame
    /// it is bent by two slow travelling waves (stronger toward the free, right-hand
    /// edge), the folds catch the light or fall into shadow, and a faint weave moves
    /// with the fabric. About 4 ms a frame at 854×480.
    /// </summary>
    internal sealed class WavingFlag : IDisposable
    {
        private const int Steps = 1024;
        private static readonly float[] Sin = Enumerable.Range(0, Steps).Select(i => (float)Math.Sin(i * 2 * Math.PI / Steps)).ToArray();

        private readonly int _width, _height;
        private readonly FlagPainter _painter;
        private readonly sbyte[] _weave;
        private int[] _source, _output;
        private string _sourceKey;

        public WavingFlag(int width, int height)
        {
            _width = width;
            _height = height;
            _painter = new FlagPainter(width, height);
            // Fabric: a fine two-pixel weave with a little grain, a few percent either way.
            var random = new Random(11);
            _weave = new sbyte[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    _weave[y * width + x] = (sbyte)((((x >> 1) + (y >> 1)) & 1) == 0 ? 5 : -5 + random.Next(-3, 4));
        }

        /// <summary>Changes about 30 times a second, so the runtime knows to draw a new frame.</summary>
        public static int Frame(int tickCount) => tickCount / 33;

        public void Draw(Bitmap target, TelemetrySnapshot telemetry, int tickCount)
        {
            int[] source = Source(telemetry);
            int w = _width, h = _height;
            int[] output = _output ?? (_output = new int[w * h]);
            double seconds = tickCount / 1000.0;

            // Two waves: a long main swell and a shorter ripple across it, in table steps per pixel.
            float k1x = Steps / 520f, k1y = Steps / 1500f, k2x = Steps / 290f, k2y = -Steps / 820f;
            // Keep the phases small (wrapped to one cycle) so float precision never runs out.
            float t1 = (float)(seconds / 2.8 % 1 * Steps), t2 = (float)(seconds / 1.7 % 1 * Steps);
            const float Zoom = 0.95f;
            float cx = w / 2f, cy = h / 2f;
            var amplitude = new float[w];
            var column1 = new float[w];
            var column2 = new float[w];
            for (int x = 0; x < w; x++)
            {
                amplitude[x] = 2.5f + 9.5f * x / w;
                column1[x] = x * k1x;
                column2[x] = x * k2x;
            }

            // Rows are independent, so they're shared across the CPU's cores.
            System.Threading.Tasks.Parallel.For(0, h, y =>
            {
                float row1 = y * k1y - t1 + 4 * Steps, row2 = y * k2y - t2 + 4 * Steps;
                int rowStart = y * w;
                for (int x = 0; x < w; x++)
                {
                    int p1 = (int)(column1[x] + row1) & (Steps - 1), p2 = (int)(column2[x] + row2) & (Steps - 1);
                    float s1 = Sin[p1], c1 = Sin[(p1 + Steps / 4) & (Steps - 1)];
                    float s2 = Sin[p2], c2 = Sin[(p2 + Steps / 4) & (Steps - 1)];
                    float a = amplitude[x];
                    float dy = a * (s1 + 0.35f * s2), dx = a * 0.3f * c1;
                    // Sample between pixels (bilinear) so text and edges stay smooth as the cloth bends.
                    float fx = cx + (x - cx) * Zoom + dx, fy = cy + (y - cy) * Zoom + dy;
                    if (fx < 0) fx = 0; else if (fx > w - 1.001f) fx = w - 1.001f;
                    if (fy < 0) fy = 0; else if (fy > h - 1.001f) fy = h - 1.001f;
                    int sx = (int)fx, sy = (int)fy;
                    int at = sy * w + sx;
                    int color = Blend(source[at], source[at + 1], source[at + w], source[at + w + 1], (int)((fx - sx) * 256), (int)((fy - sy) * 256));
                    // Light falls on the slopes facing up-left; folds facing away go into soft shadow.
                    float shade = 0.016f * a * (c1 + 0.35f * c2) + _weave[at] * 0.004f;
                    output[rowStart + x] = Shade(color, shade);
                }
            });

            var rect = new Rectangle(0, 0, w, h);
            BitmapData data = target.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try { Marshal.Copy(output, 0, data.Scan0, output.Length); }
            finally { target.UnlockBits(data); }
        }

        /// <summary>Darkens toward black (negative) or lifts toward white (positive), so white flags still show their folds.</summary>
        private static int Shade(int argb, float amount)
        {
            int r = (argb >> 16) & 255, g = (argb >> 8) & 255, b = argb & 255;
            if (amount < 0)
            {
                float f = 1 + amount * 1.6f;
                r = (int)(r * f); g = (int)(g * f); b = (int)(b * f);
            }
            else
            {
                r += (int)((255 - r) * amount); g += (int)((255 - g) * amount); b += (int)((255 - b) * amount);
                // White has nowhere to go, so its lit side stays white and its shadows do the work.
            }
            return unchecked((int)0xFF000000) | (Clamp(r) << 16) | (Clamp(g) << 8) | Clamp(b);
        }

        /// <summary>Mixes four neighbouring pixels by fractions out of 256.</summary>
        private static int Blend(int a, int b, int c, int d, int fx, int fy)
        {
            if (a == b && a == c && a == d) return a;
            int ix = 256 - fx, iy = 256 - fy;
            int w00 = ix * iy, w10 = fx * iy, w01 = ix * fy, w11 = fx * fy;
            int r = (((a >> 16) & 255) * w00 + ((b >> 16) & 255) * w10 + ((c >> 16) & 255) * w01 + ((d >> 16) & 255) * w11) >> 16;
            int g = (((a >> 8) & 255) * w00 + ((b >> 8) & 255) * w10 + ((c >> 8) & 255) * w01 + ((d >> 8) & 255) * w11) >> 16;
            int bl = ((a & 255) * w00 + (b & 255) * w10 + (c & 255) * w01 + (d & 255) * w11) >> 16;
            return unchecked((int)0xFF000000) | (r << 16) | (g << 8) | bl;
        }

        private static int Clamp(int v) => v < 0 ? 0 : v > 255 ? 255 : v;

        private int[] Source(TelemetrySnapshot telemetry)
        {
            string key = telemetry.Flag + (telemetry.Flag == RacingFlag.Checkered ? "|" + telemetry.Position : "");
            if (_source != null && key == _sourceKey) return _source;
            using (var flat = new Bitmap(_width, _height, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(flat))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    _painter.Draw(g, telemetry);
                }
                BitmapData data = flat.LockBits(new Rectangle(0, 0, _width, _height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                _source = new int[_width * _height];
                try { Marshal.Copy(data.Scan0, _source, 0, _source.Length); }
                finally { flat.UnlockBits(data); }
            }
            _sourceKey = key;
            return _source;
        }

        public void Dispose() => _painter.Dispose();
    }

    /// <summary>Formatting and drawing helpers shared by the dashboard renderers.</summary>
    internal static class Fmt
    {
        public static Font FontOf(float pixels, FontStyle style) => new Font("Segoe UI", pixels, style, GraphicsUnit.Pixel);

        public static string Lap(TelemetrySnapshot t) =>
            t.TotalLaps > 0 ? Math.Max(1, t.LapsCompleted + 1) + "/" + t.TotalLaps : Math.Max(1, t.LapsCompleted + 1).ToString();

        public static string Gear(int gear) => gear < 0 ? "R" : gear == 0 ? "N" : gear.ToString();

        public static string Signed(double value) => value >= 0 ? "+" + value.ToString("0.00") : value.ToString("0.00");

        public static string LapTime(double seconds)
        {
            if (seconds <= 0 || double.IsNaN(seconds)) return "--:--.---";
            TimeSpan time = TimeSpan.FromSeconds(seconds);
            return ((int)time.TotalMinutes).ToString("0") + time.ToString(@"\:ss\.fff");
        }

        public static string Remaining(double seconds)
        {
            if (seconds < 0 || double.IsNaN(seconds)) return "--:--";
            TimeSpan t = TimeSpan.FromSeconds(Math.Ceiling(seconds));
            return t.TotalHours >= 1 ? ((int)t.TotalHours).ToString("0") + t.ToString(@"\:mm\:ss") : t.ToString(@"mm\:ss");
        }

        public static GraphicsPath Rounded(RectangleF b, float r)
        {
            var p = new GraphicsPath();
            float d = r * 2;
            p.AddArc(b.X, b.Y, d, d, 180, 90);
            p.AddArc(b.Right - d, b.Y, d, d, 270, 90);
            p.AddArc(b.Right - d, b.Bottom - d, d, d, 0, 90);
            p.AddArc(b.X, b.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    internal static class GraphicsExtensions
    {
        public static void FillRoundedRectangle(this Graphics g, Brush brush, RectangleF rectangle, float radius)
        {
            using (GraphicsPath path = Fmt.Rounded(rectangle, radius)) g.FillPath(brush, path);
        }
    }
}
