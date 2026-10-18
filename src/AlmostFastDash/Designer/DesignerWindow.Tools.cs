using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AlmostFastDash.Designer
{
    /// <summary>
    /// The tool rail, as in an image editor: Move (select, move and resize), shapes and text drawn straight onto the
    /// canvas, and Hand and Zoom for getting around. A drawn shape or text becomes an ordinary widget, selected and ready
    /// to style, and the rail goes back to Move.
    /// </summary>
    public partial class DesignerWindow
    {
        private enum Tool { Move, Rectangle, Ellipse, Polygon, Line, Pen, Text, Hand, Zoom }

        private Tool _tool = Tool.Move;
        /// <summary>Space held down: the Hand for as long as it's held, whatever the tool.</summary>
        private bool _spaceHand;
        private bool _drawing, _panning;
        private Point _drawStart, _drawEnd, _panStart;
        private double _panX, _panY;

        /// <summary>Sets up the rail: the library as it was left, the tool's cursor, and Space for the Hand.</summary>
        private void InitTools()
        {
            ShowLibrary(_libraryOpen);
            PreviewKeyUp += (s, e) =>
            {
                if (e.Key != Key.Space || !_spaceHand) return;
                _spaceHand = false;
                if (_panning) StopPanning();
                ToolChanged();
                e.Handled = true;
            };
            ToolChanged();
        }

        private Tool ActiveTool => _spaceHand ? Tool.Hand : _tool;

        private void Tool_Checked(object sender, RoutedEventArgs e)
        {
            if (!(sender is RadioButton button) || !Enum.TryParse((string)button.Tag, out Tool tool)) return;
            LeavingTool(tool);
            _tool = tool;
            ToolChanged();
        }

        /// <summary>A path half drawn with the Pen is kept (open) when another tool is picked; editing points ends.</summary>
        private void LeavingTool(Tool next)
        {
            if (_tool == Tool.Pen && next != Tool.Pen && _penNodes.Count > 0) FinishPen(false);
            if (next != Tool.Move && _editPath != null) EndEditPath();
        }

        private void SetTool(Tool tool)
        {
            LeavingTool(tool);
            _tool = tool;
            RadioButton button = ToolButton(tool);
            if (button != null && button.IsChecked != true) button.IsChecked = true;
            ToolChanged();
        }

        private RadioButton ToolButton(Tool tool)
        {
            switch (tool)
            {
                case Tool.Rectangle: return ToolRectangle;
                case Tool.Ellipse: return ToolEllipse;
                case Tool.Polygon: return ToolPolygon;
                case Tool.Line: return ToolLine;
                case Tool.Pen: return ToolPen;
                case Tool.Text: return ToolText;
                case Tool.Hand: return ToolHand;
                case Tool.Zoom: return ToolZoom;
                default: return ToolMove;
            }
        }

        private void ToolChanged()
        {
            if (Overlay == null) return;
            Overlay.Cursor = CursorFor(ActiveTool);
            if (StatusText != null) StatusText.Text = HintFor(_tool);
            // Handles and hover outlines are for moving; other tools hide them.
            if (ActiveTool != Tool.Move) _hover = null;
            UpdateOverlay();
        }

        private static Cursor CursorFor(Tool tool)
        {
            switch (tool)
            {
                case Tool.Move: return Cursors.Arrow;
                case Tool.Text: return Cursors.IBeam;
                case Tool.Hand: return Cursors.ScrollAll;
                case Tool.Zoom: return Cursors.Hand;
                default: return Cursors.Cross;
            }
        }

        private static string HintFor(Tool tool)
        {
            switch (tool)
            {
                case Tool.Rectangle: return "Rectangle: drag to draw. Shift for a square, Alt to draw from the middle.";
                case Tool.Ellipse: return "Ellipse: drag to draw. Shift for a circle, Alt to draw from the middle.";
                case Tool.Line: return "Line: drag from end to end. Shift for 45° steps.";
                case Tool.Polygon: return "Polygon: drag to draw. Sides and star are in Properties.";
                case Tool.Pen: return "Pen: click for corners, drag for curves. Click the first point to close, or press Enter to finish.";
                case Tool.Text: return "Text: click or drag a box, then type in Properties.";
                case Tool.Hand: return "Hand: drag to pan. Hold Space to pan with any tool.";
                case Tool.Zoom: return "Zoom: click to zoom in, Alt+click to zoom out.";
                default: return "Drag from the Library or draw with the tools. Ctrl+/ shows every shortcut.";
            }
        }

        /// <summary>The rail's keys, as in image editors: V, U (Shift+U for the next shape), T, H, Z, and Space held for the
        /// Hand.</summary>
        private bool ToolKey(Key key, bool shift, KeyEventArgs e)
        {
            switch (key)
            {
                case Key.V: SetTool(Tool.Move); return true;
                case Key.U:
                    SetTool(!shift ? (_tool == Tool.Ellipse || _tool == Tool.Polygon || _tool == Tool.Line ? _tool : Tool.Rectangle)
                        : _tool == Tool.Rectangle ? Tool.Ellipse : _tool == Tool.Ellipse ? Tool.Polygon : _tool == Tool.Polygon ? Tool.Line
                            : Tool.Rectangle);
                    return true;
                case Key.P: SetTool(Tool.Pen); return true;
                case Key.Enter:
                    // Enter on a selected path edits its points.
                    if (_tool == Tool.Move && _editPath == null && _selection.Count == 1 && _selection[0].Type == "path")
                    {
                        BeginEditPath(_selection[0]);
                        return true;
                    }
                    return false;
                case Key.T: SetTool(Tool.Text); return true;
                case Key.H: SetTool(Tool.Hand); return true;
                case Key.Z: SetTool(Tool.Zoom); return true;
                case Key.Space:
                    // Only over the canvas: elsewhere Space still presses buttons and ticks boxes.
                    if (!CanvasScroller.IsMouseOver && !Equals(Keyboard.FocusedElement, Overlay)) return false;
                    if (!e.IsRepeat && !_spaceHand)
                    {
                        _spaceHand = true;
                        ToolChanged();
                    }
                    return true;
                case Key.Escape:
                    if (_drawing)
                    {
                        _drawing = false;
                        Overlay.ReleaseMouseCapture();
                        UpdateOverlay();
                        return true;
                    }
                    if (_tool != Tool.Move)
                    {
                        SetTool(Tool.Move);
                        return true;
                    }
                    return false;
                default: return false;
            }
        }

        // ------------------------------------------------------------------ the mouse on the canvas

        /// <summary>A press on the canvas, for any tool but Move (which the canvas handles itself). True when taken.</summary>
        private bool ToolMouseDown(MouseButtonEventArgs e)
        {
            switch (ActiveTool)
            {
                case Tool.Move:
                    return false;
                case Tool.Hand:
                    _panning = true;
                    _panStart = e.GetPosition(CanvasScroller);
                    _panX = CanvasScroller.HorizontalOffset;
                    _panY = CanvasScroller.VerticalOffset;
                    Overlay.CaptureMouse();
                    return true;
                case Tool.Zoom:
                    ZoomAt(e.GetPosition(Overlay), (Keyboard.Modifiers & ModifierKeys.Alt) != 0 ? 1 / 1.25 : 1.25);
                    return true;
                case Tool.Pen:
                    return PenMouseDown(e);
                default:
                    _drawing = true;
                    _drawStart = _drawEnd = SnapPoint(e.GetPosition(Overlay));
                    Overlay.CaptureMouse();
                    UpdateOverlay();
                    return true;
            }
        }

        private bool ToolMouseMove(MouseEventArgs e)
        {
            if (_panning)
            {
                Point p = e.GetPosition(CanvasScroller);
                CanvasScroller.ScrollToHorizontalOffset(_panX - (p.X - _panStart.X));
                CanvasScroller.ScrollToVerticalOffset(_panY - (p.Y - _panStart.Y));
                return true;
            }
            if (_drawing)
            {
                _drawEnd = SnapPoint(e.GetPosition(Overlay));
                Rect r = DrawnRect();
                CursorText.Text = _tool == Tool.Line ? $"length {Math.Round((LineEnd() - _drawStart).Length)}"
                    : $"w {Math.Round(r.Width)}   h {Math.Round(r.Height)}";
                UpdateOverlay();
                return true;
            }
            if (ActiveTool == Tool.Pen)
            {
                PenMouseMove(e);
                return true;
            }
            if (ActiveTool == Tool.Move) return false;
            Overlay.Cursor = CursorFor(ActiveTool);
            return true;
        }

        private bool ToolMouseUp()
        {
            if (_panning)
            {
                StopPanning();
                return true;
            }
            if (ActiveTool == Tool.Pen)
            {
                PenMouseUp();
                return true;
            }
            if (!_drawing) return ActiveTool != Tool.Move;
            _drawing = false;
            Overlay.ReleaseMouseCapture();
            FinishDrawing();
            return true;
        }

        private void StopPanning()
        {
            _panning = false;
            Overlay.ReleaseMouseCapture();
        }

        /// <summary>To the 4-pixel grid while snapping is on (Alt or the Snap switch off frees it), to whole pixels otherwise.</summary>
        private Point SnapPoint(Point p)
        {
            bool free = (Keyboard.Modifiers & ModifierKeys.Alt) != 0 || SnapToggle.IsChecked != true;
            double step = free ? 1 : GridSize;
            return new Point(Math.Round(p.X / step) * step, Math.Round(p.Y / step) * step);
        }

        /// <summary>The box being drawn: Shift keeps it square, Alt grows it from where the drag started.</summary>
        private Rect DrawnRect()
        {
            double dx = _drawEnd.X - _drawStart.X, dy = _drawEnd.Y - _drawStart.Y;
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            {
                double side = Math.Max(Math.Abs(dx), Math.Abs(dy));
                dx = (dx < 0 ? -1 : 1) * side;
                dy = (dy < 0 ? -1 : 1) * side;
            }
            if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0)
                return new Rect(_drawStart.X - Math.Abs(dx), _drawStart.Y - Math.Abs(dy), Math.Abs(dx) * 2, Math.Abs(dy) * 2);
            return new Rect(_drawStart, new Point(_drawStart.X + dx, _drawStart.Y + dy));
        }

        /// <summary>Where a line being drawn ends: Shift turns it to the nearest 45°.</summary>
        private Point LineEnd()
        {
            if ((Keyboard.Modifiers & ModifierKeys.Shift) == 0) return _drawEnd;
            Vector v = _drawEnd - _drawStart;
            double step = Math.PI / 4, angle = Math.Round(Math.Atan2(v.Y, v.X) / step) * step;
            return new Point(Math.Round(_drawStart.X + v.Length * Math.Cos(angle)), Math.Round(_drawStart.Y + v.Length * Math.Sin(angle)));
        }

        /// <summary>The dashed outline of what's being drawn, over the canvas.</summary>
        private void AddDrawingPreview(SolidColorBrush accent)
        {
            if (!_drawing) return;
            var dashes = new DoubleCollection { 4, 3 };
            double thickness = 1.5 * Px;
            if (_tool == Tool.Line)
            {
                Point end = LineEnd();
                Overlay.Children.Add(new Line { X1 = _drawStart.X, Y1 = _drawStart.Y, X2 = end.X, Y2 = end.Y, Stroke = accent,
                    StrokeThickness = thickness, StrokeDashArray = dashes });
                return;
            }
            Rect r = DrawnRect();
            if (_tool == Tool.Polygon)
            {
                var polygon = new Polygon { Stroke = accent, StrokeThickness = thickness, StrokeDashArray = dashes,
                    Fill = new SolidColorBrush(Color.FromArgb(24, accent.Color.R, accent.Color.G, accent.Color.B)) };
                foreach (System.Drawing.PointF p in PathNodes.Polygon(6, 1)) polygon.Points.Add(new Point(r.X + p.X * r.Width,
                    r.Y + p.Y * r.Height));
                Overlay.Children.Add(polygon);
                return;
            }
            Shape outline = _tool == Tool.Ellipse ? (Shape)new Ellipse() : new Rectangle();
            outline.Width = Math.Max(1, r.Width);
            outline.Height = Math.Max(1, r.Height);
            outline.Stroke = accent;
            outline.StrokeThickness = thickness;
            outline.StrokeDashArray = dashes;
            outline.Fill = new SolidColorBrush(Color.FromArgb(24, accent.Color.R, accent.Color.G, accent.Color.B));
            Canvas.SetLeft(outline, r.X);
            Canvas.SetTop(outline, r.Y);
            Overlay.Children.Add(outline);
        }

        /// <summary>Turns what was drawn into a widget: a shape (rectangle, ellipse or line) or a text, selected and ready to
        /// style.</summary>
        private void FinishDrawing()
        {
            DashWidget widget;
            string kind;
            if (_tool == Tool.Line)
            {
                widget = LineWidget(_drawStart, LineEnd());
                kind = "Line";
            }
            else
            {
                Rect r = DrawnRect();
                // A click without a drag makes one of a usual size, from where it was clicked.
                bool clicked = r.Width < 4 && r.Height < 4;
                if (_tool == Tool.Text)
                {
                    kind = "Text";
                    widget = WidgetCatalog.Create("text", r.X, r.Y);
                    if (!clicked) { widget.W = Math.Max(24, r.Width); widget.H = Math.Max(16, r.Height); }
                    else widget.Y = Math.Round(r.Y - widget.H / 2);
                }
                else
                {
                    kind = _tool == Tool.Ellipse ? "Ellipse" : _tool == Tool.Polygon ? "Polygon" : "Rectangle";
                    widget = WidgetCatalog.Create("shape", r.X, r.Y);
                    widget.Set("shape", kind);
                    // Drawn rectangles start with square corners, as in any drawing tool.
                    if (kind == "Rectangle") widget.Set("radius", "0");
                    if (!clicked) { widget.W = Math.Max(2, r.Width); widget.H = Math.Max(2, r.Height); }
                }
            }
            int number = _screen.Widgets.Count(w => (w.Name ?? "").StartsWith(kind + " ")) + 1;
            widget.Name = kind + " " + number;
            AddWidgets(new[] { widget }, "Draw " + kind.ToLowerInvariant());
            SetTool(Tool.Move);
            if (kind == "Text") FocusPrimaryEditor();
            else StatusText.Text = "Drew " + widget.Name.ToLowerInvariant() + ".";
        }

        /// <summary>
        /// A line from one point to another as a Shape: level and upright lines keep a thin box around them; any other
        /// goes corner to corner of its box, falling or rising.
        /// </summary>
        private static DashWidget LineWidget(Point a, Point b)
        {
            const double around = 6;
            double dx = b.X - a.X, dy = b.Y - a.Y;
            if (Math.Abs(dx) < 2 && Math.Abs(dy) < 2) { b = new Point(a.X + 120, a.Y); dx = 120; dy = 0; }
            DashWidget line;
            if (Math.Abs(dy) < 1)
            {
                line = WidgetCatalog.Create("shape", Math.Min(a.X, b.X), a.Y - around);
                line.W = Math.Abs(dx);
                line.H = around * 2;
                line.Set("shape", "Line");
            }
            else if (Math.Abs(dx) < 1)
            {
                line = WidgetCatalog.Create("shape", a.X - around, Math.Min(a.Y, b.Y));
                line.W = around * 2;
                line.H = Math.Abs(dy);
                line.Set("shape", "Line");
            }
            else
            {
                line = WidgetCatalog.Create("shape", Math.Min(a.X, b.X), Math.Min(a.Y, b.Y));
                line.W = Math.Abs(dx);
                line.H = Math.Abs(dy);
                line.Set("shape", dx * dy > 0 ? "Diagonal down" : "Diagonal up");
            }
            return line;
        }

        /// <summary>Zooms in or out with the point under the pointer staying where it is.</summary>
        private void ZoomAt(Point at, double factor)
        {
            Point before = Overlay.TranslatePoint(at, CanvasScroller);
            SetZoom(_zoom * factor);
            CanvasScroller.UpdateLayout();
            Point after = Overlay.TranslatePoint(at, CanvasScroller);
            CanvasScroller.ScrollToHorizontalOffset(CanvasScroller.HorizontalOffset + after.X - before.X);
            CanvasScroller.ScrollToVerticalOffset(CanvasScroller.VerticalOffset + after.Y - before.Y);
        }
    }
}
