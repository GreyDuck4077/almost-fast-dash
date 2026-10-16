using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using DrawingBitmap = System.Drawing.Bitmap;

namespace AlmostFastOverlays
{
    /// <summary>
    /// The dashboard designer: a canvas for placing and styling widgets, with a
    /// toolbox, layers, theme and an inspector. It edits a working copy of the
    /// design and writes it back on Save .
    /// </summary>
    public partial class DesignerWindow : Window
    {
        private const double GridSize = 4, GridDraw = 16;
        /// <summary>The size of the screen being designed for.</summary>
        private double CanvasW => _layout?.Width ?? ScreenSizes.DefaultWidth;
        private double CanvasH => _layout?.Height ?? ScreenSizes.DefaultHeight;

        private readonly AppSettings _settings;
        private readonly DashboardSettings _dashboard;
        private readonly Action _onSaved;
        private readonly UnitSystem _units;

        private DashLayout _layout;
        private DashScreen _screen;
        private readonly List<DashWidget> _selection = new List<DashWidget>();
        private DashWidget _hover;

        private LayoutRenderer _renderer;
        private bool _rendererDirty = true;
        private WriteableBitmap _frame;
        private readonly DispatcherTimer _renderTimer, _animationTimer, _commitTimer, _liveSaveTimer;
        private bool _renderPending;

        // Undo history holds whole-design snapshots; designs are small.
        private readonly List<string> _undo = new List<string>(), _redo = new List<string>();
        private string _committed, _savedJson, _lastCommitLabel;
        private DateTime _lastCommitUtc;
        private string _pendingCommitLabel;
        private static string _clipboard;

        private enum DragMode { None, Move, Resize, Marquee, Rotate }
        private DragMode _drag;
        private Point _dragStart;
        private int _handle;
        private Dictionary<DashWidget, Rect> _originals;
        private Rect _marquee;
        private readonly List<Tuple<Point, Point>> _guides = new List<Tuple<Point, Point>>();
        private bool _dragChanged;

        private readonly SyntheticTelemetry _synthetic = new SyntheticTelemetry();
        private IRacingTelemetrySource _live;
        private DiagnosticLogger _liveLog;

        private double _zoom = 1;

        internal DesignerWindow(AppSettings settings, DashboardSettings dashboard, Action onSaved)
        {
            InitializeComponent();
            _settings = settings;
            _dashboard = dashboard;
            _onSaved = onSaved;
            _units = new UnitSystem(settings.General);
            SourceInitialized += (s, e) => UseDarkTitleBar(); GlassBackdrop.Apply(this, 0xC8);

            if (_dashboard.Design == null) _dashboard.Design = DesignTemplates.Blank();
            _layout = SettingsStore.Clone(_dashboard.Design);
            _layout.Normalize();
            _screen = _layout.Screens[0];
            _committed = _savedJson = Snapshot();
            _savedAccent = _dashboard.AccentColor;
            Title = _dashboard.Name + " · Screen designer";
            TitleText.Text = _dashboard.Name;

            _renderTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(12) };
            _renderTimer.Tick += (s, e) => { _renderTimer.Stop(); if (_renderPending) RenderNow(); };
            _animationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            _animationTimer.Tick += (s, e) => RenderNow();
            _commitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            _commitTimer.Tick += (s, e) => { _commitTimer.Stop(); if (_pendingCommitLabel != null) Commit(_pendingCommitLabel); };
            // Every change saves itself shortly after it's made, like the rest of the app.
            _liveSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _liveSaveTimer.Tick += (s, e) => { _liveSaveTimer.Stop(); SaveDesign(true); };

            FitCanvasToScreen();

            BuildToolbox();
            BuildScreensCombo();
            BuildPreviewCombo();
            BuildThemeEditor();
            RefreshLayers();
            RefreshInspector();
            UpdateUndoButtons();
            UpdateSaveState();

            PreviewKeyDown += Window_PreviewKeyDown;
            InitTools();
            Loaded += (s, e) => { ZoomFit(); RenderNow(); Overlay.Focus(); };
            Closing += Window_Closing;
            Closed += (s, e) => { StopLive(); _animationTimer.Stop(); _renderer?.Dispose(); };
            SizeChanged += (s, e) => { if (_fitOnResize) ZoomFit(); };
            // In the background the preview slows to a few frames a second; minimized it stops.
            Activated += (s, e) => PaceAnimation();
            Deactivated += (s, e) => PaceAnimation();
            StateChanged += (s, e) => PaceAnimation();
        }

        private bool _fitOnResize = true;

        /// <summary>Which view each tap zone shows in the preview, kept while the design is edited.</summary>
        private readonly Dictionary<string, int> _designTaps = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Sizes the frame, the bezel, the overlay and the grid to the screen being designed for.</summary>
        private void FitCanvasToScreen()
        {
            bool resized = _frame != null;
            _frame = new WriteableBitmap((int)CanvasW, (int)CanvasH, 96, 96, PixelFormats.Bgra32, null);
            Frame.Source = _frame;
            Bezel.Width = Frame.Width = Overlay.Width = CanvasW;
            Bezel.Height = Frame.Height = Overlay.Height = CanvasH;
            if (_gridRect != null) { _gridRect.Width = CanvasW; _gridRect.Height = CanvasH; }
            if (resized && IsLoaded) ZoomFit();
        }

        // ==================================================================
        // Rendering
        // ==================================================================

        /// <summary>Asks for a redraw after something changed: the design (so cached images go) or the overlay.</summary>
        private void RequestRender()
        {
            _renderPending = true;
            _designChanged = true;
            if (!_renderTimer.IsEnabled) _renderTimer.Start();
        }

        private bool _designChanged = true;
        private string _shownErrors = "";

        /// <summary>Grey squares, 16 px, behind a design whose background is see-through.</summary>
        private static readonly Brush SeeThrough = CreateSeeThrough();

