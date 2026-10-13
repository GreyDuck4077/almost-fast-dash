using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;

namespace AlmostFastOverlays
{
    /// <summary>Renderers that can tell cheaply whether their next frame would differ.</summary>
    internal interface IStateRenderer
    {
        int StateHash(TelemetrySnapshot telemetry, bool shiftFlashOn, int gearAnimationFrame);
    }

    /// <summary>
    /// Draws a designed (<see cref="DashLayout"/>) dashboard: every dashboard is one. It also
    /// fingerprints what a frame would show, so the screen is only redrawn when that changes.
    /// </summary>
    internal sealed class LayoutRenderer : IDashboardRenderer, IStateRenderer
    {
        private readonly int _width, _height;
        private readonly DashLayout _layout;
        private readonly string _accent;
        private readonly UnitSystem _units;
        private readonly FlagPainter _flags;
        private readonly FlagTimeline _timeline;
        private WavingFlag _waving;
        private FlagFramePainter _flagFrame;
        private Bitmap _flagLayer;
        /// <summary>Fixes the flags' clock (fades, breeze, frame) at one moment in milliseconds, for previews and tests; null follows the clock.</summary>
        public int? FlagClock { get; set; }
        /// <summary>Holds the clock (time formulas, blink) at one moment, for tests; null follows the PC clock.</summary>
        public DateTime? FixedNow { get; set; }
        private readonly Dictionary<string, Font> _fonts = new Dictionary<string, Font>();
        private readonly Dictionary<string, Bitmap> _images = new Dictionary<string, Bitmap>();

        public LayoutRenderer(int width, int height, DashboardSettings settings, UnitSystem units)
        {
            _width = width;
            _height = height;
            _layout = settings?.Design ?? DesignTemplates.Blank();
            _layout.Normalize();
            _accent = settings?.AccentColor ?? "#2DD4BF";
            _units = units ?? UnitSystem.Default;
            _flags = new FlagPainter(width, height);
            _timeline = new FlagTimeline(settings?.Alerts?.BlinkMilliseconds ?? new AlertSettings().BlinkMilliseconds);
        }

        public DashLayout Layout => _layout;

        /// <summary>What the flags show now: a flag over the screen, a frame round the dashboard, or neither.</summary>
        public FlagView Flags(TelemetrySnapshot t) => Flags(t, FlagClock ?? Environment.TickCount);

        private FlagView Flags(TelemetrySnapshot t, int now) =>
            _timeline.At(t ?? TelemetrySnapshot.Disconnected, now, _layout.FullScreenFlags, _layout.FlagFrame);

        /// <summary>Forgets the flags seen so far, so the next one alerts in full again.</summary>
        public void ResetFlags() => _timeline.Reset();

        /// <summary>Widget ids whose last draw failed, with the reason; shown by the designer.</summary>
        public Dictionary<string, string> Errors { get; } = new Dictionary<string, string>();

        public EvalScope Scope(TelemetrySnapshot t, bool shiftFlashOn) => new EvalScope
        {
            T = t ?? TelemetrySnapshot.Disconnected,
            Units = _units,
            Theme = _layout.Theme,
            Accent = _accent,
            ShiftFlashOn = shiftFlashOn,
            Now = FixedNow ?? DateTime.Now,
            Page = _page % Math.Max(1, Pages.Count) + 1,
            PageCount = Math.Max(1, Pages.Count),
            Taps = Taps
        };

