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

namespace AlmostFastDash.Designer
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
            InitGuides();
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
}
