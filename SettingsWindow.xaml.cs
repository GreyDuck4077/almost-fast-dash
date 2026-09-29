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

namespace DV5Direct
{
    public partial class SettingsWindow : Window
    {
        private static readonly string[] AccentPresetColors =
            { "#2DD4BF", "#27C7D9", "#0A84FF", "#5E5CE6", "#BF5AF2", "#FF375F", "#FF9F0A", "#30D158" };

        private static readonly string[] PreviewStates =
            { "Racing", "Shift point", "Green flag", "Yellow flag", "White flag", "Blue flag", "Black flag", "Checkered flag" };

        private readonly AppSettings _settings;
        private DashboardSettings _dashboard;
        private bool _loading = true;
        private bool _dashboardOperationInProgress;
        private readonly DispatcherTimer _statusTimer;
        private readonly DispatcherTimer _saveTimer;
        private readonly DispatcherTimer _ledTimer;
        private readonly DispatcherTimer _confirmTimer;
        private bool _savePending;
        private double _ledPhase;
        private readonly Ellipse[] _ledDots = new Ellipse[LedFrame.Count];
        private readonly DropShadowEffect[] _ledGlows = new DropShadowEffect[LedFrame.Count];
        private readonly List<Border> _galleryCards = new List<Border>();
        private BankEditor _center, _left, _right;
        private Button _armedButton;

        public SettingsWindow()
        {
            InitializeComponent();
            SourceInitialized += (s, e) => UseDarkTitleBar();
            _settings = SettingsStore.Load();
            _dashboard = _settings.GetDefaultDashboard();

            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            _saveTimer.Tick += (s, e) => FlushSave();
            _ledTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _ledTimer.Tick += (s, e) => { if (AnimateLeds.IsChecked == true) _ledPhase += 0.05; UpdateLedPreview(); };
            _confirmTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            _confirmTimer.Tick += (s, e) => DisarmConfirm();

            BuildBankEditors();
            BuildLedRig();
            PopulateChoices();
            BuildAccentPresets();
            HookEvents();
            LoadGeneralSettings();
            BuildNav();
            _loading = false;
            NavGallery.IsChecked = true;

            AnimateLeds.Click += (s, e) => UpdateLedPreview();
            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _statusTimer.Tick += (s, e) => { if (!_dashboardOperationInProgress) RefreshDashboardStatus(); };
            _statusTimer.Start();
            RefreshDashboardStatus();

            Version version = Assembly.GetExecutingAssembly().GetName().Version;
            VersionText.Text = $"Version {version.Major}.{version.Minor} · Changes save automatically";
            SettingsPathText.Text = SettingsStore.FilePath;
            Closing += (s, e) => { FlushSave(); _ledTimer.Stop(); _statusTimer.Stop(); };
        }

        // ------------------------------------------------------------------
        // Setup
        // ------------------------------------------------------------------

        private sealed class BankEditor
        {
            public ComboBox Function, Pattern, Profile;
            public Button Low, Mid, High;
            public Slider Brightness;
            public TextBlock BrightnessValue, FunctionHelp;
            public Func<LedBankSettings> Bank;
            public Action<LedBankSettings> Replace;
        }

        private void BuildBankEditors()
        {
            _center = new BankEditor
            {
                Function = CenterFunction, Pattern = CenterPattern, Profile = CenterProfile,
                Low = CenterLow, Mid = CenterMid, High = CenterHigh,
                Brightness = CenterBrightness, BrightnessValue = CenterBrightnessValue,
                Bank = () => _dashboard.CenterLeds
            };
            _left = new BankEditor
            {
                Function = LeftFunction, Profile = LeftProfile, Low = LeftLow, Mid = LeftMid, High = LeftHigh,
                Brightness = LeftBrightness, BrightnessValue = LeftBrightnessValue, FunctionHelp = LeftFunctionHelp,
                Bank = () => _dashboard.LeftLeds,
                Replace = bank => _dashboard.LeftLeds = SettingsStore.Clone((SideLedSettings)bank)
            };
            _right = new BankEditor
            {
                Function = RightFunction, Profile = RightProfile, Low = RightLow, Mid = RightMid, High = RightHigh,
                Brightness = RightBrightness, BrightnessValue = RightBrightnessValue, FunctionHelp = RightFunctionHelp,
                Bank = () => _dashboard.RightLeds,
                Replace = bank => _dashboard.RightLeds = SettingsStore.Clone((SideLedSettings)bank)
            };
        }

