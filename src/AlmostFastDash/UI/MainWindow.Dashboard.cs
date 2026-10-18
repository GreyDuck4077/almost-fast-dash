using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using DrawingColor = System.Drawing.Color;

namespace AlmostFastDash.UI
{
    public partial class MainWindow : Window
    {
        // ------------------------------------------------------------------
        // Editor
        // ------------------------------------------------------------------

        private void LoadDashboardValues()
        {
            _loading = true;
            bool isDefault = _dashboard.Id == _settings.DefaultDashboardId;
            bool last = _settings.Dashboards.Count <= 1;
            EditorTitle.Text = _dashboard.Name;
            EditorSubtitle.Text = Describe(_dashboard);
            int widgets = _dashboard.Design?.AllWidgets().Count() ?? 0, screens = _dashboard.Design?.Screens.Count ?? 1;
            DesignCardTitle.Text = "Screen design";
            DesignCardText.Text = widgets + " widgets on " + screens + (screens == 1 ? " screen" : " screens") +
                ".";
            DesignButtonLabel.Text = "Open designer";
            EditorDefaultBadge.Visibility = isDefault ? Visibility.Visible : Visibility.Collapsed;
            SetDefaultButton.Visibility = isDefault ? Visibility.Collapsed : Visibility.Visible;
            NameBox.Text = _dashboard.Name;

            OvalChip.IsChecked = _dashboard.RaceTypes.Contains(Disciplines.Oval);
            RoadChip.IsChecked = _dashboard.RaceTypes.Contains(Disciplines.Road);
            DirtOvalChip.IsChecked = _dashboard.RaceTypes.Contains(Disciplines.DirtOval);
            DirtRoadChip.IsChecked = _dashboard.RaceTypes.Contains(Disciplines.DirtRoad);
            UpdateAssignmentHelp();

            BlinkCountSlider.Value = _dashboard.Alerts.BlinkCount;
            BlinkLengthSlider.Value = _dashboard.Alerts.BlinkMilliseconds;
            HoldSlider.Value = _dashboard.Alerts.HoldMilliseconds;
            UpdateAlertLabels();

            DeleteButton.IsEnabled = !last;
            DeleteButton.ToolTip = last ? "Your only dashboard can't be deleted." : null;
            ManageHelp.Text = "Duplicate, reset or delete this dashboard.";
            _loading = false;
            UpdateLightsCard();
            RefreshAccent();
            RefreshPreview();
        }

        private void OnAlertsEdited()
        {
            UpdateAlertLabels();
            if (_loading) return;
            _dashboard.Alerts.BlinkCount = (int)BlinkCountSlider.Value;
            _dashboard.Alerts.BlinkMilliseconds = (int)BlinkLengthSlider.Value;
            _dashboard.Alerts.HoldMilliseconds = (int)HoldSlider.Value;
            Save();
        }

        private void UpdateAlertLabels()
        {
            int count = (int)BlinkCountSlider.Value, length = (int)BlinkLengthSlider.Value, hold = (int)HoldSlider.Value;
            BlinkCountValue.Text = count == 1 ? "1 flash" : count + " flashes";
            BlinkLengthValue.Text = length + " ms";
            HoldValue.Text = hold == 0 ? "Off" : (hold / 1000.0).ToString("0.##") + " s";
            double total = (count * length * 2 + hold) / 1000.0;
            AlertSummary.Text = $"A green, yellow or white flag fills the screen for about {total:0.#} seconds. A caution or final lap keeps a frame around it.";
        }

        private void AssignmentChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _dashboard.RaceTypes = new List<string>();
            if (OvalChip.IsChecked == true) _dashboard.RaceTypes.Add(Disciplines.Oval);
            if (RoadChip.IsChecked == true) _dashboard.RaceTypes.Add(Disciplines.Road);
            if (DirtOvalChip.IsChecked == true) _dashboard.RaceTypes.Add(Disciplines.DirtOval);
            if (DirtRoadChip.IsChecked == true) _dashboard.RaceTypes.Add(Disciplines.DirtRoad);
            UpdateAssignmentHelp();
            Save();
        }

        private void UpdateAssignmentHelp()
        {
            if (!_settings.General.AutoSwitchByDiscipline)
            {
                AssignmentHelp.Text = "Switching by track type is off in Settings, so the default is always used.";
                return;
            }
            var shared = _dashboard.RaceTypes
                .Select(type => new { type, others = _settings.Dashboards.Where(d => d != _dashboard && d.RaceTypes.Contains(type)).Select(d => d.Name).ToList() })
                .Where(x => x.others.Count > 0).ToList();
            AssignmentHelp.Text = shared.Count == 0
                ? "Used when a session starts on these track types."
                : $"{shared[0].type} is also assigned to {string.Join(", ", shared[0].others)}. The default dashboard wins, then the one listed first.";
        }