        private static Brush CreateSeeThrough()
        {
            var squares = new GeometryGroup();
            squares.Children.Add(new RectangleGeometry(new Rect(0, 0, 16, 16)));
            squares.Children.Add(new RectangleGeometry(new Rect(16, 16, 16, 16)));
            var drawing = new DrawingGroup();
            drawing.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(38, 42, 48)), null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            drawing.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(54, 59, 66)), null, squares));
            var brush = new DrawingBrush(drawing) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 32, 32), ViewportUnits = BrushMappingMode.Absolute };
            brush.Freeze();
            return brush;
        }

        private void RenderNow()
        {
            _renderPending = false;
            // The design's screen size can change (resized, or an undo); the canvas follows it.
            if (_frame == null || _frame.PixelWidth != (int)CanvasW || _frame.PixelHeight != (int)CanvasH)
            {
                FitCanvasToScreen();
                _rendererDirty = true;
            }
            if (_rendererDirty || _renderer == null)
            {
                _renderer?.Dispose();
                // The same redraw-what-changed renderer as the wheel; every edit invalidates it, so an
                // animated preview only redraws the moving parts between edits.
                _renderer = new LayoutRenderer((int)CanvasW, (int)CanvasH,
                    new DashboardSettings { Layout = Layouts.Custom, Design = _layout, AccentColor = _dashboard.AccentColor }, _units) { Taps = _designTaps };
                _rendererDirty = false;
                _designChanged = true;
            }
            bool edited = _designChanged;
            if (edited)
            {
                _renderer.Invalidate();
                // A see-through background shows as a checkerboard, as in image editors: whatever is behind it shows there.
                Bezel.Background = _layout.Theme.BackgroundStyle == "None" ? SeeThrough : null;
            }
            _designChanged = false;

            TelemetrySnapshot snapshot = PreviewSnapshot(out bool flash, out int previousGear, out float progress);
            using (DrawingBitmap bitmap = _renderer.Render(snapshot, flash, previousGear, progress, _screen))
                CopyToFrame(bitmap);
            // Animation frames leave the selection overlay and layer list alone unless an error came or went.
            string errors = string.Join(",", _renderer.Errors.Keys.OrderBy(k => k));
            if (edited || errors != _shownErrors)
            {
                _shownErrors = errors;
                UpdateOverlay();
                MarkLayerErrors();
            }
            UpdateFormulaResults(snapshot, flash);
        }

        /// <summary>The animated preview runs at full rate only while the designer is the window in use.</summary>
        private void PaceAnimation()
        {
            bool moving = PreviewState == "Animated lap" || PreviewState == "Live iRacing" || PreviewState == "Shift point" || PreviewState.EndsWith(" flag");
            if (!moving || WindowState == WindowState.Minimized) { _animationTimer.Stop(); return; }
            _animationTimer.Interval = TimeSpan.FromMilliseconds(IsActive ? 33 : 250);
            if (!_animationTimer.IsEnabled) _animationTimer.Start();
        }

        private void CopyToFrame(DrawingBitmap bitmap)
        {
            var rect = new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
            System.Drawing.Imaging.BitmapData data = bitmap.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                _frame.WritePixels(new Int32Rect(0, 0, bitmap.Width, bitmap.Height), data.Scan0, data.Stride * bitmap.Height, data.Stride);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        /// <summary>Renders a design to an image for gallery cards and the template picker.</summary>
        internal static BitmapSource RenderThumbnail(DashboardSettings dashboard, UnitSystem units, TelemetrySnapshot snapshot = null)
        {
            snapshot = snapshot ?? IRacingTelemetrySource.CreatePreviewSnapshot();
            System.Drawing.Size size = dashboard.ScreenSize();
            using (IDashboardRenderer renderer = DashboardRendererFactory.Create(size.Width, size.Height, dashboard, units))
            using (DrawingBitmap bitmap = renderer.Render(snapshot, false, snapshot.Gear, 1f))
            {
                var data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                try
                {
                    BitmapSource source = BitmapSource.Create(bitmap.Width, bitmap.Height, 96, 96, PixelFormats.Bgra32, null, data.Scan0, data.Stride * bitmap.Height, data.Stride);
                    source.Freeze();
                    return source;
                }
                finally
                {
                    bitmap.UnlockBits(data);
                }
            }
        }

        // ==================================================================
        // Preview data
        // ==================================================================

        private static readonly string[] PreviewStates =
        {
            "Animated lap", "Live iRacing", "Racing", "Shift point", "Pit lane", "Cars alongside", "Not connected",
            "Green flag", "Yellow flag", "White flag", "Red flag", "Blue flag", "Black flag", "Repair flag", "Checkered flag"
        };

        private void BuildPreviewCombo()
        {
            PreviewCombo.ItemsSource = PreviewStates;
            PreviewCombo.SelectedIndex = 0;
        }

        private string PreviewState => PreviewCombo.SelectedItem as string ?? "Racing";

        private void PreviewCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded && _frame == null) return;
            if (PreviewState == "Live iRacing") StartLive(); else StopLive();
            PaceAnimation();
            RequestRender();
        }

        private void StartLive()
        {
            if (_live != null) return;
            _liveLog = new DiagnosticLogger(false);
            _live = new IRacingTelemetrySource(_liveLog, _dashboard);
            _live.Start();
        }

        private void StopLive()
        {
            _live?.Dispose();
            _live = null;
            _liveLog?.Dispose();
            _liveLog = null;
        }

        private TelemetrySnapshot PreviewSnapshot(out bool flash, out int previousGear, out float progress)
        {
            flash = false;
            progress = 1;
            TelemetrySnapshot snapshot;
            switch (PreviewState)
            {
                case "Animated lap":
                    snapshot = _synthetic.Next(out flash, out previousGear, out progress);
                    return snapshot;
                case "Live iRacing":
                    snapshot = _live?.GetSnapshot() ?? TelemetrySnapshot.Disconnected;
                    flash = snapshot.ShiftAlert && ((Environment.TickCount / 140) & 1) == 0;
                    StatusText.Text = snapshot.Connected ? "Showing live iRacing data." : "Waiting for iRacing… start a session to see live data.";
                    break;
                case "Shift point":
                    snapshot = ShiftSnapshot();
                    flash = ((Environment.TickCount / 140) & 1) == 0;
                    break;
                case "Pit lane": snapshot = IRacingTelemetrySource.CreatePitLanePreviewSnapshot(); break;
                case "Cars alongside": snapshot = IRacingTelemetrySource.CreateSpotterPreviewSnapshot(); break;
                case "Not connected": snapshot = TelemetrySnapshot.Disconnected; break;
                case "Green flag": snapshot = FlagPreview(RacingFlag.Green); break;
                case "Yellow flag": snapshot = FlagPreview(RacingFlag.Yellow); break;
                case "White flag": snapshot = FlagPreview(RacingFlag.White); break;
                case "Red flag": snapshot = FlagPreview(RacingFlag.Red); break;
                case "Blue flag": snapshot = FlagPreview(RacingFlag.BlueYellowStripe); break;
                case "Black flag": snapshot = FlagPreview(RacingFlag.Black); break;
                case "Repair flag": snapshot = FlagPreview(RacingFlag.Meatball); break;
                case "Checkered flag": snapshot = FlagPreview(RacingFlag.Checkered); break;
                default: snapshot = IRacingTelemetrySource.CreatePreviewSnapshot(); break;
            }
            previousGear = snapshot.Gear;
            return snapshot;
        }

        private const int FlagPreviewLoop = 9000, FlagPreviewPulse = 2000;
        private string _flagPreviewState;
        private int _flagPreviewStart, _flagPreviewRound;

        /// <summary>
        /// A flag as a race shows it, over and over: green, yellow and white alert for a couple of seconds and
        /// then hand the screen back (yellow and white keep their frame); flags that stay out alert, then step
        /// aside to their frame; red and checkered stay.
        /// </summary>
        private TelemetrySnapshot FlagPreview(RacingFlag flag)
        {
            if (_flagPreviewState != PreviewState)
            {
                _flagPreviewState = PreviewState;
                _flagPreviewStart = Environment.TickCount;
                _flagPreviewRound = -1;
            }
            int elapsed = Environment.TickCount - _flagPreviewStart, round = elapsed / FlagPreviewLoop;
            if (round != _flagPreviewRound)
            {
                _flagPreviewRound = round;
                if (FlagTimeline.StaysOut(flag)) _renderer?.ResetFlags();
            }
            bool pulse = flag == RacingFlag.Green || flag == RacingFlag.Yellow || flag == RacingFlag.White;
            TelemetrySnapshot snapshot = IRacingTelemetrySource.CreatePreviewSnapshot(pulse && elapsed % FlagPreviewLoop > FlagPreviewPulse ? RacingFlag.None : flag);
            snapshot.CautionActive = flag == RacingFlag.Yellow;
            snapshot.WhiteFlagActive = flag == RacingFlag.White;
            return snapshot;
        }

        private static TelemetrySnapshot ShiftSnapshot()
        {
            var snapshot = new TelemetrySnapshot(
                true, 4, 148, RacingFlag.None, BlindSpotZone.None, BlindSpotZone.None, 1.0,
                SessionProgressMode.LapRace, 0, 0, 8, 18, 4, 2, 24.3, 58.104, 0.46,
                "J. MILLER", -0.46, "M. CARTER", 0.82, 22, false, 87.932, 87.521, 88.204, new bool[18], 0, 6, 7950);
            return IRacingTelemetrySource.WithPreviewExtras(snapshot);
        }

        // ==================================================================
        // Overlay: grid, selection, handles, guides
        // ==================================================================

        private double Px => 1 / _zoom;

        private void UpdateOverlay()
        {
            Overlay.Children.Clear();
            var accent = (SolidColorBrush)FindResource("Brush.Accent");
            if (GridToggle.IsChecked == true) Overlay.Children.Add(GridRectangle());

            if (OutlineToggle.IsChecked == true)
                foreach (DashWidget w in _screen.Widgets.Where(w => !w.Hidden))
                    AddRect(RectOf(w), new SolidColorBrush(Color.FromArgb(110, 160, 170, 190)), 1, true);

            foreach (DashWidget w in _screen.Widgets.Where(w => !w.Hidden && _renderer != null && _renderer.Errors.ContainsKey(w.Id)))
                AddRect(RectOf(w), Brushes.OrangeRed, 1.5, true);

            if (_hover != null && !_selection.Contains(_hover) && _drag == DragMode.None)
                AddRect(RectOf(_hover), new SolidColorBrush(Color.FromArgb(150, accent.Color.R, accent.Color.G, accent.Color.B)), 1, false);

            foreach (DashWidget w in _selection)
            {
                // A turned widget's outline turns with it.
                AddRect(RectOf(w), accent, 1.5, false);
                double angle = Angle(w);
                if (angle != 0)
                {
                    Rect r = RectOf(w);
                    ((Rectangle)Overlay.Children[Overlay.Children.Count - 1]).RenderTransform = new RotateTransform(angle, r.Width / 2, r.Height / 2);
                }
            }

            if (_selection.Count > 1)
            {
                Rect around = Union(_selection);
                AddRect(around, accent, 1, true);
                if (!_selection.Any(w => w.Locked))
                    foreach (Point p in HandlePoints(around))
                    {
                        double size = 7 * Px;
                        var handle = new Rectangle { Width = size, Height = size, Fill = accent, Stroke = Brushes.White, StrokeThickness = 1 * Px, IsHitTestVisible = false };
                        Canvas.SetLeft(handle, p.X - size / 2);
                        Canvas.SetTop(handle, p.Y - size / 2);
                        Overlay.Children.Add(handle);
                    }
            }

            if (_selection.Count == 1 && !_selection[0].Locked && _editPath == null)
            {
                Rect r = RectOf(_selection[0]);
                if (CanRotate(_selection[0]))
                {
                    Point knob = RotateHandlePoint(_selection[0]);
                    Point stem = Turn(new Point(r.X + r.Width / 2, r.Y), Center(r), Angle(_selection[0]));
                    Overlay.Children.Add(new Line { X1 = stem.X, Y1 = stem.Y, X2 = knob.X, Y2 = knob.Y, Stroke = accent, StrokeThickness = 1.2 * Px, IsHitTestVisible = false });
                    double d = 11 * Px;
                    var dial = new Ellipse { Width = d, Height = d, Fill = Brushes.White, Stroke = accent, StrokeThickness = 1.5 * Px, ToolTip = "Drag to rotate · Shift snaps to 15°" };
                    Canvas.SetLeft(dial, knob.X - d / 2);
                    Canvas.SetTop(dial, knob.Y - d / 2);
                    Overlay.Children.Add(dial);
                }
                foreach (Point p in HandlePoints(_selection[0]))
                {
                    double size = 8 * Px;
                    var handle = new Rectangle { Width = size, Height = size, Fill = Brushes.White, Stroke = accent, StrokeThickness = 1.5 * Px };
                    Canvas.SetLeft(handle, p.X - size / 2);
                    Canvas.SetTop(handle, p.Y - size / 2);
                    Overlay.Children.Add(handle);
                }
                var size2 = new Border
                {
                    Background = accent, CornerRadius = new CornerRadius(4 * Px), Padding = new Thickness(5 * Px, 1 * Px, 5 * Px, 1 * Px),
                    Child = new TextBlock { Text = Math.Round(r.Width) + " × " + Math.Round(r.Height), FontSize = 12 * Px, Foreground = (Brush)FindResource("Brush.OnAccent"), FontWeight = FontWeights.SemiBold }
                };
                size2.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(size2, r.X + r.Width / 2 - size2.DesiredSize.Width / 2);
                Canvas.SetTop(size2, r.Bottom + 6 * Px);
                Overlay.Children.Add(size2);
            }

            foreach (Tuple<Point, Point> guide in _guides)
                Overlay.Children.Add(new Line
                {
                    X1 = guide.Item1.X, Y1 = guide.Item1.Y, X2 = guide.Item2.X, Y2 = guide.Item2.Y,
                    Stroke = new SolidColorBrush(Color.FromRgb(255, 64, 160)), StrokeThickness = 1 * Px
                });

            if (_drag == DragMode.Marquee)
            {
                var rect = new Rectangle
                {
                    Width = _marquee.Width, Height = _marquee.Height, Stroke = accent, StrokeThickness = 1 * Px,
                    Fill = new SolidColorBrush(Color.FromArgb(30, accent.Color.R, accent.Color.G, accent.Color.B))
                };
                Canvas.SetLeft(rect, _marquee.X);
                Canvas.SetTop(rect, _marquee.Y);
                Overlay.Children.Add(rect);
            }
            AddDrawingPreview(accent);
            AddPenPreview(accent);
            AddEditPreview(accent);
        }

        private Rectangle _gridRect;

        private Rectangle GridRectangle()
        {
            if (_gridRect != null) return _gridRect;
            var drawing = new GeometryDrawing
            {
                Pen = new Pen(new SolidColorBrush(Color.FromArgb(13, 255, 255, 255)), 0.5),
                Geometry = new RectangleGeometry(new Rect(0, 0, GridDraw, GridDraw))
            };
            _gridRect = new Rectangle
            {
                Width = CanvasW, Height = CanvasH, IsHitTestVisible = false,
                Fill = new DrawingBrush(drawing) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, GridDraw, GridDraw), ViewportUnits = BrushMappingMode.Absolute }
            };
            return _gridRect;
        }

        private void AddRect(Rect r, Brush brush, double thickness, bool dashed)
        {
            var rect = new Rectangle { Width = Math.Max(0, r.Width), Height = Math.Max(0, r.Height), Stroke = brush, StrokeThickness = thickness * Px, IsHitTestVisible = false };
            if (dashed) rect.StrokeDashArray = new DoubleCollection { 4, 3 };
            Canvas.SetLeft(rect, r.X);
            Canvas.SetTop(rect, r.Y);
            Overlay.Children.Add(rect);
        }

        private static Rect RectOf(DashWidget w) => new Rect(w.X, w.Y, w.W, w.H);

        private static Rect Union(IEnumerable<DashWidget> widgets)
        {
            Rect? union = null;
            foreach (DashWidget w in widgets) union = union.HasValue ? Rect.Union(union.Value, RectOf(w)) : RectOf(w);
            return union ?? Rect.Empty;
        }

        private static Point[] HandlePoints(Rect r) => new[]
        {
            new Point(r.Left, r.Top), new Point(r.Left + r.Width / 2, r.Top), new Point(r.Right, r.Top), new Point(r.Right, r.Top + r.Height / 2),
            new Point(r.Right, r.Bottom), new Point(r.Left + r.Width / 2, r.Bottom), new Point(r.Left, r.Bottom), new Point(r.Left, r.Top + r.Height / 2)
        };

        private static readonly Cursor[] HandleCursors =
            { Cursors.SizeNWSE, Cursors.SizeNS, Cursors.SizeNESW, Cursors.SizeWE, Cursors.SizeNWSE, Cursors.SizeNS, Cursors.SizeNESW, Cursors.SizeWE };

        /// <summary>A widget's fixed rotation in degrees; 0 when it isn't turned or a formula turns it.</summary>
        private static double Angle(DashWidget w)
        {
            string raw = w.Get("rotate", "0") ?? "0";
            if (raw.StartsWith("=")) return 0;
            double a = Expr.Num(raw) % 360;
            return double.IsNaN(a) || Math.Abs(a) < 0.01 ? 0 : a;
        }

        private static Point Center(Rect r) => new Point(r.X + r.Width / 2, r.Y + r.Height / 2);

        private static Point Turn(Point p, Point about, double degrees)
        {
            if (degrees == 0) return p;
            double a = degrees * Math.PI / 180, cos = Math.Cos(a), sin = Math.Sin(a), dx = p.X - about.X, dy = p.Y - about.Y;
            return new Point(about.X + dx * cos - dy * sin, about.Y + dx * sin + dy * cos);
        }

        /// <summary>The selection handles of a widget, turned with it.</summary>
        private static Point[] HandlePoints(DashWidget w)
        {
            Rect r = RectOf(w);
            double angle = Angle(w);
            return HandlePoints(r).Select(p => Turn(p, Center(r), angle)).ToArray();
        }

        /// <summary>Where the rotate handle sits: above the top edge's middle, turned with the widget.</summary>
        private Point RotateHandlePoint(DashWidget w)
        {
            Rect r = RectOf(w);
            return Turn(new Point(r.X + r.Width / 2, r.Y - 26 * Px), Center(r), Angle(w));
        }

        /// <summary>The rotate handle shows unless a formula turns the widget.</summary>
        private static bool CanRotate(DashWidget w) => !(w.Get("rotate", "0") ?? "").StartsWith("=");

        private const int RotateHandle = 8;

        private int HandleAt(Point p)
        {
            if (_selection.Count > 1)
            {
                // Several selected: handles around them all.
                if (_selection.Any(w => w.Locked)) return -1;
                Point[] around = HandlePoints(Union(_selection));
                for (int i = 0; i < around.Length; i++)
                    if (Math.Abs(around[i].X - p.X) <= 7 * Px && Math.Abs(around[i].Y - p.Y) <= 7 * Px) return i;
                return -1;
            }
            if (_selection.Count != 1 || _selection[0].Locked) return -1;
            if (CanRotate(_selection[0]))
            {
                Point knob = RotateHandlePoint(_selection[0]);
                if (Math.Abs(knob.X - p.X) <= 8 * Px && Math.Abs(knob.Y - p.Y) <= 8 * Px) return RotateHandle;
            }
            Point[] points = HandlePoints(_selection[0]);
            double reach = 7 * Px;
            for (int i = 0; i < points.Length; i++)
                if (Math.Abs(points[i].X - p.X) <= reach && Math.Abs(points[i].Y - p.Y) <= reach) return i;
            return -1;
        }

        /// <summary>
        /// False when a widget's Show-when condition hides it in the current preview.
        /// Hidden widgets are skipped on the canvas (pick them in Layers) unless Outlines is on.
        /// </summary>
        private bool ShownNow(DashWidget w)
        {
            string visible = w.Get("visible", "");
            if (string.IsNullOrWhiteSpace(visible) || OutlineToggle.IsChecked == true) return true;
            return Expr.Bool(Expr.Evaluate(LayoutRenderer.Strip(visible), CurrentScope()));
        }

        private DashWidget WidgetAt(Point p)
        {
            double pad = 3 * Px;
            for (int i = _screen.Widgets.Count - 1; i >= 0; i--)
            {
                DashWidget w = _screen.Widgets[i];
                if (w.Hidden || w.Locked || !ShownNow(w)) continue;
                Rect r = RectOf(w);
                r.Inflate(pad, pad);
                // A turned widget is hit where it is drawn: test the point turned back into the widget's own frame.
                if (r.Contains(Turn(p, Center(r), -Angle(w)))) return w;
            }
            return null;
        }

        // ==================================================================
        // Mouse
        // ==================================================================

        private void Overlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Overlay.Focus();
            Keyboard.Focus(Overlay);
            // A path's points being edited come first; then drawing, panning and zooming tools; Move goes on below.
            if (ActiveTool == Tool.Move && EditMouseDown(e)) return;
            if (ToolMouseDown(e)) return;
            Point p = e.GetPosition(Overlay);
            _dragStart = p;
            _dragChanged = false;
            bool additive = (Keyboard.Modifiers & (ModifierKeys.Shift | ModifierKeys.Control)) != 0;

            int handle = HandleAt(p);
            if (handle == RotateHandle)
            {
                _drag = DragMode.Rotate;
                _originals = _selection.ToDictionary(w => w, RectOf);
                Overlay.CaptureMouse();
                return;
            }
            if (handle >= 0)
            {
                _drag = DragMode.Resize;
                _handle = handle;
                _originals = _selection.ToDictionary(w => w, RectOf);
                Overlay.CaptureMouse();
                return;
            }

            DashWidget hit = WidgetAt(p);
            if (hit != null)
            {
                // A click takes a widget's whole group; a double-click picks out just that widget.
                if (e.ClickCount == 2)
                {
                    // A path opens for its points to be edited; anything else picks out that widget.
                    if (hit.Type == "path") { BeginEditPath(hit); return; }
                    Select(hit);
                    FocusPrimaryEditor();
                    return;
                }
                List<DashWidget> members = GroupOf(hit);
                if (additive)
                {
                    if (_selection.Contains(hit)) _selection.RemoveAll(members.Contains); else _selection.AddRange(members.Where(m => !_selection.Contains(m)));
                    SelectionChanged();
                }
                else if (!_selection.Contains(hit))
                {
                    _selection.Clear();
                    _selection.AddRange(members);
                    SelectionChanged();
                }
                if (_selection.Contains(hit))
                {
                    _drag = DragMode.Move;
                    _originals = _selection.Where(w => !w.Locked).ToDictionary(w => w, RectOf);
                    Overlay.CaptureMouse();
                }
                return;
            }

            if (!additive && _selection.Count > 0) { _selection.Clear(); SelectionChanged(); }
            _drag = DragMode.Marquee;
            _marquee = new Rect(p, p);
            Overlay.CaptureMouse();
        }

        private void Overlay_MouseMove(object sender, MouseEventArgs e)
        {
            Point p = e.GetPosition(Overlay);
            CursorText.Text = $"x {Math.Round(p.X)}   y {Math.Round(p.Y)}";
            if (EditMouseMove(e)) return;
            if (ToolMouseMove(e)) return;
            bool free = (Keyboard.Modifiers & ModifierKeys.Alt) != 0 || SnapToggle.IsChecked != true;

            switch (_drag)
            {
                case DragMode.None:
                {
                    int handle = HandleAt(p);
                    // Handles keep pointing the way they face on a turned widget.
                    int turned = handle < 0 || handle == RotateHandle ? -1 : ((handle + (int)Math.Round(Angle(_selection[0]) / 45)) % 8 + 8) % 8;
                    Overlay.Cursor = handle == RotateHandle ? Cursors.Hand : handle >= 0 ? HandleCursors[turned] : WidgetAt(p) != null ? Cursors.SizeAll : Cursors.Arrow;
                    DashWidget hover = WidgetAt(p);
                    if (hover != _hover) { _hover = hover; UpdateOverlay(); }
                    return;
                }
                case DragMode.Move:
                    DoMove(p, free);
                    break;
                case DragMode.Resize:
                    DoResize(p, free, (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
                    break;
                case DragMode.Rotate:
                {
                    // The angle from the widget's middle to the pointer, measured from straight up; Shift snaps to 15°.
                    DashWidget w = _selection[0];
                    Point middle = Center(_originals[w]);
                    double angle = Math.Atan2(p.Y - middle.Y, p.X - middle.X) * 180 / Math.PI + 90;
                    angle = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? Math.Round(angle / 15) * 15 : Math.Round(angle);
                    angle = ((angle + 180) % 360 + 360) % 360 - 180;
                    w.Set("rotate", angle.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    _dragChanged = true;
                    CursorText.Text = "Rotate " + angle + "°";
                    break;
                }
                case DragMode.Marquee:
                    _marquee = new Rect(_dragStart, p);
                    _selection.Clear();
                    _selection.AddRange(_screen.Widgets.Where(w => !w.Hidden && !w.Locked && ShownNow(w) && _marquee.IntersectsWith(RectOf(w))));
                    UpdateOverlay();
                    return;
            }
            RequestRender();
            RefreshPositionFields();
        }

        private void Overlay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (EditMouseUp()) return;
            if (ToolMouseUp()) return;
            DragMode mode = _drag;
            _drag = DragMode.None;
            Overlay.ReleaseMouseCapture();
            _guides.Clear();
            if (mode == DragMode.Marquee) SelectionChanged();
            else if ((mode == DragMode.Move || mode == DragMode.Resize || mode == DragMode.Rotate) && _dragChanged)
            {
                Commit(mode == DragMode.Move ? "Move" : mode == DragMode.Rotate ? "Rotate" : "Resize");
                RefreshInspector();
            }
            UpdateOverlay();
        }

        private void Overlay_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            Point p = e.GetPosition(Overlay);
            DashWidget hit = WidgetAt(p);
            if (hit != null && !_selection.Contains(hit)) Select(hit);
            var menu = new ContextMenu();
            if (_selection.Count > 0)
            {
                if (_selection.Count == 1 && _selection[0].Type == "path")
                {
                    DashWidget path = _selection[0];
                    menu.Items.Add(MenuItem("Edit points", "Enter", () => BeginEditPath(path)));
                    menu.Items.Add(new Separator());
                }
                else if (_selection.Count == 1 && _selection[0].Type == "shape" && PathNodes.FromShape(_selection[0].Get("shape", "Rectangle"), 6, 0.5f, out _) != null)
                {
                    DashWidget shape = _selection[0];
                    menu.Items.Add(MenuItem("Convert to path", null, () => ConvertToPath(shape)));
                    menu.Items.Add(new Separator());
                }
                bool allClipped = _selection.All(w => Expr.Bool(w.Get("clip", "false")));
                menu.Items.Add(MenuItem(allClipped ? "Release clipping mask" : "Clip to layer below", "Ctrl+Alt+G", () => ToggleClip(_selection.ToList())));
                menu.Items.Add(MenuItem("Duplicate", "Ctrl+D", () => DuplicateSelection()));
                menu.Items.Add(MenuItem("Copy", "Ctrl+C", CopySelection));
                menu.Items.Add(MenuItem("Delete", "Del", DeleteSelection));
                menu.Items.Add(new Separator());
                menu.Items.Add(MenuItem("Bring to front", null, () => Reorder(int.MaxValue)));
                menu.Items.Add(MenuItem("Bring forward", "]", () => Reorder(1)));
                menu.Items.Add(MenuItem("Send backward", "[", () => Reorder(-1)));
                menu.Items.Add(MenuItem("Send to back", null, () => Reorder(int.MinValue)));
                menu.Items.Add(new Separator());
                menu.Items.Add(MenuItem(_selection.All(w => w.Locked) ? "Unlock" : "Lock", "Ctrl+L", ToggleLock));
                menu.Items.Add(MenuItem("Hide", null, () => { foreach (DashWidget w in _selection) w.Hidden = true; _selection.Clear(); Commit("Hide"); SelectionChanged(); }));
                if (_layout.Screens.Count > 1)
                {
                    var move = new MenuItem { Header = "Move to screen" };
                    foreach (DashScreen screen in _layout.Screens.Where(s => s != _screen))
                    {
                        DashScreen target = screen;
                        move.Items.Add(MenuItem(target.Name, null, () => MoveToScreen(target)));
                    }
                    menu.Items.Add(move);
                }
                menu.Items.Add(new Separator());
                menu.Items.Add(MenuItem("Save as module…", "Ctrl+M", SaveSelectionAsModule));
            }
            if (_clipboard != null) menu.Items.Add(MenuItem("Paste", "Ctrl+V", () => Paste(p)));
            menu.Items.Add(MenuItem("Select all", "Ctrl+A", SelectAll));
            menu.IsOpen = true;
        }

        private static MenuItem MenuItem(string header, string gesture, Action action)
        {
            var item = new MenuItem { Header = header, InputGestureText = gesture ?? "" };
            item.Click += (s, e) => action();
            return item;
        }

        // ------------------------------------------------------------------ move & resize with snapping

        private void DoMove(Point p, bool free)
        {
            if (_originals == null || _originals.Count == 0) return;
            double dx = p.X - _dragStart.X, dy = p.Y - _dragStart.Y;
            if (Math.Abs(dx) + Math.Abs(dy) > 0.5) _dragChanged = true;
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) { if (Math.Abs(dx) > Math.Abs(dy)) dy = 0; else dx = 0; }
            Rect union = Rect.Empty;
            foreach (Rect r in _originals.Values) union = union.IsEmpty ? r : Rect.Union(union, r);
            Rect moved = new Rect(union.X + dx, union.Y + dy, union.Width, union.Height);
            _guides.Clear();
            if (!free)
            {
                dx += SnapAxis(new[] { moved.Left, moved.Left + moved.Width / 2, moved.Right }, true, moved);
                dy += SnapAxis(new[] { moved.Top, moved.Top + moved.Height / 2, moved.Bottom }, false, new Rect(union.X + dx, moved.Y, moved.Width, moved.Height));
            }
            foreach (KeyValuePair<DashWidget, Rect> pair in _originals)
            {
                pair.Key.X = Math.Round(pair.Value.X + dx);
                pair.Key.Y = Math.Round(pair.Value.Y + dy);
            }
        }

        private void DoResize(Point p, bool free, bool keepAspect)
        {
            if (_selection.Count > 1)
            {
                ResizeTogether(p, free, keepAspect);
                return;
            }
            DashWidget w = _selection[0];
            Rect o = _originals[w];
            double angle = Angle(w);
            // A turned widget is resized in its own frame: the pointer is turned back about the original middle.
            if (angle != 0) p = Turn(p, Center(o), -angle);
            double left = o.Left, top = o.Top, right = o.Right, bottom = o.Bottom;
            bool l = _handle == 0 || _handle == 6 || _handle == 7, r = _handle == 2 || _handle == 3 || _handle == 4;
            bool t = _handle == 0 || _handle == 1 || _handle == 2, b = _handle == 4 || _handle == 5 || _handle == 6;
            _guides.Clear();
            double x = p.X, y = p.Y;
            if (!free && angle == 0)
            {
                if (l || r) x += SnapAxis(new[] { x }, true, new Rect(x, top, 0, bottom - top));
                if (t || b) y += SnapAxis(new[] { y }, false, new Rect(left, y, right - left, 0));
            }
            if (l) left = Math.Min(x, right - 4);
            if (r) right = Math.Max(x, left + 4);
            if (t) top = Math.Min(y, bottom - 4);
            if (b) bottom = Math.Max(y, top + 4);
            if (keepAspect && o.Width > 0 && o.Height > 0)
            {
                double ratio = o.Width / o.Height;
                double width = right - left, height = bottom - top;
                if ((l || r) && !(t || b)) height = width / ratio;
                else if ((t || b) && !(l || r)) width = height * ratio;
                else if (width / height > ratio) height = width / ratio; else width = height * ratio;
                if (l) left = right - width; else right = left + width;
                if (t) top = bottom - height; else bottom = top + height;
            }
            if (angle != 0)
            {
                // Back to the screen: the new box's middle turns about the original middle, which keeps the
                // edge opposite the handle where it was.
                Point middle = Turn(new Point((left + right) / 2, (top + bottom) / 2), Center(o), angle);
                double width = right - left, height = bottom - top;
                left = middle.X - width / 2;
                right = middle.X + width / 2;
                top = middle.Y - height / 2;
                bottom = middle.Y + height / 2;
            }
            w.X = Math.Round(left);
            w.Y = Math.Round(top);
            w.W = Math.Max(2, Math.Round(right - left));
            w.H = Math.Max(2, Math.Round(bottom - top));
            _dragChanged = true;
        }

        /// <summary>
        /// Several widgets (or a group) resized from the handles around them all: the outer box follows the
        /// handle as a single widget's would, and every widget keeps its place and share within it.
        /// </summary>
        private void ResizeTogether(Point p, bool free, bool keepAspect)
        {
            Rect o = Rect.Empty;
            foreach (Rect r in _originals.Values) o = o.IsEmpty ? r : Rect.Union(o, r);
            if (o.Width < 1 || o.Height < 1) return;
            double left = o.Left, top = o.Top, right = o.Right, bottom = o.Bottom;
            bool l = _handle == 0 || _handle == 6 || _handle == 7, rt = _handle == 2 || _handle == 3 || _handle == 4;
            bool t = _handle == 0 || _handle == 1 || _handle == 2, b = _handle == 4 || _handle == 5 || _handle == 6;
            _guides.Clear();
            double x = p.X, y = p.Y;
            if (!free)
            {
                if (l || rt) x += SnapAxis(new[] { x }, true, new Rect(x, top, 0, bottom - top));
                if (t || b) y += SnapAxis(new[] { y }, false, new Rect(left, y, right - left, 0));
            }
            if (l) left = Math.Min(x, right - 8);
            if (rt) right = Math.Max(x, left + 8);
            if (t) top = Math.Min(y, bottom - 8);
            if (b) bottom = Math.Max(y, top + 8);
            if (keepAspect)
            {
                double ratio = o.Width / o.Height, width = right - left, height = bottom - top;
                if ((l || rt) && !(t || b)) height = width / ratio;
                else if ((t || b) && !(l || rt)) width = height * ratio;
                else if (width / height > ratio) height = width / ratio; else width = height * ratio;
                if (l) left = right - width; else right = left + width;
                if (t) top = bottom - height; else bottom = top + height;
            }
            double sx = (right - left) / o.Width, sy = (bottom - top) / o.Height;
            foreach (KeyValuePair<DashWidget, Rect> pair in _originals)
            {
                Rect r = pair.Value;
                pair.Key.X = Math.Round(left + (r.X - o.X) * sx);
                pair.Key.Y = Math.Round(top + (r.Y - o.Y) * sy);
                pair.Key.W = Math.Max(2, Math.Round(r.Width * sx));
                pair.Key.H = Math.Max(2, Math.Round(r.Height * sy));
            }
            _dragChanged = true;
        }

        /// <summary>
        /// Finds the nudge (on one axis) that lines any of the given edges up with the
        /// canvas, its center, or another widget's edges or center; falls back to the grid.
        /// Adds a guide line for the match.
        /// </summary>
        private double SnapAxis(double[] edges, bool horizontal, Rect moving)
        {
            double threshold = 6 * Px;
            var targets = new List<double> { 0, horizontal ? CanvasW / 2 : CanvasH / 2, horizontal ? CanvasW : CanvasH };
            foreach (DashWidget other in _screen.Widgets)
            {
                if (other.Hidden || _selection.Contains(other)) continue;
                Rect r = RectOf(other);
                if (horizontal) targets.AddRange(new[] { r.Left, r.Left + r.Width / 2, r.Right });
                else targets.AddRange(new[] { r.Top, r.Top + r.Height / 2, r.Bottom });
            }
            double best = double.MaxValue, bestTarget = 0;
            foreach (double edge in edges)
                foreach (double target in targets)
                {
                    double d = target - edge;
                    if (Math.Abs(d) < Math.Abs(best) && Math.Abs(d) <= threshold) { best = d; bestTarget = target; }
                }
            if (best != double.MaxValue)
            {
                if (horizontal) _guides.Add(Tuple.Create(new Point(bestTarget, Math.Min(0, moving.Top) - 2000), new Point(bestTarget, Math.Max(CanvasH, moving.Bottom) + 2000)));
                else _guides.Add(Tuple.Create(new Point(Math.Min(0, moving.Left) - 2000, bestTarget), new Point(Math.Max(CanvasW, moving.Right) + 2000, bestTarget)));
                return best;
            }
            double first = edges[0];
            return Math.Round(first / GridSize) * GridSize - first;
        }

        // ------------------------------------------------------------------ drag in from the toolbox

        private void Overlay_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent("afo-widget") || e.Data.GetDataPresent("afo-block") || e.Data.GetDataPresent("afo-module") || ModuleFiles(e.Data).Count > 0
                ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void Overlay_Drop(object sender, DragEventArgs e)
        {
            Point p = e.GetPosition(Overlay);
            List<DashWidget> widgets;
            string label;
            if (e.Data.GetData("afo-module") is DesignModule module)
            {
                PlaceModule(module, p, true);
                return;
            }
            // Module files dragged in from Explorer join the Library and land where they're dropped.
            List<string> files = ModuleFiles(e.Data);
            if (files.Count > 0)
            {
                ImportModuleFiles(files, p);
                return;
            }
            if (e.Data.GetData("afo-widget") is string type)
            {
                WidgetType info = WidgetCatalog.Find(type);
                widgets = new List<DashWidget> { WidgetCatalog.Create(type, p.X - info.DefaultW / 2, p.Y - info.DefaultH / 2) };
                label = "Add " + info.Name;
            }
            else if (e.Data.GetData("afo-block") is DesignBlock block)
            {
                widgets = block.Create();
                Rect bounds = Union(widgets);
                foreach (DashWidget w in widgets) { w.X += Math.Round(p.X - bounds.Width / 2 - bounds.X); w.Y += Math.Round(p.Y - bounds.Height / 2 - bounds.Y); }
                label = "Add " + block.Name;
            }
            else return;
            // Dropped on a panel: ask whether it belongs to the panel, and on which of its views.
            DashWidget panel = PanelAt(p);
            if (panel != null && !widgets.Any(w => w.Type == "panel")) AskPanelDrop(panel, widgets, label);
            else AddWidgets(widgets, label);
        }

        // ==================================================================
        // Selection and editing commands
        // ==================================================================

        private void Select(DashWidget widget)
        {
            _selection.Clear();
            if (widget != null) _selection.Add(widget);
            SelectionChanged();
        }

        private void SelectAll()
        {
            _selection.Clear();
            _selection.AddRange(_screen.Widgets.Where(w => !w.Hidden && !w.Locked));
            SelectionChanged();
        }

        private void SelectionChanged()
        {
            RefreshInspector();
            RefreshLayers();
            UpdateOverlay();
        }

        /// <summary>A widget's whole group on this screen (just the widget when it isn't grouped).</summary>
        private List<DashWidget> GroupOf(DashWidget widget) =>
            string.IsNullOrEmpty(widget.Group) ? new List<DashWidget> { widget }
                : _screen.Widgets.Where(w => w.Group == widget.Group && !w.Hidden && !w.Locked).ToList();

        /// <summary>The selection is exactly one whole group.</summary>
        private bool SelectionIsOneGroup(out string group)
        {
            group = _selection.Count > 0 ? _selection[0].Group : null;
            string id = group;
            return !string.IsNullOrEmpty(id) && _selection.All(w => w.Group == id) && _screen.Widgets.Count(w => w.Group == id) == _selection.Count;
        }

        private void GroupSelection()
        {
            if (_selection.Count < 2) { StatusText.Text = "Select two or more widgets to group them."; return; }
            string id = DashScreen.NewId("g");
            int number = _screen.Widgets.Select(w => w.Group).Where(g => !string.IsNullOrEmpty(g)).Distinct().Count() + 1;
            foreach (DashWidget w in _selection) { w.Group = id; w.GroupName = "Group " + number; w.Slot = 0; }
            Commit("Group");
            SelectionChanged();
            StatusText.Text = "Grouped " + _selection.Count + " widgets. Set the group's Slot to number it, and use Slot in its formulas.";
        }

        private void Ungroup()
        {
            if (!_selection.Any(w => !string.IsNullOrEmpty(w.Group))) return;
            foreach (DashWidget w in _selection) { w.Group = ""; w.GroupName = ""; w.Slot = 0; }
            Commit("Ungroup");
            SelectionChanged();
        }

        /// <summary>New widgets coming onto the screen (pasted, from a block) get groups of their own, not the ones they were copied from.</summary>
        private void FreshGroups(IEnumerable<DashWidget> widgets)
        {
            var renamed = new Dictionary<string, string>();
            foreach (DashWidget w in widgets)
            {
                if (string.IsNullOrEmpty(w.Group)) continue;
                if (!renamed.TryGetValue(w.Group, out string id)) renamed[w.Group] = id = DashScreen.NewId("g");
                w.Group = id;
            }
        }

        /// <param name="keepGroups">Joining a group already on the screen (a panel's), rather than coming in as new groups of their own.</param>
        internal void AddWidgets(IList<DashWidget> widgets, string label, bool keepGroups = false)
        {
            if (!keepGroups && widgets.Any(w => !string.IsNullOrEmpty(w.Group) && _screen.Widgets.Any(s => s.Group == w.Group))) FreshGroups(widgets);
            foreach (DashWidget w in widgets)
            {
                w.X = Math.Round(Math.Max(-w.W + 10, Math.Min(CanvasW - 10, w.X)));
                w.Y = Math.Round(Math.Max(-w.H + 10, Math.Min(CanvasH - 10, w.Y)));
                _screen.Widgets.Add(w);
            }
            _selection.Clear();
            _selection.AddRange(widgets);
            _rendererDirty = true;
            Commit(label);
            SelectionChanged();
            RequestRender();
        }

        private void AddWidgetAtCenter(string type)
        {
            WidgetType info = WidgetCatalog.Find(type);
            int cascade = _screen.Widgets.Count(w => w.Type == type) % 6 * 12;
            AddWidgets(new[] { WidgetCatalog.Create(type, (CanvasW - info.DefaultW) / 2 + cascade, (CanvasH - info.DefaultH) / 2 + cascade) }, "Add " + info.Name);
        }

        private void DeleteSelection()
        {
            if (_selection.Count == 0) return;
            foreach (DashWidget w in _selection) _screen.Widgets.Remove(w);
            _selection.Clear();
            Commit("Delete");
            SelectionChanged();
            RequestRender();
        }

        private List<DashWidget> DuplicateSelection(double offset = 16)
        {
            if (_selection.Count == 0) return null;
            // A whole group copies as the next one: just below it, numbered one higher (P1's card becomes P2's).
            bool nextInGroup = SelectionIsOneGroup(out _);
            Rect bounds = Union(_selection);
            var copies = _screen.Widgets.Where(_selection.Contains).Select(w =>
            {
                DashWidget copy = SettingsStore.Clone(w);
                copy.Id = DashScreen.NewId("w");
                copy.X += nextInGroup ? 0 : offset;
                copy.Y += nextInGroup ? Math.Round(bounds.Height + 6) : offset;
                if (nextInGroup) copy.Slot = w.Slot + 1;
                copy.Locked = false;
                return copy;
            }).ToList();
            FreshGroups(copies);
            AddWidgets(copies, "Duplicate");
            if (nextInGroup) StatusText.Text = "Copied the group as Slot " + copies[0].Slot + ".";
            return copies;
        }

        // ------------------------------------------------------------------ copy and paste style

        private static readonly HashSet<string> StyleGroups = new HashSet<string> { "Style", "Colors", "Effects", "Type" };
        private Dictionary<string, string> _copiedStyle;

        /// <summary>Copies the selected widget's look: its Style effects, colors, glow and type settings.</summary>
        private void CopyStyle()
        {
            if (_selection.Count != 1) { StatusText.Text = "Select one widget to copy its style."; return; }
            DashWidget source = _selection[0];
            WidgetType type = WidgetCatalog.Find(source.Type);
            if (type == null) return;
            _copiedStyle = type.Props.Where(p => StyleGroups.Contains(p.Group)).ToDictionary(p => p.Key, p => source.Get(p.Key, p.Default));
            StatusText.Text = "Copied the style of " + source.Name + ". Select widgets and press Ctrl+Shift+V to paste it.";
        }

        /// <summary>Pastes a copied style onto the selection, wherever a widget has the same setting.</summary>
        private void PasteStyle()
        {
            if (_copiedStyle == null || _selection.Count == 0) return;
            int changed = 0;
            foreach (DashWidget w in _selection)
            {
                WidgetType type = WidgetCatalog.Find(w.Type);
                if (type == null) continue;
                foreach (KeyValuePair<string, string> pair in _copiedStyle)
                    if (type.Prop(pair.Key) is PropDef def && StyleGroups.Contains(def.Group)) { w.Set(pair.Key, pair.Value); changed++; }
            }
            Commit("Paste style");
            RefreshInspector();
            StatusText.Text = "Pasted the style onto " + _selection.Count + (_selection.Count == 1 ? " widget." : " widgets.");
        }

        private void CopySelection()
        {
            if (_selection.Count == 0) return;
            _clipboard = SettingsStore.ToJson(_screen.Widgets.Where(_selection.Contains).ToList());
            StatusText.Text = _selection.Count == 1 ? "Copied 1 widget." : "Copied " + _selection.Count + " widgets.";
        }

        private void Paste(Point? at = null)
        {
            if (_clipboard == null) return;
            List<DashWidget> widgets;
            try { widgets = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<List<DashWidget>>(_clipboard); }
            catch { return; }
            Rect bounds = Union(widgets);
            foreach (DashWidget w in widgets)
            {
                w.Id = DashScreen.NewId("w");
                w.Normalize();
                if (at.HasValue) { w.X += Math.Round(at.Value.X - bounds.X); w.Y += Math.Round(at.Value.Y - bounds.Y); }
                else { w.X += 16; w.Y += 16; }
            }
            AddWidgets(widgets, "Paste");
        }

        private void Reorder(int step)
        {
            if (_selection.Count == 0) return;
            List<DashWidget> list = _screen.Widgets;
            List<DashWidget> moving = list.Where(_selection.Contains).ToList();
            if (step == int.MaxValue) { foreach (DashWidget w in moving) { list.Remove(w); list.Add(w); } }
            else if (step == int.MinValue) { for (int i = moving.Count - 1; i >= 0; i--) { list.Remove(moving[i]); list.Insert(0, moving[i]); } }
            else
            {
                IEnumerable<DashWidget> order = step > 0 ? Enumerable.Reverse(moving) : moving;
                foreach (DashWidget w in order)
                {
                    int index = list.IndexOf(w), target = Math.Max(0, Math.Min(list.Count - 1, index + step));
                    list.RemoveAt(index);
                    list.Insert(target, w);
                }
            }
            Commit("Reorder");
            RefreshLayers();
            RequestRender();
        }

        private void ToggleLock()
        {
            bool lockAll = !_selection.All(w => w.Locked);
            foreach (DashWidget w in _selection) w.Locked = lockAll;
            Commit(lockAll ? "Lock" : "Unlock");
            SelectionChanged();
        }

        private void MoveToScreen(DashScreen target)
        {
            foreach (DashWidget w in _selection.ToList()) { _screen.Widgets.Remove(w); target.Widgets.Add(w); }
            _selection.Clear();
            Commit("Move to " + target.Name);
            SelectionChanged();
            RequestRender();
        }

        private void Nudge(double dx, double dy)
        {
            foreach (DashWidget w in _selection.Where(w => !w.Locked)) { w.X += dx; w.Y += dy; }
            Commit("Nudge");
            RefreshPositionFields();
            RequestRender();
        }

        internal void AlignSelection(string how)
        {
            List<DashWidget> items = _selection.Where(w => !w.Locked).ToList();
            if (items.Count == 0) return;
            Rect area = items.Count == 1 ? new Rect(0, 0, CanvasW, CanvasH) : Union(items);
            foreach (DashWidget w in items)
                switch (how)
                {
                    case "left": w.X = area.Left; break;
                    case "center": w.X = Math.Round(area.Left + (area.Width - w.W) / 2); break;
                    case "right": w.X = area.Right - w.W; break;
                    case "top": w.Y = area.Top; break;
                    case "middle": w.Y = Math.Round(area.Top + (area.Height - w.H) / 2); break;
                    case "bottom": w.Y = area.Bottom - w.H; break;
                }
            if (how == "hspace" && items.Count > 2)
            {
                items = items.OrderBy(w => w.X).ToList();
                double free = area.Width - items.Sum(w => w.W), gap = free / (items.Count - 1), x = area.Left;
                foreach (DashWidget w in items) { w.X = Math.Round(x); x += w.W + gap; }
            }
            if (how == "vspace" && items.Count > 2)
            {
                items = items.OrderBy(w => w.Y).ToList();
                double free = area.Height - items.Sum(w => w.H), gap = free / (items.Count - 1), y = area.Top;
                foreach (DashWidget w in items) { w.Y = Math.Round(y); y += w.H + gap; }
            }
            if (how == "width" || how == "height")
            {
                DashWidget first = items[0];
                foreach (DashWidget w in items) { if (how == "width") w.W = first.W; else w.H = first.H; }
            }
            Commit("Align");
            RefreshPositionFields();
            RequestRender();
        }

        // ==================================================================
        // Undo, redo and saving
        // ==================================================================

        private string Snapshot() => SettingsStore.ToJson(_layout);

        /// <summary>Records the design as one undo step. Rapid repeats of the same edit merge into one.</summary>
        internal void Commit(string label)
        {
            _pendingCommitLabel = null;
            _commitTimer.Stop();
            // Every finished edit passes through here, so the preview always catches up with it.
            RequestRender();
            string json = Snapshot();
            if (json == _committed)
            {
                // The accent lives on the dashboard, not the design, so it can change without a new snapshot.
                if (Dirty) SaveSoon();
                UpdateSaveState();
                return;
            }
            // Quick repeats of one edit (a slider dragged) are one step to undo; each widget added or drawn is its own.
            bool merge = label == _lastCommitLabel && (DateTime.UtcNow - _lastCommitUtc).TotalMilliseconds < 1200 && label != "Add" && !label.StartsWith("Draw ") && _undo.Count > 0;
            if (!merge)
            {
                _undo.Add(_committed);
                if (_undo.Count > 300) _undo.RemoveAt(0);
            }
            _committed = json;
            _redo.Clear();
            _lastCommitLabel = label;
            _lastCommitUtc = DateTime.UtcNow;
            UpdateUndoButtons();
            UpdateSaveState();
            SaveSoon();
        }

        private void SaveSoon()
        {
            _liveSaveTimer.Stop();
            _liveSaveTimer.Start();
        }

        /// <summary>Commits after typing pauses, so each keystroke is not its own undo step.</summary>
        internal void CommitSoon(string label)
        {
            _pendingCommitLabel = label;
            _commitTimer.Stop();
            _commitTimer.Start();
        }

        private void FlushPendingCommit()
        {
            if (_pendingCommitLabel != null) Commit(_pendingCommitLabel);
        }

        private void Undo_Click(object sender, RoutedEventArgs e) => Undo();
        private void Redo_Click(object sender, RoutedEventArgs e) => Redo();

        private void Undo()
        {
            FlushPendingCommit();
            if (_undo.Count == 0) return;
            _redo.Add(_committed);
            _committed = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            LoadSnapshot(_committed);
            StatusText.Text = "Undid " + (_lastCommitLabel ?? "change").ToLowerInvariant() + ".";
            _lastCommitLabel = null;
        }

        private void Redo()
        {
            if (_redo.Count == 0) return;
            _undo.Add(_committed);
            _committed = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            LoadSnapshot(_committed);
            _lastCommitLabel = null;
        }

        private void LoadSnapshot(string json)
        {
            string screenId = _screen?.Id;
            var selected = new HashSet<string>(_selection.Select(w => w.Id));
            _layout = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 256 }.Deserialize<DashLayout>(json);
            _layout.Normalize();
            _screen = _layout.Screens.FirstOrDefault(s => s.Id == screenId) ?? _layout.Screens[0];
            _selection.Clear();
            _selection.AddRange(_screen.Widgets.Where(w => selected.Contains(w.Id)));
            _rendererDirty = true;
            BuildScreensCombo();
            BuildThemeEditor();
            SelectionChanged();
            UpdateUndoButtons();
            UpdateSaveState();
            RequestRender();
            SaveSoon();
        }

        private void UpdateUndoButtons()
        {
            UndoButton.IsEnabled = _undo.Count > 0 || _pendingCommitLabel != null;
            RedoButton.IsEnabled = _redo.Count > 0;
        }

        // The accent belongs to the dashboard rather than the design, so it is tracked separately.
        private string _savedAccent;

        private bool Dirty => _committed != _savedJson || _dashboard.AccentColor != _savedAccent;

        private void UpdateSaveState()
        {
            if (SaveState == null) return;
            SaveState.Text = Dirty ? "Saving…" : "Saved";
            SaveStateIcon.Visibility = Dirty ? Visibility.Collapsed : Visibility.Visible;
        }

        private void SaveDesign(bool quiet)
        {
            FlushPendingCommit();
            try
            {
                _dashboard.Design = SettingsStore.Clone(_layout);
                SettingsStore.Save(_settings);
                _savedJson = _committed;
                _savedAccent = _dashboard.AccentColor;
                _onSaved?.Invoke();
                if (!quiet) StatusText.Text = "Saved. A running dashboard picks this up within a second.";
            }
            catch (Exception exception)
            {
                StatusText.Text = "Could not save: " + exception.Message;
            }
            UpdateSaveState();
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            // Nothing is ever left unsaved: anything still waiting is written now.
            FlushPendingCommit();
            _liveSaveTimer.Stop();
            if (Dirty) SaveDesign(true);
        }

        // ==================================================================
        // Import and export
        // ==================================================================

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            FlushPendingCommit();
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export dashboard design",
                Filter = "Almost Fast Dash dashboard (*.afodash)|*.afodash",
                FileName = SafeFileName(_dashboard.Name) + ".afodash"
            };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                DesignPackage.Write(dialog.FileName, DesignPackage.From(_dashboard, _layout));
                StatusText.Text = "Exported to " + dialog.FileName;
            }
            catch (Exception exception)
            {
                StatusText.Text = "Export failed: " + exception.Message;
            }
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Import dashboard design", Filter = "Almost Fast Dash dashboard (*.afodash)|*.afodash|All files|*.*" };
            if (dialog.ShowDialog(this) != true) return;
            DesignPackage package;
            try { package = DesignPackage.Read(dialog.FileName); }
            catch (Exception exception) { StatusText.Text = "Import failed: " + exception.Message; return; }
            if (MessageBox.Show(this, "Replace this design with \"" + package.Name + "\"? You can undo this.", "Import design", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
                return;
            _layout = package.Design;
            _layout.Normalize();
            _screen = _layout.Screens[0];
            if (!string.IsNullOrEmpty(package.AccentColor)) _dashboard.AccentColor = package.AccentColor;
            _selection.Clear();
            _rendererDirty = true;
            Commit("Import");
            BuildScreensCombo();
            BuildThemeEditor();
            SelectionChanged();
            RequestRender();
        }

        internal static string SafeFileName(string name)
        {
            foreach (char c in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(c, '-');
            return name;
        }

        // ==================================================================
        // Screens
        // ==================================================================

        private bool _buildingScreens;

        private void BuildScreensCombo()
        {
            _buildingScreens = true;
            ScreenCombo.Items.Clear();
            foreach (DashScreen screen in _layout.Screens)
                ScreenCombo.Items.Add(new ComboBoxItem
                {
                    Content = screen.Name + (string.IsNullOrWhiteSpace(screen.Condition) ? "" : "  ·  when " + screen.Condition),
                    Tag = screen
                });
            ScreenCombo.SelectedIndex = Math.Max(0, _layout.Screens.IndexOf(_screen));
            _buildingScreens = false;
        }

        private void ScreenCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_buildingScreens || !(ScreenCombo.SelectedItem is ComboBoxItem item) || !(item.Tag is DashScreen screen)) return;
            _screen = screen;
            _selection.Clear();
            SelectionChanged();
            RequestRender();
        }

        private void ScreenMenu_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = PlacementMode.Bottom };
            menu.Items.Add(MenuItem("Add blank screen", null, () => AddScreen(false)));
            menu.Items.Add(MenuItem("Duplicate this screen", null, () => AddScreen(true)));
            menu.Items.Add(MenuItem("Add pit lane screen (shows on pit road)", null, () => AddScreen(true, "Pit lane", "OnPitRoad")));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItem("Edit name and show-when…", null, () => { _selection.Clear(); SelectionChanged(); InspectorScroller.ScrollToTop(); }));
            var delete = MenuItem("Delete this screen", null, DeleteScreen);
            delete.IsEnabled = _layout.Screens.Count > 1;
            menu.Items.Add(delete);
            menu.IsOpen = true;
        }

        private void AddScreen(bool copyCurrent, string name = null, string condition = null)
        {
            var screen = copyCurrent ? SettingsStore.Clone(_screen) : new DashScreen();
            screen.Id = DashScreen.NewId("screen");
            foreach (DashWidget w in screen.Widgets) w.Id = DashScreen.NewId("w");
            screen.Name = name ?? (copyCurrent ? _screen.Name + " copy" : "Screen " + (_layout.Screens.Count + 1));
            screen.Condition = condition ?? (copyCurrent ? "" : "");
            _layout.Screens.Add(screen);
            _screen = screen;
            _selection.Clear();
            Commit("Add screen");
            BuildScreensCombo();
            SelectionChanged();
            RequestRender();
            StatusText.Text = string.IsNullOrEmpty(condition)
                ? "Added a screen. Give it a show-when condition in the inspector, or it only shows if no other screen matches."
                : "Added a screen that shows while " + condition + ".";
        }

        private void DeleteScreen()
        {
            if (_layout.Screens.Count <= 1) return;
            _layout.Screens.Remove(_screen);
            _screen = _layout.Screens[0];
            _selection.Clear();
            Commit("Delete screen");
            BuildScreensCombo();
            SelectionChanged();
            RequestRender();
        }

        internal void ScreenEdited()
        {
            CommitSoon("Screen");
            int index = ScreenCombo.SelectedIndex;
            BuildScreensCombo();
            ScreenCombo.SelectedIndex = index;
            RequestRender();
        }

        // ==================================================================
        // Zoom and view
        // ==================================================================

        private void SetZoom(double zoom, bool fromFit = false)
        {
            _zoom = Math.Max(0.25, Math.Min(4, zoom));
            _fitOnResize = fromFit;
            CanvasScale.ScaleX = CanvasScale.ScaleY = _zoom;
            ZoomLabel.Content = Math.Round(_zoom * 100) + "%";
            UpdateOverlay();
        }

        private void ZoomFit()
        {
            double w = CanvasScroller.ActualWidth - 100, h = CanvasScroller.ActualHeight - 140;
            if (w <= 0 || h <= 0) return;
            SetZoom(Math.Min(w / CanvasW, h / CanvasH), true);
        }

        private void ZoomIn_Click(object sender, RoutedEventArgs e) => SetZoom(_zoom * 1.25);
        private void ZoomOut_Click(object sender, RoutedEventArgs e) => SetZoom(_zoom / 1.25);
        private void ZoomFit_Click(object sender, RoutedEventArgs e) => ZoomFit();

        private void CanvasScroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            SetZoom(_zoom * (e.Delta > 0 ? 1.1 : 1 / 1.1));
            e.Handled = true;
        }

        private void ViewOption_Click(object sender, RoutedEventArgs e) => UpdateOverlay();

        // ==================================================================
        // Keyboard
        // ==================================================================

        private void FormulaReference_Click(object sender, RoutedEventArgs e) => FormulaReferenceWindow.Open(this, CurrentScope);

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0, shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (ctrl && key == Key.S) { SaveDesign(false); e.Handled = true; return; }
            if (key == Key.F1) { FormulaReferenceWindow.Open(this, CurrentScope); e.Handled = true; return; }

            // Text fields keep their own keys.
            if (Keyboard.FocusedElement is TextBox || Keyboard.FocusedElement is ComboBox) return;

            // The tool rail's keys: V, U, T, H, Z, Space held for the Hand, and Esc back to Move.
            if (PenKey(key) || EditKey(key)) { e.Handled = true; return; }
            if (!ctrl && (Keyboard.Modifiers & ModifierKeys.Alt) == 0 && ToolKey(key, shift, e)) { e.Handled = true; return; }

            double step = shift ? 10 : 1;
            switch (key)
            {
                case Key.Delete:
                case Key.Back: DeleteSelection(); break;
                case Key.Left: Nudge(-step, 0); break;
                case Key.Right: Nudge(step, 0); break;
                case Key.Up: Nudge(0, -step); break;
                case Key.Down: Nudge(0, step); break;
                case Key.Escape: _selection.Clear(); SelectionChanged(); break;
                case Key.OemCloseBrackets: Reorder(ctrl ? int.MaxValue : 1); break;
                case Key.OemOpenBrackets: Reorder(ctrl ? int.MinValue : -1); break;
                case Key.Z when ctrl: if (shift) Redo(); else Undo(); break;
                case Key.Y when ctrl: Redo(); break;
                case Key.D when ctrl: DuplicateSelection(); break;
                case Key.G when ctrl && (Keyboard.Modifiers & ModifierKeys.Alt) != 0: ToggleClip(_selection); break;
                case Key.G when ctrl && shift: Ungroup(); break;
                case Key.G when ctrl: GroupSelection(); break;
                case Key.C when ctrl && shift: CopyStyle(); break;
                case Key.V when ctrl && shift: PasteStyle(); break;
                case Key.C when ctrl: CopySelection(); break;
                case Key.X when ctrl: CopySelection(); DeleteSelection(); break;
                case Key.V when ctrl: Paste(); break;
                case Key.A when ctrl: SelectAll(); break;
                case Key.L when ctrl: ToggleLock(); break;
                case Key.M when ctrl: SaveSelectionAsModule(); break;
                case Key.D0 when ctrl: ZoomFit(); break;
                case Key.OemPlus when ctrl: SetZoom(_zoom * 1.25); break;
                case Key.OemMinus when ctrl: SetZoom(_zoom / 1.25); break;
                default: return;
            }
            e.Handled = true;
        }

        /// <summary>Sets up a state for the --ui-snapshot self-check.</summary>
        internal void PrepareSnapshot(string scenario)
        {
            PreviewCombo.SelectedItem = "Racing";
            DashWidget Find(string name) => _screen.Widgets.FirstOrDefault(w => w.Name == name);
            if (scenario.Contains("theme")) TabTheme.IsChecked = true;

            if (scenario.Contains("masks") || scenario.Contains("modules"))
            {
                // Clipping masks, fades and blending, for the snapshots and the guide.
                _screen.Widgets.Clear();
                DashWidget Layer(string name, string kind, double x, double y, double w, double h, params string[] props)
                {
                    DashWidget s = WidgetCatalog.Create(kind == "text" ? "text" : "shape", x, y);
                    s.Name = name; s.W = w; s.H = h;
                    if (kind != "text") { s.Set("shape", kind); s.Set("strokeWidth", "0"); s.Set("radius", "0"); }
                    for (int i = 0; i + 1 < props.Length; i += 2) s.Set(props[i], props[i + 1]);
                    _screen.Widgets.Add(s);
                    return s;
                }
                // A rectangle's outline with stripes clipped to it: a kerb.
                Layer("Outline", "Rectangle", 24, 24, 300, 150, "radius", "16", "fill", "", "stroke", "#FFFFFF", "strokeWidth", "12");
                Layer("Stripes", "Rectangle", 10, 10, 330, 180, "fillStyle", "Pattern", "pattern", "Stripes", "fill", "#FF3B30", "fill2", "#FFFFFF",
                    "patternScale", "150", "patternAngle", "45", "clip", "true");
                // A gear digit filled with a gradient and a sheen, both clipped to the digit.
                Layer("Gear", "text", 350, 0, 200, 220, "text", "7", "size", "200", "color", "#FFFFFF");
                Layer("Gradient", "Rectangle", 350, 0, 200, 220, "fillStyle", "Linear", "fill", "#22D3EE", "fill2", "#A855F7", "fillAngle", "90", "clip", "true");
                Layer("Sheen", "Ellipse", 330, -60, 200, 160, "fillStyle", "Radial", "fill", "#FFFFFF", "fill2", "", "clip", "true", "fxBlend", "Screen", "opacity", "70");
                // A panel with light spilling over it, added and kept inside the panel.
                Layer("Panel", "Rectangle", 580, 24, 196, 150, "radius", "18", "fillStyle", "Pattern", "pattern", "Carbon", "fill", "#3A3F48", "fill2", "#0B0C0F", "patternAngle", "0");
                Layer("Light", "Ellipse", 640, -20, 220, 170, "fillStyle", "Radial", "fill", "#FF7A00", "fill2", "", "clip", "true", "fxBlend", "Add");
                // Fades.
                Layer("Fade right", "Rectangle", 24, 236, 360, 28, "fill", "#2DD4BF", "fxFade", "Right", "fxFadeLength", "70");
                Layer("Fade sides", "Rectangle", 24, 286, 360, 6, "fill", "#FFFFFF", "fxFade", "Sides", "fxFadeLength", "45");
                Layer("Fade bottom", "Rectangle", 24, 316, 360, 140, "radius", "14", "fillStyle", "Pattern", "pattern", "Honeycomb", "fill", "#2DD4BF", "fill2", "#10202A",
                    "patternScale", "80", "patternAngle", "0", "fxFade", "Bottom", "fxFadeLength", "80");
                // Multiply tints what's under it; Screen lightens.
                Layer("Tiles", "Rectangle", 420, 236, 356, 220, "radius", "14", "fillStyle", "Pattern", "pattern", "Checks", "fill", "#F8FAFC", "fill2", "#64748B", "patternScale", "120");
                Layer("Tint", "Ellipse", 440, 256, 180, 180, "fill", "#F43F5E", "fxBlend", "Multiply");
                Layer("Glow", "Ellipse", 576, 256, 180, 180, "fill", "#1D4ED8", "fxBlend", "Screen");
                _rendererDirty = true;
                Select(_screen.Widgets.First(w => w.Name == "Stripes"));
                if (scenario.Contains("modules"))
                {
                    // Some of them kept as modules, then placed on a fresh screen with one selected.
                    DesignModule Keep(string name, string category, string description, params string[] layers)
                    {
                        DesignModule module = DesignModule.From(name, _screen.Widgets.Where(w => layers.Contains(w.Name)), _layout, _dashboard.AccentColor, _units);
                        module.Category = category;
                        module.Description = description;
                        DesignModule.Save(module);
                        return module;
                    }
                    DesignModule kerb = Keep("Kerb frame", "Frames", "A frame in red and white kerb stripes.", "Outline", "Stripes");
                    DesignModule gear = Keep("Neon gear", "Gauges", "A gear digit in a cyan to violet gradient with a sheen.", "Gear", "Gradient", "Sheen");
                    DesignModule light = Keep("Carbon light panel", "Cards", "Carbon fibre with an orange light spilling over it.", "Panel", "Light");
                    DesignModule honeycomb = Keep("Honeycomb fade", "Backgrounds", "A honeycomb panel fading away at the bottom.", "Fade bottom");
                    Keep("Tinted tiles", "Backgrounds", "Checks tinted and lit by blended circles.", "Tiles", "Tint", "Glow");
                    _screen.Widgets.Clear();
                    ModulesChanged();
                    PlaceModule(honeycomb, new Point(190, 370), false);
                    PlaceModule(light, new Point(690, 120), false);
                    PlaceModule(gear, new Point(540, 330), false);
                    PlaceModule(kerb, new Point(190, 120), false);
                    // Properties scrolled to the placed module's colours.
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        TextBlock colours = Inspector.Children.OfType<TextBlock>().FirstOrDefault(t => t.Text == "COLOURS");
                        if (colours != null) InspectorScroller.ScrollToVerticalOffset(colours.TranslatePoint(new Point(0, 0), Inspector).Y - 120);
                    }), System.Windows.Threading.DispatcherPriority.ContextIdle);
                }
                RequestRender();
            }
            else if (scenario.Contains("drawing"))
            {
                // What the drawing tools make, for the snapshots and the guide.
                _screen.Widgets.Clear();
                DashWidget Shape(string name, string kind, double x, double y, double w, double h, params string[] props)
                {
                    DashWidget s = WidgetCatalog.Create(kind == "path" ? "path" : "shape", x, y);
                    s.Name = name; s.W = w; s.H = h;
                    if (kind != "path") s.Set("shape", kind);
                    for (int i = 0; i + 1 < props.Length; i += 2) s.Set(props[i], props[i + 1]);
                    _screen.Widgets.Add(s);
                    return s;
                }
                Shape("Carbon panel", "Rectangle", 24, 24, 380, 200, "radius", "18", "fillStyle", "Pattern", "pattern", "Carbon", "fill", "#3A3F48", "fill2", "#0B0C0F",
                    "patternAngle", "0", "stroke", "#2DD4BF", "strokeWidth", "2");
                Shape("Kerb", "Rectangle", 56, 64, 316, 120, "radius", "0", "fill", "", "strokeWidth", "10", "stroke", "#FFFFFF", "cap", "Flat", "join", "Sharp",
                    "fxFill", "Pattern", "fxFillColor", "#FF3B30", "fxPatternBack", "#FFFFFF", "fxPatternScale", "180", "fxPatternAngle", "45");
                Shape("Star", "Star", 440, 24, 150, 150, "sides", "5", "inner", "45", "fillStyle", "Linear", "fill", "#FFD54A", "fill2", "#FF7A00", "fillAngle", "90",
                    "strokeWidth", "0", "glow", "14");
                Shape("Hexagon", "Polygon", 620, 24, 200, 176, "sides", "6", "fillStyle", "Pattern", "pattern", "Honeycomb", "fill", "#2DD4BF", "fill2", "#10202A",
                    "patternScale", "90", "patternAngle", "0", "stroke", "#2DD4BF", "strokeWidth", "2.5");
                Shape("Swoosh", "path", 40, 250, 520, 150, "closed", "false", "points", "0,1|0,1|0.25,0.95 0.55,0.35|0.35,0.55|0.75,0.15 1,0|0.9,0.02|1,0",
                    "strokeWidth", "8", "strokeStyle", "Gradient", "stroke", "#22D3EE", "stroke2", "#A855F7", "strokeAngle", "0", "glow", "10");
                Shape("Dashes", "Line", 40, 420, 360, 12, "strokeWidth", "4", "dash", "Dashed", "stroke", "#94A3B8");
                Shape("Dots", "Line", 40, 450, 360, 12, "strokeWidth", "5", "dash", "Dotted", "stroke", "#FBBF24");
                DashWidget metal = WidgetCatalog.Create("text", 600, 250);
                metal.Name = "Brushed metal"; metal.W = 230; metal.H = 120;
                metal.Set("text", "136"); metal.Set("size", "110"); metal.Set("color", "#FFFFFF");
                metal.Set("fxFill", "Pattern"); metal.Set("fxPattern", "Brushed"); metal.Set("fxFillColor", "#FFFFFF"); metal.Set("fxPatternBack", "#7C8794");
                metal.Set("fxPatternAngle", "0"); metal.Set("fxPatternScale", "100");
                _screen.Widgets.Add(metal);
                Shape("Dots panel", "Rectangle", 600, 400, 230, 64, "radius", "10", "fillStyle", "Pattern", "pattern", "Dots", "fill", "#334155", "fill2", "#0F172A",
                    "patternScale", "60", "patternAngle", "0", "strokeWidth", "0");
                _rendererDirty = true;
                Select(_screen.Widgets.First(w => w.Name == "Kerb"));
                RequestRender();
            }
            else if (scenario.Contains("multi"))
            {
                _selection.Clear();
                _selection.AddRange(_screen.Widgets.Where(w => w.Type == "metric").Take(4));
                SelectionChanged();
            }
            else if (scenario.Contains("empty")) Select(null);
            else if (scenario.Contains("shape"))
            {
                // A swoosh rev bar with the Shape controls in view.
                DashWidget bar = DesignBlocks.BuiltIn().First(b => b.Name == "Swoosh rev bar").Create()[0];
                bar.X = 220;
                bar.Y = 150;
                _screen.Widgets.Add(bar);
                foreach (DashWidget half in DesignBlocks.BuiltIn().First(b => b.Name == "Mirrored rev bars").Create())
                {
                    half.X += 15;
                    half.Y = 380;
                    _screen.Widgets.Add(half);
                }
                OpenFolders.Add("Ready-made blocks/Rev gauges");
                BuildToolbox();
                CollapsedGroups.Add("Value");
                CollapsedGroups.Add("Look");
                Select(bar);
            }
            else Select(Find(scenario.Contains("neon") ? "Gear" : "Fuel laps") ?? _screen.Widgets.LastOrDefault());
            RenderNow();
        }

        // ==================================================================
        // Window chrome
        // ==================================================================

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private void UseDarkTitleBar()
        {
            try
            {
                int on = 1;
                DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref on, sizeof(int));
            }
            catch { }
        }
    }

    /// <summary>The shareable .afodash file: one designed dashboard, with its images and LED lights.</summary>
    internal sealed class DesignPackage
    {
        /// <summary>The file's identity, from before the rename to Almost Fast Dash; kept so every .afodash file stays readable.</summary>
        public string Format { get; set; } = "AlmostFastOverlays.Dashboard";
        /// <summary>2 added the LED lights; files without them still import.</summary>
        public int Version { get; set; } = 2;
        public string Name { get; set; }
        public string AccentColor { get; set; }
        public DashLayout Design { get; set; }
        public LightProfile Lights { get; set; }

        public static DesignPackage From(DashboardSettings dashboard, DashLayout design = null) => new DesignPackage
        {
            Name = dashboard.Name, AccentColor = dashboard.AccentColor, Design = design ?? dashboard.Design,
            Lights = dashboard.Lights == null ? null : SettingsStore.Clone(dashboard.Lights)
        };

        public static void Write(string path, DesignPackage package) =>
            File.WriteAllText(path, SettingsStore.ToJson(package));

        public static DesignPackage Read(string path)
        {
            var serializer = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 256 };
            DesignPackage package = serializer.Deserialize<DesignPackage>(File.ReadAllText(path));
            if (package?.Design == null || package.Format != "AlmostFastOverlays.Dashboard")
                throw new InvalidDataException("This is not an Almost Fast Dash dashboard file.");
            package.Design.Normalize();
            if (string.IsNullOrWhiteSpace(package.Name)) package.Name = System.IO.Path.GetFileNameWithoutExtension(path);
            return package;
        }
    }
}