        private void PopulateChoices()
        {
            CenterFunction.ItemsSource = new[] { "RPM and shift point", "Nothing (lights off)" };
            CenterPattern.ItemsSource = LedPatterns.Center;
            foreach (BankEditor editor in Editors()) editor.Profile.ItemsSource = LedProfiles.All;
            LeftFunction.ItemsSource = LedFunctions.Side;
            RightFunction.ItemsSource = LedFunctions.Side;
            PreviewState.ItemsSource = PreviewStates;
            PreviewState.SelectedIndex = 0;
        }

        private IEnumerable<BankEditor> Editors() => new[] { _center, _left, _right };

        private void HookEvents()
        {
            foreach (BankEditor editor in Editors())
            {
                BankEditor e = editor;
                e.Function.SelectionChanged += (s, a) => OnBankEdited(e, bank =>
                {
                    if (e == _center) bank.Function = e.Function.SelectedIndex == 1 ? LedFunctions.Disabled : LedFunctions.Rpm;
                    else bank.Function = (string)e.Function.SelectedItem ?? bank.Function;
                });
                if (e.Pattern != null)
                    e.Pattern.SelectionChanged += (s, a) => OnBankEdited(e, bank => bank.Pattern = (string)e.Pattern.SelectedItem ?? bank.Pattern);
                e.Profile.SelectionChanged += (s, a) => OnBankEdited(e, bank => ApplyProfile(bank, (string)e.Profile.SelectedItem));
                e.Brightness.ValueChanged += (s, a) =>
                {
                    e.BrightnessValue.Text = (int)e.Brightness.Value + "%";
                    OnBankEdited(e, bank => bank.Brightness = (int)e.Brightness.Value);
                };
                e.Low.Click += (s, a) => PickStageColor(e, 0);
                e.Mid.Click += (s, a) => PickStageColor(e, 1);
                e.High.Click += (s, a) => PickStageColor(e, 2);
            }

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

        private void BuildLedRig()
        {
            for (int i = 0; i < LedFrame.Count; i++)
            {
                _ledGlows[i] = new DropShadowEffect { ShadowDepth = 0, BlurRadius = 14, Opacity = 0 };
                _ledDots[i] = new Ellipse
                {
                    Width = 12, Height = 12,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    Fill = (Brush)FindResource("Brush.LedOff"),
                    Stroke = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), StrokeThickness = 1,
                    Effect = _ledGlows[i]
                };
            }
            for (int i = 4; i < 12; i++) TopLeds.Children.Add(_ledDots[i]);
            for (int i = 3; i >= 0; i--) LeftLeds.Children.Add(_ledDots[i]);
            for (int i = 12; i < 16; i++) RightLeds.Children.Add(_ledDots[i]);
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

        private void BuildNav()
        {
            DashboardNavList.Children.Clear();
            foreach (DashboardSettings dashboard in _settings.Dashboards)
            {
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = BrushFor(dashboard.AccentColor), Margin = new Thickness(3, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center });
                var name = new TextBlock { Text = dashboard.Name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(name, 1);
                row.Children.Add(name);
                if (dashboard.Id == _settings.DefaultDashboardId)
                {
                    var tag = new TextBlock { Text = "Default", Style = (Style)FindResource("T.Overline"), FontWeight = FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
                    Grid.SetColumn(tag, 2);
                    row.Children.Add(tag);
                }
                var item = new RadioButton
                {
                    Style = (Style)FindResource("Nav.Item"), GroupName = "Nav", Content = row, Tag = dashboard,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    IsChecked = EditorPage.Visibility == Visibility.Visible && dashboard == _dashboard
                };
                item.Checked += (s, e) =>
                {
                    if (_loading || (dashboard == _dashboard && EditorPage.Visibility == Visibility.Visible)) return;
                    ShowEditor(dashboard);
                };
                DashboardNavList.Children.Add(item);
            }
        }

        private void SelectNavFor(DashboardSettings dashboard)
        {
            foreach (RadioButton item in DashboardNavList.Children.OfType<RadioButton>())
                if (item.Tag == dashboard && item.IsChecked != true)
                {
                    item.IsChecked = true;
                    return;
                }
        }

        private void NavGallery_Checked(object sender, RoutedEventArgs e) => ShowPage(GalleryPage);
        private void NavSettings_Checked(object sender, RoutedEventArgs e) => ShowPage(SettingsPage);
        private void BackToGallery_Click(object sender, RoutedEventArgs e) => NavGallery.IsChecked = true;

        private void ShowPage(ScrollViewer page)
        {
            DisarmConfirm();
            GalleryPage.Visibility = page == GalleryPage ? Visibility.Visible : Visibility.Collapsed;
            EditorPage.Visibility = page == EditorPage ? Visibility.Visible : Visibility.Collapsed;
            SettingsPage.Visibility = page == SettingsPage ? Visibility.Visible : Visibility.Collapsed;
            if (page == GalleryPage) BuildGalleryCards();
            if (page == SettingsPage) RefreshPorts();
            if (page == EditorPage) _ledTimer.Start(); else _ledTimer.Stop();
            page.ScrollToTop();
        }

        private void ShowEditor(DashboardSettings dashboard)
        {
            _dashboard = dashboard;
            LoadDashboardValues();
            ShowPage(EditorPage);
            SelectNavFor(dashboard);
        }

        // ------------------------------------------------------------------
        // Gallery
        // ------------------------------------------------------------------

        private const double CardGap = 20, CardMinWidth = 330;

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

                var body = new StackPanel { Margin = new Thickness(20, 16, 20, 18) };
                var title = new StackPanel { Orientation = Orientation.Horizontal };
                title.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = BrushFor(d.AccentColor), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
                title.Children.Add(new TextBlock { Text = d.Name, Style = (Style)FindResource("T.Title"), VerticalAlignment = VerticalAlignment.Center });
                if (isDefault) title.Children.Add(Pill("Default", "Brush.SuccessSubtle", "Brush.Success", new Thickness(10, 0, 0, 0)));
                body.Children.Add(title);
                body.Children.Add(new TextBlock { Text = Layouts.Describe(d.Layout), Style = (Style)FindResource("T.Caption"), Margin = new Thickness(0, 6, 0, 12) });

                var tags = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
                if (d.RaceTypes.Count == 0)
                    tags.Children.Add(Pill(isDefault ? "Used whenever no other match" : "Not assigned to a track type", "Brush.SurfaceRaised", "Brush.TextTertiary", new Thickness(0, 0, 6, 6)));
                foreach (string type in Disciplines.All.Where(d.RaceTypes.Contains))
                    tags.Children.Add(Pill(type, "Brush.SurfaceRaised", "Brush.TextSecondary", new Thickness(0, 0, 6, 6)));
                body.Children.Add(tags);

                var actions = new StackPanel { Orientation = Orientation.Horizontal };
                var customize = new Button { Content = "Customize", Margin = new Thickness(0, 0, 8, 0) };
                customize.Click += (s, e) => ShowEditor(d);
                actions.Children.Add(customize);
                if (!isDefault)
                {
                    var makeDefault = new Button { Content = "Set as default", Style = (Style)FindResource("Btn.Ghost") };
                    makeDefault.Click += (s, e) => MakeDefault(d);
                    actions.Children.Add(makeDefault);
                }
                body.Children.Add(actions);
                stack.Children.Add(body);
                card.Child = stack;
                card.Tag = thumbnail;
                _galleryCards.Add(card);
                DashboardCards.Children.Add(card);
            }
            LayoutGalleryCards();
        }

