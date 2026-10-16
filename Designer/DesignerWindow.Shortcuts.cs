using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace AlmostFastOverlays
{
    public partial class DesignerWindow
    {
        // ==================================================================
        // The keyboard shortcuts sheet (Ctrl+/)
        // ==================================================================

        private static readonly (string Section, (string Keys, string What)[] Items)[] Shortcuts =
        {
            ("Tools", new[]
            {
                ("V", "Move: select, move and resize"), ("U", "Rectangle; Shift+U steps to ellipse, polygon, line"), ("P", "Pen: corners and curves"),
                ("T", "Text"), ("H  or hold Space", "Hand: move around the canvas"), ("Z", "Zoom; Alt+click zooms out"), ("Esc", "Back to Move")
            }),
            ("Edit", new[]
            {
                ("Ctrl+Z  ·  Ctrl+Y", "Undo · redo"), ("Ctrl+C  ·  X  ·  V", "Copy · cut · paste"), ("Ctrl+D", "Duplicate (a group: as the next Slot)"),
                ("Ctrl+Shift+D", "Repeat in rows or around a circle"), ("Del", "Delete"), ("Arrows", "Nudge 1 px; Shift for 10"), ("Ctrl+A", "Select all"),
                ("Ctrl+Shift+C  ·  V", "Copy · paste a widget's style"), ("Enter", "Edit a path's points")
            }),
            ("Layers", new[]
            {
                ("Ctrl+G", "Group"), ("Ctrl+Shift+G", "Ungroup"), ("[  ·  ]", "Send backward · bring forward; Ctrl for back · front"),
                ("Ctrl+Alt+G", "Clip to the layer below"), ("Alt+click a layer", "Clip it, or release it"), ("Ctrl+L", "Lock"), ("Ctrl+M", "Save as module")
            }),
            ("View", new[]
            {
                ("Ctrl+wheel", "Zoom"), ("Shift+1  ·  Ctrl+0", "Fit the screen"), ("Shift+2", "Zoom to the selection"), ("Ctrl+R", "Rulers"),
                ("F1", "Formula reference"), ("Ctrl+/", "These shortcuts")
            }),
            ("While dragging", new[]
            {
                ("Shift", "Keep the shape square, round, level or at 45°"), ("Alt", "Draw from the middle; stop snapping"), ("Shift", "On the rotate handle: turn in 15° steps")
            })
        };

        private void Shortcuts_Click(object sender, RoutedEventArgs e) => ShowShortcuts();

        private void ShowShortcuts() => ShowShortcuts(null);

        /// <param name="picture">For the snapshot self-check: a file to save a picture of the sheet to, then close it.</param>
        private void ShowShortcuts(string picture)
        {
            var columns = new UniformGrid { Columns = 3 };
            foreach (var section in Shortcuts)
            {
                var block = new StackPanel { Margin = new Thickness(0, 0, 28, 18) };
                block.Children.Add(new TextBlock { Text = section.Section.ToUpperInvariant(), Style = (Style)FindResource("T.Overline"), Margin = new Thickness(0, 0, 0, 8) });
                foreach (var item in section.Items)
                {
                    var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
                    row.ColumnDefinitions.Add(new ColumnDefinition());
                    var keys = new Border
                    {
                        Background = (Brush)FindResource("Brush.Surface"), BorderBrush = (Brush)FindResource("Brush.BorderStrong"), BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(5), Padding = new Thickness(7, 2, 7, 3), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                        Child = new TextBlock { Text = item.Keys, FontFamily = (FontFamily)FindResource("Font.Mono"), FontSize = 11.5 }
                    };
                    row.Children.Add(keys);
                    var what = new TextBlock { Text = item.What, Style = (Style)FindResource("T.Secondary"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
                    Grid.SetColumn(what, 1);
                    row.Children.Add(what);
                    block.Children.Add(row);
                }
                columns.Children.Add(block);
            }
            var body = new StackPanel { Margin = new Thickness(28, 22, 28, 18) };
            body.Children.Add(new TextBlock { Text = "Keyboard shortcuts", Style = (Style)FindResource("T.Title"), FontSize = 20, Margin = new Thickness(0, 0, 0, 4) });
            body.Children.Add(new TextBlock { Text = "Keys work on the canvas; text fields keep their own.", Style = (Style)FindResource("T.Caption"), Margin = new Thickness(0, 0, 0, 18) });
            body.Children.Add(columns);
            var close = new Button { Content = "Close", IsCancel = true, IsDefault = true, MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Right };
            body.Children.Add(close);
            var dialog = new Window
            {
                Title = "Keyboard shortcuts", Owner = this, Width = 1080, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = (Brush)FindResource("Brush.Surface"),
                Foreground = (Brush)FindResource("Brush.Text"), FontFamily = (FontFamily)FindResource("Font.Ui"), Content = body
            };
            close.Click += (s, e) => dialog.Close();
            if (picture != null)
                dialog.ContentRendered += (s, e) => dialog.Dispatcher.BeginInvoke(new System.Action(() =>
                {
                    var content = (FrameworkElement)dialog.Content;
                    var target = new System.Windows.Media.Imaging.RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    var back = new DrawingVisual();
                    using (DrawingContext dc = back.RenderOpen())
                    {
                        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(11, 14, 19)), null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
                        dc.DrawRectangle(dialog.Background, null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
                    }
                    target.Render(back);
                    target.Render(content);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(target));
                    using (var file = System.IO.File.Create(picture)) encoder.Save(file);
                    dialog.Close();
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            dialog.ShowDialog();
        }
    }
}
