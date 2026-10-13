using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using DrawingColor = System.Drawing.Color;

namespace AlmostFastOverlays
{
    public partial class SettingsWindow : Window
    {
        private static readonly string[] AccentPresetColors =
            { "#2DD4BF", "#27C7D9", "#0A84FF", "#5E5CE6", "#BF5AF2", "#FF375F", "#FF9F0A", "#30D158" };

        private readonly AppSettings _settings;
        private DashboardSettings _dashboard;
        private bool _loading = true;
        private bool _dashboardOperationInProgress;
        private readonly DispatcherTimer _statusTimer;
        private readonly DispatcherTimer _saveTimer;
        private readonly DispatcherTimer _ledTimer;
        private readonly DispatcherTimer _confirmTimer;
        private bool _savePending;
        private readonly List<Border> _galleryCards = new List<Border>();
        private Button _armedButton;

        public SettingsWindow()
        {
            InitializeComponent();
            SourceInitialized += (s, e) => UseDarkTitleBar(); GlassBackdrop.Apply(this);
            _settings = SettingsStore.Load();
            _dashboard = _settings.GetDefaultDashboard();

            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            _saveTimer.Tick += (s, e) => FlushSave();
            _ledTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _ledTimer.Tick += (s, e) => UpdateLedPreview();
            _confirmTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            _confirmTimer.Tick += (s, e) => DisarmConfirm();

            PopulateChoices();
            BuildAccentPresets();
            HookEvents();
            LoadGeneralSettings();
            _loading = false;
            // The page chosen in Settings › Open on.
            switch (_settings.General.StartPage)
            {
                case "Devices": NavSettings.IsChecked = true; break;
                case "Settings": NavApp.IsChecked = true; break;
                default: NavGallery.IsChecked = true; break;
            }
            SetUpTray();

            MirrorLeds.Click += (s, e) => { if (MirrorLeds.IsChecked != true) ReleaseDevices(); UpdateLedPreview(); };
            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _statusTimer.Tick += (s, e) =>
            {
                if (!_dashboardOperationInProgress) RefreshDashboardStatus();
                if (SettingsPage.Visibility == Visibility.Visible) RefreshDeviceStatus();
            };
            _statusTimer.Start();
            RefreshDashboardStatus();

            Version version = Assembly.GetExecutingAssembly().GetName().Version;
            VersionText.Text = $"Version {version.Major}.{version.Minor} · Changes save automatically";
            SettingsPathText.Text = SettingsStore.FilePath;
            Closing += (s, e) => { FlushSave(); _ledTimer.Stop(); _statusTimer.Stop(); _mirror.Dispose(); PreviewRequest.Clear(); StopLivePreview(); };
        }

        // ------------------------------------------------------------------
        // Setup
        // ------------------------------------------------------------------

        private void PopulateChoices()
        {
            PreviewState.ItemsSource = PreviewRequest.States;
            PreviewState.SelectedIndex = 0;
        }

        private void HookEvents()
        {
            BlinkCountSlider.ValueChanged += (s, e) => OnAlertsEdited();
            BlinkLengthSlider.ValueChanged += (s, e) => OnAlertsEdited();
            HoldSlider.ValueChanged += (s, e) => OnAlertsEdited();
            ScreenBrightness.ValueChanged += (s, e) =>
            {
                ScreenBrightnessValue.Text = (int)ScreenBrightness.Value + "%";
                if (_loading) return;
                _settings.General.ScreenBrightness = (int)ScreenBrightness.Value;
                Save();
            };
        }

        private void BuildAccentPresets()
        {
            foreach (string hex in AccentPresetColors)
            {
                var swatch = new Button { Style = (Style)FindResource("Swatch"), Background = BrushFor(hex), ToolTip = hex };
                swatch.Click += (s, e) => SetAccent(hex);
                AccentPresets.Children.Add(swatch);
            }
            AccentCustom.Background = RainbowBrush();
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
                    ? new TextBlock { Text = "Default", FontSize = 10.5, Foreground = (Brush)FindResource("Brush.TextTertiary"), VerticalAlignment = VerticalAlignment.Center }
                    : null;
                var item = new RadioButton
                {
                    Style = (Style)FindResource("Nav.SubItem"), GroupName = "DashboardSub", Content = SubNavRow(d.Name, marker),
                    IsChecked = onEditor && d == _dashboard
                };
                item.Checked += (s, e) => { if (!_buildingDashboardNav && !(EditorPage.Visibility == Visibility.Visible && d == _dashboard)) ShowEditor(d); };
                DashboardSubNav.Children.Add(item);
            }
            _buildingDashboardNav = false;
        }
        private void NavApp_Checked(object sender, RoutedEventArgs e) => ShowPage(AppPage);

