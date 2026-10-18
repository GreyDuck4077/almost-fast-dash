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
        /// <summary>
        /// Render hands back the same bitmap every time instead of a new copy: it belongs to the renderer, isn't
        /// to be disposed, and holds the frame only until the next Render. The wheel runtime copies each frame
        /// straight on to its screens, so this saves a 1.6 MB allocation (and the garbage) every frame.
        /// </summary>
        internal bool SharedOutput { get; set; }

        /// <summary>
        /// The fingerprints StateHash works out are used again by the Render that follows for the same telemetry,
        /// instead of evaluating every widget's formulas twice a frame. Only for the wheel runtime, where nothing
        /// changes between the two; the designer edits widgets in between.
        /// </summary>
        internal bool ReuseFingerprints { get; set; }

        private readonly Dictionary<string, int> _fingerprints = new Dictionary<string, int>();

        private TelemetrySnapshot _fingerprintsFor;

        private bool _fingerprintsFlash;

        /// <summary>A widget's fingerprint: the one StateHash just worked out for this telemetry when there is one.</summary>
        private int Fingerprint(DashWidget widget, EvalScope scope, int previousGear, float progress)
        {
            // The gear's fingerprint follows its change animation, which StateHash doesn't have.
            if (ReuseFingerprints && widget.Type != "gear" && ReferenceEquals(scope.T, _fingerprintsFor) && scope.ShiftFlashOn == _fingerprintsFlash &&
                _fingerprints.TryGetValue(widget.Id, out int known))
                return known;
            return WidgetHash(widget, scope, previousGear, progress);
        }

        private Bitmap _frame, _output;

        private DashScreen _frameScreen;

        private bool _frameConnected;

        private Dictionary<string, int> _drawn = new Dictionary<string, int>();

        /// <summary>How much of the last frame was redrawn, 0 to 1 (for the self-test).</summary>
        internal double LastRedrawShare { get; private set; }

        /// <summary>
        /// The part of the last frame that can differ from the one before it: the areas redrawn, empty when nothing
        /// was, or the whole frame after a flag or anything else drawn over it all. A window showing the dashboard
        /// copies and uploads just this.
        /// </summary>
        public Rectangle LastChanged { get; private set; }

        /// <summary>The shared output still holds the last frame exactly (no flag drawn over it since), so only what changed needs copying into it.</summary>
        private bool _outputHoldsFrame;

        private void ForgetFrame() => _frameScreen = null;

        /// <summary>
        /// Drops the cached base images and the kept frame after the design itself was edited in
        /// place (the designer calls this on every edit); the next frame is drawn in full.
        /// </summary>
        public void Invalidate()
        {
            foreach (BaseLayer layer in _bases.Values) layer.Bitmap.Dispose();
            _bases.Clear();
            ForgetStyled();
            ForgetFrame();
            _areas.Clear();
        }

        private Bitmap RenderChanges(DashScreen screen, EvalScope scope, int previousGear, float progress)
        {
            BaseLayer layer = Base(screen, scope);
            var full = new Rectangle(0, 0, _width, _height);
            if (_frame == null) _frame = new Bitmap(_width, _height, PixelFormat.Format32bppArgb);
            bool redrawAll = _frameScreen != screen || _frameConnected != scope.T.Connected;

            long started = Timings != null ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            // Fingerprint every live widget; the ones that changed mark their reach as dirty.
            var hashes = new Dictionary<string, int>(screen.Widgets.Count);
            var dirty = new List<Rectangle>();
            foreach (DashWidget widget in screen.Widgets)
            {
                if (widget.Hidden || layer.Baked.Contains(widget.Id)) continue;
                int hash;
                try { hash = Fingerprint(widget, scope, previousGear, progress); }
                catch { hash = Environment.TickCount; }
                hashes[widget.Id] = hash;
                if (!redrawAll && (!_drawn.TryGetValue(widget.Id, out int before) || before != hash))
                {
                    foreach (RectangleF paints in Areas(widget)) dirty.Add(Rectangle.Intersect(full, Outward(paints)));
                    if (Timings != null)
                    {
                        Timings.TryGetValue("changed: " + widget.Name, out double times);
                        Timings["changed: " + widget.Name] = times + 1;
                    }
                }
            }
            if (redrawAll) dirty = new List<Rectangle> { full };
            else dirty = Merge(dirty, full);
            Rectangle changed = Rectangle.Empty;
            foreach (Rectangle r in dirty) changed = changed.IsEmpty ? r : Rectangle.Union(changed, r);
            Time("(fingerprints)", ref started);

            long area = 0;
            if (dirty.Count > 0)
            {
                // Start the areas again from the base image (background and fixed widgets): a straight copy of its
                // pixels, row by row.
                Restore(layer.Bitmap, dirty);
                Time("(background restored)", ref started);
                using (Graphics g = Graphics.FromImage(_frame))
                {
                    DashKit.Prepare(g);
                    _target = _frame;
                    _targetAreas = dirty;
                    // All the areas are redone together, clipped to their union: each widget is drawn once,
                    // however many areas it touches.
                    using (var region = new Region(Rectangle.Empty))
                    {
                        foreach (Rectangle r in dirty)
                        {
                            area += (long)r.Width * r.Height;
                            region.Union(r);
                        }
                        g.SetClip(region, CombineMode.Replace);
                    }
                    foreach (DashWidget widget in screen.Widgets)
                    {
                        if (widget.Hidden || layer.Baked.Contains(widget.Id) || !dirty.Any(r => Touches(widget, r))) continue;
                        try
                        {
                            // A widget redrawn only because something near it changed (its own fingerprint held)
                            // goes from its finished picture: one image copy instead of drawing it again.
                            bool steady = !redrawAll && _drawn.TryGetValue(widget.Id, out int before) && before == hashes[widget.Id];
                            DrawWidget(g, widget, scope, previousGear, progress, steady);
                            Errors.Remove(widget.Id);
                        }
                        catch (Exception e) { Errors[widget.Id] = e.Message; }
                        Time(widget.Name ?? widget.Id, ref started);
                    }
                    g.ResetClip();
                }
            }
            LastRedrawShare = (double)area / ((long)_width * _height);
            _drawn = hashes;
            _frameScreen = screen;
            _frameConnected = scope.T.Connected;
            if (!SharedOutput) return _frame.Clone(full, PixelFormat.Format32bppArgb);
            // A straight copy of the pixels into the one output bitmap (flag frames go on it, never on _frame): only
            // what changed, while the output still holds the last frame.
            Bitmap output = Output();
            if (!_outputHoldsFrame) changed = full;
            if (!changed.IsEmpty)
            {
                BitmapData from = _frame.LockBits(changed, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                BitmapData to = output.LockBits(changed, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    long row = (long)changed.Width * 4;
                    unsafe
                    {
                        for (int y = 0; y < changed.Height; y++)
                            Buffer.MemoryCopy((byte*)from.Scan0 + (long)y * from.Stride, (byte*)to.Scan0 + (long)y * to.Stride, row, row);
                    }
                }
                finally
                {
                    _frame.UnlockBits(from);
                    output.UnlockBits(to);
                }
            }
            _outputHoldsFrame = true;
            LastChanged = changed;
            Time("(copied out)", ref started);
            return output;
        }

        /// <summary>With Timings on (the profiler), adds the time since <paramref name="since"/> under a name and restarts it.</summary>
        private void Time(string name, ref long since)
        {
            if (Timings == null) return;
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            Timings.TryGetValue(name, out double sum);
            Timings[name] = sum + (now - since) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            since = now;
        }

        private Bitmap Output() => _output ?? (_output = new Bitmap(_width, _height, PixelFormat.Format32bppArgb));

        /// <summary>Copies the base image's pixels into the frame over each area (the same as drawing it there 1:1, much cheaper).</summary>
        private void Restore(Bitmap from, List<Rectangle> areas)
        {
            foreach (Rectangle area in areas)
            {
                if (area.Width <= 0 || area.Height <= 0) continue;
                BitmapData source = from.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                BitmapData target = _frame.LockBits(area, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
                try
                {
                    long row = (long)area.Width * 4;
                    unsafe
                    {
                        for (int y = 0; y < area.Height; y++)
                            Buffer.MemoryCopy((byte*)source.Scan0 + (long)y * source.Stride, (byte*)target.Scan0 + (long)y * target.Stride, row, row);
                    }
                }
                finally
                {
                    from.UnlockBits(source);
                    _frame.UnlockBits(target);
                }
            }
        }

        private static Rectangle Outward(RectangleF r) =>
            Rectangle.FromLTRB((int)Math.Floor(r.Left), (int)Math.Floor(r.Top), (int)Math.Ceiling(r.Right), (int)Math.Ceiling(r.Bottom));

        /// <summary>
        /// Joins dirty areas where one box costs less than two (overlapping areas would otherwise be
        /// drawn twice); past half the screen, just redraws it all.
        /// </summary>
        private static List<Rectangle> Merge(List<Rectangle> rects, Rectangle full)
        {
            rects.RemoveAll(r => r.Width <= 0 || r.Height <= 0);
            long Area(Rectangle r) => (long)r.Width * r.Height;
            bool merged = true;
            while (merged)
            {
                merged = false;
                for (int i = 0; i < rects.Count && !merged; i++)
                    for (int j = i + 1; j < rects.Count; j++)
                    {
                        Rectangle a = rects[i], b = rects[j], union = Rectangle.Union(a, b);
                        if (Area(union) > Area(a) + Area(b) - Area(Rectangle.Intersect(a, b)) + 4000) continue;
                        rects[i] = union;
                        rects.RemoveAt(j);
                        merged = true;
                        break;
                    }
            }
            long area = 0;
            foreach (Rectangle r in rects) area += (long)r.Width * r.Height;
            return area * 2 > (long)full.Width * full.Height ? new List<Rectangle> { full } : rects;
        }

        private sealed class BaseLayer
        {
            public Bitmap Bitmap;
            public HashSet<string> Baked;
        }

        private readonly Dictionary<string, BaseLayer> _bases = new Dictionary<string, BaseLayer>();

        private Bitmap _background;

        private string _backgroundKey;

        /// <summary>Widget types that read telemetry directly, whatever their properties say.</summary>
        private static readonly HashSet<string> LiveTypes = new HashSet<string> { "gear", "relative", "blindspot", "flag", "progress", "trace", "deltatrace", "laphistory", "standings", "tyres", "graph", "trackmap", "radar", "traffic" };

        private static bool IsStatic(DashWidget widget)
        {
            WidgetType type = WidgetCatalog.Find(widget.Type);
            if (type == null || LiveTypes.Contains(widget.Type)) return false;
            // Clipped and blended widgets depend on what's beneath them, so they're drawn with it, never ahead.
            if (Clipped(widget) || widget.Get("fxBlend", "Normal") != "Normal") return false;
            // A tap zone shows which view it's on.
            if (!string.IsNullOrWhiteSpace(widget.Get("tapName", "")) && Expr.Bool(widget.Get("tapDots", "true"))) return false;
            foreach (PropDef prop in type.Props)
            {
                string raw = widget.Get(prop.Key, prop.Default);
                // A formula with no data in it (such as "=1") never changes.
                if (IsFormula(raw, prop) && !Expr.Compile(Strip(raw)).IsConstant) return false;
            }
            return true;
        }

        /// <summary>How far a widget can draw outside its box (glows, gear neighbors).</summary>
        private static RectangleF Reach(DashWidget widget)
        {
            // Everything a widget can paint must fall inside this, or redrawing only what changed would leave stale pixels.
            float Num(string key, float fallback, float formula) =>
                widget.Get(key) is string raw && raw.StartsWith("=") ? formula : (float)Math.Max(0, Expr.Num(widget.Get(key, fallback.ToString(System.Globalization.CultureInfo.InvariantCulture))));
            float margin = 6 + 2 * Num("glow", widget.Type == "arc" ? 9 : widget.Type == "shiftlights" ? 10 : widget.Type == "blindspot" ? 14 : 0, 40);
            RectangleF b = widget.Bounds;
            switch (widget.Type)
            {
                case "gear": margin += 1.4f * Math.Max(30, Num("neighborSize", 30, 120)); break;
                case "panel": margin += 24; break; // the Liquid style's soft shadow
                case "shape": margin += Num("strokeWidth", 2, 30); break;
                case "text":
                case "metric":
                    margin += 0.1f * Num(widget.Type == "text" ? "size" : "valueSize", widget.Type == "text" ? 28 : 32, 300);
                    if (widget.Type == "text" && !Expr.Bool(widget.Get("fit", "true"))) b.Inflate(b.Width, 0);
                    break;
                case "arc":
                    float over = Math.Max(0, Num("needleLength", 100, 130) - 100) / 100f;
                    margin += over * Math.Max(b.Width, b.Height) / 2 + Num("needleWidth", 5, 24);
                    break;
            }
            // The Style effects reach past the drawing: a shadow by its blur and offset, a glow, an outline.
            float offset = Math.Max(Math.Abs(SignedNum(widget, "fxShadowX", 0, 60)), Math.Abs(SignedNum(widget, "fxShadowY", 4, 60)));
            margin += Math.Max(Math.Max(2 * Num("fxGlow", 0, 60), Num("fxShadow", 0, 60) > 0 ? 2 * Num("fxShadow", 0, 60) + offset : 0), Num("fxOutline", 0, 30)) + 2;
            if (Math.Abs(Expr.Num(widget.Get("rotate", "0")) % 360) > 0.01 || IsFormula(widget.Get("rotate", "0"), new PropDef { Kind = PropKind.Number }))
            {
                // A turned widget can reach anywhere within the circle around its box.
                float half = (float)Math.Sqrt(b.Width * b.Width + b.Height * b.Height) / 2;
                b = new RectangleF(b.X + b.Width / 2 - half, b.Y + b.Height / 2 - half, half * 2, half * 2);
            }
            b.Inflate(margin, margin);
            return b;
        }

        private readonly Dictionary<DashWidget, RectangleF[]> _areas = new Dictionary<DashWidget, RectangleF[]>();

        /// <summary>
        /// Where a widget can paint, as one or more boxes. Usually that is its <see cref="Reach"/>. A big arc
        /// gauge only paints a strip along its ring, so it gives short boxes along the ring instead. A sweep
        /// across the whole screen then redraws that strip and not everything inside the arch.
        /// </summary>
        private bool Touches(DashWidget widget, RectangleF region)
        {
            foreach (RectangleF area in Areas(widget))
                if (area.IntersectsWith(region)) return true;
            return false;
        }

        private RectangleF[] Areas(DashWidget widget)
        {
            if (_areas.TryGetValue(widget, out RectangleF[] known)) return known;
            RectangleF[] areas = widget.Type == "arc" ? ArcStrips(widget) : null;
            if (areas == null) areas = new[] { Reach(widget) };
            _areas[widget] = areas;
            return areas;
        }

        private static RectangleF[] ArcStrips(DashWidget widget)
        {
            WidgetType type = WidgetCatalog.Find("arc");
            string Raw(string key) => widget.Get(key, type.Prop(key)?.Default) ?? "";
            bool Fixed(params string[] keys) => keys.All(k => !Raw(k).StartsWith("="));
            // Anything that can move the drawing about, or that draws across the face, needs the whole box.
            if (!Fixed("start", "sweep", "thickness", "glow", "oval", "needle", "needleLength", "tickLength", "numberSize", "rimWidth", "flipX", "flipY", "rotate")) return null;
            string needle = Raw("needle");
            if (needle == "Needle" || needle == "Line") return null;
            if (Math.Abs(Expr.Num(Raw("rotate"))) > 0.01) return null;
            if (new[] { "fxFill", "fxOutline", "fxShadow", "fxGlow" }.Any(k => { string v = widget.Get(k); return v != null && v != "0" && v != "None"; })) return null;
            float N(string key) => (float)Expr.Num(Raw(key));
            float thickness = N("thickness"), glow = Math.Max(0, N("glow")), start = N("start"), sweep = N("sweep");
            if (Math.Abs(sweep) < 1) return null;
            RectangleF b = widget.Bounds;
            float margin = thickness / 2 + glow / 2 + 1;
            bool oval = Expr.Bool(Raw("oval"));
            float radius = Math.Max(4, Math.Min(b.Width, b.Height) / 2 - margin);
            var ring = new ArcRing
            {
                Center = new PointF(b.X + b.Width / 2, b.Y + b.Height / 2), Thickness = thickness, Start = start, Sweep = sweep,
                RadiusX = oval ? Math.Max(4, b.Width / 2 - margin) : radius, RadiusY = oval ? Math.Max(4, b.Height / 2 - margin) : radius
            };
            bool ticks = Raw("ticks").StartsWith("=") || Expr.Num(Raw("ticks")) > 0;
            bool numbers = ticks && (Raw("numbers").StartsWith("=") || Expr.Bool(Raw("numbers")));
            float marker = needle == "Marker" ? thickness * Math.Max(0, N("needleLength") / 100 - 1) / 2 + N("needleWidth") : 0;
            // Out to the glow and the marker; in to the ticks and the numbers (a number is drawn 0.85 of its
            // size inside the ticks and is at most two digits wide).
            // Bezel lines sit just outside both edges of the band, and push the marks further in.
            float bezel = Raw("rim").Trim().Length > 0 ? WidgetPainters.BezelGap + Math.Max(0.5f, N("rimWidth")) + 1.5f : 0;
            float outward = Math.Max(thickness / 2 + marker + glow, thickness / 2 + bezel + 1) + 3;
            float inward = Math.Max(thickness / 2 + glow, thickness / 2 + 4 + bezel + (ticks ? N("tickLength") : 0) + (numbers ? N("numberSize") * 1.9f : 0)) + 3;
            const float pad = 3;
            float cap = thickness / 2 + glow;
            int pieces = Math.Max(1, (int)Math.Ceiling(Math.Abs(sweep) / 10));
            var strips = new RectangleF[pieces];
            for (int i = 0; i < pieces; i++)
            {
                float a0 = start + sweep * i / pieces, a1 = start + sweep * (i + 1) / pieces;
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                foreach (float a in new[] { a0, (a0 + a1) / 2, a1 })
                    foreach (PointF p in new[] { ring.At(a, outward), ring.At(a, -inward) })
                    {
                        minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
                        minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
                    }
                // The round caps at the two ends reach past the ring's ends.
                float grow = pad + (i == 0 || i == pieces - 1 ? cap : 0);
                var strip = RectangleF.FromLTRB(minX - grow, minY - grow, maxX + grow, maxY + grow);
                if (Expr.Bool(Raw("flipX"))) strip.X = b.Left + b.Right - strip.Right;
                if (Expr.Bool(Raw("flipY"))) strip.Y = b.Top + b.Bottom - strip.Bottom;
                strips[i] = strip;
            }
            return strips;
        }

        /// <summary>A number setting that may be negative (such as a shadow offset); a formula counts as the given limit.</summary>
        private static float SignedNum(DashWidget widget, string key, float fallback, float formula)
        {
            string raw = widget.Get(key);
            if (raw != null && raw.StartsWith("=")) return formula;
            double v = Expr.Num(raw ?? fallback.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return double.IsNaN(v) ? 0 : (float)v;
        }

        private BaseLayer Base(DashScreen screen, EvalScope scope)
        {
            if (_bases.TryGetValue(screen.Id, out BaseLayer cached)) return cached;
            var layer = new BaseLayer { Bitmap = new Bitmap(_width, _height, PixelFormat.Format32bppArgb), Baked = new HashSet<string>() };
            using (Graphics g = Graphics.FromImage(layer.Bitmap))
            {
                DashKit.Prepare(g);
                g.DrawImageUnscaled(Background(), 0, 0);
                // A static widget can move into the base only if nothing live is drawn under it,
                // otherwise drawing it early would put it below that live widget.
                var liveBelow = new List<RectangleF>();
                foreach (DashWidget widget in screen.Widgets)
                {
                    if (widget.Hidden) continue;
                    if (IsStatic(widget))
                    {
                        RectangleF reach = Reach(widget);
                        if (!liveBelow.Any(r => r.IntersectsWith(reach)))
                        {
                            try { DrawWidget(g, widget, scope, 0, 1); layer.Baked.Add(widget.Id); }
                            catch { }
                            continue;
                        }
                    }
                    liveBelow.AddRange(Areas(widget));
                }
            }
            _bases[screen.Id] = layer;
            return layer;
        }

        internal bool IsBaked(DashScreen screen, DashWidget widget) =>
            _bases.TryGetValue(screen.Id, out BaseLayer layer) && layer.Baked.Contains(widget.Id);
    }
}
