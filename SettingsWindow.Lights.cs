using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DrawingColor = System.Drawing.Color;

namespace AlmostFastOverlays
{
    public partial class SettingsWindow
    {
        private LightRenderer _previewLights;
        private LightProfile _previewLightsProfile;
        private readonly SyntheticTelemetry _previewSynthetic = new SyntheticTelemetry();
        private readonly Dictionary<string, LightsWindow> _lightDesigners = new Dictionary<string, LightsWindow>();

        // ------------------------------------------------------------------ card

        private void UpdateLightsCard()
        {
            bool layered = _dashboard.Lights != null;
            ClassicLights.Visibility = layered ? Visibility.Collapsed : Visibility.Visible;
            ClassicLightsButton.Visibility = layered ? Visibility.Visible : Visibility.Collapsed;
            LightsCardTitle.Text = layered ? "Light layers" : "Lights";
            LightsCardText.Text = layered
                ? _dashboard.Lights.Layers.Count + " layers drive the DV-5 and light bar for this dashboard. Open the light designer to change them."
                : "Using the classic light settings below. The light designer adds priorities, conditions, car-accurate rev lights, fades and more; your current settings carry over.";
            LightsButtonLabel.Text = "Open light designer";
        }

        private void Lights_Click(object sender, RoutedEventArgs e)
        {
            FlushSave();
            DashboardSettings dashboard = _dashboard;
            if (_lightDesigners.TryGetValue(dashboard.Id, out LightsWindow open))
            {
                if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
                open.Activate();
                return;
            }
            var window = new LightsWindow(_settings, dashboard, () =>
            {
                if (EditorPage.Visibility == Visibility.Visible && _dashboard == dashboard) LoadDashboardValues();
                ShowSaved("Lights saved");
            }) { Owner = this };
            _lightDesigners[dashboard.Id] = window;
            window.Closed += (s, a) => _lightDesigners.Remove(dashboard.Id);
            window.Show();
        }

        private void ClassicLights_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show(this, "Switch \"" + _dashboard.Name + "\" back to the classic light settings? Its light layers will be removed.",
                    "Classic lights", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            _dashboard.Lights = null;
            Save("Classic lights");
            LoadDashboardValues();
        }

        // ------------------------------------------------------------------ fast LED updates

        private void FastLed_Click(object sender, RoutedEventArgs e)
        {
            _settings.General.FastLedUpdates = FastLedToggle.IsChecked == true;
            Save(_settings.General.FastLedUpdates ? "Fast LED updates on" : "Fast LED updates off");
        }

        private async void TestFastLed_Click(object sender, RoutedEventArgs e)
        {
            FastLedStatus.Visibility = Visibility.Visible;
            if (IsDashboardRunning())
            {
                SetFastStatus("Stop the dashboard first so the lights are free to test.", "Brush.Warning");
                return;
            }
            string port = DevicePorts.Resolve(_settings.General.LedPort, DevicePorts.Dv5);
            TestFastLedButton.IsEnabled = false;
            SetFastStatus("Showing a rainbow on " + port + " using fast updates…", "Brush.TextSecondary");
            try
            {
                await Task.Run(() => RunFastTest(port));
                MessageBoxResult seen = MessageBox.Show(this,
                    "Did all 16 DV-5 lights show a rainbow, from red up the left side, across the top, to violet down the right?",
                    "Fast LED updates", MessageBoxButton.YesNo, MessageBoxImage.Question);
                bool works = seen == MessageBoxResult.Yes;
                _settings.General.FastLedUpdates = works;
                FastLedToggle.IsChecked = works;
                Save(works ? "Fast LED updates on" : "Fast LED updates off");
                SetFastStatus(works
                    ? "Fast updates are on. Lights now update in one message per frame."
                    : "Fast updates stay off; your DV-5 needs the standard mode. Nothing else changes.", works ? "Brush.Success" : "Brush.TextSecondary");
            }
            catch (Exception ex)
            {
                SetFastStatus("Couldn't reach the lights on " + port + ". " + ex.Message, "Brush.Danger");
            }
            finally
            {
                TestFastLedButton.IsEnabled = true;
            }
        }

