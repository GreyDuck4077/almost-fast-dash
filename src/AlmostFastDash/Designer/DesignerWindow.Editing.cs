using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace AlmostFastDash.Designer
{
    public partial class DesignerWindow : Window
    {
        // ==================================================================
        // Selection and editing commands
        // ==================================================================

        private void Select(DashWidget widget)
        {
            _selection.Clear();
            if (widget != null) _selection.Add(widget);
            SelectionChanged();
        }

        private void SelectAll()
        {
            _selection.Clear();
            _selection.AddRange(_screen.Widgets.Where(w => !w.Hidden && !w.Locked));
            SelectionChanged();
        }

        private void SelectionChanged()
        {
            RefreshInspector();
            RefreshLayers();
            UpdateOverlay();
        }

        /// <summary>A widget's whole group on this screen (just the widget when it isn't grouped).</summary>
        private List<DashWidget> GroupOf(DashWidget widget) =>
            string.IsNullOrEmpty(widget.Group) ? new List<DashWidget> { widget }
                : _screen.Widgets.Where(w => w.Group == widget.Group && !w.Hidden && !w.Locked).ToList();

        /// <summary>The selection is exactly one whole group.</summary>
        private bool SelectionIsOneGroup(out string group)
        {
            group = _selection.Count > 0 ? _selection[0].Group : null;
            string id = group;
            return !string.IsNullOrEmpty(id) && _selection.All(w => w.Group == id) && _screen.Widgets.Count(w => w.Group == id) == _selection.Count;
        }

        private void GroupSelection()
        {
            if (_selection.Count < 2) { StatusText.Text = "Select two or more widgets to group them."; return; }
            string id = DashScreen.NewId("g");
            int number = _screen.Widgets.Select(w => w.Group).Where(g => !string.IsNullOrEmpty(g)).Distinct().Count() + 1;
            foreach (DashWidget w in _selection) { w.Group = id; w.GroupName = "Group " + number; w.Slot = 0; }
            Commit("Group");
            SelectionChanged();
            StatusText.Text = "Grouped " + _selection.Count + " widgets.";
        }

        private void Ungroup()
        {
            if (!_selection.Any(w => !string.IsNullOrEmpty(w.Group))) return;
            foreach (DashWidget w in _selection) { w.Group = ""; w.GroupName = ""; w.Slot = 0; }
            Commit("Ungroup");
            SelectionChanged();
        }

        /// <summary>New widgets coming onto the screen (pasted, from a block) get groups of their own, not the ones they were copied from.</summary>
        private void FreshGroups(IEnumerable<DashWidget> widgets)
        {
            var renamed = new Dictionary<string, string>();
            foreach (DashWidget w in widgets)
            {
                if (string.IsNullOrEmpty(w.Group)) continue;
                if (!renamed.TryGetValue(w.Group, out string id)) renamed[w.Group] = id = DashScreen.NewId("g");
                w.Group = id;
            }
        }

        /// <param name="keepGroups">Joining a group already on the screen (a panel's), rather than coming in as new groups of their own.</param>
        internal void AddWidgets(IList<DashWidget> widgets, string label, bool keepGroups = false)
        {
            if (!keepGroups && widgets.Any(w => !string.IsNullOrEmpty(w.Group) && _screen.Widgets.Any(s => s.Group == w.Group))) FreshGroups(widgets);
            foreach (DashWidget w in widgets)
            {
                w.X = Math.Round(Math.Max(-w.W + 10, Math.Min(CanvasW - 10, w.X)));
                w.Y = Math.Round(Math.Max(-w.H + 10, Math.Min(CanvasH - 10, w.Y)));
                _screen.Widgets.Add(w);
            }
            _selection.Clear();
            _selection.AddRange(widgets);
            _rendererDirty = true;
            Commit(label);
            SelectionChanged();
            RequestRender();
        }

        private void AddWidgetAtCenter(string type)
        {
            WidgetType info = WidgetCatalog.Find(type);
            int cascade = _screen.Widgets.Count(w => w.Type == type) % 6 * 12;
            AddWidgets(new[] { WidgetCatalog.Create(type, (CanvasW - info.DefaultW) / 2 + cascade, (CanvasH - info.DefaultH) / 2 + cascade) }, "Add " + info.Name);
        }

        private void DeleteSelection()
        {
            if (_selection.Count == 0) return;
            foreach (DashWidget w in _selection) _screen.Widgets.Remove(w);
            _selection.Clear();
            Commit("Delete");
            SelectionChanged();
            RequestRender();
        }

        private List<DashWidget> DuplicateSelection(double offset = 16)
        {
            if (_selection.Count == 0) return null;
            // A whole group copies as the next one: just below it, numbered one higher (P1's card becomes P2's).
            bool nextInGroup = SelectionIsOneGroup(out _);
            Rect bounds = Union(_selection);
            var copies = _screen.Widgets.Where(_selection.Contains).Select(w =>
            {
                DashWidget copy = SettingsStore.Clone(w);
                copy.Id = DashScreen.NewId("w");
                copy.X += nextInGroup ? 0 : offset;
                copy.Y += nextInGroup ? Math.Round(bounds.Height + 6) : offset;
                if (nextInGroup) copy.Slot = w.Slot + 1;
                copy.Locked = false;
                return copy;
            }).ToList();
            FreshGroups(copies);
            AddWidgets(copies, "Duplicate");
            if (nextInGroup) StatusText.Text = "Copied the group as Slot " + copies[0].Slot + ".";
            return copies;
        }

        // ------------------------------------------------------------------ copy and paste style

        private static readonly HashSet<string> StyleGroups = new HashSet<string> { "Style", "Colors", "Effects", "Type" };

        private Dictionary<string, string> _copiedStyle;

        /// <summary>Copies the selected widget's look: its Style effects, colors, glow and type settings.</summary>
        private void CopyStyle()
        {
            if (_selection.Count != 1) { StatusText.Text = "Select one widget to copy its style."; return; }
            DashWidget source = _selection[0];
            WidgetType type = WidgetCatalog.Find(source.Type);
            if (type == null) return;
            _copiedStyle = type.Props.Where(p => StyleGroups.Contains(p.Group)).ToDictionary(p => p.Key, p => source.Get(p.Key, p.Default));
            StatusText.Text = "Copied the style of " + source.Name + ". Select widgets and press Ctrl+Shift+V to paste it.";
        }

        /// <summary>Pastes a copied style onto the selection, wherever a widget has the same setting.</summary>
        private void PasteStyle()
        {
            if (_copiedStyle == null || _selection.Count == 0) return;
            int changed = 0;
            foreach (DashWidget w in _selection)
            {
                WidgetType type = WidgetCatalog.Find(w.Type);
                if (type == null) continue;
                foreach (KeyValuePair<string, string> pair in _copiedStyle)
                    if (type.Prop(pair.Key) is PropDef def && StyleGroups.Contains(def.Group)) { w.Set(pair.Key, pair.Value); changed++; }
            }
            Commit("Paste style");
            RefreshInspector();
            StatusText.Text = "Pasted the style onto " + _selection.Count + (_selection.Count == 1 ? " widget." : " widgets.");
        }

        private void CopySelection()
        {
            if (_selection.Count == 0) return;
            _clipboard = SettingsStore.ToJson(_screen.Widgets.Where(_selection.Contains).ToList());
            StatusText.Text = _selection.Count == 1 ? "Copied 1 widget." : "Copied " + _selection.Count + " widgets.";
        }

        private void Paste(Point? at = null)
        {
            if (_clipboard == null) return;
            List<DashWidget> widgets;
            try { widgets = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<List<DashWidget>>(_clipboard); }
            catch { return; }
            Rect bounds = Union(widgets);
            foreach (DashWidget w in widgets)
            {
                w.Id = DashScreen.NewId("w");
                w.Normalize();
                if (at.HasValue) { w.X += Math.Round(at.Value.X - bounds.X); w.Y += Math.Round(at.Value.Y - bounds.Y); }
                else { w.X += 16; w.Y += 16; }
            }
            AddWidgets(widgets, "Paste");
        }

        private void Reorder(int step)
        {
            if (_selection.Count == 0) return;
            List<DashWidget> list = _screen.Widgets;
            List<DashWidget> moving = list.Where(_selection.Contains).ToList();
            if (step == int.MaxValue) { foreach (DashWidget w in moving) { list.Remove(w); list.Add(w); } }
            else if (step == int.MinValue) { for (int i = moving.Count - 1; i >= 0; i--) { list.Remove(moving[i]); list.Insert(0, moving[i]); } }
            else
            {
                IEnumerable<DashWidget> order = step > 0 ? Enumerable.Reverse(moving) : moving;
                foreach (DashWidget w in order)
                {
                    int index = list.IndexOf(w), target = Math.Max(0, Math.Min(list.Count - 1, index + step));
                    list.RemoveAt(index);
                    list.Insert(target, w);
                }
            }
            Commit("Reorder");
            RefreshLayers();
            RequestRender();
        }

        private void ToggleLock()
        {
            bool lockAll = !_selection.All(w => w.Locked);
            foreach (DashWidget w in _selection) w.Locked = lockAll;
            Commit(lockAll ? "Lock" : "Unlock");
            SelectionChanged();
        }

        private void MoveToScreen(DashScreen target)
        {
            foreach (DashWidget w in _selection.ToList()) { _screen.Widgets.Remove(w); target.Widgets.Add(w); }
            _selection.Clear();
            Commit("Move to " + target.Name);
            SelectionChanged();
            RequestRender();
        }

        private void Nudge(double dx, double dy)
        {
            foreach (DashWidget w in _selection.Where(w => !w.Locked)) { w.X += dx; w.Y += dy; }
            Commit("Nudge");
            RefreshPositionFields();
            RequestRender();
        }

        internal void AlignSelection(string how)
        {
            List<DashWidget> items = _selection.Where(w => !w.Locked).ToList();
            if (items.Count == 0) return;
            Rect area = items.Count == 1 ? new Rect(0, 0, CanvasW, CanvasH) : Union(items);
            foreach (DashWidget w in items)
                switch (how)
                {
                    case "left": w.X = area.Left; break;
                    case "center": w.X = Math.Round(area.Left + (area.Width - w.W) / 2); break;
                    case "right": w.X = area.Right - w.W; break;
                    case "top": w.Y = area.Top; break;
                    case "middle": w.Y = Math.Round(area.Top + (area.Height - w.H) / 2); break;
                    case "bottom": w.Y = area.Bottom - w.H; break;
                }
            if (how == "hspace" && items.Count > 2)
            {
                items = items.OrderBy(w => w.X).ToList();
                double free = area.Width - items.Sum(w => w.W), gap = free / (items.Count - 1), x = area.Left;
                foreach (DashWidget w in items) { w.X = Math.Round(x); x += w.W + gap; }
            }
            if (how == "vspace" && items.Count > 2)
            {
                items = items.OrderBy(w => w.Y).ToList();
                double free = area.Height - items.Sum(w => w.H), gap = free / (items.Count - 1), y = area.Top;
                foreach (DashWidget w in items) { w.Y = Math.Round(y); y += w.H + gap; }
            }
            if (how == "width" || how == "height")
            {
                DashWidget first = items[0];
                foreach (DashWidget w in items) { if (how == "width") w.W = first.W; else w.H = first.H; }
            }
            Commit("Align");
            RefreshPositionFields();
            RequestRender();
        }
    }
}