        private void ShowPage(ScrollViewer page)
        {
            DisarmConfirm();
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

        // ------------------------------------------------------------------
        // Gallery
        // ------------------------------------------------------------------

        private const double CardGap = 20, CardMinWidth = 340;

        private void BuildGalleryCards()
        {
            DashboardCards.Children.Clear();
            _galleryCards.Clear();
            var units = new UnitSystem(_settings.General);
            foreach (DashboardSettings dashboard in _settings.Dashboards)
            {
                DashboardSettings d = dashboard;
                bool isDefault = d.Id == _settings.DefaultDashboardId;
                var card = new Border
                {
                    Style = (Style)FindResource("Card"), Padding = new Thickness(0), Margin = new Thickness(0, 0, CardGap, CardGap),
                    Cursor = Cursors.Hand, Background = (Brush)FindResource("Brush.Surface")
                };
                card.MouseEnter += (s, e) => card.BorderBrush = (Brush)FindResource("Brush.BorderStrong");
                card.MouseLeave += (s, e) => card.BorderBrush = (Brush)FindResource("Brush.Border");
                card.MouseLeftButtonUp += (s, e) => { if (!(e.OriginalSource is DependencyObject o && FindParent<Button>(o) != null)) ShowEditor(d); };

                var stack = new StackPanel();
                var thumbnail = new Border
                {
                    CornerRadius = new CornerRadius(11, 11, 0, 0), Background = new ImageBrush(RenderPreview(d, units, null)) { Stretch = Stretch.UniformToFill },
                    BorderBrush = (Brush)FindResource("Brush.Border"), BorderThickness = new Thickness(0, 0, 0, 1)
                };
                stack.Children.Add(thumbnail);

                var body = new StackPanel { Margin = new Thickness(20, 16, 20, 10) };
                var title = new StackPanel { Orientation = Orientation.Horizontal };
                title.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = BrushFor(d.AccentColor), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
                title.Children.Add(new TextBlock { Text = d.Name, Style = (Style)FindResource("T.Title"), VerticalAlignment = VerticalAlignment.Center });
                if (isDefault) title.Children.Add(Pill("Default", "Brush.SuccessSubtle", "Brush.Success", new Thickness(10, 0, 0, 0)));
                body.Children.Add(title);
                body.Children.Add(new TextBlock { Text = Describe(d), TextWrapping = TextWrapping.Wrap, Style = (Style)FindResource("T.Caption"), Margin = new Thickness(0, 6, 0, 12) });

                var tags = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
                if (d.RaceTypes.Count == 0)
                    tags.Children.Add(Pill(isDefault ? "Used whenever no other match" : "Not assigned to a track type", "Brush.SurfaceRaised", "Brush.TextTertiary", new Thickness(0, 0, 6, 6)));
                foreach (string type in Disciplines.All.Where(d.RaceTypes.Contains))
                    tags.Children.Add(Pill(type, "Brush.SurfaceRaised", "Brush.TextSecondary", new Thickness(0, 0, 6, 6)));
                body.Children.Add(tags);

                // Wraps rather than clips when a card is narrow.
                var actions = new WrapPanel();
                var customize = new Button { Content = "Customize", Margin = new Thickness(0, 0, 8, 8) };
                customize.Click += (s, e) => ShowEditor(d);
                actions.Children.Add(customize);
                var design = new Button { Content = "Design", Style = (Style)FindResource("Btn.Primary"), Margin = new Thickness(0, 0, 8, 8) };
                design.Click += (s, e) => OpenDesigner(d);
                actions.Children.Add(design);
                if (!isDefault)
                {
                    var makeDefault = new Button { Content = "Set as default", Style = (Style)FindResource("Btn.Ghost"), Margin = new Thickness(0, 0, 0, 8) };
                    makeDefault.Click += (s, e) => MakeDefault(d);
                    actions.Children.Add(makeDefault);
                }
                body.Children.Add(actions);
                stack.Children.Add(body);
                card.Child = stack;
                card.Tag = thumbnail;
                _galleryCards.Add(card);
                Motion.Lift(card);
                DashboardCards.Children.Add(card);
            }
            LayoutCards(DashboardCards, _galleryCards);
        }

        private void DashboardCards_SizeChanged(object sender, SizeChangedEventArgs e) => LayoutCards(DashboardCards, _galleryCards);

        /// <summary>
        /// Lays out a grid of dashboard cards in as many equal columns as fit.
        /// A card whose Tag is its thumbnail keeps that thumbnail in the DV-5's shape.
        /// </summary>
        private static void LayoutCards(Panel panel, List<Border> cards)
        {
            double available = panel.ActualWidth;
            if (available <= 0) return;
            int columns = Math.Max(1, (int)((available + CardGap) / (CardMinWidth + CardGap)));
            // No empty columns: two dashboards share the row instead of leaving room for a third.
            columns = Math.Max(1, Math.Min(columns, Math.Max(cards.Count, 2)));
            double width = Math.Floor((available - CardGap * columns) / columns);
            foreach (Border card in cards)
            {
                card.Width = width;
                if (card.Tag is Border thumbnail) thumbnail.Height = Math.Round((width - 2) * Dv5Screen.Height / Dv5Screen.Width);
            }
        }

        private void NewDashboard_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu { PlacementTarget = NewDashboardButton, Placement = PlacementMode.Bottom };

            var designHeader = new StackPanel { Margin = new Thickness(0, 1, 0, 1) };
            designHeader.Children.Add(new TextBlock { Text = "New dashboard…", FontWeight = FontWeights.SemiBold });
            designHeader.Children.Add(new TextBlock { Text = "Start from Halo, Oval or a blank canvas in the designer", Style = (Style)FindResource("T.Caption"), MaxWidth = 260 });
            var designItem = new MenuItem { Header = designHeader, Icon = GlyphIcon("") };
            designItem.Click += (s, a) =>
            {
                DesignTemplate template = TemplatePicker.Choose(this, new UnitSystem(_settings.General));
                if (template == null) return;
                DashboardSettings created = _settings.AddDesignedDashboard(template);
                Save();
                ShowEditor(created);
                OpenDesigner(created);
            };
            menu.Items.Add(designItem);
            var importItem = new MenuItem { Header = "Import a design (.afodash)…", Icon = GlyphIcon("") };
            importItem.Click += (s, a) => ImportDesign();
            menu.Items.Add(importItem);
            menu.IsOpen = true;
        }

