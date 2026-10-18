using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using DrawingColor = System.Drawing.Color;

namespace AlmostFastDash.UI
{
    public partial class MainWindow : Window
    {
        private static readonly string[] AccentPresetColors =
            { "#2DD4BF", "#27C7D9", "#0A84FF", "#5E5CE6", "#BF5AF2", "#FF375F", "#FF9F0A", "#30D158" };

        private readonly AppSettings _settings;
        private DashboardSettings _dashboard;
        private bool _loading = true;
        private bool _dashboardOperationInProgress;
        private readonly DispatcherTimer _statusTimer;
        private readonly DispatcherTimer _saveTimer;
        private readonly DispatcherTimer _ledTimer;
        private readonly DispatcherTimer _confirmTimer;
        private bool _savePending;
        private readonly List<Border> _galleryCards = new List<Border>();
        private Button _armedButton;

        public MainWindow()
        {
            InitializeComponent();
            SourceInitialized += (s, e) => UseDarkTitleBar(); GlassBackdrop.Apply(this);
            _settings = SettingsStore.Load();
            _dashboard = _settings.GetDefaultDashboard();

            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            _saveTimer.Tick += (s, e) => FlushSave();
            _ledTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _ledTimer.Tick += (s, e) => UpdateLedPreview();
            _confirmTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            _confirmTimer.Tick += (s, e) => DisarmConfirm();

            PopulateChoices();
            BuildAccentPresets();
            HookEvents();
            LoadGeneralSettings();
            _loading = false;
            // The page chosen in Settings › Open on.
            switch (_settings.General.StartPage)
            {
                case "Devices": NavSettings.IsChecked = true; break;
                case "Settings": NavApp.IsChecked = true; break;
                default: NavGallery.IsChecked = true; break;
            }
            SetUpTray();

            MirrorLeds.Click += (s, e) => { if (MirrorLeds.IsChecked != true) ReleaseDevices(); UpdateLedPreview(); };
            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _statusTimer.Tick += (s, e) =>
            {
                if (!_dashboardOperationInProgress) RefreshDashboardStatus();
                RefreshShowOn();
                if (SettingsPage.Visibility == Visibility.Visible) RefreshDeviceStatus();
                if (GalleryPage.Visibility == Visibility.Visible) RefreshWelcome();
            };
            _statusTimer.Start();
            RefreshDashboardStatus();
            RefreshWelcome();
            RefreshWhatsNew();

            Version version = Assembly.GetExecutingAssembly().GetName().Version;
            VersionText.Text = $"Version {version.Major}.{version.Minor} · Changes save automatically";
            AboutVersion.Text = $"Almost Fast Dash {version.Major}.{version.Minor}";
            SettingsPathText.Text = SettingsStore.FilePath;
            Loaded += (s, e) => CheckForUpdate();
            Closing += (s, e) => { FlushSave(); _ledTimer.Stop(); _statusTimer.Stop(); _mirror.Dispose(); PreviewRequest.Clear(); StopLivePreview(); };
        }

        // ------------------------------------------------------------------
        // Setup
        // ------------------------------------------------------------------

        private void PopulateChoices()
        {
            PreviewState.ItemsSource = PreviewRequest.States;
            PreviewState.SelectedIndex = 0;
        }

        private void HookEvents()
        {
            BlinkCountSlider.ValueChanged += (s, e) => OnAlertsEdited();
            BlinkLengthSlider.ValueChanged += (s, e) => OnAlertsEdited();
            HoldSlider.ValueChanged += (s, e) => OnAlertsEdited();
            ScreenBrightness.ValueChanged += (s, e) =>
            {
                ScreenBrightnessValue.Text = (int)ScreenBrightness.Value + "%";
                if (_loading) return;
                _settings.General.ScreenBrightness = (int)ScreenBrightness.Value;
                Save();
            };
        }

        private void BuildAccentPresets()
        {
            foreach (string hex in AccentPresetColors)
            {
                var swatch = new Button { Style = (Style)FindResource("Swatch"), Background = BrushFor(hex), ToolTip = hex };
                swatch.Click += (s, e) => SetAccent(hex);
                AccentPresets.Children.Add(swatch);
            }
            AccentCustom.Background = RainbowBrush();
        }

        private static T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            while (child != null && !(child is T)) child = VisualTreeHelper.GetParent(child);
            return child as T;
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private Border Pill(string text, string backgroundKey, string foregroundKey, Thickness margin) => new Border
        {
            Style = (Style)FindResource("Pill"),
            Background = (Brush)FindResource(backgroundKey),
            Margin = margin,
            Child = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource(foregroundKey) }
        };

        private TextBlock GlyphIcon(string glyph) => new TextBlock { Text = glyph, Style = (Style)FindResource("T.Icon"), FontSize = 13 };

        private static SolidColorBrush BrushFor(string hex) =>
            BrushFor(SettingsStore.ParseColor(hex, DrawingColor.FromArgb(40, 50, 65)));

        private static SolidColorBrush BrushFor(DrawingColor color)
        {
            var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
            brush.Freeze();
            return brush;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private void UseDarkTitleBar()
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                int dark = 1;
                DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
                int caption = 0x00130E0B; // COLORREF (BGR) of Brush.Bg #0B0E13
                DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int));
            }
            catch { }
        }
    }
}
