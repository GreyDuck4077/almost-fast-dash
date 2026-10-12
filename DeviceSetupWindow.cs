using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using DrawingColor = System.Drawing.Color;

namespace AlmostFastOverlays
{
    /// <summary>
    /// Sets up an LED device step by step: find it, name it, describe its shape (LEDs around a
    /// screen, light bars, a strip), then show the app where each LED is. The device lights one
    /// LED at a time and the user clicks where it is on the drawing; two neighbours are enough
    /// for the app to follow an ordinary chain. Nothing is filled in from known devices: every
    /// device is described from scratch, so the lights never depend on assumptions about wiring.
    /// For a device that's already set up, only its shape and map are asked again (its name, power and
    /// the rest are in its settings window).
    /// </summary>
    internal sealed class DeviceSetupWindow : Window
    {
        private enum Step { Find, Name, Shape, Map, Done }

        private readonly AppSettings _settings;
        private readonly DeviceSettings _existing;
        private readonly Func<bool> _dashboardRunning;
        private readonly Func<Task<bool>> _stopDashboard;
        private readonly TextBlock _stepLabel = new TextBlock(), _title = new TextBlock(), _subtitle = new TextBlock(), _status = new TextBlock();
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
            _layout = step == "shape-empty" ? new LedLayout { Shape = "" } : new LedLayout { Shape = LedShapes.Screen, Left = 4, Top = 8, Right = 4 };
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
            _back.Visibility = step == Step.Find || (step == Step.Shape && _ledsOnly) || step == Step.Done ? Visibility.Collapsed : Visibility.Visible;
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
                    _device.Name = UniqueName(string.IsNullOrWhiteSpace(_nameBox?.Text) ? (_device.DetectedName ?? "LED device") : _nameBox.Text.Trim());
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

        // ------------------------------------------------------------------ 1. find

        private List<DeviceSettings> _found = new List<DeviceSettings>();
        private StackPanel _foundList;

        private void ShowFind()
        {
            _title.Text = "Find your device";
            _subtitle.Text = "Plug it in, then look for it. The app asks each USB serial port whether it's an LED board; ports already set up are left alone. WLED controllers are found by their address.";
            var body = new StackPanel();
            _foundList = new StackPanel();
            body.Children.Add(_foundList);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
            var scan = _scanButton = new Button { Content = "Look again" };
            scan.Click += async (s, e) => await Scan();
            actions.Children.Add(scan);
            body.Children.Add(actions);

            body.Children.Add(new TextBlock { Text = "WLED ON YOUR NETWORK", Style = (Style)FindResource("T.Overline"), Margin = new Thickness(0, 26, 0, 8) });
            var wled = new StackPanel { Orientation = Orientation.Horizontal };
            var address = new TextBox { Width = 240, ToolTip = "The controller's IP address, such as 192.168.1.40, or its name" };
            var lookUp = new Button { Content = "Look up", Margin = new Thickness(8, 0, 0, 0) };
            async Task LookUp()
            {
                string text = address.Text.Trim();
                if (text.Length == 0 || !lookUp.IsEnabled) return;
                DeviceSettings known = _found.FirstOrDefault(d => d.Kind == LedDeviceKinds.Wled && string.Equals(d.Address, text, StringComparison.OrdinalIgnoreCase));
                if (known != null) { Select(known); return; }
                _status.Text = "Asking " + text + "…";
                lookUp.IsEnabled = false;
                Tuple<string, int> info = await Task.Run(() => WledOutput.Info(text));
                lookUp.IsEnabled = true;
                if (info == null) { _status.Text = "No WLED controller answered at " + text + ". Check the address in the WLED app, and that this PC is on the same network."; return; }
                var found = new DeviceSettings { Kind = LedDeviceKinds.Wled, Address = text, DetectedName = info.Item1, Name = info.Item1, LedCount = info.Item2 > 0 ? info.Item2 : 30 };
                _found.Add(found);
                _status.Text = "";
                RenderFound();
                Select(found);
            }
            lookUp.Click += async (s, e) => await LookUp();
            address.KeyDown += async (s, e) => { if (e.Key == Key.Enter) await LookUp(); };
            wled.Children.Add(address);
            wled.Children.Add(lookUp);
            body.Children.Add(wled);
            _body.Content = body;
            _next.IsEnabled = _device != null;
            if (Offline && _found.Count == 0)
            {
                _found.Add(new DeviceSettings { Kind = LedDeviceKinds.Arduino, Port = "COM4", DetectedName = "SSG DV 5", Name = "SSG DV 5" });
                _found.Add(new DeviceSettings { Kind = LedDeviceKinds.Arduino, Port = "COM9", DetectedName = "Arduino LED board", Name = "Arduino LED board" });
            }
            RenderFound();
            if (_found.Count == 0) Dispatcher.BeginInvoke(new Func<Task>(Scan), DispatcherPriority.Background);
        }

        private bool _scanning;
        private Button _scanButton;

