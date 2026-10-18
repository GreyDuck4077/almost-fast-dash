using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using DrawingColor = System.Drawing.Color;

namespace AlmostFastDash.UI
{
    internal sealed partial class DeviceSetupWindow : Window
    {
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
                    if (error != null) _status.Text = "Lost contact with " + _device.Name + ": " + error +
                        " Check its cable and power; the LEDs come back when it does.";
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
                body.Children.Add(new TextBlock { Text = "Couldn't reach " + _device.Name + " on " + _device.Connection + ".",
                    Style = (Style)FindResource("T.Label") });
                body.Children.Add(new TextBlock { Text = exception.Message, Style = (Style)FindResource("T.Caption"),
                    TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 12) });
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
            _subtitle.Text = "Click where the lit LED is on the drawing. Most devices take two clicks.";
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
            _mapPrompt = new TextBlock { FontSize = 22, FontWeight = FontWeights.SemiBold,
                FontFamily = (FontFamily)FindResource("Font.Display"), TextWrapping = TextWrapping.Wrap };
            _mapHint = new TextBlock { Style = (Style)FindResource("T.Secondary"), TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 16), MaxWidth = 680, HorizontalAlignment = HorizontalAlignment.Left };
            body.Children.Add(_mapPrompt);
            body.Children.Add(_mapHint);
            // The check question and its answers sit right under the prompt, where they're seen.
            _verify = new StackPanel { Margin = new Thickness(0, -4, 0, 16) };
            body.Children.Add(_verify);
            _mapDrawing = new ContentControl { HorizontalAlignment = HorizontalAlignment.Center };
            body.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(5, 7, 10)), BorderBrush = (Brush)FindResource("Brush.Border"),
                    BorderThickness = new Thickness(1),
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
                    Fill = chasing ? Brushes.White : led >= 0 ? (Brush)FindResource("Brush.AccentSubtle")
                    : (Brush)FindResource("Brush.LedOff"),
                    Stroke = led >= 0 ? (Brush)FindResource("Brush.Accent") : new SolidColorBrush(Color.FromArgb(90, 255, 255, 255))
                };
                var label = new TextBlock
                {
                    Text = led >= 0 ? (led + 1).ToString() : "", FontSize = size * 0.38, FontWeight = FontWeights.SemiBold,
                    Foreground = chasing ? Brushes.Black : (Brush)FindResource("Brush.Text"),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                };
                var cell = new Grid { Width = size, Height = size, Background = Brushes.Transparent,
                    ToolTip = led >= 0 ? "LED " + (led + 1) : "Click if LED " + (asking + 1) + " is here" };
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
                    _mapHint.Text = "Click LEDs in different places on the drawing; the same ones should light on your device. " +
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
                _mapHint.Text = _mapper.ByHand
                    ? "Placing every LED by hand: " + asking + " of " + _mapper.Total + " placed. Undo takes back the last one."
                    : _mapper.Pending
                    ? "The one after LED #" + asking + ". That shows the app which way the chain runs."
                    : asking == 0 ? "It's the bright white LED."
                    : "The chain jumped or reached the end of a part, so the app needs LED #" + (asking + 1) + " too.";
                _next.IsEnabled = false;
                var missing = new Button
                {
                    Content = "LED #" + (asking + 1) + " didn't light up ›", Style = (Style)FindResource("Btn.Ghost"),
                        Foreground = (Brush)FindResource("Brush.Accent"),
                    HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(0, 2, 0, 2),
                    ToolTip = "Nothing lit on the device: the app works out why"
                };
                missing.Click += (s, e) => { _notLit = asking; RenderMap(); };
                _verify.Children.Add(missing);
                _verify.Visibility = Visibility.Visible;
                ShowAsking(asking);
            }
        }

        /// <summary>An LED that didn't light: the device has fewer LEDs than the shape, or (for the first) isn't showing
        /// anything.</summary>
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
                _mapHint.Text = "The shape adds up to " + _layout.Total + ", but LED #" + (led + 1) +
                    " didn't light, so it's past the end of the device. Go back and correct the counts.";
                var back = new Button { Content = "Back to the shape", Style = (Style)FindResource("Btn.Primary"),
                    Margin = new Thickness(0, 0, 8, 0) };
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
                ? "Some LEDs don't follow the chain, such as one side wired the other way around. Place each LED by hand instead."
                : "If each LED you clicked lit up in the same place on your device, the map is right.";
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            if (!_doubted)
            {
                var yes = new Button { Content = "Yes, they all matched", Style = (Style)FindResource("Btn.Primary"),
                    Margin = new Thickness(0, 0, 8, 0) };
                yes.Click += (s, e) => { _verified = true; _preview?.Show(new DrawingColor[_mapper.Total]); _testLed = -1; RenderMap(); };
                var no = new Button { Content = "No, something was off" };
                no.Click += (s, e) => { _doubted = true; RenderMap(); };
                buttons.Children.Add(yes);
                buttons.Children.Add(no);
            }
            else
            {
                var byHand = new Button { Content = "Place every LED by hand", Style = (Style)FindResource("Btn.Primary"),
                    Margin = new Thickness(0, 0, 8, 0) };
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
    }
}