        private static void RunFastTest(string port)
        {
            using (var leds = new Dv5LedController(port, 115200, LedFrame.Count) { Batched = true })
            {
                leds.Connect();
                leds.QueryDeviceName();
                var frame = new DrawingColor[LedFrame.Count];
                for (int i = 0; i < frame.Length; i++)
                {
                    // Every LED a different hue, so a frame that is only partly applied is obvious.
                    double h = i / (double)frame.Length * 300;
                    frame[i] = FromHue(h, 0.45);
                }
                leds.SetFrame(frame);
                Thread.Sleep(4000);
                leds.Clear();
            }
        }

        private static DrawingColor FromHue(double hue, double level)
        {
            double c = 1, x = 1 - Math.Abs(hue / 60 % 2 - 1);
            double r, g, b;
            if (hue < 60) { r = c; g = x; b = 0; }
            else if (hue < 120) { r = x; g = c; b = 0; }
            else if (hue < 180) { r = 0; g = c; b = x; }
            else if (hue < 240) { r = 0; g = x; b = c; }
            else if (hue < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }
            return DrawingColor.FromArgb((int)(r * 255 * level), (int)(g * 255 * level), (int)(b * 255 * level));
        }

        private void SetFastStatus(string text, string brushKey)
        {
            FastLedStatus.Text = text;
            FastLedStatus.Foreground = (Brush)FindResource(brushKey);
        }

        // ------------------------------------------------------------------ preview of layered lights

        private void UpdateLayeredPreview()
        {
            LightProfile profile = _dashboard.Lights;
            int browCount = Math.Max(1, _browDots.Count);
            if (_previewLights == null || _previewLightsProfile != profile || _previewLights.BrowCount != browCount)
            {
                _previewLights = new LightRenderer(profile, _dashboard.AccentColor, new UnitSystem(_settings.General), browCount);
                _previewLightsProfile = profile;
            }
            TelemetrySnapshot snapshot;
            if (AnimateLeds.IsChecked == true) snapshot = _previewSynthetic.Next(out _, out _, out _);
            else
                switch (PreviewState.SelectedIndex)
                {
                    case 1: snapshot = LightsWindow.Scenario("Shift point"); break;
                    case 2: snapshot = IRacingTelemetrySource.CreatePitLanePreviewSnapshot(); break;
                    case 3: snapshot = IRacingTelemetrySource.CreateSpotterPreviewSnapshot(); break;
                    default: snapshot = IRacingTelemetrySource.CreatePreviewSnapshot(_previewFlag); break;
                }
            LightFrame frame = _previewLights.Compose(snapshot, Environment.TickCount);
            for (int i = 0; i < LedFrame.Count; i++) PaintLed(_ledDots[i], _ledGlows[i], frame.Dv5[i]);
            for (int i = 0; i < _browDots.Count && i < BrowLedsPreview.Children.Count && i < frame.Brow.Length; i++)
            {
                var dot = (Border)BrowLedsPreview.Children[i];
                LightsWindow.ShowLed(frame.Brow[i], profile.Gamma, out Color hue, out double glow);
                dot.Background = glow <= 0 ? (Brush)FindResource("Brush.LedOff") : new SolidColorBrush(hue);
                _browGlows[i].Color = hue;
                _browGlows[i].Opacity = glow;
            }
        }

        private void PaintLed(System.Windows.Shapes.Ellipse dot, System.Windows.Media.Effects.DropShadowEffect glowEffect, DrawingColor color)
        {
            LightsWindow.ShowLed(color, _dashboard.Lights?.Gamma ?? false, out Color hue, out double glow);
            dot.Fill = glow <= 0 ? (Brush)FindResource("Brush.LedOff") : new SolidColorBrush(hue);
            glowEffect.Color = hue;
            glowEffect.Opacity = glow;
        }
    }
}
