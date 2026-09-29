using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using System.Threading.Tasks;
using System.Drawing.Imaging;
using System.Windows.Threading;

namespace DV5Direct
{
    public partial class SettingsWindow : Window
    {
        private readonly AppSettings _settings;
        private DashboardSettings _dashboard;
        private bool _loading = true;
        private bool _dashboardOperationInProgress;
        private readonly DispatcherTimer _statusTimer;

        public SettingsWindow()
        {
            InitializeComponent();
            _settings = SettingsStore.Load();
            _dashboard = _settings.GetDefaultDashboard();
            PopulateChoices();
            HookEvents();
            LoadValues();
            BuildGalleryCards();
            _loading = false;
            ShowGallery();
            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _statusTimer.Tick += (s, e) => { if (!_dashboardOperationInProgress) RefreshDashboardStatus(); };
            _statusTimer.Start();
            RefreshDashboardStatus();
        }

        private void PopulateChoices()
        {
            CenterPattern.ItemsSource = new[] { "Edges to center", "Left to right", "Right to left", "Center outward" };
            string[] profiles = { "Green / Yellow / Red", "Blue / Purple / Red", "Single color", "Custom", "Red" };
            CenterProfile.ItemsSource = profiles;
            LeftProfile.ItemsSource = profiles;
            RightProfile.ItemsSource = profiles;
            string[] functions = { "Blind spot", "RPM", "Brake pressure", "Disabled" };
            LeftFunction.ItemsSource = functions;
            RightFunction.ItemsSource = functions;
        }

        private void LoadValues()
        {
            _loading = true;
            DashboardNav.Content = "◈   " + _dashboard.Name.ToUpperInvariant();
            EditorTitle.Text = _dashboard.Name.ToUpperInvariant();
            OvalCheck.IsChecked = _dashboard.RaceTypes.Contains("Oval");
            RoadCheck.IsChecked = _dashboard.RaceTypes.Contains("Road");
            DirtOvalCheck.IsChecked = _dashboard.RaceTypes.Contains("Dirt Oval");
            DirtRoadCheck.IsChecked = _dashboard.RaceTypes.Contains("Dirt Road");
            SetAccentVisual();
            CenterPattern.SelectedItem = _dashboard.CenterLeds.Pattern;
            CenterProfile.SelectedItem = _dashboard.CenterLeds.ColorProfile;
            LeftFunction.SelectedItem = _dashboard.LeftLeds.Function;
            LeftProfile.SelectedItem = _dashboard.LeftLeds.ColorProfile;
            RightFunction.SelectedItem = _dashboard.RightLeds.Function;
            RightProfile.SelectedItem = _dashboard.RightLeds.ColorProfile;
            CenterBrightness.Value = _dashboard.CenterLeds.Brightness;
            LeftBrightness.Value = _dashboard.LeftLeds.Brightness;
            RightBrightness.Value = _dashboard.RightLeds.Brightness;
            UpdateBrightnessLabels();
            BlinkCountBox.Text = _dashboard.Alerts.BlinkCount.ToString();
            BlinkLengthBox.Text = _dashboard.Alerts.BlinkMilliseconds.ToString();
            HoldBox.Text = _dashboard.Alerts.HoldMilliseconds.ToString();
            _loading = false;
        }

        private void HookEvents()
        {
            OvalCheck.Checked += AssignmentChanged; OvalCheck.Unchecked += AssignmentChanged;
            RoadCheck.Checked += AssignmentChanged; RoadCheck.Unchecked += AssignmentChanged;
            DirtOvalCheck.Checked += AssignmentChanged; DirtOvalCheck.Unchecked += AssignmentChanged;
            DirtRoadCheck.Checked += AssignmentChanged; DirtRoadCheck.Unchecked += AssignmentChanged;
            CenterPattern.SelectionChanged += ChoicesChanged;
            CenterProfile.SelectionChanged += ChoicesChanged;
            LeftFunction.SelectionChanged += ChoicesChanged;
            LeftProfile.SelectionChanged += ChoicesChanged;
            RightFunction.SelectionChanged += ChoicesChanged;
            RightProfile.SelectionChanged += ChoicesChanged;
            CenterBrightness.ValueChanged += BrightnessChanged;
            LeftBrightness.ValueChanged += BrightnessChanged;
            RightBrightness.ValueChanged += BrightnessChanged;
            BlinkCountBox.LostFocus += TimingChanged;
            BlinkLengthBox.LostFocus += TimingChanged;
            HoldBox.LostFocus += TimingChanged;
        }

