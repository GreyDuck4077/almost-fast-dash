using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace AlmostFastDash.Designer
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

            var pick = new Button
            {
                Content = "Pick from the screen", Style = (Style)_owner.FindResource("Btn.Ghost"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 6),
                ToolTip = "Eyedropper: click any color in this window. Esc cancels."
            };
            pick.Click += (s, e) => { popup.IsOpen = false; ScreenColorPicker.Pick(_owner, v => SetValue(v, true)); };
            body.Children.Add(pick);
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
}