        private void DashboardCards_SizeChanged(object sender, SizeChangedEventArgs e) => LayoutGalleryCards();

        private void LayoutGalleryCards()
        {
            double available = DashboardCards.ActualWidth;
            if (available <= 0) return;
            int columns = Math.Max(1, (int)((available + CardGap) / (CardMinWidth + CardGap)));
            double width = Math.Floor((available - CardGap * columns) / columns);
            foreach (Border card in _galleryCards)
            {
                card.Width = width;
                ((Border)card.Tag).Height = Math.Round((width - 2) * Dv5Screen.Height / Dv5Screen.Width);
            }
        }

        private void NewDashboard_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu { PlacementTarget = NewDashboardButton, Placement = PlacementMode.Bottom };
            foreach (string layout in Layouts.All)
            {
                string l = layout;
                var header = new StackPanel { Margin = new Thickness(0, 1, 0, 1) };
                header.Children.Add(new TextBlock { Text = l + " layout", FontWeight = FontWeights.SemiBold });
                header.Children.Add(new TextBlock { Text = Layouts.Describe(l), Style = (Style)FindResource("T.Caption"), MaxWidth = 260 });
                var item = new MenuItem { Header = header, Icon = GlyphIcon("") };
                item.Click += (s, a) =>
                {
                    DashboardSettings created = _settings.AddDashboard(l);
                    Save();
                    BuildNav();
                    ShowEditor(created);
                    NameBox.Focus();
                    NameBox.SelectAll();
                };
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
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
            bool builtIn = AppSettings.IsBuiltIn(_dashboard);
            EditorTitle.Text = _dashboard.Name;
            EditorSubtitle.Text = _dashboard.Layout + " layout · " + Layouts.Describe(_dashboard.Layout);
            EditorDefaultBadge.Visibility = isDefault ? Visibility.Visible : Visibility.Collapsed;
            SetDefaultButton.Visibility = isDefault ? Visibility.Collapsed : Visibility.Visible;
            NameBox.Text = _dashboard.Name;

            OvalChip.IsChecked = _dashboard.RaceTypes.Contains(Disciplines.Oval);
            RoadChip.IsChecked = _dashboard.RaceTypes.Contains(Disciplines.Road);
            DirtOvalChip.IsChecked = _dashboard.RaceTypes.Contains(Disciplines.DirtOval);
            DirtRoadChip.IsChecked = _dashboard.RaceTypes.Contains(Disciplines.DirtRoad);
            UpdateAssignmentHelp();

            foreach (BankEditor editor in Editors()) LoadBank(editor);
            MirrorSides.IsChecked = SameJson(_dashboard.LeftLeds, _dashboard.RightLeds);
            UpdateMirrorState();

            BlinkCountSlider.Value = _dashboard.Alerts.BlinkCount;
            BlinkLengthSlider.Value = _dashboard.Alerts.BlinkMilliseconds;
            HoldSlider.Value = _dashboard.Alerts.HoldMilliseconds;
            UpdateAlertLabels();

            DeleteButton.IsEnabled = !builtIn;
            DeleteButton.ToolTip = builtIn ? "Built-in dashboards can be reset but not deleted." : null;
            ManageHelp.Text = builtIn
                ? "Duplicate this dashboard to experiment freely. Built-in dashboards can be reset but not deleted."
                : "Duplicate this dashboard to experiment, reset its lights and alerts, or remove it.";
            _loading = false;
            RefreshAccent();
            RefreshPreview();
        }

