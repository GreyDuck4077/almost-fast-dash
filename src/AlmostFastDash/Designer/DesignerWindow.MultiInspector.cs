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
        // ------------------------------------------------------------------ several selected

        private void BuildMultiInspector()
        {
            Inspector.Children.Add(Heading(_selection.Count + " widgets"));
            Inspector.Children.Add(Caption("Drag to move them together. Align and distribute below."));
            AddGhostNotice(_selection);
            if (SelectionIsOneGroup(out _)) AddModuleOptions(_selection[0]);
            Inspector.Children.Add(SectionTitle("Align"));
            Inspector.Children.Add(AlignBar(true));
            Inspector.Children.Add(SectionTitle("Distribute and match"));
            var row = new WrapPanel();
            row.Children.Add(ActionButton("", "Space horizontally", () => AlignSelection("hspace")));
            row.Children.Add(ActionButton("", "Space vertically", () => AlignSelection("vspace")));
            row.Children.Add(ActionButton("", "Same width", () => AlignSelection("width")));
            row.Children.Add(ActionButton("", "Same height", () => AlignSelection("height")));
            Inspector.Children.Add(row);
            Inspector.Children.Add(SectionTitle("Arrange"));
            var arrange = new WrapPanel();
            arrange.Children.Add(ActionButton("", "Duplicate", () => DuplicateSelection()));
            arrange.Children.Add(ActionButton("", "Delete", DeleteSelection));
            arrange.Children.Add(ActionButton("", "Save as module", SaveSelectionAsModule));
            arrange.Children.Add(ActionButton("", "Lock", ToggleLock));
            bool oneGroup = SelectionIsOneGroup(out _);
            if (!oneGroup) arrange.Children.Add(ActionButton("", "Group  Ctrl+G", GroupSelection));
            arrange.Children.Add(ActionButton("", "Repeat…", RepeatSelection));
            Inspector.Children.Add(arrange);
            Inspector.Children.Add(ScaleRow());
            AddSelectionColors(_selection);

            if (oneGroup)
            {
                DashWidget first = _selection[0];
                Inspector.Children.Add(SectionTitle("Group"));
                var groupName = new TextBox { Text = first.GroupName, MaxLength = 40 };
                groupName.TextChanged += (s,
                    e) => { foreach (DashWidget w in _selection) w.GroupName = groupName.Text.Trim(); CommitSoon("Rename group"); RefreshLayers(); };
                Inspector.Children.Add(LabeledRow("Name", groupName));
                var slot = new TextBox { Text = first.Slot.ToString("0.##", CultureInfo.InvariantCulture),
                    FontFamily = (FontFamily)FindResource("Font.Mono"), FontSize = 12, Width = 70,
                    HorizontalAlignment = HorizontalAlignment.Left };
                slot.TextChanged += (s, e) =>
                {
                    if (!double.TryParse(slot.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) return;
                    foreach (DashWidget w in _selection) w.Slot = v;
                    RequestRender();
                    CommitSoon("Group slot");
                };
                AttachWheel(slot, 1, v => slot.Text = v.ToString("0.##", CultureInfo.InvariantCulture));
                Inspector.Children.Add(LabeledRow("Slot", slot));
                Inspector.Children.Add(Caption("Formulas in the group read this as Slot. " +
                    "Ctrl+D copies the group with the next Slot: build P1, then copy it for P2, P3 and on.", 6));
                var groupActions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
                groupActions.Children.Add(ActionButton("", "Copy as next Slot", () => DuplicateSelection()));
                groupActions.Children.Add(ActionButton("", "Ungroup", Ungroup));
                Inspector.Children.Add(groupActions);
            }

            // Settings every selected widget has, edited together.
            List<WidgetType> types = _selection.Select(w => WidgetCatalog.Find(w.Type)).Where(t => t != null).Distinct().ToList();
            if (types.Count > 0 && types.Count == _selection.Select(w => w.Type).Distinct().Count())
            {
                List<PropDef> common = types[0].Props.Where(p => p.Key != "visible" && !p.Hidden &&
                    types.All(t => t.Prop(p.Key) is PropDef other && other.Kind == p.Kind))
                    .ToList();
                if (common.Count > 0)
                {
                    Inspector.Children.Add(SectionTitle(types.Count == 1 ? "Settings (all " + _selection.Count + ")" : "Shared settings"));
                    Inspector.Children.Add(Caption("Changes here apply to every selected widget.", 0));
                    foreach (IGrouping<string, PropDef> group in common.GroupBy(p => p.Group))
                    {
                        var content = new StackPanel { Visibility = Visibility.Collapsed };
                        var title = new TextBlock { Text = group.Key.ToUpperInvariant() + "  ›",
                            Style = (Style)FindResource("T.Overline") };
                        var header = new Border { Child = title, Background = Brushes.Transparent, Cursor = Cursors.Hand,
                            Margin = new Thickness(0, 14, 0, 6) };
                        header.MouseLeftButtonUp += (s, e) =>
                        {
                            bool open = content.Visibility != Visibility.Visible;
                            // Rows are built when a group first opens; most multi-selections only need one or two.
                            if (open && content.Children.Count == 0)
                                foreach (PropDef def in group) content.Children.Add(PropertyRow(_selection[0], def, _selection.ToList()));
                            content.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
                            title.Text = group.Key.ToUpperInvariant() + (open ? "  ˅" : "  ›");
                            if (open) Motion.FadeIn(content);
                        };
                        Inspector.Children.Add(header);
                        Inspector.Children.Add(content);
                    }
                }
            }

            // Shared show-when for the whole group.
            Inspector.Children.Add(SectionTitle("Show when (all selected)"));
            string shared = _selection.Select(w => w.Get("visible", "")).Distinct().Count() == 1 ? _selection[0].Get("visible", "") : "";
            var visible = new FormulaBox(this, LayoutRenderer.Strip(shared), CurrentScope, "e.g. OnPitRoad");
            visible.Changed += text =>
            {
                foreach (DashWidget w in _selection) w.Set("visible", text.Trim().Length == 0 ? "" : "=" + text.Trim());
                RequestRender();
                CommitSoon("Show when");
            };
            _liveFormulas.Add(visible);
            Inspector.Children.Add(visible);
        }

        private Button ActionButton(string glyph, string text, Action action)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(Glyph(glyph, 12));
            content.Children.Add(new TextBlock { Text = text, Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center });
            var button = new Button { Content = content, Margin = new Thickness(0, 0, 6, 6), Height = 32,
                Padding = new Thickness(10, 0, 10, 0) };
            System.Windows.Automation.AutomationProperties.SetName(button, text);
            button.Click += (s, e) => action();
            return button;
        }

        private UniformGrid AlignBar(bool multi)
        {
            var bar = new UniformGrid { Columns = 6, Margin = new Thickness(0, 0, 0, 4) };
            (string Glyph, string How, string Tip)[] items =
            {
                ("", "left", "Align left"), ("", "center", "Center horizontally"), ("", "right", "Align right"),
                ("", "top", "Align top"), ("", "middle", "Center vertically"), ("", "bottom", "Align bottom")
            };
            foreach (var item in items)
            {
                var button = new Button
                {
                    Style = (Style)FindResource("Btn.Ghost"), Height = 30, Padding = new Thickness(0), Margin = new Thickness(0, 0, 4, 0),
                    ToolTip = item.Tip + (multi ? "" : " on the screen"), Content = AlignGlyph(item.How)
                };
                string how = item.How;
                button.Click += (s, e) => AlignSelection(how);
                System.Windows.Automation.AutomationProperties.SetName(button, item.Tip);
                bar.Children.Add(button);
            }
            return bar;
        }

        /// <summary>Small drawn align icons, so they read clearly whatever icon font is installed.</summary>
        private FrameworkElement AlignGlyph(string how)
        {
            var canvas = new Canvas { Width = 16, Height = 16 };
            Brush line = (Brush)FindResource("Brush.Accent"), block = (Brush)FindResource("Brush.TextSecondary");
            void Rect(double x, double y, double w, double h,
                Brush b) { var r = new System.Windows.Shapes.Rectangle { Width = w, Height = h, Fill = b, RadiusX = 1,
                RadiusY = 1 }; Canvas.SetLeft(r, x); Canvas.SetTop(r, y); canvas.Children.Add(r); }
            switch (how)
            {
                case "left": Rect(1, 1, 1.5, 14, line); Rect(4, 3, 10, 4, block); Rect(4, 9, 6, 4, block); break;
                case "center": Rect(7.25, 1, 1.5, 14, line); Rect(3, 3, 10, 4, block); Rect(5, 9, 6, 4, block); break;
                case "right": Rect(13.5, 1, 1.5, 14, line); Rect(2, 3, 10, 4, block); Rect(6, 9, 6, 4, block); break;
                case "top": Rect(1, 1, 14, 1.5, line); Rect(3, 4, 4, 10, block); Rect(9, 4, 4, 6, block); break;
                case "middle": Rect(1, 7.25, 14, 1.5, line); Rect(3, 3, 4, 10, block); Rect(9, 5, 4, 6, block); break;
                default: Rect(1, 13.5, 14, 1.5, line); Rect(3, 2, 4, 10, block); Rect(9, 6, 4, 6, block); break;
            }
            return canvas;
        }
    }
}