        /// <summary>One line under a dashboard's name: the template's description, or what's in a design of the user's own.</summary>
        private static string Describe(DashboardSettings d)
        {
            DesignTemplate template = DesignTemplates.All.FirstOrDefault(t => t.Id != "blank" && string.Equals(t.Name, d.Name, StringComparison.OrdinalIgnoreCase));
            if (template != null) return template.Description;
            int widgets = d.Design?.AllWidgets().Count() ?? 0, screens = d.Design?.Screens.Count ?? 1;
            return "Your own design: " + widgets + " widgets on " + screens + (screens == 1 ? " screen" : " screens") + ".";
        }

        // ------------------------------------------------------------------
        // Designer
        // ------------------------------------------------------------------

        private readonly Dictionary<string, DesignerWindow> _designers = new Dictionary<string, DesignerWindow>();

        private void OpenDesigner(DashboardSettings dashboard)
        {
            FlushSave();
            if (_designers.TryGetValue(dashboard.Id, out DesignerWindow open))
            {
                if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
                open.Activate();
                return;
            }
            var designer = new DesignerWindow(_settings, dashboard, () =>
            {
                if (GalleryPage.Visibility == Visibility.Visible) BuildGalleryCards();
                else if (EditorPage.Visibility == Visibility.Visible && _dashboard == dashboard) LoadDashboardValues();
                ShowSaved("Design saved");
            }) { Owner = this };
            _designers[dashboard.Id] = designer;
            designer.Closed += (s, e) => _designers.Remove(dashboard.Id);
            designer.Show();
        }

