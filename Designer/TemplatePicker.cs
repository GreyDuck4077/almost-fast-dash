using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace AlmostFastOverlays
{
    /// <summary>A gallery of design templates with live thumbnails; returns the one picked.</summary>
    internal static class TemplatePicker
    {
        public static DesignTemplate Choose(Window owner, UnitSystem units)
        {
            DesignTemplate chosen = null;
            var grid = new UniformGrid { Columns = 3 };
            var window = new Window
            {
                Title = "Start a new design", Owner = owner, Width = 1080, Height = 760, MinWidth = 820, MinHeight = 560,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = (Brush)owner.FindResource("Brush.Bg"),
                Foreground = (Brush)owner.FindResource("Brush.Text"), FontFamily = (FontFamily)owner.FindResource("Font.Ui")
            };

            GlassBackdrop.Apply(window);
            foreach (DesignTemplate template in DesignTemplates.All)
            {
                DesignTemplate t = template;
                var thumbnail = new Image
                {
                    Source = DesignerWindow.RenderThumbnail(DashboardSettings.CreateDesigned(t), units),
                    Stretch = Stretch.Uniform
                };
                RenderOptions.SetBitmapScalingMode(thumbnail, BitmapScalingMode.HighQuality);
                var body = new StackPanel { Margin = new Thickness(16, 12, 16, 16) };
                var title = new StackPanel { Orientation = Orientation.Horizontal };
                title.Children.Add(new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(4.5), Background = new SolidColorBrush(DesignerWindow.ToMedia(t.Accent)), Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center });
                title.Children.Add(new TextBlock { Text = t.Name, Style = (Style)owner.FindResource("T.Title") });
                body.Children.Add(title);
                body.Children.Add(new TextBlock { Text = t.Description, Style = (Style)owner.FindResource("T.Caption"), Margin = new Thickness(0, 6, 0, 0) });
                var stack = new StackPanel();
                stack.Children.Add(new Border { Child = thumbnail, CornerRadius = new CornerRadius(11, 11, 0, 0), ClipToBounds = true, BorderBrush = (Brush)owner.FindResource("Brush.Border"), BorderThickness = new Thickness(0, 0, 0, 1) });
                stack.Children.Add(body);
                var card = new Border
                {
                    Child = stack, Margin = new Thickness(0, 0, 16, 16), CornerRadius = new CornerRadius(12), Cursor = Cursors.Hand,
                    Background = (Brush)owner.FindResource("Brush.Surface"), BorderBrush = (Brush)owner.FindResource("Brush.Border"), BorderThickness = new Thickness(1)
                };
                card.MouseEnter += (s, e) => card.BorderBrush = (Brush)owner.FindResource("Brush.Accent");
                card.MouseLeave += (s, e) => card.BorderBrush = (Brush)owner.FindResource("Brush.Border");
                card.MouseLeftButtonUp += (s, e) => { chosen = t; window.DialogResult = true; };
                grid.Children.Add(card);
            }

            var root = new DockPanel { Margin = new Thickness(28, 24, 12, 12) };
            var header = new StackPanel { Margin = new Thickness(0, 0, 16, 20) };
            header.Children.Add(new TextBlock { Text = "Start a new design", Style = (Style)owner.FindResource("T.PageTitle") });
            header.Children.Add(new TextBlock
            {
                Text = "Pick a starting point. The first four are the built-in dashboards rebuilt as editable designs; the originals stay as they are.",
                Style = (Style)owner.FindResource("T.Secondary"), Margin = new Thickness(0, 6, 0, 0)
            });
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);
            root.Children.Add(new ScrollViewer { Content = grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            window.Content = root;
            window.KeyDown += (s, e) => { if (e.Key == Key.Escape) window.DialogResult = false; };
            return window.ShowDialog() == true ? chosen : null;
        }
    }
}
