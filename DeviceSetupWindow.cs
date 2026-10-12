using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DrawingColor = System.Drawing.Color;

namespace AlmostFastOverlays
{
    /// <summary>
    /// Sets up an LED device step by step: find it, name it, count its LEDs, then split
    /// them into groups. The device lights up along the way so every answer can be
    /// checked against the real thing. Opens at the naming step for a device that is
    /// already set up.
    /// </summary>
    internal sealed class DeviceSetupWindow : Window
    {
        private enum Step { Find, Name, Count, Map, Done }

        private readonly AppSettings _settings;
        private readonly DeviceSettings _existing;
        private readonly Func<bool> _dashboardRunning;
        private readonly Func<Task<bool>> _stopDashboard;
        private readonly TextBlock _stepLabel = new TextBlock(), _title = new TextBlock(), _subtitle = new TextBlock(), _status = new TextBlock();
        private readonly ContentControl _body = new ContentControl();
        private readonly Button _back = new Button { Content = "Back", MinWidth = 96 };
        private readonly Button _next = new Button { Content = "Continue", MinWidth = 120 };
        private Step _step;

        // The device being set up. LedCount and Groups are only final at the end.
        private DeviceSettings _device;
        private KnownDevices.Profile _profile;
        private int _expectedCount;
        private List<LedGroup> _groups;
        private LedPreview _preview;

        /// <summary>The device, set when setup finishes.</summary>
        public DeviceSettings Result { get; private set; }

        /// <summary>True when setup paused the running dashboard (so the caller can start it again).</summary>
        public bool StoppedDashboard { get; private set; }

        /// <summary>For --ui-snapshot: sample devices, and LEDs that go nowhere, so no hardware is touched.</summary>
        internal bool Offline { get; set; }

        /// <summary>For --ui-snapshot: opens a step directly with the first set-up device (or a sample).</summary>
        internal void JumpTo(string step)
        {
            Offline = true;
            DeviceSettings sample = _settings.General.Devices.FirstOrDefault() ?? KnownDevices.Create(KnownDevices.Dv5, "COM4", 16, true, false);
            if (step == "find") { _device = null; Show(Step.Find); return; }
            _device = SettingsStore.Clone(sample);
            _profile = KnownDevices.Find(sample.UsbId, sample.DetectedName);
            _expectedCount = step == "count-guided" ? 0 : sample.LedCount;
            _groups = sample.Groups.Select(SettingsStore.Clone).ToList();
            Show(step == "name" ? Step.Name : step.StartsWith("count") ? Step.Count : step == "map" ? Step.Map : Step.Done);
        }

        public DeviceSetupWindow(AppSettings settings, DeviceSettings existing, Func<bool> dashboardRunning, Func<Task<bool>> stopDashboard)
        {
            _settings = settings;
            _existing = existing;
            _dashboardRunning = dashboardRunning;
            _stopDashboard = stopDashboard;
            Title = existing == null ? "Add a device" : "Set up " + existing.Name;
            Width = 820;
            Height = 680;
            MinWidth = 700;
            MinHeight = 560;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.CanResize;
            FontFamily = (FontFamily)FindResource("Font.Ui");
            FontSize = 13;
            Foreground = (Brush)FindResource("Brush.Text");
            Background = (Brush)FindResource("Brush.Bg");
            GlassBackdrop.Apply(this, 0xD8);
            Content = new Border { Padding = new Thickness(32, 26, 32, 22), Child = Layout() };
            Closed += (s, e) => _preview?.Dispose();

            if (existing == null) Show(Step.Find);
            else
            {
                _device = SettingsStore.Clone(existing);
                _profile = KnownDevices.Find(existing.UsbId, existing.DetectedName);
                _expectedCount = existing.LedCount;
                _groups = existing.Groups.Select(SettingsStore.Clone).ToList();
                Show(Step.Name);
            }
        }

        // ------------------------------------------------------------------ frame

        private Grid Layout()
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
            _step = step;
            _status.Text = "";
            int number = (int)step + (_existing == null ? 1 : 0), total = _existing == null ? 4 : 3;
            _stepLabel.Text = step == Step.Done ? "ALL SET" : "STEP " + number + " OF " + total;
            _back.Visibility = step == Step.Find || (step == Step.Name && _existing != null) || step == Step.Done ? Visibility.Collapsed : Visibility.Visible;
            _next.Content = step == Step.Done ? "Finish" : "Continue";
            _next.IsEnabled = true;
            switch (step)
            {
                case Step.Find: ShowFind(); break;
                case Step.Name: ShowName(); break;
                case Step.Count: ShowCount(); break;
                case Step.Map: ShowMap(); break;
                default: ShowDone(); break;
            }
        }