        private void LoadBank(BankEditor editor)
        {
            bool was = _loading;
            _loading = true;
            LedBankSettings bank = editor.Bank();
            if (editor == _center) editor.Function.SelectedIndex = bank.Function == LedFunctions.Disabled ? 1 : 0;
            else editor.Function.SelectedItem = LedFunctions.Side.Contains(bank.Function) ? bank.Function : LedFunctions.BlindSpot;
            if (editor.Pattern != null) editor.Pattern.SelectedItem = LedPatterns.Center.Contains(bank.Pattern) ? bank.Pattern : LedPatterns.Center[0];
            editor.Profile.SelectedItem = LedProfiles.All.Contains(bank.ColorProfile) ? bank.ColorProfile : LedProfiles.Custom;
            editor.Brightness.Value = bank.Brightness;
            editor.BrightnessValue.Text = bank.Brightness + "%";
            if (editor.FunctionHelp != null) editor.FunctionHelp.Text = DescribeSideFunction(bank.Function);

            DrawingColor[] colors = LedFrame.GetProfile(bank);
            editor.Low.Background = BrushFor(colors[0]);
            editor.Mid.Background = BrushFor(colors[1]);
            editor.High.Background = BrushFor(colors[2]);
            bool single = bank.ColorProfile == LedProfiles.Single;
            editor.Low.Visibility = single ? Visibility.Collapsed : Visibility.Visible;
            editor.Mid.Visibility = single ? Visibility.Collapsed : Visibility.Visible;
            string role = editor == _center ? "RPM" : "level";
            editor.Low.ToolTip = "Low " + role + " · " + SettingsStore.ToHex(colors[0]);
            editor.Mid.ToolTip = "Mid " + role + " · " + SettingsStore.ToHex(colors[1]);
            editor.High.ToolTip = (single ? "Color" : "High " + role) + " · " + SettingsStore.ToHex(colors[2]);
            bool off = bank.Function == LedFunctions.Disabled;
            foreach (UIElement control in new UIElement[] { editor.Profile, editor.Low, editor.Mid, editor.High, editor.Brightness })
                control.IsEnabled = !off;
            if (editor.Pattern != null) editor.Pattern.IsEnabled = !off;
            _loading = was;
        }

