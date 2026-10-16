using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AlmostFastOverlays
{
    public partial class DesignerWindow
    {
        // ==================================================================
        // Layer thumbnails: each layer's part of the canvas, small, as an image editor shows them
        // ==================================================================

        private const int ThumbW = 34, ThumbH = 22;
        private readonly Dictionary<string, Image> _layerThumbs = new Dictionary<string, Image>();

        private Border LayerThumb(DashWidget widget)
        {
            var image = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(1) };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            _layerThumbs[widget.Id] = image;
            return new Border
            {
                Width = ThumbW, Height = ThumbH, CornerRadius = new CornerRadius(3), Background = (Brush)FindResource("Brush.Bg"), ClipToBounds = true,
                BorderBrush = (Brush)FindResource("Brush.Border"), BorderThickness = new Thickness(1), Child = image, VerticalAlignment = VerticalAlignment.Center,
                Opacity = widget.Hidden ? 0.4 : 1
            };
        }

        /// <summary>Fills the thumbnails from the canvas as last drawn: one copy of the frame, then each layer's box from it.</summary>
        private void UpdateLayerThumbs()
        {
            if (_frame == null || _layerThumbs.Count == 0) return;
            int width = _frame.PixelWidth, height = _frame.PixelHeight, stride = width * 4;
            var pixels = new byte[stride * height];
            _frame.CopyPixels(pixels, stride, 0);
            const int scale = 2;
            foreach (DashWidget w in _screen.Widgets)
            {
                if (!_layerThumbs.TryGetValue(w.Id, out Image image)) continue;
                int left = Math.Max(0, (int)Math.Floor(w.X)), top = Math.Max(0, (int)Math.Floor(w.Y));
                int right = Math.Min(width, (int)Math.Ceiling(w.X + w.W)), bottom = Math.Min(height, (int)Math.Ceiling(w.Y + w.H));
                if (right - left < 1 || bottom - top < 1) { image.Source = null; continue; }
                // Sampled down to twice the thumbnail's size, so it stays sharp on a high-DPI screen.
                double step = Math.Max((right - left) / (double)(ThumbW * scale), (bottom - top) / (double)(ThumbH * scale));
                step = Math.Max(1, step);
                int tw = Math.Max(1, (int)((right - left) / step)), th = Math.Max(1, (int)((bottom - top) / step));
                var thumb = new byte[tw * th * 4];
                for (int y = 0; y < th; y++)
                {
                    int sy = top + (int)(y * step);
                    for (int x = 0; x < tw; x++)
                    {
                        int sx = left + (int)(x * step), from = sy * stride + sx * 4, to = (y * tw + x) * 4;
                        thumb[to] = pixels[from];
                        thumb[to + 1] = pixels[from + 1];
                        thumb[to + 2] = pixels[from + 2];
                        thumb[to + 3] = pixels[from + 3];
                    }
                }
                var bitmap = BitmapSource.Create(tw, th, 96, 96, _frame.Format, null, thumb, tw * 4);
                bitmap.Freeze();
                image.Source = bitmap;
            }
        }
    }
}
