using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AlmostFastOverlays
{
    public partial class DesignerWindow
    {
        // ==================================================================
        // Rulers along the canvas, and guides dragged out of them that widgets snap to
        // ==================================================================

        private const double RulerSize = 20;
        /// <summary>Rulers stay as they were left, for every design opened in this session.</summary>
        private static bool _rulersOn;
        private Ruler _rulerTop, _rulerLeft;
        private Border _rulerCorner;
        /// <summary>The guide being dragged: on which axis, which one (-1 for a new one), and where it is now.</summary>
        private bool _guideDragging, _guideVertical;
        private int _guideIndex = -1;
        private double _guideAt;
        private FrameworkElement _guideFrom;
        private static readonly SolidColorBrush GuideBrush = Freeze(new SolidColorBrush(Color.FromRgb(34, 211, 238)));

        private static SolidColorBrush Freeze(SolidColorBrush brush) { brush.Freeze(); return brush; }

        private void InitGuides()
        {
            var host = (Grid)CanvasScroller.Parent;
            Ruler Make(bool vertical) => new Ruler
            {
                Vertical = vertical, Origin = r => vertical ? Overlay.TranslatePoint(new Point(0, 0), r).Y : Overlay.TranslatePoint(new Point(0, 0), r).X,
                Scale = () => _zoom, Span = () => SelectionSpan(vertical), Cursor = vertical ? Cursors.SizeWE : Cursors.SizeNS,
                Background = (Brush)FindResource("Brush.Sidebar"), Line = (Brush)FindResource("Brush.TextTertiary"),
                Text = (Brush)FindResource("Brush.TextSecondary"), Mark = (Brush)FindResource("Brush.Accent"),
                ToolTip = "Drag onto the canvas for a guide; drag a guide back here to remove it. Right-click for more."
            };
            _rulerTop = Make(false);
            _rulerTop.Height = RulerSize;
            _rulerTop.VerticalAlignment = VerticalAlignment.Top;
            _rulerTop.Margin = new Thickness(RulerSize, 0, 0, 0);
            _rulerLeft = Make(true);
            _rulerLeft.Width = RulerSize;
            _rulerLeft.HorizontalAlignment = HorizontalAlignment.Left;
            _rulerLeft.Margin = new Thickness(0, RulerSize, 0, 0);
            _rulerCorner = new Border
            {
                Width = RulerSize, Height = RulerSize, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Background = (Brush)FindResource("Brush.Sidebar"), BorderBrush = (Brush)FindResource("Brush.Border"), BorderThickness = new Thickness(0, 0, 1, 1)
            };
            foreach (FrameworkElement ruler in new FrameworkElement[] { _rulerTop, _rulerLeft, _rulerCorner }) host.Children.Add(ruler);
            foreach (Ruler ruler in new[] { _rulerTop, _rulerLeft })
            {
                Ruler r = ruler;
                r.MouseLeftButtonDown += (s, e) => { BeginGuide(r.Vertical, -1, r); e.Handled = true; };
                r.MouseMove += (s, e) => { if (_guideDragging && _guideFrom == r) MoveGuide(e); };
                r.MouseLeftButtonUp += (s, e) => { if (_guideDragging && _guideFrom == r) EndGuide(e); };
                r.MouseRightButtonUp += (s, e) => { RulerMenu(r); e.Handled = true; };
            }
            CanvasScroller.ScrollChanged += (s, e) => InvalidateRulers();
            // Existing guides are picked up with the Move tool; the handles of a selection still come first.
            Overlay.PreviewMouseLeftButtonDown += (s, e) =>
            {
                if (ActiveTool != Tool.Move || _editPath != null) return;
                Point p = e.GetPosition(Overlay);
                if (HandleAt(p) >= 0) return;
                if (!GuideAt(p, out bool vertical, out int index)) return;
                BeginGuide(vertical, index, Overlay);
                e.Handled = true;
            };
            Overlay.PreviewMouseMove += (s, e) =>
            {
                if (_guideDragging && _guideFrom == Overlay) { MoveGuide(e); e.Handled = true; }
            };
            Overlay.PreviewMouseLeftButtonUp += (s, e) =>
            {
                if (_guideDragging && _guideFrom == Overlay) { EndGuide(e); e.Handled = true; }
            };
            // After the canvas's own pointer handling: a guide under the pointer shows it can be dragged, and the rulers mark where it is.
            Overlay.AddHandler(MouseMoveEvent, new MouseEventHandler((s, e) =>
            {
                Point p = e.GetPosition(Overlay);
                if (_rulersOn) { _rulerTop.Pointer = p.X; _rulerLeft.Pointer = p.Y; InvalidateRulers(); }
                if (_guideDragging || ActiveTool != Tool.Move || _drag != DragMode.None || HandleAt(p) >= 0) return;
                if (GuideAt(p, out bool vertical, out _)) Overlay.Cursor = vertical ? Cursors.SizeWE : Cursors.SizeNS;
            }), true);
            Overlay.MouseLeave += (s, e) => { if (_rulerTop == null) return; _rulerTop.Pointer = _rulerLeft.Pointer = null; InvalidateRulers(); };
            ShowRulers(_rulersOn);
        }

        private void RulersToggle_Click(object sender, RoutedEventArgs e) => ShowRulers(RulersToggle.IsChecked == true);

        private void ShowRulers(bool on)
        {
            _rulersOn = on;
            if (RulersToggle != null) RulersToggle.IsChecked = on;
            if (_rulerTop == null) return;
            Visibility shown = on ? Visibility.Visible : Visibility.Collapsed;
            _rulerTop.Visibility = _rulerLeft.Visibility = _rulerCorner.Visibility = shown;
            CanvasScroller.Margin = new Thickness(on ? RulerSize : 0, on ? RulerSize : 0, 0, 0);
            // A fitted view fits again, now with or without the rulers' room.
            if (_fitOnResize) Dispatcher.BeginInvoke(new Action(ZoomFit), System.Windows.Threading.DispatcherPriority.Loaded);
            InvalidateRulers();
        }

        private void InvalidateRulers()
        {
            if (!_rulersOn || _rulerTop == null) return;
            _rulerTop.InvalidateVisual();
            _rulerLeft.InvalidateVisual();
        }

        /// <summary>Where the selection runs along a ruler, marked on it.</summary>
        private Tuple<double, double> SelectionSpan(bool vertical)
        {
            if (_selection.Count == 0) return null;
            Rect r = Union(_selection);
            return vertical ? Tuple.Create(r.Top, r.Bottom) : Tuple.Create(r.Left, r.Right);
        }

        private List<double> GuidesOn(bool vertical) => vertical ? _layout.GuidesX : _layout.GuidesY;

        /// <summary>A guide within a few pixels (on screen) of the point.</summary>
        private bool GuideAt(Point p, out bool vertical, out int index)
        {
            double reach = 4 * Px;
            for (int i = 0; i < _layout.GuidesX.Count; i++)
                if (Math.Abs(_layout.GuidesX[i] - p.X) <= reach) { vertical = true; index = i; return true; }
            for (int i = 0; i < _layout.GuidesY.Count; i++)
                if (Math.Abs(_layout.GuidesY[i] - p.Y) <= reach) { vertical = false; index = i; return true; }
            vertical = false;
            index = -1;
            return false;
        }

        private void BeginGuide(bool vertical, int index, FrameworkElement from)
        {
            _guideDragging = true;
            _guideVertical = vertical;
            _guideIndex = index;
            _guideFrom = from;
            _guideAt = index >= 0 ? GuidesOn(vertical)[index] : double.NaN;
            from.CaptureMouse();
            UpdateOverlay();
        }

        private void MoveGuide(MouseEventArgs e)
        {
            Point p = e.GetPosition(Overlay);
            double at = _guideVertical ? p.X : p.Y;
            // Guides snap to whole pixels, and to the screen's edges and middle and widgets' edges and middles when close.
            bool free = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
            at = Math.Round(at);
            if (!free)
            {
                var targets = new List<double> { 0, _guideVertical ? CanvasW / 2 : CanvasH / 2, _guideVertical ? CanvasW : CanvasH };
                foreach (DashWidget w in _screen.Widgets.Where(w => !w.Hidden))
                    targets.AddRange(_guideVertical ? new[] { w.X, w.X + w.W / 2, w.X + w.W } : new[] { w.Y, w.Y + w.H / 2, w.Y + w.H });
                double best = targets.OrderBy(t => Math.Abs(t - at)).First();
                if (Math.Abs(best - at) <= 6 * Px) at = Math.Round(best, 1);
            }
            _guideAt = at;
            StatusText.Text = (_guideVertical ? "Guide at x " : "Guide at y ") + at.ToString("0.#", CultureInfo.InvariantCulture) +
                ". Drop it on the canvas, or back on the ruler to remove it. Alt stops it snapping.";
            UpdateOverlay();
        }

        private void EndGuide(MouseEventArgs e)
        {
            _guideFrom?.ReleaseMouseCapture();
            _guideDragging = false;
            Ruler home = _guideVertical ? _rulerLeft : _rulerTop;
            Point onRuler = e.GetPosition(home);
            bool overRuler = _rulersOn && (_guideVertical ? onRuler.X < RulerSize : onRuler.Y < RulerSize);
            List<double> guides = GuidesOn(_guideVertical);
            if (double.IsNaN(_guideAt) || overRuler)
            {
                if (_guideIndex >= 0)
                {
                    guides.RemoveAt(_guideIndex);
                    Commit("Remove guide");
                    StatusText.Text = "Removed the guide.";
                }
            }
            else
            {
                if (_guideIndex >= 0) guides[_guideIndex] = _guideAt;
                else guides.Add(_guideAt);
                Commit(_guideIndex >= 0 ? "Move guide" : "Add guide");
                StatusText.Text = "Widgets snap to guides as they move. Drag one back onto its ruler to remove it.";
            }
            _guideIndex = -1;
            _guideFrom = null;
            UpdateOverlay();
        }

        private void RulerMenu(FrameworkElement ruler)
        {
            var menu = new ContextMenu { PlacementTarget = ruler };
            if (_layout.GuidesX.Count + _layout.GuidesY.Count > 0) menu.Items.Add(MenuItem("Clear guides", null, ClearGuides));
            menu.Items.Add(MenuItem("Hide rulers", "Ctrl+R", () => ShowRulers(false)));
            menu.IsOpen = true;
        }

        private void ClearGuides()
        {
            if (_layout.GuidesX.Count + _layout.GuidesY.Count == 0) return;
            _layout.GuidesX.Clear();
            _layout.GuidesY.Clear();
            Commit("Clear guides");
            UpdateOverlay();
            StatusText.Text = "Cleared the guides.";
        }

        /// <summary>The guides as lines across the canvas and beyond it, with the one being dragged.</summary>
        private void AddGuideLines()
        {
            void Add(bool vertical, double at, bool dragging)
            {
                var line = new Line
                {
                    X1 = vertical ? at : -4000, Y1 = vertical ? -4000 : at, X2 = vertical ? at : CanvasW + 4000, Y2 = vertical ? CanvasH + 4000 : at,
                    Stroke = GuideBrush, StrokeThickness = 1 * Px, IsHitTestVisible = false, Opacity = dragging ? 1 : 0.75
                };
                if (dragging) line.StrokeDashArray = new DoubleCollection { 4, 3 };
                Overlay.Children.Add(line);
            }
            for (int i = 0; i < _layout.GuidesX.Count; i++)
                if (!(_guideDragging && _guideVertical && _guideIndex == i)) Add(true, _layout.GuidesX[i], false);
            for (int i = 0; i < _layout.GuidesY.Count; i++)
                if (!(_guideDragging && !_guideVertical && _guideIndex == i)) Add(false, _layout.GuidesY[i], false);
            if (_guideDragging && !double.IsNaN(_guideAt)) Add(_guideVertical, _guideAt, true);
            InvalidateRulers();
        }
    }

    /// <summary>A ruler along the canvas in the screen's own pixels, marking the selection's extent and the pointer.</summary>
    internal sealed class Ruler : FrameworkElement
    {
        private static readonly double[] Steps = { 1, 2, 5, 10, 20, 25, 50, 100, 200, 250, 500, 1000, 2000 };
        public bool Vertical;
        /// <summary>Where the canvas's 0 is along this ruler, in the ruler's own coordinates.</summary>
        public Func<Ruler, double> Origin;
        public Func<double> Scale;
        public Func<Tuple<double, double>> Span;
        public double? Pointer;
        public Brush Background, Line, Text, Mark;

        protected override void OnRender(DrawingContext dc)
        {
            double length = Vertical ? ActualHeight : ActualWidth, thick = Vertical ? ActualWidth : ActualHeight;
            if (length <= 0 || Origin == null || Scale == null) return;
            dc.DrawRectangle(Background, null, new Rect(0, 0, ActualWidth, ActualHeight));
            double origin = Origin(this), scale = Math.Max(0.01, Scale());
            Rect Along(double from, double to) => Vertical ? new Rect(0, from, thick, Math.Max(0, to - from)) : new Rect(from, 0, Math.Max(0, to - from), thick);

            Tuple<double, double> span = Span?.Invoke();
            if (span != null)
            {
                Brush band = Mark.Clone();
                band.Opacity = 0.18;
                dc.DrawRectangle(band, null, Along(origin + span.Item1 * scale, origin + span.Item2 * scale));
            }
            double major = Steps.FirstOrDefault(s => s * scale >= 60);
            if (major == 0) major = Steps[Steps.Length - 1];
            double minor = major / (major * scale >= 100 ? 10 : 5);
            if (minor * scale < 5) minor = major / 2;
            var pen = new Pen(Line, 1);
            var typeface = new Typeface("Segoe UI");
            double pixels = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            double first = Math.Floor(-origin / scale / minor) * minor, last = (length - origin) / scale;
            for (double v = first; v <= last; v += minor)
            {
                double at = Math.Round(origin + v * scale) + 0.5;
                if (at < 0) continue;
                bool isMajor = Math.Abs(v / major - Math.Round(v / major)) < 1e-6;
                double tick = isMajor ? thick : thick * 0.28;
                if (Vertical) dc.DrawLine(pen, new Point(thick - tick, at), new Point(thick, at));
                else dc.DrawLine(pen, new Point(at, thick - tick), new Point(at, thick));
                if (!isMajor) continue;
                var label = new FormattedText(Math.Round(v).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 9.5, Text, pixels);
                if (Vertical)
                {
                    // Read from the bottom up, just above its tick.
                    dc.PushTransform(new RotateTransform(-90, 3, at - 3));
                    dc.DrawText(label, new Point(3, at - 3));
                    dc.Pop();
                }
                else dc.DrawText(label, new Point(at + 3, 1));
            }
            var edge = new Pen(Line, 1) { DashStyle = DashStyles.Solid };
            if (Vertical) dc.DrawLine(edge, new Point(thick - 0.5, 0), new Point(thick - 0.5, length));
            else dc.DrawLine(edge, new Point(0, thick - 0.5), new Point(length, thick - 0.5));
            if (Pointer.HasValue)
            {
                double at = Math.Round(origin + Pointer.Value * scale) + 0.5;
                var mark = new Pen(Mark, 1);
                if (Vertical) dc.DrawLine(mark, new Point(0, at), new Point(thick, at));
                else dc.DrawLine(mark, new Point(at, 0), new Point(at, thick));
            }
        }
    }
}
