using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace AlmostFastDash.Lights
{
    /// <summary>
    /// The light designer: a stack of light layers (top wins) with a live preview of
    /// the DV-5 and LB-10 LEDs. Edits a working copy of the dashboard's light profile
    /// and writes it back on Save, or continuously with "Live on device".
    /// </summary>
    public partial class LightsWindow : Window
    {
        private readonly AppSettings _settings;
        private readonly DashboardSettings _dashboard;
        private readonly Action _onSaved;
        private readonly UnitSystem _units;
        private readonly DashLayout _colorContext = new DashLayout { Theme = LayoutTheme.ForLights() };

        private LightProfile _profile;
        private LightLayer _selected;
        private LightRenderer _renderer;
        private LightFrame _lastFrame;
        private TelemetrySnapshot _lastSnapshot;
        private readonly SyntheticTelemetry _synthetic = new SyntheticTelemetry();
        private IRacingTelemetrySource _live;
        private DiagnosticLogger _liveLog;

        private readonly DispatcherTimer _frameTimer, _commitTimer, _liveSaveTimer;
        private readonly List<string> _undo = new List<string>(), _redo = new List<string>();
        private string _committed, _savedJson, _pendingCommit, _lastCommitLabel;
        private DateTime _lastCommitUtc;

        private readonly List<Ellipse[]> _deviceLeds = new List<Ellipse[]>();
        private readonly List<DropShadowEffect[]> _deviceGlows = new List<DropShadowEffect[]>();
        private List<DeviceSettings> Devices => _settings.General.Devices;
        private readonly List<FormulaBox> _formulas = new List<FormulaBox>();
        private bool _loading;

        internal LightsWindow(AppSettings settings, DashboardSettings dashboard, Action onSaved)
        {
            InitializeComponent();
            _settings = settings;
            _dashboard = dashboard;
            _onSaved = onSaved;
            _units = new UnitSystem(settings.General);
            SourceInitialized += (s, e) => UseDarkTitleBar(); GlassBackdrop.Apply(this, 0xC0);

            _profile = SettingsStore.Clone(dashboard.Lights ?? LightPresets.Refined());
            _profile.Normalize();
            _selected = _profile.Layers.FirstOrDefault();
            _committed = _savedJson = Snapshot();
            TitleText.Text = dashboard.Name;
            Title = dashboard.Name + " · Light designer";

            _frameTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
            _frameTimer.Tick += (s, e) => RenderFrame();
            _commitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            _commitTimer.Tick += (s, e) => { _commitTimer.Stop(); if (_pendingCommit != null) Commit(_pendingCommit); };
            // Every change saves itself shortly after it's made, like the rest of the app.
            _liveSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _liveSaveTimer.Tick += (s, e) => { _liveSaveTimer.Stop(); SaveProfile(true); };

            BuildStage();
            ScenarioCombo.ItemsSource = PreviewRequest.States;
            ScenarioCombo.SelectedIndex = 0;
            ScenarioCombo.SelectionChanged += (s, e) => { if (ScenarioName == "Live iRacing") StartLive(); else StopLive(); };
            HookOutputControls();
            LoadOutputControls();
            RefreshLayers();
            RefreshInspector();
            UpdateUndoButtons();
            UpdateSaveState();

            try { ScreenImage.ImageSource = DesignerWindow.RenderThumbnail(dashboard, _units); } catch { }
            PreviewKeyDown += Window_PreviewKeyDown;
            Loaded += (s, e) => _frameTimer.Start();
            Closing += Window_Closing;
            Closed += (s, e) => { _frameTimer.Stop(); StopLive(); };
        }

        // ==================================================================
        // Preview scenarios
        // ==================================================================

        private string ScenarioName => ScenarioCombo.SelectedItem as string ?? "Animated lap";

        private static TelemetrySnapshot Snap(double speed = 136, int gear = 4, double shift = 0.62, double rpm = 6120,
            RacingFlag flag = RacingFlag.None,
            BlindSpotZone left = BlindSpotZone.None, BlindSpotZone right = BlindSpotZone.None) =>
            IRacingTelemetrySource.WithPreviewExtras(new TelemetrySnapshot(
                true, gear, speed, flag, left, right, shift, SessionProgressMode.LapRace, 0, 0, 8, 18, 4, 2, 24.3, 58.104, 0.46,
                "J. MILLER", -0.46, "M. CARTER", 0.82, 22, false, 87.932, 87.521, 88.204, new bool[18], 0, 6, rpm));

        /// <summary>A fixed telemetry state for previewing lights; also used by the main window.</summary>
        internal static TelemetrySnapshot Scenario(string name)
        {
            TelemetrySnapshot t;
            switch (name)
            {
                case "Shift point": return Snap(152, 4, 1, 7960);
                case "Car left": return Snap(left: BlindSpotZone.Beside);
                case "Car right": return Snap(right: BlindSpotZone.Beside);
                case "Three wide": return Snap(left: BlindSpotZone.Beside, right: BlindSpotZone.Beside);
                case "Pit limiter": t = Snap(44, 2, 0.1, 3900); t.OnPitRoad = true; t.PitLimiter = true; return t;
                case "Over pit limit": t = Snap(52, 2, 0.2, 4300); t.OnPitRoad = true; return t;
                case "Low fuel": t = Snap(); t.FuelPerLapAverage = 13; return t;
                case "ABS working": t = Snap(96, 3, 0.3, 5200); t.AbsActive = true; t.Brake = 0.9; t.Throttle = 0; return t;
                case "Green flag": return Snap(flag: RacingFlag.Green);
                case "Yellow flag": return Snap(flag: RacingFlag.Yellow);
                case "White flag": return Snap(flag: RacingFlag.White);
                case "Blue flag": return Snap(flag: RacingFlag.BlueYellowStripe);
                case "Black flag": return Snap(flag: RacingFlag.Black);
                case "Checkered flag": return Snap(flag: RacingFlag.Checkered);
                case "Not connected": return TelemetrySnapshot.Disconnected;
                default: return Snap();
            }
        }

        private TelemetrySnapshot CurrentSnapshot()
        {
            switch (ScenarioName)
            {
                case "Animated lap": return _synthetic.Next(out _, out _, out _);
                case "Live iRacing":
                    TelemetrySnapshot live = _live?.GetSnapshot() ?? TelemetrySnapshot.Disconnected;
                    StatusText.Text = live.Connected ? "Showing live iRacing data."
                        : "Waiting for iRacing… start a session to see live lights.";
                    return live;
                default: return Scenario(ScenarioName);
            }
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

        private EvalScope CurrentScope() => new EvalScope
        {
            T = _lastSnapshot ?? Snap(), Units = _units, Theme = _colorContext.Theme, Accent = _dashboard.AccentColor
        };

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            if (ctrl && e.Key == Key.S) { SaveProfile(false); e.Handled = true; return; }
            if (Keyboard.FocusedElement is TextBox) return;
            if (ctrl && e.Key == Key.Z) { Undo(); e.Handled = true; }
            else if (ctrl && e.Key == Key.Y) { Redo(); e.Handled = true; }
            else if (ctrl && e.Key == Key.D) { Duplicate(); e.Handled = true; }
            else if (e.Key == Key.Delete) { DeleteSelected(); e.Handled = true; }
        }

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
