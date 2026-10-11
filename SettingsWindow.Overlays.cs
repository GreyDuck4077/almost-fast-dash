using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace AlmostFastOverlays
{
    public partial class SettingsWindow
    {
        private DispatcherTimer _overlayPreviewTimer;
        private readonly System.Collections.Generic.List<Tuple<FrameworkElement, OverlayConfig>> _overlayPreviews = new System.Collections.Generic.List<Tuple<FrameworkElement, OverlayConfig>>();
        private int _overlayPreviewTick;
        private Button _overlayEdit;
        private readonly System.Collections.Generic.List<Border> _overlayCards = new System.Collections.Generic.List<Border>();

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
            if (scenario.Contains("standings")) _overlaySelected = "standings";
            else if (scenario.Contains("relative")) _overlaySelected = "relative";
            else if (scenario.Contains("toasts")) _overlaySelected = "toasts";
            if (scenario.Contains("editor")) ShowEditor(_settings.GetDefaultDashboard());
            if (scenario.Contains("overlays")) NavOverlays.IsChecked = true;
            if (scenario.Contains("columns"))
                Dispatcher.BeginInvoke(new Action(() => OverlaysPage.ScrollToVerticalOffset(820)), DispatcherPriority.Loaded);
            else if (scenario.Contains("devices")) NavSettings.IsChecked = true;
            else if (scenario.Contains("app")) NavApp.IsChecked = true;
        }

        private void StopOverlayPreview()
        {
            _overlayPreviewTimer?.Stop();
        }

        // ------------------------------------------------------------------
        // Overlays section: an overview of every overlay, and a page for each one,
        // laid out the same way as Dashboards.
        // ------------------------------------------------------------------

        /// <summary>The overlay whose page is open; null shows the overview.</summary>
        private string _overlaySelected;
        private bool _buildingOverlayNav;

        private void NavOverlays_Click(object sender, RoutedEventArgs e)
        {
            // Clicking Overlays again from an overlay's page goes back to the overview, like Dashboards.
            if (_overlaySelected != null && OverlaysPage.Visibility == Visibility.Visible) ShowOverlay(null);
        }

        private void ShowOverlay(string id)
        {
            _overlaySelected = id;
            BuildOverlaySubNav();
            if (NavOverlays.IsChecked == true)
            {
                BuildOverlaysPage();
                AnimatePageIn(OverlaysPage);
                OverlaysPage.ScrollToTop();
            }
            else NavOverlays.IsChecked = true;
        }

        /// <summary>
        /// The overlays listed under Overlays in the sidebar, each with a dot that's lit
        /// when it's switched on. Picking one opens its page.
        /// </summary>
        private void BuildOverlaySubNav()
        {
            _buildingOverlayNav = true;
            OverlaySubNav.Children.Clear();
            OverlaySettings overlays = _settings.Overlays;
            if (overlays.Find(_overlaySelected ?? "") == null) _overlaySelected = null;
            foreach (OverlayConfig config in overlays.Items)
            {
                OverlayConfig c = config;
                var item = new RadioButton
                {
                    Style = (Style)FindResource("Nav.SubItem"), GroupName = "OverlaySub", IsChecked = c.Id == _overlaySelected,
                    Content = SubNavRow(c.Name, new Ellipse
                    {
                        Width = 6, Height = 6, VerticalAlignment = VerticalAlignment.Center,
                        Fill = (Brush)FindResource(c.Enabled ? "Brush.Success" : "Brush.TextTertiary"), Opacity = c.Enabled ? 1 : 0.5,
                        ToolTip = c.Enabled ? "On" : "Off"
                    })
                };
                item.Checked += (s, e) => { if (!_buildingOverlayNav && _overlaySelected != c.Id) ShowOverlay(c.Id); };
                OverlaySubNav.Children.Add(item);
            }
            _buildingOverlayNav = false;
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

        private void ShowOverlaySubNav(bool show) => ShowSubNav(OverlaySubNav, show);

        private void BuildOverlaysPage()
        {
            OverlaysHost.Children.Clear();
            _overlayPreviews.Clear();
            _overlayCards.Clear();
            OverlaySettings overlays = _settings.Overlays;
            OverlayConfig selected = overlays.Find(_overlaySelected ?? "");
            OverlayType selectedType = selected == null ? null : OverlayTypes.Find(selected.Type);
            if (selectedType == null) { _overlaySelected = null; selected = null; }

            _overlayEdit = new Button { ToolTip = "Show every overlay that's on with a frame, so you can drag it into place" };
            _overlayEdit.Click += (s, e) => ToggleOverlayEditing();

            if (selected == null)
            {
                // ---------------- overview
                OverlaysHost.Children.Add(PageHeader(null, "Overlays",
                    "Live information on your monitor, over the game. Run iRacing in borderless window mode so overlays can sit on top.", _overlayEdit));
                var general = new Border { Style = (Style)FindResource("Card") };
                general.Child = SwitchRow("Start overlays with the dashboard", "When the dashboard starts (or starts with Windows), the overlays come up too, and close with it.",
                    overlays.StartWithDashboard, v => { overlays.StartWithDashboard = v; Save(); });
                OverlaysHost.Children.Add(general);
                var cards = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
                cards.SizeChanged += (s, e) => LayoutCards(cards, _overlayCards);
                foreach (OverlayConfig config in overlays.Items)
                {
                    OverlayType type = OverlayTypes.Find(config.Type);
                    if (type == null) continue;
                    Border card = OverlaySummaryCard(config, type);
                    _overlayCards.Add(card);
                    cards.Children.Add(card);
                }
                OverlaysHost.Children.Add(cards);
            }
            else
            {
                // ---------------- one overlay
                var enabled = new CheckBox { Style = (Style)FindResource("Switch"), Content = "On", IsChecked = selected.Enabled, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0), ToolTip = "Show this overlay" };
                enabled.Click += (s, e) => SetOverlayEnabled(selected, selectedType, enabled.IsChecked == true);
                OverlaysHost.Children.Add(PageHeader("All overlays", selectedType.Name, selectedType.Description, enabled, _overlayEdit));
                OverlaysHost.Children.Add(OverlayCard(selected, selectedType));
            }

            RefreshOverlayStatus();
            _overlayPreviewTimer = _overlayPreviewTimer ?? new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1800) };
            _overlayPreviewTimer.Tick -= OverlayPreviewTick;
            _overlayPreviewTimer.Tick += OverlayPreviewTick;
            _overlayPreviewTimer.Start();
        }

        private void SetOverlayEnabled(OverlayConfig config, OverlayType type, bool on)
        {
            config.Enabled = on;
            Save(on ? type.Name + " on" : type.Name + " off");
            BuildOverlaySubNav();
        }

        /// <summary>
        /// The top of every page in a section: a link back to the section's overview
        /// (on an item's page), the title and description, and the page's actions.
        /// </summary>
        private StackPanel PageHeader(string backText, string title, string description, params FrameworkElement[] actions)
        {
            var page = new StackPanel { Margin = new Thickness(0, 0, 0, 22) };
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var titles = new StackPanel();
            if (backText != null)
            {
                var back = new Button { Style = (Style)FindResource("Btn.Ghost"), HorizontalAlignment = HorizontalAlignment.Left, Height = 28, Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(-6, -8, 0, 10) };
                var backContent = new StackPanel { Orientation = Orientation.Horizontal };
                backContent.Children.Add(new TextBlock { Style = (Style)FindResource("T.Icon"), Text = "", FontSize = 11, Margin = new Thickness(0, 0, 8, 0) });
                backContent.Children.Add(new TextBlock { Text = backText, FontWeight = FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center });
                back.Content = backContent;
                back.Click += (s, e) => ShowOverlay(null);
                page.Children.Add(back);
            }
            titles.Children.Add(new TextBlock { Text = title, Style = (Style)FindResource("T.PageTitle") });
            titles.Children.Add(new TextBlock { Text = description, Style = (Style)FindResource("T.Secondary"), Margin = new Thickness(0, 6, 0, 0), MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left, TextWrapping = TextWrapping.Wrap });
            header.Children.Add(titles);
            var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
            foreach (FrameworkElement action in actions) controls.Children.Add(action);
            Grid.SetColumn(controls, 1);
            header.Children.Add(controls);
            page.Children.Add(header);
            return page;
        }

        /// <summary>An overlay in the overview: a still preview, its name and state, and the way in. Mirrors a dashboard card.</summary>
        private Border OverlaySummaryCard(OverlayConfig config, OverlayType type)
        {
            var card = new Border
            {
                Style = (Style)FindResource("Card"), Padding = new Thickness(0), Margin = new Thickness(0, 0, CardGap, CardGap),
                Cursor = System.Windows.Input.Cursors.Hand, Background = (Brush)FindResource("Brush.Surface")
            };
            card.MouseEnter += (s, e) => card.BorderBrush = (Brush)FindResource("Brush.BorderStrong");
            card.MouseLeave += (s, e) => card.BorderBrush = (Brush)FindResource("Brush.Border");
            card.MouseLeftButtonUp += (s, e) =>
            {
                if (!(e.OriginalSource is DependencyObject o && (FindParent<Button>(o) != null || FindParent<CheckBox>(o) != null))) ShowOverlay(config.Id);
            };

            var stack = new StackPanel();
            var stage = new Border
            {
                CornerRadius = new CornerRadius(11, 11, 0, 0), Padding = new Thickness(18), ClipToBounds = true, Height = 220,
                BorderBrush = (Brush)FindResource("Brush.Border"), BorderThickness = new Thickness(0, 0, 0, 1),
                Background = StageBrush()
            };
            FrameworkElement view;
            if (type.Id == "toasts")
            {
                var toasts = new ToastView();
                toasts.Update(null, null, config, false);
                foreach (Toast sample in ToastView.Samples.Reverse()) toasts.Show(sample, true);
                view = toasts;
            }
            else
            {
                var table = new StandingsView();
                table.Update(StandingsSample.Create(_overlayPreviewTick), config);
                view = table;
                _overlayPreviews.Add(Tuple.Create<FrameworkElement, OverlayConfig>(table, config));
            }
            // Fit the preview to the card's width and let a tall table run off the bottom,
            // so it stays readable instead of shrinking to fit the height.
            var fit = new StackPanel();
            fit.Children.Add(new Viewbox { Child = view, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Center });
            stage.Child = fit;
            stack.Children.Add(stage);

            var body = new StackPanel { Margin = new Thickness(20, 16, 20, 18) };
            var title = new Grid();
            title.ColumnDefinitions.Add(new ColumnDefinition());
            title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            title.Children.Add(new TextBlock { Text = type.Name, Style = (Style)FindResource("T.Title"), VerticalAlignment = VerticalAlignment.Center });
            var enabled = new CheckBox { Style = (Style)FindResource("Switch"), IsChecked = config.Enabled, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Show this overlay" };
            enabled.Click += (s, e) => SetOverlayEnabled(config, type, enabled.IsChecked == true);
            Grid.SetColumn(enabled, 1);
            title.Children.Add(enabled);
            body.Children.Add(title);
            body.Children.Add(new TextBlock { Text = type.Description, Style = (Style)FindResource("T.Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 14) });
            var customize = new Button { Content = "Customize", HorizontalAlignment = HorizontalAlignment.Left };
            customize.Click += (s, e) => ShowOverlay(config.Id);
            body.Children.Add(customize);
            stack.Children.Add(body);
            card.Child = stack;
            return card;
        }

        /// <summary>The dark "track" behind overlay previews, so glass reads as it will over the game.</summary>
        private static Brush StageBrush() => new LinearGradientBrush(new GradientStopCollection
        {
            new GradientStop(Color.FromArgb(150, 54, 78, 96), 0), new GradientStop(Color.FromArgb(150, 22, 30, 28), 0.7), new GradientStop(Color.FromArgb(150, 12, 14, 12), 1)
        }, 90);

        private void OverlayPreviewTick(object sender, EventArgs e)
        {
            // Move the sample field on now and then so the animations are visible.
            _overlayPreviewTick++;
            foreach (var preview in _overlayPreviews)
            {
                if (preview.Item1 is StandingsView table) table.Update(StandingsSample.Create(_overlayPreviewTick), preview.Item2);
                else if (preview.Item1 is ToastView toasts && _overlayPreviewTick % 2 == 0)
                    toasts.Show(ToastView.Samples[_overlayPreviewTick / 2 % ToastView.Samples.Length]);
            }
        }

        /// <summary>The overlay's page: a live preview on the left and its settings on the right.</summary>
        private Border OverlayCard(OverlayConfig config, OverlayType type)
        {
            var card = new Border { Style = (Style)FindResource("Card"), Padding = new Thickness(0) };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(380) });

            // Live preview over a dark "track" backdrop.
            var stage = new Border
            {
                CornerRadius = new CornerRadius(15, 0, 0, 15), Padding = new Thickness(26), MinHeight = 420, ClipToBounds = true, Background = StageBrush()
            };
            FrameworkElement view;
            if (type.Id == "toasts")
            {
                var toasts = new ToastView { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
                toasts.Update(null, null, config, false);
                foreach (Toast sample in ToastView.Samples.Reverse()) toasts.Show(sample);
                view = toasts;
            }
            else
            {
                var table = new StandingsView { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
                table.Update(StandingsSample.Create(_overlayPreviewTick), config);
                view = table;
            }
            _overlayPreviews.Add(Tuple.Create(view, config));
            var viewbox = new Viewbox { Child = view, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, VerticalAlignment = VerticalAlignment.Top };
            stage.Child = viewbox;
            grid.Children.Add(stage);

            // Options.
            var options = new StackPanel { Margin = new Thickness(24, 6, 24, 22) };

            void Changed()
            {
                if (view is StandingsView table) table.Update(StandingsSample.Create(_overlayPreviewTick), config);
                else if (view is ToastView toasts) toasts.Update(null, null, config, false);
                Save();
            }
            options.Children.Add(Section("Look"));
            options.Children.Add(SliderRow("Size", config.Scale * 100, 50, 200, "%", v => { config.Scale = v / 100; Changed(); }));
            options.Children.Add(SliderRow("Background", config.Opacity, 0, 100, "%", v => { config.Opacity = (int)v; Changed(); }));
            options.Children.Add(CompactSwitch("Show in the garage", config.ShowInGarage, v => { config.ShowInGarage = v; Save(); }));
            foreach (var group in type.Options.Where(o => !o.Inline).GroupBy(o => o.Group))
            {
                options.Children.Add(Section(group.Key));
                if (group.Any(o => o.HeaderKey != null))
                {
                    options.Children.Add(new TextBlock { Text = "Drag to reorder. Header shows the column's label above it.", Style = (Style)FindResource("T.Caption"), Margin = new Thickness(0, -4, 0, 6) });
                    options.Children.Add(ColumnList(config, type, Changed));
                    continue;
                }
                foreach (OverlayOption option in group)
                {
                    OverlayOption o = option;
                    switch (o.Kind)
                    {
                        case PropKind.Bool:
                            options.Children.Add(CompactSwitch(o.Label, config.Bool(o.Key), v => { config.Set(o.Key, v ? "true" : "false"); Changed(); }, o.Help));
                            break;
                        case PropKind.Number:
                            options.Children.Add(SliderRow(o.Label, config.Int(o.Key), o.Min, o.Max, "", v => { config.Set(o.Key, ((int)v).ToString(CultureInfo.InvariantCulture)); Changed(); }, o.Help));
                            break;
                        case PropKind.Choice:
                            var combo = new ComboBox { ItemsSource = o.Choices, SelectedItem = config.Get(o.Key), MinWidth = 0 };
                            combo.SelectionChanged += (s, e) => { config.Set(o.Key, (string)combo.SelectedItem); Changed(); };
                            options.Children.Add(LabeledRow(o.Label, combo, o.Help));
                            break;
                    }
                }
            }
            Grid.SetColumn(options, 1);
            grid.Children.Add(options);
            card.Child = grid;
            return card;
        }

        /// <summary>
        /// The columns as rows that can be dragged into a new order. Each has a Header chip
        /// (the column's label in the header row) and, unless it's always shown, an on/off switch.
        /// </summary>
        private StackPanel ColumnList(OverlayConfig config, OverlayType type, Action changed)
        {
            var list = new StackPanel { AllowDrop = true, Background = Brushes.Transparent };
            Border marker = null;
            const string Format = "AFO.OverlayColumn";

            void Fill()
            {
                list.Children.Clear();
                foreach (OverlayOption option in type.OrderedColumns(config))
                {
                    OverlayOption o = option;
                    var row = new Border
                    {
                        Tag = o.Key, CornerRadius = new CornerRadius(8), Padding = new Thickness(4, 3, 0, 3), Margin = new Thickness(0, 1, 0, 1),
                        Background = Brushes.Transparent, BorderThickness = new Thickness(0, 2, 0, 0), BorderBrush = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.SizeNS
                    };
                    var grid = new Grid();
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    grid.ColumnDefinitions.Add(new ColumnDefinition());
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
                    grid.Children.Add(new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("Font.Icons"), FontSize = 12, Foreground = (Brush)FindResource("Brush.TextTertiary"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
                    var label = new TextBlock { Text = o.Label, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("Brush.TextSecondary"), TextTrimming = TextTrimming.CharacterEllipsis };
                    Grid.SetColumn(label, 1);
                    grid.Children.Add(label);

                    bool on = o.Fixed || config.Bool(o.Key);
                    var header = new System.Windows.Controls.Primitives.ToggleButton
                    {
                        Content = "Header", Style = (Style)FindResource("Chip"), Height = 24, FontSize = 11, Margin = new Thickness(0, 0, 10, 0),
                        IsChecked = config.Bool(o.HeaderKey), IsEnabled = on, Cursor = System.Windows.Input.Cursors.Hand,
                        ToolTip = "Show this column's label in the header row"
                    };
                    header.Click += (s, e) => { config.Set(o.HeaderKey, header.IsChecked == true ? "true" : "false"); changed(); };
                    Grid.SetColumn(header, 2);
                    grid.Children.Add(header);
                    if (!o.Fixed)
                    {
                        var toggle = new CheckBox { Style = (Style)FindResource("Switch"), IsChecked = on, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Cursor = System.Windows.Input.Cursors.Hand };
                        toggle.Click += (s, e) =>
                        {
                            config.Set(o.Key, toggle.IsChecked == true ? "true" : "false");
                            header.IsEnabled = toggle.IsChecked == true;
                            changed();
                        };
                        Grid.SetColumn(toggle, 3);
                        grid.Children.Add(toggle);
                    }
                    else
                    {
                        var always = new TextBlock { Text = "Always", Style = (Style)FindResource("T.Caption"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                        Grid.SetColumn(always, 3);
                        grid.Children.Add(always);
                    }
                    row.Child = grid;

                    // Drag from anywhere on the row except its switches.
                    Point start = default(Point);
                    bool armed = false;
                    row.PreviewMouseLeftButtonDown += (s, e) =>
                    {
                        armed = !(e.OriginalSource is DependencyObject d && (FindParent<System.Windows.Controls.Primitives.ToggleButton>(d) != null));
                        start = e.GetPosition(list);
                    };
                    row.PreviewMouseMove += (s, e) =>
                    {
                        if (!armed || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;
                        Point now = e.GetPosition(list);
                        if (Math.Abs(now.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                        armed = false;
                        row.Opacity = 0.45;
                        DragDrop.DoDragDrop(row, new DataObject(Format, o.Key), DragDropEffects.Move);
                        row.Opacity = 1;
                        if (marker != null) marker.BorderBrush = Brushes.Transparent;
                    };
                    row.MouseEnter += (s, e) => row.Background = (Brush)FindResource("Brush.SurfaceRaised");
                    row.MouseLeave += (s, e) => row.Background = Brushes.Transparent;
                    list.Children.Add(row);
                }
            }

            // Where a drop would land: before the row under the pointer, or at the end.
            int TargetIndex(DragEventArgs e)
            {
                double y = e.GetPosition(list).Y;
                for (int i = 0; i < list.Children.Count; i++)
                {
                    var child = (FrameworkElement)list.Children[i];
                    Point top = child.TranslatePoint(new Point(0, 0), list);
                    if (y < top.Y + child.ActualHeight / 2) return i;
                }
                return list.Children.Count;
            }

            list.DragOver += (s, e) =>
            {
                if (!e.Data.GetDataPresent(Format)) { e.Effects = DragDropEffects.None; e.Handled = true; return; }
                e.Effects = DragDropEffects.Move;
                if (marker != null) marker.BorderBrush = Brushes.Transparent;
                int index = TargetIndex(e);
                marker = (Border)list.Children[Math.Min(index, list.Children.Count - 1)];
                marker.BorderThickness = index >= list.Children.Count ? new Thickness(0, 0, 0, 2) : new Thickness(0, 2, 0, 0);
                marker.BorderBrush = (Brush)FindResource("Brush.Accent");
                e.Handled = true;
            };
            list.DragLeave += (s, e) => { if (marker != null) marker.BorderBrush = Brushes.Transparent; };
            list.Drop += (s, e) =>
            {
                if (!e.Data.GetDataPresent(Format)) return;
                string key = (string)e.Data.GetData(Format);
                var keys = list.Children.OfType<Border>().Select(b => (string)b.Tag).ToList();
                int from = keys.IndexOf(key), to = TargetIndex(e);
                if (from < 0) return;
                keys.RemoveAt(from);
                if (to > from) to--;
                keys.Insert(Math.Max(0, Math.Min(keys.Count, to)), key);
                config.Set("columnOrder", string.Join(",", keys));
                Fill();
                changed();
            };
            Fill();
            return list;
        }

        private TextBlock Section(string text) => new TextBlock { Text = text.ToUpperInvariant(), Style = (Style)FindResource("T.Overline"), Margin = new Thickness(0, 18, 0, 8) };

        private Grid LabeledRow(string label, FrameworkElement editor, string help = null)
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("Brush.TextSecondary"), ToolTip = help, TextWrapping = TextWrapping.Wrap });
            Grid.SetColumn(editor, 1);
            row.Children.Add(editor);
            return row;
        }

        private Grid SliderRow(string label, double value, double min, double max, string suffix, Action<double> changed, string help = null)
        {
            var valueText = new TextBlock { Text = Math.Round(value) + suffix, Width = 46, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
            var slider = new Slider { Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, value)), IsSnapToTickEnabled = true, TickFrequency = 1, VerticalAlignment = VerticalAlignment.Center };
            slider.ValueChanged += (s, e) => { valueText.Text = Math.Round(slider.Value) + suffix; changed(slider.Value); };
            var pair = new Grid();
            pair.ColumnDefinitions.Add(new ColumnDefinition());
            pair.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            pair.Children.Add(slider);
            Grid.SetColumn(valueText, 1);
            pair.Children.Add(valueText);
            return LabeledRow(label, pair, help);
        }

        private Grid CompactSwitch(string label, bool value, Action<bool> changed, string help = null)
        {
            var toggle = new CheckBox { Style = (Style)FindResource("Switch"), IsChecked = value, HorizontalAlignment = HorizontalAlignment.Right };
            toggle.Click += (s, e) => changed(toggle.IsChecked == true);
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("Brush.TextSecondary"), ToolTip = help });
            Grid.SetColumn(toggle, 1);
            row.Children.Add(toggle);
            return row;
        }

        private Grid SwitchRow(string label, string help, bool value, Action<bool> changed)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 24, 0) };
            text.Children.Add(new TextBlock { Text = label, Style = (Style)FindResource("T.Label") });
            text.Children.Add(new TextBlock { Text = help, Style = (Style)FindResource("T.Caption"), Margin = new Thickness(0, 3, 0, 0) });
            row.Children.Add(text);
            var toggle = new CheckBox { Style = (Style)FindResource("Switch"), IsChecked = value, VerticalAlignment = VerticalAlignment.Center };
            toggle.Click += (s, e) => changed(toggle.IsChecked == true);
            Grid.SetColumn(toggle, 1);
            row.Children.Add(toggle);
            return row;
        }

        // ---------------- running the overlay process

        /// <summary>The Overlays control in the header, and the Arrange button on the overlay pages.</summary>
        private void RefreshOverlayStatus()
        {
            bool running = OverlayHost.IsRunning();
            bool editing = !_settings.Overlays.Locked;
            int shown = _settings.Overlays.Items.Count(i => i.Enabled);
            OverlayRunText.Text = !running ? "Off" : editing ? "Arranging" : "On · " + shown + " shown";
            OverlayRunDot.Fill = (Brush)FindResource(running ? (editing ? "Brush.Warning" : "Brush.Success") : "Brush.TextTertiary");
            SetRunButton(OverlayRunButton, OverlayRunIcon, !running, running ? "Stop the overlays" : "Start the overlays", "\uE71A");
            if (_overlayEdit != null) _overlayEdit.Content = editing ? "Done arranging" : "Arrange on screen";
        }

        private void OverlayRun_Click(object sender, RoutedEventArgs e) => ToggleOverlays();

        private void ToggleOverlays()
        {
            if (OverlayHost.IsRunning()) OverlayHost.RequestStop();
            else StartOverlayProcess();
            var check = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            check.Tick += (s, e) => { check.Stop(); RefreshOverlayStatus(); };
            check.Start();
        }

        private void StartOverlayProcess()
        {
            FlushSave();
            if (!_settings.Overlays.Items.Any(i => i.Enabled))
            {
                OverlayConfig first = _settings.Overlays.Items.FirstOrDefault();
                if (first != null)
                {
                    first.Enabled = true;
                    Save(first.Name + " on");
                    BuildOverlaySubNav();
                    if (OverlaysPage.Visibility == Visibility.Visible) BuildOverlaysPage();
                }
            }
            Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName, "--overlays")
            {
                UseShellExecute = false, WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
            });
        }

        private void ToggleOverlayEditing()
        {
            _settings.Overlays.Locked = !_settings.Overlays.Locked;
            Save(_settings.Overlays.Locked ? "Overlays locked" : "Drag overlays into place");
            if (!_settings.Overlays.Locked && !OverlayHost.IsRunning()) StartOverlayProcess();
            RefreshOverlayStatus();
        }
    }
}
