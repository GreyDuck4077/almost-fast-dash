using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace AlmostFastDash.Rendering
{
    internal sealed partial class LayoutRenderer : IDashboardRenderer, IStateRenderer
    {
        // ------------------------------------------------------------------ clipping masks and blending

        /// <summary>The bitmap being drawn now, and the areas of it being redrawn (null: all of it), for blended widgets.</summary>
        private Bitmap _target;

        private List<Rectangle> _targetAreas;

        /// <summary>Each base's shape, drawn on its own, kept while its fingerprint holds: the mask for what clips to it.</summary>
        private readonly Dictionary<string, StyledPicture> _masks = new Dictionary<string, StyledPicture>();

        private static bool Clipped(DashWidget widget) => Expr.Bool(widget.Get("clip", "false"));

        private DashScreen ScreenOf(DashWidget widget) => _layout.Screens.FirstOrDefault(s => s.Widgets.Contains(widget));

        /// <summary>
        /// The layer a clipped widget shows through: the nearest below it that isn't clipped itself, so several clipped
        /// layers in a row share one base. Null when the widget isn't clipped, or nothing is below it.
        /// </summary>
        internal DashWidget ClipBase(DashWidget widget)
        {
            if (!Clipped(widget)) return null;
            DashScreen screen = ScreenOf(widget);
            if (screen == null) return null;
            for (int i = screen.Widgets.IndexOf(widget) - 1; i >= 0; i--)
                if (!Clipped(screen.Widgets[i])) return screen.Widgets[i];
            return null;
        }

        /// <summary>Whether a widget is on the screen now: not hidden in the designer, and its Show when (if any) true.</summary>
        private bool Shows(DashWidget widget, EvalScope scope)
        {
            if (widget.Hidden) return false;
            string visible = widget.Get("visible", "");
            return string.IsNullOrWhiteSpace(visible) || Expr.Bool(Expr.Evaluate(Strip(visible), ForWidget(scope, widget)));
        }

        /// <summary>
        /// Keeps a clipped widget's picture only where its base is drawn: each pixel scaled by the base's own coverage
        /// there (its shape as drawn, without its Style effects), and nothing beyond the base.
        /// </summary>
        private unsafe void Mask(StyledPicture picture, DashWidget clipBase, EvalScope scope)
        {
            StyledPicture mask = MaskOf(clipBase, scope);
            Rectangle at = picture.At, m = mask.At;
            BitmapData p = picture.Bitmap.LockBits(new Rectangle(0, 0, at.Width, at.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
            BitmapData q = mask.Bitmap.LockBits(new Rectangle(0, 0, m.Width, m.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try
            {
                for (int y = 0; y < at.Height; y++)
                {
                    uint* row = (uint*)((byte*)p.Scan0 + (long)y * p.Stride);
                    int my = at.Y + y - m.Y;
                    uint* maskRow = my >= 0 && my < m.Height ? (uint*)((byte*)q.Scan0 + (long)my * q.Stride) : null;
                    for (int x = 0; x < at.Width; x++)
                    {
                        uint pixel = row[x];
                        if (pixel == 0) continue;
                        int mx = at.X + x - m.X;
                        uint cover = maskRow != null && mx >= 0 && mx < m.Width ? maskRow[mx] >> 24 : 0;
                        if (cover == 255) continue;
                        row[x] = cover == 0 ? 0 : ScaleAll(pixel, cover);
                    }
                }
            }
            finally
            {
                picture.Bitmap.UnlockBits(p);
                mask.Bitmap.UnlockBits(q);
            }
        }

        private StyledPicture MaskOf(DashWidget clipBase, EvalScope scope)
        {
            int hash = WidgetHash(clipBase, scope, 0, 1);
            if (_masks.TryGetValue(clipBase.Id, out StyledPicture mask) && mask.Hash == hash) return mask;
            mask?.Bitmap.Dispose();
            WidgetType type = WidgetCatalog.Find(clipBase.Type);
            EvalScope own = ForWidget(scope, clipBase);
            var read = new WidgetContext(this, null, own, clipBase, type);
            mask = Compose(clipBase, type, own, 0, 1, new LayerStyle(), read.B, read.Bool("flipX"), read.Bool("flipY"), (float)(read.Num("rotate") % 360));
            mask.Hash = hash;
            _masks[clipBase.Id] = mask;
            return mask;
        }

        /// <summary>A premultiplied pixel at a coverage from 0 to 255: every channel scaled, exactly, without a division.</summary>
        private static uint ScaleAll(uint pixel, uint cover)
        {
            uint Scale(uint v) { uint t = v * cover + 128; return (t + (t >> 8)) >> 8; }
            return Scale(pixel >> 24) << 24 | Scale((pixel >> 16) & 255) << 16 | Scale((pixel >> 8) & 255) << 8 | Scale(pixel & 255);
        }

        /// <summary>
        /// Lays a widget's picture over what's beneath it with a blend mode, within the areas being redrawn only (so a
        /// redraw of part of the screen blends each pixel once). Multiply darkens, Screen and Add brighten, Overlay
        /// adds contrast; where nothing is beneath, the picture is laid as it is.
        /// </summary>
        private unsafe void Blend(Graphics g, StyledPicture picture, string mode, float opacity)
        {
            if (_target == null)
            {
                g.DrawImageUnscaled(picture.Bitmap, picture.At.X, picture.At.Y);
                return;
            }
            g.Flush(FlushIntention.Sync);
            var whole = new Rectangle(0, 0, _target.Width, _target.Height);
            Rectangle at = Rectangle.Intersect(picture.At, whole);
            if (at.IsEmpty) return;
            // The areas being redrawn, as rectangles that don't overlap.
            var areas = new List<Rectangle>();
            using (var region = new Region(Rectangle.Empty))
            {
                foreach (Rectangle area in _targetAreas ?? new List<Rectangle> { whole }) region.Union(area);
                region.Intersect(at);
                using (var identity = new Matrix())
                    foreach (RectangleF scan in region.GetRegionScans(identity)) areas.Add(Rectangle.Round(scan));
            }
            uint fade = (uint)Math.Round(Math.Max(0, Math.Min(1, opacity)) * 255);
            foreach (Rectangle r in areas.Where(a => a.Width > 0 && a.Height > 0))
            {
                BitmapData dst = _target.LockBits(r, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
                BitmapData src = picture.Bitmap.LockBits(new Rectangle(r.X - picture.At.X, r.Y - picture.At.Y, r.Width, r.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
                try
                {
                    for (int y = 0; y < r.Height; y++)
                    {
                        uint* under = (uint*)((byte*)dst.Scan0 + (long)y * dst.Stride), top = (uint*)((byte*)src.Scan0 + (long)y * src.Stride);
                        for (int x = 0; x < r.Width; x++)
                        {
                            uint t = top[x];
                            if (t == 0) continue;
                            if (fade < 255) t = ScaleAll(t, fade);
                            under[x] = BlendPixel(under[x], t, mode);
                        }
                    }
                }
                finally
                {
                    _target.UnlockBits(dst);
                    picture.Bitmap.UnlockBits(src);
                }
            }
        }

        /// <summary>A premultiplied pixel blended onto a straight one beneath it.</summary>
        internal static uint BlendPixel(uint under, uint top, string mode)
        {
            uint ta = top >> 24, ua = under >> 24;
            if (ta == 0) return under;
            float a = ta / 255f;
            // The top's own color, un-premultiplied.
            float tr = ((top >> 16) & 255) / a, tg = ((top >> 8) & 255) / a, tb = (top & 255) / a;
            float ur = (under >> 16) & 255, ug = (under >> 8) & 255, ub = under & 255;
            float Mix(float u, float c)
            {
                float blended;
                switch (mode)
                {
                    case "Multiply": blended = u * c / 255f; break;
                    case "Screen": blended = 255f - (255f - u) * (255f - c) / 255f; break;
                    case "Add": blended = Math.Min(255f, u + c); break;
                    case "Overlay": blended = u < 128 ? 2 * u * c / 255f : 255f - 2 * (255f - u) * (255f - c) / 255f; break;
                    default: blended = c; break;
                }
                // Over nothing there's nothing to blend with: the top shows as it is.
                float under01 = ua / 255f;
                blended = blended * under01 + c * (1 - under01);
                return u + (blended - u) * a;
            }
            uint r = (uint)Math.Max(0, Math.Min(255, Math.Round(Mix(ur, tr)))), gg = (uint)Math.Max(0, Math.Min(255, Math.Round(Mix(ug, tg)))), b = (uint)Math.Max(0, Math.Min(255, Math.Round(Mix(ub, tb))));
            uint outA = ua + (ta * (255 - ua) + 127) / 255;
            if (ua == 0)
            {
                // Nothing beneath: the picture's own color at its own coverage.
                r = (uint)Math.Min(255, Math.Round(tr)); gg = (uint)Math.Min(255, Math.Round(tg)); b = (uint)Math.Min(255, Math.Round(tb));
            }
            return outA << 24 | r << 16 | gg << 8 | b;
        }
    }
}
