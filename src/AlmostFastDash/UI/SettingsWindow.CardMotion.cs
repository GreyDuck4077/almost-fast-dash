using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace AlmostFastDash.UI
{
    public partial class SettingsWindow
    {
        // ==================================================================
        // A gallery card comes alive under the pointer: its picture plays the animated lap
        // ==================================================================

        private DispatcherTimer _cardTimer;
        private IDashboardRenderer _cardRenderer;
        private SyntheticTelemetry _cardLap;
        private WriteableBitmap _cardFrame;
        private Border _cardPicture;
        private Brush _cardStill;
        /// <summary>Started by the --ui-snapshot self-check, which isn't the window in use.</summary>
        private bool _cardSnapshot;

        /// <summary>Plays the animated lap in a card's picture while the pointer is on the card.</summary>
        private void AnimateCardOnHover(FrameworkElement card, Border picture, DashboardSettings dashboard, UnitSystem units)
        {
            card.MouseEnter += (s, e) => StartCardMotion(picture, dashboard, units);
            card.MouseLeave += (s, e) => StopCardMotion();
        }

        private void StartCardMotion(Border picture, DashboardSettings dashboard, UnitSystem units, bool snapshot = false)
        {
            StopCardMotion();
            if (!snapshot && (!Motion.On || !IsActive)) return;
            _cardSnapshot = snapshot;
            try
            {
                System.Drawing.Size size = dashboard.ScreenSize();
                _cardRenderer = DashboardRendererFactory.Create(size.Width, size.Height, dashboard, units);
                _cardLap = new SyntheticTelemetry();
                _cardFrame = new WriteableBitmap(size.Width, size.Height, 96, 96, PixelFormats.Bgra32, null);
            }
            catch
            {
                StopCardMotion();
                return;
            }
            _cardPicture = picture;
            _cardStill = picture.Background;
            var still = _cardStill as ImageBrush;
            picture.Background = new ImageBrush(_cardFrame)
            {
                Stretch = still?.Stretch ?? Stretch.Uniform, AlignmentX = still?.AlignmentX ?? AlignmentX.Center, AlignmentY = still?.AlignmentY ?? AlignmentY.Center
            };
            _cardTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
            _cardTimer.Tick += (s, e) => CardFrame();
            CardFrame();
            _cardTimer.Start();
        }

        private void CardFrame()
        {
            if (_cardRenderer == null || _cardFrame == null) return;
            // Only while the app is the window in use: behind iRacing it costs nothing.
            if (!IsActive && !_cardSnapshot) { StopCardMotion(); return; }
            TelemetrySnapshot snapshot = _cardLap.Next(out bool flash, out int previousGear, out float progress);
            using (System.Drawing.Bitmap frame = _cardRenderer.Render(snapshot, flash, previousGear, progress))
            {
                var rect = new System.Drawing.Rectangle(0, 0, frame.Width, frame.Height);
                System.Drawing.Imaging.BitmapData data = frame.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                try { _cardFrame.WritePixels(new Int32Rect(0, 0, frame.Width, frame.Height), data.Scan0, data.Stride * frame.Height, data.Stride); }
                finally { frame.UnlockBits(data); }
            }
        }

        // ------------------------------------------------------------------ the dashboard page's screen, in motion

        private IDashboardRenderer _editorRenderer;
        private DashboardSettings _editorRendered;
        private WriteableBitmap _editorFrame;

        /// <summary>
        /// Plays the dashboard page's screen preview in step with its lights, for the animated lap and a live
        /// session: while the page is open and the app is the window in use; otherwise it holds its last picture.
        /// </summary>
        private void AnimateEditorScreen(TelemetrySnapshot snapshot)
        {
            string state = PreviewStateName;
            if ((state != PreviewRequest.AnimatedLap && state != PreviewRequest.LiveIRacing) || !IsActive || EditorPage.Visibility != Visibility.Visible || _dashboard == null) return;
            try
            {
                if (_editorRenderer == null || _editorRendered != _dashboard)
                {
                    ForgetEditorScreen();
                    System.Drawing.Size size = _dashboard.ScreenSize();
                    _editorRenderer = DashboardRendererFactory.Create(size.Width, size.Height, _dashboard, new UnitSystem(_settings.General));
                    _editorRendered = _dashboard;
                    _editorFrame = new WriteableBitmap(size.Width, size.Height, 96, 96, PixelFormats.Bgra32, null);
                }
                using (System.Drawing.Bitmap frame = _editorRenderer.Render(snapshot, snapshot.ShiftAlert, snapshot.Gear, 1f))
                {
                    var rect = new System.Drawing.Rectangle(0, 0, frame.Width, frame.Height);
                    System.Drawing.Imaging.BitmapData data = frame.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    try { _editorFrame.WritePixels(new Int32Rect(0, 0, frame.Width, frame.Height), data.Scan0, data.Stride * frame.Height, data.Stride); }
                    finally { frame.UnlockBits(data); }
                }
                if (PreviewBrush.ImageSource != _editorFrame) PreviewBrush.ImageSource = _editorFrame;
            }
            catch { ForgetEditorScreen(); }
        }

        /// <summary>The design changed, or the page did: the next frame starts a fresh renderer.</summary>
        private void ForgetEditorScreen()
        {
            _editorRenderer?.Dispose();
            _editorRenderer = null;
            _editorRendered = null;
            _editorFrame = null;
        }

        private void StopCardMotion()
        {
            _cardTimer?.Stop();
            _cardTimer = null;
            _cardRenderer?.Dispose();
            _cardRenderer = null;
            if (_cardPicture != null) _cardPicture.Background = _cardStill;
            _cardPicture = null;
            _cardStill = null;
            _cardFrame = null;
        }
    }
}
