using System;
using System.Windows;
using Brush = System.Windows.Media.Brush;

namespace AlmostFastOverlays
{
    /// <summary>
    /// What a new user needs to know first: the welcome card's three steps (plug in the wheel screen, add LED
    /// devices, start the dashboard) with each one's live state, and whether the wheel screen can be seen at all.
    /// </summary>
    public partial class SettingsWindow
    {
        private bool _screenPresent;
        private DateTime _screenCheckedAt = DateTime.MinValue;

        /// <summary>Whether the wheel screen is plugged in, looked up at most every two seconds.</summary>
        private bool ScreenPresent()
        {
            if (DateTime.UtcNow - _screenCheckedAt < TimeSpan.FromSeconds(2)) return _screenPresent;
            _screenCheckedAt = DateTime.UtcNow;
            try { _screenPresent = WheelScreen.IsPresent(); }
            catch { _screenPresent = false; }
            return _screenPresent;
        }

        private static string ScreenSize => WheelScreen.Width + " × " + WheelScreen.Height;

        /// <summary>The welcome card, while it's wanted: each step marked done as it is.</summary>
        private void RefreshWelcome()
        {
            bool show = _settings.General.ShowWelcome;
            WelcomeCard.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (!show) return;
            bool screen = ScreenPresent();
            MarkStep(WelcomeScreenBadge, WelcomeScreenMark, "1", screen);
            WelcomeScreenText.Text = screen
                ? "Found: " + ScreenSize + "."
                : "Not found yet. Plug it in by USB. VoCore-family M-PRO screens are supported.";
            int devices = _settings.General.Devices.Count;
            MarkStep(WelcomeLedBadge, WelcomeLedMark, "2", devices > 0);
            WelcomeLedText.Text = devices > 0
                ? devices + (devices == 1 ? " device" : " devices") + " set up. Add more any time on the Devices page."
                : "Optional: the LEDs around a screen, light bars and strips. The app finds them for you.";
        }

        private void MarkStep(System.Windows.Controls.Border badge, System.Windows.Controls.TextBlock mark, string number, bool done)
        {
            badge.Background = (Brush)FindResource(done ? "Brush.SuccessSubtle" : "Brush.SurfaceRaised");
            mark.Text = done ? "✓" : number;
            mark.Foreground = (Brush)FindResource(done ? "Brush.Success" : "Brush.TextSecondary");
        }

        /// <summary>The welcome card has done its job once the dashboard runs, or when it's dismissed.</summary>
        private void EndWelcome()
        {
            if (!_settings.General.ShowWelcome) return;
            _settings.General.ShowWelcome = false;
            WelcomeCard.Visibility = Visibility.Collapsed;
            Save();
        }

        private void WelcomeDismiss_Click(object sender, RoutedEventArgs e) => EndWelcome();

        private void WelcomeAddDevice_Click(object sender, RoutedEventArgs e)
        {
            NavSettings.IsChecked = true;
            OpenDeviceSetup(null);
        }

        /// <summary>The wheel screen's status on the Devices page: showing the dashboard, plugged in, or not found.</summary>
        private void RefreshScreenStatus(bool drawing)
        {
            string text, tone;
            if (!ScreenPresent())
            {
                text = "Not found. Check it's plugged in by USB. VoCore-family M-PRO screens are supported.";
                tone = "Brush.Warning";
            }
            else if (_dashboardRunning)
            {
                text = drawing ? "Connected · " + ScreenSize + " · showing the dashboard" : "Connecting…";
                tone = drawing ? "Brush.Success" : "Brush.Warning";
            }
            else
            {
                text = "Plugged in · " + ScreenSize + " · not in use right now";
                tone = "Brush.TextTertiary";
            }
            ScreenStatusText.Text = text;
            ScreenStatusDot.Fill = (Brush)FindResource(tone);
        }
    }
}
