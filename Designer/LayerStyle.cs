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
    /// draws: recolor (a color, gradient or texture over the widget's own pixels), an outline, a
    /// drop shadow and an outer glow. Because they work on the drawn result, every widget gets them
    /// the same way, including ones added later. The work is done on the pixels directly (read once,
    /// shared by every effect), which is many times faster than GDI+ color matrices.
    /// </summary>
    internal sealed class LayerStyle
    {
        public static readonly string[] FillModes = { "None", "Color", "Gradient", "Pattern", "Image" };
        public static readonly string[] FadeModes = { "None", "Left", "Right", "Top", "Bottom", "Sides", "Top and bottom", "Edges", "Radial" };
        public static readonly string[] BlendModes = { "Normal", "Multiply", "Screen", "Add", "Overlay" };

        public string Fill = "None";
        public Color FillColor, FillColor2;
        public float FillAngle, FillAmount;
        public Bitmap Texture;
        public string Pattern = "Stripes";
        public Color PatternBack;
        public float PatternScale = 100, PatternAngle = 45;
        public float Outline, Shadow, ShadowX, ShadowY, Glow;
        public Color OutlineColor, ShadowColor, GlowColor;

        /// <summary>A fade toward an edge (a gradient mask), and how far into the widget it reaches (0 to 1).</summary>
        public string FadeEdge = "None";
        public float FadeLength = 0.35f;
        /// <summary>How the widget mixes with what's beneath it.</summary>
        public string BlendMode = "Normal";

        /// <summary>Anything that makes the widget a finished picture laid on its own (effects, a fade or a blend).</summary>
        public bool Any => NeedsCompose || BlendMode != "Normal";

        /// <summary>Effects worked out on the widget's pixels.</summary>
        public bool NeedsCompose => Recolors || Outline > 0 || Shadow > 0 || Glow > 0 || FadeEdge != "None";
        private bool Recolors => FillAmount > 0 && (Fill == "Color" || Fill == "Gradient" || Fill == "Pattern" || (Fill == "Image" && Texture != null));

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
            Pattern = c.Choice("fxPattern"),
            PatternBack = c.OptionalColor("fxPatternBack") ?? Color.Transparent,
            PatternScale = (float)c.Num("fxPatternScale"),
            PatternAngle = (float)c.Num("fxPatternAngle"),
            Outline = (float)Math.Max(0, Math.Min(20, c.Num("fxOutline"))),
            OutlineColor = c.Color("fxOutlineColor"),
            Shadow = (float)Math.Max(0, Math.Min(40, c.Num("fxShadow"))),
            ShadowColor = c.Color("fxShadowColor"),
            ShadowX = (float)Math.Max(-40, Math.Min(40, c.Num("fxShadowX"))),
            ShadowY = (float)Math.Max(-40, Math.Min(40, c.Num("fxShadowY"))),
            Glow = (float)Math.Max(0, Math.Min(40, c.Num("fxGlow"))),
            GlowColor = c.Color("fxGlowColor"),
            FadeEdge = c.Choice("fxFade"),
            FadeLength = (float)Math.Max(0.02, Math.Min(1, c.Num("fxFadeLength") / 100)),
            BlendMode = c.Choice("fxBlend")
        };

        /// <summary>
        /// The finished picture: the drawn layer (recolored in place) over its outline, glow and shadow. The
        /// effects are worked out on the pixels themselves and laid together in one pass, premultiplied like the
        /// layer, instead of each being drawn through GDI+ as a picture of its own: several times less work for a
        /// widget that changes every frame. <paramref name="place"/> maps the widget's box into the layer.
        /// </summary>
        public unsafe Bitmap Compose(Bitmap layer, RectangleF box, Matrix place)
        {
            int w = layer.Width, h = layer.Height;
            if (Recolors)
            {
                int[] pixels = Read(layer);
                RecolorPixels(pixels, w, h, box, place);
                Write(layer, pixels);
            }
            var whole = new Rectangle(0, 0, w, h);
            var composite = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            BitmapData from = layer.LockBits(whole, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            BitmapData to = composite.LockBits(whole, ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try
            {
                uint[] under = null;
                if (Shadow > 0 || Glow > 0 || Outline > 0)
                {
                    // The silhouette every effect is made from.
                    var alpha = new byte[w * h];
                    for (int y = 0; y < h; y++)
                    {
                        uint* row = (uint*)((byte*)from.Scan0 + (long)y * from.Stride);
                        for (int x = 0; x < w; x++) alpha[y * w + x] = (byte)(row[x] >> 24);
                    }
                    // Built up from the bottom: the shadow, the glow over it, the outline over both.
                    under = new uint[w * h];
                    if (Shadow > 0) Blurred(under, alpha, w, h, ShadowColor, Shadow, ShadowX, ShadowY);
                    if (Glow > 0) Blurred(under, alpha, w, h, GlowColor, Glow, 0, 0);
                    if (Outline > 0) Stroke(under, alpha, w, h);
                }
                for (int y = 0; y < h; y++)
                {
                    uint* top = (uint*)((byte*)from.Scan0 + (long)y * from.Stride), result = (uint*)((byte*)to.Scan0 + (long)y * to.Stride);
                    for (int x = 0; x < w; x++) result[x] = under == null ? top[x] : Over(top[x], under[y * w + x]);
                }
                if (FadeEdge != "None") FadeOut(to, w, h, box, place);
            }
            finally
            {
                layer.UnlockBits(from);
                composite.UnlockBits(to);
            }
            return composite;
        }

        /// <summary>
        /// The fade: every pixel of the finished picture (effects too) scaled by how far it is from the faded edge, in
        /// the widget's own frame, so a turned widget fades along its own sides. Smoothed, so it starts and ends softly.
        /// </summary>
        private unsafe void FadeOut(BitmapData to, int w, int h, RectangleF box, Matrix place)
        {
            float[] e;
            using (Matrix back = place.Clone())
            {
                if (!back.IsInvertible) return;
                back.Invert();
                e = back.Elements;
            }
            float bw = Math.Max(1, box.Width), bh = Math.Max(1, box.Height), length = FadeLength;
            float Ramp(float d)
            {
                float t = Math.Max(0, Math.Min(1, d / length));
                return t * t * (3 - 2 * t);
            }
            for (int y = 0; y < h; y++)
            {
                uint* row = (uint*)((byte*)to.Scan0 + (long)y * to.Stride);
                for (int x = 0; x < w; x++)
                {
                    uint p = row[x];
                    if (p == 0) continue;
                    // This pixel's place across and down the widget's box, 0 to 1.
                    float px = x + 0.5f, py = y + 0.5f;
                    float u = (px * e[0] + py * e[2] + e[4] - box.X) / bw, v = (px * e[1] + py * e[3] + e[5] - box.Y) / bh;
                    float k;
                    switch (FadeEdge)
                    {
                        case "Left": k = Ramp(u); break;
                        case "Right": k = Ramp(1 - u); break;
                        case "Top": k = Ramp(v); break;
                        case "Bottom": k = Ramp(1 - v); break;
                        case "Sides": k = Ramp(Math.Min(u, 1 - u)); break;
                        case "Top and bottom": k = Ramp(Math.Min(v, 1 - v)); break;
                        case "Radial":
                        {
                            float dx = (u - 0.5f) * 2, dy = (v - 0.5f) * 2;
                            k = Ramp(1 - (float)Math.Sqrt(dx * dx + dy * dy));
                            break;
                        }
                        default: k = Ramp(Math.Min(Math.Min(u, 1 - u), Math.Min(v, 1 - v))); break;
                    }
                    if (k >= 0.999f) continue;
                    uint cover = (uint)(k * 255 + 0.5f);
                    row[x] = cover == 0 ? 0 : ScaleAll(p, cover);
                }
            }
        }

        private static uint ScaleAll(uint pixel, uint cover) =>
            Scale(pixel >> 24, cover) << 24 | Scale((pixel >> 16) & 255, cover) << 16 | Scale((pixel >> 8) & 255, cover) << 8 | Scale(pixel & 255, cover);

        /// <summary>A premultiplied pixel over another.</summary>
        private static uint Over(uint top, uint bottom)
        {
            uint a = top >> 24;
            if (a == 255 || bottom == 0) return top;
            if (a == 0) return bottom;
            uint keep = 255 - a;
            return (a + Scale(bottom >> 24, keep)) << 24
                | (((top >> 16) & 255) + Scale((bottom >> 16) & 255, keep)) << 16
                | (((top >> 8) & 255) + Scale((bottom >> 8) & 255, keep)) << 8
                | ((top & 255) + Scale(bottom & 255, keep));
        }

        /// <summary><paramref name="value"/> × <paramref name="by"/> / 255, rounded: exact, without a division.</summary>
        private static uint Scale(uint value, uint by)
        {
            uint t = value * by + 128;
            return (t + (t >> 8)) >> 8;
        }

        /// <summary>A color at every opacity from 0 to 255, premultiplied.</summary>
        private static uint[] Shades(Color color)
        {
            var shades = new uint[256];
            for (uint a = 1; a < 256; a++)
                shades[a] = a << 24 | Scale(color.R, a) << 16 | Scale(color.G, a) << 8 | Scale(color.B, a);
            return shades;
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
                        case "Pattern":
                            // Laid from the widget's corner, so it moves and turns with the widget.
                            using (TextureBrush brush = Fills.PatternBrush(Pattern, FillColor, PatternBack, PatternScale, PatternAngle, box.Location)) og.FillRectangle(brush, area);
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
        /// The silhouette, softened, laid over <paramref name="under"/>: averaged down to a small grid (block size
        /// from the softness), smoothed once more, then stretched back up with bilinear sampling, which finishes
        /// the blur. Offset by <paramref name="dx"/>, <paramref name="dy"/>.
        /// </summary>
        private static unsafe void Blurred(uint[] under, byte[] alpha, int w, int h, Color color, float size, float dx, float dy)
        {
            int k = Math.Max(1, (int)Math.Round(size / 1.6f));
            int sw = (w + k - 1) / k + 2, sh = (h + k - 1) / k + 2;
            // Block averages, with a one-cell border of nothing so the blur can spread outward.
            var small = new float[sw * sh];
            var cellOf = new int[w];
            for (int x = 0; x < w; x++) cellOf[x] = x / k + 1;
            fixed (float* sums = small)
            fixed (byte* source = alpha)
            fixed (int* cells = cellOf)
                for (int y = 0; y < h; y++)
                {
                    float* row = sums + (y / k + 1) * sw;
                    byte* from = source + y * w;
                    for (int x = 0; x < w; x++) row[cells[x]] += from[x];
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
                    soft[y * sw + x] = Math.Min(255, sum / (9 * block) * color.A / 255f);
                }
            // The grid again with enough empty cells round it that the offset can never sample past its edge.
            int padX = (int)Math.Ceiling(Math.Abs(dx) / k) + 2, padY = (int)Math.Ceiling(Math.Abs(dy) / k) + 2;
            int pw = sw + padX * 2;
            var grid = new float[pw * (sh + padY * 2)];
            for (int y = 0; y < sh; y++) Array.Copy(soft, y * sw, grid, (y + padY) * pw + padX, sw);
            // Each small cell covers k by k layer pixels, and the border cell sits just outside the layer: a pixel's
            // centre falls at (x + 0.5 - origin) / k - 0.5 in cells.
            float originX = dx - k, originY = dy - k;
            var columns = new int[w];
            var across = new float[w];
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f - originX) / k - 0.5f;
                int column = (int)Math.Floor(u);
                columns[x] = column + padX;
                across[x] = u - column;
            }
            uint[] shades = Shades(color);
            fixed (float* cells = grid, weights = across)
            fixed (int* starts = columns)
            fixed (uint* tints = shades, result = under)
                for (int y = 0; y < h; y++)
                {
                    float v = (y + 0.5f - originY) / k - 0.5f;
                    int row = (int)Math.Floor(v);
                    float down = v - row;
                    float* upper = cells + (row + padY) * pw, lower = upper + pw;
                    uint* line = result + y * w;
                    for (int x = 0; x < w; x++)
                    {
                        int c = starts[x];
                        float a00 = upper[c], a01 = upper[c + 1], a10 = lower[c], a11 = lower[c + 1];
                        if (a00 + a01 + a10 + a11 <= 0) continue;
                        float t = weights[x], top = a00 + (a01 - a00) * t, bottom = a10 + (a11 - a10) * t;
                        int a = (int)(top + (bottom - top) * down + 0.5f);
                        if (a <= 0) continue;
                        uint shade = tints[a > 255 ? 255 : a];
                        line[x] = line[x] == 0 ? shade : Over(shade, line[x]);
                    }
                }
        }

        /// <summary>An outline: the silhouette grown by the outline width in every direction (a round brush), in the outline color, over <paramref name="under"/>.</summary>
        private void Stroke(uint[] under, byte[] alpha, int w, int h)
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
            uint[] shades = Shades(OutlineColor);
            uint strength = OutlineColor.A;
            for (int i = 0; i < grown.Length; i++)
            {
                uint a = Scale(grown[i], strength);
                if (a > 0) under[i] = under[i] == 0 ? shades[a] : Over(shades[a], under[i]);
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
