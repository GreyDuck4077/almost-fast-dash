using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AlmostFastDash.Designer
{
    public partial class DesignerWindow
    {
        // ==================================================================
        // Layers
        // ==================================================================

        private void RefreshLayers()
        {
            if (LayersList == null) return;
            LayersList.Children.Clear();
            _layerThumbs.Clear();
            int count = _screen.Widgets.Count;
            LayerCount.Text = count == 0 ? "" : count + (count == 1 ? " layer" : " layers");
            if (count == 0)
            {
                LayersList.Children.Add(new TextBlock { Text = "This screen is empty. Drag in from the Library or draw with the tools.", Style = (Style)FindResource("T.Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 8, 8, 0) });
                return;
            }
            for (int i = _screen.Widgets.Count - 1; i >= 0; i--) LayersList.Children.Add(LayerRow(_screen.Widgets[i]));
            UpdateLayerThumbs();
        }

        private Border LayerRow(DashWidget widget)
        {
            bool selected = _selection.Contains(widget);
            WidgetType type = WidgetCatalog.Find(widget.Type);
            var grid = new Grid { Height = 30 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ThumbW + 8) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock typeIcon = Glyph(type?.Icon ?? "", 13, selected ? "Brush.Accent" : "Brush.TextTertiary");
            typeIcon.HorizontalAlignment = HorizontalAlignment.Center;
            grid.Children.Add(typeIcon);
            // A small picture of the layer, as an image editor shows them.
            Border thumb = LayerThumb(widget);
            thumb.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetColumn(thumb, 1);
            grid.Children.Add(thumb);

            var name = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(widget.Name) ? type?.Name : widget.Name, VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis, Opacity = widget.Hidden ? 0.45 : 1, Tag = "error-" + widget.Id
            };
            if (!string.IsNullOrWhiteSpace(widget.Get("visible")))
                name.ToolTip = "Shows when " + widget.Get("visible");
            if (!string.IsNullOrEmpty(widget.Group))
            {
                // Grouped widgets carry their group's name (and Slot) in a quiet second color.
                name.Inlines.Clear();
                name.Inlines.Add(new System.Windows.Documents.Run(string.IsNullOrWhiteSpace(widget.Name) ? type?.Name : widget.Name));
                name.Inlines.Add(new System.Windows.Documents.Run("  " + widget.GroupName + (widget.Slot != 0 ? " · " + widget.Slot.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) : ""))
                    { Foreground = (Brush)FindResource("Brush.TextTertiary"), FontSize = 12 });
            }
            Grid.SetColumn(name, 2);
            grid.Children.Add(name);
            // A clipped layer sits indented, with an arrow down to the layer it shows through, as in an image editor.
            if (Expr.Bool(widget.Get("clip", "false")))
            {
                DashWidget clipBase = ClipBaseOf(widget);
                grid.Margin = new Thickness(16, 0, 0, 0);
                typeIcon.Text = "\u21B4";
                typeIcon.FontFamily = (FontFamily)FindResource("Font.Ui");
                typeIcon.FontSize = 17;
                typeIcon.Foreground = (Brush)FindResource("Brush.Accent");
                name.ToolTip = clipBase == null ? "Set to clip, but there's no layer below to clip to"
                    : "Clipped to " + (string.IsNullOrWhiteSpace(clipBase.Name) ? clipBase.Type : clipBase.Name) + ": shows only where it's drawn. Alt-click to release.";
            }

            var toggles = new StackPanel { Orientation = Orientation.Horizontal };
            if (!string.IsNullOrWhiteSpace(widget.Get("visible")))
                toggles.Children.Add(new TextBlock { Text = "ƒ", FontStyle = FontStyles.Italic, Foreground = (Brush)FindResource("Brush.Accent"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), ToolTip = "Conditional: shows when " + widget.Get("visible") });
            toggles.Children.Add(SmallToggle(widget.Locked ? "" : "", widget.Locked ? "Unlock" : "Lock", widget.Locked, () =>
            {
                widget.Locked = !widget.Locked;
                if (widget.Locked) _selection.Remove(widget);
                Commit(widget.Locked ? "Lock" : "Unlock");
                SelectionChanged();
            }));
            toggles.Children.Add(SmallToggle(widget.Hidden ? "" : "", widget.Hidden ? "Show" : "Hide", widget.Hidden, () =>
            {
                widget.Hidden = !widget.Hidden;
                if (widget.Hidden) _selection.Remove(widget);
                Commit(widget.Hidden ? "Hide" : "Show");
                SelectionChanged();
                RequestRender();
            }));
            Grid.SetColumn(toggles, 3);
            grid.Children.Add(toggles);

            var row = new Border
            {
                Child = grid, CornerRadius = new CornerRadius(7), Padding = new Thickness(2, 0, 4, 0), Margin = new Thickness(0, 1, 0, 1),
                Background = selected ? (Brush)FindResource("Brush.AccentSubtle") : Brushes.Transparent, AllowDrop = true, Cursor = Cursors.Hand
            };
            row.MouseEnter += (s, e) => { if (!_selection.Contains(widget)) row.Background = (Brush)FindResource("Brush.SurfaceHover"); _hover = widget; UpdateOverlay(); };
            row.MouseLeave += (s, e) => { if (!_selection.Contains(widget)) row.Background = Brushes.Transparent; if (_hover == widget) { _hover = null; UpdateOverlay(); } };

            Point? down = null;
            row.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2) { RenameLayer(widget); e.Handled = true; return; }
                down = e.GetPosition(row);
            };
            row.MouseMove += (s, e) =>
            {
                if (down == null || e.LeftButton != MouseButtonState.Pressed) return;
                if (Math.Abs(e.GetPosition(row).Y - down.Value.Y) < 6) return;
                down = null;
                DragDrop.DoDragDrop(row, new DataObject("afo-layer", widget.Id), DragDropEffects.Move);
            };
            row.MouseLeftButtonUp += (s, e) =>
            {
                if (down == null) return;
                down = null;
                // Alt-click clips a layer to the one below it, or releases it, as in an image editor.
                if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0)
                {
                    ToggleClip(new[] { widget });
                    return;
                }
                if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0)
                {
                    if (_selection.Contains(widget)) _selection.Remove(widget); else if (!widget.Locked) _selection.Add(widget);
                    SelectionChanged();
                }
                else if (!widget.Locked) Select(widget);
            };
            row.DragOver += (s, e) => { e.Effects = e.Data.GetDataPresent("afo-layer") ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; };
            row.Drop += (s, e) =>
            {
                if (!(e.Data.GetData("afo-layer") is string id) || id == widget.Id) return;
                DashWidget moving = _screen.Widgets.FirstOrDefault(w => w.Id == id);
                if (moving == null) return;
                // The list shows the top layer first, so dropping on the upper half puts it above.
                bool above = e.GetPosition(row).Y < row.ActualHeight / 2;
                _screen.Widgets.Remove(moving);
                int index = _screen.Widgets.IndexOf(widget);
                _screen.Widgets.Insert(above ? index + 1 : index, moving);
                Commit("Reorder");
                RefreshLayers();
                RequestRender();
            };
            return row;
        }

        /// <summary>The layer a clipped layer shows through: the nearest below it that isn't clipped itself.</summary>
        private DashWidget ClipBaseOf(DashWidget widget)
        {
            for (int i = _screen.Widgets.IndexOf(widget) - 1; i >= 0; i--)
                if (!Expr.Bool(_screen.Widgets[i].Get("clip", "false"))) return _screen.Widgets[i];
            return null;
        }

        /// <summary>Clips layers to the layer below them, or releases them when they all are already.</summary>
        private void ToggleClip(IEnumerable<DashWidget> widgets)
        {
            List<DashWidget> list = widgets.Where(w => w != null).ToList();
            if (list.Count == 0) return;
            bool release = list.All(w => Expr.Bool(w.Get("clip", "false")));
            if (!release && list.Any(w => _screen.Widgets.IndexOf(w) == 0))
            {
                StatusText.Text = "The bottom layer has nothing below it to clip to.";
                return;
            }
            foreach (DashWidget w in list) w.Set("clip", release ? "false" : "true");
            Commit(release ? "Release clipping" : "Clip to layer below");
            RefreshLayers();
            RefreshInspector();
            RequestRender();
            StatusText.Text = release ? "Released: shows in full again."
                : "Clipped: shows only where the layer below is drawn. Alt-click the layer (or Ctrl+Alt+G) to release.";
        }

        private Button SmallToggle(string glyph, string tip, bool active, Action action)
        {
            var button = new Button
            {
                Style = (Style)FindResource("Btn.Ghost"), Width = 26, Height = 24, Padding = new Thickness(0), ToolTip = tip,
                Content = Glyph(glyph, 12, active ? "Brush.Warning" : "Brush.TextTertiary")
            };
            System.Windows.Automation.AutomationProperties.SetName(button, tip);
            button.Click += (s, e) => { action(); e.Handled = true; };
            return button;
        }

        private void RenameLayer(DashWidget widget)
        {
            string name = Prompt("Rename layer", "Layer name:", widget.Name);
            if (string.IsNullOrWhiteSpace(name)) return;
            widget.Name = name.Trim();
            Commit("Rename");
            RefreshLayers();
            RefreshInspector();
        }

        private void MarkLayerErrors()
        {
            if (LayersList == null || _renderer == null) return;
            foreach (Border row in LayersList.Children.OfType<Border>())
                if (row.Child is Grid grid)
                    foreach (TextBlock text in grid.Children.OfType<TextBlock>())
                        if (text.Tag is string tag && tag.StartsWith("error-"))
                        {
                            bool error = _renderer.Errors.ContainsKey(tag.Substring(6));
                            text.Foreground = error ? (Brush)FindResource("Brush.Danger") : (Brush)FindResource("Brush.Text");
                        }
        }

        private void Forward_Click(object sender, RoutedEventArgs e) => Reorder(1);

        private void Backward_Click(object sender, RoutedEventArgs e) => Reorder(-1);

        private void Duplicate_Click(object sender, RoutedEventArgs e) => DuplicateSelection();

        private void Delete_Click(object sender, RoutedEventArgs e) => DeleteSelection();
    }
}
