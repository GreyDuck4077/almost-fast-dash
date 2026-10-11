using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace AlmostFastOverlays
{
    /// <summary>
    /// A color editor: swatch plus text. The popup offers the design's theme colors
    /// (kept as live references such as "accent"), a curated palette, recent colors,
    /// hex with opacity and the Windows color dialog.
    /// </summary>
    internal sealed class ColorField : Grid
    {
        private static readonly List<string> Recent = new List<string>();
        private static readonly string[] Palette =
        {
            "#FFFFFF", "#ECF1F7", "#A0AAB8", "#4E5A6A", "#1B232E", "#000000",
            "#2DD4BF", "#38BDF8", "#0A84FF", "#5E5CE6", "#BF5AF2", "#FF2BD6",
            "#FF375F", "#F43F4E", "#FF9F0A", "#FFBE2E", "#C8F04B", "#2EDE7C"
        };

        private readonly FrameworkElement _owner;
        private readonly DashLayout _layout;
        private readonly Func<string> _accent;
        private readonly bool _allowTokens, _allowEmpty;
        private readonly Border _swatch;
        private readonly TextBox _text;
        private bool _updating;

        public event Action<string> Changed;

        public ColorField(FrameworkElement owner, string value, DashLayout layout, Func<string> accent, bool allowTokens = true, bool allowEmpty = false)
        {
            _owner = owner;
            _layout = layout;
            _accent = accent;
            _allowTokens = allowTokens;
            _allowEmpty = allowEmpty;
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            ColumnDefinitions.Add(new ColumnDefinition());

            _swatch = new Border
            {
                Width = 28, Height = 28, CornerRadius = new CornerRadius(7), Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Left,
                BorderBrush = (Brush)owner.FindResource("Brush.BorderStrong"), BorderThickness = new Thickness(1), ToolTip = "Pick a color"
            };
            _swatch.MouseLeftButtonUp += (s, e) => OpenPopup();
            Children.Add(_swatch);

            _text = new TextBox { Text = value ?? "", FontFamily = (FontFamily)owner.FindResource("Font.Mono"), FontSize = 12, VerticalContentAlignment = VerticalAlignment.Center };
            _text.TextChanged += (s, e) =>
            {
                if (_updating) return;
                UpdateSwatch();
                string v = _text.Text.Trim();
                if (v.Length == 0 && !_allowEmpty) return;
                if (v.Length == 0 || Resolve(v).HasValue) Changed?.Invoke(v);
            };
            SetColumn(_text, 1);
            Children.Add(_text);
            UpdateSwatch();
        }

        public string Value => _text.Text.Trim();

        public void SetValue(string value, bool notify)
        {
            _updating = true;
            _text.Text = value ?? "";
            _updating = false;
            UpdateSwatch();
            if (notify)
            {
                Remember(value);
                Changed?.Invoke(value ?? "");
            }
        }

        private System.Drawing.Color? Resolve(string value) =>
            Expr.Color(value, new EvalScope { Theme = _layout?.Theme ?? new LayoutTheme(), Accent = _accent?.Invoke() ?? "#2DD4BF" });

        private void UpdateSwatch()
        {
            System.Drawing.Color? c = Resolve(_text.Text.Trim());
            _swatch.Background = c.HasValue ? Checker(Color.FromArgb(c.Value.A, c.Value.R, c.Value.G, c.Value.B)) : (Brush)new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
            if (!c.HasValue && _text.Text.Trim().Length > 0) _text.Foreground = (Brush)_owner.FindResource("Brush.Danger");
            else _text.Foreground = (Brush)_owner.FindResource("Brush.Text");
        }

        /// <summary>A swatch brush that shows transparency over a checkerboard.</summary>
        internal static Brush Checker(Color color)
        {
            var group = new DrawingGroup();
            group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(70, 70, 76)), null, new RectangleGeometry(new Rect(0, 0, 8, 8))));
            group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(110, 110, 118)), null, new RectangleGeometry(new Rect(0, 0, 4, 4))));
            group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(110, 110, 118)), null, new RectangleGeometry(new Rect(4, 4, 4, 4))));
            group.Children.Add(new GeometryDrawing(new SolidColorBrush(color), null, new RectangleGeometry(new Rect(0, 0, 8, 8))));
            return new DrawingBrush(group) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 8, 8), ViewportUnits = BrushMappingMode.Absolute };
        }

        private static void Remember(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || !value.StartsWith("#")) return;
            Recent.Remove(value);
            Recent.Insert(0, value);
            if (Recent.Count > 12) Recent.RemoveAt(12);
        }

        private void OpenPopup()
        {
            var body = new StackPanel { Margin = new Thickness(14) };
            var popup = new Popup
            {
                PlacementTarget = _swatch, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true,
                Child = new Border
                {
                    Width = 300, Background = (Brush)_owner.FindResource("Brush.Popup"), BorderBrush = (Brush)_owner.FindResource("Brush.BorderStrong"),
                    BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Child = body,
                    Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.5 }
                }
            };

            void Section(string title) => body.Children.Add(new TextBlock { Text = title, Style = (Style)_owner.FindResource("T.Overline"), Margin = new Thickness(0, body.Children.Count == 0 ? 0 : 12, 0, 6) });
            WrapPanel Swatches(IEnumerable<string> values, bool tokens)
            {
                var wrap = new WrapPanel();
                foreach (string v in values)
                {
                    System.Drawing.Color? c = Resolve(v);
                    if (!c.HasValue) continue;
                    var swatch = new Border
                    {
                        Width = 30, Height = 30, Margin = new Thickness(0, 0, 6, 6), CornerRadius = new CornerRadius(7), Cursor = Cursors.Hand,
                        BorderBrush = (Brush)_owner.FindResource("Brush.Border"), BorderThickness = new Thickness(1),
                        Background = Checker(Color.FromArgb(c.Value.A, c.Value.R, c.Value.G, c.Value.B)),
                        ToolTip = tokens ? "$" + v + " (follows the theme)" : v
                    };
                    string chosen = v;
                    swatch.MouseLeftButtonUp += (s, e) => { SetValue(chosen, true); popup.IsOpen = false; };
                    wrap.Children.Add(swatch);
                }
                return wrap;
            }

            if (_allowEmpty)
            {
                var none = new Button { Content = "No color", Style = (Style)_owner.FindResource("Btn.Ghost"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 6) };
                none.Click += (s, e) => { SetValue("", true); popup.IsOpen = false; };
                body.Children.Add(none);
            }
            if (_allowTokens)
            {
                Section("THEME COLORS");
                body.Children.Add(Swatches(LayoutTheme.Tokens, true));
            }
            Section("PALETTE");
            body.Children.Add(Swatches(Palette, false));
            if (Recent.Count > 0)
            {
                Section("RECENT");
                body.Children.Add(Swatches(Recent, false));
            }

            Section("CUSTOM");
            System.Drawing.Color current = Resolve(Value) ?? System.Drawing.Color.White;
            var hex = new TextBox { Text = SettingsStore.ToHex(current), FontFamily = (FontFamily)_owner.FindResource("Font.Mono"), Width = 96 };
            var opacity = new Slider { Minimum = 0, Maximum = 100, Value = Math.Round(current.A / 2.55), Width = 120, Margin = new Thickness(10, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            var opacityText = new TextBlock { Text = (int)opacity.Value + "%", Width = 36, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)_owner.FindResource("Brush.TextSecondary") };
            void ApplyCustom()
            {
                System.Drawing.Color? c = Expr.ParseHex(hex.Text.Trim().StartsWith("#") ? hex.Text.Trim() : "#" + hex.Text.Trim());
                if (!c.HasValue) return;
                int a = (int)Math.Round(opacity.Value * 2.55);
                SetValue(a >= 255 ? SettingsStore.ToHex(c.Value) : "#" + a.ToString("X2") + SettingsStore.ToHex(c.Value).Substring(1), true);
            }
            hex.KeyDown += (s, e) => { if (e.Key == Key.Enter) ApplyCustom(); };
            hex.LostKeyboardFocus += (s, e) => ApplyCustom();
            opacity.ValueChanged += (s, e) => { opacityText.Text = (int)opacity.Value + "%"; ApplyCustom(); };
            var custom = new StackPanel { Orientation = Orientation.Horizontal };
            custom.Children.Add(hex);
            custom.Children.Add(opacity);
            custom.Children.Add(opacityText);
            body.Children.Add(custom);

            var more = new Button { Content = "More colors…", Style = (Style)_owner.FindResource("Btn.Ghost"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-6, 10, 0, 0) };
            more.Click += (s, e) =>
            {
                popup.IsOpen = false;
                if (WindowsColorPicker.TryChoose(SettingsStore.ToHex(current), out string picked)) SetValue(picked, true);
            };
            body.Children.Add(more);
            popup.IsOpen = true;
        }
    }

    /// <summary>
    /// A formula editor: monospace text, an Insert button that opens the data and
    /// function picker, and a live line showing the current result or the error.
    /// </summary>
    internal sealed class FormulaBox : StackPanel
    {
        private readonly FrameworkElement _owner;
        private readonly Func<EvalScope> _scope;
        public readonly TextBox Text;
        public readonly TextBlock Result;
        public event Action<string> Changed;

        public FormulaBox(FrameworkElement owner, string formula, Func<EvalScope> scope, string placeholder = null)
        {
            _owner = owner;
            _scope = scope;
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Text = new TextBox
            {
                Text = formula ?? "", FontFamily = (FontFamily)owner.FindResource("Font.Mono"), FontSize = 12, TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = false, MinHeight = 30, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = placeholder
            };
            Text.TextChanged += (s, e) => { Validate(); Changed?.Invoke(Text.Text); };
            row.Children.Add(Text);
            var insert = new Button
            {
                Style = (Style)owner.FindResource("Btn.Ghost"), Width = 30, Height = 30, Padding = new Thickness(0), Margin = new Thickness(4, 0, 0, 0),
                ToolTip = "Insert data, a function or a recipe",
                Content = new TextBlock { Text = "", Style = (Style)owner.FindResource("T.Icon"), FontSize = 12 }
            };
            insert.Click += (s, e) => DataPicker.Open(owner, insert, _scope, InsertText);
            Grid.SetColumn(insert, 1);
            row.Children.Add(insert);
            Children.Add(row);
            Result = new TextBlock { FontFamily = (FontFamily)owner.FindResource("Font.Mono"), FontSize = 11, Margin = new Thickness(2, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            Children.Add(Result);
            Validate();
        }

        private void InsertText(string snippet, bool replace)
        {
            if (replace || Text.Text.Trim().Length == 0) Text.Text = snippet;
            else
            {
                int at = Text.CaretIndex;
                Text.Text = Text.Text.Insert(at, snippet);
                Text.CaretIndex = at + snippet.Length;
            }
            Text.Focus();
        }

        private void Validate()
        {
            Expr.Compiled compiled = Expr.Compile(Text.Text);
            if (compiled.Error != null)
            {
                Result.Text = "⚠ " + compiled.Error;
                Result.Foreground = (Brush)_owner.FindResource("Brush.Danger");
            }
            else ShowResult();
        }

        public void ShowResult()
        {
            Expr.Compiled compiled = Expr.Compile(Text.Text);
            if (compiled.Error != null || Text.Text.Trim().Length == 0)
            {
                if (Text.Text.Trim().Length == 0) Result.Text = "";
                return;
            }
            object value = Expr.Evaluate(Text.Text, _scope());
            string shown = value is System.Drawing.Color c ? SettingsStore.ToHex(c) + (c.A < 255 ? " @" + Math.Round(c.A / 2.55) + "%" : "") : Expr.Text(value);
            Result.Text = "→ " + (shown.Length == 0 ? "(empty)" : shown);
            Result.Foreground = (Brush)_owner.FindResource("Brush.TextTertiary");
        }
    }

    /// <summary>The searchable data, function and recipe picker behind the formula Insert button.</summary>
    internal static class DataPicker
    {
        private static readonly (string Name, string Formula, string Description)[] Recipes =
        {
            ("Speed in your unit", "format(Speed, \"0\")", "Whole-number speed in MPH or km/h."),
            ("Lap time", "laptime(LastLap)", "Any lap value as 1:27.932."),
            ("Delta with sign", "signed(Delta, 2)", "+0.46 / -0.12."),
            ("Delta color", "deltacolor(Delta)", "Green when gaining, red when losing."),
            ("Color by thresholds", "steps(FuelLapsLeft, $bad, 2, $warn, 5, $text)", "Red under 2 laps, amber under 5, otherwise text color."),
            ("Flash a warning", "FuelLapsLeft >= 0 && FuelLapsLeft < 2 && blink(300)", "Use in Show when to blink a widget."),
            ("Only on pit road", "OnPitRoad", "Use in Show when."),
            ("Only while racing", "!OnPitRoad && !FlagActive", "Hide during pit lane and flags."),
            ("Position text", "Position > 0 ? \"P\" + Position : \"P--\"", "P4, or P-- when unknown."),
            ("Fuel to add", "FuelToFinish < 0 ? \"--.-\" : FuelToFinish <= 0.05 ? \"NONE\" : \"+\" + fuel(FuelToFinish)", "How much to add to finish."),
            ("Shift light color", "ShiftFlash ? $bad : steps(ShiftPct, $good, 0.5, $warn, 0.8, $bad)", "For bars or text that follow the revs."),
            ("Gap to car ahead", "AheadName != \"---\" ? format(AheadGap, \"0.0\") : \"--\"", "Seconds, or -- when nobody is ahead."),
            ("Spotter call", "CarLeft && CarRight ? \"THREE WIDE\" : CarLeft ? \"CAR LEFT\" : CarRight ? \"CAR RIGHT\" : \"CLEAR\"", "Text for an oval spotter."),
            ("Clock", "time(\"HH:mm\")", "The PC's time."),
            ("Fade a color", "alpha($accent, 0.3)", "30% opacity version of a color."),
        };

        public static void Open(FrameworkElement owner, UIElement anchor, Func<EvalScope> scope, Action<string, bool> insert)
        {
            var search = new TextBox { Margin = new Thickness(12, 12, 12, 8) };
            var tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 12, 8) };
            var list = new StackPanel();
            var scroller = new ScrollViewer { Content = list, Height = 380, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(6, 0, 6, 8) };
            var root = new DockPanel();
            DockPanel.SetDock(search, Dock.Top);
            DockPanel.SetDock(tabs, Dock.Top);
            root.Children.Add(search);
            root.Children.Add(tabs);
            root.Children.Add(scroller);
            var popup = new Popup
            {
                PlacementTarget = anchor, Placement = PlacementMode.Left, StaysOpen = false, AllowsTransparency = true,
                Child = new Border
                {
                    Width = 440, Background = (Brush)owner.FindResource("Brush.Popup"), BorderBrush = (Brush)owner.FindResource("Brush.BorderStrong"),
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
                bool Match(params string[] parts) => f.Length == 0 || parts.Any(p => (p ?? "").IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0);
                if (mode == "Data")
                {
                    foreach (string category in DataCatalog.Categories)
                    {
                        List<DataField> fields = DataCatalog.InCategory(category).Where(d => Match(d.Name, d.Description, category)).ToList();
                        if (fields.Count == 0) continue;
                        list.Children.Add(new TextBlock { Text = category.ToUpperInvariant(), Style = (Style)owner.FindResource("T.Overline"), Margin = new Thickness(8, 10, 0, 4) });
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
                        list.Children.Add(Item(owner, fn.Signature, fn.Description, "", () => { insert(fn.Signature, false); popup.IsOpen = false; }, null));
                    list.Children.Add(new TextBlock
                    {
                        Style = (Style)owner.FindResource("T.Caption"), Margin = new Thickness(8, 12, 8, 4), TextWrapping = TextWrapping.Wrap,
                        Text = "Operators: + - * / %   = != < > <= >=   and or not (or && || !)   condition ? a : b\nText goes in \"quotes\". Colors: #FF3040, or theme colors $accent $good $bad $warn $text $muted $panel."
                    });
                }
                else
                {
                    foreach (var recipe in Recipes.Where(r => Match(r.Name, r.Description, r.Formula)))
                        list.Children.Add(Item(owner, recipe.Name, recipe.Description, "", () => { insert(recipe.Formula, true); popup.IsOpen = false; }, recipe.Formula));
                }
            }

            foreach (string tab in new[] { "Data", "Functions", "Recipes" })
            {
                var button = new RadioButton { Content = tab, GroupName = "picker" + anchor.GetHashCode(), Style = (Style)owner.FindResource("Segment"), MinWidth = 90, IsChecked = tab == "Data" };
                string t = tab;
                button.Checked += (s, e) => { mode = t; Fill(); };
                tabs.Children.Add(button);
            }
            search.TextChanged += (s, e) => Fill();
            Fill();
            popup.Opened += (s, e) => search.Focus();
            popup.IsOpen = true;
        }

        private static Border Item(FrameworkElement owner, string title, string description, string value, Action click, string tooltip)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = title, FontFamily = (FontFamily)owner.FindResource("Font.Mono"), FontSize = 12, FontWeight = FontWeights.SemiBold });
            text.Children.Add(new TextBlock { Text = description, Style = (Style)owner.FindResource("T.Caption"), FontSize = 11, TextWrapping = TextWrapping.Wrap });
            grid.Children.Add(text);
            if (!string.IsNullOrEmpty(value))
            {
                var v = new TextBlock { Text = value.Length > 14 ? value.Substring(0, 14) + "…" : value, FontFamily = (FontFamily)owner.FindResource("Font.Mono"), FontSize = 11, Foreground = (Brush)owner.FindResource("Brush.Accent"), Margin = new Thickness(10, 1, 0, 0) };
                Grid.SetColumn(v, 1);
                grid.Children.Add(v);
            }
            var row = new Border { Child = grid, Padding = new Thickness(8, 6, 8, 6), CornerRadius = new CornerRadius(7), Cursor = Cursors.Hand, Background = Brushes.Transparent, ToolTip = tooltip };
            row.MouseEnter += (s, e) => row.Background = (Brush)owner.FindResource("Brush.SurfaceHover");
            row.MouseLeave += (s, e) => row.Background = Brushes.Transparent;
            row.MouseLeftButtonUp += (s, e) => click();
            return row;
        }
    }
}