        /// <summary>
        /// Which view each tap zone is on, by its tap name (see <see cref="Tap"/>). The designer shares its
        /// own, so a view chosen there stays chosen while the design is edited.
        /// </summary>
        public Dictionary<string, int> Taps { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private DashScreen _shown;

        /// <summary>
        /// A tap on the screen at a point in the dashboard's own pixels. The top-most widget there that has a
        /// tap name moves on to its next view, wrapping round. Returns whether anything changed.
        /// </summary>
        public bool Tap(double x, double y, TelemetrySnapshot t = null)
        {
            EvalScope scope = Scope(t ?? TelemetrySnapshot.Disconnected, false);
            DashScreen screen = _shown ?? ActiveScreen(scope);
            for (int i = screen.Widgets.Count - 1; i >= 0; i--)
            {
                DashWidget widget = screen.Widgets[i];
                string name = widget.Get("tapName", "")?.Trim();
                if (widget.Hidden || string.IsNullOrEmpty(name) || !widget.Bounds.Contains((float)x, (float)y)) continue;
                EvalScope own = ForWidget(scope, widget);
                string visible = widget.Get("visible", "");
                if (!string.IsNullOrWhiteSpace(visible) && !Expr.Bool(Expr.Evaluate(Strip(visible), own))) continue;
                int views = TapViews(widget);
                Taps.TryGetValue(name, out int view);
                Taps[name] = (view + 1) % views;
                return true;
            }
            return false;
        }

        internal static int TapViews(DashWidget widget) => Math.Max(2, Math.Min(9, (int)Math.Round(Expr.Num(widget.Get("tapViews", "2")))));

        /// <summary>The screen to show: the first conditional screen that matches, else the current page.</summary>
        public DashScreen ActiveScreen(EvalScope scope)
        {
            foreach (DashScreen screen in _layout.Screens)
                if (!string.IsNullOrWhiteSpace(screen.Condition) && Expr.Bool(Expr.Evaluate(Strip(screen.Condition), scope)))
                    return screen;
            List<DashScreen> pages = Pages;
            return pages.Count == 0 ? _layout.Screens[0] : pages[_page % pages.Count];
        }

        /// <summary>The screens without a condition: the pages a wheel button steps through.</summary>
        private List<DashScreen> Pages => _layout.Screens.Where(s => string.IsNullOrWhiteSpace(s.Condition)).ToList();

        private int _page;

        /// <summary>Steps to the next page (1) or the previous one (-1), wrapping round.</summary>
        public void StepPage(int delta)
        {
            int count = Math.Max(1, Pages.Count);
            _page = ((_page + delta) % count + count) % count;
        }

        public Bitmap Render(TelemetrySnapshot t, bool shiftFlashOn, int previousGear, float gearTransitionProgress) =>
            Render(t, shiftFlashOn, previousGear, gearTransitionProgress, null);

        /// <summary>Renders a frame; the designer passes the screen being edited.</summary>
        public Bitmap Render(TelemetrySnapshot t, bool shiftFlashOn, int previousGear, float gearTransitionProgress, DashScreen forceScreen)
        {
            t = t ?? TelemetrySnapshot.Disconnected;
            int now = FlagClock ?? Environment.TickCount;
            FlagView flags = Flags(t, now);
            if (flags.Full)
            {
                // The flag covers everything, so the dashboard isn't drawn; it is redrawn in full when the flag goes.
                ForgetFrame();
                var cover = new Bitmap(_width, _height, PixelFormat.Format32bppArgb);
                DrawFlag(cover, flags.Takeover, t, now);
                return cover;
            }
            Bitmap bitmap = RenderDashboard(t, shiftFlashOn, previousGear, gearTransitionProgress, forceScreen);
            if (flags.Frame == RacingFlag.None && flags.Alpha <= 0) return bitmap;
            // The frame and a fading flag go over the finished frame (a copy, never the one kept for the next).
            if (flags.Frame != RacingFlag.None)
                (_flagFrame ?? (_flagFrame = new FlagFramePainter(_width, _height))).Draw(bitmap, flags.Frame, flags.FrameAlpha, now);
            if (flags.Alpha > 0)
            {
                Bitmap layer = _flagLayer ?? (_flagLayer = new Bitmap(_width, _height, PixelFormat.Format32bppArgb));
                DrawFlag(layer, flags.Takeover, t, now);
                using (Graphics g = Graphics.FromImage(bitmap))
                using (var attributes = new ImageAttributes())
                {
                    attributes.SetColorMatrix(new ColorMatrix { Matrix33 = flags.Alpha });
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.DrawImage(layer, new Rectangle(0, 0, _width, _height), 0, 0, _width, _height, GraphicsUnit.Pixel, attributes);
                }
            }
            return bitmap;
        }

        /// <summary>A flag over the whole bitmap: waving cloth, or the still picture.</summary>
        private void DrawFlag(Bitmap target, RacingFlag flag, TelemetrySnapshot t, int now)
        {
            if (_layout.WavingFlags)
            {
                (_waving ?? (_waving = new WavingFlag(_width, _height, _flags))).Draw(target, flag, t, now);
                return;
            }
            using (Graphics g = Graphics.FromImage(target))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImageUnscaled(_flags.Picture(flag, t), 0, 0);
            }
        }

        private Bitmap RenderDashboard(TelemetrySnapshot t, bool shiftFlashOn, int previousGear, float gearTransitionProgress, DashScreen forceScreen)
        {
            if (CacheStatic && RedrawChangesOnly)
            {
                EvalScope live = Scope(t, shiftFlashOn);
                _shown = forceScreen ?? ActiveScreen(live);
                return RenderChanges(_shown, live, previousGear, gearTransitionProgress);
            }

            var bitmap = new Bitmap(_width, _height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                DashKit.Prepare(g);
                EvalScope scope = Scope(t, shiftFlashOn);
                DashScreen screen = forceScreen ?? ActiveScreen(scope);
                _shown = screen;
                Errors.Clear();
                HashSet<string> baked = null;
                if (CacheStatic)
                {
                    BaseLayer layer = Base(screen, scope);
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.DrawImageUnscaled(layer.Bitmap, 0, 0);
                    g.CompositingMode = CompositingMode.SourceOver;
                    baked = layer.Baked;
                }
                else
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.DrawImageUnscaled(Background(), 0, 0);
                    g.CompositingMode = CompositingMode.SourceOver;
                }
                foreach (DashWidget widget in screen.Widgets)
                {
                    if (widget.Hidden || (baked != null && baked.Contains(widget.Id))) continue;
                    long started = Timings != null ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
                    try { DrawWidget(g, widget, scope, previousGear, gearTransitionProgress); }
                    catch (Exception e) { Errors[widget.Id] = e.Message; }
                    if (Timings != null)
                    {
                        Timings.TryGetValue(widget.Name ?? widget.Id, out double sum);
                        Timings[widget.Name ?? widget.Id] = sum + (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                    }
                }
            }
            return bitmap;
        }