        private static string DescribeSideFunction(string function)
        {
            switch (function)
            {
                case LedFunctions.BlindSpot: return "Lights when a car is alongside: top when it's ahead, middle beside you, bottom as it drops back.";
                case LedFunctions.Rpm: return "Fills from the top as revs rise, together with the shift lights.";
                case LedFunctions.Brake: return "Fills from the bottom with brake pedal pressure.";
                case LedFunctions.Throttle: return "Fills from the bottom with throttle position.";
                default: return "These lights stay off.";
            }
        }

        private void OnBankEdited(BankEditor editor, Action<LedBankSettings> apply)
        {
            if (_loading) return;
            apply(editor.Bank());
            AfterBankChange(editor);
        }

        private void AfterBankChange(BankEditor editor)
        {
            if (editor == _left && MirrorSides.IsChecked == true)
            {
                _right.Replace(_dashboard.LeftLeds);
                LoadBank(_right);
            }
            LoadBank(editor);
            Changed();
        }

        private static void ApplyProfile(LedBankSettings bank, string profile)
        {
            if (profile == null || profile == bank.ColorProfile) return;
            string[] preset = LedProfiles.PresetColors(profile);
            if (preset != null)
            {
                bank.LowColor = preset[0];
                bank.MidColor = preset[1];
                bank.HighColor = preset[2];
            }
            else if (profile == LedProfiles.Single)
            {
                bank.LowColor = bank.HighColor;
                bank.MidColor = bank.HighColor;
            }
            bank.ColorProfile = profile;
        }

