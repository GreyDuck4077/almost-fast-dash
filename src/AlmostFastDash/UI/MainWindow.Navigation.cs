using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Linq;
using System.Windows.Threading;

namespace AlmostFastDash.UI
{
    public partial class MainWindow
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
            // "…-scroll900": the page scrolled down that far, to see all of a long page.
            var scroll = System.Text.RegularExpressions.Regex.Match(scenario, @"scroll(\d+)");
            if (scroll.Success)
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    foreach (ScrollViewer page in new[] { GalleryPage, EditorPage, SettingsPage, AppPage })
                        if (page.Visibility == Visibility.Visible) page.ScrollToVerticalOffset(double.Parse(scroll.Groups[1].Value));
                }), System.Windows.Threading.DispatcherPriority.Loaded);
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

        // ------------------------------------------------------------------
        // Navigation
        // ------------------------------------------------------------------

        private void NavGallery_Checked(object sender, RoutedEventArgs e) => ShowPage(GalleryPage);

        private void NavSettings_Checked(object sender, RoutedEventArgs e) => ShowPage(SettingsPage);

        private void BackToGallery_Click(object sender, RoutedEventArgs e) => ShowGallery();

        // A dashboard's editor lives inside Dashboards, so the Dashboards item stays
        // checked there and clicking it again goes back to the gallery.
        private void NavGallery_Click(object sender, RoutedEventArgs e)
        {
            if (GalleryPage.Visibility != Visibility.Visible) ShowGallery();
        }

        private void ShowGallery()
        {
            if (NavGallery.IsChecked == true) ShowPage(GalleryPage);
            else NavGallery.IsChecked = true;
        }

        private bool _buildingDashboardNav;

        /// <summary>
        /// The dashboards listed under Dashboards in the sidebar. Picking one opens its page.
        /// </summary>
        private void BuildDashboardSubNav()
        {
            _buildingDashboardNav = true;
            DashboardSubNav.Children.Clear();
            bool onEditor = EditorPage.Visibility == Visibility.Visible;
            foreach (DashboardSettings dashboard in _settings.Dashboards)
            {
                DashboardSettings d = dashboard;
                TextBlock marker = d.Id == _settings.DefaultDashboardId
                    ? new TextBlock { Text = "Default", FontSize = 12, Foreground = (Brush)FindResource("Brush.TextTertiary"), VerticalAlignment = VerticalAlignment.Center }
                    : null;
                var item = new RadioButton
                {
                    Style = (Style)FindResource("Nav.SubItem"), GroupName = "DashboardSub", Content = SubNavRow(d.Name, marker),
                    IsChecked = onEditor && d == _dashboard
                };
                item.Checked += (s, e) => { if (!_buildingDashboardNav && !(EditorPage.Visibility == Visibility.Visible && d == _dashboard)) ShowEditor(d); };
                item.ContextMenu = DashboardMenu(d);
                System.Windows.Automation.AutomationProperties.SetName(item, d.Name);
                DashboardSubNav.Children.Add(item);
            }
            _buildingDashboardNav = false;
        }

        private void NavApp_Checked(object sender, RoutedEventArgs e) => ShowPage(AppPage);

        private void ShowPage(ScrollViewer page)
        {
            DisarmConfirm();
            StopCardMotion();
            // Drilling into a dashboard comes in from the right, going back from the left; other pages rise into place.
            ScrollViewer from = new[] { GalleryPage, EditorPage, SettingsPage, AppPage }.FirstOrDefault(p => p.Visibility == Visibility.Visible);
            int direction = page == EditorPage && from == GalleryPage ? 1 : page == GalleryPage && from == EditorPage ? -1 : 0;
            GalleryPage.Visibility = page == GalleryPage ? Visibility.Visible : Visibility.Collapsed;
            EditorPage.Visibility = page == EditorPage ? Visibility.Visible : Visibility.Collapsed;
            SettingsPage.Visibility = page == SettingsPage ? Visibility.Visible : Visibility.Collapsed;
            AppPage.Visibility = page == AppPage ? Visibility.Visible : Visibility.Collapsed;
            bool dashboards = page == GalleryPage || page == EditorPage;
            if (dashboards) BuildDashboardSubNav();
            ShowSubNav(DashboardSubNav, dashboards);
            if (page != from) AnimatePageIn(page, direction);
            if (page == GalleryPage)
            {
                BuildGalleryCards();
                if (page != from) Motion.Stagger(DashboardCards.Children);
            }
            if (page == SettingsPage)
            {
                BuildDeviceList();
                if (page != from) Motion.Stagger(DevicesHost.Children, 45);
            }
            if (page == EditorPage) _ledTimer.Start(); else { _ledTimer.Stop(); ReleaseDevices(); }
            page.ScrollToTop();
        }

        private void ShowEditor(DashboardSettings dashboard)
        {
            _dashboard = dashboard;
            LoadDashboardValues();
            ShowPage(EditorPage);
        }
    }
}
