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
        /// <summary>0 to 1: how far the colors split at the rim, as a prism does.</summary>
        public float Dispersion;
        /// <summary>The direction the light comes from, in degrees (0 is right, 90 down, 225 top left).</summary>
        public float Light;
        public Color Tint;

        public string Key => string.Join(",", Frost, Depth, Refraction, Shine, Dispersion, Light, Tint.ToArgb());
    }

    /// <summary>
    /// Apple-style Liquid Glass drawn pixel by pixel. The pane frosts what lies behind it, and its edge
    /// is a rounded rim that works like a lens: it bends the backdrop, splits its colors a little, and
    /// catches a bright highlight on the side facing the light with a fainter one opposite. The rim is
    /// found by blurring the pane's own shape, so any outline works: rounded, cut corners, bent or
    /// tapered by the Shape tools.
    /// </summary>
    internal static class LiquidGlass
    {
        /// <summary>How far past the pane the backdrop must reach for the rim to bend and reflect it.</summary>
        public static int Margin(GlassSettings s) => (int)Math.Ceiling(s.Depth * (1.6f + s.Refraction) + s.Frost * 2 + 6);

        /// <param name="backdrop">What lies behind the pane, row by row: the part of the screen in <paramref name="area"/>.</param>
        /// <param name="area">Where the backdrop sits on the screen; it should reach <see cref="Margin"/> past the pane.</param>
        /// <param name="at">The pane's box on the screen, in whole pixels.</param>
        /// <param name="shape">The pane's outline, in screen coordinates.</param>
        public static Bitmap Render(int[] backdrop, Rectangle area, Rectangle at, GraphicsPath shape, GlassSettings s)
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

            // The exact distance from the edge, so the rim is equally wide along the sides and round the corners.
            // Pixels the edge passes through use their coverage for the part of a pixel.
            float[] dist = InsideDistance(mask, w, h);
            Blur(dist, w, h, 1, 1);
            float[] z = new float[w * h];
            for (int i = 0; i < z.Length; i++)
            {
                // A quarter circle over the rim's depth: the rounded, lens-like edge.
                float t = Math.Max(0, Math.Min(1, dist[i] / depth));
                z[i] = (float)Math.Sqrt(1 - (1 - t) * (1 - t));
            }

            // The backdrop, frosted.
            int cw = Math.Max(1, area.Width), ch = Math.Max(1, area.Height);
            float[] r = new float[cw * ch], gr = new float[cw * ch], bl = new float[cw * ch];
            for (int i = 0; i < r.Length && i < backdrop.Length; i++)
            {
                int p = backdrop[i];
                r[i] = (p >> 16) & 0xFF; gr[i] = (p >> 8) & 0xFF; bl[i] = p & 0xFF;
            }
            int frost = Math.Max(0, (int)Math.Round(s.Frost / 2));
            if (frost > 0) { BlurEdges(r, cw, ch, frost, 3); BlurEdges(gr, cw, ch, frost, 3); BlurEdges(bl, cw, ch, frost, 3); }
            float ox = at.X - area.X, oy = at.Y - area.Y;

            double light = s.Light * Math.PI / 180;
            float lx = (float)Math.Cos(light), ly = (float)Math.Sin(light);
            float tintA = s.Tint.A / 255f * 0.38f;
            float shine = Math.Max(0, Math.Min(1, s.Shine)), refraction = Math.Max(0, Math.Min(1, s.Refraction)), bend = refraction * depth;
            float dispersion = Math.Max(0, Math.Min(1, s.Dispersion)), split = dispersion * 0.22f;
            var output = new int[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    float a = mask[i];
                    if (a <= 0.002f) continue;
                    float zc = z[i], d = dist[i], rim = 1 - zc;
                    // The outward normal points down the distance field.
                    float gx = (dist[y * w + Math.Min(w - 1, x + 1)] - dist[y * w + Math.Max(0, x - 1)]) / 2;
                    float gy = (dist[Math.Min(h - 1, y + 1) * w + x] - dist[Math.Max(0, y - 1) * w + x]) / 2;
                    float len = (float)Math.Sqrt(gx * gx + gy * gy);
                    float nx = 0, ny = 0;
                    if (len > 1e-5f) { nx = -gx / len; ny = -gy / len; }

                    // The rim bends the backdrop: each pixel looks further in, most at the very edge, with red and
                    // blue bent a little more and less than green.
                    float off = bend * rim;
                    float bx = x + ox, by = y + oy;
                    float cr = Sample(r, cw, ch, bx - nx * off * (1 + split), by - ny * off * (1 + split));
                    float cg = Sample(gr, cw, ch, bx - nx * off, by - ny * off);
                    float cb = Sample(bl, cw, ch, bx - nx * off * (1 - split), by - ny * off * (1 - split));
                    // Toward the outer edge it also reflects what lies just outside, so light and color from
                    // beside the pane wrap round its rim.
                    float reflect = 0.45f * refraction * rim * rim;
                    if (reflect > 0.01f)
                    {
                        float reach = 3 + depth * 0.6f;
                        float qx = bx + nx * reach, qy = by + ny * reach;
                        cr += (Sample(r, cw, ch, qx, qy) - cr) * reflect;
                        cg += (Sample(gr, cw, ch, qx, qy) - cg) * reflect;
                        cb += (Sample(bl, cw, ch, qx, qy) - cb) * reflect;
                    }

                    // Seen through glass, colors are a little richer (Apple's vibrancy).
                    float grey = cr * 0.299f + cg * 0.587f + cb * 0.114f;
                    cr = grey + (cr - grey) * 1.35f; cg = grey + (cg - grey) * 1.35f; cb = grey + (cb - grey) * 1.35f;
                    // The glass's own tint.
                    cr += (s.Tint.R - cr) * tintA; cg += (s.Tint.G - cg) * tintA; cb += (s.Tint.B - cb) * tintA;
                    // The thickness of the glass darkens a little just inside the rim.
                    float shade = 1 - 0.2f * zc * rim * 4 * rim;
                    cr *= shade; cg *= shade; cb *= shade;
                    // A soft wash of light across the pane from the side the light comes from.
                    float toward = Math.Max(0, Math.Min(1, 0.5f + (x / (float)w - 0.5f) * lx + (y / (float)h - 0.5f) * ly));
                    float wash = 0.015f + 0.075f * toward * toward + shine * 0.08f * rim * (float)Math.Sqrt(rim);
                    cr += (255 - cr) * wash; cg += (255 - cg) * wash; cb += (255 - cb) * wash;

                    // Light on the rim: a crisp highlight where it faces the light and a softer one on the far side,
                    // gathered at the outer edge, and a clean hairline of light all the way round. The light splits
                    // a little as it leaves the rim, so it is tinted with the colors of a prism round the pane.
                    float facing = nx * lx + ny * ly;
                    float near = Math.Max(0, facing), far = Math.Max(0, -facing);
                    float spec = shine * rim * (float)Math.Sqrt(rim) * (2.0f * near * near * near + 0.7f * far * far * far);
                    float hairline = shine * 0.45f * Math.Max(0, Math.Min(1, 1.6f - d)) * (0.55f + 0.45f * near);
                    float glint = Math.Min(1, spec + hairline);
                    if (glint > 0.002f)
                    {
                        float hr = 255, hg = 255, hb = 255;
                        if (dispersion > 0)
                        {
                            Prism((float)Math.Atan2(ny, nx), out float pr, out float pg, out float pb);
                            float k = 0.5f * dispersion;
                            hr += (pr - hr) * k; hg += (pg - hg) * k; hb += (pb - hb) * k;
                        }
                        cr += (hr - cr) * glint; cg += (hg - cg) * glint; cb += (hb - cb) * glint;
                    }

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

        /// <summary>
        /// How far each pixel lies inside the shape, in pixels: the exact Euclidean distance to the nearest
        /// pixel outside it (Felzenszwalb and Huttenlocher), less half a pixel, with the coverage of the
        /// pixels the edge runs through standing in for their part-pixel distance.
        /// </summary>
        private static float[] InsideDistance(float[] mask, int w, int h)
        {
            const float Far = 1e12f;
            var squared = new float[w * h];
            for (int i = 0; i < squared.Length; i++) squared[i] = mask[i] >= 0.5f ? Far : 0;
            int n = Math.Max(w, h);
            var f = new float[n];
            var result = new float[n];
            var v = new int[n];
            var zs = new float[n + 1];
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++) f[y] = squared[y * w + x];
                Squared1D(f, h, result, v, zs);
                for (int y = 0; y < h; y++) squared[y * w + x] = result[y];
            }
            for (int y = 0; y < h; y++)
            {
                Array.Copy(squared, y * w, f, 0, w);
                Squared1D(f, w, result, v, zs);
                Array.Copy(result, 0, squared, y * w, w);
            }
            var dist = new float[w * h];
            for (int i = 0; i < dist.Length; i++)
            {
                float a = mask[i];
                dist[i] = a <= 0 ? 0 : a < 1 && squared[i] <= 1.5f ? a * 0.5f : Math.Max(0.5f, (float)Math.Sqrt(squared[i]) - 0.5f);
            }
            return dist;
        }

        /// <summary>The one-dimensional squared distance transform: the lower envelope of parabolas.</summary>
        private static void Squared1D(float[] f, int n, float[] d, int[] v, float[] z)
        {
            int k = 0;
            v[0] = 0;
            z[0] = float.NegativeInfinity;
            z[1] = float.PositiveInfinity;
            for (int q = 1; q < n; q++)
            {
                float s = ((f[q] + q * q) - (f[v[k]] + v[k] * v[k])) / (2f * q - 2f * v[k]);
                while (s <= z[k])
                {
                    k--;
                    s = ((f[q] + q * q) - (f[v[k]] + v[k] * v[k])) / (2f * q - 2f * v[k]);
                }
                k++;
                v[k] = q;
                z[k] = s;
                z[k + 1] = float.PositiveInfinity;
            }
            k = 0;
            for (int q = 0; q < n; q++)
            {
                while (z[k + 1] < q) k++;
                int p = v[k];
                d[q] = (q - p) * (q - p) + f[p];
            }
        }

        /// <summary>A spectrum color for a direction round the pane, as light split by a prism.</summary>
        private static void Prism(float angle, out float r, out float g, out float b)
        {
            // Hue runs round the rim; blue and violet sit at the top, warm colors lower down, as on Apple's glass.
            double hue = ((angle * 180 / Math.PI) + 360 + 330) % 360 / 60;
            double x = 1 - Math.Abs(hue % 2 - 1);
            double rr, gg, bb;
            if (hue < 1) { rr = 1; gg = x; bb = 0; }
            else if (hue < 2) { rr = x; gg = 1; bb = 0; }
            else if (hue < 3) { rr = 0; gg = 1; bb = x; }
            else if (hue < 4) { rr = 0; gg = x; bb = 1; }
            else if (hue < 5) { rr = x; gg = 0; bb = 1; }
            else { rr = 1; gg = 0; bb = x; }
            // Pastel, as light is: mixed halfway to white.
            r = (float)(255 * (0.5 + rr * 0.5)); g = (float)(255 * (0.5 + gg * 0.5)); b = (float)(255 * (0.5 + bb * 0.5));
        }

        /// <summary>Repeated box blurs where the edge pixels carry on outward (for a backdrop cut from the screen).</summary>
        private static void BlurEdges(float[] values, int w, int h, int radius, int passes)
        {
            var temp = new float[values.Length];
            float scale = 1f / (radius * 2 + 1);
            for (int pass = 0; pass < passes; pass++)
            {
                for (int y = 0; y < h; y++)
                {
                    int row = y * w;
                    float sum = 0;
                    for (int x = -radius; x <= radius; x++) sum += values[row + Math.Max(0, Math.Min(w - 1, x))];
                    for (int x = 0; x < w; x++)
                    {
                        temp[row + x] = sum * scale;
                        sum += values[row + Math.Min(w - 1, x + radius + 1)] - values[row + Math.Max(0, x - radius)];
                    }
                }
                for (int x = 0; x < w; x++)
                {
                    float sum = 0;
                    for (int y = -radius; y <= radius; y++) sum += temp[Math.Max(0, Math.Min(h - 1, y)) * w + x];
                    for (int y = 0; y < h; y++)
                    {
                        values[y * w + x] = sum * scale;
                        sum += temp[Math.Min(h - 1, y + radius + 1) * w + x] - temp[Math.Max(0, y - radius) * w + x];
                    }
                }
            }
        }

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
