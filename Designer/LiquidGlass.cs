using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace AlmostFastOverlays
{
    /// <summary>The settings of one pane of glass.</summary>
    internal struct GlassSettings
    {
        /// <summary>How much the backdrop is frosted, in pixels of blur.</summary>
        public float Frost;
        /// <summary>How far in from the edge the rounded rim runs, in pixels.</summary>
        public float Depth;
        /// <summary>0 to 1: how strongly the rim bends the backdrop.</summary>
        public float Refraction;
        /// <summary>0 to 1: how bright the light on the rim is.</summary>
        public float Shine;
        /// <summary>0 to 1: how far the colours split at the rim, as a prism does.</summary>
        public float Dispersion;
        /// <summary>The direction the light comes from, in degrees (0 is right, 90 down, 225 top left).</summary>
        public float Light;
        public Color Tint;

        public string Key => string.Join(",", Frost, Depth, Refraction, Shine, Dispersion, Light, Tint.ToArgb());
    }

    /// <summary>
    /// Apple-style Liquid Glass drawn pixel by pixel. The pane frosts what lies behind it, and its edge
    /// is a rounded rim that works like a lens: it bends the backdrop, splits its colours a little, and
    /// catches a bright highlight on the side facing the light with a fainter one opposite. The rim is
    /// found by blurring the pane's own shape, so any outline works: rounded, cut corners, bent or
    /// tapered by the Shape tools.
    /// </summary>
    internal static class LiquidGlass
    {
        /// <param name="backdrop">What lies behind the pane: the whole screen's pixels, row by row.</param>
        /// <param name="at">The pane's box on the screen, in whole pixels.</param>
        /// <param name="shape">The pane's outline, in screen coordinates.</param>
        public static Bitmap Render(int[] backdrop, int screenW, int screenH, Rectangle at, GraphicsPath shape, GlassSettings s)
        {
            int w = Math.Max(1, at.Width), h = Math.Max(1, at.Height);
            float depth = Math.Max(2, Math.Min(s.Depth, Math.Min(w, h) / 2f));

            // The pane's coverage, antialiased.
            float[] mask = new float[w * h];
            using (var shapeBitmap = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(shapeBitmap))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.TranslateTransform(-at.X, -at.Y);
                    g.FillPath(Brushes.White, shape);
                }
                int[] px = Pixels(shapeBitmap);
                for (int i = 0; i < mask.Length; i++) mask[i] = ((px[i] >> 24) & 0xFF) / 255f;
            }

            // The rim's height: the shape blurred across the rim's depth is 0.5 at the edge and 1 a rim's
            // depth inside. A quarter circle over that makes the rounded, lens-like edge.
            float[] height = (float[])mask.Clone();
            Blur(height, w, h, Math.Max(1, (int)Math.Round(depth / 2)), 3);
            float[] z = new float[w * h];
            for (int i = 0; i < z.Length; i++)
            {
                float t = Math.Max(0, Math.Min(1, (height[i] - 0.5f) * 2));
                z[i] = (float)Math.Sqrt(1 - (1 - t) * (1 - t));
            }

            // The backdrop around the pane, frosted. It reaches past the pane far enough for the rim to bend it in.
            int margin = (int)Math.Ceiling(depth * (1 + s.Refraction) + s.Frost * 2 + 4);
            int cx = at.X - margin, cy = at.Y - margin, cw = w + margin * 2, ch = h + margin * 2;
            float[] r = new float[cw * ch], gr = new float[cw * ch], bl = new float[cw * ch];
            for (int y = 0; y < ch; y++)
            {
                int sy = Math.Max(0, Math.Min(screenH - 1, cy + y));
                for (int x = 0; x < cw; x++)
                {
                    int sx = Math.Max(0, Math.Min(screenW - 1, cx + x));
                    int p = backdrop[sy * screenW + sx], i = y * cw + x;
                    r[i] = (p >> 16) & 0xFF; gr[i] = (p >> 8) & 0xFF; bl[i] = p & 0xFF;
                }
            }
            int frost = Math.Max(0, (int)Math.Round(s.Frost / 2));
            if (frost > 0) { Blur(r, cw, ch, frost, 3); Blur(gr, cw, ch, frost, 3); Blur(bl, cw, ch, frost, 3); }

            double light = s.Light * Math.PI / 180;
            float lx = (float)Math.Cos(light), ly = (float)Math.Sin(light);
            float tintA = s.Tint.A / 255f * 0.38f;
            float shine = Math.Max(0, Math.Min(1, s.Shine)), bend = Math.Max(0, Math.Min(1, s.Refraction)) * depth;
            float split = Math.Max(0, Math.Min(1, s.Dispersion)) * 0.22f;
            var output = new int[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    float a = mask[i];
                    if (a <= 0.002f) continue;
                    float zc = z[i];
                    // The slope of the rim; the surface rises inward, so the outward normal points down the slope.
                    float gx = (z[y * w + Math.Min(w - 1, x + 1)] - z[y * w + Math.Max(0, x - 1)]) / 2;
                    float gy = (z[Math.Min(h - 1, y + 1) * w + x] - z[Math.Max(0, y - 1) * w + x]) / 2;
                    float len = (float)Math.Sqrt(gx * gx + gy * gy);
                    float nx = 0, ny = 0;
                    if (len > 1e-5f) { nx = -gx / len; ny = -gy / len; }
                    float slope = Math.Min(1, len * depth * 0.8f);

                    // The rim bends the backdrop: each pixel looks further in, most at the very edge, with red and
                    // blue bent a little more and less than green.
                    float off = bend * (1 - zc);
                    float bx = x + margin, by = y + margin;
                    float cr = Sample(r, cw, ch, bx - nx * off * (1 + split), by - ny * off * (1 + split));
                    float cg = Sample(gr, cw, ch, bx - nx * off, by - ny * off);
                    float cb = Sample(bl, cw, ch, bx - nx * off * (1 - split), by - ny * off * (1 - split));

                    // Seen through glass, colours are a little richer (Apple's vibrancy).
                    float grey = cr * 0.299f + cg * 0.587f + cb * 0.114f;
                    cr = grey + (cr - grey) * 1.35f; cg = grey + (cg - grey) * 1.35f; cb = grey + (cb - grey) * 1.35f;
                    // The glass's own tint.
                    cr += (s.Tint.R - cr) * tintA; cg += (s.Tint.G - cg) * tintA; cb += (s.Tint.B - cb) * tintA;
                    // A soft wash of light across the pane from the side the light comes from.
                    float toward = Math.Max(0, Math.Min(1, 0.5f + (x / (float)w - 0.5f) * lx + (y / (float)h - 0.5f) * ly));
                    float lift = 0.015f + 0.075f * toward * toward;
                    // The thickness of the glass darkens the rim a little just inside the light.
                    float rim = 1 - zc, inner = zc * rim * 4;
                    float shade = 1 - 0.22f * inner * slope;
                    cr *= shade; cg *= shade; cb *= shade;
                    // Light on the rim: a crisp highlight where it faces the light and a softer one on the far side,
                    // gathered at the outer edge, plus a hairline of light all the way round.
                    float facing = nx * lx + ny * ly;
                    float near = Math.Max(0, facing), far = Math.Max(0, -facing);
                    float gather = rim * rim;
                    float spec = shine * gather * (1.6f * near * near * near + 0.6f * far * far * far);
                    float edge = shine * 0.28f * Math.Max(0, 1 - zc * 7) * (0.5f + 0.5f * near);
                    // Light travelling through the rim makes it glow faintly all the way round, brightest outside.
                    float glowBand = shine * 0.09f * rim * (float)Math.Sqrt(rim);
                    float light01 = Math.Min(1, lift + spec + edge + glowBand);
                    cr += (255 - cr) * light01; cg += (255 - cg) * light01; cb += (255 - cb) * light01;

                    int A = (int)(a * 255 + 0.5f);
                    output[i] = (A << 24) | (Pre(cr, a) << 16) | (Pre(cg, a) << 8) | Pre(cb, a);
                }

            var result = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            BitmapData data = result.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try
            {
                for (int y = 0; y < h; y++) Marshal.Copy(output, y * w, data.Scan0 + y * data.Stride, w);
            }
            finally { result.UnlockBits(data); }
            return result;
        }

        private static int Pre(float channel, float alpha) => Math.Max(0, Math.Min(255, (int)(channel * alpha + 0.5f)));

        /// <summary>A bitmap's pixels as ARGB ints, row by row.</summary>
        public static int[] Pixels(Bitmap bitmap)
        {
            int w = bitmap.Width, h = bitmap.Height;
            var pixels = new int[w * h];
            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < h; y++) Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * w, w);
            }
            finally { bitmap.UnlockBits(data); }
            return pixels;
        }

        private static float Sample(float[] channel, int w, int h, float x, float y)
        {
            x = Math.Max(0, Math.Min(w - 1.001f, x));
            y = Math.Max(0, Math.Min(h - 1.001f, y));
            int x0 = (int)x, y0 = (int)y;
            float fx = x - x0, fy = y - y0;
            int i = y0 * w + x0;
            float top = channel[i] + (channel[i + 1] - channel[i]) * fx;
            float bottom = channel[i + w] + (channel[i + w + 1] - channel[i + w]) * fx;
            return top + (bottom - top) * fy;
        }

        /// <summary>Repeated box blurs, which together come close to a Gaussian. Outside the array counts as 0.</summary>
        private static void Blur(float[] values, int w, int h, int radius, int passes)
        {
            var temp = new float[values.Length];
            for (int pass = 0; pass < passes; pass++)
            {
                BoxRows(values, temp, w, h, radius);
                BoxColumns(temp, values, w, h, radius);
            }
        }

        private static void BoxRows(float[] src, float[] dst, int w, int h, int r)
        {
            float scale = 1f / (r * 2 + 1);
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                float sum = 0;
                for (int x = 0; x <= Math.Min(r, w - 1); x++) sum += src[row + x];
                for (int x = 0; x < w; x++)
                {
                    dst[row + x] = sum * scale;
                    int add = x + r + 1, remove = x - r;
                    if (add < w) sum += src[row + add];
                    if (remove >= 0) sum -= src[row + remove];
                }
            }
        }

        private static void BoxColumns(float[] src, float[] dst, int w, int h, int r)
        {
            float scale = 1f / (r * 2 + 1);
            for (int x = 0; x < w; x++)
            {
                float sum = 0;
                for (int y = 0; y <= Math.Min(r, h - 1); y++) sum += src[y * w + x];
                for (int y = 0; y < h; y++)
                {
                    dst[y * w + x] = sum * scale;
                    int add = y + r + 1, remove = y - r;
                    if (add < h) sum += src[add * w + x];
                    if (remove >= 0) sum -= src[remove * w + x];
                }
            }
        }
    }
}
