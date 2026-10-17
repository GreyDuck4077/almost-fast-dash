using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using DrawingColor = System.Drawing.Color;

namespace AlmostFastOverlays
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

        private static TelemetrySnapshot Snap(double speed = 136, int gear = 4, double shift = 0.62, double rpm = 6120, RacingFlag flag = RacingFlag.None,
            BlindSpotZone left = BlindSpotZone.None, BlindSpotZone right = BlindSpotZone.None) =>
            IRacingTelemetrySource.WithPreviewExtras(new TelemetrySnapshot(
                true, gear, speed, flag, left, right, shift, SessionProgressMode.LapRace, 0, 0, 8, 18, 4, 2, 24.3, 58.104, 0.46,
                "J. MILLER", -0.46, "M. CARTER", 0.82, 22, false, 87.932, 87.521, 88.204, new bool[18], 0, 6, rpm));

        /// <summary>A fixed telemetry state for previewing lights; also used by the settings window.</summary>
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
                    StatusText.Text = live.Connected ? "Showing live iRacing data." : "Waiting for iRacing… start a session to see live lights.";
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

        // ==================================================================
        // LED stage
        // ==================================================================

        /// <summary>One panel per set-up device, its LEDs in order.</summary>
        private void BuildStage()
        {
            DeviceStages.Children.Clear();
            _deviceLeds.Clear();
            _deviceGlows.Clear();
            if (!Devices.Any(d => d.Enabled))
                DeviceStages.Children.Add(new TextBlock
                {
                    Style = (Style)FindResource("T.Caption"), TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center,
                    Text = "No LED devices are set up yet. Add them on the Devices page; until then the layers have nothing to light."
                });
            for (int d = 0; d < Devices.Count; d++)
            {
                DeviceSettings device = Devices[d];
                var leds = new Ellipse[device.LedCount];
                var glows = new DropShadowEffect[device.LedCount];
                _deviceLeds.Add(leds);
                _deviceGlows.Add(glows);
                if (!device.Enabled) continue;
                // Drawn the way the LEDs sit on the device, sized to fit the stage.
                double size = LedDrawing.FitSize(device, 600, 20);
                for (int i = 0; i < device.LedCount; i++)
                {
                    glows[i] = new DropShadowEffect { ShadowDepth = 0, BlurRadius = 16, Opacity = 0 };
                    leds[i] = MakeLed(size, glows[i], new LedRef(d, i));
                }
                FrameworkElement row = LedDrawing.Arrange(device, leds, size);
                var panel = new StackPanel();
                panel.Children.Add(new TextBlock { Text = device.Name.ToUpperInvariant(), Style = (Style)FindResource("T.Overline"), Margin = new Thickness(2, 0, 0, 8) });
                panel.Children.Add(row);
                DeviceStages.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(5, 7, 10)), BorderBrush = new SolidColorBrush(Color.FromRgb(29, 35, 45)), BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 0, 12), Child = panel
                });
            }
        }

        private Ellipse MakeLed(double size, DropShadowEffect glow, LedRef led)
        {
            var dot = new Ellipse
            {
                Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Fill = (Brush)FindResource("Brush.LedOff"), Stroke = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), StrokeThickness = 1,
                Effect = glow, Tag = led, Cursor = Cursors.Help
            };
            dot.MouseEnter += (s, e) => ExplainLed(led);
            dot.MouseLeave += (s, e) => HoverText.Text = "Hover an LED to see which layer lit it. The selected layer's LEDs are ringed.";
            return dot;
        }

        private void ExplainLed(LedRef led)
        {
            DeviceSettings device = led.Device < Devices.Count ? Devices[led.Device] : null;
            string groups = device == null ? "" : string.Join(", ", device.Groups.Where(g => g.Indexes().Contains(led.Index)).Select(g => g.Name));
            string where = (device?.Name ?? "Device") + " LED " + (led.Index + 1) + (groups.Length > 0 ? " · " + groups : "");
            bool known = _lastFrame != null && led.Device < _lastFrame.Leds.Length && led.Index < _lastFrame.Leds[led.Device].Length;
            string owner = known ? _lastFrame.Owners[led.Device][led.Index] : null;
            DrawingColor c = known ? _lastFrame.Leds[led.Device][led.Index] : DrawingColor.Black;
            bool lit = (c.R | c.G | c.B) != 0;
            HoverText.Text = where + (owner == null ? (lit ? " · fading out" : " · off: no layer is lighting it right now")
                : lit ? " · lit by \"" + owner + "\"" : " · held dark by \"" + owner + "\" (Replace)");
        }

        private void RenderFrame()
        {
            if (_renderer == null || _renderer.Profile != _profile || _renderer.Signature != LightRenderer.DeviceSignature(Devices))
                _renderer = new LightRenderer(_profile, _dashboard.AccentColor, _units, Devices);
            _lastSnapshot = CurrentSnapshot();
            _lastFrame = _renderer.Compose(_lastSnapshot, Environment.TickCount);
            for (int d = 0; d < _deviceLeds.Count && d < _lastFrame.Leds.Length; d++)
                for (int i = 0; i < _deviceLeds[d].Length && i < _lastFrame.Leds[d].Length; i++)
                    if (_deviceLeds[d][i] != null) Paint(_deviceLeds[d][i], _deviceGlows[d][i], _lastFrame.Leds[d][i]);
            HighlightTarget();
            foreach (FormulaBox box in _formulas) box.ShowResult();
        }

        private void Paint(Ellipse dot, DropShadowEffect glow, DrawingColor color)
        {
            ShowLed(color, out Color hue, out double level);
            dot.Fill = level <= 0 ? (Brush)FindResource("Brush.LedOff") : new SolidColorBrush(hue);
            glow.Color = hue;
            glow.Opacity = level;
        }

        /// <summary>
        /// How an LED output value looks on screen. LEDs give out light in proportion
        /// to the value sent, while a monitor expects gamma-encoded color, so the value
        /// is encoded for display; the glow follows its brightness.
        /// </summary>
        internal static void ShowLed(DrawingColor color, out Color hue, out double glow)
        {
            byte Encode(int v) => (byte)Math.Round(255 * Math.Pow(Math.Max(0, Math.Min(255, v)) / 255.0, 1 / 2.2));
            hue = Color.FromRgb(Encode(color.R), Encode(color.G), Encode(color.B));
            int peak = Math.Max(color.R, Math.Max(color.G, color.B));
            glow = peak == 0 ? 0 : 0.35 + 0.6 * Math.Max(hue.R, Math.Max(hue.G, hue.B)) / 255.0;
        }

        private void HighlightTarget()
        {
            var ringed = new HashSet<LedRef>();
            if (_selected != null)
            {
                LedTarget target = LedTargets.Find(_selected.Target, Devices);
                if (target != null) foreach (LedRef[] strip in target.Strips) foreach (LedRef led in strip) ringed.Add(led);
            }
            var accent = (SolidColorBrush)FindResource("Brush.Accent");
            var normal = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
            foreach (Ellipse dot in _deviceLeds.SelectMany(leds => leds).Where(dot => dot != null))
            {
                bool on = ringed.Contains((LedRef)dot.Tag);
                dot.Stroke = on ? accent : normal;
                dot.StrokeThickness = on ? 2 : 1;
            }
        }

        // ==================================================================
        // Output settings
        // ==================================================================

        private void HookOutputControls()
        {
            MasterSlider.ValueChanged += (s, e) =>
            {
                MasterValue.Text = (int)MasterSlider.Value + "%";
                if (_loading) return;
                _profile.Brightness = (int)MasterSlider.Value;
                CommitSoon("Master brightness");
            };
            FadeSlider.ValueChanged += (s, e) =>
            {
                FadeValue.Text = (int)FadeSlider.Value + " ms";
                if (_loading) return;
                _profile.FadeMs = (int)FadeSlider.Value;
                CommitSoon("Fade");
            };
            GammaToggle.Click += (s, e) => { _profile.Gamma = GammaToggle.IsChecked == true; Commit("Gamma"); };
        }

        private void LoadOutputControls()
        {
            _loading = true;
            MasterSlider.Value = _profile.Brightness;
            FadeSlider.Value = _profile.FadeMs;
            GammaToggle.IsChecked = _profile.Gamma;
            MasterValue.Text = _profile.Brightness + "%";
            FadeValue.Text = _profile.FadeMs + " ms";
            _loading = false;
            BuildThemePicker();
        }

        // ==================================================================
        // Layer stack
        // ==================================================================

        private void RefreshLayers()
        {
            LayerList.Children.Clear();
            if (_profile.Layers.Count == 0)
            {
                LayerList.Children.Add(new TextBlock { Text = "No layers yet. Click Add, or pick a preset.", Style = (Style)FindResource("T.Caption"), Margin = new Thickness(8, 12, 8, 0) });
                return;
            }
            for (int i = 0; i < _profile.Layers.Count; i++) LayerList.Children.Add(LayerCard(_profile.Layers[i], i));
        }

        private Border LayerCard(LightLayer layer, int index)
        {
            bool selected = layer == _selected;
            LightEffect effect = LightEffects.Find(layer.Effect);
            LedTarget target = LedTargets.Find(layer.Target, Devices);

            var grid = new Grid { Margin = new Thickness(10, 9, 10, 9) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // An eye, as in the screen designer's layers: lit when the layer is on.
            var enabled = new Button
            {
                Style = (Style)FindResource("Btn.Ghost"), Width = 28, Height = 28, Padding = new Thickness(0), Margin = new Thickness(-4, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
                ToolTip = layer.Enabled ? "On: click to turn this layer off" : "Off: click to turn this layer on",
                Content = new TextBlock
                {
                    Text = layer.Enabled ? "" : "", Style = (Style)FindResource("T.Icon"), FontSize = 13,
                    Foreground = (Brush)FindResource(layer.Enabled ? "Brush.Accent" : "Brush.TextTertiary")
                }
            };
            enabled.Click += (s, e) => { layer.Enabled = !layer.Enabled; Commit(layer.Enabled ? "Enable layer" : "Disable layer"); RefreshLayers(); e.Handled = true; };
            grid.Children.Add(enabled);

            var text = new StackPanel { Opacity = layer.Enabled ? 1 : 0.45 };
            var title = new StackPanel { Orientation = Orientation.Horizontal };
            title.Children.Add(new TextBlock { Text = (index + 1) + "", Foreground = (Brush)FindResource("Brush.TextTertiary"), FontSize = 12, Width = 18, VerticalAlignment = VerticalAlignment.Center });
            title.Children.Add(new TextBlock { Text = layer.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(title);
            text.Children.Add(new TextBlock
            {
                Text = (effect?.Name ?? layer.Effect) + " · " + (target?.Name ?? layer.Target) + (layer.Blend == "Replace" ? " · replaces" : ""),
                Style = (Style)FindResource("T.Caption"), FontSize = 12, Margin = new Thickness(18, 1, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap
            });
            if (!string.IsNullOrWhiteSpace(layer.Condition))
                text.Children.Add(new TextBlock
                {
                    Text = "ƒ when " + layer.Condition, FontFamily = (FontFamily)FindResource("Font.Mono"), FontSize = 12, Foreground = (Brush)FindResource("Brush.Accent"),
                    Margin = new Thickness(18, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap
                });
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            var swatch = new Border { Width = 10, Height = 26, CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Center, Background = SwatchFor(layer), Margin = new Thickness(8, 0, 0, 0) };
            Grid.SetColumn(swatch, 2);
            grid.Children.Add(swatch);

            var card = new Border
            {
                Child = grid, CornerRadius = new CornerRadius(9), Margin = new Thickness(0, 0, 0, 6), Cursor = Cursors.Hand, AllowDrop = true,
                Background = (Brush)FindResource(selected ? "Brush.AccentSubtle" : "Brush.Surface"),
                BorderBrush = (Brush)FindResource(selected ? "Brush.AccentBorder" : "Brush.Border"), BorderThickness = new Thickness(1)
            };
            Point? down = null;
            card.MouseLeftButtonDown += (s, e) => down = e.GetPosition(card);
            card.MouseMove += (s, e) =>
            {
                if (down == null || e.LeftButton != MouseButtonState.Pressed) return;
                if (Math.Abs(e.GetPosition(card).Y - down.Value.Y) < 6) return;
                down = null;
                DragDrop.DoDragDrop(card, new DataObject("afo-light-layer", layer.Id), DragDropEffects.Move);
            };
            card.MouseLeftButtonUp += (s, e) =>
            {
                if (down == null) return;
                down = null;
                Select(layer);
            };
            card.DragOver += (s, e) => { e.Effects = e.Data.GetDataPresent("afo-light-layer") ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; };
            card.Drop += (s, e) =>
            {
                if (!(e.Data.GetData("afo-light-layer") is string id) || id == layer.Id) return;
                LightLayer moving = _profile.Layers.FirstOrDefault(l => l.Id == id);
                if (moving == null) return;
                bool above = e.GetPosition(card).Y < card.ActualHeight / 2;
                _profile.Layers.Remove(moving);
                int at = _profile.Layers.IndexOf(layer);
                _profile.Layers.Insert(above ? at : at + 1, moving);
                Commit("Reorder");
                RefreshLayers();
            };
            var menu = new ContextMenu();
            menu.Items.Add(MenuItem("Duplicate", () => { Select(layer); Duplicate(); }));
            menu.Items.Add(MenuItem("Move to top", () => { _profile.Layers.Remove(layer); _profile.Layers.Insert(0, layer); Commit("Reorder"); RefreshLayers(); }));
            menu.Items.Add(MenuItem("Move to bottom", () => { _profile.Layers.Remove(layer); _profile.Layers.Add(layer); Commit("Reorder"); RefreshLayers(); }));
            menu.Items.Add(MenuItem("Delete", () => { Select(layer); DeleteSelected(); }));
            card.ContextMenu = menu;
            return card;
        }

        /// <summary>A small color strip showing what a layer lights up with.</summary>
        private Brush SwatchFor(LightLayer layer)
        {
            var scope = CurrentScope();
            Color C(string key, string fallback)
            {
                LightEffect effect = LightEffects.Find(layer.Effect);
                string raw = layer.Get(key, effect?.Prop(key)?.Default ?? fallback);
                DrawingColor? c = Expr.Color(LayoutRenderer.Strip(raw), scope) ?? Expr.Color(fallback, scope);
                return c.HasValue ? Color.FromRgb(c.Value.R, c.Value.G, c.Value.B) : Colors.Gray;
            }
            switch (layer.Effect)
            {
                case "revs":
                case "meter":
                case "gradient":
                    return new LinearGradientBrush(new GradientStopCollection { new GradientStop(C("high", "bad"), 0), new GradientStop(C("mid", "warn"), 0.5), new GradientStop(C("low", "good"), 1) }, 90);
                case "center":
                    return new LinearGradientBrush(C("positive", "good"), C("negative", "bad"), 90);
                case "flag":
                    return new LinearGradientBrush(new GradientStopCollection { new GradientStop(Colors.Yellow, 0), new GradientStop(Colors.White, 0.5), new GradientStop(Color.FromRgb(0, 90, 255), 1) }, 90);
                case "blank":
                    return new SolidColorBrush(Color.FromRgb(20, 20, 24));
                default:
                    return new SolidColorBrush(C("color", "accent"));
            }
        }

        private static MenuItem MenuItem(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (s, e) => action();
            return item;
        }

        private void Select(LightLayer layer)
        {
            FlushCommit();
            _selected = layer;
            RefreshLayers();
            RefreshInspector();
        }

        private void AddLayer_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = PlacementMode.Bottom };
            foreach (LightEffect effect in LightEffects.All)
            {
                LightEffect fx = effect;
                var header = new StackPanel { Margin = new Thickness(0, 1, 0, 1), MaxWidth = 320 };
                header.Children.Add(new TextBlock { Text = fx.Name, FontWeight = FontWeights.SemiBold });
                header.Children.Add(new TextBlock { Text = fx.Description, Style = (Style)FindResource("T.Caption"), TextWrapping = TextWrapping.Wrap });
                MenuItem item = MenuItem("", () => AddLayer(fx));
                item.Header = header;
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        }

        private void AddLayer(LightEffect effect)
        {
            string target;
            switch (effect.Id)
            {
                case "spotter": target = "sides"; break;
                case "flag": target = "brow"; break;
                case "center": target = "brow-mirror"; break;
                case "meter": case "gradient": case "breathe": target = "brow"; break;
                default: target = "shift"; break;
            }
            var layer = new LightLayer { Name = effect.Name, Effect = effect.Id, Target = target };
            int at = _selected == null ? 0 : Math.Max(0, _profile.Layers.IndexOf(_selected));
            _profile.Layers.Insert(at, layer);
            _selected = layer;
            Commit("Add " + effect.Name);
            RefreshLayers();
            RefreshInspector();
            StatusText.Text = "Added \"" + effect.Name + "\" above the selected layer. Drag it to change its priority.";
        }

        private void Duplicate()
        {
            if (_selected == null) return;
            LightLayer copy = SettingsStore.Clone(_selected);
            copy.Id = DashScreen.NewId("layer");
            copy.Name = _selected.Name + " copy";
            _profile.Layers.Insert(_profile.Layers.IndexOf(_selected), copy);
            _selected = copy;
            Commit("Duplicate");
            RefreshLayers();
            RefreshInspector();
        }

        private void DeleteSelected()
        {
            if (_selected == null) return;
            int index = _profile.Layers.IndexOf(_selected);
            _profile.Layers.Remove(_selected);
            _selected = _profile.Layers.Count == 0 ? null : _profile.Layers[Math.Min(index, _profile.Layers.Count - 1)];
            Commit("Delete layer");
            RefreshLayers();
            RefreshInspector();
        }

        private void Move(int step)
        {
            if (_selected == null) return;
            int index = _profile.Layers.IndexOf(_selected), target = Math.Max(0, Math.Min(_profile.Layers.Count - 1, index + step));
            if (index == target) return;
            _profile.Layers.RemoveAt(index);
            _profile.Layers.Insert(target, _selected);
            Commit("Reorder");
            RefreshLayers();
        }

        private void Up_Click(object sender, RoutedEventArgs e) => Move(-1);
        private void Down_Click(object sender, RoutedEventArgs e) => Move(1);
        private void Duplicate_Click(object sender, RoutedEventArgs e) => Duplicate();
        private void Delete_Click(object sender, RoutedEventArgs e) => DeleteSelected();

        private void Presets_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = PlacementMode.Bottom };
            foreach (LightPreset preset in LightPresets.All)
            {
                LightPreset p = preset;
                var header = new StackPanel { Margin = new Thickness(0, 1, 0, 1), MaxWidth = 340 };
                header.Children.Add(new TextBlock { Text = p.Name, FontWeight = FontWeights.SemiBold });
                header.Children.Add(new TextBlock { Text = p.Description, Style = (Style)FindResource("T.Caption"), TextWrapping = TextWrapping.Wrap });
                var item = new MenuItem { Header = header };
                item.Click += (s, a) =>
                {
                    if (_profile.Layers.Count > 0 && MessageBox.Show(this, "Replace these layers with the \"" + p.Name + "\" preset? You can undo this.",
                            "Light presets", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
                    LightProfile previous = _profile;
                    _profile = p.Create(_dashboard);
                    // A preset changes the layers, not the colors chosen for them.
                    _profile.ThemeId = previous.ThemeId;
                    _profile.CustomLow = previous.CustomLow;
                    _profile.CustomMid = previous.CustomMid;
                    _profile.CustomHigh = previous.CustomHigh;
                    _profile.Normalize();
                    _selected = _profile.Layers.FirstOrDefault();
                    _renderer = null;
                    Commit("Preset " + p.Name);
                    LoadOutputControls();
                    RefreshLayers();
                    RefreshInspector();
                };
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        }

        // ==================================================================
        // Inspector
        // ==================================================================

        // Targets kept for layers that already use them, but not offered: the device and group
        // entries and the split light bar cover them more plainly.
        private static readonly HashSet<string> RetiredTargets = new HashSet<string> { "dv5", "brow-left", "brow-right" };

        /// <summary>
        /// The LEDs a layer can light, in two plain sections: by role across every device (only roles
        /// some device has), then each device on its own, whole or one group.
        /// </summary>
        private List<object> TargetItems(List<LedTarget> targets, string current)
        {
            var items = new List<object>();
            ComboBoxItem Header(string text) => new ComboBoxItem
            {
                Content = text.ToUpperInvariant(), IsEnabled = false, Focusable = false, FontSize = 12, Margin = new Thickness(0, items.Count == 0 ? 0 : 6, 0, 0),
                Foreground = (Brush)FindResource("Brush.TextTertiary")
            };
            ComboBoxItem Item(string text, string id) => new ComboBoxItem { Content = text, Tag = id, Padding = new Thickness(18, 6, 10, 6) };

            items.Add(Header("By role, on every device"));
            foreach (LedTarget target in targets.Where(t => !t.Id.StartsWith("device:") && !t.Id.StartsWith("group:")))
                if (target.Id == current || (target.Strips.Count > 0 && !RetiredTargets.Contains(target.Id)))
                    items.Add(Item(target.Name, target.Id));
            foreach (DeviceSettings device in Devices.Where(d => d.Enabled))
            {
                items.Add(Header(device.Name));
                items.Add(Item("All " + device.LedCount + " LEDs", "device:" + device.Id));
                foreach (LedGroup group in device.Groups)
                    items.Add(Item(group.Name + "  ·  " + (group.From == group.To ? "LED " + group.From : "LEDs " + group.From + "–" + group.To), "group:" + device.Id + "/" + group.Id));
            }
            if (!items.OfType<ComboBoxItem>().Any(i => (string)i.Tag == current))
                items.Add(Item("A device or group that isn't set up", current));
            return items;
        }

        private TextBlock Section(string text) => new TextBlock { Text = text.ToUpperInvariant(), Style = (Style)FindResource("T.Overline"), Margin = new Thickness(0, 18, 0, 8) };
        private TextBlock Caption(string text, double top = 4) => new TextBlock { Text = text, Style = (Style)FindResource("T.Caption"), Margin = new Thickness(0, top, 0, 0) };

        private Grid Row(string label, FrameworkElement editor, string help = null)
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(122) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("Brush.TextSecondary"), ToolTip = help, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 8, 0) });
            Grid.SetColumn(editor, 1);
            row.Children.Add(editor);
            return row;
        }

        /// <summary>A label over a full-width editor, for pickers whose choices have long names.</summary>
        private StackPanel Stacked(string label, FrameworkElement editor, double top = 3)
        {
            var row = new StackPanel { Margin = new Thickness(0, top, 0, 3) };
            row.Children.Add(new TextBlock { Text = label, Foreground = (Brush)FindResource("Brush.TextSecondary"), Margin = new Thickness(0, 0, 0, 4) });
            row.Children.Add(editor);
            return row;
        }

        private void RefreshInspector()
        {
            Inspector.Children.Clear();
            _formulas.Clear();
            if (_selected == null)
            {
                Inspector.Children.Add(new TextBlock { Text = "Light layers", Style = (Style)FindResource("T.Title") });
                Inspector.Children.Add(Caption("Each layer lights a group of LEDs with an effect, optionally only when a condition is true. Higher layers win where they overlap, so put warnings above everyday lights."));
                Inspector.Children.Add(Section("Good to know"));
                Inspector.Children.Add(Caption("• Rev lights follow the car's own shift-light RPMs from iRacing, so they match the real car.\n• Replace makes a layer take over its whole target, with unlit LEDs going dark: right for flags and pit-lane lights.\n• Hover any LED on the stage to see which layer lit it.\n• Presets are a quick start; everything stays editable."));
                return;
            }

            LightLayer layer = _selected;
            LightEffect effect = LightEffects.Find(layer.Effect) ?? LightEffects.All[0];

            var name = new TextBox { Text = layer.Name, FontWeight = FontWeights.SemiBold, MaxLength = 40 };
            name.TextChanged += (s, e) => { if (name.Text.Trim().Length > 0) { layer.Name = name.Text.Trim(); CommitSoon("Rename"); } };
            name.LostKeyboardFocus += (s, e) => RefreshLayers();
            Inspector.Children.Add(name);

            Inspector.Children.Add(Section("What and where"));
            var effectCombo = new ComboBox { ItemsSource = LightEffects.All.Select(x => x.Name).ToList(), SelectedItem = effect.Name };
            effectCombo.SelectionChanged += (s, e) =>
            {
                LightEffect chosen = LightEffects.All.FirstOrDefault(x => x.Name == (string)effectCombo.SelectedItem);
                if (chosen == null || chosen.Id == layer.Effect) return;
                layer.Effect = chosen.Id;
                Commit("Effect");
                RefreshLayers();
                RefreshInspector();
            };
            Inspector.Children.Add(Stacked("Effect", effectCombo));
            Inspector.Children.Add(Caption(effect.Description, 0));
            List<LedTarget> targets = LedTargets.For(Devices);
            List<object> targetItems = TargetItems(targets, layer.Target);
            var targetCombo = new ComboBox { ItemsSource = targetItems };
            targetCombo.SelectedItem = targetItems.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == layer.Target);
            targetCombo.SelectionChanged += (s, e) =>
            {
                if (!(targetCombo.SelectedItem is ComboBoxItem chosen) || !(chosen.Tag is string id) || id == layer.Target) return;
                layer.Target = id;
                Commit("Target");
                RefreshLayers();
                RefreshInspector();
            };
            Inspector.Children.Add(Stacked("LEDs", targetCombo, 12));
            Inspector.Children.Add(Caption(targets.FirstOrDefault(x => x.Id == layer.Target)?.Description ?? "Aimed at a device or group that isn't set up right now.", 0));

            Inspector.Children.Add(Section("When and how"));
            Inspector.Children.Add(new TextBlock { Text = "Show when", Foreground = (Brush)FindResource("Brush.TextSecondary"), Margin = new Thickness(0, 0, 0, 4) });
            var condition = new FormulaBox(this, LayoutRenderer.Strip(layer.Condition), CurrentScope, "Empty means always, e.g. OnPitRoad && PitLimiter");
            condition.Changed += text => { layer.Condition = text.Trim(); CommitSoon("Condition"); };
            _formulas.Add(condition);
            Inspector.Children.Add(condition);
            Inspector.Children.Add(Caption("Empty means always. Examples: OnPitRoad, FuelLapsLeft < 2, AbsActive, FlagActive.", 4));
            var blend = new ComboBox { ItemsSource = LightLayer.Blends, SelectedItem = layer.Blend };
            blend.SelectionChanged += (s, e) => { layer.Blend = (string)blend.SelectedItem; Commit("Blend"); RefreshLayers(); };
            Inspector.Children.Add(Row("Blend", blend, "Over: only lit LEDs cover the layers below. Replace: the whole target is taken over and unlit LEDs go dark."));
            Inspector.Children.Add(Row("Brightness", NumberEditor(layer.Brightness.ToString(CultureInfo.InvariantCulture), 0, 100, 1, v =>
            {
                if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int b)) { layer.Brightness = Math.Max(0, Math.Min(100, b)); CommitSoon("Brightness"); }
            })));

            string source = layer.Get("source", effect.Prop("source")?.Default);
            foreach (IGrouping<string, PropDef> group in effect.Props.GroupBy(p => p.Group))
            {
                var visible = group.Where(p => Relevant(effect, p, source)).ToList();
                if (visible.Count == 0) continue;
                Inspector.Children.Add(Section(group.Key));
                foreach (PropDef def in visible) Inspector.Children.Add(PropertyRow(layer, def));
            }
        }

        /// <summary>Hides properties that do not apply, such as RPM limits when revs come from the car.</summary>
        private static bool Relevant(LightEffect effect, PropDef def, string source)
        {
            if (effect.Id != "revs") return true;
            if (def.Key == "fromRpm" || def.Key == "toRpm") return source == "RPM range";
            if (def.Key == "value" || def.Key == "flashWhen") return source == "Formula";
            return true;
        }

        private FrameworkElement PropertyRow(LightLayer layer, PropDef def)
        {
            string raw = layer.Get(def.Key, def.Default) ?? "";
            void Apply(string value) { layer.Set(def.Key, value); CommitSoon("Edit " + def.Label.ToLowerInvariant()); }
            switch (def.Kind)
            {
                case PropKind.Formula:
                {
                    var panel = new StackPanel { Margin = new Thickness(0, 3, 0, 3) };
                    panel.Children.Add(new TextBlock { Text = def.Label, Foreground = (Brush)FindResource("Brush.TextSecondary"), Margin = new Thickness(0, 0, 0, 4), ToolTip = def.Help });
                    var box = new FormulaBox(this, LayoutRenderer.Strip(raw), CurrentScope, def.Help);
                    box.Changed += text => Apply(text.Trim().Length == 0 ? "" : "=" + text.Trim());
                    _formulas.Add(box);
                    panel.Children.Add(box);
                    return panel;
                }
                case PropKind.Number:
                    return Row(def.Label, NumberEditor(raw, def.Min, def.Max, def.Step, Apply), def.Help);
                case PropKind.Color:
                {
                    var field = new ColorField(this, raw, _colorContext, () => _dashboard.AccentColor, true, string.IsNullOrEmpty(def.Default));
                    field.Changed += v => { Apply(v); RefreshLayers(); };
                    return Row(def.Label, field, def.Help);
                }
                case PropKind.Bool:
                {
                    var toggle = new CheckBox { Style = (Style)FindResource("Switch"), IsChecked = Expr.Bool(raw), VerticalAlignment = VerticalAlignment.Center };
                    toggle.Click += (s, e) => { layer.Set(def.Key, toggle.IsChecked == true ? "true" : "false"); Commit("Edit " + def.Label.ToLowerInvariant()); };
                    return Row(def.Label, toggle, def.Help);
                }
                case PropKind.Choice:
                {
                    var combo = new ComboBox { ItemsSource = def.Choices, SelectedItem = def.Choices.Contains(raw) ? raw : def.Default };
                    combo.SelectionChanged += (s, e) =>
                    {
                        layer.Set(def.Key, (string)combo.SelectedItem);
                        Commit("Edit " + def.Label.ToLowerInvariant());
                        if (def.Key == "source") RefreshInspector();
                    };
                    return Row(def.Label, combo, def.Help);
                }
                default:
                {
                    var box = new TextBox { Text = raw };
                    box.TextChanged += (s, e) => Apply(box.Text);
                    return Row(def.Label, box, def.Help);
                }
            }
        }

        private FrameworkElement NumberEditor(string raw, double min, double max, double step, Action<string> apply)
        {
            var box = new TextBox { Text = raw, Width = 70, FontFamily = (FontFamily)FindResource("Font.Mono"), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Left };
            step = step > 0 ? step : 1;
            bool ranged = !double.IsNaN(min) && !double.IsNaN(max) && max > min;
            Slider slider = null;
            bool syncing = false;
            box.TextChanged += (s, e) =>
            {
                if (!double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) return;
                if (slider != null && !syncing) { syncing = true; slider.Value = Math.Max(min, Math.Min(max, v)); syncing = false; }
                apply(box.Text.Trim());
            };
            box.PreviewMouseWheel += (s, e) =>
            {
                if (!box.IsKeyboardFocusWithin) return;
                double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v);
                v += e.Delta > 0 ? step : -step;
                if (ranged) v = Math.Max(min, Math.Min(max, v));
                box.Text = v.ToString("0.###", CultureInfo.InvariantCulture);
                e.Handled = true;
            };
            if (!ranged) return box;
            double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double initial);
            slider = new Slider { Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, initial)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            slider.ValueChanged += (s, e) =>
            {
                if (syncing) return;
                syncing = true;
                box.Text = (Math.Round(slider.Value / step) * step).ToString("0.###", CultureInfo.InvariantCulture);
                syncing = false;
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.Children.Add(box);
            Grid.SetColumn(slider, 1);
            grid.Children.Add(slider);
            return grid;
        }

        // ==================================================================
        // Undo, redo, saving
        // ==================================================================

        private string Snapshot() => SettingsStore.ToJson(_profile);

        private void Commit(string label)
        {
            _pendingCommit = null;
            _commitTimer.Stop();
            string json = Snapshot();
            if (json != _committed)
            {
                bool merge = label == _lastCommitLabel && (DateTime.UtcNow - _lastCommitUtc).TotalMilliseconds < 1200 && _undo.Count > 0;
                if (!merge)
                {
                    _undo.Add(_committed);
                    if (_undo.Count > 300) _undo.RemoveAt(0);
                }
                _committed = json;
                _redo.Clear();
                _lastCommitLabel = label;
                _lastCommitUtc = DateTime.UtcNow;
                SaveSoon();
            }
            UpdateUndoButtons();
            UpdateSaveState();
        }

        private void CommitSoon(string label)
        {
            _pendingCommit = label;
            _commitTimer.Stop();
            _commitTimer.Start();
        }

        private void FlushCommit()
        {
            if (_pendingCommit != null) Commit(_pendingCommit);
        }

        private void Undo_Click(object sender, RoutedEventArgs e) => Undo();
        private void Redo_Click(object sender, RoutedEventArgs e) => Redo();

        private void Undo()
        {
            FlushCommit();
            if (_undo.Count == 0) return;
            _redo.Add(_committed);
            _committed = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            Load(_committed);
        }

        private void Redo()
        {
            if (_redo.Count == 0) return;
            _undo.Add(_committed);
            _committed = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            Load(_committed);
        }

        private void Load(string json)
        {
            string selectedId = _selected?.Id;
            _profile = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<LightProfile>(json);
            _profile.Normalize();
            _selected = _profile.Layers.FirstOrDefault(l => l.Id == selectedId) ?? _profile.Layers.FirstOrDefault();
            _renderer = null;
            _lastCommitLabel = null;
            LoadOutputControls();
            RefreshLayers();
            RefreshInspector();
            UpdateUndoButtons();
            UpdateSaveState();
            SaveSoon();
        }

        private void SaveSoon()
        {
            _liveSaveTimer.Stop();
            _liveSaveTimer.Start();
        }

        private void UpdateUndoButtons()
        {
            UndoButton.IsEnabled = _undo.Count > 0 || _pendingCommit != null;
            RedoButton.IsEnabled = _redo.Count > 0;
        }

        private bool Dirty => _committed != _savedJson;

        private void UpdateSaveState()
        {
            SaveState.Text = Dirty ? "Saving…" : "Saved";
            SaveStateIcon.Visibility = Dirty ? Visibility.Collapsed : Visibility.Visible;
        }

        private void SaveProfile(bool quiet)
        {
            FlushCommit();
            try
            {
                _dashboard.Lights = SettingsStore.Clone(_profile);
                SettingsStore.Save(_settings);
                _savedJson = _committed;
                _onSaved?.Invoke();
                if (!quiet) StatusText.Text = "Saved. A running dashboard's lights switch over within a second.";
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
            FlushCommit();
            _liveSaveTimer.Stop();
            if (Dirty) SaveProfile(true);
        }

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
