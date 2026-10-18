using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AlmostFastDash.Designer
{
    public partial class DesignerWindow : Window
    {
        // ==================================================================
        // Mouse
        // ==================================================================

        private void Overlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Overlay.Focus();
            Keyboard.Focus(Overlay);
            // A path's points being edited come first; then drawing, panning and zooming tools; Move goes on below.
            if (ActiveTool == Tool.Move && EditMouseDown(e)) return;
            if (ToolMouseDown(e)) return;
            Point p = e.GetPosition(Overlay);
            _dragStart = p;
            _dragChanged = false;
            bool additive = (Keyboard.Modifiers & (ModifierKeys.Shift | ModifierKeys.Control)) != 0;

            int handle = HandleAt(p);
            if (handle == RotateHandle)
            {
                _drag = DragMode.Rotate;
                _originals = _selection.ToDictionary(w => w, RectOf);
                Overlay.CaptureMouse();
                return;
            }
            if (handle >= 0)
            {
                _drag = DragMode.Resize;
                _handle = handle;
                _originals = _selection.ToDictionary(w => w, RectOf);
                Overlay.CaptureMouse();
                return;
            }

            DashWidget hit = WidgetAt(p);
            if (hit != null)
            {
                // A click takes a widget's whole group; a double-click picks out just that widget.
                if (e.ClickCount == 2)
                {
                    // A path opens for its points to be edited; anything else picks out that widget.
                    if (hit.Type == "path") { BeginEditPath(hit); return; }
                    Select(hit);
                    FocusPrimaryEditor();
                    return;
                }
                List<DashWidget> members = GroupOf(hit);
                if (additive)
                {
                    if (_selection.Contains(hit)) _selection.RemoveAll(members.Contains); else _selection.AddRange(members.Where(m => !_selection.Contains(m)));
                    SelectionChanged();
                }
                else if (!_selection.Contains(hit))
                {
                    _selection.Clear();
                    _selection.AddRange(members);
                    SelectionChanged();
                }
                if (_selection.Contains(hit))
                {
                    _drag = DragMode.Move;
                    _originals = _selection.Where(w => !w.Locked).ToDictionary(w => w, RectOf);
                    Overlay.CaptureMouse();
                }
                return;
            }

            if (!additive && _selection.Count > 0) { _selection.Clear(); SelectionChanged(); }
            _drag = DragMode.Marquee;
            _marquee = new Rect(p, p);
            Overlay.CaptureMouse();
        }

        private void Overlay_MouseMove(object sender, MouseEventArgs e)
        {
            Point p = e.GetPosition(Overlay);
            CursorText.Text = $"x {Math.Round(p.X)}   y {Math.Round(p.Y)}";
            if (EditMouseMove(e)) return;
            if (ToolMouseMove(e)) return;
            bool free = (Keyboard.Modifiers & ModifierKeys.Alt) != 0 || SnapToggle.IsChecked != true;

            switch (_drag)
            {
                case DragMode.None:
                {
                    int handle = HandleAt(p);
                    // Handles keep pointing the way they face on a turned widget.
                    int turned = handle < 0 || handle == RotateHandle ? -1 : ((handle + (int)Math.Round(Angle(_selection[0]) / 45)) % 8 + 8) % 8;
                    Overlay.Cursor = handle == RotateHandle ? Cursors.Hand : handle >= 0 ? HandleCursors[turned] : WidgetAt(p) != null ? Cursors.SizeAll : Cursors.Arrow;
                    DashWidget hover = WidgetAt(p);
                    if (hover != _hover) { _hover = hover; UpdateOverlay(); }
                    return;
                }
                case DragMode.Move:
                    DoMove(p, free);
                    break;
                case DragMode.Resize:
                    DoResize(p, free, (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
                    break;
                case DragMode.Rotate:
                {
                    // The angle from the widget's middle to the pointer, measured from straight up; Shift snaps to 15°.
                    DashWidget w = _selection[0];
                    Point middle = Center(_originals[w]);
                    double angle = Math.Atan2(p.Y - middle.Y, p.X - middle.X) * 180 / Math.PI + 90;
                    angle = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? Math.Round(angle / 15) * 15 : Math.Round(angle);
                    angle = ((angle + 180) % 360 + 360) % 360 - 180;
                    w.Set("rotate", angle.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    _dragChanged = true;
                    CursorText.Text = "Rotate " + angle + "°";
                    break;
                }
                case DragMode.Marquee:
                    _marquee = new Rect(_dragStart, p);
                    _selection.Clear();
                    _selection.AddRange(_screen.Widgets.Where(w => !w.Hidden && !w.Locked && ShownNow(w) && _marquee.IntersectsWith(RectOf(w))));
                    UpdateOverlay();
                    return;
            }
            RequestRender();
            RefreshPositionFields();
        }

        private void Overlay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (EditMouseUp()) return;
            if (ToolMouseUp()) return;
            DragMode mode = _drag;
            _drag = DragMode.None;
            Overlay.ReleaseMouseCapture();
            _guides.Clear();
            if (mode == DragMode.Marquee) SelectionChanged();
            else if ((mode == DragMode.Move || mode == DragMode.Resize || mode == DragMode.Rotate) && _dragChanged)
            {
                Commit(mode == DragMode.Move ? "Move" : mode == DragMode.Rotate ? "Rotate" : "Resize");
                RefreshInspector();
            }
            UpdateOverlay();
        }

        private void Overlay_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            Point p = e.GetPosition(Overlay);
            DashWidget hit = WidgetAt(p);
            if (hit != null && !_selection.Contains(hit)) Select(hit);
            var menu = new ContextMenu();
            if (_selection.Count > 0)
            {
                if (_selection.Count == 1 && _selection[0].Type == "path")
                {
                    DashWidget path = _selection[0];
                    menu.Items.Add(MenuItem("Edit points", "Enter", () => BeginEditPath(path)));
                    menu.Items.Add(new Separator());
                }
                else if (_selection.Count == 1 && _selection[0].Type == "shape" && PathNodes.FromShape(_selection[0].Get("shape", "Rectangle"), 6, 0.5f, out _) != null)
                {
                    DashWidget shape = _selection[0];
                    menu.Items.Add(MenuItem("Convert to path", null, () => ConvertToPath(shape)));
                    menu.Items.Add(new Separator());
                }
                bool allClipped = _selection.All(w => Expr.Bool(w.Get("clip", "false")));
                menu.Items.Add(MenuItem(allClipped ? "Release clipping mask" : "Clip to layer below", "Ctrl+Alt+G", () => ToggleClip(_selection.ToList())));
                menu.Items.Add(MenuItem("Duplicate", "Ctrl+D", () => DuplicateSelection()));
                menu.Items.Add(MenuItem("Repeat…", "Ctrl+Shift+D", RepeatSelection));
                menu.Items.Add(MenuItem("Zoom to selection", "Shift+2", ZoomToSelection));
                menu.Items.Add(MenuItem("Copy", "Ctrl+C", CopySelection));
                menu.Items.Add(MenuItem("Delete", "Del", DeleteSelection));
                menu.Items.Add(new Separator());
                menu.Items.Add(MenuItem("Bring to front", null, () => Reorder(int.MaxValue)));
                menu.Items.Add(MenuItem("Bring forward", "]", () => Reorder(1)));
                menu.Items.Add(MenuItem("Send backward", "[", () => Reorder(-1)));
                menu.Items.Add(MenuItem("Send to back", null, () => Reorder(int.MinValue)));
                menu.Items.Add(new Separator());
                menu.Items.Add(MenuItem(_selection.All(w => w.Locked) ? "Unlock" : "Lock", "Ctrl+L", ToggleLock));
                menu.Items.Add(MenuItem("Hide", null, () => { foreach (DashWidget w in _selection) w.Hidden = true; _selection.Clear(); Commit("Hide"); SelectionChanged(); }));
                if (_layout.Screens.Count > 1)
                {
                    var move = new MenuItem { Header = "Move to screen" };
                    foreach (DashScreen screen in _layout.Screens.Where(s => s != _screen))
                    {
                        DashScreen target = screen;
                        move.Items.Add(MenuItem(target.Name, null, () => MoveToScreen(target)));
                    }
                    menu.Items.Add(move);
                }
                menu.Items.Add(new Separator());
                menu.Items.Add(MenuItem("Save as module…", "Ctrl+M", SaveSelectionAsModule));
            }
            if (_clipboard != null) menu.Items.Add(MenuItem("Paste", "Ctrl+V", () => Paste(p)));
            menu.Items.Add(MenuItem("Select all", "Ctrl+A", SelectAll));
            if (_layout.GuidesX.Count + _layout.GuidesY.Count > 0) menu.Items.Add(MenuItem("Clear guides", null, ClearGuides));
            menu.Items.Add(MenuItem(_rulersOn ? "Hide rulers" : "Show rulers", "Ctrl+R", () => ShowRulers(!_rulersOn)));
            menu.IsOpen = true;
        }

        private static MenuItem MenuItem(string header, string gesture, Action action)
        {
            var item = new MenuItem { Header = header, InputGestureText = gesture ?? "" };
            item.Click += (s, e) => action();
            return item;
        }

        // ------------------------------------------------------------------ move & resize with snapping

        private void DoMove(Point p, bool free)
        {
            if (_originals == null || _originals.Count == 0) return;
            double dx = p.X - _dragStart.X, dy = p.Y - _dragStart.Y;
            if (Math.Abs(dx) + Math.Abs(dy) > 0.5) _dragChanged = true;
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) { if (Math.Abs(dx) > Math.Abs(dy)) dy = 0; else dx = 0; }
            Rect union = Rect.Empty;
            foreach (Rect r in _originals.Values) union = union.IsEmpty ? r : Rect.Union(union, r);
            Rect moved = new Rect(union.X + dx, union.Y + dy, union.Width, union.Height);
            _guides.Clear();
            if (!free)
            {
                dx += SnapAxis(new[] { moved.Left, moved.Left + moved.Width / 2, moved.Right }, true, moved);
                dy += SnapAxis(new[] { moved.Top, moved.Top + moved.Height / 2, moved.Bottom }, false, new Rect(union.X + dx, moved.Y, moved.Width, moved.Height));
            }
            foreach (KeyValuePair<DashWidget, Rect> pair in _originals)
            {
                pair.Key.X = Math.Round(pair.Value.X + dx);
                pair.Key.Y = Math.Round(pair.Value.Y + dy);
            }
        }

        private void DoResize(Point p, bool free, bool keepAspect)
        {
            if (_selection.Count > 1)
            {
                ResizeTogether(p, free, keepAspect);
                return;
            }
            DashWidget w = _selection[0];
            Rect o = _originals[w];
            double angle = Angle(w);
            // A turned widget is resized in its own frame: the pointer is turned back about the original middle.
            if (angle != 0) p = Turn(p, Center(o), -angle);
            double left = o.Left, top = o.Top, right = o.Right, bottom = o.Bottom;
            bool l = _handle == 0 || _handle == 6 || _handle == 7, r = _handle == 2 || _handle == 3 || _handle == 4;
            bool t = _handle == 0 || _handle == 1 || _handle == 2, b = _handle == 4 || _handle == 5 || _handle == 6;
            _guides.Clear();
            double x = p.X, y = p.Y;
            if (!free && angle == 0)
            {
                if (l || r) x += SnapAxis(new[] { x }, true, new Rect(x, top, 0, bottom - top));
                if (t || b) y += SnapAxis(new[] { y }, false, new Rect(left, y, right - left, 0));
            }
            if (l) left = Math.Min(x, right - 4);
            if (r) right = Math.Max(x, left + 4);
            if (t) top = Math.Min(y, bottom - 4);
            if (b) bottom = Math.Max(y, top + 4);
            if (keepAspect && o.Width > 0 && o.Height > 0)
            {
                double ratio = o.Width / o.Height;
                double width = right - left, height = bottom - top;
                if ((l || r) && !(t || b)) height = width / ratio;
                else if ((t || b) && !(l || r)) width = height * ratio;
                else if (width / height > ratio) height = width / ratio; else width = height * ratio;
                if (l) left = right - width; else right = left + width;
                if (t) top = bottom - height; else bottom = top + height;
            }
            if (angle != 0)
            {
                // Back to the screen: the new box's middle turns about the original middle, which keeps the
                // edge opposite the handle where it was.
                Point middle = Turn(new Point((left + right) / 2, (top + bottom) / 2), Center(o), angle);
                double width = right - left, height = bottom - top;
                left = middle.X - width / 2;
                right = middle.X + width / 2;
                top = middle.Y - height / 2;
                bottom = middle.Y + height / 2;
            }
            w.X = Math.Round(left);
            w.Y = Math.Round(top);
            w.W = Math.Max(2, Math.Round(right - left));
            w.H = Math.Max(2, Math.Round(bottom - top));
            _dragChanged = true;
        }

        /// <summary>
        /// Several widgets (or a group) resized from the handles around them all: the outer box follows the
        /// handle as a single widget's would, and every widget keeps its place and share within it.
        /// </summary>
        private void ResizeTogether(Point p, bool free, bool keepAspect)
        {
            Rect o = Rect.Empty;
            foreach (Rect r in _originals.Values) o = o.IsEmpty ? r : Rect.Union(o, r);
            if (o.Width < 1 || o.Height < 1) return;
            double left = o.Left, top = o.Top, right = o.Right, bottom = o.Bottom;
            bool l = _handle == 0 || _handle == 6 || _handle == 7, rt = _handle == 2 || _handle == 3 || _handle == 4;
            bool t = _handle == 0 || _handle == 1 || _handle == 2, b = _handle == 4 || _handle == 5 || _handle == 6;
            _guides.Clear();
            double x = p.X, y = p.Y;
            if (!free)
            {
                if (l || rt) x += SnapAxis(new[] { x }, true, new Rect(x, top, 0, bottom - top));
                if (t || b) y += SnapAxis(new[] { y }, false, new Rect(left, y, right - left, 0));
            }
            if (l) left = Math.Min(x, right - 8);
            if (rt) right = Math.Max(x, left + 8);
            if (t) top = Math.Min(y, bottom - 8);
            if (b) bottom = Math.Max(y, top + 8);
            if (keepAspect)
            {
                double ratio = o.Width / o.Height, width = right - left, height = bottom - top;
                if ((l || rt) && !(t || b)) height = width / ratio;
                else if ((t || b) && !(l || rt)) width = height * ratio;
                else if (width / height > ratio) height = width / ratio; else width = height * ratio;
                if (l) left = right - width; else right = left + width;
                if (t) top = bottom - height; else bottom = top + height;
            }
            double sx = (right - left) / o.Width, sy = (bottom - top) / o.Height;
            foreach (KeyValuePair<DashWidget, Rect> pair in _originals)
            {
                Rect r = pair.Value;
                pair.Key.X = Math.Round(left + (r.X - o.X) * sx);
                pair.Key.Y = Math.Round(top + (r.Y - o.Y) * sy);
                pair.Key.W = Math.Max(2, Math.Round(r.Width * sx));
                pair.Key.H = Math.Max(2, Math.Round(r.Height * sy));
            }
            _dragChanged = true;
        }

        /// <summary>
        /// Finds the nudge (on one axis) that lines any of the given edges up with the
        /// canvas, its center, or another widget's edges or center; falls back to the grid.
        /// Adds a guide line for the match.
        /// </summary>
        private double SnapAxis(double[] edges, bool horizontal, Rect moving)
        {
            double threshold = 6 * Px;
            var targets = new List<double> { 0, horizontal ? CanvasW / 2 : CanvasH / 2, horizontal ? CanvasW : CanvasH };
            targets.AddRange(horizontal ? _layout.GuidesX : _layout.GuidesY);
            foreach (DashWidget other in _screen.Widgets)
            {
                if (other.Hidden || _selection.Contains(other)) continue;
                Rect r = RectOf(other);
                if (horizontal) targets.AddRange(new[] { r.Left, r.Left + r.Width / 2, r.Right });
                else targets.AddRange(new[] { r.Top, r.Top + r.Height / 2, r.Bottom });
            }
            double best = double.MaxValue, bestTarget = 0;
            foreach (double edge in edges)
                foreach (double target in targets)
                {
                    double d = target - edge;
                    if (Math.Abs(d) < Math.Abs(best) && Math.Abs(d) <= threshold) { best = d; bestTarget = target; }
                }
            if (best != double.MaxValue)
            {
                if (horizontal) _guides.Add(Tuple.Create(new Point(bestTarget, Math.Min(0, moving.Top) - 2000), new Point(bestTarget, Math.Max(CanvasH, moving.Bottom) + 2000)));
                else _guides.Add(Tuple.Create(new Point(Math.Min(0, moving.Left) - 2000, bestTarget), new Point(Math.Max(CanvasW, moving.Right) + 2000, bestTarget)));
                return best;
            }
            double first = edges[0];
            return Math.Round(first / GridSize) * GridSize - first;
        }

        // ------------------------------------------------------------------ drag in from the toolbox

        private void Overlay_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent("afo-widget") || e.Data.GetDataPresent("afo-block") || e.Data.GetDataPresent("afo-module") || ModuleFiles(e.Data).Count > 0
                ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void Overlay_Drop(object sender, DragEventArgs e)
        {
            Point p = e.GetPosition(Overlay);
            List<DashWidget> widgets;
            string label;
            if (e.Data.GetData("afo-module") is DesignModule module)
            {
                PlaceModule(module, p, true);
                return;
            }
            // Module files dragged in from Explorer join the Library and land where they're dropped.
            List<string> files = ModuleFiles(e.Data);
            if (files.Count > 0)
            {
                ImportModuleFiles(files, p);
                return;
            }
            if (e.Data.GetData("afo-widget") is string type)
            {
                WidgetType info = WidgetCatalog.Find(type);
                widgets = new List<DashWidget> { WidgetCatalog.Create(type, p.X - info.DefaultW / 2, p.Y - info.DefaultH / 2) };
                label = "Add " + info.Name;
            }
            else if (e.Data.GetData("afo-block") is DesignBlock block)
            {
                widgets = block.Create();
                Rect bounds = Union(widgets);
                foreach (DashWidget w in widgets) { w.X += Math.Round(p.X - bounds.Width / 2 - bounds.X); w.Y += Math.Round(p.Y - bounds.Height / 2 - bounds.Y); }
                label = "Add " + block.Name;
            }
            else return;
            // Dropped on a panel: ask whether it belongs to the panel, and on which of its views.
            DashWidget panel = PanelAt(p);
            if (panel != null && !widgets.Any(w => w.Type == "panel")) AskPanelDrop(panel, widgets, label);
            else AddWidgets(widgets, label);
        }
    }
}
