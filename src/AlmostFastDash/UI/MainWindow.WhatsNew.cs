using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AlmostFastDash.UI
{
    public partial class MainWindow
    {
        private Border _whatsNewCard;

        /// <summary>After an update, the Dashboards page says once what's new; "Got it" puts it away until the next one.</summary>
        private void RefreshWhatsNew()
        {
            bool show = !_settings.General.ShowWelcome && WhatsNew.IsNewTo(_settings.General.SeenVersion) &&
                WhatsNew.Highlights.Length > 0;
            if (!show)
            {
                if (_whatsNewCard != null) _whatsNewCard.Visibility = Visibility.Collapsed;
                return;
            }
            if (_whatsNewCard == null) BuildWhatsNew();
            _whatsNewCard.Visibility = Visibility.Visible;
        }

        private void BuildWhatsNew()
        {
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var titles = new StackPanel();
            titles.Children.Add(new TextBlock { Text = "What's new in " + WhatsNew.Version, Style = (Style)FindResource("T.Title") });
            titles.Children.Add(new TextBlock { Text = "Updated. Here's what changed lately.", Style = (Style)FindResource("T.Caption"),
                Margin = new Thickness(0, 4, 0, 0) });
            header.Children.Add(titles);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
            var all = new Button { Content = "Every change", Style = (Style)FindResource("Btn.Ghost"), Margin = new Thickness(0, 0, 6, 0),
                ToolTip = "The changelog, on the project's page" };
            all.Click += (s, e) =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(WhatsNew.Changelog) { UseShellExecute = true }); } catch { }
            };
            var done = new Button { Content = "Got it", Style = (Style)FindResource("Btn.Ghost") };
            done.Click += (s, e) =>
            {
                _settings.General.SeenVersion = WhatsNew.Version;
                Save();
                _whatsNewCard.Visibility = Visibility.Collapsed;
            };
            buttons.Children.Add(all);
            buttons.Children.Add(done);
            Grid.SetColumn(buttons, 1);
            header.Children.Add(buttons);

            var list = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
            foreach (string line in WhatsNew.Highlights)
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
                var dot = new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3),
                    Background = (Brush)FindResource("Brush.Accent"), Margin = new Thickness(2, 7, 12, 0),
                    VerticalAlignment = VerticalAlignment.Top };
                DockPanel.SetDock(dot, Dock.Left);
                row.Children.Add(dot);
                row.Children.Add(new TextBlock { Text = line, Style = (Style)FindResource("T.Secondary"),
                    TextWrapping = TextWrapping.Wrap });
                list.Children.Add(row);
            }
            var body = new StackPanel();
            body.Children.Add(header);
            body.Children.Add(list);
            _whatsNewCard = new Border { Style = (Style)FindResource("Card"), Margin = new Thickness(0, 0, 0, 24), Child = body };
            // Beside the welcome card, where a new install would see its first steps.
            var host = (Panel)WelcomeCard.Parent;
            host.Children.Insert(host.Children.IndexOf(WelcomeCard) + 1, _whatsNewCard);
            Motion.FadeIn(_whatsNewCard);
        }
    }
}
