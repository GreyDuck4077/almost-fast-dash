using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace AlmostFastDash.Designer
{
    public partial class DesignerWindow
    {
        // ------------------------------------------------------------------ one widget

        private void BuildWidgetInspector(DashWidget widget)
        {
            WidgetType type = WidgetCatalog.Find(widget.Type);
            if (type == null)
            {
                Inspector.Children.Add(Heading("Unknown widget"));
                Inspector.Children.Add(Caption("This widget type (" + widget.Type + ") is not available in this version."));
                return;
            }

            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var badge = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(7), Background = (Brush)FindResource("Brush.AccentSubtle"), Child = Glyph(type.Icon, 14, "Brush.Accent") };
            ((TextBlock)badge.Child).HorizontalAlignment = HorizontalAlignment.Center;
            header.Children.Add(badge);
            var name = new TextBox { Text = widget.Name ?? type.Name, FontWeight = FontWeights.SemiBold, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "Layer name" };
            name.TextChanged += (s, e) => { widget.Name = name.Text; CommitSoon("Rename"); };
            name.LostKeyboardFocus += (s, e) => RefreshLayers();
            Grid.SetColumn(name, 1);
            header.Children.Add(name);
            var more = new Button { Style = (Style)FindResource("Btn.Ghost"), Width = 30, Height = 30, Padding = new Thickness(0), Margin = new Thickness(6, 0, 0, 0), Content = Glyph("", 13), ToolTip = "More" };
            more.Click += (s, e) =>
            {
                var menu = new ContextMenu { PlacementTarget = more, Placement = PlacementMode.Bottom };
                menu.Items.Add(MenuItem("Duplicate", "Ctrl+D", () => DuplicateSelection()));
                menu.Items.Add(MenuItem("Repeat…", "Ctrl+Shift+D", RepeatSelection));
                menu.Items.Add(MenuItem("Reset to defaults", null, () => { widget.Props.Clear(); Commit("Reset widget"); RefreshInspector(); RequestRender(); }));
                menu.Items.Add(MenuItem("Save as module…", "Ctrl+M", SaveSelectionAsModule));
                menu.Items.Add(MenuItem(widget.Locked ? "Unlock" : "Lock", "Ctrl+L", ToggleLock));
                menu.Items.Add(MenuItem("Delete", "Del", DeleteSelection));
                menu.IsOpen = true;
            };
            Grid.SetColumn(more, 2);
            header.Children.Add(more);
            Inspector.Children.Add(header);
            Inspector.Children.Add(Caption(type.Description, 8));
            if (!string.IsNullOrEmpty(widget.Group))
                Inspector.Children.Add(Caption("In " + widget.GroupName + (widget.Slot != 0 ? " (Slot " + widget.Slot.ToString("0.##", CultureInfo.InvariantCulture) + ")" : "") +
                    ". Click the group on the canvas to select all of it.", 6));
            AddGhostNotice(new[] { widget });
            AddModuleOptions(widget);
            // A tap zone, or a widget that shows on one of its views: flip the preview through the views here,
            // as a tap on a touch screen would.
            string tapName = widget.Get("tapName", "")?.Trim();
            if (string.IsNullOrEmpty(tapName))
            {
                var used = System.Text.RegularExpressions.Regex.Match(widget.Get("visible", "") ?? "", "tap\\(\\s*\"([^\"]+)\"");
                if (used.Success) tapName = used.Groups[1].Value.Trim();
            }
            if (!string.IsNullOrEmpty(tapName))
            {
                DashWidget zone = _layout.AllWidgets().FirstOrDefault(w => string.Equals(w.Get("tapName", "")?.Trim(), tapName, StringComparison.OrdinalIgnoreCase));
                int views = zone != null ? LayoutRenderer.TapViews(zone) : 2;
                _designTaps.TryGetValue(tapName, out int view);
                var next = new Button { Content = "Tap \"" + tapName + "\": showing view " + (view % views + 1) + " of " + views + "  ·  Next ›", Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
                next.Click += (s, e) => { _designTaps[tapName] = (view + 1) % views; RequestRender(); RefreshInspector(); };
                Inspector.Children.Add(next);
            }
            if (_renderer != null && _renderer.Errors.TryGetValue(widget.Id, out string error))
                Inspector.Children.Add(new TextBlock { Text = "⚠ " + error, Foreground = (Brush)FindResource("Brush.Danger"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });

            // Position and size
            Inspector.Children.Add(SectionTitle("Position and size"));
            var position = new UniformGrid { Columns = 4 };
            foreach (string axis in new[] { "X", "Y", "W", "H" })
            {
                var cell = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
                cell.Children.Add(new TextBlock { Text = axis, Style = (Style)FindResource("T.Caption"), Margin = new Thickness(2, 0, 0, 2) });
                var box = new TextBox { Text = Axis(widget, axis).ToString("0.##", CultureInfo.InvariantCulture), FontFamily = (FontFamily)FindResource("Font.Mono"), FontSize = 12 };
                string a = axis;
                box.TextChanged += (s, e) =>
                {
                    if (!double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) return;
                    SetAxis(widget, a, v);
                    RequestRender();
                    CommitSoon("Position");
                };
                AttachWheel(box, 1, v => { box.Text = v.ToString("0.##", CultureInfo.InvariantCulture); });
                _positionBoxes[axis] = box;
                cell.Children.Add(box);
                position.Children.Add(cell);
            }
            Inspector.Children.Add(position);
            Inspector.Children.Add(new TextBlock { Text = "Align on screen", Style = (Style)FindResource("T.Caption"), Margin = new Thickness(0, 10, 0, 4) });
            Inspector.Children.Add(AlignBar(false));
            // Paths are reshaped on the canvas; shapes can become paths to reshape.
            if (widget.Type == "path")
                Inspector.Children.Add(ActionButton("\uE70F", "Edit points on the canvas", () => BeginEditPath(widget)));
            else if (widget.Type == "shape" && PathNodes.FromShape(widget.Get("shape", "Rectangle"), 6, 0.5f, out _) != null)
                Inspector.Children.Add(ActionButton("\uE70F", "Convert to path, to reshape point by point", () => ConvertToPath(widget)));
            Inspector.Children.Add(ActionButton("\uE8EE", "Repeat in rows or around a circle\u2026", RepeatSelection));

            // Properties by group, in definition order; visibility last.
            foreach (IGrouping<string, PropDef> group in type.Props.Where(p => !p.Hidden).GroupBy(p => p.Group))
            {
                string groupName = group.Key;
                bool collapsed = CollapsedGroups.Contains(groupName);
                var content = new StackPanel { Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible };
                var chevron = Glyph(collapsed ? "" : "", 10, "Brush.TextTertiary");
                var title = new StackPanel { Orientation = Orientation.Horizontal };
                title.Children.Add(new TextBlock { Text = groupName.ToUpperInvariant(), Style = (Style)FindResource("T.Overline") });
                chevron.Margin = new Thickness(8, 0, 0, 0);
                title.Children.Add(chevron);
                int formulas = group.Count(p => LayoutRenderer.IsFormula(widget.Get(p.Key, p.Default), p) && p.Kind != PropKind.Formula);
                if (formulas > 0)
                    title.Children.Add(new TextBlock { Text = "  ƒ" + formulas, FontStyle = FontStyles.Italic, FontSize = 12, Foreground = (Brush)FindResource("Brush.Accent"), VerticalAlignment = VerticalAlignment.Center });
                var headerButton = new Border { Child = title, Background = Brushes.Transparent, Cursor = Cursors.Hand, Margin = new Thickness(0, 18, 0, 8) };
                headerButton.MouseLeftButtonUp += (s, e) =>
                {
                    bool nowCollapsed = content.Visibility == Visibility.Visible;
                    content.Visibility = nowCollapsed ? Visibility.Collapsed : Visibility.Visible;
                    if (!nowCollapsed) Motion.FadeIn(content);
                    chevron.Text = nowCollapsed ? "" : "";
                    if (nowCollapsed) CollapsedGroups.Add(groupName); else CollapsedGroups.Remove(groupName);
                };
                Inspector.Children.Add(headerButton);
                foreach (PropDef def in group)
                {
                    FrameworkElement row = PropertyRow(widget, def);
                    _propRows[def.Key] = row;
                    content.Children.Add(row);
                }
                Inspector.Children.Add(content);
                _groupHeaders[groupName] = headerButton;
            }
            UpdateApplicableRows(widget, type);
        }

        private readonly Dictionary<string, FrameworkElement> _propRows = new Dictionary<string, FrameworkElement>();

        private readonly Dictionary<string, FrameworkElement> _groupHeaders = new Dictionary<string, FrameworkElement>();

        /// <summary>Hides settings that don't matter as the widget is set up (and groups left empty), without rebuilding the panel.</summary>
        private void UpdateApplicableRows(DashWidget widget, WidgetType type)
        {
            foreach (PropDef def in type.Props)
                if (_propRows.TryGetValue(def.Key, out FrameworkElement row))
                    row.Visibility = def.Applies(widget, type) ? Visibility.Visible : Visibility.Collapsed;
            foreach (IGrouping<string, PropDef> group in type.Props.GroupBy(p => p.Group))
                if (_groupHeaders.TryGetValue(group.Key, out FrameworkElement header))
                    header.Visibility = group.Any(p => p.Applies(widget, type)) ? Visibility.Visible : Visibility.Collapsed;
        }

        private static double Axis(DashWidget w, string axis) => axis == "X" ? w.X : axis == "Y" ? w.Y : axis == "W" ? w.W : w.H;

        private static void SetAxis(DashWidget w, string axis, double value)
        {
            switch (axis)
            {
                case "X": w.X = value; break;
                case "Y": w.Y = value; break;
                case "W": w.W = Math.Max(2, value); break;
                default: w.H = Math.Max(2, value); break;
            }
        }

        private void RefreshPositionFields()
        {
            if (_selection.Count != 1 || _positionBoxes.Count == 0) return;
            foreach (KeyValuePair<string, TextBox> pair in _positionBoxes)
            {
                string text = Axis(_selection[0], pair.Key).ToString("0.##", CultureInfo.InvariantCulture);
                if (pair.Value.Text != text && !pair.Value.IsKeyboardFocusWithin) pair.Value.Text = text;
            }
        }

        private void FocusPrimaryEditor()
        {
            if (_primaryEditor == null) return;
            _primaryEditor.Focus();
            if (_primaryEditor is TextBox box) box.SelectAll();
            _primaryEditor.BringIntoView();
        }
    }
}
