using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace AlmostFastOverlays
{
    /// <summary>
    /// The shared Style tools, applied like layer styles in an image editor to whatever a widget
    /// draws: recolor (a colour, gradient or texture over the widget's own pixels), an outline, a
    /// drop shadow and an outer glow. Because they work on the drawn result, every widget gets them
    /// the same way, including ones added later. The work is done on the pixels directly (read once,
    /// shared by every effect), which is many times faster than GDI+ colour matrices.
    /// </summary>
    internal sealed class LayerStyle
    {
        public static readonly string[] FillModes = { "None", "Color", "Gradient", "Image" };

        public string Fill = "None";
        public Color FillColor, FillColor2;
        public float FillAngle, FillAmount;
        public Bitmap Texture;
        public float Outline, Shadow, ShadowX, ShadowY, Glow;
        public Color OutlineColor, ShadowColor, GlowColor;

        public bool Any => Recolors || Outline > 0 || Shadow > 0 || Glow > 0;
        private bool Recolors => FillAmount > 0 && (Fill == "Color" || Fill == "Gradient" || (Fill == "Image" && Texture != null));

        /// <summary>How far the effects reach past what the widget itself draws.</summary>
        public float Extent => Math.Max(Math.Max(Glow * 2, Shadow * 2 + Math.Max(Math.Abs(ShadowX), Math.Abs(ShadowY))), Outline) + 2;

        public static LayerStyle From(WidgetContext c) => new LayerStyle
        {
            Fill = c.Choice("fxFill"),
            FillColor = c.Color("fxFillColor"),
            FillColor2 = c.Color("fxFillColor2"),
            FillAngle = (float)c.Num("fxFillAngle"),
            FillAmount = (float)Math.Max(0, Math.Min(1, c.Num("fxFillAmount") / 100)),
            Texture = c.Choice("fxFill") == "Image" ? c.Renderer.Image(c.Text("fxImage")) : null,
            Outline = (float)Math.Max(0, Math.Min(20, c.Num("fxOutline"))),
            OutlineColor = c.Color("fxOutlineColor"),
            Shadow = (float)Math.Max(0, Math.Min(40, c.Num("fxShadow"))),
            ShadowColor = c.Color("fxShadowColor"),
            ShadowX = (float)Math.Max(-40, Math.Min(40, c.Num("fxShadowX"))),
            ShadowY = (float)Math.Max(-40, Math.Min(40, c.Num("fxShadowY"))),
            Glow = (float)Math.Max(0, Math.Min(40, c.Num("fxGlow"))),
            GlowColor = c.Color("fxGlowColor")
        };

        /// <summary>
        /// Recolors the drawn layer in place and paints the shadow, glow and outline beneath where the
        /// layer goes (<paramref name="at"/>). <paramref name="place"/> maps the widget's box into the layer.
        /// </summary>
        public void Apply(Graphics g, Bitmap layer, RectangleF box, Matrix place, Rectangle at, float opacity)
        {
            int w = layer.Width, h = layer.Height;
            int[] pixels = Read(layer);
            if (Recolors)
            {
                RecolorPixels(pixels, w, h, box, place);
                Write(layer, pixels);
            }
            if (Shadow <= 0 && Glow <= 0 && Outline <= 0) return;
            var alpha = new byte[pixels.Length];
            for (int i = 0; i < pixels.Length; i++) alpha[i] = (byte)((uint)pixels[i] >> 24);
            if (Shadow > 0) Blurred(g, alpha, w, h, at, ShadowColor, Shadow, ShadowX, ShadowY, opacity);
            if (Glow > 0) Blurred(g, alpha, w, h, at, GlowColor, Glow, 0, 0, opacity);
            if (Outline > 0) Stroke(g, alpha, w, h, at, opacity);
        }

        private void RecolorPixels(int[] pixels, int w, int h, RectangleF box, Matrix place)
        {
            using (var overlay = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (Graphics og = Graphics.FromImage(overlay))
                {
                    og.SmoothingMode = SmoothingMode.AntiAlias;
                    og.Transform = place;
                    RectangleF area = RectangleF.Inflate(box, 1, 1);
                    switch (Fill)
                    {
                        case "Gradient":
                            using (var brush = new LinearGradientBrush(area, FillColor, FillColor2, FillAngle)) og.FillRectangle(brush, area);
                            break;
                        case "Image":
                            // Cover the box, cropping the texture rather than stretching it.
                            og.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            float scale = Math.Max(area.Width / Texture.Width, area.Height / Texture.Height);
                            float tw = Texture.Width * scale, th = Texture.Height * scale;
                            og.SetClip(area);
                            og.DrawImage(Texture, area.X + (area.Width - tw) / 2, area.Y + (area.Height - th) / 2, tw, th);
                            break;
                        default:
                            using (var brush = new SolidBrush(FillColor)) og.FillRectangle(brush, area);
                            break;
                    }
                }
                int[] paint = Read(overlay);
                for (int i = 0; i < pixels.Length; i++)
                {
                    int p = pixels[i];
                    uint a = (uint)p >> 24;
                    if (a == 0) continue;
                    int q = paint[i];
                    float mix = FillAmount * ((uint)q >> 24) / 255f;
                    if (mix <= 0) continue;
                    int r = Blend((p >> 16) & 255, (q >> 16) & 255, mix), gr = Blend((p >> 8) & 255, (q >> 8) & 255, mix), b = Blend(p & 255, q & 255, mix);
                    pixels[i] = (int)(a << 24) | (r << 16) | (gr << 8) | b;
                }
            }
        }

        private static int Blend(int from, int to, float t) => (int)(from + (to - from) * t + 0.5f);

        /// <summary>
        /// The silhouette, softened: averaged down to a small grid (block size from the softness),
        /// smoothed once more, then drawn scaled back up so the bilinear stretch finishes the blur.
        /// </summary>
        private static void Blurred(Graphics g, byte[] alpha, int w, int h, Rectangle at, Color color, float size, float dx, float dy, float opacity)
        {
            int k = Math.Max(1, (int)Math.Round(size / 1.6f));
            int sw = (w + k - 1) / k + 2, sh = (h + k - 1) / k + 2;
            // Block averages, with a one-cell border of nothing so the blur can spread outward.
            var small = new float[sw * sh];
            for (int y = 0; y < h; y++)
            {
                int row = (y / k + 1) * sw, src = y * w;
                for (int x = 0; x < w; x++) small[row + x / k + 1] += alpha[src + x];
            }
            float block = k * k;
            // One 3x3 box pass on the small grid evens out the blocks.
            var soft = new float[sw * sh];
            for (int y = 0; y < sh; y++)
                for (int x = 0; x < sw; x++)
                {
                    float sum = 0;
                    for (int oy = -1; oy <= 1; oy++)
                    {
                        int yy = y + oy;
                        if (yy < 0 || yy >= sh) continue;
                        for (int ox = -1; ox <= 1; ox++)
                        {
                            int xx = x + ox;
                            if (xx >= 0 && xx < sw) sum += small[yy * sw + xx];
                        }
                    }
                    soft[y * sw + x] = sum / (9 * block);
                }
            float strength = color.A / 255f * opacity;
            int rgb = color.R << 16 | color.G << 8 | color.B;
            var tinted = new int[sw * sh];
            for (int i = 0; i < tinted.Length; i++)
            {
                int a = (int)Math.Min(255, soft[i] * strength + 0.5f);
                tinted[i] = a << 24 | rgb;
            }
            using (var bitmap = new Bitmap(sw, sh, PixelFormat.Format32bppArgb))
            {
                Write(bitmap, tinted);
                GraphicsState state = g.Save();
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                // Each small cell covers k by k layer pixels, and the border cell sits just outside the layer.
                g.DrawImage(bitmap, new RectangleF(at.X - k + dx, at.Y - k + dy, sw * k, sh * k), new RectangleF(0, 0, sw, sh), GraphicsUnit.Pixel);
                g.Restore(state);
            }
        }

        /// <summary>An outline: the silhouette grown by the outline width in every direction (a round brush), in the outline colour.</summary>
        private void Stroke(Graphics g, byte[] alpha, int w, int h, Rectangle at, float opacity)
        {
            float radius = Outline;
            int reach = (int)Math.Ceiling(radius);
            // For each row offset, how far the round brush reaches sideways; rows of a given width are maxed once and shared.
            var spans = new int[reach * 2 + 1];
            for (int dy = -reach; dy <= reach; dy++)
                spans[dy + reach] = (int)Math.Floor(Math.Sqrt(Math.Max(0, radius * radius - dy * dy)) + 0.001);
            var sideways = new Dictionary<int, byte[]>();
            foreach (int half in spans)
                if (!sideways.ContainsKey(half)) sideways[half] = RowMax(alpha, w, h, half);
            var grown = new byte[alpha.Length];
            for (int dy = -reach; dy <= reach; dy++)
            {
                byte[] rows = sideways[spans[dy + reach]];
                int from = Math.Max(0, -dy), to = Math.Min(h, h - dy);
                for (int y = from; y < to; y++)
                {
                    int target = y * w, source = (y + dy) * w;
                    for (int x = 0; x < w; x++)
                        if (rows[source + x] > grown[target + x]) grown[target + x] = rows[source + x];
                }
            }
            float strength = OutlineColor.A / 255f * opacity;
            int rgb = OutlineColor.R << 16 | OutlineColor.G << 8 | OutlineColor.B;
            var tinted = new int[grown.Length];
            for (int i = 0; i < grown.Length; i++)
                if (grown[i] > 0) tinted[i] = (int)(grown[i] * strength + 0.5f) << 24 | rgb;
            using (var bitmap = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                Write(bitmap, tinted);
                g.DrawImageUnscaled(bitmap, at.X, at.Y);
            }
        }

        /// <summary>Each pixel becomes the largest value within <paramref name="half"/> pixels to its left or right (van Herk / Gil-Werman, linear time).</summary>
        private static byte[] RowMax(byte[] source, int w, int h, int half)
        {
            if (half <= 0) return source;
            int size = half * 2 + 1;
            var result = new byte[source.Length];
            var forward = new byte[w + size];
            var backward = new byte[w + size];
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                // Padded row: half zeros, the row, half zeros; block maxima forward and backward.
                int n = w + 2 * half;
                for (int i = 0; i < n; i++)
                {
                    int x = i - half;
                    byte v = x >= 0 && x < w ? source[row + x] : (byte)0;
                    forward[i] = i % size == 0 ? v : Math.Max(forward[i - 1], v);
                }
                for (int i = n - 1; i >= 0; i--)
                {
                    int x = i - half;
                    byte v = x >= 0 && x < w ? source[row + x] : (byte)0;
                    backward[i] = i == n - 1 || (i + 1) % size == 0 ? v : Math.Max(backward[i + 1], v);
                }
                for (int x = 0; x < w; x++)
                {
                    // The window covers padded positions x .. x + size - 1.
                    result[row + x] = Math.Max(backward[x], forward[x + size - 1]);
                }
            }
            return result;
        }

        internal static ImageAttributes Fade(float alpha)
        {
            var attributes = new ImageAttributes();
            attributes.SetColorMatrix(new ColorMatrix { Matrix33 = Math.Max(0, Math.Min(1, alpha)) });
            return attributes;
        }

        private static int[] Read(Bitmap bitmap)
        {
            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var pixels = new int[bitmap.Width * bitmap.Height];
                for (int y = 0; y < bitmap.Height; y++) Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * bitmap.Width, bitmap.Width);
                return pixels;
            }
            finally { bitmap.UnlockBits(data); }
        }

        private static void Write(Bitmap bitmap, int[] pixels)
        {
            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < bitmap.Height; y++) Marshal.Copy(pixels, y * bitmap.Width, data.Scan0 + y * data.Stride, bitmap.Width);
            }
            finally { bitmap.UnlockBits(data); }
        }
    }
}
