using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace AlmostFastDash.Designer
{
    public partial class DesignerWindow : Window
    {
        // ==================================================================
        // Screens
        // ==================================================================

        private bool _buildingScreens;

        private void BuildScreensCombo()
        {
            _buildingScreens = true;
            ScreenCombo.Items.Clear();
            foreach (DashScreen screen in _layout.Screens)
                ScreenCombo.Items.Add(new ComboBoxItem
                {
                    Content = screen.Name + (string.IsNullOrWhiteSpace(screen.Condition) ? "" : "  ·  when " + screen.Condition),
                    Tag = screen
                });
            ScreenCombo.SelectedIndex = Math.Max(0, _layout.Screens.IndexOf(_screen));
            _buildingScreens = false;
        }

        private void ScreenCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_buildingScreens || !(ScreenCombo.SelectedItem is ComboBoxItem item) || !(item.Tag is DashScreen screen)) return;
            _screen = screen;
            _selection.Clear();
            SelectionChanged();
            RequestRender();
        }

        private void ScreenMenu_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = PlacementMode.Bottom };
            menu.Items.Add(MenuItem("Add blank screen", null, () => AddScreen(false)));
            menu.Items.Add(MenuItem("Duplicate this screen", null, () => AddScreen(true)));
            menu.Items.Add(MenuItem("Add pit lane screen (shows on pit road)", null, () => AddScreen(true, "Pit lane", "OnPitRoad")));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItem("Edit name and show-when…", null, () => { _selection.Clear(); SelectionChanged(); InspectorScroller.ScrollToTop(); }));
            var delete = MenuItem("Delete this screen", null, DeleteScreen);
            delete.IsEnabled = _layout.Screens.Count > 1;
            menu.Items.Add(delete);
            menu.IsOpen = true;
        }

        private void AddScreen(bool copyCurrent, string name = null, string condition = null)
        {
            var screen = copyCurrent ? SettingsStore.Clone(_screen) : new DashScreen();
            screen.Id = DashScreen.NewId("screen");
            foreach (DashWidget w in screen.Widgets) w.Id = DashScreen.NewId("w");
            screen.Name = name ?? (copyCurrent ? _screen.Name + " copy" : "Screen " + (_layout.Screens.Count + 1));
            screen.Condition = condition ?? (copyCurrent ? "" : "");
            _layout.Screens.Add(screen);
            _screen = screen;
            _selection.Clear();
            Commit("Add screen");
            BuildScreensCombo();
            SelectionChanged();
            RequestRender();
            StatusText.Text = string.IsNullOrEmpty(condition)
                ? "Added a screen. Without a Show when condition, it's a page."
                : "Added a screen that shows while " + condition + ".";
        }

        private void DeleteScreen()
        {
            if (_layout.Screens.Count <= 1) return;
            _layout.Screens.Remove(_screen);
            _screen = _layout.Screens[0];
            _selection.Clear();
            Commit("Delete screen");
            BuildScreensCombo();
            SelectionChanged();
            RequestRender();
        }

        internal void ScreenEdited()
        {
            CommitSoon("Screen");
            int index = ScreenCombo.SelectedIndex;
            BuildScreensCombo();
            ScreenCombo.SelectedIndex = index;
            RequestRender();
        }
    }
}
