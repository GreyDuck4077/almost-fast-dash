using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AlmostFastOverlays
{
    public partial class SettingsWindow
    {
        /// <summary>Slides and fades a page in, iOS style.</summary>
        private static void AnimatePageIn(FrameworkElement page)
        {
            var shift = new TranslateTransform(0, 14);
            page.RenderTransform = shift;
            var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(14, 0, new Duration(TimeSpan.FromMilliseconds(340))) { EasingFunction = ease });
            page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(260))));
        }

        /// <summary>Opens a page for the --ui-snapshot self-check.</summary>
        internal void PrepareSnapshot(string scenario)
        {
            if (scenario.Contains("editor"))
            {
                ShowEditor(_settings.GetDefaultDashboard());
                if (scenario.Contains("shift")) PreviewState.SelectedIndex = 1;
            }
            else if (scenario.Contains("devices"))
            {
                NavSettings.IsChecked = true;
                if (scenario.Contains("bottom"))
                    Dispatcher.BeginInvoke(new Action(() => SettingsPage.ScrollToEnd()), System.Windows.Threading.DispatcherPriority.Loaded);
            }
            else if (scenario.Contains("app")) NavApp.IsChecked = true;
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
            if (show) list.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(220))));
        }
    }
}
