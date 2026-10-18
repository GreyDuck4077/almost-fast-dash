using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AlmostFastDash.Designer
{
    public partial class DesignerWindow
    {
        // ------------------------------------------------------------------ nothing selected: the screen

        private void BuildScreenInspector()
        {
            Inspector.Children.Add(Heading("Screen: " + _screen.Name));
            Inspector.Children.Add(Caption("Nothing selected. Click a widget, or drag one in from the Library."));

            Inspector.Children.Add(SectionTitle("Screen"));
            var name = new TextBox { Text = _screen.Name, MaxLength = 40 };
            name.TextChanged += (s, e) => { if (name.Text.Trim().Length > 0) { _screen.Name = name.Text.Trim(); ScreenEdited(); } };
            Inspector.Children.Add(LabeledRow("Name", name));

            Inspector.Children.Add(new TextBlock { Text = "Show when", Foreground = (Brush)FindResource("Brush.TextSecondary"), Margin = new Thickness(0, 10, 0, 4) });
            var condition = new FormulaBox(this, _screen.Condition, CurrentScope, "e.g. OnPitRoad");
            condition.Changed += text => { _screen.Condition = text.Trim(); ScreenEdited(); };
            _liveFormulas.Add(condition);
            Inspector.Children.Add(condition);
            Inspector.Children.Add(Caption("Empty for your main screen. With a condition, this screen takes over while it's true.", 6));

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
                ? "Change it to match your display; everything scales to fit."
                : wheel.Value == current
                    ? "Matches your wheel screen."
                    : "Your wheel screen is " + wheel.Value.Width + " × " + wheel.Value.Height + ". This dashboard fits it without stretching.", 6));
            resize.Click += (s, e) =>
            {
                System.Drawing.Size? chosen = ScreenSizePicker.Ask(this, "Display resolution",
                    "Every screen of this dashboard is scaled to the new size. Undo puts it back.",
                    wheel ?? current, "Resize");
                if (chosen == null || chosen.Value == current) return;
                _layout.ScaleTo(chosen.Value.Width, chosen.Value.Height);
                Commit("Resize to " + chosen.Value.Width + " × " + chosen.Value.Height);
                RefreshInspector();
            };

            Inspector.Children.Add(SectionTitle("Pages"));
            int pages = _layout.Screens.Count(s => string.IsNullOrWhiteSpace(s.Condition));
            Inspector.Children.Add(Caption("Screens without a Show-when condition are pages (" + pages + " here). Pick wheel buttons to step through them."));
            Inspector.Children.Add(LabeledRow("Next page", ButtonPicker(() => _layout.PageButton, v => _layout.PageButton = v)));
            Inspector.Children.Add(LabeledRow("Previous page", ButtonPicker(() => _layout.PageBackButton, v => _layout.PageBackButton = v)));

            Inspector.Children.Add(SectionTitle("Quick start"));
            Inspector.Children.Add(Tip("", "Ready-made blocks", "Cards and clusters in the Library are the fastest start."));
            Inspector.Children.Add(Tip("", "Theme first", "Pick a preset under Theme to restyle everything that uses its colors."));
            Inspector.Children.Add(Tip("", "Make it live", "Click ƒx beside any property to drive it with live data."));
            Inspector.Children.Add(Tip("", "Test it", "Preview the animated lap, a flag or your live session."));

            Inspector.Children.Add(SectionTitle("Shortcuts"));
            var sheet = new Button
            {
                Content = "Show every shortcut (Ctrl+/)", Style = (Style)FindResource("Btn.Ghost"), Foreground = (Brush)FindResource("Brush.Accent"),
                HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(0, 2, 0, 2)
            };
            sheet.Click += (s, e) => ShowShortcuts();
            Inspector.Children.Add(sheet);
        }

        /// <summary>Shows the assigned wheel button and assigns a new one by pressing it.</summary>
        private FrameworkElement ButtonPicker(Func<string> get, Action<string> set)
        {
            // One line: Assign, the button it's set to, and Clear once there's something to clear.
            var row = new DockPanel();
            var label = new TextBlock
            {
                Text = WheelButtons.Describe(get()), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("Brush.TextSecondary")
            };
            label.ToolTip = label.Text;
            var assign = new Button { Content = "Assign…", Margin = new Thickness(0, 0, 10, 0) };
            var clear = new Button
            {
                Content = "Clear", Style = (Style)FindResource("Btn.Ghost"), Margin = new Thickness(6, 0, 0, 0),
                Visibility = string.IsNullOrEmpty(get()) ? Visibility.Collapsed : Visibility.Visible
            };
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
                    label.ToolTip = label.Text;
                    clear.Visibility = Visibility.Visible;
                    Commit("Page button");
                };
                listen.Start();
            };
            clear.Click += (s, e) =>
            {
                set("");
                label.Text = WheelButtons.Describe("");
                label.ToolTip = label.Text;
                clear.Visibility = Visibility.Collapsed;
                Commit("Page button");
            };
            DockPanel.SetDock(assign, Dock.Left);
            DockPanel.SetDock(clear, Dock.Right);
            row.Children.Add(assign);
            row.Children.Add(clear);
            row.Children.Add(label);
            return row;
        }
    }
}
