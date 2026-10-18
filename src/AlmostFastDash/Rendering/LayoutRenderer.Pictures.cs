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
        private sealed class StyledPicture
        {
            public Bitmap Bitmap;
            public Rectangle At;
            public int Hash;
        }

        private readonly Dictionary<string, StyledPicture> _styled = new Dictionary<string, StyledPicture>();

        /// <summary>Draws a widget into a layer exactly as big as it can reach, then lays its Style effects beneath it.</summary>
        private StyledPicture Compose(DashWidget widget, WidgetType type, EvalScope scope, int previousGear, float progress, LayerStyle style,
            RectangleF b, bool flipX, bool flipY, float rotate, bool sample = false)
        {
            Rectangle at = Outward(Reach(widget));
            if (at.Width < 1 || at.Height < 1) at = new Rectangle((int)b.X, (int)b.Y, Math.Max(1, (int)b.Width), Math.Max(1, (int)b.Height));
            // Premultiplied pictures blend back exactly; see-through panels would lose precision otherwise.
            var layer = new Bitmap(at.Width, at.Height, PixelFormat.Format32bppPArgb);
            Matrix place;
            using (Graphics lg = Graphics.FromImage(layer))
            {
                DashKit.Prepare(lg);
                lg.TranslateTransform(-at.X, -at.Y);
                Turn(lg, b, flipX, flipY, rotate);
                place = lg.Transform;
                var inner = new WidgetContext(this, lg, scope, widget, type) { PreviousGear = previousGear, GearProgress = progress, FlipX = flipX, FlipY = flipY, Sample = sample };
                type.Draw(inner);
            }
            // No effects to lay beneath it: the layer is the finished picture.
            if (!style.NeedsCompose)
            {
                place.Dispose();
                return new StyledPicture { Bitmap = layer, At = at };
            }
            using (layer)
            using (place)
                return new StyledPicture { Bitmap = style.Compose(layer, b, place), At = at };
        }

        private readonly Dictionary<string, Bitmap> _glass = new Dictionary<string, Bitmap>();

        /// <summary>
        /// A pane of Liquid Glass, made once per shape, place, setting and backdrop and then kept. The backdrop
        /// is the screen's background with every fixed widget beneath the pane (images, shapes, gradients,
        /// other panes). Live widgets are left out: the glass would have to be made again every frame. The
        /// renderer owns the bitmap: callers must not dispose it.
        /// </summary>
        internal Bitmap Glass(DashWidget pane, EvalScope scope, Rectangle at, GraphicsPath shape, GlassSettings settings)
        {
            Bitmap background = Background();
            int margin = LiquidGlass.Margin(settings);
            Rectangle area = Rectangle.Intersect(Rectangle.Inflate(at, margin, margin), new Rectangle(0, 0, _width, _height));
            if (area.Width < 1 || area.Height < 1) area = new Rectangle(Math.Max(0, Math.Min(_width - 1, at.X)), Math.Max(0, Math.Min(_height - 1, at.Y)), 1, 1);
            var beneath = new List<DashWidget>();
            DashScreen screen = _layout.Screens.FirstOrDefault(s => s.Widgets.Contains(pane));
            if (screen != null)
                foreach (DashWidget widget in screen.Widgets)
                {
                    if (widget == pane) break;
                    if (!widget.Hidden && Still(widget) && Touches(widget, area)) beneath.Add(widget);
                }

            int outline = 17;
            foreach (PointF p in shape.PathPoints) outline = outline * 31 + (int)Math.Round(p.X * 8) * 7 + (int)Math.Round(p.Y * 8);
            int under = 0;
            foreach (DashWidget widget in beneath) under = under * 31 + widget.Id.GetHashCode() * 7 + WidgetHash(widget, scope, 0, 1);
            string key = at + "|" + outline + "|" + shape.PointCount + "|" + settings.Key + "|" + _backgroundKey + "|" + under;
            if (_glass.TryGetValue(key, out Bitmap made)) return made;
            if (_glass.Count >= 64) ForgetGlass();

            int[] backdrop;
            using (var behind = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(behind))
                {
                    DashKit.Prepare(g);
                    g.TranslateTransform(-area.X, -area.Y);
                    g.DrawImageUnscaled(background, 0, 0);
                    foreach (DashWidget widget in beneath)
                        try { DrawWidget(g, widget, scope, 0, 1); }
                        catch { }
                }
                backdrop = LiquidGlass.Pixels(behind);
            }
            return _glass[key] = LiquidGlass.Render(backdrop, area, at, shape, settings);
        }

        private void ForgetGlass()
        {
            foreach (Bitmap pane in _glass.Values) pane.Dispose();
            _glass.Clear();
        }

        private void ForgetStyled()
        {
            foreach (StyledPicture picture in _styled.Values) picture.Bitmap.Dispose();
            _styled.Clear();
            foreach (StyledPicture mask in _masks.Values) mask.Bitmap.Dispose();
            _masks.Clear();
            ForgetGlass();
            _still.Clear();
            ForgetShapes();
        }

        private readonly Dictionary<string, bool> _still = new Dictionary<string, bool>();

        private readonly Dictionary<string, GraphicsPath> _shapes = new Dictionary<string, GraphicsPath>();

        /// <summary>
        /// A shape kept by a key describing its geometry (such as a warped bar's segments), built once.
        /// The renderer owns it: callers must not dispose it.
        /// </summary>
        internal GraphicsPath Shape(string key, Func<GraphicsPath> build)
        {
            if (_shapes.TryGetValue(key, out GraphicsPath path)) return path;
            if (_shapes.Count >= 4000) ForgetShapes();
            return _shapes[key] = build();
        }

        private void ForgetShapes()
        {
            foreach (GraphicsPath path in _shapes.Values) path.Dispose();
            _shapes.Clear();
        }

        /// <summary>Whether a widget never changes, worked out once per widget (and again after an edit).</summary>
        private bool Still(DashWidget widget)
        {
            if (!_still.TryGetValue(widget.Id, out bool still)) _still[widget.Id] = still = IsStatic(widget);
            return still;
        }

        /// <summary>Mirrors and turns everything drawn next about the middle of a widget's box.</summary>
        private static void Turn(Graphics g, RectangleF b, bool flipX, bool flipY, float rotate)
        {
            if (!flipX && !flipY && Math.Abs(rotate) < 0.01f) return;
            float cx = b.X + b.Width / 2, cy = b.Y + b.Height / 2;
            g.TranslateTransform(cx, cy);
            if (Math.Abs(rotate) >= 0.01f) g.RotateTransform(rotate);
            g.ScaleTransform(flipX ? -1 : 1, flipY ? -1 : 1);
            g.TranslateTransform(-cx, -cy);
        }
    }
}
