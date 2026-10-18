using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace AlmostFastDash.UI
{
    public partial class MainWindow : Window
    {
        // ------------------------------------------------------------------
        // Preview
        // ------------------------------------------------------------------

        private void PreviewState_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshPreview();

        private void RefreshPreview()
        {
            if (_dashboard == null || PreviewState == null) return;
            string state = PreviewStateName;
            if (state == PreviewRequest.LiveIRacing) StartLivePreview(); else StopLivePreview();
            // The screen shows the same telemetry as the lights. A lap in motion starts as a still of racing, then plays
            // with the lights while the window is in use (see AnimateEditorScreen).
            TelemetrySnapshot snapshot = state == PreviewRequest.AnimatedLap ? LightsWindow.Scenario("Racing")
                : PreviewRequest.ForState(state, _previewSynthetic, _livePreview?.GetSnapshot());
            ForgetEditorScreen();
            PreviewBrush.ImageSource = RenderPreview(_dashboard, new UnitSystem(_settings.General), snapshot, snapshot.ShiftAlert);
            UpdateLedPreview();
        }

        private string PreviewStateName => PreviewState.SelectedItem as string ?? PreviewRequest.AnimatedLap;

        private IRacingTelemetrySource _livePreview;

        private DiagnosticLogger _livePreviewLog;

        private void StartLivePreview()
        {
            if (_livePreview != null) return;
            _livePreviewLog = new DiagnosticLogger(false);
            _livePreview = new IRacingTelemetrySource(_livePreviewLog, _dashboard);
            _livePreview.Start();
        }

        private void StopLivePreview()
        {
            _livePreview?.Dispose();
            _livePreview = null;
            _livePreviewLog?.Dispose();
            _livePreviewLog = null;
        }

        private static BitmapSource RenderPreview(DashboardSettings dashboard, UnitSystem units, TelemetrySnapshot snapshot, bool shiftFlash = false)
        {
            snapshot = snapshot ?? IRacingTelemetrySource.CreatePreviewSnapshot();
            System.Drawing.Size size = dashboard.ScreenSize();
            using (IDashboardRenderer renderer = DashboardRendererFactory.Create(size.Width, size.Height, dashboard, units))
            using (var frame = renderer.Render(snapshot, shiftFlash, snapshot.Gear, 1f))
            using (var stream = new MemoryStream())
            {
                frame.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                stream.Position = 0;
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                return image;
            }
        }

        private void UpdateLedPreview() => UpdateLayeredPreview();
    }
}
