using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AlmostFastDash.Designer
{
    public partial class DesignerWindow
    {
        // ==================================================================
        // Panels: the library on the left, properties or the theme on the right (layers are always there)
        // ==================================================================

        private void RightTab_Checked(object sender, RoutedEventArgs e)
        {
            if (InspectorScroller == null || ThemePanel == null) return;
            bool theme = TabTheme.IsChecked == true;
            InspectorScroller.Visibility = theme ? Visibility.Collapsed : Visibility.Visible;
            ThemePanel.Visibility = theme ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>The library panel stays as it was left, for every design opened in this session.</summary>
        private static bool _libraryOpen = true;

        private void LibraryToggle_Click(object sender, RoutedEventArgs e) => ShowLibrary(LibraryToggle.IsChecked == true);

        private void LibraryHide_Click(object sender, RoutedEventArgs e) => ShowLibrary(false);

        private void ShowLibrary(bool open)
        {
            _libraryOpen = open;
            LibraryToggle.IsChecked = open;
            LibraryPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            LibraryColumn.Width = new GridLength(open ? 272 : 0);
        }

        private void GroupLayers_Click(object sender, RoutedEventArgs e) => GroupSelection();

        private TextBlock Glyph(string glyph, double size = 14, string brush = null)
        {
            var icon = new TextBlock { Text = glyph, Style = (Style)FindResource("T.Icon"), FontSize = size };
            if (brush != null) icon.Foreground = (Brush)FindResource(brush);
            return icon;
        }

        private TextBlock SectionTitle(string text) => new TextBlock { Text = text.ToUpperInvariant(), Style = (Style)FindResource("T.Overline"), Margin = new Thickness(2, 16, 0, 8) };

        /// <summary>A small themed text prompt.</summary>
        private string Prompt(string title, string message, string initial)
        {
            var box = new TextBox { Text = initial ?? "", Margin = new Thickness(0, 12, 0, 16) };
            var ok = new Button { Content = "Save", Style = (Style)FindResource("Btn.Primary"), IsDefault = true, MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
            var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90 };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            var body = new StackPanel { Margin = new Thickness(22) };
            body.Children.Add(new TextBlock { Text = message, Style = (Style)FindResource("T.Secondary") });
            body.Children.Add(box);
            body.Children.Add(buttons);
            var dialog = new Window
            {
                Title = title, Owner = this, Width = 420, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = (Brush)FindResource("Brush.Surface"),
                Foreground = (Brush)FindResource("Brush.Text"), FontFamily = (FontFamily)FindResource("Font.Ui"), Content = body
            };
            ok.Click += (s, e) => dialog.DialogResult = true;
            dialog.Loaded += (s, e) => { box.Focus(); box.SelectAll(); };
            return dialog.ShowDialog() == true ? box.Text : null;
        }

        internal static Color ToMedia(string hex)
        {
            System.Drawing.Color? c = Expr.ParseHex(hex);
            return c.HasValue ? Color.FromArgb(c.Value.A, c.Value.R, c.Value.G, c.Value.B) : Colors.Transparent;
        }
    }
}
