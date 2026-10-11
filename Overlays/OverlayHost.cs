using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace AlmostFastOverlays
{
    /// <summary>
    /// The overlay process (--overlays): one transparent, always-on-top window per
    /// enabled overlay, fed from its own telemetry reader. Locked overlays let clicks
    /// through to the game; unlocked ones can be dragged and remember their place.
    /// It runs apart from the dashboard so an overlay problem can never touch the wheel.
    /// </summary>
    internal sealed class OverlayHost
    {
        public static readonly string StatusPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "overlays.running");
        public static readonly string StopPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "overlays.stop");
        public const string StopEventName = @"Local\AlmostFastOverlays.StopOverlays";
        public const string MutexName = @"Local\AlmostFastOverlays.Overlays";

        private readonly IRacingTelemetrySource _telemetry;
        private readonly DiagnosticLogger _log = new DiagnosticLogger(false);
        private readonly Dictionary<string, OverlayWindow> _windows = new Dictionary<string, OverlayWindow>();
        private readonly DispatcherTimer _timer;
        private readonly EventWaitHandle _stop;
        private AppSettings _settings;
        private DateTime _settingsWriteUtc;
        private DateTime _nextSettingsCheckUtc;
        private int _sampleTick;

        public OverlayHost()
        {
            _stop = new EventWaitHandle(false, EventResetMode.AutoReset, StopEventName);
            while (_stop.WaitOne(0)) { }
            if (File.Exists(StopPath)) File.Delete(StopPath);
            File.WriteAllText(StatusPath, Process.GetCurrentProcess().Id.ToString());
            _settings = SettingsStore.Load();
            _settingsWriteUtc = SettingsStore.LastWriteUtc();
            _telemetry = new IRacingTelemetrySource(_log, null) { CollectStandings = true };
            _telemetry.Start();
            ApplySettings();
            _timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(100) };
            _timer.Tick += (s, e) => Tick();
            _timer.Start();
        }

        private void Tick()
        {
            if (_stop.WaitOne(0) || File.Exists(StopPath))
            {
                Shutdown();
                return;
            }
            if (DateTime.UtcNow >= _nextSettingsCheckUtc)
            {
                _nextSettingsCheckUtc = DateTime.UtcNow.AddSeconds(1);
                DateTime write = SettingsStore.LastWriteUtc();
                if (write != _settingsWriteUtc)
                {
                    _settingsWriteUtc = write;
                    _settings = SettingsStore.Load();
                    ApplySettings();
                }
            }

            TelemetrySnapshot t = _telemetry.GetSnapshot();
            bool editing = !_settings.Overlays.Locked;
            StandingsSnapshot standings = t.Connected ? t.Standings : null;
            // While arranging overlays without iRacing, show a sample field that keeps moving.
            if (standings == null && editing) standings = StandingsSample.Create(++_sampleTick / 30);
            foreach (OverlayWindow window in _windows.Values)
            {
                bool show = editing || (t.Connected && (window.Config.ShowInGarage || !(standings?.InGarage ?? false)));
                window.Show(show);
                if (show) window.Update(t, standings ?? StandingsSnapshot.Empty, editing);
            }
        }

        private void ApplySettings()
        {
            Dictionary<string, double[]> positions = OverlayPositions.Load();
            var enabled = _settings.Overlays.Items.Where(i => i.Enabled).ToList();
            foreach (string id in _windows.Keys.Where(id => enabled.All(i => i.Id != id)).ToList())
            {
                _windows[id].Close();
                _windows.Remove(id);
            }
            int index = 0;
            foreach (OverlayConfig config in enabled)
            {
                if (!_windows.TryGetValue(config.Id, out OverlayWindow window))
                {
                    window = new OverlayWindow(config);
                    // Toasts start in the middle of the screen, a little above centre; tables near the top left.
                    double[] start = config.Type == "toasts"
                        ? new[] { Math.Round((SystemParameters.PrimaryScreenWidth - ToastView.CardWidth) / 2), Math.Round(SystemParameters.PrimaryScreenHeight * 0.2) }
                        : new[] { 40.0 + index * 30, 120.0 + index * 30 };
                    double[] place = positions.TryGetValue(config.Id, out double[] p) && p.Length == 2 ? p : start;
                    window.Left = place[0];
                    window.Top = place[1];
                    _windows[config.Id] = window;
                }
                window.Config = config;
                window.SetLocked(_settings.Overlays.Locked);
                index++;
            }
        }

        private void Shutdown()
        {
            _timer.Stop();
            foreach (OverlayWindow window in _windows.Values) window.Close();
            _windows.Clear();
            _telemetry.Dispose();
            _log.Dispose();
            try
            {
                if (File.Exists(StatusPath) && File.ReadAllText(StatusPath).Trim() == Process.GetCurrentProcess().Id.ToString()) File.Delete(StatusPath);
                if (File.Exists(StopPath)) File.Delete(StopPath);
            }
            catch { }
            Application.Current.Shutdown();
        }

        /// <summary>True when an overlay process is running.</summary>
        public static bool IsRunning()
        {
            try
            {
                if (!File.Exists(StatusPath)) return false;
                int id = int.Parse(File.ReadAllText(StatusPath).Trim());
                using (Process process = Process.GetProcessById(id))
                    return !process.HasExited && string.Equals(process.ProcessName, "AlmostFastOverlays", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        /// <summary>
        /// Overlay animations run at up to 120 frames a second. On a high-refresh display WPF
        /// would otherwise draw at the full refresh rate (165 Hz and up) while anything moves;
        /// 120 looks just as fluid for these short slides and fades at about half the CPU.
        /// Must run before any animation is created.
        /// </summary>
        public static void LimitAnimationRate()
        {
            System.Windows.Media.Animation.Timeline.DesiredFrameRateProperty.OverrideMetadata(
                typeof(System.Windows.Media.Animation.Timeline), new FrameworkPropertyMetadata { DefaultValue = AnimationFrameRate });
        }

        public const int AnimationFrameRate = 120;

        public static void RequestStop()
        {
            try { File.WriteAllText(StopPath, DateTime.UtcNow.ToString("O")); } catch { }
            try { using (var stop = new EventWaitHandle(false, EventResetMode.AutoReset, StopEventName)) stop.Set(); } catch { }
        }
    }

    /// <summary>A borderless, transparent, topmost window hosting one overlay.</summary>
    internal sealed class OverlayWindow : Window
    {
        private const int GwlExStyle = -20, WsExTransparent = 0x20, WsExToolWindow = 0x80, WsExLayered = 0x80000, WsExNoActivate = 0x08000000;
        private readonly FrameworkElement _view;
        private readonly Border _editFrame;
        private readonly TextBlock _editLabel;
        private readonly DispatcherTimer _savePosition;
        private bool _locked = true;
        private bool _visible;

        public OverlayWindow(OverlayConfig config)
        {
            Config = config;
            _view = config.Type == "toasts" ? (FrameworkElement)new ToastView() : new StandingsView();
            // See-through on the GPU (not WPF's AllowsTransparency, which copies every frame
            // back from the graphics card), so animations keep up with the display.
            GlassBackdrop.MakeClear(this);
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Title = "Almost Fast Overlays · " + config.Name;

            _editLabel = new TextBlock
            {
                Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 4, 10, 4),
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI")
            };
            var labelChip = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(230, 45, 212, 191)), CornerRadius = new CornerRadius(8), Child = _editLabel,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -30, 0, 0)
            };
            _editFrame = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(45, 212, 191)), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(16),
                Margin = new Thickness(-3), IsHitTestVisible = false, Visibility = Visibility.Collapsed
            };
            var root = new Grid { Margin = new Thickness(6, 36, 6, 6) };
            root.Children.Add(_view);
            root.Children.Add(_editFrame);
            root.Children.Add(labelChip);
            labelChip.SetBinding(VisibilityProperty, new System.Windows.Data.Binding("Visibility") { Source = _editFrame });
            Content = root;

            _savePosition = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _savePosition.Tick += (s, e) => { _savePosition.Stop(); OverlayPositions.Save(Config.Id, Left, Top); };
            LocationChanged += (s, e) => { if (!_locked) { _savePosition.Stop(); _savePosition.Start(); } };
            MouseLeftButtonDown += (s, e) => { if (!_locked) DragMove(); };
            SourceInitialized += (s, e) => ApplyStyle();
        }

        public OverlayConfig Config { get; set; }

        /// <summary>What the window shows: the timing table or the toast stack.</summary>
        internal FrameworkElement View => _view;

        public void Update(TelemetrySnapshot telemetry, StandingsSnapshot standings, bool editing)
        {
            if (_view is ToastView toasts) toasts.Update(telemetry, standings, Config, editing);
            else ((StandingsView)_view).Update(standings, Config);
            _editLabel.Text = "✥  " + Config.Name + "  ·  drag to move";
        }

        public void Show(bool show)
        {
            if (show == _visible) return;
            _visible = show;
            if (show) base.Show(); else Hide();
        }

        public void SetLocked(bool locked)
        {
            _locked = locked;
            _editFrame.Visibility = locked ? Visibility.Collapsed : Visibility.Visible;
            Cursor = locked ? null : Cursors.SizeAll;
            ApplyStyle();
        }

        /// <summary>Tool window that never takes focus from the game; locked overlays are also click-through.</summary>
        private void ApplyStyle() => MakeClickThrough(this, _locked);

        /// <summary>
        /// A tool window that never takes focus from the game; when locked, clicks pass
        /// straight through to whatever is underneath. Windows needs the layered style for
        /// that, and a GPU-composited window then needs its opacity set or it draws nothing.
        /// </summary>
        internal static void MakeClickThrough(Window window, bool locked)
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            int style = GetWindowLong(handle, GwlExStyle) | WsExToolWindow | WsExLayered | WsExNoActivate;
            style = locked ? style | WsExTransparent : style & ~WsExTransparent;
            SetWindowLong(handle, GwlExStyle, style);
            if (!window.AllowsTransparency) SetLayeredWindowAttributes(handle, 0, 255, LwaAlpha);
        }

        private const int LwaAlpha = 0x2;
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);
    }
}