        private void PickStageColor(BankEditor editor, int stage)
        {
            LedBankSettings bank = editor.Bank();
            DrawingColor[] current = LedFrame.GetProfile(bank);
            if (!WindowsColorPicker.TryChoose(SettingsStore.ToHex(current[stage]), out string chosen)) return;
            // Keep the displayed colors, then switch to Custom so the choice sticks.
            bank.LowColor = SettingsStore.ToHex(current[0]);
            bank.MidColor = SettingsStore.ToHex(current[1]);
            bank.HighColor = SettingsStore.ToHex(current[2]);
            if (bank.ColorProfile == LedProfiles.Single)
            {
                bank.LowColor = bank.MidColor = bank.HighColor = chosen;
            }
            else
            {
                if (stage == 0) bank.LowColor = chosen;
                else if (stage == 1) bank.MidColor = chosen;
                else bank.HighColor = chosen;
                bank.ColorProfile = LedProfiles.Custom;
            }
            AfterBankChange(editor);
        }

        private void MirrorSides_Click(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            if (MirrorSides.IsChecked == true)
            {
                _right.Replace(_dashboard.LeftLeds);
                LoadBank(_right);
                Changed();
            }
            UpdateMirrorState();
        }

        private void UpdateMirrorState()
        {
            bool mirrored = MirrorSides.IsChecked == true;
            RightPanel.IsEnabled = !mirrored;
            RightPanel.Opacity = mirrored ? 0.45 : 1;
            RightPanel.ToolTip = mirrored ? "Following the left side. Turn off “Same on both sides” to edit." : null;
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
            BuildNav();
        }

        private void SetAccent(string hex)
        {
            _dashboard.AccentColor = hex;
            RefreshAccent();
            BuildNav();
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
            BuildNav();
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
            var delete = new MenuItem { Header = "Delete", Icon = GlyphIcon(""), IsEnabled = !AppSettings.IsBuiltIn(_dashboard) };
            delete.Click += (s, a) => { DeleteButton.BringIntoView(); ArmConfirm(DeleteButton, DeleteLabel, "Click again to delete"); };
            menu.Items.Add(duplicate);
            menu.Items.Add(reset);
            menu.Items.Add(delete);
            menu.IsOpen = true;
        }