        private async Task Scan()
        {
            if (_scanning || Offline) return;
            if (_dashboardRunning())
            {
                ShowNeedsStop(async () => await Scan());
                return;
            }
            _scanning = true;
            if (_scanButton != null) _scanButton.IsEnabled = false;
            _status.Text = "Looking for LED devices…";
            var skip = _settings.General.Devices.Select(d => d.ResolvePort()).Where(p => p != null).ToList();
            List<DeviceSettings> found;
            List<string> busy = new List<string>();
            try
            {
                found = await Task.Run(() =>
                {
                    List<DeviceSettings> result = LedDeviceScanner.Scan(skip, port => Dispatcher.BeginInvoke(new Action(() => _status.Text = "Looking for LED devices… " + port)), out List<string> inUse);
                    busy = inUse;
                    return result;
                });
            }
            catch (Exception exception) { found = new List<DeviceSettings>(); _status.Text = "The search stopped: " + exception.Message; }
            _scanning = false;
            if (_scanButton != null) _scanButton.IsEnabled = true;
            foreach (DeviceSettings device in found.Where(f => _found.All(x => x.Port != f.Port))) _found.Add(device);
            if (_step != Step.Find) return;
            _status.Text = busy.Count > 0
                ? string.Join(", ", busy) + (busy.Count == 1 ? " is" : " are") + " in use by another program, such as SimHub, so " + (busy.Count == 1 ? "it" : "they") + " can't be asked. Close it, then look again."
                : _found.Count == 0 ? "Nothing answered. Check the cable, then look again. Boards need the common sim-racing LED firmware or Adalight." : "";
            RenderFound();
            if (_device == null && _found.Count == 1) Select(_found[0]);
        }

        /// <summary>Setup needs the devices to itself, so a running dashboard has to pause first.</summary>
        private void ShowNeedsStop(Func<Task> then)
        {
            _foundList.Children.Clear();
            var card = Card();
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = "The dashboard is using your devices", Style = (Style)FindResource("T.Label") });
            stack.Children.Add(new TextBlock
            {
                Text = "Setup needs them to itself for a moment. The dashboard pauses now and starts again when you're done.",
                Style = (Style)FindResource("T.Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 12)
            });
            var stop = new Button { Content = "Pause the dashboard", Style = (Style)FindResource("Btn.Primary"), HorizontalAlignment = HorizontalAlignment.Left };
            stop.Click += async (s, e) =>
            {
                stop.IsEnabled = false;
                _status.Text = "Pausing the dashboard…";
                if (await _stopDashboard())
                {
                    StoppedDashboard = true;
                    await then();
                }
                else { _status.Text = "The dashboard didn't stop. Stop it from the main window, then look again."; stop.IsEnabled = true; }
            };
            stack.Children.Add(stop);
            card.Child = stack;
            _foundList.Children.Add(card);
        }

        private void RenderFound()
        {
            if (_foundList == null) return;
            _foundList.Children.Clear();
            foreach (DeviceSettings found in _found)
            {
                DeviceSettings f = found;
                var card = Card();
                card.Cursor = Cursors.Hand;
                card.BorderThickness = new Thickness(1.5);
                card.BorderBrush = (Brush)FindResource(_device != null && f.Connection == _device.Connection && f.Kind == _device.Kind ? "Brush.Accent" : "Brush.Border");
                var text = new StackPanel();
                text.Children.Add(new TextBlock { Text = f.DetectedName, Style = (Style)FindResource("T.Label") });
                text.Children.Add(new TextBlock { Text = f.Kind + " · " + f.Connection, Style = (Style)FindResource("T.Caption"), Margin = new Thickness(0, 3, 0, 0) });
                card.Child = text;
                card.MouseLeftButtonUp += (s, e) => Select(f);
                _foundList.Children.Add(card);
            }
        }

        private void Select(DeviceSettings found)
        {
            _device = SettingsStore.Clone(found);
            // Nothing is assumed from a recognised board: its shape is described from scratch, and fast updates are detected when it connects.
            _layout = new LedLayout { Shape = "" };
            _mapper = null;
            _next.IsEnabled = true;
            RenderFound();
        }

        // ------------------------------------------------------------------ 2. name

        private TextBox _nameBox;

