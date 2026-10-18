using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace AlmostFastOverlays
{
    /// <summary>
    /// Building panels with tap views by dragging: something dropped on a panel can join it, either on the
    /// view showing or on a new view a tap switches to. The designer writes the tap name, the view count
    /// and every Show-when condition itself.
    /// </summary>
    public partial class DesignerWindow
    {
        /// <summary>The top-most panel showing under a point on the screen being designed, if any.</summary>
        private DashWidget PanelAt(Point p)
        {
            EvalScope scope = CurrentScope();
            for (int i = _screen.Widgets.Count - 1; i >= 0; i--)
            {
                DashWidget widget = _screen.Widgets[i];
                if (widget.Type != "panel" || widget.Hidden || widget.Locked || !widget.Bounds.Contains((float)p.X, (float)p.Y)) continue;
                string visible = widget.Get("visible", "");
                if (!string.IsNullOrWhiteSpace(visible) && !Expr.Bool(Expr.Evaluate(LayoutRenderer.Strip(visible), scope))) continue;
                return widget;
            }
            return null;
        }

        /// <summary>What sits on a panel: its group, plus anything above it that lies wholly inside it and isn't grouped elsewhere.</summary>
        private List<DashWidget> PanelContents(DashWidget panel)
        {
            int at = _screen.Widgets.IndexOf(panel);
            Rect box = new Rect(panel.X - 1, panel.Y - 1, panel.W + 2, panel.H + 2);
            return _screen.Widgets.Where(w => w != panel && (
                (!string.IsNullOrEmpty(panel.Group) && w.Group == panel.Group) ||
                (string.IsNullOrEmpty(w.Group) && w.Type != "panel" && _screen.Widgets.IndexOf(w) > at && box.Contains(new Rect(w.X, w.Y, w.W, w.H))))).ToList();
        }

        private enum PanelDrop { Place, Join, SameView, NewView }

        private void AskPanelDrop(DashWidget panel, List<DashWidget> widgets, string label)
        {
            string tapName = panel.Get("tapName", "")?.Trim();
            bool hasViews = !string.IsNullOrEmpty(tapName);
            List<DashWidget> contents = PanelContents(panel);
            string panelName = string.IsNullOrWhiteSpace(panel.Name) ? "the panel" : panel.Name;
            int views = hasViews ? LayoutRenderer.TapViews(panel) : 1, showing = 0;
            if (hasViews) _designTaps.TryGetValue(tapName, out showing);
            showing %= Math.Max(1, views);

            var menu = new ContextMenu { Placement = PlacementMode.MousePoint };
            var header = new StackPanel { Margin = new Thickness(0, 2, 0, 4) };
            header.Children.Add(new TextBlock { Text = "Dropped on " + panelName, FontWeight = FontWeights.SemiBold });
            header.Children.Add(new TextBlock
            {
                Text = contents.Count == 0 && !hasViews ? "Add it to the panel so they move together?"
                    : hasViews ? "This panel has " + views + " views; a tap on a touch screen switches them."
                    : "Put it with what's already on the panel, or on a new view a tap switches to?",
                Style = (Style)FindResource("T.Caption"), MaxWidth = 300, TextWrapping = TextWrapping.Wrap
            });
            menu.Items.Add(new MenuItem { Header = header, IsEnabled = false });
            PanelDrop chosen = PanelDrop.Place;
            void Option(string text, string detail, PanelDrop drop)
            {
                var content = new StackPanel();
                content.Children.Add(new TextBlock { Text = text });
                if (detail != null) content.Children.Add(new TextBlock { Text = detail, Style = (Style)FindResource("T.Caption") });
                var item = new MenuItem { Header = content };
                item.Click += (s, e) => chosen = drop;
                menu.Items.Add(item);
            }
            if (contents.Count == 0 && !hasViews)
                Option("Add to " + panelName, "Grouped with the panel", PanelDrop.Join);
            else
            {
                Option(hasViews ? "Add to view " + (showing + 1) + " (showing now)" : "Add to the panel as it is", hasViews ? null : "Shows with what's already on it", PanelDrop.SameView);
                if (views < 9)
                    Option(hasViews ? "Add as view " + (views + 1) : "Add as a new view", "A tap switches the panel between its views", PanelDrop.NewView);
            }
            menu.Items.Add(new Separator());
            Option("Just place it here", "Not part of the panel", PanelDrop.Place);
            menu.Closed += (s, e) => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (chosen == PanelDrop.Place) AddWidgets(widgets, label);
                else DropOnPanel(panel, widgets, label, chosen == PanelDrop.NewView);
            }));
            menu.IsOpen = true;
        }

        /// <summary>Adds widgets to a panel: into its group, inside its edges, and on a view of its own when asked.</summary>
        internal void DropOnPanel(DashWidget panel, List<DashWidget> widgets, string label, bool newView)
        {
            List<DashWidget> contents = PanelContents(panel);
            if (string.IsNullOrEmpty(panel.Group))
            {
                panel.Group = DashScreen.NewId("g");
                panel.GroupName = string.IsNullOrWhiteSpace(panel.Name) ? "Panel" : panel.Name;
            }
            foreach (DashWidget w in contents.Concat(widgets)) { w.Group = panel.Group; w.GroupName = panel.GroupName; w.Slot = panel.Slot; }

            // Inside the panel's edges.
            Rect bounds = Union(widgets);
            double dx = 0, dy = 0;
            if (bounds.Width <= panel.W) dx = Math.Max(panel.X - bounds.Left, Math.Min(0, panel.X + panel.W - bounds.Right));
            if (bounds.Height <= panel.H) dy = Math.Max(panel.Y - bounds.Top, Math.Min(0, panel.Y + panel.H - bounds.Bottom));
            foreach (DashWidget w in widgets) { w.X = Math.Round(w.X + dx); w.Y = Math.Round(w.Y + dy); }

            string tapName = panel.Get("tapName", "")?.Trim();
            string status;
            if (newView)
            {
                int views;
                if (string.IsNullOrEmpty(tapName))
                {
                    // The panel's first view is everything already on it.
                    tapName = UniqueTapName(panel.Name);
                    panel.Set("tapName", tapName);
                    foreach (DashWidget w in contents) TouchViews.Only(w, "tap(\"" + tapName + "\") == 0");
                    views = 2;
                }
                else views = LayoutRenderer.TapViews(panel) + 1;
                panel.Set("tapViews", views.ToString());
                foreach (DashWidget w in widgets) TouchViews.Only(w, "tap(\"" + tapName + "\") == " + (views - 1));
                _designTaps[tapName] = views - 1;
                status = "Added as view " + views + " of " + (string.IsNullOrWhiteSpace(panel.Name) ? "the panel" : panel.Name) +
                    ". A tap switches views; here, use Next in Properties.";
            }
            else if (!string.IsNullOrEmpty(tapName))
            {
                _designTaps.TryGetValue(tapName, out int showing);
                showing %= LayoutRenderer.TapViews(panel);
                foreach (DashWidget w in widgets) TouchViews.Only(w, "tap(\"" + tapName + "\") == " + showing);
                status = "Added to view " + (showing + 1) + ".";
            }
            else status = "Added to " + (string.IsNullOrWhiteSpace(panel.Name) ? "the panel" : panel.Name) + "; they move together. Drop another to add a second view.";
            AddWidgets(widgets, label, keepGroups: true);
            StatusText.Text = status;
        }

        /// <summary>A short tap name from a panel's name ("Fuel panel" makes "fuel"), not used by another tap zone.</summary>
        private string UniqueTapName(string panelName)
        {
            var name = new StringBuilder();
            foreach (string word in (panelName ?? "").ToLowerInvariant().Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries))
                if (word != "panel" && word != "tile" && word != "card") name.Append(new string(word.Where(char.IsLetterOrDigit).ToArray()));
            string stem = name.Length > 0 ? name.ToString() : "panel";
            var taken = new HashSet<string>(_layout.AllWidgets().Select(w => w.Get("tapName", "")?.Trim()).Where(n => !string.IsNullOrEmpty(n)), StringComparer.OrdinalIgnoreCase);
            string candidate = stem;
            for (int i = 2; taken.Contains(candidate); i++) candidate = stem + i;
            return candidate;
        }
    }
}