        // ------------------------------------------------------------------ static layer cache

        /// <summary>
        /// When on, the background and every widget that can never change (no formulas,
        /// no live data) are drawn once per screen into a base image that each frame
        /// starts from. The device runtime builds a new renderer whenever settings
        /// change, so the cache cannot go stale there; the designer, which edits the
        /// design in place, turns this off.
        /// </summary>
        public bool CacheStatic { get; set; } = true;

        /// <summary>When set (by the self-test), each live widget's drawing time is added up here in milliseconds.</summary>
        internal Dictionary<string, double> Timings { get; set; }

        // ------------------------------------------------------------------ redrawing only what changed

        /// <summary>
        /// With the static cache on, keep the last frame and redraw only the areas of widgets whose
        /// fingerprint changed (and whatever overlaps them), clipped to those areas. Same pixels as a
        /// full redraw, a fraction of the work: most of a dashboard holds still from frame to frame.
        /// </summary>
        public bool RedrawChangesOnly { get; set; } = true;

        private Bitmap _frame;
        private DashScreen _frameScreen;
        private bool _frameConnected;
        private Dictionary<string, int> _drawn = new Dictionary<string, int>();

        /// <summary>How much of the last frame was redrawn, 0 to 1 (for the self-test).</summary>
        internal double LastRedrawShare { get; private set; }

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