        private void NameBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { CommitName(); Keyboard.ClearFocus(); }
            else if (e.Key == Key.Escape) { NameBox.Text = _dashboard.Name; Keyboard.ClearFocus(); }
        }

        private void NameBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitName();

        private void CommitName()
        {
            string name = NameBox.Text.Trim();
            if (name.Length == 0) { NameBox.Text = _dashboard.Name; return; }
            if (name == _dashboard.Name) return;
            _dashboard.Name = name;
            EditorTitle.Text = name;
            Save();
        }

        private void SetAccent(string hex)
        {
            _dashboard.AccentColor = hex;
            RefreshAccent();
            Changed();
        }

        private void AccentCustom_Click(object sender, RoutedEventArgs e)
        {
            if (WindowsColorPicker.TryChoose(_dashboard.AccentColor, out string chosen)) SetAccent(chosen);
        }

        private void RefreshAccent()
        {
            AccentHex.Text = _dashboard.AccentColor.ToUpperInvariant();
            bool matched = false;
            foreach (Button swatch in AccentPresets.Children.OfType<Button>())
            {
                bool selected = string.Equals((string)swatch.ToolTip, _dashboard.AccentColor, StringComparison.OrdinalIgnoreCase);
                swatch.Tag = selected ? "Selected" : null;
                matched |= selected;
            }
            AccentCustom.Tag = matched ? null : "Selected";
            AccentCustom.Background = matched ? RainbowBrush() : BrushFor(_dashboard.AccentColor);
        }

        private static Brush RainbowBrush() => new LinearGradientBrush(
            new GradientStopCollection
            {
                new GradientStop(Color.FromRgb(255, 80, 80), 0),
                new GradientStop(Color.FromRgb(255, 210, 60), 0.35),
                new GradientStop(Color.FromRgb(60, 210, 190), 0.65),
                new GradientStop(Color.FromRgb(120, 90, 255), 1)
            }, new Point(0, 0), new Point(1, 1));

        private void SetDefault_Click(object sender, RoutedEventArgs e) => MakeDefault(_dashboard);

        private void MakeDefault(DashboardSettings dashboard)
        {
            _settings.DefaultDashboardId = dashboard.Id;
            Save();
            if (GalleryPage.Visibility == Visibility.Visible) BuildGalleryCards();
            else if (EditorPage.Visibility == Visibility.Visible) LoadDashboardValues();
            if (DashboardSubNav.Visibility == Visibility.Visible) BuildDashboardSubNav();
        }

        /// <summary>A dashboard's right-click menu in the sidebar.</summary>
        private ContextMenu DashboardMenu(DashboardSettings dashboard)
        {
            var menu = new ContextMenu();
            bool isDefault = dashboard.Id == _settings.DefaultDashboardId;
            var makeDefault = new MenuItem
            {
                Header = isDefault ? "The default dashboard" : "Set as default", Icon = GlyphIcon(isDefault ? "" : ""), IsEnabled = !isDefault,
                ToolTip = "Shown unless another dashboard is set for the session's track type"
            };
            makeDefault.Click += (s, e) => MakeDefault(dashboard);
            var open = new MenuItem { Header = "Open", Icon = GlyphIcon("") };
            open.Click += (s, e) => ShowEditor(dashboard);
            var design = new MenuItem { Header = dashboard.Design != null ? "Edit the screen design" : "Design the screen", Icon = GlyphIcon("") };
            design.Click += (s, e) => OpenDesigner(dashboard);
            var duplicate = new MenuItem { Header = "Duplicate", Icon = GlyphIcon("") };
            duplicate.Click += (s, e) =>
            {
                DashboardSettings copy = _settings.AddDashboard(dashboard);
                Save();
                ShowEditor(copy);
            };
            menu.Items.Add(makeDefault);
            menu.Items.Add(new Separator());
            menu.Items.Add(open);
            menu.Items.Add(design);
            menu.Items.Add(duplicate);
            return menu;
        }

        private void More_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu { PlacementTarget = MoreButton, Placement = PlacementMode.Bottom, HorizontalOffset = -186 };
            var duplicate = new MenuItem { Header = "Duplicate", Icon = GlyphIcon("") };
            duplicate.Click += Duplicate_Click;
            var reset = new MenuItem { Header = "Reset to defaults", Icon = GlyphIcon("") };
            reset.Click += (s, a) => { ResetButton.BringIntoView(); ArmConfirm(ResetButton, ResetLabel, "Click again to reset"); };
            var delete = new MenuItem { Header = "Delete", Icon = GlyphIcon(""), IsEnabled = _settings.Dashboards.Count > 1 };
            delete.Click += (s, a) => { DeleteButton.BringIntoView(); ArmConfirm(DeleteButton, DeleteLabel, "Click again to delete"); };
            var export = new MenuItem { Header = "Export to a file (.afodash)…", Icon = GlyphIcon(""), IsEnabled = _dashboard.Design != null };
            export.Click += (s, a) => ExportDashboard(_dashboard);
            menu.Items.Add(duplicate);
            menu.Items.Add(export);
            menu.Items.Add(reset);
            menu.Items.Add(delete);
            menu.IsOpen = true;
        }

        private void Duplicate_Click(object sender, RoutedEventArgs e)
        {
            DashboardSettings copy = _settings.AddDashboard(_dashboard);
            Save();
            ShowEditor(copy);
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            if (_armedButton != ResetButton) { ArmConfirm(ResetButton, ResetLabel, "Click again to reset"); return; }
            DisarmConfirm();
            _settings.ResetDashboard(_dashboard);
            Save("Reset to defaults");
            LoadDashboardValues();
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (_armedButton != DeleteButton) { ArmConfirm(DeleteButton, DeleteLabel, "Click again to delete"); return; }
            DisarmConfirm();
            string name = _dashboard.Name;
            if (!_settings.RemoveDashboard(_dashboard)) return;
            _dashboard = _settings.GetDefaultDashboard();
            Save("Deleted " + name);
            ShowGallery();
        }

        private void ArmConfirm(Button button, TextBlock label, string text)
        {
            DisarmConfirm();
            _armedButton = button;
            label.Tag = label.Text;
            label.Text = text;
            _confirmTimer.Start();
        }

        private void DisarmConfirm()
        {
            _confirmTimer?.Stop();
            _armedButton = null;
            foreach (TextBlock label in new[] { ResetLabel, DeleteLabel })
                if (label.Tag is string original) { label.Text = original; label.Tag = null; }
        }
    }
}
