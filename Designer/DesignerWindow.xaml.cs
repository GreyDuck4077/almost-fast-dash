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
using DrawingRect = System.Drawing.RectangleF;

namespace AlmostFastOverlays
{
    /// <summary>
    /// The dashboard designer: a canvas for placing and styling widgets, with a
    /// toolbox, layers, theme and an inspector. It edits a working copy of the
    /// design and writes it back on Save .
    /// </summary>
    public partial class DesignerWindow : Window
    {
        private const double CanvasW = 854, CanvasH = 480, GridSize = 4, GridDraw = 16;

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

        private enum DragMode { None, Move, Resize, Marquee }
        private DragMode _drag;
        private Point _dragStart;
        private int _handle;
        private Dictionary<DashWidget, Rect> _originals;
        private Rect _marquee;
        private readonly List<Tuple<Point, Point>> _guides = new List<Tuple<Point, Point>>();
        private bool _dragChanged;

        private SyntheticTelemetry _synthetic = new SyntheticTelemetry();
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

            _frame = new WriteableBitmap((int)CanvasW, (int)CanvasH, 96, 96, PixelFormats.Bgra32, null);
            Frame.Source = _frame;

            BuildToolbox();
            BuildScreensCombo();
            BuildPreviewCombo();
            BuildThemeEditor();
            RefreshLayers();
            RefreshInspector();
            UpdateUndoButtons();
            UpdateSaveState();

            PreviewKeyDown += Window_PreviewKeyDown;
            Loaded += (s, e) => { ZoomFit(); RenderNow(); Overlay.Focus(); };
            Closing += Window_Closing;
            Closed += (s, e) => { StopLive(); _animationTimer.Stop(); _renderer?.Dispose(); };
            SizeChanged += (s, e) => { if (_fitOnResize) ZoomFit(); };
        }

        private bool _fitOnResize = true;

        // ==================================================================
        // Rendering
        // ==================================================================

        private void RequestRender()
        {
            _renderPending = true;
            if (!_renderTimer.IsEnabled) _renderTimer.Start();
        }

        private void RenderNow()
        {
            _renderPending = false;
            if (_rendererDirty || _renderer == null)
            {
                _renderer?.Dispose();
                _renderer = new LayoutRenderer((int)CanvasW, (int)CanvasH,
                    new DashboardSettings { Layout = Layouts.Custom, Design = _layout, AccentColor = _dashboard.AccentColor }, _units) { CacheStatic = false };
                _rendererDirty = false;
            }

            TelemetrySnapshot snapshot = PreviewSnapshot(out bool flash, out int previousGear, out float progress);
            bool flagTakeover = _layout.FullScreenFlags && snapshot.Flag != RacingFlag.None;
            using (DrawingBitmap bitmap = _renderer.Render(snapshot, flash, previousGear, progress, flagTakeover ? null : _screen))
                CopyToFrame(bitmap);
            UpdateOverlay();
            UpdateFormulaResults(snapshot, flash);
            MarkLayerErrors();
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
            using (IDashboardRenderer renderer = DashboardRendererFactory.Create((int)CanvasW, (int)CanvasH, dashboard, units))
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
            "Green flag", "Yellow flag", "White flag", "Blue flag", "Black flag", "Checkered flag"
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
            bool moving = PreviewState == "Animated lap" || PreviewState == "Live iRacing" || PreviewState == "Shift point";
            if (moving) _animationTimer.Start(); else _animationTimer.Stop();
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
                case "Green flag": snapshot = IRacingTelemetrySource.CreatePreviewSnapshot(RacingFlag.Green); break;
                case "Yellow flag": snapshot = IRacingTelemetrySource.CreatePreviewSnapshot(RacingFlag.Yellow); break;
                case "White flag": snapshot = IRacingTelemetrySource.CreatePreviewSnapshot(RacingFlag.White); break;
                case "Blue flag": snapshot = IRacingTelemetrySource.CreatePreviewSnapshot(RacingFlag.BlueYellowStripe); break;
                case "Black flag": snapshot = IRacingTelemetrySource.CreatePreviewSnapshot(RacingFlag.Black); break;
                case "Checkered flag": snapshot = IRacingTelemetrySource.CreatePreviewSnapshot(RacingFlag.Checkered); break;
                default: snapshot = IRacingTelemetrySource.CreatePreviewSnapshot(); break;
            }
            previousGear = snapshot.Gear;
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
                AddRect(RectOf(w), accent, 1.5, false);

