using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace AlmostFastDash.Rendering
{
    /// <summary>
    /// Rowdy's easter egg, a tribute to Kyle Busch: tap the speed eight times and a black flag with a slanted 8 and
    /// "FOREVER ROWDY" waves over the screen for eight seconds. The widget that starts it carries "tribute" = "8"; the
    /// renderer counts the taps (<see cref="LayoutRenderer.Tap"/>). The 8 is this app's own drawing of a squared racing
    /// number, not anyone's logo.
    /// </summary>
    internal static class TributeEight
    {
        /// <summary>The value of a widget's "tribute" setting that makes it the place to tap.</summary>
        public const string Mark = "8";
        /// <summary>Taps in a row (each within <see cref="TapGap"/> of the last) that start it, and how long it waves.</summary>
        public const int Taps = 8, TapGap = 1500, Milliseconds = 8000;

        private static readonly Dictionary<Size, Bitmap> Pictures = new Dictionary<Size, Bitmap>();

        /// <summary>The flat flag at a screen's size, drawn once and kept.</summary>
        public static Bitmap Picture(int width, int height)
        {
            var size = new Size(width, height);
            lock (Pictures)
            {
                if (Pictures.TryGetValue(size, out Bitmap kept)) return kept;
                Bitmap picture = Draw(width, height);
                Pictures[size] = picture;
                return picture;
            }
        }

        private static Bitmap Draw(int width, int height)
        {
            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.FromArgb(10, 10, 12));

                // The 8 is drawn in a 200 × 300 box, then scaled to the screen, slanted and centered above the words.
                float scale = Math.Min(width / 854f, height / 480f) * 0.95f;
                const float W = 200, H = 300, Slant = 0.2f;
                float left = width / 2f - (W + Slant * H) * scale / 2, top = height * 0.05f;
                using (GraphicsPath eight = Eight(W, H))
                using (var place = new Matrix(scale, 0, -Slant * scale, scale, left + Slant * H * scale, top))
                {
                    eight.Transform(place);
                    // A grey edge below and to the right gives the number depth, the way painted numbers have it.
                    using (GraphicsPath edge = (GraphicsPath)eight.Clone())
                    using (var nudge = new Matrix(1, 0, 0, 1, 7 * scale, 6 * scale))
                    using (var grey = new SolidBrush(Color.FromArgb(92, 92, 96)))
                    {
                        edge.Transform(nudge);
                        g.FillPath(grey, edge);
                    }
                    using (var black = new SolidBrush(Color.FromArgb(6, 6, 8)))
                        g.FillPath(black, eight);
                    using (var white = new Pen(Color.White, 9 * scale) { LineJoin = LineJoin.Miter })
                        g.DrawPath(white, eight);
                }

                // The words underneath, slanted with the number.
                float size = 40 * Math.Min(width / 854f, height / 480f);
                using (var font = new Font("Bahnschrift", size, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var white = new SolidBrush(Color.White))
                using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    string words = "F O R E V E R   R O W D Y";
                    float y = top + H * scale + (height - top - H * scale) / 2f;
                    g.TranslateTransform(width / 2f, y);
                    g.MultiplyTransform(new Matrix(1, 0, -Slant, 1, 0, 0));
                    g.DrawString(words, font, white, 0, 0, format);
                    g.ResetTransform();
                }
            }
            return bitmap;
        }

        /// <summary>
        /// A squared racing 8 in a box w × h: chamfered corners, a notch either side at the waist, and two counters (the
        /// holes, the lower one wider). The holes are separate figures, so filling leaves them open.
        /// </summary>
        private static GraphicsPath Eight(float w, float h)
        {
            const float C = 26, Notch = 24, Waist = 22;
            float mid = h / 2;
            var path = new GraphicsPath(FillMode.Alternate);
            path.AddPolygon(new[]
            {
                new PointF(C, 0), new PointF(w - C, 0), new PointF(w, C), new PointF(w, mid - Waist), new PointF(w - Notch, mid),
                new PointF(w, mid + Waist), new PointF(w, h - C), new PointF(w - C, h), new PointF(C, h), new PointF(0, h - C),
                new PointF(0, mid + Waist), new PointF(Notch, mid), new PointF(0, mid - Waist), new PointF(0, C)
            });
            path.AddPolygon(Counter(80, 54, 124, 124, 8));
            path.AddPolygon(Counter(72, 178, 130, 248, 8));
            return path;
        }

        private static PointF[] Counter(float x0, float y0, float x1, float y1, float c) => new[]
        {
            new PointF(x0 + c, y0), new PointF(x1 - c, y0), new PointF(x1, y0 + c), new PointF(x1, y1 - c),
            new PointF(x1 - c, y1), new PointF(x0 + c, y1), new PointF(x0, y1 - c), new PointF(x0, y0 + c)
        };
    }
}
