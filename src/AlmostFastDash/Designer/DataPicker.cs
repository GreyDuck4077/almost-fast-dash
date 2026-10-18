using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace AlmostFastDash.Designer
{
    /// <summary>The searchable data, function and recipe picker behind the formula Insert button.</summary>
    internal static class DataPicker
    {
        internal static readonly (string Name, string Formula, string Description)[] Recipes =
        {
            ("Speed in your unit", "format(Speed, \"0\")", "Whole-number speed in MPH or km/h."),
            ("Lap time", "laptime(LastLap)", "Any lap value as 1:27.932."),
            ("Delta with sign", "signed(Delta, 2)", "+0.46 / -0.12."),
            ("Delta color", "deltacolor(Delta)", "Green when gaining, red when losing."),
            ("Color by thresholds", "steps(FuelLapsLeft, $bad, 2, $warn, 5, $text)",
            "Red under 2 laps, amber under 5, otherwise text color."),
            ("Flash a warning", "FuelLapsLeft >= 0 && FuelLapsLeft < 2 && blink(300)", "Use in Show when to blink a widget."),
            ("Only on pit road", "OnPitRoad", "Use in Show when."),
            ("Only while racing", "!OnPitRoad && !FlagActive", "Hide during pit lane and flags."),
            ("Position text", "Position > 0 ? \"P\" + Position : \"P--\"", "P4, or P-- when unknown."),
            ("Fuel to add", "FuelToFinish < 0 ? \"--.-\" : FuelToFinish <= 0.05 ? \"NONE\" : \"+\" + fuel(FuelToFinish)",
            "How much to add to finish."),
            ("Shift light color", "ShiftFlash ? $bad : steps(ShiftPct, $good, 0.5, $warn, 0.8, $bad)",
            "For bars or text that follow the revs."),
            ("Gap to car ahead", "AheadName != \"---\" ? format(AheadGap, \"0.0\") : \"--\"", "Seconds, or -- when nobody is ahead."),
            ("Spotter call", "CarLeft && CarRight ? \"THREE WIDE\" : CarLeft ? \"CAR LEFT\" : CarRight ? \"CAR RIGHT\" : \"CLEAR\"",
            "Text for an oval spotter."),
            ("Clock", "time(\"HH:mm\")", "The PC's time."),
            ("Fade a color", "alpha($accent, 0.3)", "30% opacity version of a color."),
        };

        public static void Open(FrameworkElement owner, UIElement anchor, Func<EvalScope> scope, Action<string, bool> insert)
        {
            var search = new TextBox { Margin = new Thickness(12, 12, 12, 8) };
            var tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 12, 8) };
            var list = new StackPanel();
            var scroller = new ScrollViewer { Content = list, Height = 380, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(6, 0, 6, 8) };
            var root = new DockPanel();
            DockPanel.SetDock(search, Dock.Top);
            DockPanel.SetDock(tabs, Dock.Top);
            root.Children.Add(search);
            root.Children.Add(tabs);
            var reference = new Button
            {
                Content = "Open the full formula reference (F1)", Style = (Style)owner.FindResource("Btn.Ghost"),
                    HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(10, 0, 10, 10), ToolTip = "Every field and function, with examples (F1)"
            };
            DockPanel.SetDock(reference, Dock.Bottom);
            root.Children.Add(reference);
            root.Children.Add(scroller);
            var popup = new Popup
            {
                PlacementTarget = anchor, Placement = PlacementMode.Left, StaysOpen = false, AllowsTransparency = true,
                Child = new Border
                {
                    Width = 440, Background = (Brush)owner.FindResource("Brush.Popup"),
                        BorderBrush = (Brush)owner.FindResource("Brush.BorderStrong"),
                    BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Child = root,
                    Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 28, ShadowDepth = 4, Opacity = 0.55 }
                }
            };

            string mode = "Data";
            EvalScope live = scope();
            void Fill()
            {
                list.Children.Clear();
                string f = search.Text.Trim();
                bool Match(params string[] parts) => f.Length == 0 ||
                    parts.Any(p => (p ?? "").IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0);
                if (mode == "Data")
                {
                    foreach (string category in DataCatalog.Categories)
                    {
                        List<DataField> fields = DataCatalog.InCategory(category).Where(d => Match(d.Name, d.Description, category))
                            .ToList();
                        if (fields.Count == 0) continue;
                        list.Children.Add(new TextBlock { Text = category.ToUpperInvariant(),
                            Style = (Style)owner.FindResource("T.Overline"), Margin = new Thickness(8, 10, 0, 4) });
                        foreach (DataField field in fields)
                        {
                            string value;
                            try { value = Expr.Text(field.Get(live)); } catch { value = ""; }
                            list.Children.Add(Item(owner, field.Name, field.Description, value, () =>
                            {
                                bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
                                insert(shift ? field.Suggested : field.Name, false);
                                popup.IsOpen = false;
                            }, "Click to insert the name. Shift+click inserts a ready-formatted formula:\n" + field.Suggested));
                        }
                    }
                }
                else if (mode == "Functions")
                {
                    foreach (FunctionInfo fn in Functions.All.Where(x => Match(x.Name, x.Description, x.Signature)))
                        list.Children.Add(Item(owner, fn.Signature, fn.Description, "",
                            () => { insert(fn.Signature, false); popup.IsOpen = false; }, null));
                    list.Children.Add(new TextBlock
                    {
                        Style = (Style)owner.FindResource("T.Caption"), Margin = new Thickness(8, 12, 8, 4),
                            TextWrapping = TextWrapping.Wrap,
                        Text = "Operators: + - * / %   = != < > <= >=   and or not (or && || !)   condition ? a : b\nText goes in \"quotes\". Colors: #FF3040, or theme colors $accent $good $bad $warn $text $muted $panel."
                    });
                }
                else
                {
                    foreach (var recipe in Recipes.Where(r => Match(r.Name, r.Description, r.Formula)))
                        list.Children.Add(Item(owner, recipe.Name, recipe.Description, "",
                            () => { insert(recipe.Formula, true); popup.IsOpen = false; }, recipe.Formula));
                }
            }

            foreach (string tab in new[] { "Data", "Functions", "Recipes" })
            {
                var button = new RadioButton { Content = tab, GroupName = "picker" + anchor.GetHashCode(),
                    Style = (Style)owner.FindResource("Segment"), MinWidth = 90, IsChecked = tab == "Data" };
                string t = tab;
                button.Checked += (s, e) => { mode = t; Fill(); };
                tabs.Children.Add(button);
            }
            search.TextChanged += (s, e) => Fill();
            Fill();
            reference.Click += (s, e) =>
            {
                popup.IsOpen = false;
                FormulaReferenceWindow.Open(Window.GetWindow(owner), scope);
            };
            popup.Opened += (s, e) => search.Focus();
            popup.IsOpen = true;
        }

        private static Border Item(FrameworkElement owner, string title, string description, string value, Action click, string tooltip)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = title, FontFamily = (FontFamily)owner.FindResource("Font.Mono"), FontSize = 12,
                FontWeight = FontWeights.SemiBold });
            text.Children.Add(new TextBlock { Text = description, Style = (Style)owner.FindResource("T.Caption"), FontSize = 12,
                TextWrapping = TextWrapping.Wrap });
            grid.Children.Add(text);
            if (!string.IsNullOrEmpty(value))
            {
                var v = new TextBlock { Text = value.Length > 14 ? value.Substring(0, 14) + "…" : value,
                    FontFamily = (FontFamily)owner.FindResource("Font.Mono"), FontSize = 12,
                    Foreground = (Brush)owner.FindResource("Brush.Accent"), Margin = new Thickness(10, 1, 0, 0) };
                Grid.SetColumn(v, 1);
                grid.Children.Add(v);
            }
            var row = new Border { Child = grid, Padding = new Thickness(8, 6, 8, 6), CornerRadius = new CornerRadius(7),
                Cursor = Cursors.Hand, Background = Brushes.Transparent, ToolTip = tooltip };
            row.MouseEnter += (s, e) => row.Background = (Brush)owner.FindResource("Brush.SurfaceHover");
            row.MouseLeave += (s, e) => row.Background = Brushes.Transparent;
            row.MouseLeftButtonUp += (s, e) => click();
            return row;
        }
    }
}
