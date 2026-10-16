using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AlmostFastOverlays
{
    public partial class SettingsWindow
    {
        /// <summary>Slides and fades a page in: sideways when drilling in (1) or going back (-1), rising otherwise.</summary>
        private static void AnimatePageIn(FrameworkElement page, int direction)
        {
            page.RenderTransform = new TranslateTransform();
            Motion.Enter(page, direction * 28, direction == 0 ? 14 : 0, 0, 340);
        }

        /// <summary>Opens a page for the --ui-snapshot self-check.</summary>
        internal void PrepareSnapshot(string scenario)
        {
            if (scenario.Contains("editor"))
            {
                ShowEditor(_settings.GetDefaultDashboard());
                if (scenario.Contains("shift")) PreviewState.SelectedItem = "Shift point";
                if (scenario.Contains("carleft")) PreviewState.SelectedItem = "Car left";
                if (scenario.Contains("carright")) PreviewState.SelectedItem = "Car right";
            }
            else if (scenario.Contains("devices"))
            {
                NavSettings.IsChecked = true;
                if (scenario.Contains("bottom"))
                    Dispatcher.BeginInvoke(new Action(() => SettingsPage.ScrollToEnd()), System.Windows.Threading.DispatcherPriority.Loaded);
            }
            else if (scenario.Contains("app")) NavApp.IsChecked = true;
            // The first gallery card as the pointer finds it: its picture playing the animated lap.
            if (scenario.Contains("hover") && _galleryCards.Count > 0 && _galleryCards[0].Child is StackPanel stack && stack.Children[0] is Border picture)
                StartCardMotion(picture, _settings.Dashboards[0], new UnitSystem(_settings.General), true);
        }

        /// <summary>A sidebar sub-item: the name, with a small marker on the right.</summary>
        private static Grid SubNavRow(string name, UIElement marker)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            if (marker != null)
            {
                Grid.SetColumn(marker, 1);
                row.Children.Add(marker);
            }
            return row;
        }

        /// <summary>Opens or folds a section's list in the sidebar, with a short fade.</summary>
        private static void ShowSubNav(StackPanel list, bool show)
        {
            if (show == (list.Visibility == Visibility.Visible)) return;
            list.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (show) Motion.Stagger(list.Children, 30, 200);
        }
    }
}