            // Fingerprint every live widget; the ones that changed mark their reach as dirty.
            var hashes = new Dictionary<string, int>(screen.Widgets.Count);
            var dirty = new List<Rectangle>();
            foreach (DashWidget widget in screen.Widgets)
            {
                if (widget.Hidden || layer.Baked.Contains(widget.Id)) continue;
                int hash;
                try { hash = WidgetHash(widget, scope, previousGear, progress); }
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

            long area = 0;
            if (dirty.Count > 0)
                using (Graphics g = Graphics.FromImage(_frame))
                {
                    DashKit.Prepare(g);
                    // All the areas are redone together, clipped to their union: each pixel is restored once and
                    // each widget drawn once, however many areas it touches.
                    using (var region = new Region(Rectangle.Empty))
                    {
                        foreach (Rectangle r in dirty)
                        {
                            area += (long)r.Width * r.Height;
                            region.Union(r);
                        }
                        g.SetClip(region, CombineMode.Replace);
                    }
                    // Start the areas again from the base image (background and fixed widgets), drawn 1:1.
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.DrawImageUnscaled(layer.Bitmap, 0, 0);
                    g.CompositingMode = CompositingMode.SourceOver;
                    foreach (DashWidget widget in screen.Widgets)
                    {
                        if (widget.Hidden || layer.Baked.Contains(widget.Id) || !dirty.Any(r => Touches(widget, r))) continue;
                        try
                        {
                            DrawWidget(g, widget, scope, previousGear, progress);
                            Errors.Remove(widget.Id);
                        }
                        catch (Exception e) { Errors[widget.Id] = e.Message; }
                    }
                    g.ResetClip();
                }
            LastRedrawShare = (double)area / ((long)_width * _height);
            _drawn = hashes;
            _frameScreen = screen;
            _frameConnected = scope.T.Connected;
            return _frame.Clone(full, PixelFormat.Format32bppArgb);
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

        /// <summary>How far a widget can draw outside its box (glows, gear neighbours).</summary>
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

        private Bitmap Background()
        {
            LayoutTheme t = _layout.Theme;
            string key = t.BackgroundStyle + "|" + t.Background + "|" + t.Background2 + "|" + _accent;
            if (_background != null && key == _backgroundKey) return _background;
            _background?.Dispose();
            _background = new Bitmap(_width, _height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(_background))
            {
                DashKit.Prepare(g);
                DrawBackground(g);
            }
            _backgroundKey = key;
            return _background;
        }

        public int StateHash(TelemetrySnapshot t, bool shiftFlashOn, int gearAnimationFrame)
        {
            unchecked
            {
                EvalScope scope = Scope(t, shiftFlashOn);
                int now = FlagClock ?? Environment.TickCount;
                FlagView flags = Flags(scope.T, now);
                int flagHash = FlagHash(flags, scope.T, now);
                if (flags.Full) return flagHash;
                DashScreen screen = ActiveScreen(scope);
                int hash = screen.Id.GetHashCode();
                hash = hash * 31 + gearAnimationFrame;
                hash = hash * 31 + (scope.T.Connected ? 1 : 0);
                foreach (DashWidget widget in screen.Widgets)
                    if (!widget.Hidden) hash = hash * 31 + WidgetHash(widget, scope, 0, 1);
                return hash * 31 + flagHash;
            }
        }

        /// <summary>A fingerprint of what the flags add to a frame: the flag and how far it has faded, the cloth's movement, the frame's breath.</summary>
        private int FlagHash(FlagView flags, TelemetrySnapshot t, int now)
        {
            unchecked
            {
                int hash = 0;
                if (flags.Alpha > 0)
                {
                    hash = (int)flags.Takeover * 7919 + (int)(flags.Alpha * 48) * 131 + FlagPainter.Key(flags.Takeover, t).GetHashCode();
                    if (_layout.WavingFlags) hash = hash * 31 + WavingFlag.Frame(now);
                }
                if (flags.Frame != RacingFlag.None)
                    hash = hash * 31 + (int)flags.Frame * 104729 + (int)(flags.FrameAlpha * 48) * 17 + FlagFramePainter.Breath(now);
                return hash;
            }
        }

        /// <summary>
        /// A fingerprint of everything a widget would draw: its formula results and, for widgets
        /// that read telemetry directly, that data. Equal fingerprints mean identical pixels.
        /// </summary>
        private int WidgetHash(DashWidget widget, EvalScope scope, int previousGear, float progress)
        {
            unchecked
            {
                WidgetType type = WidgetCatalog.Find(widget.Type);
                if (type == null) return 0;
                scope = ForWidget(scope, widget);
                // A hidden widget draws nothing, whatever its other values do.
                string visible = widget.Get("visible", "");
                if (!string.IsNullOrWhiteSpace(visible) && !Expr.Bool(Expr.Evaluate(Strip(visible), scope))) return 1;
                int hash = 17;
                foreach (PropDef prop in type.Props)
                {
                    string raw = widget.Get(prop.Key, prop.Default);
                    if (!IsFormula(raw, prop)) continue;
                    object value = Expr.Evaluate(Strip(raw), scope);
                    // Exact values: even a tiny change moves an anti-aliased edge.
                    hash = hash * 31 + (value is double d ? d.GetHashCode() : value is Color c ? c.ToArgb() : Expr.Text(value).GetHashCode());
                }
                TelemetrySnapshot t = scope.T;
                // Several widgets look different while iRacing isn't connected.
                hash = hash * 31 + (t.Connected ? 1 : 0);
                // A tap zone's dots show which view it's on.
                string tapName = widget.Get("tapName", "");
                if (!string.IsNullOrWhiteSpace(tapName) && scope.Taps != null)
                {
                    scope.Taps.TryGetValue(tapName.Trim(), out int view);
                    hash = hash * 31 + view * 7 + TapViews(widget) * 101;
                }
                // Data-heavy widgets read more than their properties show.
                switch (widget.Type)
                {
                    case "gear": hash = hash * 31 + t.Gear * 7 + previousGear * 13 + (int)(progress * 1000) + t.MaxForwardGear * 3; break;
                    case "relative": hash = hash * 31 + RelativeHash(t); break;
                    case "laphistory": hash = hash * 31 + (t.RecentLapSeconds?.Length ?? 0) * 17 + (int)(t.BestLapSeconds * 1000) + ArrayHash(t.RecentLapSeconds) * 7 + ArrayHash(t.StintLapSeconds); break;
                    case "progress":
                        hash = hash * 31 + t.LapsCompleted * 13 + t.LapColorVersion + (int)t.SessionSecondsRemaining + t.TotalLaps * 7 + (int)t.ProgressMode;
                        // Which laps ran under yellow colours their blocks.
                        if (t.YellowCompletedLaps != null) for (int i = 0; i < t.YellowCompletedLaps.Length; i++) if (t.YellowCompletedLaps[i]) hash = hash * 31 + i + 1;
                        break;
                    case "blindspot": hash = hash * 31 + (int)t.LeftBlindSpot * 5 + (int)t.RightBlindSpot; break;
                    case "flag": hash = hash * 31 + (int)t.Flag; break;
                    case "standings": hash = hash * 31 + StandingsHash(t) + (int)(t.SessionBestLapSeconds * 1000); break;
                    case "tyres": hash = hash * 31 + TyresHash(t); break;
                    case "trace": hash = hash * 31 + t.TraceVersion; break;
                    case "deltatrace": hash = hash * 31 + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(t.DeltaTrace ?? new float[0]); break;
                    case "graph": hash = hash * 31 + SampleGraph(widget, type, scope).Version; break;
                    case "trackmap": hash = hash * 31 + MapHash(t); break;
                    case "radar": hash = hash * 31 + RadarHash(t); break;
                    case "traffic": hash = hash * 31 + MapHash(t) + (int)(t.ReferenceLapSeconds * 10) + (int)t.SpeedMph; break;
                }
                return hash;
            }
        }

        /// <summary>
        /// The radar's cars, the gaps its sensors show and how far you've driven (the lanes move), all exact:
        /// even a fraction of a pixel moves an anti-aliased edge.
        /// </summary>
        private static int RadarHash(TelemetrySnapshot t)
        {
            unchecked
            {
                int hash = t.AheadMeters.GetHashCode() * 31 + t.BehindMeters.GetHashCode();
                hash = hash * 31 + (t.LapFraction * t.TrackLengthMiles).GetHashCode();
                foreach (RadarCar car in t.Radar ?? new RadarCar[0])
                    hash = hash * 31 + car.CarIdx * 7 + car.Meters.GetHashCode() * 13 + car.Lane.GetHashCode() * 17 + (car.Alongside ? 1 : 0) + (car.PaceCar ? 2 : 0)
                        + (car.Number?.GetHashCode() ?? 0) + car.ClassColor.ToArgb();
                return hash;
            }
        }

        /// <summary>A grouped widget sees its group's number as Slot.</summary>
        internal static EvalScope ForWidget(EvalScope scope, DashWidget widget) =>
            string.IsNullOrEmpty(widget.Group) ? scope : scope.WithSlot(widget.Slot);

        private readonly Dictionary<string, GraphHistory> _graphs = new Dictionary<string, GraphHistory>();

        /// <summary>A Graph widget's history, topped up with a sample of its values when one is due.</summary>
        private GraphHistory SampleGraph(DashWidget widget, WidgetType type, EvalScope scope)
        {
            if (!_graphs.TryGetValue(widget.Id, out GraphHistory history)) _graphs[widget.Id] = history = new GraphHistory();
            double Value(string key)
            {
                string raw = widget.Get(key, type.Prop(key)?.Default) ?? "";
                if (raw.Trim().Length == 0) return double.NaN;
                return Expr.Num(Expr.Evaluate(Strip(raw), scope));
            }
            history.Sample(scope.Now, Math.Max(1, Expr.Num(widget.Get("seconds", "10"))), Value("value"), Value("value2"));
            return history;
        }

        /// <summary>The history a Graph widget draws (sampling it if a sample is due).</summary>
        internal GraphHistory Graph(WidgetContext c) => SampleGraph(c.Widget, c.Type, c.Scope);

        private static int MapHash(TelemetrySnapshot t)
        {
            unchecked
            {
                int hash = (t.TrackMap?.Key?.GetHashCode() ?? 0) * 31 + (int)(t.MapProgress * 100) + t.PlayerCarIdx * 7;
                // Positions to a two-thousandth of a lap: smooth on screen without redrawing for nothing.
                foreach (float d in t.CarLapDist ?? new float[0]) hash = hash * 31 + (int)Math.Round(d * 2000);
                foreach (StandingsEntry r in t.Standings ?? new StandingsEntry[0]) hash = hash * 31 + (r.InPits ? 1 : 0) + r.CarIdx;
                return hash;
            }
        }

        private static int ArrayHash(double[] values)
        {
            unchecked
            {
                int hash = 0;
                if (values != null) foreach (double v in values) hash = hash * 31 + v.GetHashCode();
                return hash;
            }
        }

        private static int StandingsHash(TelemetrySnapshot t)
        {
            unchecked
            {
                int hash = 0;
                foreach (StandingsEntry r in t.Standings ?? new StandingsEntry[0])
                    hash = hash * 31 + r.CarIdx * 7 + r.Position + (int)Math.Round(r.GapToLeader * 10) * 13 + (int)(r.LastLap * 1000) + (int)(r.BestLap * 1000) + (r.InPits ? 1 : 0);
                return hash;
            }
        }

        private static int TyresHash(TelemetrySnapshot t)
        {
            unchecked
            {
                int hash = 0;
                foreach (TyreCorner c in t.Tyres ?? new TyreCorner[0])
                    hash = hash * 31 + (c.Temp + c.TempInner * 3 + c.TempOuter * 7 + c.Wear * 1000 + c.PressureKpa * 11).GetHashCode();
                return hash;
            }
        }

        private static int RelativeHash(TelemetrySnapshot t)
        {
            unchecked
            {
                int hash = 0;
                foreach (RelativeEntry r in t.Relatives ?? new RelativeEntry[0])
                    hash = hash * 31 + (r.Name?.GetHashCode() ?? 0) + (int)Math.Round(r.Gap * 10) * 7 + r.Position + (r.InPits ? 1 : 0);
                return hash;
            }
        }

        internal static bool IsFormula(string raw, PropDef prop) =>
            raw != null && (raw.StartsWith("=") || (prop.Kind == PropKind.Formula && raw.Trim().Length > 0));

        internal static string Strip(string raw) => raw != null && raw.StartsWith("=") ? raw.Substring(1) : raw;

        private void DrawWidget(Graphics g, DashWidget widget, EvalScope scope, int previousGear, float progress)
        {
            WidgetType type = WidgetCatalog.Find(widget.Type);
            if (type == null) return;
            scope = ForWidget(scope, widget);
            var context = new WidgetContext(this, g, scope, widget, type) { PreviousGear = previousGear, GearProgress = progress };
            string visible = widget.Get("visible", "");
            if (!string.IsNullOrWhiteSpace(visible) && !Expr.Bool(Expr.Evaluate(Strip(visible), scope))) return;

            double opacity = Math.Max(0, Math.Min(100, context.Num("opacity"))) / 100.0;
            if (opacity <= 0.001) return;
            bool flipX = context.Bool("flipX"), flipY = context.Bool("flipY");
            float rotate = (float)(context.Num("rotate") % 360);
            RectangleF b = context.B;
            LayerStyle style = LayerStyle.From(context);
            // A widget that can never change (no live data, no formulas) but couldn't go into the base
            // image, because something live sits beneath it, is kept as a finished picture: redrawing it
            // when something near it changes is then one image copy instead of drawing it again.
            bool still = CacheStatic && Still(widget);
            if (opacity >= 0.999 && !style.Any && !still)
            {
                GraphicsState state = g.Save();
                Turn(g, b, flipX, flipY, rotate);
                context.FlipX = flipX;
                context.FlipY = flipY;
                type.Draw(context);
                g.Restore(state);
                DrawTapDots(g, widget, scope);
                return;
            }

            // Partial opacity or Style effects: draw into a layer first, so overlapping parts fade together
            // and the effects can work on the drawn result. The finished picture (effects beneath, widget
            // on top) is kept per widget and reused while the widget's fingerprint holds, so a styled
            // widget that didn't change costs one image copy when something near it redraws.
            int hash = WidgetHash(widget, scope, previousGear, progress);
            if (!_styled.TryGetValue(widget.Id, out StyledPicture picture) || picture.Hash != hash)
            {
                picture?.Bitmap.Dispose();
                picture = Compose(widget, type, scope, previousGear, progress, style, b, flipX, flipY, rotate);
                picture.Hash = hash;
                _styled[widget.Id] = picture;
            }
            if (opacity >= 0.999) g.DrawImageUnscaled(picture.Bitmap, picture.At.X, picture.At.Y);
            else
                using (ImageAttributes attributes = LayerStyle.Fade((float)opacity))
                    g.DrawImage(picture.Bitmap, picture.At, 0, 0, picture.At.Width, picture.At.Height, GraphicsUnit.Pixel, attributes);
            DrawTapDots(g, widget, scope);
        }

        /// <summary>A row of small dots along the bottom of a tap zone: one per view, the current one lit.</summary>
        private static void DrawTapDots(Graphics g, DashWidget widget, EvalScope scope)
        {
            string name = widget.Get("tapName", "")?.Trim();
            if (string.IsNullOrEmpty(name) || !Expr.Bool(widget.Get("tapDots", "true"))) return;
            int views = TapViews(widget), view = 0;
            scope.Taps?.TryGetValue(name, out view);
            RectangleF b = widget.Bounds;
            float d = Math.Max(3, Math.Min(6, b.Height / 18)), gap = d * 1.4f;
            float total = views * d + (views - 1) * gap, x = b.X + (b.Width - total) / 2, y = b.Bottom - d - Math.Max(4, d * 1.4f);
            Color text = Expr.ParseHex(scope.Theme?.Text) ?? Color.White;
            for (int i = 0; i < views; i++)
                using (var brush = new SolidBrush(Color.FromArgb(i == view % views ? 210 : 60, text)))
                    g.FillEllipse(brush, x + i * (d + gap), y, d, d);
        }

        private sealed class StyledPicture
        {
            public Bitmap Bitmap;
            public Rectangle At;
            public int Hash;
        }

        private readonly Dictionary<string, StyledPicture> _styled = new Dictionary<string, StyledPicture>();

        /// <summary>Draws a widget into a layer exactly as big as it can reach, then lays its Style effects beneath it.</summary>
        private StyledPicture Compose(DashWidget widget, WidgetType type, EvalScope scope, int previousGear, float progress, LayerStyle style,
            RectangleF b, bool flipX, bool flipY, float rotate)
        {
            Rectangle at = Outward(Reach(widget));
            if (at.Width < 1 || at.Height < 1) at = new Rectangle((int)b.X, (int)b.Y, Math.Max(1, (int)b.Width), Math.Max(1, (int)b.Height));
            // Premultiplied pictures blend back exactly; see-through panels would lose precision otherwise.
            var composite = new Bitmap(at.Width, at.Height, PixelFormat.Format32bppPArgb);
            using (var layer = new Bitmap(at.Width, at.Height, PixelFormat.Format32bppPArgb))
            {
                Matrix place;
                using (Graphics lg = Graphics.FromImage(layer))
                {
                    DashKit.Prepare(lg);
                    lg.TranslateTransform(-at.X, -at.Y);
                    Turn(lg, b, flipX, flipY, rotate);
                    place = lg.Transform;
                    var inner = new WidgetContext(this, lg, scope, widget, type) { PreviousGear = previousGear, GearProgress = progress, FlipX = flipX, FlipY = flipY };
                    type.Draw(inner);
                }
                using (place)
                using (Graphics cg = Graphics.FromImage(composite))
                {
                    var local = new Rectangle(0, 0, at.Width, at.Height);
                    if (style.Any) style.Apply(cg, layer, b, place, local, 1f);
                    cg.DrawImageUnscaled(layer, 0, 0);
                }
            }
            return new StyledPicture { Bitmap = composite, At = at };
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

        private void DrawBackground(Graphics g)
        {
            LayoutTheme theme = _layout.Theme;
            Color bg = Expr.ParseHex(theme.Background) ?? DashKit.Background;
            Color bg2 = Expr.ParseHex(theme.Background2) ?? bg;
            Color accent = Expr.ParseHex(_accent) ?? Color.Teal;
            switch (theme.BackgroundStyle)
            {
                case "Solid":
                    g.Clear(bg);
                    break;
                case "Gradient":
                    using (var brush = new LinearGradientBrush(new Rectangle(0, -1, _width, _height + 2), bg2, bg, 90f)) g.FillRectangle(brush, 0, 0, _width, _height);
                    break;
                case "Aurora":
                {
                    // Soft colour blooms for the glass panels to sit over, like a blurred wallpaper.
                    using (var brush = new LinearGradientBrush(new Rectangle(0, -1, _width, _height + 2), bg2, bg, 90f)) g.FillRectangle(brush, 0, 0, _width, _height);
                    Color info = Expr.ParseHex(theme.Info) ?? Color.RoyalBlue, purple = Expr.ParseHex(theme.Purple) ?? Color.MediumPurple;
                    void Bloom(float cx, float cy, float rx, float ry, Color color, int alpha)
                    {
                        using (var path = new GraphicsPath())
                        {
                            path.AddEllipse(cx - rx, cy - ry, rx * 2, ry * 2);
                            using (var bloom = new PathGradientBrush(path) { CenterColor = Color.FromArgb(alpha, color), SurroundColors = new[] { Color.FromArgb(0, color) } })
                                g.FillPath(bloom, path);
                        }
                    }
                    Bloom(_width * 0.18f, _height * 0.12f, _width * 0.42f, _height * 0.62f, accent, 64);
                    Bloom(_width * 0.86f, _height * 0.92f, _width * 0.44f, _height * 0.66f, info, 56);
                    Bloom(_width * 0.78f, _height * 0.02f, _width * 0.32f, _height * 0.42f, purple, 40);
                    Bloom(_width * 0.5f, _height * 0.5f, _width * 0.3f, _height * 0.45f, accent, 22);
                    break;
                }
                case "Vignette":
                    g.Clear(bg2);
                    using (var path = new GraphicsPath())
                    {
                        path.AddRectangle(new RectangleF(-_width * 0.2f, -_height * 0.2f, _width * 1.4f, _height * 1.4f));
                        using (var brush = new PathGradientBrush(path) { CenterColor = bg2, SurroundColors = new[] { bg }, FocusScales = new PointF(0.55f, 0.4f) })
                            g.FillRectangle(brush, 0, 0, _width, _height);
                    }
                    break;
                default:
                    g.Clear(bg);
                    using (var path = new GraphicsPath())
                    {
                        path.AddEllipse(_width * 0.15f, -_height * 0.55f, _width * 0.7f, _height * 1.1f);
                        using (var bloom = new PathGradientBrush(path) { CenterColor = Color.FromArgb(22, accent), SurroundColors = new[] { Color.FromArgb(0, accent) } })
                            g.FillPath(bloom, path);
                    }
                    break;
            }
        }

        public Font Font(string family, float px, bool bold)
        {
            if (string.IsNullOrWhiteSpace(family) || family.Equals("theme", StringComparison.OrdinalIgnoreCase)) family = _layout.Theme.Font;
            px = Math.Max(1, Math.Min(600, px));
            string key = family + "|" + px.ToString("0.#") + "|" + bold;
            if (_fonts.TryGetValue(key, out Font font)) return font;
            FontStyle style = bold ? FontStyle.Bold : FontStyle.Regular;
            try
            {
                font = new Font(family, px, style, GraphicsUnit.Pixel);
                // GDI+ silently substitutes unknown families; fall back to the house face instead.
                if (!font.Name.Equals(family, StringComparison.OrdinalIgnoreCase) && !family.Equals(_layout.Theme.Font, StringComparison.OrdinalIgnoreCase))
                {
                    font.Dispose();
                    return Font("theme", px, bold);
                }
            }
            catch
            {
                font = new Font("Segoe UI", px, style, GraphicsUnit.Pixel);
            }
            _fonts[key] = font;
            return font;
        }

        public Bitmap Image(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (_images.TryGetValue(id, out Bitmap cached)) return cached;
            Bitmap bitmap = null;
            try
            {
                if (_layout.Images.TryGetValue(id, out string base64))
                    using (var stream = new MemoryStream(Convert.FromBase64String(base64)))
                    using (var loaded = new Bitmap(stream))
                        bitmap = new Bitmap(loaded);
            }
            catch { bitmap = null; }
            _images[id] = bitmap;
            return bitmap;
        }

        public void Dispose()
        {
            foreach (Font font in _fonts.Values) font.Dispose();
            _fonts.Clear();
            foreach (Bitmap image in _images.Values) image?.Dispose();
            _images.Clear();
            foreach (BaseLayer layer in _bases.Values) layer.Bitmap.Dispose();
            _bases.Clear();
            _background?.Dispose();
            _frame?.Dispose();
            _frame = null;
            ForgetStyled();
            _flags.Dispose();
            _waving?.Dispose();
            _flagFrame?.Dispose();
            _flagLayer?.Dispose();
        }
    }

    /// <summary>One widget being drawn: its bounds and typed access to its resolved properties.</summary>
    internal sealed class WidgetContext
    {
        private readonly Dictionary<string, object> _resolved = new Dictionary<string, object>();

        public WidgetContext(LayoutRenderer renderer, Graphics g, EvalScope scope, DashWidget widget, WidgetType type)
        {
            Renderer = renderer;
            G = g;
            Scope = scope;
            Widget = widget;
            Type = type;
            B = widget.Bounds;
        }

        public LayoutRenderer Renderer { get; }
        public Graphics G { get; }
        public EvalScope Scope { get; }
        public DashWidget Widget { get; }
        public WidgetType Type { get; }
        public RectangleF B { get; }
        public TelemetrySnapshot T => Scope.T;
        public int PreviousGear { get; set; }
        public float GearProgress { get; set; } = 1;
        /// <summary>The widget is mirrored; its text is turned back so it still reads the right way round.</summary>
        public bool FlipX { get; set; }
        public bool FlipY { get; set; }

        public string Raw(string key) => Widget.Get(key, Type.Prop(key)?.Default ?? "");

        public bool IsFormula(string key)
        {
            PropDef def = Type.Prop(key);
            return def != null && LayoutRenderer.IsFormula(Raw(key), def);
        }

        /// <summary>The property's value: a formula result, or the literal text.</summary>
        public object Value(string key)
        {
            if (_resolved.TryGetValue(key, out object cached)) return cached;
            string raw = Raw(key);
            object value = IsFormula(key) ? Expr.Evaluate(LayoutRenderer.Strip(raw), Scope) : raw;
            _resolved[key] = value;
            return value;
        }

        public double Num(string key)
        {
            object value = Value(key);
            if (value is string s && string.IsNullOrWhiteSpace(s)) value = Type.Prop(key)?.Default;
            double d = Expr.Num(value);
            return double.IsNaN(d) || double.IsInfinity(d) ? 0 : d;
        }

        public bool Bool(string key) => Expr.Bool(Value(key));
        public string Text(string key) => Expr.Text(Value(key));
        public string Choice(string key) => Text(key);

        public Color Color(string key, Color? fallback = null)
        {
            object value = Value(key);
            Color? color = Expr.Color(value, Scope);
            if (color.HasValue) return color.Value;
            if (fallback.HasValue) return fallback.Value;
            return Expr.Color(Type.Prop(key)?.Default, Scope) ?? System.Drawing.Color.White;
        }

        /// <summary>Null when the property is empty, for optional colors such as fills.</summary>
        public Color? OptionalColor(string key)
        {
            object value = Value(key);
            if (value is string s && string.IsNullOrWhiteSpace(s)) return null;
            return Expr.Color(value, Scope);
        }

        public Color Theme(string token) => Expr.Color(token, Scope) ?? System.Drawing.Color.White;

        public Font Font(float px, bool bold) => Renderer.Font(Text("font"), px, bold);
    }
}
