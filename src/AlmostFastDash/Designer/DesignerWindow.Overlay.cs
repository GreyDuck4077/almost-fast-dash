using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AlmostFastDash.Designer
{
    public partial class DesignerWindow : Window
    {
        // ==================================================================
        // Overlay: grid, selection, handles, guides
        // ==================================================================

        private double Px => 1 / _zoom;

        private void UpdateOverlay()
        {
            Overlay.Children.Clear();
            var accent = (SolidColorBrush)FindResource("Brush.Accent");
            if (GridToggle.IsChecked == true) Overlay.Children.Add(GridRectangle());

            if (OutlineToggle.IsChecked == true)
                foreach (DashWidget w in _screen.Widgets.Where(w => !w.Hidden))
                    AddRect(RectOf(w), new SolidColorBrush(Color.FromArgb(110, 160, 170, 190)), 1, true);

            foreach (DashWidget w in _screen.Widgets.Where(w => !w.Hidden && _renderer != null && _renderer.Errors.ContainsKey(w.Id)))
                AddRect(RectOf(w), Brushes.OrangeRed, 1.5, true);
            AddGhostTags();

            if (_hover != null && !_selection.Contains(_hover) && _drag == DragMode.None)
                AddRect(RectOf(_hover), new SolidColorBrush(Color.FromArgb(150, accent.Color.R, accent.Color.G, accent.Color.B)), 1, false);

            foreach (DashWidget w in _selection)
            {
                // A turned widget's outline turns with it.
                AddRect(RectOf(w), accent, 1.5, false);
                double angle = Angle(w);
                if (angle != 0)
                {
                    Rect r = RectOf(w);
                    ((Rectangle)Overlay.Children[Overlay.Children.Count - 1]).RenderTransform = new RotateTransform(angle, r.Width / 2,
                        r.Height / 2);
                }
            }

            if (_selection.Count > 1)
            {
                Rect around = Union(_selection);
                AddRect(around, accent, 1, true);
                if (!_selection.Any(w => w.Locked))
                    foreach (Point p in HandlePoints(around))
                    {
                        double size = 7 * Px;
                        var handle = new Rectangle { Width = size, Height = size, Fill = accent, Stroke = Brushes.White,
                            StrokeThickness = 1 * Px, IsHitTestVisible = false };
                        Canvas.SetLeft(handle, p.X - size / 2);
                        Canvas.SetTop(handle, p.Y - size / 2);
                        Overlay.Children.Add(handle);
                    }
            }

            if (_selection.Count == 1 && !_selection[0].Locked && _editPath == null)
            {
                Rect r = RectOf(_selection[0]);
                if (CanRotate(_selection[0]))
                {
                    Point knob = RotateHandlePoint(_selection[0]);
                    Point stem = Turn(new Point(r.X + r.Width / 2, r.Y), Center(r), Angle(_selection[0]));
                    Overlay.Children.Add(new Line { X1 = stem.X, Y1 = stem.Y, X2 = knob.X, Y2 = knob.Y, Stroke = accent,
                        StrokeThickness = 1.2 * Px, IsHitTestVisible = false });
                    double d = 11 * Px;
                    var dial = new Ellipse { Width = d, Height = d, Fill = Brushes.White, Stroke = accent, StrokeThickness = 1.5 * Px,
                        ToolTip = "Drag to rotate · Shift snaps to 15°" };
                    Canvas.SetLeft(dial, knob.X - d / 2);
                    Canvas.SetTop(dial, knob.Y - d / 2);
                    Overlay.Children.Add(dial);
                }
                foreach (Point p in HandlePoints(_selection[0]))
                {
                    double size = 8 * Px;
                    var handle = new Rectangle { Width = size, Height = size, Fill = Brushes.White, Stroke = accent,
                        StrokeThickness = 1.5 * Px };
                    Canvas.SetLeft(handle, p.X - size / 2);
                    Canvas.SetTop(handle, p.Y - size / 2);
                    Overlay.Children.Add(handle);
                }
                var size2 = new Border
                {
                    Background = accent, CornerRadius = new CornerRadius(4 * Px), Padding = new Thickness(5 * Px, 1 * Px, 5 * Px, 1 * Px),
                    Child = new TextBlock { Text = Math.Round(r.Width) + " × " + Math.Round(r.Height), FontSize = 12 * Px,
                    Foreground = (Brush)FindResource("Brush.OnAccent"), FontWeight = FontWeights.SemiBold }
                };
                size2.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(size2, r.X + r.Width / 2 - size2.DesiredSize.Width / 2);
                Canvas.SetTop(size2, r.Bottom + 6 * Px);
                Overlay.Children.Add(size2);
            }

            AddGuideLines();
            foreach (Tuple<Point, Point> guide in _guides)
                Overlay.Children.Add(new Line
                {
                    X1 = guide.Item1.X, Y1 = guide.Item1.Y, X2 = guide.Item2.X, Y2 = guide.Item2.Y,
                    Stroke = new SolidColorBrush(Color.FromRgb(255, 64, 160)), StrokeThickness = 1 * Px
                });

            if (_drag == DragMode.Marquee)
            {
                var rect = new Rectangle
                {
                    Width = _marquee.Width, Height = _marquee.Height, Stroke = accent, StrokeThickness = 1 * Px,
                    Fill = new SolidColorBrush(Color.FromArgb(30, accent.Color.R, accent.Color.G, accent.Color.B))
                };
                Canvas.SetLeft(rect, _marquee.X);
                Canvas.SetTop(rect, _marquee.Y);
                Overlay.Children.Add(rect);
            }
            AddDrawingPreview(accent);
            AddPenPreview(accent);
            AddEditPreview(accent);
            UpdateStartCard();
        }

        private Rectangle _gridRect;

        private Rectangle GridRectangle()
        {
            if (_gridRect != null) return _gridRect;
            var drawing = new GeometryDrawing
            {
                Pen = new Pen(new SolidColorBrush(Color.FromArgb(13, 255, 255, 255)), 0.5),
                Geometry = new RectangleGeometry(new Rect(0, 0, GridDraw, GridDraw))
            };
            _gridRect = new Rectangle
            {
                Width = CanvasW, Height = CanvasH, IsHitTestVisible = false,
                Fill = new DrawingBrush(drawing) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, GridDraw, GridDraw),
                ViewportUnits = BrushMappingMode.Absolute }
            };
            return _gridRect;
        }

        private void AddRect(Rect r, Brush brush, double thickness, bool dashed)
        {
            var rect = new Rectangle { Width = Math.Max(0, r.Width), Height = Math.Max(0, r.Height), Stroke = brush,
                StrokeThickness = thickness * Px, IsHitTestVisible = false };
            if (dashed) rect.StrokeDashArray = new DoubleCollection { 4, 3 };
            Canvas.SetLeft(rect, r.X);
            Canvas.SetTop(rect, r.Y);
            Overlay.Children.Add(rect);
        }

        private static Rect RectOf(DashWidget w) => new Rect(w.X, w.Y, w.W, w.H);

        private static Rect Union(IEnumerable<DashWidget> widgets)
        {
            Rect? union = null;
            foreach (DashWidget w in widgets) union = union.HasValue ? Rect.Union(union.Value, RectOf(w)) : RectOf(w);
            return union ?? Rect.Empty;
        }

        private static Point[] HandlePoints(Rect r) => new[]
        {
            new Point(r.Left, r.Top), new Point(r.Left + r.Width / 2, r.Top), new Point(r.Right, r.Top),
                new Point(r.Right, r.Top + r.Height / 2),
            new Point(r.Right, r.Bottom), new Point(r.Left + r.Width / 2, r.Bottom), new Point(r.Left, r.Bottom),
            new Point(r.Left, r.Top + r.Height / 2)
        };

        private static readonly Cursor[] HandleCursors =
            { Cursors.SizeNWSE, Cursors.SizeNS, Cursors.SizeNESW, Cursors.SizeWE, Cursors.SizeNWSE, Cursors.SizeNS, Cursors.SizeNESW,
            Cursors.SizeWE };

        /// <summary>A widget's fixed rotation in degrees; 0 when it isn't turned or a formula turns it.</summary>
        private static double Angle(DashWidget w)
        {
            string raw = w.Get("rotate", "0") ?? "0";
            if (raw.StartsWith("=")) return 0;
            double a = Expr.Num(raw) % 360;
            return double.IsNaN(a) || Math.Abs(a) < 0.01 ? 0 : a;
        }

        private static Point Center(Rect r) => new Point(r.X + r.Width / 2, r.Y + r.Height / 2);

        private static Point Turn(Point p, Point about, double degrees)
        {
            if (degrees == 0) return p;
            double a = degrees * Math.PI / 180, cos = Math.Cos(a), sin = Math.Sin(a), dx = p.X - about.X, dy = p.Y - about.Y;
            return new Point(about.X + dx * cos - dy * sin, about.Y + dx * sin + dy * cos);
        }

        /// <summary>The selection handles of a widget, turned with it.</summary>
        private static Point[] HandlePoints(DashWidget w)
        {
            Rect r = RectOf(w);
            double angle = Angle(w);
            return HandlePoints(r).Select(p => Turn(p, Center(r), angle)).ToArray();
        }

        /// <summary>Where the rotate handle sits: above the top edge's middle, turned with the widget.</summary>
        private Point RotateHandlePoint(DashWidget w)
        {
            Rect r = RectOf(w);
            return Turn(new Point(r.X + r.Width / 2, r.Y - 26 * Px), Center(r), Angle(w));
        }

        /// <summary>The rotate handle shows unless a formula turns the widget.</summary>
        private static bool CanRotate(DashWidget w) => !(w.Get("rotate", "0") ?? "").StartsWith("=");

        private const int RotateHandle = 8;

        private int HandleAt(Point p)
        {
            if (_selection.Count > 1)
            {
                // Several selected: handles around them all.
                if (_selection.Any(w => w.Locked)) return -1;
                Point[] around = HandlePoints(Union(_selection));
                for (int i = 0; i < around.Length; i++)
                    if (Math.Abs(around[i].X - p.X) <= 7 * Px && Math.Abs(around[i].Y - p.Y) <= 7 * Px) return i;
                return -1;
            }
            if (_selection.Count != 1 || _selection[0].Locked) return -1;
            if (CanRotate(_selection[0]))
            {
                Point knob = RotateHandlePoint(_selection[0]);
                if (Math.Abs(knob.X - p.X) <= 8 * Px && Math.Abs(knob.Y - p.Y) <= 8 * Px) return RotateHandle;
            }
            Point[] points = HandlePoints(_selection[0]);
            double reach = 7 * Px;
            for (int i = 0; i < points.Length; i++)
                if (Math.Abs(points[i].X - p.X) <= reach && Math.Abs(points[i].Y - p.Y) <= reach) return i;
            return -1;
        }

        /// <summary>
        /// False when a widget's Show-when condition hides it in the current preview.
        /// Hidden widgets are skipped on the canvas (pick them in Layers) unless Outlines is on.
        /// </summary>
        private bool ShownNow(DashWidget w)
        {
            string visible = w.Get("visible", "");
            if (string.IsNullOrWhiteSpace(visible) || OutlineToggle.IsChecked == true) return true;
            return Expr.Bool(Expr.Evaluate(LayoutRenderer.Strip(visible), CurrentScope()));
        }

        private DashWidget WidgetAt(Point p)
        {
            double pad = 3 * Px;
            for (int i = _screen.Widgets.Count - 1; i >= 0; i--)
            {
                DashWidget w = _screen.Widgets[i];
                if (w.Hidden || w.Locked || !ShownNow(w)) continue;
                Rect r = RectOf(w);
                r.Inflate(pad, pad);
                // A turned widget is hit where it is drawn: test the point turned back into the widget's own frame.
                if (r.Contains(Turn(p, Center(r), -Angle(w)))) return w;
            }
            return null;
        }
    }
}