        private void Duplicate_Click(object sender, RoutedEventArgs e)
        {
            DashboardSettings copy = _settings.AddDashboard(_dashboard.Layout, _dashboard);
            Save();
            BuildNav();
            ShowEditor(copy);
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            if (_armedButton != ResetButton) { ArmConfirm(ResetButton, ResetLabel, "Click again to reset"); return; }
            DisarmConfirm();
            _settings.ResetDashboard(_dashboard);
            Save("Reset to defaults");
            BuildNav();
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
            BuildNav();
            NavGallery.IsChecked = true;
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
            RacingFlag flag;
            switch (PreviewState.SelectedIndex)
            {
                case 2: flag = RacingFlag.Green; break;
                case 3: flag = RacingFlag.Yellow; break;
                case 4: flag = RacingFlag.White; break;
                case 5: flag = RacingFlag.BlueYellowStripe; break;
                case 6: flag = RacingFlag.Black; break;
                case 7: flag = RacingFlag.Checkered; break;
                default: flag = RacingFlag.None; break;
            }
            PreviewBrush.ImageSource = RenderPreview(_dashboard, new UnitSystem(_settings.General),
                IRacingTelemetrySource.CreatePreviewSnapshot(flag), PreviewState.SelectedIndex == 1);
            UpdateLedPreview();
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

        private void UpdateLedPreview()
        {
            if (_dashboard == null || _ledDots[0] == null) return;
            double t = _ledPhase;
            bool animate = AnimateLeds.IsChecked == true;
            bool shiftState = PreviewState.SelectedIndex == 1;
            BlindSpotZone[] cycle = { BlindSpotZone.None, BlindSpotZone.Front, BlindSpotZone.Beside, BlindSpotZone.Rear, BlindSpotZone.None };
            double rpm = animate ? Math.Min(1, (t % 3.2) / 3.2 * 1.12) : shiftState ? 1 : 0.62;
            var input = new LedInputs
            {
                ShiftPct = rpm,
                ShiftFlashOn = rpm >= 0.98 && (!animate || ((int)(t * 7) & 1) == 0),
                Left = animate ? cycle[(int)(t / 0.7) % cycle.Length] : BlindSpotZone.Beside,
                Right = animate ? cycle[((int)(t / 0.7) + 2) % cycle.Length] : BlindSpotZone.None,
                Brake = animate ? Math.Max(0, Math.Sin(t * 1.6)) : 0.55,
                Throttle = animate ? Math.Max(0, -Math.Sin(t * 1.6)) : 0.8
            };
            DrawingColor[] frame = LedFrame.Compose(_dashboard, input);
            for (int i = 0; i < LedFrame.Count; i++)
            {
                DrawingColor c = frame[i];
                if (c.R == 0 && c.G == 0 && c.B == 0)
                {
                    _ledDots[i].Fill = (Brush)FindResource("Brush.LedOff");
                    _ledGlows[i].Opacity = 0;
                    continue;
                }
                // Show the true hue at a visible level, and express brightness as intensity.
                int brightness = Math.Max(1, (i < 4 ? _dashboard.LeftLeds : i < 12 ? _dashboard.CenterLeds : _dashboard.RightLeds).Brightness);
                Color hue = Color.FromRgb(Boost(c.R, brightness), Boost(c.G, brightness), Boost(c.B, brightness));
                double level = 0.45 + 0.55 * brightness / 100.0;
                _ledDots[i].Fill = new SolidColorBrush(Color.FromArgb((byte)(255 * level), hue.R, hue.G, hue.B));
                _ledGlows[i].Color = hue;
                _ledGlows[i].Opacity = 0.9 * level;
            }
        }

        private static byte Boost(int channel, int brightness) => (byte)Math.Min(255, channel * 100 / brightness);

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
            AutoSwitchToggle.IsChecked = general.AutoSwitchByDiscipline;
            DiagnosticsToggle.IsChecked = general.DiagnosticsEnabled;
            StartupToggle.IsChecked = File.Exists(StartupShortcutPath);
            RefreshPorts();
        }

        private void RefreshPorts()
        {
            bool was = _loading;
            _loading = true;
            var ports = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            try { foreach (string port in SerialPort.GetPortNames()) ports.Add(port); } catch { }
            if (!string.IsNullOrWhiteSpace(_settings.General.LedPort)) ports.Add(_settings.General.LedPort);
            PortCombo.ItemsSource = ports.OrderBy(p => p.Length).ThenBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
            PortCombo.SelectedItem = ports.FirstOrDefault(p => string.Equals(p, _settings.General.LedPort, StringComparison.OrdinalIgnoreCase));
            _loading = was;
        }

        private void RefreshPorts_Click(object sender, RoutedEventArgs e) => RefreshPorts();

