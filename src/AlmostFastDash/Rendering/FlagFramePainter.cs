using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace AlmostFastDash.Rendering
{
    /// <summary>
    /// The frame round the dashboard while a flag is out: a soft glow in the flag's color along the screen's
    /// edge, breathing slowly, with a tab at the top saying what it is. Glow and tab are drawn once per flag;
    /// each frame only mixes the glow into the edge bands (well under a millisecond) and sets the tab on top.
    /// </summary>
    internal sealed class FlagFramePainter : IDisposable
    {
        private const int BreathSteps = 24, BreathMilliseconds = 2400;
        private readonly int _w, _h, _band;
        private readonly float _s;
        private int[] _glow;
        private Bitmap _tab;
        private Point _tabAt;
        private RacingFlag _flag;

        public FlagFramePainter(int width, int height)
        {
            _w = width;
            _h = height;
            _s = Math.Min(width / 854f, height / 480f);
            _band = Math.Min(Math.Min(width, height) / 2, (int)Math.Ceiling(42 * _s));
        }

        /// <summary>Where the slow breath is, in steps: it changes about ten times a second, so the runtime redraws for it.</summary>
        public static int Breath(int now) => (int)((uint)now % BreathMilliseconds * BreathSteps / BreathMilliseconds);

        public void Draw(Bitmap target, RacingFlag flag, float alpha, int now)
        {
            if (flag == RacingFlag.None || alpha <= 0) return;
            if (flag != _flag || _glow == null) Prepare(flag);
            float breath = 0.8f + 0.2f * (float)(0.5 + 0.5 * Math.Cos(Breath(now) * 2 * Math.PI / BreathSteps));
            MixGlow(target, (int)Math.Round(Math.Max(0, Math.Min(1, alpha * breath)) * 256));
            using (Graphics g = Graphics.FromImage(target))
            {
                if (alpha >= 0.999f) g.DrawImageUnscaled(_tab, _tabAt);
                else
                    using (var attributes = new ImageAttributes())
                    {
                        attributes.SetColorMatrix(new ColorMatrix { Matrix33 = alpha });
                        g.DrawImage(_tab, new Rectangle(_tabAt, _tab.Size), 0, 0, _tab.Width, _tab.Height, GraphicsUnit.Pixel, attributes);
                    }
            }
        }

        /// <summary>Lays the glow over the edge bands at a strength out of 256.</summary>
        private void MixGlow(Bitmap target, int strength)
        {
            if (strength <= 0) return;
            int w = _w, h = _h, band = _band;
            BitmapData data = target.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                var row = new int[w];
                for (int y = 0; y < h; y++)
                {
                    IntPtr line = data.Scan0 + y * data.Stride;
                    if (y < band || y >= h - band)
                        Mix(line, row, y * w, 0, w, strength);
                    else
                    {
                        Mix(line, row, y * w, 0, band, strength);
                        Mix(line, row, y * w, w - band, band, strength);
                    }
                }
            }
            finally { target.UnlockBits(data); }
        }

        private void Mix(IntPtr line, int[] row, int rowStart, int x, int count, int strength)
        {
            IntPtr at = line + x * 4;
            System.Runtime.InteropServices.Marshal.Copy(at, row, x, count);
            for (int i = x; i < x + count; i++)
            {
                int glow = _glow[rowStart + i];
                int a = (int)((uint)glow >> 24) * strength >> 8;
                if (a == 0) continue;
                int under = row[i], keep = 255 - a;
                int r = (((glow >> 16) & 255) * a + ((under >> 16) & 255) * keep) / 255;
                int g = (((glow >> 8) & 255) * a + ((under >> 8) & 255) * keep) / 255;
                int b = ((glow & 255) * a + (under & 255) * keep) / 255;
                int alpha = a + (int)((uint)under >> 24) * keep / 255;
                row[i] = (alpha << 24) | (r << 16) | (g << 8) | b;
            }
            System.Runtime.InteropServices.Marshal.Copy(row, x, at, count);
        }

        private void Prepare(RacingFlag flag)
        {
            Color color = FlagStyle.Of(flag);
            float s = _s, radius = 16 * s, depth = 34 * s, stroke = 3.5f * s, inset = 2 + stroke / 2;
            using (var glow = new Bitmap(_w, _h, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(glow))
                {
                    g.Clear(Color.Transparent);
                    DashKit.Prepare(g);
                    // Light falling off inward from the edge.
                    int rings = (int)Math.Ceiling(depth / 1.5f);
                    for (int i = rings; i >= 0; i--)
                    {
                        float ringInset = inset + i * 1.5f, f = i / (float)rings;
                        int a = (int)(165 * Math.Exp(-3.4 * f) * (1 - f));
                        if (a <= 0) continue;
                        using (var pen = new Pen(Color.FromArgb(a, color), 1.6f))
                        using (GraphicsPath ring = DashKit.Rounded(new RectangleF(ringInset, ringInset, _w - ringInset * 2,
                            _h - ringInset * 2),
                            Math.Max(2, radius - i * 0.6f)))
                            g.DrawPath(pen, ring);
                    }
                    // The edge itself, with the corners outside its curve filled so the frame meets the screen's edge.
                    using (GraphicsPath inside = DashKit.Rounded(new RectangleF(inset, inset, _w - inset * 2, _h - inset * 2), radius))
                    using (var outside = new Region(new Rectangle(0, 0, _w, _h)))
                    {
                        outside.Exclude(inside);
                        using (var brush = new SolidBrush(Color.FromArgb(170, color))) g.FillRegion(brush, outside);
                        using (var pen = new Pen(color, stroke)) g.DrawPath(pen, inside);
                        if (flag == RacingFlag.Checkered)
                            using (var squares = new Pen(Color.FromArgb(14, 15, 18), stroke) { DashPattern = new[] { 1f, 1f } })
                                g.DrawPath(squares, inside);
                    }
                }
                BitmapData data = glow.LockBits(new Rectangle(0, 0, _w, _h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                if (_glow == null) _glow = new int[_w * _h];
                try { System.Runtime.InteropServices.Marshal.Copy(data.Scan0, _glow, 0, _glow.Length); }
                finally { glow.UnlockBits(data); }
            }
            _tab?.Dispose();
            _tab = Tab(flag, out _tabAt);
            _flag = flag;
        }

        /// <summary>A tab hanging from the top edge, in the flag's color, saying what's out.</summary>
        private Bitmap Tab(RacingFlag flag, out Point at)
        {
            string text = FlagStyle.Banner(flag);
            float s = _s, size = 16 * s, tracking = 2.4f * s, height = 32 * s, r = 12 * s, margin = 4 * s;
            SizeF ink;
            using (var measure = new Bitmap(1, 1))
            using (Graphics g = Graphics.FromImage(measure))
                ink = FlagType.Measure(g, text, FlagType.Semi, FontStyle.Regular, size, tracking);
            float width = ink.Width + 44 * s;
            at = new Point((int)Math.Floor(_w / 2f - width / 2 - margin), 0);
            var tab = new Bitmap((int)Math.Ceiling(width + margin * 2) + 1, (int)Math.Ceiling(height + margin) + 1,
                PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(tab))
            {
                DashKit.Prepare(g);
                // In the tab's own pixels; the top corners sit above it, so only the lower ones show round.
                float left = _w / 2f - width / 2 - at.X;
                var box = new RectangleF(left, -r, width, height + r);
                using (var shadow = new SolidBrush(Color.FromArgb(100, 0, 0, 0)))
                    g.FillRoundedRectangle(shadow, new RectangleF(box.X - 2 * s, box.Y, box.Width + 4 * s, box.Height + 3 * s), r + 2 * s);
                using (var fill = new SolidBrush(FlagStyle.TabFill(flag)))
                    g.FillRoundedRectangle(fill, box, r);
                if (flag == RacingFlag.Black)
                    using (GraphicsPath outline = DashKit.Rounded(box, r))
                    using (var pen = new Pen(Color.FromArgb(242, 242, 242), Math.Max(1, 1.5f * s)))
                        g.DrawPath(pen, outline);
                FlagType.Draw(g, text, FlagType.Semi, FontStyle.Regular, size, tracking,
                    new PointF(left + width / 2, height / 2 + 0.5f * s), FlagStyle.TabInk(flag));
            }
            return tab;
        }

        public void Dispose() => _tab?.Dispose();
    }
}
