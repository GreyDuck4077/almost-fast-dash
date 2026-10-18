using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DrawingSize = System.Drawing.Size;

namespace AlmostFastDash.Designer
{
    /// <summary>Choosing a dashboard's screen resolution: a common screen size, or any width and height.</summary>
    internal static class ScreenSizePicker
    {
        /// <summary>The chooser itself: a list of common sizes, with width and height boxes for any other.</summary>
        public sealed class Chooser
        {
            private readonly ComboBox _list = new ComboBox { MinWidth = 300 };
            private readonly TextBox _width = new TextBox { Width = 72 }, _height = new TextBox { Width = 72 };
            private readonly StackPanel _custom = new StackPanel { Orientation = Orientation.Horizontal,
                Margin = new Thickness(12, 0, 0, 0), Visibility = Visibility.Collapsed };
            // Shown in the empty list until a size is picked, so it never looks like a blank, broken box.
            private readonly TextBlock _placeholder = new TextBlock { Text = "Choose your screen size…", IsHitTestVisible = false,
                Margin = new Thickness(12, 0, 34, 0), VerticalAlignment = VerticalAlignment.Center };

            public Chooser(FrameworkElement owner, DrawingSize? initial)
            {
                _placeholder.Foreground = (Brush)(owner?.TryFindResource("Brush.TextTertiary") ?? Application.Current.FindResource("Brush.TextTertiary"));
                foreach (ScreenSizes.Preset preset in ScreenSizes.Presets) _list.Items.Add(new ComboBoxItem { Content = preset.Label,
                    Tag = preset });
                _list.Items.Add(new ComboBoxItem { Content = "Another size…" });
                _custom.Children.Add(_width);
                _custom.Children.Add(new TextBlock { Text = "×", Margin = new Thickness(8, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = (Brush)(owner?.TryFindResource("Brush.TextSecondary") ?? Application.Current.FindResource("Brush.TextSecondary")) });
                _custom.Children.Add(_height);
                _custom.Children.Add(new TextBlock { Text = "pixels", Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = (Brush)(owner?.TryFindResource("Brush.TextSecondary") ?? Application.Current.FindResource("Brush.TextSecondary")) });
                _list.SelectionChanged += (s, e) =>
                {
                    bool custom = _list.SelectedIndex == _list.Items.Count - 1;
                    _custom.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
                    _placeholder.Visibility = _list.SelectedIndex < 0 ? Visibility.Visible : Visibility.Collapsed;
                    if (custom &&
                        _width.Text.Length == 0) { _width.Text = ScreenSizes.DefaultWidth.ToString(); _height.Text = ScreenSizes.DefaultHeight.ToString(); }
                    Changed?.Invoke();
                };
                _width.TextChanged += (s, e) => Changed?.Invoke();
                _height.TextChanged += (s, e) => Changed?.Invoke();
                Select(initial);
                _placeholder.Visibility = _list.SelectedIndex < 0 ? Visibility.Visible : Visibility.Collapsed;
                Element = new StackPanel { Orientation = Orientation.Horizontal,
                    Children = { new Grid { Children = { _list, _placeholder } }, _custom } };
            }

            public FrameworkElement Element { get; }
            public event Action Changed;

            public void Select(DrawingSize? size)
            {
                if (size == null) { _list.SelectedIndex = -1; return; }
                int index = Array.FindIndex(ScreenSizes.Presets, p => p.Width == size.Value.Width && p.Height == size.Value.Height);
                if (index >= 0) { _list.SelectedIndex = index; return; }
                _width.Text = size.Value.Width.ToString(CultureInfo.InvariantCulture);
                _height.Text = size.Value.Height.ToString(CultureInfo.InvariantCulture);
                _list.SelectedIndex = _list.Items.Count - 1;
            }

            /// <summary>The size chosen, or null while none is (or the custom size isn't a usable number).</summary>
            public DrawingSize? Size
            {
                get
                {
                    if (_list.SelectedIndex < 0) return null;
                    if ((_list.SelectedItem as ComboBoxItem)?.Tag is ScreenSizes.Preset preset) return new DrawingSize(preset.Width,
                        preset.Height);
                    if (int.TryParse(_width.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int w) &&
                        int.TryParse(_height.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int h) &&
                        w >= ScreenSizes.Smallest && w <= ScreenSizes.Largest && h >= ScreenSizes.Smallest && h <= ScreenSizes.Largest)
                        return new DrawingSize(w, h);
                    return null;
                }
            }

            public void Focus() => _list.Focus();
        }

        /// <summary>Asks for a screen size in a small window; null when cancelled.</summary>
        public static DrawingSize? Ask(Window owner, string title, string message, DrawingSize? initial, string confirm)
        {
            var window = new Window
            {
                Title = title, Owner = owner, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = (Brush)owner.FindResource("Brush.Bg"),
                Foreground = (Brush)owner.FindResource("Brush.Text"), FontFamily = (FontFamily)owner.FindResource("Font.Ui")
            };
            GlassBackdrop.Apply(window);
            var chooser = new Chooser(owner, initial);
            var ok = new Button { Content = confirm, Style = (Style)owner.FindResource("Btn.Primary"), IsDefault = true, MinWidth = 110,
                Margin = new Thickness(8, 0, 0, 0) };
            var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90 };
            ok.IsEnabled = chooser.Size != null;
            chooser.Changed += () => ok.IsEnabled = chooser.Size != null;
            DrawingSize? result = null;
            ok.Click += (s, e) => { result = chooser.Size; window.DialogResult = true; };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 24, 0, 0), Children = { cancel, ok } };
            var root = new StackPanel { Margin = new Thickness(28, 24, 28, 24), MaxWidth = 620 };
            root.Children.Add(new TextBlock { Text = title, Style = (Style)owner.FindResource("T.PageTitle") });
            root.Children.Add(new TextBlock { Text = message, Style = (Style)owner.FindResource("T.Secondary"),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 18) });
            root.Children.Add(chooser.Element);
            root.Children.Add(buttons);
            window.Content = root;
            window.Loaded += (s, e) => chooser.Focus();
            window.KeyDown += (s, e) => { if (e.Key == Key.Escape) window.DialogResult = false; };
            return window.ShowDialog() == true ? result : null;
        }
    }
}
