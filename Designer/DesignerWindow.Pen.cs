using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using DrawingPoint = System.Drawing.PointF;
using DrawingRect = System.Drawing.RectangleF;

namespace AlmostFastOverlays
{
    /// <summary>
    /// The Pen, and editing a path's points, as in a vector drawing tool. The Pen places points with clicks (corners) or
    /// click-and-drag (curves, pulling out handles); clicking the first point closes the shape, a double-click, Enter or
    /// Esc leaves it open. A path's points are edited on the canvas: double-click it (or Enter), drag points and
    /// handles, Alt-click a point to switch it between corner and curve, Delete removes it, Esc or Enter finishes.
    /// </summary>
    public partial class DesignerWindow
    {
        private readonly List<PathNode> _penNodes = new List<PathNode>();
        private bool _penPulling;
        private Point _penHover;

        private DashWidget _editPath;
        private List<PathNode> _editNodes;
        private bool _editClosed;
        private int _editNode = -1, _editPart;
        private bool _editDragging, _editChanged;

        private static DrawingPoint F(Point p) => new DrawingPoint((float)p.X, (float)p.Y);
        private static Point W(DrawingPoint p) => new Point(p.X, p.Y);

        /// <summary>Close enough to grab, in screen pixels whatever the zoom.</summary>
        private bool Near(Point a, Point b, double pixels = 7) => (a - b).Length <= pixels * Px;

        // ------------------------------------------------------------------ the Pen

        private bool PenMouseDown(MouseButtonEventArgs e)
        {
            Point p = SnapPoint(e.GetPosition(Overlay));
            if (e.ClickCount == 2)
            {
                FinishPen(false);
                return true;
            }
            if (_penNodes.Count >= 2 && Near(e.GetPosition(Overlay), W(_penNodes[0].P), 9))
            {
                FinishPen(true);
                return true;
            }
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 && _penNodes.Count > 0) p = Constrained(W(_penNodes[_penNodes.Count - 1].P), p);
            _penNodes.Add(new PathNode { P = F(p) });
            _penPulling = true;
            _penHover = p;
            Overlay.CaptureMouse();
            StatusText.Text = "Pen: click for a corner, drag for a curve. Click the first point to close; double-click, Enter or Esc to finish open; Backspace takes the last point back.";
            UpdateOverlay();
            return true;
        }

