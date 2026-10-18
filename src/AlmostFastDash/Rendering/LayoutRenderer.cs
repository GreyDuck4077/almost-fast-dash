using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace AlmostFastDash.Rendering
{
    /// <summary>
    /// Draws a designed (<see cref="DashLayout"/>) dashboard: every dashboard is one. It also
    /// fingerprints what a frame would show, so the screen is only redrawn when that changes.
    /// </summary>
    internal sealed partial class LayoutRenderer : IDashboardRenderer, IStateRenderer
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
        /// <summary>Fixes the flags' clock (fades, breeze, frame) at one moment in milliseconds, for previews and tests; null follows the
        /// clock.</summary>
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
            LastChanged = new Rectangle(0, 0, _width, _height);
            if (flags.Full)
            {
                _outputHoldsFrame = false;
                // The flag covers everything, so the dashboard isn't drawn; it is redrawn in full when the flag goes.
                ForgetFrame();
                Bitmap cover = SharedOutput ? Output() : new Bitmap(_width, _height, PixelFormat.Format32bppArgb);
                DrawFlag(cover, flags.Takeover, t, now);
                return cover;
            }
            Bitmap bitmap = RenderDashboard(t, shiftFlashOn, previousGear, gearTransitionProgress, forceScreen);
            if (flags.Frame == RacingFlag.None && flags.Alpha <= 0) return bitmap;
            // Drawn over the output, so the next frame can't start from it, and all of this one may have changed.
            _outputHoldsFrame = false;
            LastChanged = new Rectangle(0, 0, _width, _height);
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

        private Bitmap RenderDashboard(TelemetrySnapshot t, bool shiftFlashOn, int previousGear, float gearTransitionProgress,
            DashScreen forceScreen)
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
                _target = bitmap;
                _targetAreas = null;
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
                        Timings[widget.Name ?? widget.Id] = sum +
                            (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
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

        /// <summary>When set (by the dashboard audit), every piece of text drawn is noted here, at the size it was drawn.</summary>
        internal List<DrawnText> TextLog { get; set; }

        /// <summary>Leaves all text out (the audit's view of what is behind the text).</summary>
        internal bool SkipText { get; set; }

        // ------------------------------------------------------------------ redrawing only what changed

        /// <summary>
        /// With the static cache on, keep the last frame and redraw only the areas of widgets whose
        /// fingerprint changed (and whatever overlaps them), clipped to those areas. Same pixels as a
        /// full redraw, a fraction of the work: most of a dashboard holds still from frame to frame.
        /// </summary>
        public bool RedrawChangesOnly { get; set; } = true;

        /// <summary>
        /// The designer's view: widgets hidden right now by their Show when are drawn as a faint ghost instead of
        /// not at all, so a warning that only appears on pit road can still be seen, placed and styled.
        /// </summary>
        public bool GhostHidden { get; set; }
        private const double GhostOpacity = 0.28;

        /// <summary>
        /// Whether a widget hidden by this Show when shows as a ghost in the designer. A tap view's widgets don't: they're
        /// the panel's other faces, flipped through with Next in Properties, and would crowd the view that's showing.
        /// </summary>
        internal bool Ghosts(string visible) => GhostHidden && !IsTapView(visible);

        internal static bool IsTapView(string visible) => visible != null &&
            visible.IndexOf("tap(", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Widgets that show nothing at quiet moments: no flag out, no car alongside.</summary>
        internal static bool Idle(string type, WidgetContext c)
        {
            switch (type)
            {
                case "flag": return c.T.Flag == RacingFlag.None;
                case "blindspot": return (c.Choice("side") == "Right" ? c.T.RightBlindSpot : c.T.LeftBlindSpot) == BlindSpotZone.None;
                default: return false;
            }
        }

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

        /// <summary>A grouped widget sees its group's number as Slot, and its module options.</summary>
        internal static EvalScope ForWidget(EvalScope scope, DashWidget widget)
        {
            if (string.IsNullOrEmpty(widget.Group)) return scope;
            if (widget.Props != null)
                foreach (string key in widget.Props.Keys)
                    if (key.StartsWith(ModuleOptions.Prefix, StringComparison.Ordinal)) return scope.ForGroup(widget);
            return scope.WithSlot(widget.Slot);
        }

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

        internal static bool IsFormula(string raw, PropDef prop) =>
            raw != null && (raw.StartsWith("=") || (prop.Kind == PropKind.Formula && raw.Trim().Length > 0));

        internal static string Strip(string raw) => raw != null && raw.StartsWith("=") ? raw.Substring(1) : raw;

        /// <param name="steady">The widget's fingerprint held since the last frame: it's being redrawn only because
        /// something near it changed, so its finished picture can be laid down instead of drawing it again.</param>
        private void DrawWidget(Graphics g, DashWidget widget, EvalScope scope, int previousGear, float progress, bool steady = false)
        {
            WidgetType type = WidgetCatalog.Find(widget.Type);
            if (type == null) return;
            scope = ForWidget(scope, widget);
            var context = new WidgetContext(this, g, scope, widget, type) { PreviousGear = previousGear, GearProgress = progress };
            string visible = widget.Get("visible", "");
            bool ghost = false;
            if (!string.IsNullOrWhiteSpace(visible) && !Expr.Bool(Expr.Evaluate(Strip(visible), scope)))
            {
                if (!Ghosts(visible)) return;
                ghost = true;
            }
            // In the designer, a widget with nothing to show right now (no flag out, no car alongside) shows a ghost
            // of what it would.
            if (GhostHidden && !ghost && Idle(widget.Type, context)) ghost = context.Sample = true;

            double opacity = Math.Max(0, Math.Min(100, context.Num("opacity"))) / 100.0 * (ghost ? GhostOpacity : 1);
            if (opacity <= 0.001) return;
            bool flipX = context.Bool("flipX"), flipY = context.Bool("flipY");
            float rotate = (float)(context.Num("rotate") % 360);
            RectangleF b = context.B;
            LayerStyle style = LayerStyle.From(context);
            // Clipped to the layer below: drawn only where that layer is, and not at all while it's hidden.
            DashWidget clipBase = ClipBase(widget);
            if (clipBase != null && !Shows(clipBase, scope)) return;
            // A widget that can never change (no live data, no formulas) but couldn't go into the base
            // image, because something live sits beneath it, is kept as a finished picture: redrawing it
            // when something near it changes is then one image copy instead of drawing it again.
            bool still = CacheStatic && Still(widget);
            if (opacity >= 0.999 && !style.Any && !still && !(steady && CacheStatic) && clipBase == null)
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
            // widget that didn't change costs one image copy when something near it redraws. Plain widgets that held
            // still since the last frame are kept the same way.
            int hash = Fingerprint(widget, scope, previousGear, progress);
            if (!_styled.TryGetValue(widget.Id, out StyledPicture picture) || picture.Hash != hash)
            {
                picture?.Bitmap.Dispose();
                picture = Compose(widget, type, scope, previousGear, progress, style, b, flipX, flipY, rotate, context.Sample);
                if (clipBase != null) Mask(picture, clipBase, scope);
                picture.Hash = hash;
                _styled[widget.Id] = picture;
            }
            if (style.BlendMode != "Normal") Blend(g, picture, style.BlendMode, (float)opacity);
            else if (opacity >= 0.999) g.DrawImageUnscaled(picture.Bitmap, picture.At.X, picture.At.Y);
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

        private void DrawBackground(Graphics g)
        {
            LayoutTheme theme = _layout.Theme;
            Color bg = Expr.ParseHex(theme.Background) ?? DashKit.Background;
            Color bg2 = Expr.ParseHex(theme.Background2) ?? bg;
            Color accent = Expr.ParseHex(_accent) ?? Color.Teal;
            switch (theme.BackgroundStyle)
            {
                case "None":
                    // See-through: over the game, the game shows wherever no widget is drawn. On a wheel screen it's black.
                    g.Clear(Color.Transparent);
                    break;
                case "Solid":
                    g.Clear(bg);
                    break;
                case "Gradient":
                    using (var brush = new LinearGradientBrush(new Rectangle(0, -1, _width, _height + 2), bg2, bg,
                    90f)) g.FillRectangle(brush,
                    0, 0, _width, _height);
                    break;
                case "Aurora":
                {
                    // Soft color blooms for the glass panels to sit over, like a blurred wallpaper.
                    using (var brush = new LinearGradientBrush(new Rectangle(0, -1, _width, _height + 2), bg2, bg,
                        90f)) g.FillRectangle(brush,
                        0, 0, _width, _height);
                    Color info = Expr.ParseHex(theme.Info) ?? Color.RoyalBlue, purple = Expr.ParseHex(theme.Purple) ?? Color.MediumPurple;
                    void Bloom(float cx, float cy, float rx, float ry, Color color, int alpha)
                    {
                        using (var path = new GraphicsPath())
                        {
                            path.AddEllipse(cx - rx, cy - ry, rx * 2, ry * 2);
                            using (var bloom = new PathGradientBrush(path) { CenterColor = Color.FromArgb(alpha, color),
                                SurroundColors = new[] { Color.FromArgb(0, color) } })
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
                        using (var brush = new PathGradientBrush(path) { CenterColor = bg2, SurroundColors = new[] { bg },
                            FocusScales = new PointF(0.55f, 0.4f) })
                            g.FillRectangle(brush, 0, 0, _width, _height);
                    }
                    break;
                default:
                    g.Clear(bg);
                    using (var path = new GraphicsPath())
                    {
                        path.AddEllipse(_width * 0.15f, -_height * 0.55f, _width * 0.7f, _height * 1.1f);
                        using (var bloom = new PathGradientBrush(path) { CenterColor = Color.FromArgb(22, accent),
                            SurroundColors = new[] { Color.FromArgb(0, accent) } })
                            g.FillPath(bloom, path);
                    }
                    break;
            }
        }

        public Font Font(string family, float px, bool bold)
        {
            if (string.IsNullOrWhiteSpace(family) ||
                family.Equals("theme", StringComparison.OrdinalIgnoreCase)) family = _layout.Theme.Font;
            px = Math.Max(1, Math.Min(600, px));
            string key = family + "|" + px.ToString("0.#") + "|" + bold;
            if (_fonts.TryGetValue(key, out Font font)) return font;
            FontStyle style = bold ? FontStyle.Bold : FontStyle.Regular;
            try
            {
                font = new Font(family, px, style, GraphicsUnit.Pixel);
                // GDI+ silently substitutes unknown families; fall back to the house face instead.
                if (!font.Name.Equals(family, StringComparison.OrdinalIgnoreCase) &&
                    !family.Equals(_layout.Theme.Font, StringComparison.OrdinalIgnoreCase))
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
            _output?.Dispose();
            _output = null;
            ForgetStyled();
            _flags.Dispose();
            _waving?.Dispose();
            _flagFrame?.Dispose();
            _flagLayer?.Dispose();
        }
    }
}
