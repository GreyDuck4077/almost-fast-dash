using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using DrawingBitmap = System.Drawing.Bitmap;

namespace AlmostFastDash.Designer
{
    public partial class DesignerWindow : Window
    {
        // ==================================================================
        // Zoom and view
        // ==================================================================

        private void SetZoom(double zoom, bool fromFit = false)
        {
            _zoom = Math.Max(0.25, Math.Min(4, zoom));
            _fitOnResize = fromFit;
            CanvasScale.ScaleX = CanvasScale.ScaleY = _zoom;
            ZoomLabel.Content = Math.Round(_zoom * 100) + "%";
            UpdateOverlay();
        }

        private void ZoomFit()
        {
            double w = CanvasScroller.ActualWidth - 100, h = CanvasScroller.ActualHeight - 140;
            if (w <= 0 || h <= 0) return;
            SetZoom(Math.Min(w / CanvasW, h / CanvasH), true);
        }

        private void ZoomIn_Click(object sender, RoutedEventArgs e) => SetZoom(_zoom * 1.25);

        private void ZoomOut_Click(object sender, RoutedEventArgs e) => SetZoom(_zoom / 1.25);

        private void ZoomFit_Click(object sender, RoutedEventArgs e) => ZoomFit();

        private void CanvasScroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            SetZoom(_zoom * (e.Delta > 0 ? 1.1 : 1 / 1.1));
            e.Handled = true;
        }

        private void ViewOption_Click(object sender, RoutedEventArgs e) => UpdateOverlay();

        // ==================================================================
        // Keyboard
        // ==================================================================

        private void FormulaReference_Click(object sender, RoutedEventArgs e) => FormulaReferenceWindow.Open(this, CurrentScope);
    }
}