        private void PortCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || !(PortCombo.SelectedItem is string port)) return;
            _settings.General.LedPort = port;
            Save(IsDashboardRunning() ? "Saved · restart the dashboard to use " + port : "Saved");
        }

        private async void TestLeds_Click(object sender, RoutedEventArgs e)
        {
            string port = _settings.General.LedPort;
            LedTestStatus.Visibility = Visibility.Visible;
            if (IsDashboardRunning())
            {
                ShowTestStatus("Stop the dashboard first so the lights are free to test.", "Brush.Warning");
                return;
            }
            TestLedsButton.IsEnabled = false;
            ShowTestStatus("Testing lights on " + port + "…", "Brush.TextSecondary");
            DashboardSettings dashboard = SettingsStore.Clone(_dashboard ?? _settings.GetDefaultDashboard());
            try
            {
                string device = await Task.Run(() => RunLedTest(port, dashboard));
                ShowTestStatus($"Found {device} on {port}. The lights just ran through a sweep.", "Brush.Success");
            }
            catch (Exception ex)
            {
                ShowTestStatus($"Couldn't reach the lights on {port}. {ex.Message}", "Brush.Danger");
            }
            finally
            {
                TestLedsButton.IsEnabled = true;
            }
        }

        private static string RunLedTest(string port, DashboardSettings dashboard)
        {
            using (var leds = new Dv5LedController(port, 115200, LedFrame.Count))
            {
                leds.Connect();
                string name = leds.QueryDeviceName();
                for (int step = 0; step <= 8; step++)
                {
                    leds.SetFrame(LedFrame.Compose(dashboard, new LedInputs { ShiftPct = step / 8.0 }));
                    Thread.Sleep(110);
                }
                for (int flash = 0; flash < 6; flash++)
                {
                    leds.SetFrame(LedFrame.Compose(dashboard, new LedInputs { ShiftPct = 1, ShiftFlashOn = flash % 2 == 0 }));
                    Thread.Sleep(120);
                }
                foreach (BlindSpotZone zone in new[] { BlindSpotZone.Front, BlindSpotZone.Beside, BlindSpotZone.Rear })
                {
                    leds.SetFrame(LedFrame.Compose(dashboard, new LedInputs { Left = zone, Right = zone }));
                    Thread.Sleep(350);
                }
                leds.Clear();
                return name;
            }
        }

        private void ShowTestStatus(string text, string brushKey)
        {
            LedTestStatus.Text = text;
            LedTestStatus.Foreground = (Brush)FindResource(brushKey);
        }

        private void Units_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _settings.General.SpeedUnit = SpeedKph.IsChecked == true ? "KPH" : "MPH";
            _settings.General.FuelUnit = FuelGallons.IsChecked == true ? "Gallons" : "Liters";
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
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "DV5Direct.lnk");

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
                t.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { "Start the DV5Direct iRacing dashboard automatically" });
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

        private async void StartDashboard_Click(object sender, RoutedEventArgs e)
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
            Launch("--run");
            bool running = await WaitForDashboardState(true, 10000);
            _dashboardOperationInProgress = false;
            StartButton.IsEnabled = true;
            if (running) RefreshDashboardStatus(); else SetStopped("Couldn't start");
        }

        private async void StopDashboard_Click(object sender, RoutedEventArgs e)
        {
            _dashboardOperationInProgress = true;
            SetStatus("Stopping…", "Brush.Warning");
            Launch("--stop");
            bool stopped = await WaitForDashboardState(false, 10000);
            _dashboardOperationInProgress = false;
            if (stopped) RefreshDashboardStatus(); else SetStatus("Didn't stop", "Brush.Danger");
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
                return !process.HasExited && string.Equals(process.ProcessName, "DV5Direct", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private void RefreshDashboardStatus()
        {
            if (!IsDashboardRunning())
            {
                SetStopped("Stopped");
                return;
            }
            SetStatus("Running", "Brush.Success");
            StartLabel.Text = "Restart dashboard";
            StartIcon.Text = "";
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
            ActiveText.Text = shown == null ? "" :
                "Showing " + shown.Name + (string.IsNullOrEmpty(discipline) ? "" : " · " + discipline.ToLowerInvariant() + " track");
        }

        private void SetStopped(string text)
        {
            SetStatus(text, "Brush.TextTertiary");
            StartLabel.Text = "Start dashboard";
            StartIcon.Text = "";
            StopButton.IsEnabled = false;
            ActiveText.Text = "Default is " + _settings.GetDefaultDashboard().Name;
        }

        private void SetStatus(string text, string brushKey)
        {
            var brush = (SolidColorBrush)FindResource(brushKey);
            StatusText.Text = text;
            StatusDot.Fill = brush;
            StatusPill.Background = new SolidColorBrush(Color.FromArgb(0x1F, brush.Color.R, brush.Color.G, brush.Color.B));
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
            Newtonsoft.Json.JsonConvert.SerializeObject(a) == Newtonsoft.Json.JsonConvert.SerializeObject(b);

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