            if (_selection.Count > 1)
                AddRect(Union(_selection), accent, 1, true);

            if (_selection.Count == 1 && !_selection[0].Locked)
            {
                Rect r = RectOf(_selection[0]);
                foreach (Point p in HandlePoints(r))
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
                    Child = new TextBlock { Text = Math.Round(r.Width) + " × " + Math.Round(r.Height), FontSize = 11 * Px, Foreground = (Brush)FindResource("Brush.OnAccent"), FontWeight = FontWeights.SemiBold }
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

        private int HandleAt(Point p)
        {
            if (_selection.Count != 1 || _selection[0].Locked) return -1;
            Point[] points = HandlePoints(RectOf(_selection[0]));
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
                if (r.Contains(p)) return w;
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
            Point p = e.GetPosition(Overlay);
            _dragStart = p;
            _dragChanged = false;
            bool additive = (Keyboard.Modifiers & (ModifierKeys.Shift | ModifierKeys.Control)) != 0;

            int handle = HandleAt(p);
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
                if (e.ClickCount == 2)
                {
                    Select(hit);
                    FocusPrimaryEditor();
                    return;
                }
                if (additive)
                {
                    if (_selection.Contains(hit)) _selection.Remove(hit); else _selection.Add(hit);
                    SelectionChanged();
                }
                else if (!_selection.Contains(hit)) Select(hit);
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
            bool free = (Keyboard.Modifiers & ModifierKeys.Alt) != 0 || SnapToggle.IsChecked != true;

            switch (_drag)
            {
                case DragMode.None:
                {
                    int handle = HandleAt(p);
                    Overlay.Cursor = handle >= 0 ? HandleCursors[handle] : WidgetAt(p) != null ? Cursors.SizeAll : Cursors.Arrow;
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
            DragMode mode = _drag;
            _drag = DragMode.None;
            Overlay.ReleaseMouseCapture();
            _guides.Clear();
            if (mode == DragMode.Marquee) SelectionChanged();
            else if ((mode == DragMode.Move || mode == DragMode.Resize) && _dragChanged)
            {
                Commit(mode == DragMode.Move ? "Move" : "Resize");
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
                menu.Items.Add(MenuItem("Save as reusable block…", null, SaveSelectionAsBlock));
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
            DashWidget w = _selection[0];
            Rect o = _originals[w];
            double left = o.Left, top = o.Top, right = o.Right, bottom = o.Bottom;
            bool l = _handle == 0 || _handle == 6 || _handle == 7, r = _handle == 2 || _handle == 3 || _handle == 4;
            bool t = _handle == 0 || _handle == 1 || _handle == 2, b = _handle == 4 || _handle == 5 || _handle == 6;
            _guides.Clear();
            double x = p.X, y = p.Y;
            if (!free)
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
            w.X = Math.Round(left);
            w.Y = Math.Round(top);
            w.W = Math.Max(2, Math.Round(right - left));
            w.H = Math.Max(2, Math.Round(bottom - top));
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
            e.Effects = e.Data.GetDataPresent("afo-widget") || e.Data.GetDataPresent("afo-block") ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void Overlay_Drop(object sender, DragEventArgs e)
        {
            Point p = e.GetPosition(Overlay);
            if (e.Data.GetData("afo-widget") is string type)
            {
                WidgetType info = WidgetCatalog.Find(type);
                AddWidgets(new[] { WidgetCatalog.Create(type, p.X - info.DefaultW / 2, p.Y - info.DefaultH / 2) }, "Add " + info.Name);
            }
            else if (e.Data.GetData("afo-block") is DesignBlock block)
            {
                List<DashWidget> widgets = block.Create();
                Rect bounds = Union(widgets);
                foreach (DashWidget w in widgets) { w.X += Math.Round(p.X - bounds.Width / 2 - bounds.X); w.Y += Math.Round(p.Y - bounds.Height / 2 - bounds.Y); }
                AddWidgets(widgets, "Add " + block.Name);
            }
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

        internal void AddWidgets(IList<DashWidget> widgets, string label)
        {
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
            var copies = _screen.Widgets.Where(_selection.Contains).Select(w =>
            {
                DashWidget copy = SettingsStore.Clone(w);
                copy.Id = DashScreen.NewId("w");
                copy.X += offset;
                copy.Y += offset;
                copy.Locked = false;
                return copy;
            }).ToList();
            AddWidgets(copies, "Duplicate");
            return copies;
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
            string json = Snapshot();
            if (json == _committed)
            {
                // The accent lives on the dashboard, not the design, so it can change without a new snapshot.
                if (Dirty) SaveSoon();
                UpdateSaveState();
                return;
            }
            bool merge = label == _lastCommitLabel && (DateTime.UtcNow - _lastCommitUtc).TotalMilliseconds < 1200 && label != "Add" && _undo.Count > 0;
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
                Filter = "Almost Fast Overlays dashboard (*.afodash)|*.afodash",
                FileName = SafeFileName(_dashboard.Name) + ".afodash"
            };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                DesignPackage.Write(dialog.FileName, new DesignPackage { Name = _dashboard.Name, AccentColor = _dashboard.AccentColor, Design = _layout });
                StatusText.Text = "Exported to " + dialog.FileName;
            }
            catch (Exception exception)
            {
                StatusText.Text = "Export failed: " + exception.Message;
            }
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Import dashboard design", Filter = "Almost Fast Overlays dashboard (*.afodash)|*.afodash|All files|*.*" };
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

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0, shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (ctrl && key == Key.S) { SaveDesign(false); e.Handled = true; return; }

            // Text fields keep their own keys.
            if (Keyboard.FocusedElement is TextBox || Keyboard.FocusedElement is ComboBox) return;

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
                case Key.C when ctrl: CopySelection(); break;
                case Key.X when ctrl: CopySelection(); DeleteSelection(); break;
                case Key.V when ctrl: Paste(); break;
                case Key.A when ctrl: SelectAll(); break;
                case Key.L when ctrl: ToggleLock(); break;
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
            if (scenario.Contains("layers")) TabLayers.IsChecked = true;
            if (scenario.Contains("multi"))
            {
                _selection.Clear();
                _selection.AddRange(_screen.Widgets.Where(w => w.Type == "metric").Take(4));
                SelectionChanged();
            }
            else if (scenario.Contains("empty")) Select(null);
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

    /// <summary>The shareable .afodash file: one designed dashboard.</summary>
    internal sealed class DesignPackage
    {
        public string Format { get; set; } = "AlmostFastOverlays.Dashboard";
        public int Version { get; set; } = 1;
        public string Name { get; set; }
        public string AccentColor { get; set; }
        public DashLayout Design { get; set; }

        public static void Write(string path, DesignPackage package) =>
            File.WriteAllText(path, SettingsStore.ToJson(package));

        public static DesignPackage Read(string path)
        {
            var serializer = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 256 };
            DesignPackage package = serializer.Deserialize<DesignPackage>(File.ReadAllText(path));
            if (package?.Design == null || package.Format != "AlmostFastOverlays.Dashboard")
                throw new InvalidDataException("This is not an Almost Fast Overlays dashboard file.");
            package.Design.Normalize();
            if (string.IsNullOrWhiteSpace(package.Name)) package.Name = System.IO.Path.GetFileNameWithoutExtension(path);
            return package;
        }
    }
}
