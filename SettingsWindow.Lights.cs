using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using DrawingColor = System.Drawing.Color;

namespace AlmostFastOverlays
{
    public partial class SettingsWindow
    {
        private LightRenderer _previewLights;
        private LightProfile _previewLightsProfile;
        private DashboardSettings _previewDashboard;
        private readonly SyntheticTelemetry _previewSynthetic = new SyntheticTelemetry();
        private readonly Dictionary<string, LightsWindow> _lightDesigners = new Dictionary<string, LightsWindow>();
        private readonly List<Ellipse[]> _stripDots = new List<Ellipse[]>();
        private readonly List<DropShadowEffect[]> _stripGlows = new List<DropShadowEffect[]>();
        private string _stripsSignature;
        private readonly DeviceMirror _mirror = new DeviceMirror();
        private bool _dashboardRunning;
        private int _lastRequestTick;

        /// <summary>Screenshots for the self-check: never touch the LED devices.</summary>
        internal bool Offline { get; set; }

        // ------------------------------------------------------------------ card

        private void UpdateLightsCard()
        {
            int devices = _settings.General.Devices.Count(d => d.Enabled);
            LightsCardTitle.Text = "Lights";
            int layers = _dashboard.Lights?.Layers.Count ?? 0;
            LightsCardText.Text = devices == 0
                ? layers + " light layers, ready for when you add an LED device."
                : layers + " light layers drive " + (devices == 1 ? "your LED device" : devices + " LED devices") + ".";
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

        // ------------------------------------------------------------------ live preview of every device's LEDs

        /// <summary>One row of LEDs per enabled device, rebuilt when devices change.</summary>
        private void BuildLedStrips()
        {
            List<DeviceSettings> devices = _settings.General.Devices;
            string signature = LightRenderer.DeviceSignature(devices) + "|" + string.Join(",", devices.Select(d => d.Name));
            if (signature == _stripsSignature) return;
            _stripsSignature = signature;
            LedStripsHost.Children.Clear();
            _stripDots.Clear();
            _stripGlows.Clear();
            if (!devices.Any(d => d.Enabled))
            {
                LedStripsHost.Children.Add(new TextBlock { Style = (Style)FindResource("T.Caption"), TextWrapping = TextWrapping.Wrap, Text = "No LED devices yet. Add them on the Devices page." });
            }
            foreach (DeviceSettings device in devices)
            {
                var dots = new Ellipse[device.LedCount];
                var glows = new DropShadowEffect[device.LedCount];
                _stripDots.Add(dots);
                _stripGlows.Add(glows);
                if (!device.Enabled) continue;
                LedStripsHost.Children.Add(new TextBlock { Text = device.Name.ToUpperInvariant(), Style = (Style)FindResource("T.Overline"), Margin = new Thickness(0, 6, 0, 6) });
                double size = LedDrawing.FitSize(device, 480, 12);
                for (int i = 0; i < device.LedCount; i++)
                {
                    glows[i] = new DropShadowEffect { ShadowDepth = 0, BlurRadius = 12, Opacity = 0 };
                    dots[i] = new Ellipse
                    {
                        Width = size, Height = size, Fill = (Brush)FindResource("Brush.LedOff"),
                        Stroke = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), StrokeThickness = 1, Effect = glows[i], ToolTip = "LED " + (i + 1)
                    };
                }
                FrameworkElement drawing = LedDrawing.Arrange(device, dots, size);
                drawing.HorizontalAlignment = HorizontalAlignment.Left;
                LedStripsHost.Children.Add(new Border { Margin = new Thickness(0, 0, 0, 8), Child = drawing });
            }
            _previewLights = null;
        }

        private void UpdateLayeredPreview()
        {
            if (_dashboard == null) return;
            BuildLedStrips();
            LightProfile profile = _dashboard.Lights;
            if (_previewLights == null || _previewDashboard != _dashboard || _previewLightsProfile != profile
                || _previewLights.Signature != LightRenderer.DeviceSignature(_settings.General.Devices))
            {
                _previewLights = new LightRenderer(profile, _dashboard.AccentColor, new UnitSystem(_settings.General), _settings.General.Devices);
                _previewLightsProfile = profile;
                _previewDashboard = _dashboard;
            }
            TelemetrySnapshot snapshot;
            // Exactly the telemetry the screen preview and the light designer use for this state.
            snapshot = PreviewRequest.ForState(PreviewStateName, _previewSynthetic, _livePreview?.GetSnapshot());
            LightFrame frame = _previewLights.Compose(snapshot, Environment.TickCount);
            AnimateEditorScreen(snapshot);
            for (int d = 0; d < _stripDots.Count && d < frame.Leds.Length; d++)
                for (int i = 0; i < _stripDots[d].Length && i < frame.Leds[d].Length; i++)
                    if (_stripDots[d][i] != null) PaintLed(_stripDots[d][i], _stripGlows[d][i], frame.Leds[d][i]);

            // The real devices show the preview too: directly while the dashboard is stopped, or through
            // the running dashboard (which owns them) while iRacing isn't connected.
            // Only while the dashboard page is on screen: not while the window is still opening, or on another page.
            bool show = !Offline && IsLoaded && EditorPage.Visibility == Visibility.Visible && MirrorLeds.IsChecked == true && !_dashboardOperationInProgress && !_deviceBusy && WindowState != WindowState.Minimized;
            if (show && !_dashboardRunning) { PreviewRequest.Clear(); _mirror.Show(frame, _settings.General.Devices); }
            else if (show)
            {
                if (_mirror.Active) _mirror.Release();
                if (Environment.TickCount - _lastRequestTick > 500 || Environment.TickCount < _lastRequestTick)
                {
                    PreviewRequest.Write(_dashboard.Id, PreviewStateName);
                    _lastRequestTick = Environment.TickCount;
                }
            }
            else ReleaseDevices();
            MirrorNote.Text = _dashboardRunning
                ? "Live data takes over once iRacing connects."
                : "Lights your devices while the dashboard is stopped.";
        }

        /// <summary>Stops showing the preview on the devices, turning them off and freeing them for the dashboard or device setup.</summary>
        private void ReleaseDevices()
        {
            if (_mirror.Active) _mirror.Release();
            PreviewRequest.Clear();
        }

        private void PaintLed(Ellipse dot, DropShadowEffect glowEffect, DrawingColor color)
        {
            LightsWindow.ShowLed(color, out Color hue, out double glow);
            dot.Fill = glow <= 0 ? (Brush)FindResource("Brush.LedOff") : new SolidColorBrush(hue);
            glowEffect.Color = hue;
            glowEffect.Opacity = glow;
        }
    }
}
