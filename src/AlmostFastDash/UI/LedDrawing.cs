using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace AlmostFastDash.UI
{
    /// <summary>
    /// Lays out one device's LED dots the way the LEDs sit on the device: around the screen, along
    /// each bar. A device without a mapped layout falls back to a row in wire order.
    /// </summary>
    internal static class LedDrawing
    {
        /// <param name="dots">One element per LED, by LED number (from 0); null entries are skipped.</param>
        /// <param name="size">The dot size; LEDs are spaced a little wider than this.</param>
        public static FrameworkElement Arrange(DeviceSettings device, IList<FrameworkElement> dots, double size)
        {
            if (device.Mapped && device.Layout.Total == dots.Count)
                return Arrange(device.Layout.SlotOfLed().Select(s => s == null ? (Point?)null : new Point(s.X, s.Y)).ToList(), dots, size);
            var row = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
            foreach (FrameworkElement dot in dots.Where(d => d != null))
            {
                dot.Margin = new Thickness(size * 0.3);
                row.Children.Add(dot);
            }
            return row;
        }

        /// <summary>Places each dot at its point (in LED spacings) on a canvas just big enough for them.</summary>
        public static Canvas Arrange(IList<Point?> points, IList<FrameworkElement> dots, double size)
        {
            double spacing = size * 1.6;
            var placed = points.Where(p => p.HasValue).Select(p => p.Value).ToList();
            double minX = placed.Count == 0 ? 0 : placed.Min(p => p.X), minY = placed.Count == 0 ? 0 : placed.Min(p => p.Y);
            double maxX = placed.Count == 0 ? 0 : placed.Max(p => p.X), maxY = placed.Count == 0 ? 0 : placed.Max(p => p.Y);
            var canvas = new Canvas
            {
                Width = (maxX - minX) * spacing + size, Height = (maxY - minY) * spacing + size,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 4)
            };
            for (int i = 0; i < dots.Count && i < points.Count; i++)
            {
                if (dots[i] == null || !points[i].HasValue) continue;
                dots[i].Margin = new Thickness(0);
                Canvas.SetLeft(dots[i], (points[i].Value.X - minX) * spacing);
                Canvas.SetTop(dots[i], (points[i].Value.Y - minY) * spacing);
                canvas.Children.Add(dots[i]);
            }
            return canvas;
        }

        /// <summary>A dot size that keeps a device's drawing within a width.</summary>
        public static double FitSize(DeviceSettings device, double width, double largest)
        {
            double across = device.Mapped ? device.Layout.Slots().Select(s => s.X).DefaultIfEmpty(0).Max() + 1 : device.LedCount;
            return Math.Max(7, Math.Min(largest, width / (Math.Max(1, across) * 1.6)));
        }
    }
}