        private void AssignmentChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _dashboard.RaceTypes.Clear();
            if (OvalCheck.IsChecked == true) _dashboard.RaceTypes.Add("Oval");
            if (RoadCheck.IsChecked == true) _dashboard.RaceTypes.Add("Road");
            if (DirtOvalCheck.IsChecked == true) _dashboard.RaceTypes.Add("Dirt Oval");
            if (DirtRoadCheck.IsChecked == true) _dashboard.RaceTypes.Add("Dirt Road");
            Save();
        }

        private void ChoicesChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            _dashboard.CenterLeds.Pattern = Selected(CenterPattern, _dashboard.CenterLeds.Pattern);
            _dashboard.CenterLeds.ColorProfile = Selected(CenterProfile, _dashboard.CenterLeds.ColorProfile);
            _dashboard.LeftLeds.Function = Selected(LeftFunction, _dashboard.LeftLeds.Function);
            _dashboard.LeftLeds.ColorProfile = Selected(LeftProfile, _dashboard.LeftLeds.ColorProfile);
            _dashboard.RightLeds.Function = Selected(RightFunction, _dashboard.RightLeds.Function);
            _dashboard.RightLeds.ColorProfile = Selected(RightProfile, _dashboard.RightLeds.ColorProfile);
            Save();
        }

        private void BrightnessChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading) return;
            _dashboard.CenterLeds.Brightness = (int)CenterBrightness.Value;
            _dashboard.LeftLeds.Brightness = (int)LeftBrightness.Value;
            _dashboard.RightLeds.Brightness = (int)RightBrightness.Value;
            UpdateBrightnessLabels();
            Save();
        }

        private void TimingChanged(object sender, RoutedEventArgs e)
        {
            _dashboard.Alerts.BlinkCount = Parse(BlinkCountBox.Text, 1, 20, _dashboard.Alerts.BlinkCount);
            _dashboard.Alerts.BlinkMilliseconds = Parse(BlinkLengthBox.Text, 50, 2000, _dashboard.Alerts.BlinkMilliseconds);
            _dashboard.Alerts.HoldMilliseconds = Parse(HoldBox.Text, 0, 30000, _dashboard.Alerts.HoldMilliseconds);
            BlinkCountBox.Text = _dashboard.Alerts.BlinkCount.ToString();
            BlinkLengthBox.Text = _dashboard.Alerts.BlinkMilliseconds.ToString();
            HoldBox.Text = _dashboard.Alerts.HoldMilliseconds.ToString();
            Save();
        }

        private void AccentButton_Click(object sender, RoutedEventArgs e)
        {
            if (WindowsColorPicker.TryChoose(_dashboard.AccentColor, out string selectedColor))
            {
                _dashboard.AccentColor = selectedColor;
                SetAccentVisual();
                BuildGalleryCards();
                Save("ACCENT SAVED — APPLIES ON NEXT START");
            }
        }

        private void CenterColors_Click(object sender, RoutedEventArgs e) => EditLedColors("Center LED colors", _dashboard.CenterLeds);
        private void LeftColors_Click(object sender, RoutedEventArgs e) => EditLedColors("Left LED colors", _dashboard.LeftLeds);
        private void RightColors_Click(object sender, RoutedEventArgs e) => EditLedColors("Right LED colors", _dashboard.RightLeds);

        private void EditLedColors(string title, LedBankSettings bank)
        {
            var dialog = new ColorEditorWindow(title, bank.LowColor, bank.MidColor, bank.HighColor) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                bank.LowColor = dialog.LowColor;
                bank.MidColor = dialog.MiddleColor;
                bank.HighColor = dialog.HighColor;
                bank.ColorProfile = "Custom";
                LoadProfileSelections();
                Save("CUSTOM LED COLORS SAVED");
            }
        }

        private void LoadProfileSelections()
        {
            _loading = true;
            CenterProfile.SelectedItem = _dashboard.CenterLeds.ColorProfile;
            LeftProfile.SelectedItem = _dashboard.LeftLeds.ColorProfile;
            RightProfile.SelectedItem = _dashboard.RightLeds.ColorProfile;
            _loading = false;
        }

        private void ShowGallery_Click(object sender, RoutedEventArgs e) => ShowGallery();
        private void ShowDashboard_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is DashboardSettings selected)
            {
                _dashboard = selected;
                LoadValues();
            }
            GalleryPage.Visibility = Visibility.Collapsed;
            EditorPage.Visibility = Visibility.Visible;
        }

        private void ShowGallery()
        {
            EditorPage.Visibility = Visibility.Collapsed;
            GalleryPage.Visibility = Visibility.Visible;
        }

        private async void StartDashboard_Click(object sender, RoutedEventArgs e)
        {
            TimingChanged(null, null);
            _dashboardOperationInProgress = true;
            StartDashboardButton.IsEnabled = false;
            bool wasRunning = IsDashboardRunning();
            if (wasRunning)
            {
                SetDashboardStatus("●  DASHBOARD STOPPING", Color.FromRgb(244, 190, 55));
                Launch("--stop");
                await WaitForDashboardState(false, 10000);
            }
            SetDashboardStatus(wasRunning ? "●  DASHBOARD RESTARTING" : "●  DASHBOARD STARTING", Color.FromRgb(244, 190, 55));
            Launch("--run");
            bool running = await WaitForDashboardState(true, 10000);
            _dashboardOperationInProgress = false;
            StartDashboardButton.IsEnabled = true;
            if (running) SetRunningStatus(); else SetStoppedStatus("●  DASHBOARD COULD NOT START");
        }

        private async void StopDashboard_Click(object sender, RoutedEventArgs e)
        {
            _dashboardOperationInProgress = true;
            SetDashboardStatus("●  DASHBOARD STOPPING", Color.FromRgb(244, 190, 55));
            Launch("--stop");
            bool stopped = await WaitForDashboardState(false, 10000);
            _dashboardOperationInProgress = false;
            if (stopped) SetStoppedStatus(); else SetDashboardStatus("●  DASHBOARD DID NOT STOP", Color.FromRgb(235, 70, 82));
        }

        private static Process Launch(string arguments) => Process.Start(new ProcessStartInfo
        {
            FileName = Assembly.GetEntryAssembly().Location,
            Arguments = arguments,
            WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
            UseShellExecute = true
        });

        private async Task<bool> WaitForDashboardState(bool expectedRunning, int timeoutMilliseconds)
        {
            int waited = 0;
            while (waited < timeoutMilliseconds)
            {
                if (IsDashboardRunning() == expectedRunning) return true;
                await Task.Delay(200);
                waited += 200;
            }
            return IsDashboardRunning() == expectedRunning;
        }

        private static bool IsDashboardRunning()
        {
            try
            {
                if (!File.Exists(Program.DashboardStatusPath)) return false;
                if (!int.TryParse(File.ReadAllText(Program.DashboardStatusPath).Trim(), out int pid)) return false;
                Process process = Process.GetProcessById(pid);
                return !process.HasExited && string.Equals(process.ProcessName, "DV5Direct", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private void RefreshDashboardStatus()
        {
            if (IsDashboardRunning()) SetRunningStatus(); else SetStoppedStatus();
        }

        private void SetRunningStatus()
        {
            SetDashboardStatus("●  DASHBOARD RUNNING", Color.FromRgb(92, 220, 155));
            StartDashboardButton.Content = "↻  RESTART DASHBOARD";
        }

        private void SetStoppedStatus(string text = "●  DASHBOARD STOPPED")
        {
            SetDashboardStatus(text, Color.FromRgb(235, 70, 82));
            StartDashboardButton.Content = "▶  START DASHBOARD";
        }

        private void SetDashboardStatus(string text, Color color)
        {
            StatusText.Text = text;
            StatusText.Foreground = new SolidColorBrush(color);
            bool warning = color.R > 200 && color.G > 120;
            bool failure = color.R > 180 && color.G < 120;
            StatusBadge.Background = new SolidColorBrush(
                warning ? Color.FromRgb(52, 41, 15) :
                failure ? Color.FromRgb(55, 22, 29) :
                Color.FromRgb(16, 46, 41));
        }

        private void Save(string status = "SETTINGS SAVED")
        {
            SettingsStore.Save(_settings);
            StatusText.Text = "●  " + status;
        }

        private void SetAccentVisual()
        {
            var color = (Color)ColorConverter.ConvertFromString(_dashboard.AccentColor);
            AccentSwatch.Background = new SolidColorBrush(color);
            AccentButton.Content = _dashboard.AccentColor;
            AccentButton.Background = new SolidColorBrush(color);
            double luminance = (0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B);
            AccentButton.Foreground = luminance > 155 ? Brushes.Black : Brushes.White;
        }

        private void UpdateBrightnessLabels()
        {
            CenterBrightnessValue.Text = ((int)CenterBrightness.Value) + "%";
            LeftBrightnessValue.Text = ((int)LeftBrightness.Value) + "%";
            RightBrightnessValue.Text = ((int)RightBrightness.Value) + "%";
        }

        private void BuildGalleryCards()
        {
            if (DashboardCards == null) return;
            DashboardCards.Children.Clear();
            foreach (DashboardSettings dashboard in _settings.Dashboards)
            {
                var card = new Border
                {
                    Width = 410, Padding = new Thickness(11), Margin = new Thickness(0, 0, 18, 18),
                    Background = new SolidColorBrush(Color.FromRgb(16, 27, 43)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(42, 59, 80)), BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(16)
                };
                var stack = new StackPanel();
                stack.Children.Add(new Border
                {
                    Height = 205, CornerRadius = new CornerRadius(11), ClipToBounds = true,
                    Background = Brushes.Black, BorderBrush = new SolidColorBrush(Color.FromRgb(40, 80, 100)),
                    BorderThickness = new Thickness(1), Child = new Image { Source = CreatePreviewImage(dashboard), Stretch = Stretch.UniformToFill }
                });
                var footer = new Grid { Margin = new Thickness(5, 11, 5, 1) };
                footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var name = new StackPanel();
                name.Children.Add(new TextBlock { Text = dashboard.Name, FontSize = 17, FontWeight = FontWeights.SemiBold });
                name.Children.Add(new TextBlock { Text = dashboard.Layout == "Endurance" ? "Fuel and stint strategy" : "Premium race dashboard", Foreground = new SolidColorBrush(Color.FromRgb(139, 158, 181)), Margin = new Thickness(0, 3, 0, 0) });
                footer.Children.Add(name);
                var actions = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
                var radio = new RadioButton { Content = "DEFAULT", Foreground = new SolidColorBrush(Color.FromRgb(92, 220, 155)), FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right, IsChecked = _settings.DefaultDashboardId == dashboard.Id, GroupName = "DefaultDashboard" };
                radio.Checked += (s, e) => { if (_loading) return; _settings.DefaultDashboardId = dashboard.Id; Save("DEFAULT DASHBOARD SAVED"); };
                var edit = new Button { Content = "CUSTOMIZE  ›", Margin = new Thickness(0, 8, 0, 0), Tag = dashboard };
                edit.Click += ShowDashboard_Click;
                actions.Children.Add(radio); actions.Children.Add(edit); Grid.SetColumn(actions, 1); footer.Children.Add(actions);
                stack.Children.Add(footer); card.Child = stack; DashboardCards.Children.Add(card);
            }
        }

        private static BitmapSource CreatePreviewImage(DashboardSettings dashboard)
        {
            using (var diagnostics = new DiagnosticLogger(false))
            using (var telemetry = new IRacingTelemetrySource(diagnostics, dashboard))
            using (IDashboardRenderer renderer = DashboardRendererFactory.Create(Dv5Screen.Width, Dv5Screen.Height, dashboard))
            {
                telemetry.StartDashboardPreview();
                TelemetrySnapshot snapshot = telemetry.GetSnapshot();
                using (var frame = renderer.Render(snapshot, false, snapshot.Gear, 1f))
                using (var stream = new MemoryStream())
                {
                    frame.Save(stream, ImageFormat.Png); stream.Position = 0;
                    var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze(); return image;
                }
            }
        }

        private static string Selected(ComboBox combo, string fallback) => combo.SelectedItem?.ToString() ?? fallback;
        private static int Parse(string value, int min, int max, int fallback) => int.TryParse(value, out int result) ? Math.Max(min, Math.Min(max, result)) : fallback;
    }
}