        private void ShowName()
        {
            _title.Text = _existing == null ? "Name it" : "Name";
            _subtitle.Text = _existing == null
                ? "Found " + _device.DetectedName + " on " + _device.Connection + ". Give it a name you'll recognise; it's how it appears everywhere in the app."
                : _device.DetectedName + " on " + _device.Connection + ". The name appears everywhere in the app, including the light designer.";
            var body = new StackPanel();
            body.Children.Add(new TextBlock { Text = "Name", Style = (Style)FindResource("T.Label"), Margin = new Thickness(0, 0, 0, 8) });
            _nameBox = new TextBox { Text = _device.Name, Width = 360, HorizontalAlignment = HorizontalAlignment.Left, MaxLength = 40 };
            _nameBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) GoNext(); };
            body.Children.Add(_nameBox);
            _body.Content = body;
            Dispatcher.BeginInvoke(new Action(() => { _nameBox.Focus(); _nameBox.SelectAll(); }), DispatcherPriority.Input);
        }

        /// <summary>The name, or with a number added when another device already has it, so every device can be told apart.</summary>
        private string UniqueName(string name)
        {
            string candidate = name;
            for (int n = 2; _settings.General.Devices.Any(d => d.Id != _device.Id && string.Equals(d.Name, candidate, StringComparison.OrdinalIgnoreCase)); n++)
                candidate = name + " " + n;
            return candidate;
        }

        // ------------------------------------------------------------------ 3. shape

        private StackPanel _shapeFields;
        private ContentControl _shapeDrawing;
        private TextBlock _shapeTotal;

        private int Limit => _device.Kind == LedDeviceKinds.Wled ? 1500 : _device.Kind == LedDeviceKinds.Adalight ? 300 : ArduinoLedController.MaxLeds;

        private bool ShapeReady() => !string.IsNullOrEmpty(_layout.Shape) && _layout.Total >= 1 && _layout.Total <= Limit;

        private void ShowShape()
        {
            _title.Text = "What does it look like?";
            _subtitle.Text = "Pick the kind of device and count the LEDs in each part. Next you'll show the app where each LED is, so count exactly: a board sent colours for LEDs it doesn't have can freeze until it's unplugged.";
            var body = new StackPanel();
            var choices = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 18) };
            choices.Children.Add(ShapeChoice(LedShapes.Screen, "Screen with LEDs", "LEDs down the sides of a wheel screen, across the top, or both."));
            choices.Children.Add(ShapeChoice(LedShapes.Bars, "Light bars", "One bar or several side by side, like brow lights."));
            choices.Children.Add(ShapeChoice(LedShapes.Strip, "LED strip", "A single straight strip of any length."));
            body.Children.Add(choices);
            _shapeFields = new StackPanel();
            body.Children.Add(_shapeFields);
            _shapeTotal = new TextBlock { Style = (Style)FindResource("T.Label"), Margin = new Thickness(0, 18, 0, 8) };
            body.Children.Add(_shapeTotal);
            _shapeDrawing = new ContentControl { HorizontalAlignment = HorizontalAlignment.Left };
            body.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(5, 7, 10)), BorderBrush = (Brush)FindResource("Brush.Border"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12), Padding = new Thickness(20), Child = _shapeDrawing, MinHeight = 80
            });
            _body.Content = body;
            RenderShapeFields();
        }

        private Border ShapeChoice(string shape, string title, string text)
        {
            var card = Card();
            card.Margin = new Thickness(0, 0, 10, 0);
            card.Cursor = Cursors.Hand;
            card.BorderThickness = new Thickness(1.5);
            card.BorderBrush = (Brush)FindResource(_layout.Shape == shape ? "Brush.Accent" : "Brush.Border");
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = title, Style = (Style)FindResource("T.Label") });
            stack.Children.Add(new TextBlock { Text = text, Style = (Style)FindResource("T.Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
            card.Child = stack;
            card.MouseLeftButtonUp += (s, e) =>
            {
                if (_layout.Shape == shape) return;
                _layout = new LedLayout { Shape = shape };
                _mapper = null;
                ShowShape();
            };
            return card;
        }

        private void RenderShapeFields()
        {
            _shapeFields.Children.Clear();
            var row = new WrapPanel();
            switch (_layout.Shape)
            {
                case LedShapes.Screen:
                    row.Children.Add(CountField("Left side", _layout.Left, v => _layout.Left = v));
                    row.Children.Add(CountField("Across the top", _layout.Top, v => _layout.Top = v));
                    row.Children.Add(CountField("Right side", _layout.Right, v => _layout.Right = v));
                    row.Children.Add(CountField("Along the bottom", _layout.Bottom, v => _layout.Bottom = v));
                    break;
                case LedShapes.Bars:
                    // Only the joined switch follows the count, so the box being typed in keeps its focus.
                    row.Children.Add(CountField("How many bars", _layout.Bars, v => { _layout.Bars = v; RenderJoined(); }, 1, 8));
                    row.Children.Add(CountField("LEDs on each bar", _layout.PerBar, v => _layout.PerBar = v));
                    break;
                case LedShapes.Strip:
                    row.Children.Add(CountField("LEDs on the strip", _layout.Length, v => _layout.Length = v));
                    break;
                default:
                    _shapeFields.Children.Add(new TextBlock { Text = "Pick one of the three above.", Style = (Style)FindResource("T.Secondary") });
                    UpdateShape();
                    return;
            }
            _shapeFields.Children.Add(row);
            _shapeFields.Children.Add(_joined = new StackPanel());
            RenderJoined();
            _shapeFields.Children.Add(_roles = new StackPanel { Margin = new Thickness(0, 14, 0, 0) });
            RenderRoles();
            UpdateShape();
        }

        private StackPanel _roles, _joined;

        /// <summary>For two or more bars: whether they're joined into one long bar.</summary>
        private void RenderJoined()
        {
            if (_joined == null) return;
            _joined.Children.Clear();
            if (_layout.Shape != LedShapes.Bars || _layout.Bars < 2) return;
            var joined = new CheckBox { Style = (Style)FindResource("Switch"), Content = "Joined end to end into one long bar", IsChecked = _layout.Connected, Margin = new Thickness(0, 14, 0, 2) };
            joined.Click += (s, e) => { _layout.Connected = joined.IsChecked == true; _layout.Map = null; _mapper = null; UpdateShape(); RenderRoles(); };
            _joined.Children.Add(joined);
            _joined.Children.Add(new TextBlock
            {
                Text = "Joined bars work as one bar: a fill can run from both outer ends to the middle, and \"split down the middle\" gives a left and a right half. Separate bars each work on their own.",
                Style = (Style)FindResource("T.Caption"), TextWrapping = TextWrapping.Wrap, MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left
            });
        }

        /// <summary>What each part of the shape is for, which decides which light layers use it.</summary>
        private void RenderRoles()
        {
            if (_roles == null) return;
            _roles.Children.Clear();
            List<LayoutPiece> pieces = _layout.Total > 0 ? _layout.Pieces() : new List<LayoutPiece>();
            if (pieces.Count == 0) return;
            _roles.Children.Add(new TextBlock { Text = "WHAT EACH PART IS FOR", Style = (Style)FindResource("T.Overline"), Margin = new Thickness(0, 0, 0, 8) });
            foreach (LayoutPiece piece in pieces)
            {
                LayoutPiece p = piece;
                var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                grid.Children.Add(new TextBlock { Text = p.Name + " · " + p.Slots.Length, VerticalAlignment = VerticalAlignment.Center });
                var role = new ComboBox { ItemsSource = LedPurposes.All, SelectedItem = _layout.RoleOf(p), MinWidth = 0 };
                var help = new TextBlock { Text = LedPurposes.Describe(_layout.RoleOf(p)), Style = (Style)FindResource("T.Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), TextWrapping = TextWrapping.Wrap };
                role.SelectionChanged += (s, e) =>
                {
                    string chosen = (string)role.SelectedItem;
                    if (chosen == p.DefaultRole) _layout.Roles.Remove(p.Id); else _layout.Roles[p.Id] = chosen;
                    help.Text = LedPurposes.Describe(chosen);
                };
                Grid.SetColumn(role, 1);
                Grid.SetColumn(help, 2);
                grid.Children.Add(role);
                grid.Children.Add(help);
                _roles.Children.Add(grid);
            }
        }

        private FrameworkElement CountField(string label, int value, Action<int> set, int min = 0, int max = 250)
        {
            var stack = new StackPanel { Margin = new Thickness(0, 0, 18, 0) };
            stack.Children.Add(new TextBlock { Text = label, Style = (Style)FindResource("T.Caption"), Margin = new Thickness(0, 0, 0, 6) });
            var box = new TextBox { Text = value > 0 ? value.ToString() : "", Width = 96, HorizontalAlignment = HorizontalAlignment.Left };
            box.TextChanged += (s, e) =>
            {
                int n = int.TryParse(box.Text.Trim(), out int parsed) ? Math.Max(min, Math.Min(max, parsed)) : min;
                set(n);
                _layout.Map = null;
                _mapper = null;
                UpdateShape();
                RenderRoles();
            };
            stack.Children.Add(box);
            return stack;
        }

        private void UpdateShape()
        {
            int total = _layout.Total;
            _shapeTotal.Text = total == 0 ? "" : total + (total == 1 ? " LED" : " LEDs") + " in all";
            _status.Text = total > Limit ? "That's more LEDs than this kind of board can drive (" + Limit + ")." : "";
            List<LayoutSlot> slots = total > 0 && total <= Limit ? _layout.Slots() : new List<LayoutSlot>();
            var dots = slots.Select(s => (FrameworkElement)new Ellipse
            {
                Width = 14, Height = 14, Fill = (Brush)FindResource("Brush.LedOff"), Stroke = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), StrokeThickness = 1
            }).ToList();
            _shapeDrawing.Content = slots.Count == 0
                ? (object)new TextBlock { Text = "The shape appears here as you fill it in.", Style = (Style)FindResource("T.Caption") }
                : LedDrawing.Arrange(slots.Select(s => (Point?)new Point(s.X, s.Y)).ToList(), dots, 14);
            _next.IsEnabled = ShapeReady();
        }

        // ------------------------------------------------------------------ 4. map

        private LayoutMapper _mapper;
        private DispatcherTimer _chase;
        private int _chaseLed = -1;
        private TextBlock _mapPrompt, _mapHint;
        private ContentControl _mapDrawing;
        private Button _undo, _replay;
        // Checking the map: LEDs clicked on the drawing light on the device; after a few, the user confirms.
        private const int ChecksNeeded = 4;
        private readonly HashSet<int> _checked = new HashSet<int>();
        private int _testLed = -1;
        private bool _verified, _doubted;
        // The LED the user said didn't light up, or -1: the shape has more LEDs than the device.
        private int _notLit = -1;
        private StackPanel _verify;
        private DispatcherTimer _health;

        /// <summary>While mapping, says so at once if the device stops taking frames (unplugged mid-setup, say).</summary>
        private void WatchDevice(bool on)
        {
            if (!on) { _health?.Stop(); return; }
            if (_health == null)
            {
                _health = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                _health.Tick += (s, e) =>
                {
                    string error = _preview?.LastError;
                    if (error != null) _status.Text = "Lost contact with " + _device.Name + ": " + error + " Check its cable and power; the LEDs come back when it does.";
                    else if (_status.Text.StartsWith("Lost contact")) _status.Text = "";
                };
            }
            _health.Start();
        }

        private void ResetChecks()
        {
            _checked.Clear();
            _testLed = -1;
            _verified = false;
            _doubted = false;
        }

        private static readonly DrawingColor Asking = DrawingColor.FromArgb(220, 220, 220), Placed = DrawingColor.FromArgb(0, 45, 40);

        /// <summary>Opens the device for showing LEDs; false (with a message and Try again) when it can't be reached.</summary>
        private async Task<bool> EnsurePreview(Action retry)
        {
            if (_preview != null && _preview.Matches(_device)) return true;
            _preview?.Dispose();
            _preview = null;
            if (Offline)
            {
                _preview = new LedPreview(_device, new NowhereOutput());
                return true;
            }
            if (_device.Kind != LedDeviceKinds.Wled && _dashboardRunning())
            {
                var holder = new StackPanel();
                _foundList = holder;
                _body.Content = holder;
                ShowNeedsStop(async () => { if (await EnsurePreview(retry)) retry(); });
                return false;
            }
            try
            {
                DeviceSettings target = _device;
                _preview = await Task.Run(() => new LedPreview(target));
                return true;
            }
            catch (Exception exception)
            {
                var body = new StackPanel();
                body.Children.Add(new TextBlock { Text = "Couldn't reach " + _device.Name + " on " + _device.Connection + ".", Style = (Style)FindResource("T.Label") });
                body.Children.Add(new TextBlock { Text = exception.Message, Style = (Style)FindResource("T.Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 12) });
                var again = new Button { Content = "Try again", HorizontalAlignment = HorizontalAlignment.Left };
                again.Click += (s, e) => retry();
                body.Children.Add(again);
                _body.Content = body;
                return false;
            }
        }

        private async void ShowMap()
        {
            _title.Text = "Where is each LED?";
            _subtitle.Text = "One LED at a time lights on your device. Click where it is on the drawing. After two neighbours the app follows the chain on its own, so most devices take two clicks.";
            _next.IsEnabled = false;
            _body.Content = new TextBlock { Text = "Connecting to " + _device.Name + "…", Style = (Style)FindResource("T.Secondary") };
            if (!await EnsurePreview(ShowMap)) return;
            if (_mapper == null || _mapper.Total != _layout.Total)
            {
                _mapper = new LayoutMapper(_layout);
                ResetChecks();
                // A shape mapped (and checked) before is shown done, ready to check again or map again.
                if (_layout.IsMapped) { _mapper.Load(_layout.Map); _verified = true; }
            }

            var body = new StackPanel();
            _mapPrompt = new TextBlock { FontSize = 22, FontWeight = FontWeights.SemiBold, FontFamily = (FontFamily)FindResource("Font.Display"), TextWrapping = TextWrapping.Wrap };
            _mapHint = new TextBlock { Style = (Style)FindResource("T.Secondary"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 16), MaxWidth = 680, HorizontalAlignment = HorizontalAlignment.Left };
            body.Children.Add(_mapPrompt);
            body.Children.Add(_mapHint);
            // The check question and its answers sit right under the prompt, where they're seen.
            _verify = new StackPanel { Margin = new Thickness(0, -4, 0, 16) };
            body.Children.Add(_verify);
            _mapDrawing = new ContentControl { HorizontalAlignment = HorizontalAlignment.Center };
            body.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(5, 7, 10)), BorderBrush = (Brush)FindResource("Brush.Border"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12), Padding = new Thickness(24), Child = _mapDrawing
            });
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
            _undo = new Button { Content = "Undo", Margin = new Thickness(0, 0, 8, 0) };
            _undo.Click += (s, e) => { StopChase(); _mapper.Undo(); ResetChecks(); RenderMap(); };
            var restart = new Button { Content = "Start over", Margin = new Thickness(0, 0, 8, 0) };
            restart.Click += (s, e) => { StopChase(); _mapper = new LayoutMapper(_layout, !_mapper.ByHand); ResetChecks(); RenderMap(); };
            var replay = _replay = new Button { Content = "Play the order again" };
            replay.Click += (s, e) => StartChase();
            actions.Children.Add(_undo);
            actions.Children.Add(restart);
            actions.Children.Add(replay);
            body.Children.Add(actions);
            _body.Content = body;
            RenderMap();
            if (_mapper.Done) StartChase();
        }

        private void RenderMap()
        {
            if (_mapDrawing == null) return;
            int asking = _mapper.Next;
            List<LayoutSlot> slots = _layout.Slots();
            double size = Math.Max(22, Math.Min(34, 640 / (slots.Select(s => s.X).DefaultIfEmpty(0).Max() + 1) / 1.6));
            var dots = new List<FrameworkElement>();
            foreach (LayoutSlot slot in slots)
            {
                int led = _mapper.LedAt(slot.Index);
                bool chasing = led >= 0 && (led == _chaseLed || (_chaseLed < 0 && led == _testLed));
                var circle = new Ellipse
                {
                    Width = size, Height = size, StrokeThickness = 1.5,
                    Fill = chasing ? Brushes.White : led >= 0 ? (Brush)FindResource("Brush.AccentSubtle") : (Brush)FindResource("Brush.LedOff"),
                    Stroke = led >= 0 ? (Brush)FindResource("Brush.Accent") : new SolidColorBrush(Color.FromArgb(90, 255, 255, 255))
                };
                var label = new TextBlock
                {
                    Text = led >= 0 ? (led + 1).ToString() : "", FontSize = size * 0.38, FontWeight = FontWeights.SemiBold,
                    Foreground = chasing ? Brushes.Black : (Brush)FindResource("Brush.Text"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                };
                var cell = new Grid { Width = size, Height = size, Background = Brushes.Transparent, ToolTip = led >= 0 ? "LED " + (led + 1) : "Click if LED " + (asking + 1) + " is here" };
                cell.Children.Add(circle);
                cell.Children.Add(label);
                if (led < 0 && asking >= 0)
                {
                    int index = slot.Index;
                    cell.Cursor = Cursors.Hand;
                    cell.MouseEnter += (s, e) => circle.Stroke = (Brush)FindResource("Brush.Accent");
                    cell.MouseLeave += (s, e) => circle.Stroke = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255));
                    cell.MouseLeftButtonUp += (s, e) =>
                    {
                        _mapper.Place(index);
                        RenderMap();
                        // Once every LED is placed, run them all in order once before the check.
                        if (_mapper.Done) StartChase();
                    };
                }
                else if (led >= 0 && _mapper.Done)
                {
                    // Checking: the clicked LED lights on the device, so the user can see it's the same one.
                    cell.Cursor = Cursors.Hand;
                    cell.ToolTip = "Light LED " + (led + 1) + " on the device";
                    cell.MouseLeftButtonUp += (s, e) => TestLed(led);
                }
                dots.Add(cell);
            }
            _mapDrawing.Content = LedDrawing.Arrange(slots.Select(s => (Point?)new Point(s.X, s.Y)).ToList(), dots, size);
            _undo.IsEnabled = _mapper.CanUndo;
            _replay.Visibility = _mapper.Done ? Visibility.Visible : Visibility.Collapsed;

            _verify.Children.Clear();
            _verify.Visibility = Visibility.Collapsed;
            if (_mapper.Done)
            {
                if (_verified)
                {
                    _mapPrompt.Text = "Every LED is placed and checked";
                    _mapHint.Text = "Click any LED to light it on your device again, or continue.";
                }
                else if (_checked.Count < ChecksNeeded)
                {
                    _mapPrompt.Text = "Now check it: click " + ChecksNeeded + " LEDs";
                    _mapHint.Text = "Click any LED on the drawing and the same LED should light on your device. Pick ones in different places, such as each end and each side. " +
                        _checked.Count + " of " + ChecksNeeded + " checked.";
                }
                else ShowVerifyQuestion();
                _next.IsEnabled = _verified;
            }
            else
            {
                if (_notLit == asking) { ShowNotLit(asking); return; }
                _notLit = -1;
                _mapPrompt.Text = "LED #" + (asking + 1) + " is lit on your device. Click where it is.";
                _mapHint.Text = _mapper.ByHand ? "Placing every LED by hand: " + asking + " of " + _mapper.Total + " placed. Undo takes back the last one."
                    : _mapper.Pending
                    ? "The one after LED #" + asking + ". That shows the app which way the chain runs."
                    : asking == 0 ? "It's the bright white LED. Placed LEDs glow faintly so you can keep your bearings."
                    : "The chain jumped or reached the end of a part, so the app needs LED #" + (asking + 1) + " too.";
                _next.IsEnabled = false;
                var missing = new Button { Content = "LED #" + (asking + 1) + " didn't light up", Style = (Style)FindResource("Btn.Ghost"), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(0, 2, 0, 2) };
                missing.Click += (s, e) => { _notLit = asking; RenderMap(); };
                _verify.Children.Add(missing);
                _verify.Visibility = Visibility.Visible;
                ShowAsking(asking);
            }
        }

        /// <summary>An LED that didn't light: the device has fewer LEDs than the shape, or (for the first) isn't showing anything.</summary>
        private void ShowNotLit(int led)
        {
            _preview?.Show(new DrawingColor[Math.Max(1, led)]);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            if (led == 0)
            {
                _mapPrompt.Text = "Nothing lit up?";
                _mapHint.Text = "The app couldn't light " + _device.Name + ". Check it's plugged in and powered, then try again.";
            }
            else
            {
                _mapPrompt.Text = "Your device may have " + led + (led == 1 ? " LED" : " LEDs");
                _mapHint.Text = "The shape adds up to " + _layout.Total + ", but LED #" + (led + 1) + " didn't light, so it's past the end of the device. Go back and correct the counts; sending colours to LEDs a board doesn't have can make it freeze.";
                var back = new Button { Content = "Back to the shape", Style = (Style)FindResource("Btn.Primary"), Margin = new Thickness(0, 0, 8, 0) };
                back.Click += (s, e) => { _notLit = -1; Show(Step.Shape); };
                buttons.Children.Add(back);
            }
            var again = new Button { Content = led == 0 ? "Try again" : "It did light, carry on" };
            again.Click += (s, e) => { _notLit = -1; RenderMap(); };
            buttons.Children.Add(again);
            _verify.Children.Add(buttons);
            _verify.Visibility = Visibility.Visible;
            _next.IsEnabled = false;
        }

        private void TestLed(int led)
        {
            StopChase();
            _testLed = led;
            _checked.Add(led);
            var frame = new DrawingColor[_mapper.Total];
            frame[led] = Asking;
            _preview?.Show(frame);
            RenderMap();
        }

        /// <summary>After a few checks: did the right LED light each time? No offers to place every LED by hand.</summary>
        private void ShowVerifyQuestion()
        {
            _mapPrompt.Text = _doubted ? "Let's fix it" : "Did the right LED light up each time?";
            _mapHint.Text = _doubted
                ? "Some LEDs don't follow the chain the app worked out, such as one side wired the other way round. Place every LED by hand instead: the app lights each in turn and you click where it is."
                : "If each LED you clicked lit up in the same place on your device, the map is right.";
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            if (!_doubted)
            {
                var yes = new Button { Content = "Yes, they all matched", Style = (Style)FindResource("Btn.Primary"), Margin = new Thickness(0, 0, 8, 0) };
                yes.Click += (s, e) => { _verified = true; _preview?.Show(new DrawingColor[_mapper.Total]); _testLed = -1; RenderMap(); };
                var no = new Button { Content = "No, something was off" };
                no.Click += (s, e) => { _doubted = true; RenderMap(); };
                buttons.Children.Add(yes);
                buttons.Children.Add(no);
            }
            else
            {
                var byHand = new Button { Content = "Place every LED by hand", Style = (Style)FindResource("Btn.Primary"), Margin = new Thickness(0, 0, 8, 0) };
                byHand.Click += (s, e) => { StopChase(); _mapper = new LayoutMapper(_layout, false); ResetChecks(); RenderMap(); };
                var again = new Button { Content = "Try the two clicks again" };
                again.Click += (s, e) => { StopChase(); _mapper = new LayoutMapper(_layout); ResetChecks(); RenderMap(); };
                buttons.Children.Add(byHand);
                buttons.Children.Add(again);
            }
            _verify.Children.Add(buttons);
            _verify.Visibility = Visibility.Visible;
        }

        /// <summary>Lights the LED being asked for brightly and the placed ones faintly; nothing past the furthest LED involved.</summary>
        private void ShowAsking(int asking)
        {
            var frame = new DrawingColor[asking + 1];
            for (int led = 0; led < asking; led++) if (_mapper.SlotOf(led) >= 0) frame[led] = Placed;
            frame[asking] = Asking;
            _preview?.Show(frame);
        }

        /// <summary>Runs every LED in order on the device and the drawing at once, to check the mapping.</summary>
        private void StartChase()
        {
            StopChase();
            if (_mapper == null || !_mapper.Done) return;
            int n = _mapper.Total;
            _chaseLed = 0;
            _chase = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(80, Math.Min(350, 6000 / Math.Max(1, n)))) };
            _chase.Tick += (s, e) =>
            {
                if (_chaseLed >= n) { StopChase(); RenderMap(); _preview?.Show(new DrawingColor[n]); return; }
                var frame = new DrawingColor[n];
                frame[_chaseLed] = Asking;
                _preview?.Show(frame);
                RenderMap();
                _chaseLed++;
            };
            _chase.Start();
        }

        private void StopChase()
        {
            _chase?.Stop();
            _chase = null;
            _chaseLed = -1;
        }

        // ------------------------------------------------------------------ 5. done

        private void ShowDone()
        {
            StopChase();
            _title.Text = _device.Name + " is ready";
            _subtitle.Text = _layout.Describe() + ". Light layers aimed at a part's role use it, filling the way the part runs: sides bottom to top, rows and bars left to right." +
                (_device.Kind == LedDeviceKinds.Arduino ? " Fast updates are used automatically when the board supports them." : "");
            var body = new StackPanel();
            foreach (LayoutPiece piece in _layout.Pieces())
                body.Children.Add(new TextBlock
                {
                    Text = piece.Name + " · " + piece.Slots.Length + (piece.Slots.Length == 1 ? " LED" : " LEDs") + " · " + _layout.RoleOf(piece),
                    Style = (Style)FindResource("T.Secondary"), Margin = new Thickness(0, 0, 0, 6)
                });
            // Power is asked once here for a new device; a device's settings window changes it later.
            if (_device.Kind != LedDeviceKinds.Wled && !_ledsOnly)
            {
                body.Children.Add(new TextBlock { Text = "POWER", Style = (Style)FindResource("T.Overline"), Margin = new Thickness(0, 22, 0, 8) });
                body.Children.Add(DeviceUi.PowerPicker(this, _device));
            }
            if (_ledsOnly) _title.Text = _device.Name + "'s LEDs are mapped";
            _body.Content = body;
            _preview?.Show(new DrawingColor[Math.Max(1, _layout.Total)]);
        }

        private Border Card() => new Border
        {
            Background = (Brush)FindResource("Brush.SurfaceRaised"), CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 12, 16, 12),
            Margin = new Thickness(0, 0, 0, 8), BorderBrush = (Brush)FindResource("Brush.Border"), BorderThickness = new Thickness(1)
        };
    }

    /// <summary>
    /// Shows frames on a device while it's being set up. Frames are sent from a background
    /// thread, always the newest one, so a slider dragged quickly never queues up stale frames.
    /// </summary>
    internal sealed class LedPreview : IDisposable
    {
        private readonly ILedOutput _output;
        private readonly Thread _thread;
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private readonly string _signature;
        private DrawingColor[] _pending;
        private volatile bool _stop;

        private int _sent;

        /// <summary>The most LEDs a frame may hold. Frames are sent as long as they are, never padded,
        /// so a board is only ever sent LEDs the setup has asked for.</summary>
        public int Limit { get; }

        public LedPreview(DeviceSettings device, ILedOutput output = null)
        {
            var open = SettingsStore.Clone(device);
            Limit = device.Kind == LedDeviceKinds.Wled ? Math.Max(device.LedCount, 1) : device.Kind == LedDeviceKinds.Adalight ? 300 : ArduinoLedController.MaxLeds;
            open.LedCount = Limit;
            _output = output ?? LedDeviceLink.Open(open);
            _signature = device.Kind + "|" + device.Connection;
            _thread = new Thread(Run) { IsBackground = true, Name = "LED setup preview" };
            _thread.Start();
        }

        public bool Matches(DeviceSettings device) => _signature == device.Kind + "|" + device.Connection;

        /// <summary>Why the last frame couldn't be sent (the device was unplugged, say), or null while it's fine.</summary>
        public string LastError { get; private set; }

        public void Show(DrawingColor[] frame)
        {
            Volatile.Write(ref _pending, frame);
            _wake.Set();
        }

        private void Run()
        {
            DrawingColor[] last = null;
            while (!_stop)
            {
                _wake.WaitOne(_output.KeepAliveMilliseconds > 0 ? _output.KeepAliveMilliseconds : 1000);
                DrawingColor[] frame = Volatile.Read(ref _pending);
                if (frame == null || (frame == last && _output.KeepAliveMilliseconds == 0)) continue;
                try
                {
                    var sized = new DrawingColor[Math.Min(frame.Length, Limit)];
                    Array.Copy(frame, sized, sized.Length);
                    if (sized.Length == 0) continue;
                    _output.Send(sized);
                    _sent = sized.Length;
                    last = frame;
                    LastError = null;
                }
                catch (Exception exception) { LastError = exception.Message; }
            }
        }

        public void Dispose()
        {
            _stop = true;
            _wake.Set();
            _thread.Join(1500);
            // Off, for exactly the LEDs that were lit.
            if (_sent > 0) try { _output.Send(new DrawingColor[_sent]); } catch { }
            _output.Dispose();
            _wake.Dispose();
        }
    }

    /// <summary>LEDs that go nowhere: setup screenshots without touching hardware.</summary>
    internal sealed class NowhereOutput : ILedOutput
    {
        public int KeepAliveMilliseconds => 0;
        public string Detail => "nowhere";
        public void Send(DrawingColor[] frame) { }
        public void Dispose() { }
    }
}
