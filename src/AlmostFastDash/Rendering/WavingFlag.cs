using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;

namespace AlmostFastDash.Rendering
{
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

        /// <param name="painter">Draws the flat flags; shared, so the waving flag doesn't dispose it.</param>
        public WavingFlag(int width, int height, FlagPainter painter)
        {
            _width = width;
            _height = height;
            _painter = painter;
            // Fabric: a fine two-pixel weave with a little grain, a few percent either way.
            var random = new Random(11);
            _weave = new sbyte[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    _weave[y * width + x] = (sbyte)((((x >> 1) + (y >> 1)) & 1) == 0 ? 5 : -5 + random.Next(-3, 4));
        }

        /// <summary>Changes about 30 times a second, so the runtime knows to draw a new frame.</summary>
        public static int Frame(int tickCount) => tickCount / 33;

        public void Draw(Bitmap target, RacingFlag flag, TelemetrySnapshot telemetry, int tickCount) =>
            Wave(target, Source(flag, telemetry), tickCount);

        /// <summary>Waves any flat picture of the screen's size the same way (Rowdy's tribute); key names it for the cache.</summary>
        public void Draw(Bitmap target, Bitmap flat, string key, int tickCount) => Wave(target, Source(flat, key), tickCount);

        private void Wave(Bitmap target, int[] source, int tickCount)
        {
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
                    int color = Blend(source[at], source[at + 1], source[at + w], source[at + w + 1], (int)((fx - sx) * 256),
                        (int)((fy - sy) * 256));
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

        /// <summary>Mixes four neighboring pixels by fractions out of 256.</summary>
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

        private int[] Source(RacingFlag flag, TelemetrySnapshot telemetry)
        {
            string key = FlagPainter.Key(flag, telemetry);
            if (_source != null && key == _sourceKey) return _source;
            return Source(_painter.Picture(flag, telemetry), key);
        }

        private int[] Source(Bitmap flat, string key)
        {
            if (_source != null && key == _sourceKey) return _source;
            BitmapData data = flat.LockBits(new Rectangle(0, 0, _width, _height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            if (_source == null) _source = new int[_width * _height];
            try { Marshal.Copy(data.Scan0, _source, 0, _source.Length); }
            finally { flat.UnlockBits(data); }
            _sourceKey = key;
            return _source;
        }

        public void Dispose() { }
    }
}
