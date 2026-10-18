using System.Windows;
using Brush = System.Windows.Media.Brush;

namespace AlmostFastDash.UI
{
    /// <summary>
    /// What a new user needs to know first: the welcome card's three steps (where the dashboard shows: a wheel
    /// screen or the PC; add LED devices; start the dashboard), each with its live state.
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>The welcome card, while it's wanted: each step marked done as it is.</summary>
        private void RefreshWelcome()
        {
            bool show = _settings.General.ShowWelcome;
            WelcomeCard.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (!show) return;
            bool screen = ScreenPresent();
            string pc = _settings.General.EffectivePcOutput();
            bool placed = screen || pc != "Off";
            MarkStep(WelcomeScreenBadge, WelcomeScreenMark, "1", placed);
            WelcomeScreenText.Text = screen ? "Wheel screen found: " + ScreenSize + "."
                : pc == MonitorOutput.Windowed ? "In a window on your PC."
                : pc == MonitorOutput.Overlay ? "Over the game on your PC."
                : pc == MonitorOutput.FullScreen ? "Full screen on another display."
                : "Plug in a wheel screen, or show the dashboard on your PC.";
            WelcomePcButton.Visibility = placed ? Visibility.Collapsed : Visibility.Visible;
            int devices = _settings.General.Devices.Count;
            MarkStep(WelcomeLedBadge, WelcomeLedMark, "2", devices > 0);
            WelcomeLedText.Text = devices > 0
                ? devices + (devices == 1 ? " device" : " devices") + " set up."
                : "Optional: wheel LEDs, light bars and strips.";
        }

        /// <summary>No wheel screen: show it in a window on the PC (Show on, at the top, changes it any time).</summary>
        private void WelcomeShowOnPc_Click(object sender, RoutedEventArgs e) => ShowWindow.IsChecked = true;

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
    }
}
