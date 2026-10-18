using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DrawingColor = System.Drawing.Color;

namespace AlmostFastDash.UI
{
    /// <summary>
    /// Sets up an LED device step by step: find it, name it, describe its shape (LEDs around a
    /// screen, light bars, a strip), then show the app where each LED is. The device lights one
    /// LED at a time and the user clicks where it is on the drawing; two neighbors are enough
    /// for the app to follow an ordinary chain. Nothing is filled in from known devices: every
    /// device is described from scratch, so the lights never depend on assumptions about wiring.
    /// For a device that's already set up, only its shape and map are asked again (its name, power and
    /// the rest are in its settings window).
    /// </summary>
    internal sealed partial class DeviceSetupWindow : Window
    {
        private enum Step { Find, Name, Shape, Map, Done }

        private readonly AppSettings _settings;
        private readonly DeviceSettings _existing;
        private readonly Func<bool> _dashboardRunning;
        private readonly Func<Task<bool>> _stopDashboard;
        private readonly TextBlock _stepLabel = new TextBlock(), _title = new TextBlock(), _subtitle = new TextBlock(),
            _status = new TextBlock();
        private readonly ContentControl _body = new ContentControl();
        private readonly Button _back = new Button { Content = "Back", MinWidth = 96 };
        private readonly Button _next = new Button { Content = "Continue", MinWidth = 120 };
        private Step _step;

        // The device being set up, and its shape; the LED count and groups come from the shape when it's mapped.
        private DeviceSettings _device;
        private LedLayout _layout;
        private LedPreview _preview;

        /// <summary>The device, set when setup finishes.</summary>
        public DeviceSettings Result { get; private set; }

        /// <summary>True when setup paused the running dashboard (so the caller can start it again).</summary>
        public bool StoppedDashboard { get; private set; }

        /// <summary>For --ui-snapshot: sample devices, and LEDs that go nowhere, so no hardware is touched.</summary>
        internal bool Offline { get; set; }

        /// <summary>For --ui-snapshot: opens a step directly with a sample device.</summary>
        internal void JumpTo(string step)
        {
            Offline = true;
            if (step == "find") { _device = null; Show(Step.Find); return; }
            _device = new DeviceSettings { Kind = LedDeviceKinds.Arduino, Port = "COM4", DetectedName = "SSG DV 5", Name = "Wheel LEDs" };
            _layout = step == "shape-empty" ? new LedLayout { Shape = "" }
                : new LedLayout { Shape = LedShapes.Screen, Left = 4, Top = 8, Right = 4 };
            if (step.StartsWith("map-") || step == "done")
            {
                _mapper = new LayoutMapper(_layout, step != "map-hand");
                _mapper.Place(0);
                if (step != "map-partial" && step != "map-notlit") _mapper.Place(1);
                if (step == "map-notlit") _notLit = 1;
                if (step == "map-ask" || step == "map-doubt") foreach (int led in new[] { 0, 5, 10, 15 }) _checked.Add(led);
                if (step == "map-check") { _checked.Add(3); _testLed = 3; }
                _doubted = step == "map-doubt";
            }
            Show(step == "name" ? Step.Name : step.StartsWith("shape") ? Step.Shape : step.StartsWith("map") ? Step.Map : Step.Done);
        }

        // Setting up an existing device's LEDs again: shape, map, done.
        private readonly bool _ledsOnly;

        /// <param name="existing">A device to set its LEDs up again, or null to add one.</param>
        public DeviceSetupWindow(AppSettings settings, DeviceSettings existing, Func<bool> dashboardRunning, Func<Task<bool>> stopDashboard)
        {
            _ledsOnly = existing != null;
            _settings = settings;
            _existing = existing;
            _dashboardRunning = dashboardRunning;
            _stopDashboard = stopDashboard;
            Title = existing == null ? "Add a device" : "Set up LEDs: " + existing.Name;
            Width = 860;
            Height = 720;
            MinWidth = 720;
            MinHeight = 600;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.CanResize;
            FontFamily = (FontFamily)FindResource("Font.Ui");
            FontSize = 13;
            Foreground = (Brush)FindResource("Brush.Text");
            Background = (Brush)FindResource("Brush.Bg");
            GlassBackdrop.Apply(this, 0xD8);
            Content = new Border { Padding = new Thickness(32, 26, 32, 22), Child = Frame() };
            Closed += (s, e) => { StopChase(); WatchDevice(false); _preview?.Dispose(); };
            KeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };

            if (existing == null) Show(Step.Find);
            else
            {
                _device = SettingsStore.Clone(existing);
                _layout = existing.Layout != null ? SettingsStore.Clone(existing.Layout) : new LedLayout { Shape = "" };
                Show(Step.Shape);
            }
        }

        // ------------------------------------------------------------------ frame

