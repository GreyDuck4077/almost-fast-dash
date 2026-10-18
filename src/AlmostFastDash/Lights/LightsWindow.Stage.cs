using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using DrawingColor = System.Drawing.Color;

namespace AlmostFastDash.Lights
{
    public partial class LightsWindow : Window
    {
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
                    Style = (Style)FindResource("T.Caption"), TextWrapping = TextWrapping.Wrap,
                        HorizontalAlignment = HorizontalAlignment.Center,
                    Text = "No LED devices yet. Add them on the Devices page."
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
                panel.Children.Add(new TextBlock { Text = device.Name.ToUpperInvariant(), Style = (Style)FindResource("T.Overline"),
                    Margin = new Thickness(2, 0, 0, 8) });
                panel.Children.Add(row);
                DeviceStages.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(5, 7, 10)),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(29, 35, 45)), BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 0, 12),
                    Child = panel
                });
            }
        }

        private Ellipse MakeLed(double size, DropShadowEffect glow, LedRef led)
        {
            var dot = new Ellipse
            {
                Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Fill = (Brush)FindResource("Brush.LedOff"), Stroke = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                StrokeThickness = 1,
                Effect = glow, Tag = led, Cursor = Cursors.Help
            };
            dot.MouseEnter += (s, e) => ExplainLed(led);
            dot.MouseLeave += (s, e) => HoverText.Text = "Hover an LED to see which layer lit it.";
            return dot;
        }

        private void ExplainLed(LedRef led)
        {
            DeviceSettings device = led.Device < Devices.Count ? Devices[led.Device] : null;
            string groups = device == null ? ""
                : string.Join(", ", device.Groups.Where(g => g.Indexes().Contains(led.Index)).Select(g => g.Name));
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
    }
}