        private void PenMouseMove(MouseEventArgs e)
        {
            Point p = SnapPoint(e.GetPosition(Overlay));
            _penHover = p;
            if (_penPulling && e.LeftButton == MouseButtonState.Pressed && _penNodes.Count > 0)
            {
                // Dragging pulls the newest point's handles out evenly either side: a smooth curve through it.
                PathNode node = _penNodes[_penNodes.Count - 1];
                if ((p - W(node.P)).Length > 2 * Px)
                {
                    node.Out = F(p);
                    node.In = new DrawingPoint(2 * node.P.X - (float)p.X, 2 * node.P.Y - (float)p.Y);
                }
            }
            else if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 && _penNodes.Count > 0) _penHover = Constrained(W(_penNodes[_penNodes.Count - 1].P), p);
            UpdateOverlay();
        }

        private void PenMouseUp()
        {
            _penPulling = false;
            Overlay.ReleaseMouseCapture();
        }

        /// <summary>A point turned to the nearest 45° from another.</summary>
        private static Point Constrained(Point from, Point to)
        {
            Vector v = to - from;
            double step = Math.PI / 4, angle = Math.Round(Math.Atan2(v.Y, v.X) / step) * step;
            return new Point(Math.Round(from.X + v.Length * Math.Cos(angle)), Math.Round(from.Y + v.Length * Math.Sin(angle)));
        }

        /// <summary>The path drawn so far becomes a Path widget (two points at least), selected; the rail goes back to Move.</summary>
        private void FinishPen(bool closed)
        {
            _penPulling = false;
            Overlay.ReleaseMouseCapture();
            if (_penNodes.Count < 2)
            {
                _penNodes.Clear();
                UpdateOverlay();
                return;
            }
            DrawingRect box = PathNodes.Fit(_penNodes, out List<PathNode> local);
            DashWidget path = WidgetCatalog.Create("path", box.X, box.Y);
            path.W = box.Width;
            path.H = box.Height;
            path.Set("points", PathNodes.Format(local));
            path.Set("closed", closed ? "true" : "false");
            int number = _screen.Widgets.Count(w => (w.Name ?? "").StartsWith("Path ")) + 1;
            path.Name = "Path " + number;
            _penNodes.Clear();
            AddWidgets(new[] { path }, "Draw path");
            SetTool(Tool.Move);
            StatusText.Text = "Drew " + path.Name.ToLowerInvariant() + (closed ? " (closed)" : "") + ". Double-click it to edit its points; style it in Properties.";
        }

        private bool PenKey(Key key)
        {
            if (_penNodes.Count == 0) return false;
            switch (key)
            {
                case Key.Enter:
                case Key.Escape:
                    FinishPen(false);
                    return true;
                case Key.Back:
                case Key.Delete:
                    _penNodes.RemoveAt(_penNodes.Count - 1);
                    UpdateOverlay();
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>The path being drawn: its lines, its points (the first ringed when the pointer can close it), and the line to come.</summary>
        private void AddPenPreview(SolidColorBrush accent)
        {
            if (_tool != Tool.Pen || _penNodes.Count == 0) return;
            double thin = 1.5 * Px;
            Overlay.Children.Add(new Path { Data = Geometry(_penNodes, false), Stroke = accent, StrokeThickness = thin });
            PathNode last = _penNodes[_penNodes.Count - 1];
            if (!_penPulling)
            {
                // What the next click would add, curving out of the last point's handle.
                var next = new PathFigure { StartPoint = W(last.P) };
                next.Segments.Add(last.Out.HasValue ? (PathSegment)new BezierSegment(W(last.Out.Value), _penHover, _penHover, true) : new LineSegment(_penHover, true));
                var geometry = new PathGeometry();
                geometry.Figures.Add(next);
                Overlay.Children.Add(new Path { Data = geometry, Stroke = accent, StrokeThickness = thin, StrokeDashArray = new DoubleCollection { 4, 3 }, Opacity = 0.8 });
            }
            foreach (PathNode node in _penNodes) AddPoint(node, accent, node == last);
            if (_penNodes.Count >= 2 && Near(_penHover, W(_penNodes[0].P), 9))
            {
                double r = 7 * Px;
                var ring = new Ellipse { Width = r * 2, Height = r * 2, Stroke = accent, StrokeThickness = thin };
                Canvas.SetLeft(ring, _penNodes[0].P.X - r);
                Canvas.SetTop(ring, _penNodes[0].P.Y - r);
                Overlay.Children.Add(ring);
            }
        }

        /// <summary>A point as a small square, and with its handles when it's the one in hand.</summary>
        private void AddPoint(PathNode node, SolidColorBrush accent, bool withHandles, bool selected = false)
        {
            double s = 7 * Px;
            if (withHandles)
                foreach (DrawingPoint? handle in new[] { node.In, node.Out })
                {
                    if (!handle.HasValue) continue;
                    Overlay.Children.Add(new Line { X1 = node.P.X, Y1 = node.P.Y, X2 = handle.Value.X, Y2 = handle.Value.Y, Stroke = accent, StrokeThickness = 1 * Px, Opacity = 0.8 });
                    var dot = new Ellipse { Width = s, Height = s, Fill = accent };
                    Canvas.SetLeft(dot, handle.Value.X - s / 2);
                    Canvas.SetTop(dot, handle.Value.Y - s / 2);
                    Overlay.Children.Add(dot);
                }
            var square = new Rectangle { Width = s, Height = s, Fill = selected ? accent : Brushes.White, Stroke = accent, StrokeThickness = 1.2 * Px };
            Canvas.SetLeft(square, node.P.X - s / 2);
            Canvas.SetTop(square, node.P.Y - s / 2);
            Overlay.Children.Add(square);
        }

        private static PathGeometry Geometry(List<PathNode> nodes, bool closed)
        {
            var figure = new PathFigure { StartPoint = W(nodes[0].P), IsClosed = closed };
            int segments = closed ? nodes.Count : nodes.Count - 1;
            for (int i = 0; i < segments; i++)
            {
                PathNode from = nodes[i], to = nodes[(i + 1) % nodes.Count];
                if (from.Out.HasValue || to.In.HasValue)
                    figure.Segments.Add(new BezierSegment(W(from.Out ?? from.P), W(to.In ?? to.P), W(to.P), true));
                else figure.Segments.Add(new LineSegment(W(to.P), true));
            }
            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            return geometry;
        }

        // ------------------------------------------------------------------ editing a path's points

        /// <summary>Shows a path's points and handles on the canvas to drag.</summary>
        private void BeginEditPath(DashWidget widget)
        {
            if (widget?.Type != "path") return;
            _editPath = widget;
            Select(widget);
            LoadEditNodes();
            StatusText.Text = "Editing points: drag points and handles. Alt-click a point to switch corner and curve; Delete removes the one picked; Esc or Enter when done.";
            UpdateOverlay();
        }

        private void LoadEditNodes()
        {
            _editNodes = PathNodes.OnScreen(PathNodes.Parse(_editPath.Get("points", "")), RectOfF(_editPath)).Select(n => new PathNode { P = ToScreen(n.P), In = ToScreen(n.In), Out = ToScreen(n.Out) }).ToList();
            _editClosed = Expr.Bool(_editPath.Get("closed", "true"));
        }

        private void EndEditPath()
        {
            if (_editPath == null) return;
            _editPath = null;
            _editNodes = null;
            _editNode = -1;
            StatusText.Text = HintFor(_tool);
            UpdateOverlay();
        }

        private DrawingRect RectOfF(DashWidget w) => new DrawingRect((float)w.X, (float)w.Y, (float)w.W, (float)w.H);

        /// <summary>From the path's own frame to where it's drawn: flipped and turned about its middle as the widget is.</summary>
        private DrawingPoint ToScreen(DrawingPoint p) => F(PathFrame(_editPath, W(p), false));
        private DrawingPoint? ToScreen(DrawingPoint? p) => p.HasValue ? ToScreen(p.Value) : (DrawingPoint?)null;
        private DrawingPoint FromScreen(DrawingPoint p) => F(PathFrame(_editPath, W(p), true));

        private Point PathFrame(DashWidget w, Point p, bool back)
        {
            Rect r = RectOf(w);
            Point middle = Center(r);
            bool flipX = Expr.Bool(w.Get("flipX", "false")), flipY = Expr.Bool(w.Get("flipY", "false"));
            if (back) p = Turn(p, middle, -Angle(w));
            if (flipX) p.X = 2 * middle.X - p.X;
            if (flipY) p.Y = 2 * middle.Y - p.Y;
            if (!back) p = Turn(p, middle, Angle(w));
            return p;
        }

        private bool EditMouseDown(MouseButtonEventArgs e)
        {
            if (_editPath == null) return false;
            Point p = e.GetPosition(Overlay);
            int node = -1, part = 0;
            for (int i = 0; i < _editNodes.Count && node < 0; i++)
            {
                if (_editNodes[i].In.HasValue && Near(p, W(_editNodes[i].In.Value))) { node = i; part = 1; }
                else if (_editNodes[i].Out.HasValue && Near(p, W(_editNodes[i].Out.Value))) { node = i; part = 2; }
            }
            for (int i = 0; i < _editNodes.Count && node < 0; i++)
                if (Near(p, W(_editNodes[i].P))) { node = i; part = 0; }
            if (node < 0)
            {
                // A click away from the path's points ends editing and goes on as a normal click.
                EndEditPath();
                return false;
            }
            _editNode = node;
            _editPart = part;
            if (part == 0 && (Keyboard.Modifiers & ModifierKeys.Alt) != 0)
            {
                ToggleSmooth(node);
                ApplyEdit();
                Commit("Edit points");
                return true;
            }
            _editDragging = true;
            _editChanged = false;
            Overlay.CaptureMouse();
            UpdateOverlay();
            return true;
        }

        private bool EditMouseMove(MouseEventArgs e)
        {
            if (_editPath == null || !_editDragging) return false;
            Point p = SnapPoint(e.GetPosition(Overlay));
            PathNode n = _editNodes[_editNode];
            if (_editPart == 0)
            {
                // A point carries its handles with it.
                float dx = (float)p.X - n.P.X, dy = (float)p.Y - n.P.Y;
                n.P = F(p);
                if (n.In.HasValue) n.In = new DrawingPoint(n.In.Value.X + dx, n.In.Value.Y + dy);
                if (n.Out.HasValue) n.Out = new DrawingPoint(n.Out.Value.X + dx, n.Out.Value.Y + dy);
            }
            else
            {
                // A handle turns its partner the other way, keeping the curve smooth, unless Alt breaks them apart.
                bool apart = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
                DrawingPoint moved = F(p);
                if (_editPart == 1) n.In = moved; else n.Out = moved;
                DrawingPoint? partner = _editPart == 1 ? n.Out : n.In;
                if (!apart && partner.HasValue)
                {
                    double length = Math.Sqrt(Math.Pow(partner.Value.X - n.P.X, 2) + Math.Pow(partner.Value.Y - n.P.Y, 2));
                    double vx = n.P.X - moved.X, vy = n.P.Y - moved.Y, v = Math.Max(0.001, Math.Sqrt(vx * vx + vy * vy));
                    var opposite = new DrawingPoint((float)(n.P.X + vx / v * length), (float)(n.P.Y + vy / v * length));
                    if (_editPart == 1) n.Out = opposite; else n.In = opposite;
                }
            }
            _editChanged = true;
            ApplyEdit(fitBox: false);
            return true;
        }

        private bool EditMouseUp()
        {
            if (_editPath == null || !_editDragging) return false;
            _editDragging = false;
            Overlay.ReleaseMouseCapture();
            if (_editChanged)
            {
                ApplyEdit();
                Commit("Edit points");
                RefreshInspector();
            }
            UpdateOverlay();
            return true;
        }

        /// <summary>A corner becomes a curve (handles along its neighbors' direction), and a curve a corner.</summary>
        private void ToggleSmooth(int index)
        {
            PathNode n = _editNodes[index];
            if (n.In.HasValue || n.Out.HasValue)
            {
                n.In = n.Out = null;
                return;
            }
            int count = _editNodes.Count;
            DrawingPoint before = _editNodes[(index - 1 + count) % count].P, after = _editNodes[(index + 1) % count].P;
            if (!_editClosed && index == 0) before = n.P;
            if (!_editClosed && index == count - 1) after = n.P;
            float tx = (after.X - before.X) / 4, ty = (after.Y - before.Y) / 4;
            if (Math.Abs(tx) + Math.Abs(ty) < 1) tx = 20;
            n.In = new DrawingPoint(n.P.X - tx, n.P.Y - ty);
            n.Out = new DrawingPoint(n.P.X + tx, n.P.Y + ty);
        }

        /// <summary>
        /// Writes the edited points back to the widget. Fitting the box to the points (straight paths only, whose box
        /// can move without the path moving) happens once a drag ends.
        /// </summary>
        private void ApplyEdit(bool fitBox = true)
        {
            List<PathNode> frame = _editNodes.Select(n => new PathNode { P = FromScreen(n.P), In = n.In.HasValue ? FromScreen(n.In.Value) : (DrawingPoint?)null, Out = n.Out.HasValue ? FromScreen(n.Out.Value) : (DrawingPoint?)null }).ToList();
            bool straight = Math.Abs(Angle(_editPath)) < 0.01 && !Expr.Bool(_editPath.Get("flipX", "false")) && !Expr.Bool(_editPath.Get("flipY", "false"));
            List<PathNode> local;
            if (fitBox && straight)
            {
                DrawingRect box = PathNodes.Fit(frame, out local);
                _editPath.X = box.X;
                _editPath.Y = box.Y;
                _editPath.W = box.Width;
                _editPath.H = box.Height;
            }
            else
            {
                DrawingRect box = RectOfF(_editPath);
                local = frame.Select(n => new PathNode { P = Local(n.P), In = n.In.HasValue ? Local(n.In.Value) : (DrawingPoint?)null, Out = n.Out.HasValue ? Local(n.Out.Value) : (DrawingPoint?)null }).ToList();
                DrawingPoint Local(DrawingPoint p) => new DrawingPoint((p.X - box.X) / Math.Max(1, box.Width), (p.Y - box.Y) / Math.Max(1, box.Height));
            }
            _editPath.Set("points", PathNodes.Format(local));
            if (fitBox) LoadEditNodes();
            RequestRender();
            UpdateOverlay();
            RefreshPositionFields();
        }

        private bool EditKey(Key key)
        {
            if (_editPath == null) return false;
            switch (key)
            {
                case Key.Escape:
                case Key.Enter:
                    EndEditPath();
                    return true;
                case Key.Delete:
                case Key.Back:
                    if (_editNode >= 0 && _editNode < _editNodes.Count && _editNodes.Count > 2)
                    {
                        _editNodes.RemoveAt(_editNode);
                        _editNode = Math.Min(_editNode, _editNodes.Count - 1);
                        ApplyEdit();
                        Commit("Delete point");
                    }
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>The path being edited: its outline, every point, and the handles of curves.</summary>
        private void AddEditPreview(SolidColorBrush accent)
        {
            if (_editPath == null || _editNodes == null || _editNodes.Count < 2) return;
            if (!_screen.Widgets.Contains(_editPath)) { _editPath = null; return; }
            Overlay.Children.Add(new Path { Data = Geometry(_editNodes, _editClosed), Stroke = accent, StrokeThickness = 1.2 * Px, Opacity = 0.9 });
            for (int i = 0; i < _editNodes.Count; i++) AddPoint(_editNodes[i], accent, true, i == _editNode);
        }

        // ------------------------------------------------------------------ shapes into paths

        /// <summary>A shape (rectangle, ellipse, polygon, star, triangle, line or chevron) turned into a path in its place, to reshape point by point.</summary>
        private void ConvertToPath(DashWidget shape)
        {
            List<PathNode> nodes = PathNodes.FromShape(shape.Get("shape", "Rectangle"), (int)Expr.Num(shape.Get("sides", "6")), (float)Expr.Num(shape.Get("inner", "50")) / 100f, out bool closed);
            if (nodes == null) { StatusText.Text = "That shape can't be turned into a path."; return; }
            DashWidget path = WidgetCatalog.Create("path", shape.X, shape.Y);
            path.W = shape.W;
            path.H = shape.H;
            // Everything shapes and paths share comes along: fill, line, shape tools, style, transform, visibility.
            WidgetType pathType = WidgetCatalog.Find("path");
            foreach (PropDef def in pathType.Props)
            {
                string value = shape.Get(def.Key);
                if (value != null) path.Set(def.Key, value);
            }
            path.Set("points", PathNodes.Format(nodes));
            path.Set("closed", closed ? "true" : "false");
            path.Name = string.IsNullOrWhiteSpace(shape.Name) || shape.Name == "Shape" ? "Path" : shape.Name;
            path.Group = shape.Group;
            path.GroupName = shape.GroupName;
            path.Slot = shape.Slot;
            int index = _screen.Widgets.IndexOf(shape);
            _screen.Widgets[index] = path;
            _rendererDirty = true;
            Commit("Convert to path");
            BeginEditPath(path);
            RefreshLayers();
            RequestRender();
        }
    }
}
