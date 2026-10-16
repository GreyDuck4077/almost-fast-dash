using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace AlmostFastOverlays
{
    /// <summary>
    /// The eyedropper: the pointer becomes a cross with the colour under it beside it, and a click anywhere in the
    /// window takes that colour. Esc, a right-click or leaving the window stops it. Clicks are only taken inside the
    /// app's own window, so nothing else on the PC is ever clicked by mistake.
    /// </summary>
    internal static class ScreenColorPicker
    {
        private static bool _active;

        public static void Pick(FrameworkElement from, Action<string> picked)
        {
            Window window = Window.GetWindow(from);
            if (window == null || _active) return;
            _active = true;

            var swatch = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(5), BorderBrush = Brushes.White, BorderThickness = new Thickness(1) };
            var label = new TextBlock
            {
                FontFamily = (FontFamily)window.FindResource("Font.Mono"), FontSize = 12, Margin = new Thickness(8, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)window.FindResource("Brush.Text"), Text = "Click to pick"
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(swatch);
            row.Children.Add(label);
            var tip = new Popup
            {
                AllowsTransparency = true, Placement = PlacementMode.Absolute, IsHitTestVisible = false,
                Child = new Border
                {
                    Background = (Brush)window.FindResource("Brush.Popup"), BorderBrush = (Brush)window.FindResource("Brush.BorderStrong"),
                    BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(6), Child = row
                }
            };
            Cursor before = Mouse.OverrideCursor;
            Mouse.OverrideCursor = Cursors.Cross;

            MouseEventHandler move = null;
            MouseButtonEventHandler down = null, up = null, right = null;
            KeyEventHandler key = null;
            EventHandler lost = null;
            bool swallowUp = false;
            void End()
            {
                _active = false;
                Mouse.OverrideCursor = before;
                tip.IsOpen = false;
                window.PreviewMouseMove -= move;
                window.PreviewMouseLeftButtonDown -= down;
                window.PreviewMouseRightButtonDown -= right;
                window.PreviewKeyDown -= key;
                window.Deactivated -= lost;
                if (!swallowUp) window.PreviewMouseLeftButtonUp -= up;
            }
            System.Drawing.Color Under(MouseEventArgs e) => Sample(window.PointToScreen(e.GetPosition(window)));

            move = (s, e) =>
            {
                System.Drawing.Color c = Under(e);
                swatch.Background = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
                label.Text = SettingsStore.ToHex(c);
                // The preview sits off to the side of the pointer, so it's never what gets sampled.
                PresentationSource source = PresentationSource.FromVisual(window);
                Point at = window.PointToScreen(e.GetPosition(window));
                if (source?.CompositionTarget != null) at = source.CompositionTarget.TransformFromDevice.Transform(at);
                tip.HorizontalOffset = at.X + 20;
                tip.VerticalOffset = at.Y + 20;
                tip.IsOpen = true;
            };
            down = (s, e) =>
            {
                System.Drawing.Color c = Under(e);
                e.Handled = true;
                swallowUp = true;
                End();
                picked(SettingsStore.ToHex(c));
            };
            // The button's release after the pick goes nowhere either.
            up = (s, e) =>
            {
                if (!swallowUp) return;
                swallowUp = false;
                e.Handled = true;
                window.PreviewMouseLeftButtonUp -= up;
            };
            right = (s, e) => { e.Handled = true; End(); };
            key = (s, e) => { if (e.Key == Key.Escape) { e.Handled = true; End(); } };
            lost = (s, e) => End();
            window.PreviewMouseMove += move;
            window.PreviewMouseLeftButtonDown += down;
            window.PreviewMouseLeftButtonUp += up;
            window.PreviewMouseRightButtonDown += right;
            window.PreviewKeyDown += key;
            window.Deactivated += lost;
        }

        /// <summary>The colour of one pixel on screen, at device coordinates.</summary>
        internal static System.Drawing.Color Sample(Point device)
        {
            try
            {
                using (var pixel = new System.Drawing.Bitmap(1, 1))
                {
                    using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(pixel))
                        g.CopyFromScreen((int)Math.Floor(device.X), (int)Math.Floor(device.Y), 0, 0, new System.Drawing.Size(1, 1));
                    System.Drawing.Color c = pixel.GetPixel(0, 0);
                    return System.Drawing.Color.FromArgb(255, c.R, c.G, c.B);
                }
            }
            catch
            {
                return System.Drawing.Color.Black;
            }
        }
    }
}
