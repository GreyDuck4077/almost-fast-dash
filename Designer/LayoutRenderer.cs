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
        private WavingFlag _waving;
        /// <summary>Fixes the flags' breeze at one moment (milliseconds), for previews and tests; null follows the clock.</summary>
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
        }

        public DashLayout Layout => _layout;

        /// <summary>Widget ids whose last draw failed, with the reason; shown by the designer.</summary>
        public Dictionary<string, string> Errors { get; } = new Dictionary<string, string>();

        public EvalScope Scope(TelemetrySnapshot t, bool shiftFlashOn) => new EvalScope
        {
            T = t ?? TelemetrySnapshot.Disconnected,
            Units = _units,
            Theme = _layout.Theme,
            Accent = _accent,
            ShiftFlashOn = shiftFlashOn,
            Now = FixedNow ?? DateTime.Now
        };

        /// <summary>The screen to show: the first conditional screen that matches, else the first plain one.</summary>
        public DashScreen ActiveScreen(EvalScope scope)
        {
            foreach (DashScreen screen in _layout.Screens)
                if (!string.IsNullOrWhiteSpace(screen.Condition) && Expr.Bool(Expr.Evaluate(Strip(screen.Condition), scope)))
                    return screen;
            return _layout.Screens.FirstOrDefault(s => string.IsNullOrWhiteSpace(s.Condition)) ?? _layout.Screens[0];
        }

        public Bitmap Render(TelemetrySnapshot t, bool shiftFlashOn, int previousGear, float gearTransitionProgress) =>
            Render(t, shiftFlashOn, previousGear, gearTransitionProgress, null);

        /// <summary>Renders a frame; the designer passes the screen being edited.</summary>
        public Bitmap Render(TelemetrySnapshot t, bool shiftFlashOn, int previousGear, float gearTransitionProgress, DashScreen forceScreen)
        {
            t = t ?? TelemetrySnapshot.Disconnected;
            bool flagScreen = _layout.FullScreenFlags && t.Flag != RacingFlag.None && forceScreen == null;
            if (flagScreen) ForgetFrame();
            else if (CacheStatic && RedrawChangesOnly)
            {
                EvalScope live = Scope(t, shiftFlashOn);
                return RenderChanges(forceScreen ?? ActiveScreen(live), live, previousGear, gearTransitionProgress);
            }

            var bitmap = new Bitmap(_width, _height, PixelFormat.Format32bppArgb);
            if (flagScreen && _layout.WavingFlags)
            {
                (_waving ?? (_waving = new WavingFlag(_width, _height))).Draw(bitmap, t, FlagClock ?? Environment.TickCount);
                return bitmap;
            }
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                DashKit.Prepare(g);
                EvalScope scope = Scope(t, shiftFlashOn);
                if (flagScreen)
                {
                    _flags.Draw(g, scope.T);
                    return bitmap;
                }
                DashScreen screen = forceScreen ?? ActiveScreen(scope);
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
            ForgetFrame();
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
                    dirty.Add(Rectangle.Intersect(full, Outward(Reach(widget))));
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
                    // Areas are finished one at a time, so where two overlap the second simply redoes the overlap.
                    foreach (Rectangle region in dirty)
                    {
                        area += (long)region.Width * region.Height;
                        g.SetClip(region);
                        // Start the area again from the base image (background and fixed widgets), drawn 1:1.
                        g.CompositingMode = CompositingMode.SourceCopy;
                        g.DrawImageUnscaled(layer.Bitmap, 0, 0);
                        g.CompositingMode = CompositingMode.SourceOver;
                        foreach (DashWidget widget in screen.Widgets)
                        {
                            if (widget.Hidden || layer.Baked.Contains(widget.Id) || !Reach(widget).IntersectsWith(region)) continue;
                            try
                            {
                                DrawWidget(g, widget, scope, previousGear, progress);
                                Errors.Remove(widget.Id);
                            }
                            catch (Exception e) { Errors[widget.Id] = e.Message; }
                        }
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
        private static readonly HashSet<string> LiveTypes = new HashSet<string> { "gear", "relative", "blindspot", "flag", "progress", "trace", "deltatrace", "laphistory", "standings", "tyres" };

        private static bool IsStatic(DashWidget widget)
        {
            WidgetType type = WidgetCatalog.Find(widget.Type);
            if (type == null || LiveTypes.Contains(widget.Type)) return false;
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
            if (Math.Abs(Expr.Num(widget.Get("rotate", "0")) % 360) > 0.01 || IsFormula(widget.Get("rotate", "0"), new PropDef { Kind = PropKind.Number }))
            {
                // A turned widget can reach anywhere within the circle around its box.
                float half = (float)Math.Sqrt(b.Width * b.Width + b.Height * b.Height) / 2;
                b = new RectangleF(b.X + b.Width / 2 - half, b.Y + b.Height / 2 - half, half * 2, half * 2);
            }
            b.Inflate(margin, margin);
            return b;
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
                    liveBelow.Add(Reach(widget));
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
                if (_layout.FullScreenFlags && scope.T.Flag != RacingFlag.None)
                    return (int)scope.T.Flag * 7919 + (_layout.WavingFlags ? WavingFlag.Frame(FlagClock ?? Environment.TickCount) * 31 : 0);
                DashScreen screen = ActiveScreen(scope);
                int hash = screen.Id.GetHashCode();
                hash = hash * 31 + gearAnimationFrame;
                hash = hash * 31 + (scope.T.Connected ? 1 : 0);
                foreach (DashWidget widget in screen.Widgets)
                    if (!widget.Hidden) hash = hash * 31 + WidgetHash(widget, scope, 0, 1);
                return hash;
            }
        }

        /// <summary>
        /// A fingerprint of everything a widget would draw: its formula results and, for widgets
        /// that read telemetry directly, that data. Equal fingerprints mean identical pixels.
        /// </summary>
        private static int WidgetHash(DashWidget widget, EvalScope scope, int previousGear, float progress)
        {
            unchecked
            {
                WidgetType type = WidgetCatalog.Find(widget.Type);
                if (type == null) return 0;
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
                // Data-heavy widgets read more than their properties show.
                switch (widget.Type)
                {
                    case "gear": hash = hash * 31 + t.Gear * 7 + previousGear * 13 + (int)(progress * 1000) + t.MaxForwardGear * 3; break;
                    case "relative": hash = hash * 31 + RelativeHash(t); break;
                    case "laphistory": hash = hash * 31 + (t.RecentLapSeconds?.Length ?? 0) * 17 + (int)(t.BestLapSeconds * 1000) + ArrayHash(t.RecentLapSeconds); break;
                    case "progress": hash = hash * 31 + t.LapsCompleted * 13 + t.LapColorVersion + (int)t.SessionSecondsRemaining + t.TotalLaps * 7 + (int)t.ProgressMode; break;
                    case "blindspot": hash = hash * 31 + (int)t.LeftBlindSpot * 5 + (int)t.RightBlindSpot; break;
                    case "flag": hash = hash * 31 + (int)t.Flag; break;
                    case "standings": hash = hash * 31 + StandingsHash(t) + (int)(t.SessionBestLapSeconds * 1000); break;
                    case "tyres": hash = hash * 31 + TyresHash(t); break;
                    case "trace": hash = hash * 31 + t.TraceVersion; break;
                    case "deltatrace": hash = hash * 31 + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(t.DeltaTrace ?? new float[0]); break;
                }
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
            var context = new WidgetContext(this, g, scope, widget, type) { PreviousGear = previousGear, GearProgress = progress };
            string visible = widget.Get("visible", "");
            if (!string.IsNullOrWhiteSpace(visible) && !Expr.Bool(Expr.Evaluate(Strip(visible), scope))) return;

            double opacity = Math.Max(0, Math.Min(100, context.Num("opacity"))) / 100.0;
            if (opacity <= 0.001) return;
            bool flipX = context.Bool("flipX"), flipY = context.Bool("flipY");
            float rotate = (float)(context.Num("rotate") % 360);
            RectangleF b = context.B;
            if (opacity >= 0.999)
            {
                GraphicsState state = g.Save();
                Turn(g, b, flipX, flipY, rotate);
                context.FlipX = flipX;
                context.FlipY = flipY;
                type.Draw(context);
                g.Restore(state);
                return;
            }

            // Partial opacity: draw into a layer and blend it, so overlapping parts fade together. The
            // layer is centred on the widget and big enough for it at any angle.
            const int pad = 48;
            float span = Math.Abs(rotate) > 0.01f ? (float)Math.Sqrt(b.Width * b.Width + b.Height * b.Height) : 0;
            int lw = (int)Math.Ceiling(Math.Max(b.Width, span)) + pad * 2, lh = (int)Math.Ceiling(Math.Max(b.Height, span)) + pad * 2;
            float left = (float)Math.Round(b.X + b.Width / 2 - lw / 2f), top = (float)Math.Round(b.Y + b.Height / 2 - lh / 2f);
            using (var layer = new Bitmap(lw, lh, PixelFormat.Format32bppArgb))
            {
                using (Graphics lg = Graphics.FromImage(layer))
                {
                    DashKit.Prepare(lg);
                    lg.TranslateTransform(-left, -top);
                    Turn(lg, b, flipX, flipY, rotate);
                    var inner = new WidgetContext(this, lg, scope, widget, type) { PreviousGear = previousGear, GearProgress = progress, FlipX = flipX, FlipY = flipY };
                    type.Draw(inner);
                }
                var matrix = new ColorMatrix { Matrix33 = (float)opacity };
                using (var attributes = new ImageAttributes())
                {
                    attributes.SetColorMatrix(matrix);
                    g.DrawImage(layer, new Rectangle((int)left, (int)top, lw, lh), 0, 0, lw, lh, GraphicsUnit.Pixel, attributes);
                }
            }
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
            _flags.Dispose();
            _waving?.Dispose();
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