        private void GoBack()
        {
            if (_step == Step.Name) Show(Step.Find);
            else if (_step == Step.Count) Show(Step.Name);
            else if (_step == Step.Map) Show(Step.Count);
        }

        private void GoNext()
        {
            switch (_step)
            {
                case Step.Find: if (_device != null) Show(Step.Name); break;
                case Step.Name:
                    _device.Name = string.IsNullOrWhiteSpace(_nameBox?.Text) ? (_device.DetectedName ?? "LED device") : _nameBox.Text.Trim();
                    Show(Step.Count);
                    break;
                case Step.Count:
                    if (_countValue < 1) return;
                    _device.LedCount = _countValue;
                    Show(Step.Map);
                    break;
                case Step.Map:
                    foreach (LedGroup group in _groups) group.Normalize(_device.LedCount);
                    _device.Groups = _groups.ToList();
                    Show(Step.Done);
                    break;
                default:
                    Finish();
                    break;
            }
        }

        private void Finish()
        {
            _device.Groups = _groups ?? _device.Groups;
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
            var scan = new Button { Content = "Look again" };
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
                if (text.Length == 0) return;
                _status.Text = "Asking " + text + "…";
                Tuple<string, int> info = await Task.Run(() => WledOutput.Info(text));
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
                _found.Add(KnownDevices.Create(KnownDevices.Dv5, "COM4", 16, true, false));
                _found[0].DetectedName = "SSG DV 5";
                _found.Add(new DeviceSettings { Kind = LedDeviceKinds.Arduino, Port = "COM9", DetectedName = "Arduino LED board", Name = "Arduino LED board" });
            }
            RenderFound();
            if (_found.Count == 0) Dispatcher.BeginInvoke(new Func<Task>(Scan), DispatcherPriority.Background);
        }

        private bool _scanning;

        private async Task Scan()
        {
            if (_scanning || Offline) return;
            if (_dashboardRunning())
            {
                ShowNeedsStop(async () => await Scan());
                return;
            }
            _scanning = true;
            _status.Text = "Looking for LED devices…";
            var skip = _settings.General.Devices.Select(d => d.ResolvePort()).Where(p => p != null).ToList();
            List<DeviceSettings> found;
            try
            {
                found = await Task.Run(() => LedDeviceScanner.Scan(skip, port => Dispatcher.BeginInvoke(new Action(() => _status.Text = "Looking for LED devices… " + port))));
            }
            catch (Exception exception) { found = new List<DeviceSettings>(); _status.Text = "The search stopped: " + exception.Message; }
            _scanning = false;
            foreach (DeviceSettings device in found.Where(f => _found.All(x => x.Port != f.Port))) _found.Add(device);
            if (_step != Step.Find) return;
            _status.Text = _found.Count == 0 ? "Nothing answered. Check the cable, then look again. Boards need the common sim-racing LED firmware or Adalight." : "";
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
                KnownDevices.Profile known = KnownDevices.Find(f.UsbId, f.DetectedName);
                var card = Card();
                card.Cursor = Cursors.Hand;
                card.BorderThickness = new Thickness(1.5);
                card.BorderBrush = (Brush)FindResource(f == _device ? "Brush.Accent" : "Brush.Border");
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var text = new StackPanel();
                text.Children.Add(new TextBlock { Text = f.DetectedName, Style = (Style)FindResource("T.Label") });
                text.Children.Add(new TextBlock { Text = f.Kind + " · " + f.Connection, Style = (Style)FindResource("T.Caption"), Margin = new Thickness(0, 3, 0, 0) });
                grid.Children.Add(text);
                if (known != null)
                {
                    var tag = new Border { Style = (Style)FindResource("Pill"), Background = (Brush)FindResource("Brush.AccentSubtle"), VerticalAlignment = VerticalAlignment.Center,
                        Child = new TextBlock { Text = "Known: " + known.Name, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("Brush.Accent") } };
                    Grid.SetColumn(tag, 1);
                    grid.Children.Add(tag);
                }
                card.Child = grid;
                card.MouseLeftButtonUp += (s, e) => Select(f);
                _foundList.Children.Add(card);
            }
        }

        private void Select(DeviceSettings found)
        {
            _device = SettingsStore.Clone(found);
            _profile = KnownDevices.Find(found.UsbId, found.DetectedName);
            _expectedCount = _profile?.LedCount ?? (found.Kind == LedDeviceKinds.Wled ? found.LedCount : 0);
            _groups = null;
            if (_profile != null) _device.Name = _profile.Name;
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
            if (_profile != null)
                body.Children.Add(new TextBlock
                {
                    Text = "This is a " + _profile.Name + ", so its LED count and groups are filled in for you in the next steps. You can still change them.",
                    Style = (Style)FindResource("T.Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0)
                });
            _body.Content = body;
            Dispatcher.BeginInvoke(new Action(() => { _nameBox.Focus(); _nameBox.SelectAll(); }), DispatcherPriority.Input);
        }

        // ------------------------------------------------------------------ 3. count

        private int _countValue;

        private async void ShowCount()
        {
            _title.Text = "How many LEDs?";
            _countValue = Math.Max(1, _device.LedCount);
            _next.IsEnabled = false;
            _body.Content = new TextBlock { Text = "Connecting to " + _device.Name + "…", Style = (Style)FindResource("T.Secondary") };
            if (!await EnsurePreview()) return;
            if (_expectedCount > 0) ShowConfirmCount(); else ShowGuidedCount(Math.Max(1, _device.LedCount));
        }

        private void ShowConfirmCount()
        {
            _subtitle.Text = "Most LED boards can't report their LED count, but this one we know.";
            _countValue = _expectedCount;
            var body = new StackPanel();
            body.Children.Add(new TextBlock { Text = _expectedCount + " LEDs", FontSize = 40, FontWeight = FontWeights.SemiBold, FontFamily = (FontFamily)FindResource("Font.Display") });
            body.Children.Add(new TextBlock
            {
                Text = "They're all lit now. Count them on the device, then continue if that's right.",
                Style = (Style)FindResource("T.Secondary"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 18)
            });
            var no = new Button { Content = "No, let me count", HorizontalAlignment = HorizontalAlignment.Left };
            no.Click += (s, e) => ShowGuidedCount(_expectedCount);
            body.Children.Add(no);
            _body.Content = body;
            _next.IsEnabled = true;
            _preview.Show(Enumerable.Range(0, _preview.Size).Select(i => i < _expectedCount ? DrawingColor.FromArgb(120, 120, 120) : DrawingColor.Black).ToArray());
        }

        private void ShowGuidedCount(int start)
        {
            _subtitle.Text = "Most LED boards can't report how many LEDs they have, so count them together: only one LED is lit. Move it until it's the very last LED on your device.";
            int max = _preview.Size;
            var body = new StackPanel();
            var number = new TextBlock { FontSize = 40, FontWeight = FontWeights.SemiBold, FontFamily = (FontFamily)FindResource("Font.Display") };
            var slider = new Slider { Minimum = 1, Maximum = max, IsSnapToTickEnabled = true, TickFrequency = 1, Width = 460, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) };
            void Update(int n)
            {
                _countValue = Math.Max(1, Math.Min(max, n));
                number.Text = _countValue + (_countValue == 1 ? " LED" : " LEDs");
                if ((int)slider.Value != _countValue) slider.Value = _countValue;
                // The lit LED is the count; the ones before it glow faintly so the strip is easy to follow.
                _preview.Show(Enumerable.Range(0, max).Select(i => i == _countValue - 1 ? DrawingColor.White : i < _countValue - 1 ? DrawingColor.FromArgb(0, 0, 40) : DrawingColor.Black).ToArray());
            }
            var stepper = new StackPanel { Orientation = Orientation.Horizontal };
            var less = new Button { Content = "−", Width = 44, FontSize = 18 };
            var more = new Button { Content = "+", Width = 44, FontSize = 18, Margin = new Thickness(8, 0, 20, 0) };
            less.Click += (s, e) => Update(_countValue - 1);
            more.Click += (s, e) => Update(_countValue + 1);
            stepper.Children.Add(less);
            stepper.Children.Add(more);
            stepper.Children.Add(number);
            body.Children.Add(stepper);
            slider.ValueChanged += (s, e) => { if ((int)slider.Value != _countValue) Update((int)slider.Value); };
            body.Children.Add(slider);
            body.Children.Add(new TextBlock
            {
                Text = "The bright white LED is the one you're counting to; the faint blue ones come before it. When the white one is the last LED on the device, that's the count.",
                Style = (Style)FindResource("T.Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0), MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left
            });
            _body.Content = body;
            _next.IsEnabled = true;
            Update(start);
        }

        /// <summary>Opens the device for showing LEDs; false (with a message and Retry) when it can't be reached.</summary>
        private async Task<bool> EnsurePreview()
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
                ShowNeedsStop(async () => { if (await EnsurePreview()) ShowCount(); });
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
                var retry = new Button { Content = "Try again", HorizontalAlignment = HorizontalAlignment.Left };
                retry.Click += (s, e) => ShowCount();
                body.Children.Add(retry);
                _body.Content = body;
                return false;
            }
        }

        // ------------------------------------------------------------------ 4. map

        private static readonly DrawingColor[] Palette =
        {
            DrawingColor.FromArgb(0, 200, 255), DrawingColor.FromArgb(0, 220, 90), DrawingColor.FromArgb(255, 170, 0),
            DrawingColor.FromArgb(220, 60, 255), DrawingColor.FromArgb(255, 60, 80), DrawingColor.FromArgb(255, 255, 255)
        };

        private DispatcherTimer _chase;
        private LedGroup _focused;
        private TextBlock _chaseNumber;

        private async void ShowMap()
        {
            _title.Text = "Which LEDs are which?";
            _subtitle.Text = "Split the LEDs into groups and say what each is for. Light layers aimed at Shift lights, Left side and so on use the groups with that purpose. Watch the order first if you're not sure where the LEDs run.";
            if (!await EnsurePreview()) return;
            if (_groups == null || _groups.Count == 0)
                _groups = _profile != null ? _profile.Groups(_device.LedCount) : new List<LedGroup> { LedGroup.Of("All LEDs", LedPurposes.Bar, 1, _device.LedCount) };
            foreach (LedGroup group in _groups) group.Normalize(_device.LedCount);
            RenderMap();
        }

        private void RenderMap()
        {
            var body = new StackPanel();

            var order = Card();
            var orderRow = new Grid();
            orderRow.ColumnDefinitions.Add(new ColumnDefinition());
            orderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var orderText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            orderText.Children.Add(new TextBlock { Text = "Watch the order", Style = (Style)FindResource("T.Label") });
            orderText.Children.Add(new TextBlock { Text = "Each LED lights in turn, with its number here.", Style = (Style)FindResource("T.Caption"), Margin = new Thickness(0, 3, 0, 0) });
            orderRow.Children.Add(orderText);
            var chaseControls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _chaseNumber = new TextBlock { FontSize = 28, FontWeight = FontWeights.SemiBold, FontFamily = (FontFamily)FindResource("Font.Display"), MinWidth = 90, TextAlignment = TextAlignment.Right, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
            var play = new Button { Content = "Show the order" };
            play.Click += (s, e) => { if (_chase != null) StopChase(); else StartChase(); play.Content = _chase != null ? "Stop" : "Show the order"; };
            chaseControls.Children.Add(_chaseNumber);
            chaseControls.Children.Add(play);
            Grid.SetColumn(chaseControls, 1);
            orderRow.Children.Add(chaseControls);
            order.Child = orderRow;
            body.Children.Add(order);

            body.Children.Add(new TextBlock { Text = "GROUPS", Style = (Style)FindResource("T.Overline"), Margin = new Thickness(0, 18, 0, 8) });
            var header = GroupGrid();
            string[] heads = { "Name", "Purpose", "From LED", "To LED", "" };
            for (int i = 0; i < heads.Length; i++)
            {
                var h = new TextBlock { Text = heads[i], Style = (Style)FindResource("T.Caption") };
                Grid.SetColumn(h, i);
                header.Children.Add(h);
            }
            body.Children.Add(header);
            for (int g = 0; g < _groups.Count; g++) body.Children.Add(GroupRow(_groups[g], Palette[g % Palette.Length]));

            var add = new Button { Content = "Add group", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
            add.Click += (s, e) =>
            {
                int next = _groups.Count == 0 ? 1 : Math.Min(_device.LedCount, _groups.Max(x => Math.Max(x.From, x.To)) + 1);
                _groups.Add(new LedGroup { Name = "Group " + (_groups.Count + 1), Purpose = LedPurposes.Other, From = next, To = _device.LedCount });
                RenderMap();
            };
            body.Children.Add(add);
            body.Children.Add(new TextBlock
            {
                Text = "Reverse runs a group from its last LED to its first, for fills that should grow the other way (a right side that fills bottom to top, say). While you edit a group it lights on the device, brightest at its start.",
                Style = (Style)FindResource("T.Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0), MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left
            });
            _body.Content = body;
            ShowGroupsOnDevice();
        }

        private static Grid GroupGrid()
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            foreach (double width in new[] { 200.0, 150, 90, 90, 1 })
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width == 1 ? new GridLength(1, GridUnitType.Star) : new GridLength(width) });
            return grid;
        }

        private Grid GroupRow(LedGroup group, DrawingColor colour)
        {
            var grid = GroupGrid();
            var name = new TextBox { Text = group.Name, Margin = new Thickness(0, 0, 10, 0), MaxLength = 30 };
            name.TextChanged += (s, e) => group.Name = name.Text;
            var purpose = new ComboBox { ItemsSource = LedPurposes.All, SelectedItem = group.Purpose, MinWidth = 0, Margin = new Thickness(0, 0, 10, 0), ToolTip = LedPurposes.Describe(group.Purpose) };
            purpose.SelectionChanged += (s, e) => { group.Purpose = (string)purpose.SelectedItem; purpose.ToolTip = LedPurposes.Describe(group.Purpose); };
            TextBox Number(int value, Action<int> set)
            {
                var box = new TextBox { Text = value.ToString(), Margin = new Thickness(0, 0, 10, 0) };
                box.TextChanged += (s, e) =>
                {
                    if (int.TryParse(box.Text.Trim(), out int n) && n >= 1 && n <= _device.LedCount) { set(n); ShowGroupsOnDevice(); }
                };
                return box;
            }
            var from = Number(group.From, n => group.From = n);
            var to = Number(group.To, n => group.To = n);
            var tail = new StackPanel { Orientation = Orientation.Horizontal };
            var reverse = new CheckBox { Style = (Style)FindResource("Switch"), Content = "Reverse", IsChecked = group.Reverse, VerticalAlignment = VerticalAlignment.Center };
            reverse.Click += (s, e) => { group.Reverse = reverse.IsChecked == true; ShowGroupsOnDevice(); };
            var swatch = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Margin = new Thickness(14, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(Color.FromRgb(colour.R, colour.G, colour.B)), ToolTip = "This group's colour on the device" };
            var remove = new Button { Style = (Style)FindResource("Btn.Icon"), Content = "", ToolTip = "Remove this group" };
            remove.Click += (s, e) => { _groups.Remove(group); _focused = null; RenderMap(); };
            tail.Children.Add(reverse);
            tail.Children.Add(swatch);
            tail.Children.Add(remove);
            UIElement[] cells = { name, purpose, from, to, tail };
            for (int i = 0; i < cells.Length; i++)
            {
                Grid.SetColumn(cells[i], i);
                grid.Children.Add(cells[i]);
            }
            // While a group is being edited, only it lights, brightest at its start.
            grid.IsKeyboardFocusWithinChanged += (s, e) => { _focused = grid.IsKeyboardFocusWithin ? group : (_focused == group ? null : _focused); ShowGroupsOnDevice(); };
            grid.MouseEnter += (s, e) => { if (_focused == null) ShowGroupsOnDevice(group); };
            grid.MouseLeave += (s, e) => ShowGroupsOnDevice();
            return grid;
        }

        /// <summary>Every group in its colour, or just one group with its direction shown when one is picked.</summary>
        private void ShowGroupsOnDevice(LedGroup only = null)
        {
            if (_preview == null || _chase != null) return;
            only = only ?? _focused;
            var frame = new DrawingColor[_preview.Size];
            for (int g = 0; g < _groups.Count; g++)
            {
                LedGroup group = _groups[g];
                if (only != null && group != only) continue;
                var probe = SettingsStore.Clone(group);
                probe.Normalize(_device.LedCount);
                int[] leds = probe.Indexes();
                DrawingColor colour = only != null ? DrawingColor.FromArgb(0, 230, 120) : Palette[g % Palette.Length];
                for (int k = 0; k < leds.Length; k++)
                {
                    // A single group fades along its fill order, so you can see which way it runs.
                    double level = only != null && leds.Length > 1 ? 1 - 0.85 * k / (leds.Length - 1) : 0.6;
                    if (leds[k] < frame.Length) frame[leds[k]] = DrawingColor.FromArgb((int)(colour.R * level), (int)(colour.G * level), (int)(colour.B * level));
                }
            }
            _preview.Show(frame);
        }

        private void StartChase()
        {
            int n = _device.LedCount, index = 0;
            _chase = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(70, Math.Min(400, 7000 / n))) };
            _chase.Tick += (s, e) =>
            {
                if (index >= n) { StopChase(); RenderMap(); return; }
                var frame = new DrawingColor[_preview.Size];
                frame[index] = DrawingColor.White;
                _preview.Show(frame);
                _chaseNumber.Text = "LED " + (index + 1);
                index++;
            };
            _chase.Start();
        }

        private void StopChase()
        {
            _chase?.Stop();
            _chase = null;
            if (_chaseNumber != null) _chaseNumber.Text = "";
            ShowGroupsOnDevice();
        }

        // ------------------------------------------------------------------ 5. done

        private void ShowDone()
        {
            StopChase();
            _title.Text = _device.Name + " is ready";
            var purposes = _groups.Select(g => g.Purpose).Where(p => p != LedPurposes.Other).Distinct().ToList();
            _subtitle.Text = _device.LedCount + " LEDs in " + _groups.Count + (_groups.Count == 1 ? " group" : " groups") + "." +
                (purposes.Count > 0 ? " Light layers aimed at " + string.Join(", ", purposes) + " will use it." : " Aim light layers at its groups in the light designer.");
            var body = new StackPanel();
            foreach (LedGroup group in _groups)
                body.Children.Add(new TextBlock
                {
                    Text = group.Name + (string.Equals(group.Name, group.Purpose, StringComparison.OrdinalIgnoreCase) ? "" : " · " + group.Purpose) +
                        " · LEDs " + group.From + "–" + group.To + (group.Reverse ? ", reversed" : ""),
                    Style = (Style)FindResource("T.Secondary"), Margin = new Thickness(0, 0, 0, 6)
                });
            if (_device.Kind == LedDeviceKinds.Arduino)
            {
                var fast = new CheckBox
                {
                    Style = (Style)FindResource("Switch"), Content = "Fast updates", IsChecked = _device.FastUpdates, Margin = new Thickness(0, 18, 0, 4),
                    ToolTip = "Sends each light frame in one message. Smoother if the board supports it"
                };
                fast.Click += (s, e) => _device.FastUpdates = fast.IsChecked == true;
                body.Children.Add(fast);
                body.Children.Add(new TextBlock { Text = "Sends each light frame in one message, for smoother lights. If the lights look wrong with it on, turn it off.", Style = (Style)FindResource("T.Caption"), TextWrapping = TextWrapping.Wrap });
            }
            _body.Content = body;
            _preview?.Show(new DrawingColor[_preview.Size]);
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

        /// <summary>How many LEDs a frame has: room to count past what the device has.</summary>
        public int Size { get; }

        public LedPreview(DeviceSettings device, ILedOutput output = null)
        {
            var open = SettingsStore.Clone(device);
            Size = device.Kind == LedDeviceKinds.Wled ? Math.Max(device.LedCount, 1) : device.Kind == LedDeviceKinds.Adalight ? 300 : ArduinoLedController.MaxLeds;
            open.LedCount = Size;
            _output = output ?? LedDeviceLink.Open(open);
            _signature = device.Kind + "|" + device.Connection;
            _thread = new Thread(Run) { IsBackground = true, Name = "LED setup preview" };
            _thread.Start();
        }

        public bool Matches(DeviceSettings device) => _signature == device.Kind + "|" + device.Connection;

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
                    var sized = new DrawingColor[Size];
                    Array.Copy(frame, sized, Math.Min(frame.Length, Size));
                    _output.Send(sized);
                    last = frame;
                }
                catch { }
            }
        }

        public void Dispose()
        {
            _stop = true;
            _wake.Set();
            _thread.Join(1500);
            try { _output.Send(new DrawingColor[Size]); } catch { }
            _output.Dispose();
            _wake.Dispose();
        }
    }

    /// <summary>LEDs that go nowhere: setup screenshots without touching hardware.</summary>
    internal sealed class NowhereOutput : ILedOutput
    {
        public int KeepAliveMilliseconds => 0;
        public void Send(DrawingColor[] frame) { }
        public void Dispose() { }
    }
}
