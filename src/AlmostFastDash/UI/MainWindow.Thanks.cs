using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace AlmostFastDash.UI
{
    /// <summary>
    /// A thank-you note over the whole window, on the very first start of a new install and never again
    /// (GeneralSettings.ShowThanks). Got it, Esc, Enter or a click outside the note closes it for good.
    /// </summary>
    public partial class MainWindow
    {
        private void ShowThanksOnce()
        {
            if (!_settings.General.ShowThanks || ThanksOverlay.Visibility == Visibility.Visible) return;
            ThanksOverlay.Visibility = Visibility.Visible;
            Motion.FadeIn(ThanksOverlay, 220);
            PreviewKeyDown += ThanksKeyDown;
            ThanksClose.Focus();
        }

        private void CloseThanks()
        {
            PreviewKeyDown -= ThanksKeyDown;
            if (ThanksOverlay.Visibility != Visibility.Visible) return;
            ThanksOverlay.Visibility = Visibility.Collapsed;
            _settings.General.ShowThanks = false;
            Save();
        }

        private void ThanksKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape && e.Key != Key.Enter) return;
            // Enter on Open Discord opens it; anywhere else it closes the note.
            if (e.Key == Key.Enter && Keyboard.FocusedElement is System.Windows.Controls.Button button && button != ThanksClose) return;
            CloseThanks();
            e.Handled = true;
        }

        private void ThanksClose_Click(object sender, RoutedEventArgs e) => CloseThanks();

        // A click outside the note closes it; a click on it doesn't.
        private void ThanksBackdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => CloseThanks();

        private void ThanksCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

        private void DiscordLink_Click(object sender, RoutedEventArgs e)
        {
            try { Process.Start(new ProcessStartInfo(Links.Discord) { UseShellExecute = true }); }
            catch { }
        }
    }
}
