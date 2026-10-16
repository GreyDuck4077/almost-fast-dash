using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AlmostFastOverlays
{
    public partial class DesignerWindow
    {
        private static readonly HashSet<string> CollapsedGroups = new HashSet<string> { "Effects", "Visibility" };
        private readonly List<FormulaBox> _liveFormulas = new List<FormulaBox>();
        private readonly Dictionary<string, TextBox> _positionBoxes = new Dictionary<string, TextBox>();
        private FrameworkElement _primaryEditor;
        private TelemetrySnapshot _lastSnapshot;
        private bool _lastFlash;
        private DateTime _formulaRefreshUtc;

        private EvalScope CurrentScope() => new EvalScope
        {
            T = _lastSnapshot ?? IRacingTelemetrySource.CreatePreviewSnapshot(),
            Units = _units,
            Theme = _layout.Theme,
            Accent = _dashboard.AccentColor,
            ShiftFlashOn = _lastFlash
        };

        private void UpdateFormulaResults(TelemetrySnapshot snapshot, bool flash)
        {
            _lastSnapshot = snapshot;
            _lastFlash = flash;
            if ((DateTime.UtcNow - _formulaRefreshUtc).TotalMilliseconds < 200) return;
            _formulaRefreshUtc = DateTime.UtcNow;
            foreach (FormulaBox box in _liveFormulas) box.ShowResult();
        }

        // ==================================================================
        // Inspector
        // ==================================================================

        private void RefreshInspector()
        {
            if (Inspector == null) return;
            Inspector.Children.Clear();
            _liveFormulas.Clear();
            _positionBoxes.Clear();
            _propRows.Clear();
            _groupHeaders.Clear();
            _primaryEditor = null;
            if (_selection.Count == 0) BuildScreenInspector();
            else if (_selection.Count > 1) BuildMultiInspector();
            else BuildWidgetInspector(_selection[0]);
        }

        private TextBlock Heading(string text) => new TextBlock { Text = text, Style = (Style)FindResource("T.Title"), Margin = new Thickness(0, 0, 0, 4) };
        private TextBlock Caption(string text, double top = 0) => new TextBlock { Text = text, Style = (Style)FindResource("T.Caption"), Margin = new Thickness(0, top, 0, 0) };

        // ------------------------------------------------------------------ nothing selected: the screen

        private void BuildScreenInspector()
        {
            Inspector.Children.Add(Heading("Screen: " + _screen.Name));
            Inspector.Children.Add(Caption("Nothing selected. Click a widget or its layer, drag across several, drag one in from the Library, or draw one with the tools on the left."));

            Inspector.Children.Add(SectionTitle("Screen"));
            var name = new TextBox { Text = _screen.Name, MaxLength = 40 };
            name.TextChanged += (s, e) => { if (name.Text.Trim().Length > 0) { _screen.Name = name.Text.Trim(); ScreenEdited(); } };
            Inspector.Children.Add(LabeledRow("Name", name));

            Inspector.Children.Add(new TextBlock { Text = "Show when", Foreground = (Brush)FindResource("Brush.TextSecondary"), Margin = new Thickness(0, 10, 0, 4) });
            var condition = new FormulaBox(this, _screen.Condition, CurrentScope, "e.g. OnPitRoad");
            condition.Changed += text => { _screen.Condition = text.Trim(); ScreenEdited(); };
            _liveFormulas.Add(condition);
            Inspector.Children.Add(condition);
            Inspector.Children.Add(Caption(
                "Leave empty for your main screen. A screen with a condition takes over whenever its condition is true, " +
                "for example OnPitRoad for a pit-lane page, or Caution for a caution page. With several, the first true one wins.", 6));

            Inspector.Children.Add(SectionTitle("Display resolution"));
            var current = new System.Drawing.Size(_layout.Width, _layout.Height);
            var resolution = new DockPanel();
            var resize = new Button { Content = "Change…", Margin = new Thickness(8, 0, 0, 0) };
            DockPanel.SetDock(resize, Dock.Right);
            resolution.Children.Add(resize);
            resolution.Children.Add(new TextBlock { Text = current.Width + " × " + current.Height + " pixels", VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold });
            Inspector.Children.Add(resolution);
            System.Drawing.Size? wheel = ScreenSizes.Detect();
            Inspector.Children.Add(Caption(wheel == null
                ? "The dashboard is built for a screen this size. Change it to match your display; everything on it is scaled to fit."
                : wheel.Value == current
                    ? "Matches your wheel screen."
                    : "Your wheel screen is " + wheel.Value.Width + " × " + wheel.Value.Height + ". This dashboard is fitted to it without stretching; change the resolution to fill it exactly.", 6));
            resize.Click += (s, e) =>
            {
                System.Drawing.Size? chosen = ScreenSizePicker.Ask(this, "Display resolution",
                    "Every screen of this dashboard is scaled to the new size: positions and sizes follow the width and height, and text keeps its proportions. Undo puts it back.",
                    wheel ?? current, "Resize");
                if (chosen == null || chosen.Value == current) return;
                _layout.ScaleTo(chosen.Value.Width, chosen.Value.Height);
                Commit("Resize to " + chosen.Value.Width + " × " + chosen.Value.Height);
                RefreshInspector();
            };

            Inspector.Children.Add(SectionTitle("Pages"));
            int pages = _layout.Screens.Count(s => string.IsNullOrWhiteSpace(s.Condition));
            Inspector.Children.Add(Caption("Screens without a Show-when condition are pages (this design has " + pages + "). Pick a button on your wheel to step through them while driving; " +
                "the Page field tells widgets which page is showing."));
            Inspector.Children.Add(LabeledRow("Next page", ButtonPicker(() => _layout.PageButton, v => _layout.PageButton = v)));
            Inspector.Children.Add(LabeledRow("Previous page", ButtonPicker(() => _layout.PageBackButton, v => _layout.PageBackButton = v)));

            Inspector.Children.Add(SectionTitle("Quick start"));
            Inspector.Children.Add(Tip("", "Ready-made blocks", "Cards, clusters and strips in the Library are the fastest way to a polished layout."));
            Inspector.Children.Add(Tip("", "Theme first", "Pick a preset under Theme. Widgets that use theme colors ($accent, $panel...) restyle together."));
            Inspector.Children.Add(Tip("", "Make it live", "Click ƒx next to any property to drive it with a formula, such as a color that turns red when fuel is low."));
            Inspector.Children.Add(Tip("", "Show when", "Every widget has a Show when condition, so one screen can change with the session."));
            Inspector.Children.Add(Tip("", "Test it", "Use Preview › Animated lap, Live iRacing or a flag state, Saved changes reach a running dashboard on the wheel within a second."));

            Inspector.Children.Add(SectionTitle("Shortcuts"));
            Inspector.Children.Add(Caption("Arrows nudge 1 px, Shift+arrows 10 px · Ctrl+D duplicate (a group copies as the next Slot) · Ctrl+C / Ctrl+V copy and paste · Ctrl+Shift+C / Ctrl+Shift+V copy and paste style · Ctrl+G group, Ctrl+Shift+G ungroup · Del delete · [ and ] change order · Ctrl+Z / Ctrl+Y undo and redo · Ctrl+wheel zoom · the round handle above a widget turns it · several selected widgets resize together · Shift while resizing keeps proportions · Alt turns off snapping. Changes save as you go."));
        }

        /// <summary>Shows the assigned wheel button and assigns a new one by pressing it.</summary>
        private FrameworkElement ButtonPicker(Func<string> get, Action<string> set)
        {
            var panel = new StackPanel();
            var label = new TextBlock { Text = WheelButtons.Describe(get()), TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("Brush.TextSecondary"), Margin = new Thickness(0, 0, 0, 6) };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var assign = new Button { Content = "Assign…", Margin = new Thickness(0, 0, 6, 0) };
            var clear = new Button { Content = "Clear", Style = (Style)FindResource("Btn.Ghost"), IsEnabled = !string.IsNullOrEmpty(get()) };
            System.Windows.Threading.DispatcherTimer listen = null;
            assign.Click += (s, e) =>
            {
                if (listen != null) return;
                // Wait (up to ten seconds) for a button that wasn't already held when listening started.
                Dictionary<string, uint> before = WheelButtons.Read();
                DateTime until = DateTime.UtcNow.AddSeconds(10);
                assign.Content = "Press a button…";
                listen = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
                listen.Tick += (s2, e2) =>
                {
                    Dictionary<string, uint> now = WheelButtons.Read();
                    string pressed = WheelButtons.NewlyPressed(before, now);
                    before = now;
                    if (pressed == null && DateTime.UtcNow < until) return;
                    listen.Stop();
                    listen = null;
                    assign.Content = "Assign…";
                    if (pressed == null) { StatusText.Text = "No button was pressed. Is the wheel plugged in?"; return; }
                    set(pressed);
                    label.Text = WheelButtons.Describe(pressed);
                    clear.IsEnabled = true;
                    Commit("Page button");
                };
                listen.Start();
            };
            clear.Click += (s, e) => { set(""); label.Text = WheelButtons.Describe(""); clear.IsEnabled = false; Commit("Page button"); };
            row.Children.Add(assign);
            row.Children.Add(clear);
            panel.Children.Add(label);
            panel.Children.Add(row);
            return panel;
        }

        private Grid Tip(string glyph, string title, string text)
        {
            var grid = new Grid { Margin = new Thickness(0, 4, 0, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            TextBlock icon = Glyph(glyph, 15, "Brush.Accent");
            icon.VerticalAlignment = VerticalAlignment.Top;
            icon.Margin = new Thickness(0, 2, 0, 0);
            grid.Children.Add(icon);
            var body = new StackPanel();
            body.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold });
            body.Children.Add(Caption(text));
            Grid.SetColumn(body, 1);
            grid.Children.Add(body);
            return grid;
        }

        /// <summary>
        /// For a widget drawn as a ghost: why it's faint, and a button to preview the moment it shows in, where there's one.
        /// </summary>
        private void AddGhostNotice(IEnumerable<DashWidget> widgets)
        {
            DashWidget ghost = widgets.FirstOrDefault(w => _ghosts.ContainsKey(w.Id));
            if (ghost == null) return;
            bool sample = ghost.Type == "flag" || ghost.Type == "blindspot";
            string condition = LayoutRenderer.Strip(ghost.Get("visible", "")).Trim();
            string moment = ghost.Type == "flag" ? "Yellow flag" : ghost.Type == "blindspot" ? "Cars alongside"
                : condition.Contains("OnPitRoad") || condition.Contains("PitLimiter") || condition.Contains("OverPitLimit") ? "Pit lane"
                : condition.Contains("Flag") ? "Yellow flag" : null;
            Grid tip = sample
                ? Tip("", "A sample", (ghost.Type == "flag" ? "No flag is out in this preview" : "No car is alongside in this preview") +
                    ", so it's drawn faintly to place and style. On the screen it " + (ghost.Type == "flag" ? "shows the flag that's out." : "lights while a car is alongside."))
                : Tip("", "Hidden right now", "It shows when " + (condition.Length > 80 ? condition.Substring(0, 78) + "…" : condition) +
                    ". It's drawn faintly here to place and style; Show when is under Visibility.");
            if (moment != null)
            {
                var see = new Button { Content = "Preview " + moment.ToLowerInvariant() + " ›", Style = (Style)FindResource("Btn.Ghost"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-8, 2, 0, 0) };
                see.Click += (s, e) => PreviewCombo.SelectedItem = moment;
                ((StackPanel)tip.Children[1]).Children.Add(see);
            }
            Inspector.Children.Add(new Border
            {
                Background = (Brush)FindResource("Brush.AccentSubtle"), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8, 10, 4), Margin = new Thickness(0, 10, 0, 0), Child = tip
            });
        }

        // ------------------------------------------------------------------ several selected

        private void BuildMultiInspector()
        {
            Inspector.Children.Add(Heading(_selection.Count + " widgets"));
            Inspector.Children.Add(Caption("Drag to move them together. Align and distribute below."));
            AddGhostNotice(_selection);
            if (SelectionIsOneGroup(out _)) AddModuleOptions(_selection[0]);
            Inspector.Children.Add(SectionTitle("Align"));
            Inspector.Children.Add(AlignBar(true));
            Inspector.Children.Add(SectionTitle("Distribute and match"));
            var row = new WrapPanel();
            row.Children.Add(ActionButton("", "Space horizontally", () => AlignSelection("hspace")));
            row.Children.Add(ActionButton("", "Space vertically", () => AlignSelection("vspace")));
            row.Children.Add(ActionButton("", "Same width", () => AlignSelection("width")));
            row.Children.Add(ActionButton("", "Same height", () => AlignSelection("height")));
            Inspector.Children.Add(row);
            Inspector.Children.Add(SectionTitle("Arrange"));
            var arrange = new WrapPanel();
            arrange.Children.Add(ActionButton("", "Duplicate", () => DuplicateSelection()));
            arrange.Children.Add(ActionButton("", "Delete", DeleteSelection));
            arrange.Children.Add(ActionButton("", "Save as module", SaveSelectionAsModule));
            arrange.Children.Add(ActionButton("", "Lock", ToggleLock));
            bool oneGroup = SelectionIsOneGroup(out _);
            if (!oneGroup) arrange.Children.Add(ActionButton("", "Group  Ctrl+G", GroupSelection));
            arrange.Children.Add(ActionButton("", "Repeat…", RepeatSelection));
            Inspector.Children.Add(arrange);
            Inspector.Children.Add(ScaleRow());
            AddSelectionColors(_selection);

            if (oneGroup)
            {
                DashWidget first = _selection[0];
                Inspector.Children.Add(SectionTitle("Group"));
                var groupName = new TextBox { Text = first.GroupName, MaxLength = 40 };
                groupName.TextChanged += (s, e) => { foreach (DashWidget w in _selection) w.GroupName = groupName.Text.Trim(); CommitSoon("Rename group"); RefreshLayers(); };
                Inspector.Children.Add(LabeledRow("Name", groupName));
                var slot = new TextBox { Text = first.Slot.ToString("0.##", CultureInfo.InvariantCulture), FontFamily = (FontFamily)FindResource("Font.Mono"), FontSize = 12, Width = 70, HorizontalAlignment = HorizontalAlignment.Left };
                slot.TextChanged += (s, e) =>
                {
                    if (!double.TryParse(slot.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) return;
                    foreach (DashWidget w in _selection) w.Slot = v;
                    RequestRender();
                    CommitSoon("Group slot");
                };
                AttachWheel(slot, 1, v => slot.Text = v.ToString("0.##", CultureInfo.InvariantCulture));
                Inspector.Children.Add(LabeledRow("Slot", slot));
                Inspector.Children.Add(Caption("Formulas in this group read this number as Slot, for example standing(Slot, \"Name\") for a driver card. " +
                    "Ctrl+D copies the group just below with the next Slot: build P1 once, then copy it for P2, P3 and on. Double-click a widget to edit it alone.", 6));
                var groupActions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
                groupActions.Children.Add(ActionButton("", "Copy as next Slot", () => DuplicateSelection()));
                groupActions.Children.Add(ActionButton("", "Ungroup", Ungroup));
                Inspector.Children.Add(groupActions);
            }

            // Settings every selected widget has, edited together.
            List<WidgetType> types = _selection.Select(w => WidgetCatalog.Find(w.Type)).Where(t => t != null).Distinct().ToList();
            if (types.Count > 0 && types.Count == _selection.Select(w => w.Type).Distinct().Count())
            {
                List<PropDef> common = types[0].Props.Where(p => p.Key != "visible" && !p.Hidden && types.All(t => t.Prop(p.Key) is PropDef other && other.Kind == p.Kind)).ToList();
                if (common.Count > 0)
                {
                    Inspector.Children.Add(SectionTitle(types.Count == 1 ? "Settings (all " + _selection.Count + ")" : "Shared settings"));
                    Inspector.Children.Add(Caption("Changes here apply to every selected widget.", 0));
                    foreach (IGrouping<string, PropDef> group in common.GroupBy(p => p.Group))
                    {
                        var content = new StackPanel { Visibility = Visibility.Collapsed };
                        var title = new TextBlock { Text = group.Key.ToUpperInvariant() + "  ›", Style = (Style)FindResource("T.Overline") };
                        var header = new Border { Child = title, Background = Brushes.Transparent, Cursor = Cursors.Hand, Margin = new Thickness(0, 14, 0, 6) };
                        header.MouseLeftButtonUp += (s, e) =>
                        {
                            bool open = content.Visibility != Visibility.Visible;
                            // Rows are built when a group first opens; most multi-selections only need one or two.
                            if (open && content.Children.Count == 0)
                                foreach (PropDef def in group) content.Children.Add(PropertyRow(_selection[0], def, _selection.ToList()));
                            content.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
                            title.Text = group.Key.ToUpperInvariant() + (open ? "  ˅" : "  ›");
                            if (open) Motion.FadeIn(content);
                        };
                        Inspector.Children.Add(header);
                        Inspector.Children.Add(content);
                    }
                }
            }

            // Shared show-when for the whole group.
            Inspector.Children.Add(SectionTitle("Show when (all selected)"));
            string shared = _selection.Select(w => w.Get("visible", "")).Distinct().Count() == 1 ? _selection[0].Get("visible", "") : "";
            var visible = new FormulaBox(this, LayoutRenderer.Strip(shared), CurrentScope, "e.g. OnPitRoad");
            visible.Changed += text =>
            {
                foreach (DashWidget w in _selection) w.Set("visible", text.Trim().Length == 0 ? "" : "=" + text.Trim());
                RequestRender();
                CommitSoon("Show when");
            };
            _liveFormulas.Add(visible);
            Inspector.Children.Add(visible);
        }

        private Button ActionButton(string glyph, string text, Action action)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(Glyph(glyph, 12));
            content.Children.Add(new TextBlock { Text = text, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            var button = new Button { Content = content, Margin = new Thickness(0, 0, 6, 6), Height = 32, Padding = new Thickness(10, 0, 10, 0) };
            button.Click += (s, e) => action();
            return button;
        }

        private UniformGrid AlignBar(bool multi)
        {
            var bar = new UniformGrid { Columns = 6, Margin = new Thickness(0, 0, 0, 4) };
            (string Glyph, string How, string Tip)[] items =
            {
                ("", "left", "Align left"), ("", "center", "Center horizontally"), ("", "right", "Align right"),
                ("", "top", "Align top"), ("", "middle", "Center vertically"), ("", "bottom", "Align bottom")
            };
            foreach (var item in items)
            {
                var button = new Button
                {
                    Style = (Style)FindResource("Btn.Ghost"), Height = 30, Padding = new Thickness(0), Margin = new Thickness(0, 0, 4, 0),
                    ToolTip = item.Tip + (multi ? "" : " on the screen"), Content = AlignGlyph(item.How)
                };
                string how = item.How;
                button.Click += (s, e) => AlignSelection(how);
                bar.Children.Add(button);
            }
            return bar;
        }

        /// <summary>Small drawn align icons, so they read clearly whatever icon font is installed.</summary>
        private FrameworkElement AlignGlyph(string how)
        {
            var canvas = new Canvas { Width = 16, Height = 16 };
            Brush line = (Brush)FindResource("Brush.Accent"), block = (Brush)FindResource("Brush.TextSecondary");
            void Rect(double x, double y, double w, double h, Brush b) { var r = new System.Windows.Shapes.Rectangle { Width = w, Height = h, Fill = b, RadiusX = 1, RadiusY = 1 }; Canvas.SetLeft(r, x); Canvas.SetTop(r, y); canvas.Children.Add(r); }
            switch (how)
            {
                case "left": Rect(1, 1, 1.5, 14, line); Rect(4, 3, 10, 4, block); Rect(4, 9, 6, 4, block); break;
                case "center": Rect(7.25, 1, 1.5, 14, line); Rect(3, 3, 10, 4, block); Rect(5, 9, 6, 4, block); break;
                case "right": Rect(13.5, 1, 1.5, 14, line); Rect(2, 3, 10, 4, block); Rect(6, 9, 6, 4, block); break;
                case "top": Rect(1, 1, 14, 1.5, line); Rect(3, 4, 4, 10, block); Rect(9, 4, 4, 6, block); break;
                case "middle": Rect(1, 7.25, 14, 1.5, line); Rect(3, 3, 4, 10, block); Rect(9, 5, 4, 6, block); break;
                default: Rect(1, 13.5, 14, 1.5, line); Rect(3, 2, 4, 10, block); Rect(9, 6, 4, 6, block); break;
            }
            return canvas;
        }

        // ------------------------------------------------------------------ one widget

        private void BuildWidgetInspector(DashWidget widget)
        {
            WidgetType type = WidgetCatalog.Find(widget.Type);
            if (type == null)
            {
                Inspector.Children.Add(Heading("Unknown widget"));
                Inspector.Children.Add(Caption("This widget type (" + widget.Type + ") is not available in this version."));
                return;
            }

            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var badge = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(7), Background = (Brush)FindResource("Brush.AccentSubtle"), Child = Glyph(type.Icon, 14, "Brush.Accent") };
            ((TextBlock)badge.Child).HorizontalAlignment = HorizontalAlignment.Center;
            header.Children.Add(badge);
            var name = new TextBox { Text = widget.Name ?? type.Name, FontWeight = FontWeights.SemiBold, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "Layer name" };
            name.TextChanged += (s, e) => { widget.Name = name.Text; CommitSoon("Rename"); };
            name.LostKeyboardFocus += (s, e) => RefreshLayers();
            Grid.SetColumn(name, 1);
            header.Children.Add(name);
            var more = new Button { Style = (Style)FindResource("Btn.Ghost"), Width = 30, Height = 30, Padding = new Thickness(0), Margin = new Thickness(6, 0, 0, 0), Content = Glyph("", 13), ToolTip = "More" };
            more.Click += (s, e) =>
            {
                var menu = new ContextMenu { PlacementTarget = more, Placement = PlacementMode.Bottom };
                menu.Items.Add(MenuItem("Duplicate", "Ctrl+D", () => DuplicateSelection()));
                menu.Items.Add(MenuItem("Repeat…", "Ctrl+Shift+D", RepeatSelection));
                menu.Items.Add(MenuItem("Reset to defaults", null, () => { widget.Props.Clear(); Commit("Reset widget"); RefreshInspector(); RequestRender(); }));
                menu.Items.Add(MenuItem("Save as module…", "Ctrl+M", SaveSelectionAsModule));
                menu.Items.Add(MenuItem(widget.Locked ? "Unlock" : "Lock", "Ctrl+L", ToggleLock));
                menu.Items.Add(MenuItem("Delete", "Del", DeleteSelection));
                menu.IsOpen = true;
            };
            Grid.SetColumn(more, 2);
            header.Children.Add(more);
            Inspector.Children.Add(header);
            Inspector.Children.Add(Caption(type.Description, 8));
            if (!string.IsNullOrEmpty(widget.Group))
                Inspector.Children.Add(Caption("In " + widget.GroupName + (widget.Slot != 0 ? " (Slot " + widget.Slot.ToString("0.##", CultureInfo.InvariantCulture) + ")" : "") +
                    ". Click the group on the canvas to select all of it.", 6));
            AddGhostNotice(new[] { widget });
            AddModuleOptions(widget);
            // A tap zone, or a widget that shows on one of its views: flip the preview through the views here,
            // as a tap on a touch screen would.
            string tapName = widget.Get("tapName", "")?.Trim();
            if (string.IsNullOrEmpty(tapName))
            {
                var used = System.Text.RegularExpressions.Regex.Match(widget.Get("visible", "") ?? "", "tap\\(\\s*\"([^\"]+)\"");
                if (used.Success) tapName = used.Groups[1].Value.Trim();
            }
            if (!string.IsNullOrEmpty(tapName))
            {
                DashWidget zone = _layout.AllWidgets().FirstOrDefault(w => string.Equals(w.Get("tapName", "")?.Trim(), tapName, StringComparison.OrdinalIgnoreCase));
                int views = zone != null ? LayoutRenderer.TapViews(zone) : 2;
                _designTaps.TryGetValue(tapName, out int view);
                var next = new Button { Content = "Tap \"" + tapName + "\": showing view " + (view % views + 1) + " of " + views + "  ·  Next ›", Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
                next.Click += (s, e) => { _designTaps[tapName] = (view + 1) % views; RequestRender(); RefreshInspector(); };
                Inspector.Children.Add(next);
            }
            if (_renderer != null && _renderer.Errors.TryGetValue(widget.Id, out string error))
                Inspector.Children.Add(new TextBlock { Text = "⚠ " + error, Foreground = (Brush)FindResource("Brush.Danger"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });

            // Position and size
            Inspector.Children.Add(SectionTitle("Position and size"));
            var position = new UniformGrid { Columns = 4 };
            foreach (string axis in new[] { "X", "Y", "W", "H" })
            {
                var cell = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
                cell.Children.Add(new TextBlock { Text = axis, Style = (Style)FindResource("T.Caption"), Margin = new Thickness(2, 0, 0, 2) });
                var box = new TextBox { Text = Axis(widget, axis).ToString("0.##", CultureInfo.InvariantCulture), FontFamily = (FontFamily)FindResource("Font.Mono"), FontSize = 12 };
                string a = axis;
                box.TextChanged += (s, e) =>
                {
                    if (!double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) return;
                    SetAxis(widget, a, v);
                    RequestRender();
                    CommitSoon("Position");
                };
                AttachWheel(box, 1, v => { box.Text = v.ToString("0.##", CultureInfo.InvariantCulture); });
                _positionBoxes[axis] = box;
                cell.Children.Add(box);
                position.Children.Add(cell);
            }
            Inspector.Children.Add(position);
            Inspector.Children.Add(new TextBlock { Text = "Align on screen", Style = (Style)FindResource("T.Caption"), Margin = new Thickness(0, 10, 0, 4) });
            Inspector.Children.Add(AlignBar(false));
            // Paths are reshaped on the canvas; shapes can become paths to reshape.
            if (widget.Type == "path")
                Inspector.Children.Add(ActionButton("\uE70F", "Edit points on the canvas", () => BeginEditPath(widget)));
            else if (widget.Type == "shape" && PathNodes.FromShape(widget.Get("shape", "Rectangle"), 6, 0.5f, out _) != null)
                Inspector.Children.Add(ActionButton("\uE70F", "Convert to path, to reshape point by point", () => ConvertToPath(widget)));
            Inspector.Children.Add(ActionButton("\uE8EE", "Repeat in rows or around a circle\u2026", RepeatSelection));

            // Properties by group, in definition order; visibility last.
            foreach (IGrouping<string, PropDef> group in type.Props.Where(p => !p.Hidden).GroupBy(p => p.Group))
            {
                string groupName = group.Key;
                bool collapsed = CollapsedGroups.Contains(groupName);
                var content = new StackPanel { Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible };
                var chevron = Glyph(collapsed ? "" : "", 10, "Brush.TextTertiary");
                var title = new StackPanel { Orientation = Orientation.Horizontal };
                title.Children.Add(new TextBlock { Text = groupName.ToUpperInvariant(), Style = (Style)FindResource("T.Overline") });
                chevron.Margin = new Thickness(8, 0, 0, 0);
                title.Children.Add(chevron);
                int formulas = group.Count(p => LayoutRenderer.IsFormula(widget.Get(p.Key, p.Default), p) && p.Kind != PropKind.Formula);
                if (formulas > 0)
                    title.Children.Add(new TextBlock { Text = "  ƒ" + formulas, FontStyle = FontStyles.Italic, FontSize = 12, Foreground = (Brush)FindResource("Brush.Accent"), VerticalAlignment = VerticalAlignment.Center });
                var headerButton = new Border { Child = title, Background = Brushes.Transparent, Cursor = Cursors.Hand, Margin = new Thickness(0, 18, 0, 8) };
                headerButton.MouseLeftButtonUp += (s, e) =>
                {
                    bool nowCollapsed = content.Visibility == Visibility.Visible;
                    content.Visibility = nowCollapsed ? Visibility.Collapsed : Visibility.Visible;
                    if (!nowCollapsed) Motion.FadeIn(content);
                    chevron.Text = nowCollapsed ? "" : "";
                    if (nowCollapsed) CollapsedGroups.Add(groupName); else CollapsedGroups.Remove(groupName);
                };
                Inspector.Children.Add(headerButton);
                foreach (PropDef def in group)
                {
                    FrameworkElement row = PropertyRow(widget, def);
                    _propRows[def.Key] = row;
                    content.Children.Add(row);
                }
                Inspector.Children.Add(content);
                _groupHeaders[groupName] = headerButton;
            }
            UpdateApplicableRows(widget, type);
        }

        private readonly Dictionary<string, FrameworkElement> _propRows = new Dictionary<string, FrameworkElement>();
        private readonly Dictionary<string, FrameworkElement> _groupHeaders = new Dictionary<string, FrameworkElement>();

        /// <summary>Hides settings that don't matter as the widget is set up (and groups left empty), without rebuilding the panel.</summary>
        private void UpdateApplicableRows(DashWidget widget, WidgetType type)
        {
            foreach (PropDef def in type.Props)
                if (_propRows.TryGetValue(def.Key, out FrameworkElement row))
                    row.Visibility = def.Applies(widget, type) ? Visibility.Visible : Visibility.Collapsed;
            foreach (IGrouping<string, PropDef> group in type.Props.GroupBy(p => p.Group))
                if (_groupHeaders.TryGetValue(group.Key, out FrameworkElement header))
                    header.Visibility = group.Any(p => p.Applies(widget, type)) ? Visibility.Visible : Visibility.Collapsed;
        }

        private static double Axis(DashWidget w, string axis) => axis == "X" ? w.X : axis == "Y" ? w.Y : axis == "W" ? w.W : w.H;

        private static void SetAxis(DashWidget w, string axis, double value)
        {
            switch (axis)
            {
                case "X": w.X = value; break;
                case "Y": w.Y = value; break;
                case "W": w.W = Math.Max(2, value); break;
                default: w.H = Math.Max(2, value); break;
            }
        }

        private void RefreshPositionFields()
        {
            if (_selection.Count != 1 || _positionBoxes.Count == 0) return;
            foreach (KeyValuePair<string, TextBox> pair in _positionBoxes)
            {
                string text = Axis(_selection[0], pair.Key).ToString("0.##", CultureInfo.InvariantCulture);
                if (pair.Value.Text != text && !pair.Value.IsKeyboardFocusWithin) pair.Value.Text = text;
            }
        }

        private void FocusPrimaryEditor()
        {
            if (_primaryEditor == null) return;
            _primaryEditor.Focus();
            if (_primaryEditor is TextBox box) box.SelectAll();
            _primaryEditor.BringIntoView();
        }

        // ------------------------------------------------------------------ property rows

        /// <summary>
        /// One setting's row. With <paramref name="together"/> (several selected widgets) it shows their
        /// shared value, or "mixed" when they differ, and an edit applies to all of them.
        /// </summary>
        private FrameworkElement PropertyRow(DashWidget widget, PropDef def, IList<DashWidget> together = null)
        {
            IList<DashWidget> targets = together ?? new[] { widget };
            string raw = widget.Get(def.Key, def.Default) ?? "";
            bool mixed = targets.Any(t => (t.Get(def.Key, def.Default) ?? "") != raw);
            if (mixed && !raw.StartsWith("=")) raw = def.Default ?? "";
            bool alwaysFormula = def.Kind == PropKind.Formula;
            bool isFormula = alwaysFormula || raw.StartsWith("=");

            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            var label = new TextBlock
            {
                Text = mixed ? def.Label + " (mixed)" : def.Label, VerticalAlignment = isFormula ? VerticalAlignment.Top : VerticalAlignment.Center, Margin = new Thickness(0, isFormula ? 7 : 0, 8, 0),
                Foreground = (Brush)FindResource(raw == def.Default ? "Brush.TextSecondary" : "Brush.Text"), TextWrapping = TextWrapping.Wrap,
                ToolTip = def.Help
            };
            // Right-click a setting's name to make it a module option, or to link it to one.
            if (together == null && (label.ContextMenu = OptionMenu(widget, def)) != null) label.Background = Brushes.Transparent;
            row.Children.Add(label);

            void Apply(string value, bool structural = false)
            {
                foreach (DashWidget target in targets) target.Set(def.Key, value);
                if (structural) _rendererDirty = true;
                WidgetType owner = WidgetCatalog.Find(widget.Type);
                if (together == null && owner != null && owner.Props.Any(p => p.DependsOn == def.Key)) UpdateApplicableRows(widget, owner);
                label.Text = def.Label;
                label.Foreground = (Brush)FindResource(value == def.Default ? "Brush.TextSecondary" : "Brush.Text");
                RequestRender();
                CommitSoon("Edit " + def.Label.ToLowerInvariant());
            }

            FrameworkElement editor;
            if (isFormula)
            {
                // Formula results shown in the box use the widget's own Slot when it's in a numbered group.
                var box = new FormulaBox(this, LayoutRenderer.Strip(raw), () => LayoutRenderer.ForWidget(CurrentScope(), widget), def.Help);
                box.Changed += text => Apply(text.Trim().Length == 0 ? (alwaysFormula ? "" : def.Default) : "=" + text.Trim());
                _liveFormulas.Add(box);
                editor = box;
                if (_primaryEditor == null && (def.Key == "text" || def.Key == "value" || def.Key == "label")) _primaryEditor = box.Text;
            }
            else
            {
                editor = LiteralEditor(def, raw, Apply);
            }
            if (isFormula)
            {
                // Formulas get the full width on their own line under the label.
                row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                label.Margin = new Thickness(0, 4, 8, 4);
                Grid.SetColumnSpan(label, 2);
                Grid.SetRow(editor, 1);
                Grid.SetColumnSpan(editor, 3);
            }
            else Grid.SetColumn(editor, 1);
            row.Children.Add(editor);

            if (!alwaysFormula && def.Kind != PropKind.Image)
            {
                var fx = new ToggleButton
                {
                    Content = new TextBlock { Text = "ƒx", FontStyle = FontStyles.Italic, FontSize = 12, FontWeight = FontWeights.SemiBold }, IsChecked = isFormula, Width = 28, Height = 26,
                    Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Style = (Style)FindResource("Fx"),
                    ToolTip = isFormula ? "Back to a fixed value" : "Drive this with live data (a formula)"
                };
                fx.Click += (s, e) =>
                {
                    string current = widget.Get(def.Key, def.Default) ?? "";
                    Apply(fx.IsChecked == true ? "=" + ToFormula(def, current) : FromFormula(def, current));
                    Commit("Formula " + def.Label.ToLowerInvariant());
                    RefreshInspector();
                };
                Grid.SetColumn(fx, 2);
                row.Children.Add(fx);
            }
            return row;
        }

        /// <summary>Turns a fixed value into the equivalent formula.</summary>
        private static string ToFormula(PropDef def, string literal)
        {
            switch (def.Kind)
            {
                case PropKind.Number: return string.IsNullOrWhiteSpace(literal) ? "0" : literal;
                case PropKind.Bool: return Expr.Bool(literal) ? "true" : "false";
                case PropKind.Color:
                    if (string.IsNullOrWhiteSpace(literal)) return "$accent";
                    return literal.StartsWith("#") ? literal : "$" + literal;
                default:
                    return "\"" + (literal ?? "").Replace("\"", "'") + "\"";
            }
        }

        /// <summary>Turns a formula back into a fixed value: its current result where that makes sense.</summary>
        private string FromFormula(PropDef def, string formula)
        {
            object value = Expr.Evaluate(LayoutRenderer.Strip(formula), CurrentScope());
            switch (def.Kind)
            {
                case PropKind.Color:
                    System.Drawing.Color? c = Expr.Color(value, CurrentScope());
                    return c.HasValue ? (c.Value.A < 255 ? "#" + c.Value.A.ToString("X2") + SettingsStore.ToHex(c.Value).Substring(1) : SettingsStore.ToHex(c.Value)) : def.Default;
                case PropKind.Choice:
                    string choice = Expr.Text(value);
                    return def.Choices.Contains(choice) ? choice : def.Default;
                case PropKind.Bool: return Expr.Bool(value) ? "true" : "false";
                default:
                    string text = Expr.Text(value);
                    return text.Length == 0 ? def.Default : text;
            }
        }

        private FrameworkElement LiteralEditor(PropDef def, string raw, Action<string, bool> apply)
        {
            switch (def.Kind)
            {
                case PropKind.Number:
                {
                    var box = new TextBox { Text = raw, FontFamily = (FontFamily)FindResource("Font.Mono"), FontSize = 12, Width = 70, HorizontalAlignment = HorizontalAlignment.Left };
                    double step = def.Step > 0 ? def.Step : 1;
                    bool ranged = !double.IsNaN(def.Min) && !double.IsNaN(def.Max) && def.Max > def.Min;
                    Slider slider = null;
                    bool syncing = false;
                    box.TextChanged += (s, e) =>
                    {
                        if (!double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) return;
                        if (slider != null && !syncing) { syncing = true; slider.Value = Math.Max(def.Min, Math.Min(def.Max, v)); syncing = false; }
                        apply(box.Text.Trim(), false);
                    };
                    AttachWheel(box, step, v =>
                    {
                        if (ranged) v = Math.Max(def.Min, Math.Min(def.Max, v));
                        box.Text = v.ToString("0.###", CultureInfo.InvariantCulture);
                    });
                    if (!ranged) return box;
                    double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double initial);
                    slider = new Slider { Minimum = def.Min, Maximum = def.Max, Value = Math.Max(def.Min, Math.Min(def.Max, initial)), SmallChange = step, LargeChange = step * 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
                    slider.ValueChanged += (s, e) =>
                    {
                        if (syncing) return;
                        syncing = true;
                        double v = Math.Round(slider.Value / step) * step;
                        box.Text = v.ToString("0.###", CultureInfo.InvariantCulture);
                        syncing = false;
                    };
                    var grid = new Grid();
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    grid.ColumnDefinitions.Add(new ColumnDefinition());
                    grid.Children.Add(box);
                    Grid.SetColumn(slider, 1);
                    grid.Children.Add(slider);
                    return grid;
                }
                case PropKind.Color:
                {
                    bool optional = string.IsNullOrEmpty(def.Default);
                    var field = new ColorField(this, raw, _layout, () => _dashboard.AccentColor, true, optional);
                    field.Changed += v => apply(v, false);
                    return field;
                }
                case PropKind.Bool:
                {
                    var toggle = new CheckBox { Style = (Style)FindResource("Switch"), IsChecked = Expr.Bool(raw), VerticalAlignment = VerticalAlignment.Center };
                    toggle.Click += (s, e) => { apply(toggle.IsChecked == true ? "true" : "false", false); Commit("Edit " + def.Label.ToLowerInvariant()); };
                    return toggle;
                }
                case PropKind.Choice:
                {
                    var combo = new ComboBox { ItemsSource = def.Choices, SelectedItem = def.Choices.Contains(raw) ? raw : def.Default };
                    combo.SelectionChanged += (s, e) =>
                    {
                        apply((string)combo.SelectedItem, false);
                        Commit("Edit " + def.Label.ToLowerInvariant());
                    };
                    return combo;
                }
                case PropKind.Font:
                {
                    ComboBox combo = FontCombo(raw, true);
                    combo.SelectionChanged += (s, e) => { if (combo.SelectedItem is string f) { apply(f, false); Commit("Font"); } };
                    return combo;
                }
                case PropKind.Image:
                    return ImageEditor(raw, apply);
                default:
                {
                    var box = new TextBox { Text = raw };
                    box.TextChanged += (s, e) => apply(box.Text, false);
                    if (_primaryEditor == null && (def.Key == "text" || def.Key == "label" || def.Key == "value")) _primaryEditor = box;
                    return box;
                }
            }
        }

        private FrameworkElement ImageEditor(string raw, Action<string, bool> apply)
        {
            var panel = new StackPanel();
            if (!string.IsNullOrEmpty(raw) && _layout.Images.TryGetValue(raw, out string base64))
            {
                try
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.StreamSource = new MemoryStream(Convert.FromBase64String(base64));
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.DecodePixelWidth = 180;
                    image.EndInit();
                    panel.Children.Add(new Border
                    {
                        Background = ColorField.Checker(Colors.Transparent), CornerRadius = new CornerRadius(8), Padding = new Thickness(6), Margin = new Thickness(0, 0, 0, 6),
                        Child = new Image { Source = image, MaxHeight = 110, Stretch = Stretch.Uniform }
                    });
                }
                catch { }
            }
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            var choose = new Button { Content = string.IsNullOrEmpty(raw) ? "Choose image…" : "Replace…", Margin = new Thickness(0, 0, 6, 0) };
            choose.Click += (s, e) =>
            {
                var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Choose an image", Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif" };
                if (dialog.ShowDialog(this) != true) return;
                try
                {
                    string id = EmbedImage(dialog.FileName);
                    apply(id, true);
                    Commit("Image");
                    RefreshInspector();
                }
                catch (Exception exception)
                {
                    StatusText.Text = "Could not use that image: " + exception.Message;
                }
            };
            buttons.Children.Add(choose);
            if (!string.IsNullOrEmpty(raw))
            {
                var clear = new Button { Content = "Remove", Style = (Style)FindResource("Btn.Ghost") };
                clear.Click += (s, e) => { apply("", true); Commit("Image"); RefreshInspector(); };
                buttons.Children.Add(clear);
            }
            panel.Children.Add(buttons);
            panel.Children.Add(Caption("Stored inside the design, so exported files include it. Large photos are scaled to fit the screen.", 6));
            return panel;
        }

        /// <summary>Embeds an image as PNG, scaled down to at most the screen size so designs stay small.</summary>
        private string EmbedImage(string path)
        {
            using (var source = new System.Drawing.Bitmap(path))
            {
                double scale = Math.Min(1, Math.Min(CanvasW * 1.5 / source.Width, CanvasH * 1.5 / source.Height));
                int w = Math.Max(1, (int)(source.Width * scale)), h = Math.Max(1, (int)(source.Height * scale));
                using (var resized = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    using (var g = System.Drawing.Graphics.FromImage(resized))
                    {
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.DrawImage(source, 0, 0, w, h);
                    }
                    using (var stream = new MemoryStream())
                    {
                        resized.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                        string id = "img-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                        _layout.Images[id] = Convert.ToBase64String(stream.ToArray());
                        return id;
                    }
                }
            }
        }

        /// <summary>Mouse wheel over a number box steps its value (Shift for ten steps).</summary>
        private static void AttachWheel(TextBox box, double step, Action<double> set)
        {
            box.PreviewMouseWheel += (s, e) =>
            {
                if (!box.IsKeyboardFocusWithin) return;
                if (!double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) v = 0;
                double amount = step * ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 10 : 1);
                set(Math.Round((v + (e.Delta > 0 ? amount : -amount)) / step) * step);
                e.Handled = true;
            };
        }
    }
}