        private Grid Frame()
        {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
            _stepLabel.Style = (Style)FindResource("T.Overline");
            _title.Style = (Style)FindResource("T.PageTitle");
            _title.Margin = new Thickness(0, 6, 0, 0);
            _subtitle.Style = (Style)FindResource("T.Secondary");
            _subtitle.TextWrapping = TextWrapping.Wrap;
            _subtitle.Margin = new Thickness(0, 6, 0, 0);
            header.Children.Add(_stepLabel);
            header.Children.Add(_title);
            header.Children.Add(_subtitle);
            root.Children.Add(header);

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _body };
            Grid.SetRow(scroll, 1);
            root.Children.Add(scroll);

            var footer = new Grid { Margin = new Thickness(0, 18, 0, 0) };
            footer.ColumnDefinitions.Add(new ColumnDefinition());
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _status.Style = (Style)FindResource("T.Caption");
            _status.TextWrapping = TextWrapping.Wrap;
            _status.VerticalAlignment = VerticalAlignment.Center;
            _status.Margin = new Thickness(0, 0, 16, 0);
            footer.Children.Add(_status);
            _back.Margin = new Thickness(0, 0, 8, 0);
            _back.Click += (s, e) => GoBack();
            Grid.SetColumn(_back, 1);
            footer.Children.Add(_back);
            _next.Style = (Style)FindResource("Btn.Primary");
            _next.Click += (s, e) => GoNext();
            Grid.SetColumn(_next, 2);
            footer.Children.Add(_next);
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);
            return root;
        }

        private void Show(Step step)
        {
            if (_step == Step.Map && step != Step.Map) StopChase();
            WatchDevice(step == Step.Map);
            _step = step;
            _status.Text = "";
            int number = _ledsOnly ? (int)step - 1 : (int)step + 1, total = _ledsOnly ? 2 : 4;
            _stepLabel.Text = step == Step.Done ? "ALL SET" : "STEP " + number + " OF " + total;
            _back.Visibility = step == Step.Find || (step == Step.Shape && _ledsOnly) || step == Step.Done ? Visibility.Collapsed
                : Visibility.Visible;
            _next.Content = step == Step.Done ? "Finish" : "Continue";
            _next.IsEnabled = true;
            switch (step)
            {
                case Step.Find: ShowFind(); break;
                case Step.Name: ShowName(); break;
                case Step.Shape: ShowShape(); break;
                case Step.Map: ShowMap(); break;
                default: ShowDone(); break;
            }
        }

        private void GoBack()
        {
            if (_step == Step.Name) Show(Step.Find);
            else if (_step == Step.Shape) Show(Step.Name);
            else if (_step == Step.Map) Show(Step.Shape);
        }

        private void GoNext()
        {
            switch (_step)
            {
                case Step.Find: if (_device != null) Show(Step.Name); break;
                case Step.Name:
                    _device.Name = UniqueName(string.IsNullOrWhiteSpace(_nameBox?.Text) ? (_device.DetectedName ?? "LED device")
                    : _nameBox.Text.Trim());
                    Show(Step.Shape);
                    break;
                case Step.Shape:
                    if (!ShapeReady()) return;
                    Show(Step.Map);
                    break;
                case Step.Map:
                    if (_mapper == null || !_mapper.Done) return;
                    _layout.Map = _mapper.Map();
                    Show(Step.Done);
                    break;
                default:
                    Finish();
                    break;
            }
        }

        private void Finish()
        {
            _device.Layout = _layout;
            _device.LedCount = _layout.Total;
            _device.Groups = _layout.Groups();
            _device.Normalize();
            Result = _device;
            DialogResult = true;
        }

        // ------------------------------------------------------------------ 5. done

        private void ShowDone()
        {
            StopChase();
            _title.Text = _device.Name + " is ready";
            _subtitle.Text = _layout.Describe() + "." +
                (_device.Kind == LedDeviceKinds.Arduino ? " Fast updates are used automatically when the board supports them." : "");
            var body = new StackPanel();
            foreach (LayoutPiece piece in _layout.Pieces())
                body.Children.Add(new TextBlock
                {
                    Text = piece.Name + " · " + piece.Slots.Length + (piece.Slots.Length == 1 ? " LED" : " LEDs") + " · " +
                        _layout.RoleOf(piece),
                    Style = (Style)FindResource("T.Secondary"), Margin = new Thickness(0, 0, 0, 6)
                });
            // Power is asked once here for a new device; a device's settings window changes it later.
            if (_device.Kind != LedDeviceKinds.Wled && !_ledsOnly)
            {
                body.Children.Add(new TextBlock { Text = "POWER", Style = (Style)FindResource("T.Overline"),
                    Margin = new Thickness(0, 22, 0, 8) });
                body.Children.Add(DeviceUi.PowerPicker(this, _device));
            }
            if (_ledsOnly) _title.Text = _device.Name + "'s LEDs are mapped";
            _body.Content = body;
            _preview?.Show(new DrawingColor[Math.Max(1, _layout.Total)]);
        }

        private Border Card() => new Border
        {
            Background = (Brush)FindResource("Brush.SurfaceRaised"), CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16, 12, 16, 12),
            Margin = new Thickness(0, 0, 0, 8), BorderBrush = (Brush)FindResource("Brush.Border"), BorderThickness = new Thickness(1)
        };
    }
}
