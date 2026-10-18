using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DrawingSize = System.Drawing.Size;

namespace AlmostFastOverlays
{
    /// <summary>
    /// A gallery of design templates with live thumbnails, and the screen size the new dashboard is for:
    /// the wheel screen's own when one is plugged in, otherwise the user's choice. Returns the template
    /// picked and that size.
    /// </summary>
    internal static class TemplatePicker
    {
        public static DesignTemplate Choose(Window owner, UnitSystem units, GeneralSettings general, out DrawingSize screen)
        {
            DesignTemplate chosen = null;
            DrawingSize picked = new DrawingSize(ScreenSizes.DefaultWidth, ScreenSizes.DefaultHeight);
            DrawingSize? detected = ScreenSizes.Detect();
            Window window = Build(owner, units, general, detected, (template, size) => { chosen = template; picked = size; });
            screen = picked;
            if (window.ShowDialog() != true || chosen == null) return null;
            screen = picked;
            if (detected == null)
            {
                general.LastScreenWidth = picked.Width;
                general.LastScreenHeight = picked.Height;
            }
            return chosen;
        }

        /// <summary>The picker window, not yet shown. <paramref name="picked"/> hears the template and size chosen.</summary>
        internal static Window Build(Window owner, UnitSystem units, GeneralSettings general, DrawingSize? detected, Action<DesignTemplate, DrawingSize> picked)
        {
            object R(string key) => owner != null ? owner.FindResource(key) : Application.Current.FindResource(key);
            DrawingSize? remembered = general.LastScreenWidth > 0 && general.LastScreenHeight > 0
                ? new DrawingSize(general.LastScreenWidth, general.LastScreenHeight) : (DrawingSize?)null;
            var chooser = new ScreenSizePicker.Chooser(owner, detected ?? remembered);
            var grid = new UniformGrid { Columns = 3 };
            var window = new Window
            {
                Title = "Start a new design", Owner = owner, Width = 1080, Height = 780, MinWidth = 820, MinHeight = 560,
                WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen, Background = (Brush)R("Brush.Bg"),
                Foreground = (Brush)R("Brush.Text"), FontFamily = (FontFamily)R("Font.Ui")
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
                title.Children.Add(new TextBlock { Text = t.Name, Style = (Style)R("T.Title") });
                body.Children.Add(title);
                body.Children.Add(new TextBlock { Text = t.Description, Style = (Style)R("T.Caption"), Margin = new Thickness(0, 6, 0, 0) });
                var stack = new StackPanel();
                stack.Children.Add(new Border { Child = thumbnail, CornerRadius = new CornerRadius(11, 11, 0, 0), ClipToBounds = true, BorderBrush = (Brush)R("Brush.Border"), BorderThickness = new Thickness(0, 0, 0, 1) });
                stack.Children.Add(body);
                var card = new Border
                {
                    Child = stack, Margin = new Thickness(0, 0, 16, 16), CornerRadius = new CornerRadius(12), Cursor = Cursors.Hand,
                    Background = (Brush)R("Brush.Surface"), BorderBrush = (Brush)R("Brush.Border"), BorderThickness = new Thickness(1)
                };
                card.MouseEnter += (s, e) => card.BorderBrush = (Brush)R("Brush.Accent");
                card.MouseLeave += (s, e) => card.BorderBrush = (Brush)R("Brush.Border");
                card.MouseLeftButtonUp += (s, e) =>
                {
                    if (chooser.Size == null)
                    {
                        // No size yet: point at the question rather than guessing one.
                        chooser.Focus();
                        return;
                    }
                    picked(t, chooser.Size.Value);
                    window.DialogResult = true;
                };
                grid.Children.Add(card);
            }

            var root = new DockPanel { Margin = new Thickness(28, 24, 12, 12) };
            var header = new StackPanel { Margin = new Thickness(0, 0, 16, 20) };
            header.Children.Add(new TextBlock { Text = "Start a new design", Style = (Style)R("T.PageTitle") });
            header.Children.Add(new TextBlock
            {
                Text = "Pick a starting point. Every template is fully editable.",
                Style = (Style)R("T.Secondary"), Margin = new Thickness(0, 6, 0, 0)
            });

            // The screen the dashboard is for.
            var sizeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 0) };
            sizeRow.Children.Add(new TextBlock { Text = "SCREEN", Style = (Style)R("T.Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) });
            var note = new TextBlock { Style = (Style)R("T.Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
            if (detected != null)
            {
                var found = new TextBlock { Text = detected.Value.Width + " × " + detected.Value.Height, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
                var change = new TextBlock { Text = "Use another size", Foreground = (Brush)R("Brush.Accent"), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
                note.Text = "found on your wheel screen";
                sizeRow.Children.Add(found);
                sizeRow.Children.Add(note);
                sizeRow.Children.Add(change);
                change.MouseLeftButtonUp += (s, e) =>
                {
                    sizeRow.Children.Remove(found);
                    sizeRow.Children.Remove(change);
                    sizeRow.Children.Insert(1, chooser.Element);
                    note.Text = "your wheel screen is " + detected.Value.Width + " × " + detected.Value.Height;
                };
            }
            else
            {
                const string ask = "Your wheel screen's resolution, or any size for your PC.";
                note.Text = chooser.Size == null ? ask : "The size you used last time.";
                note.Foreground = (Brush)R(chooser.Size == null ? "Brush.Accent" : "Brush.TextSecondary");
                sizeRow.Children.Add(chooser.Element);
                sizeRow.Children.Add(note);
                chooser.Changed += () =>
                {
                    bool ready = chooser.Size != null;
                    note.Text = ready ? "Now pick a template below." : ask;
                    note.Foreground = (Brush)R(ready ? "Brush.TextSecondary" : "Brush.Accent");
                };
            }
            header.Children.Add(sizeRow);
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);
            root.Children.Add(new ScrollViewer { Content = grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            window.Content = root;
            window.KeyDown += (s, e) => { if (e.Key == Key.Escape) window.DialogResult = false; };
            return window;
        }
    }
}