        private void Design_Click(object sender, RoutedEventArgs e) => OpenDesigner(_dashboard);

        private void ImportDesign()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Import dashboard design", Filter = "Almost Fast Dash dashboard (*.afodash)|*.afodash|All files|*.*" };
            if (dialog.ShowDialog(this) != true) return;
            DesignPackage package;
            try { package = DesignPackage.Read(dialog.FileName); }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "Import design", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DashboardSettings created = _settings.AddDesignedDashboard(DesignTemplates.Find("blank"), package.Name);
            created.Design = package.Design;
            if (!string.IsNullOrEmpty(package.AccentColor)) created.AccentColor = package.AccentColor;
            if (package.Lights != null)
            {
                created.Lights = package.Lights;
                created.Lights.Normalize();
            }
            Save("Imported " + created.Name);
            ShowEditor(created);
        }

        /// <summary>Saves a dashboard (its screens, images, accent and LED lights) to one file to share or back up.</summary>
        private void ExportDashboard(DashboardSettings dashboard)
        {
            FlushSave();
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export dashboard",
                Filter = "Almost Fast Dash dashboard (*.afodash)|*.afodash",
                FileName = DesignerWindow.SafeFileName(dashboard.Name) + ".afodash"
            };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                DesignPackage.Write(dialog.FileName, DesignPackage.From(dashboard));
                ShowSaved("Exported " + dashboard.Name);
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "Export dashboard", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            while (child != null && !(child is T)) child = VisualTreeHelper.GetParent(child);
            return child as T;
        }

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
                ". Open the designer to move, restyle or add anything on the wheel screen.";
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
            ManageHelp.Text = "Duplicate this dashboard to experiment, reset its lights and alerts, or remove it.";
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
            AlertSummary.Text = $"A green or yellow flag stays on screen for about {total:0.#} seconds, then the dashboard returns.";
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
                AssignmentHelp.Text = "Automatic switching is off in Device & app, so the default dashboard is always used.";
                return;
            }
            var shared = _dashboard.RaceTypes
                .Select(type => new { type, others = _settings.Dashboards.Where(d => d != _dashboard && d.RaceTypes.Contains(type)).Select(d => d.Name).ToList() })
                .Where(x => x.others.Count > 0).ToList();
            AssignmentHelp.Text = shared.Count == 0
                ? "Switches to this dashboard when a session starts on these track types."
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
            else LoadDashboardValues();
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

        // ------------------------------------------------------------------
        // Preview
        // ------------------------------------------------------------------

        private void PreviewState_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshPreview();

        private void RefreshPreview()
        {
            if (_dashboard == null || PreviewState == null) return;
            string state = PreviewStateName;
            if (state == PreviewRequest.LiveIRacing) StartLivePreview(); else StopLivePreview();
            // The screen shows the same telemetry as the lights. A lap in motion is shown as a still of racing.
            TelemetrySnapshot snapshot = state == PreviewRequest.AnimatedLap ? LightsWindow.Scenario("Racing")
                : PreviewRequest.ForState(state, _previewSynthetic, _livePreview?.GetSnapshot());
            PreviewBrush.ImageSource = RenderPreview(_dashboard, new UnitSystem(_settings.General), snapshot, snapshot.ShiftAlert);
            UpdateLedPreview();
        }

        private string PreviewStateName => PreviewState.SelectedItem as string ?? PreviewRequest.AnimatedLap;

        private IRacingTelemetrySource _livePreview;
        private DiagnosticLogger _livePreviewLog;

        private void StartLivePreview()
        {
            if (_livePreview != null) return;
            _livePreviewLog = new DiagnosticLogger(false);
            _livePreview = new IRacingTelemetrySource(_livePreviewLog, _dashboard);
            _livePreview.Start();
        }

        private void StopLivePreview()
        {
            _livePreview?.Dispose();
            _livePreview = null;
            _livePreviewLog?.Dispose();
            _livePreviewLog = null;
        }

        private static BitmapSource RenderPreview(DashboardSettings dashboard, UnitSystem units, TelemetrySnapshot snapshot, bool shiftFlash = false)
        {
            snapshot = snapshot ?? IRacingTelemetrySource.CreatePreviewSnapshot();
            using (IDashboardRenderer renderer = DashboardRendererFactory.Create(Dv5Screen.Width, Dv5Screen.Height, dashboard, units))
            using (var frame = renderer.Render(snapshot, shiftFlash, snapshot.Gear, 1f))
            using (var stream = new MemoryStream())
            {
                frame.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                stream.Position = 0;
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                return image;
            }
        }

        private void UpdateLedPreview() => UpdateLayeredPreview();

        // ------------------------------------------------------------------
        // Device & app settings
        // ------------------------------------------------------------------

        private void LoadGeneralSettings()
        {
            GeneralSettings general = _settings.General;
            ScreenBrightness.Value = general.ScreenBrightness;
            ScreenBrightnessValue.Text = general.ScreenBrightness + "%";
            SpeedKph.IsChecked = string.Equals(general.SpeedUnit, "KPH", StringComparison.OrdinalIgnoreCase);
            SpeedMph.IsChecked = SpeedKph.IsChecked != true;
            FuelGallons.IsChecked = string.Equals(general.FuelUnit, "Gallons", StringComparison.OrdinalIgnoreCase);
            FuelLiters.IsChecked = FuelGallons.IsChecked != true;
            TempC.IsChecked = new UnitSystem(general).Celsius;
            TempF.IsChecked = TempC.IsChecked != true;
            OpenDevices.IsChecked = general.StartPage == "Devices";
            OpenSettings.IsChecked = general.StartPage == "Settings";
            OpenDashboards.IsChecked = OpenDevices.IsChecked != true && OpenSettings.IsChecked != true;
            TrayToggle.IsChecked = general.MinimizeToTray;
            AutoSwitchToggle.IsChecked = general.AutoSwitchByDiscipline;
            DiagnosticsToggle.IsChecked = general.DiagnosticsEnabled;
            MoveOldStartupShortcut();
            StartupToggle.IsChecked = File.Exists(StartupShortcutPath);
        }

        private void Units_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _settings.General.SpeedUnit = SpeedKph.IsChecked == true ? "KPH" : "MPH";
            _settings.General.FuelUnit = FuelGallons.IsChecked == true ? "Gallons" : "Liters";
            if (TempC.IsChecked == true || TempF.IsChecked == true) _settings.General.TemperatureUnit = TempC.IsChecked == true ? "Celsius" : "Fahrenheit";
            Save();
        }

        private void StartPage_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _settings.General.StartPage = OpenDevices.IsChecked == true ? "Devices" : OpenSettings.IsChecked == true ? "Settings" : "Dashboards";
            Save();
        }

        private void Tray_Click(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _settings.General.MinimizeToTray = TrayToggle.IsChecked == true;
            Save();
        }

        private void AutoSwitch_Click(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _settings.General.AutoSwitchByDiscipline = AutoSwitchToggle.IsChecked == true;
            Save();
        }

        private void Diagnostics_Click(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _settings.General.DiagnosticsEnabled = DiagnosticsToggle.IsChecked == true;
            Save(IsDashboardRunning() ? "Saved · applies next time the dashboard starts" : "Saved");
        }

        private static string StartupShortcutPath =>
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Almost Fast Dash.lnk");

        /// <summary>A start-with-Windows shortcut made before the rename points at the old exe; replace it with one for this one.</summary>
        private static void MoveOldStartupShortcut()
        {
            string old = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Almost Fast Dash.lnk");
            try
            {
                if (!File.Exists(old)) return;
                CreateStartupShortcut();
                File.Delete(old);
            }
            catch { }
        }

        private void Startup_Click(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            try
            {
                if (StartupToggle.IsChecked == true) CreateStartupShortcut();
                else if (File.Exists(StartupShortcutPath)) File.Delete(StartupShortcutPath);
                ShowSaved(StartupToggle.IsChecked == true ? "Will start with Windows" : "Won't start with Windows");
            }
            catch (Exception ex)
            {
                _loading = true;
                StartupToggle.IsChecked = File.Exists(StartupShortcutPath);
                _loading = false;
                ShowSaved("Couldn't update startup: " + ex.Message);
            }
        }

        private static void CreateStartupShortcut()
        {
            // WScript.Shell is the supported way to write .lnk files from .NET Framework.
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(shellType);
            try
            {
                object shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { StartupShortcutPath });
                Type t = shortcut.GetType();
                string exe = Assembly.GetEntryAssembly().Location;
                t.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { exe });
                t.InvokeMember("Arguments", BindingFlags.SetProperty, null, shortcut, new object[] { "--run" });
                t.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { System.IO.Path.GetDirectoryName(exe) });
                t.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { "Start the Almost Fast Dash iRacing dashboard automatically" });
                t.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
                Marshal.FinalReleaseComObject(shortcut);
            }
            finally
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }

        private void OpenLogs_Click(object sender, RoutedEventArgs e)
        {
            string logs = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            Directory.CreateDirectory(logs);
            Process.Start("explorer.exe", "\"" + logs + "\"");
        }

        private void OpenSettingsFolder_Click(object sender, RoutedEventArgs e)
        {
            if (File.Exists(SettingsStore.FilePath)) Process.Start("explorer.exe", "/select,\"" + SettingsStore.FilePath + "\"");
            else Process.Start("explorer.exe", "\"" + AppDomain.CurrentDomain.BaseDirectory + "\"");
        }

        // ------------------------------------------------------------------
        // Saving
        // ------------------------------------------------------------------

        private string _pendingSaveMessage;

        private void Changed()
        {
            RefreshPreview();
            Save();
        }

        /// <summary>Debounces writes so dragging a slider doesn't rewrite the file every tick.</summary>
        private void Save(string message = null)
        {
            if (_loading) return;
            _savePending = true;
            _pendingSaveMessage = message;
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private void FlushSave()
        {
            _saveTimer.Stop();
            if (!_savePending) return;
            _savePending = false;
            try
            {
                SettingsStore.Save(_settings);
                ShowSaved(_pendingSaveMessage ?? (IsDashboardRunning() ? "Saved · applied to the running dashboard" : "Saved"));
                // Names and the default show in the sidebar too.
                if (DashboardSubNav.Visibility == Visibility.Visible) BuildDashboardSubNav();
            }
            catch (Exception ex)
            {
                ShowSaved("Couldn't save: " + ex.Message);
            }
        }

        private void ShowSaved(string text)
        {
            SavedText.Text = text;
            var fade = new DoubleAnimationUsingKeyFrames();
            fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.4))));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3))));
            SavedIndicator.BeginAnimation(OpacityProperty, fade);
        }

        // ------------------------------------------------------------------
        // Dashboard process control
        // ------------------------------------------------------------------

        private async void StartDashboard_Click(object sender, RoutedEventArgs e) => await StartDashboardAsync();

        /// <summary>Starts the dashboard (restarting it if it's running) and updates the header.</summary>
        private async Task StartDashboardAsync()
        {
            FlushSave();
            _dashboardOperationInProgress = true;
            StartButton.IsEnabled = false;
            bool wasRunning = IsDashboardRunning();
            if (wasRunning)
            {
                SetStatus("Stopping…", "Brush.Warning");
                Launch("--stop");
                await WaitForDashboardState(false, 10000);
            }
            SetStatus(wasRunning ? "Restarting…" : "Starting…", "Brush.Warning");
            ReleaseDevices();
            Launch("--run");
            bool running = await WaitForDashboardState(true, 10000);
            _dashboardOperationInProgress = false;
            StartButton.IsEnabled = true;
            if (running) RefreshDashboardStatus(); else SetStopped("Couldn't start");
        }

        private async void StopDashboard_Click(object sender, RoutedEventArgs e) => await StopDashboardAsync();

        /// <summary>Stops the dashboard and updates the header; true once it has stopped.</summary>
        private async Task<bool> StopDashboardAsync()
        {
            _dashboardOperationInProgress = true;
            SetStatus("Stopping…", "Brush.Warning");
            Launch("--stop");
            bool stopped = await WaitForDashboardState(false, 10000);
            _dashboardOperationInProgress = false;
            if (stopped) RefreshDashboardStatus(); else SetStatus("Didn't stop", "Brush.Danger");
            return stopped;
        }

        private static Process Launch(string arguments) => Process.Start(new ProcessStartInfo
        {
            FileName = Assembly.GetEntryAssembly().Location,
            Arguments = arguments,
            WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
            UseShellExecute = true
        });

        private async Task<bool> WaitForDashboardState(bool expectedRunning, int timeoutMilliseconds)
        {
            int waited = 0;
            while (waited < timeoutMilliseconds)
            {
                if (IsDashboardRunning() == expectedRunning) return true;
                await Task.Delay(200);
                waited += 200;
            }
            return IsDashboardRunning() == expectedRunning;
        }

        private static bool IsDashboardRunning()
        {
            try
            {
                if (!File.Exists(Program.DashboardStatusPath)) return false;
                if (!int.TryParse(File.ReadAllText(Program.DashboardStatusPath).Trim(), out int pid)) return false;
                Process process = Process.GetProcessById(pid);
                // The exe was called AlmostFastOverlays before the rename; either is ours.
                return !process.HasExited && (string.Equals(process.ProcessName, "AlmostFastDash", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(process.ProcessName, "AlmostFastOverlays", StringComparison.OrdinalIgnoreCase));
            }
            catch { return false; }
        }

        private void RefreshDashboardStatus()
        {
            _dashboardRunning = IsDashboardRunning();
            if (!_dashboardRunning)
            {
                SetStopped("Stopped");
                return;
            }
            SetStatus("Running", "Brush.Success");
            SetRunButton(StartButton, StartIcon, false, "Restart the dashboard");
            StopButton.IsEnabled = true;
            string active = null, discipline = null;
            try
            {
                string[] lines = File.ReadAllLines(Program.ActiveDashboardPath);
                active = lines.Length > 0 ? lines[0].Trim() : null;
                discipline = lines.Length > 1 ? lines[1].Trim() : null;
            }
            catch { }
            DashboardSettings shown = _settings.Dashboards.FirstOrDefault(d => d.Id == active);
            ActiveText.Text = shown == null ? "" : " · " + shown.Name;
            StatusPill.ToolTip = shown == null ? null : "Showing " + shown.Name + (string.IsNullOrEmpty(discipline) ? "" : " for a " + discipline.ToLowerInvariant() + " track");
        }

        private void SetStopped(string text)
        {
            SetStatus(text, "Brush.TextTertiary");
            SetRunButton(StartButton, StartIcon, true, "Start the dashboard on your wheel");
            StopButton.IsEnabled = false;
            ActiveText.Text = " · " + _settings.GetDefaultDashboard().Name;
            StatusPill.ToolTip = "Starts with " + _settings.GetDefaultDashboard().Name + ", the default dashboard";
        }

        private void SetStatus(string text, string brushKey)
        {
            StatusText.Text = text;
            StatusDot.Fill = (Brush)FindResource(brushKey);
        }

        /// <summary>
        /// The run controls in the header share one look: a filled play button when
        /// something is stopped, and a quiet secondary button (restart or stop) while it runs.
        /// </summary>
        private void SetRunButton(Button button, TextBlock icon, bool start, string tip, string runningGlyph = "\uE72C")
        {
            button.Style = (Style)FindResource(start ? "Btn.RunPrimary" : "Btn.RunIcon");
            icon.Text = start ? "\uE768" : runningGlyph;
            icon.Foreground = start ? (Brush)FindResource("Brush.OnAccent") : (Brush)FindResource("Brush.Text");
            button.ToolTip = tip;
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private Border Pill(string text, string backgroundKey, string foregroundKey, Thickness margin) => new Border
        {
            Style = (Style)FindResource("Pill"),
            Background = (Brush)FindResource(backgroundKey),
            Margin = margin,
            Child = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource(foregroundKey) }
        };

        private TextBlock GlyphIcon(string glyph) => new TextBlock { Text = glyph, Style = (Style)FindResource("T.Icon"), FontSize = 13 };

        private static SolidColorBrush BrushFor(string hex) =>
            BrushFor(SettingsStore.ParseColor(hex, DrawingColor.FromArgb(40, 50, 65)));

        private static SolidColorBrush BrushFor(DrawingColor color)
        {
            var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
            brush.Freeze();
            return brush;
        }

        private static bool SameJson(object a, object b) =>
            SettingsStore.ToJson(a) == SettingsStore.ToJson(b);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private void UseDarkTitleBar()
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                int dark = 1;
                DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
                int caption = 0x00130E0B; // COLORREF (BGR) of Brush.Bg #0B0E13
                DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int));
            }
            catch { }
        }
    }
}
