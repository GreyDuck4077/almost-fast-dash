using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AlmostFastDash.Lights
{
    public partial class LightsWindow : Window
    {
        private TextBlock Section(string text) => new TextBlock { Text = text.ToUpperInvariant(), Style = (Style)FindResource("T.Overline"), Margin = new Thickness(0, 18, 0, 8) };

        private TextBlock Caption(string text, double top = 4) => new TextBlock { Text = text, Style = (Style)FindResource("T.Caption"), Margin = new Thickness(0, top, 0, 0) };

        private Grid Row(string label, FrameworkElement editor, string help = null)
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(122) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("Brush.TextSecondary"), ToolTip = help, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 8, 0) });
            Grid.SetColumn(editor, 1);
            row.Children.Add(editor);
            return row;
        }

        /// <summary>A label over a full-width editor, for pickers whose choices have long names.</summary>
        private StackPanel Stacked(string label, FrameworkElement editor, double top = 3)
        {
            var row = new StackPanel { Margin = new Thickness(0, top, 0, 3) };
            row.Children.Add(new TextBlock { Text = label, Foreground = (Brush)FindResource("Brush.TextSecondary"), Margin = new Thickness(0, 0, 0, 4) });
            row.Children.Add(editor);
            return row;
        }

        private void RefreshInspector()
        {
            Inspector.Children.Clear();
            _formulas.Clear();
            if (_selected == null)
            {
                Inspector.Children.Add(new TextBlock { Text = "Light layers", Style = (Style)FindResource("T.Title") });
                Inspector.Children.Add(Caption("Each layer lights LEDs with an effect while its condition is true. Higher layers win, so put warnings on top."));
                Inspector.Children.Add(Section("Good to know"));
                Inspector.Children.Add(Caption("• Rev lights use the car's own shift points from iRacing.\n• Replace takes over a layer's whole target: right for flags and pit lights.\n• Hover any LED to see which layer lit it."));
                return;
            }

            LightLayer layer = _selected;
            LightEffect effect = LightEffects.Find(layer.Effect) ?? LightEffects.All[0];

            var name = new TextBox { Text = layer.Name, FontWeight = FontWeights.SemiBold, MaxLength = 40 };
            name.TextChanged += (s, e) => { if (name.Text.Trim().Length > 0) { layer.Name = name.Text.Trim(); CommitSoon("Rename"); } };
            name.LostKeyboardFocus += (s, e) => RefreshLayers();
            Inspector.Children.Add(name);

            Inspector.Children.Add(Section("What and where"));
            var effectCombo = new ComboBox { ItemsSource = LightEffects.All.Select(x => x.Name).ToList(), SelectedItem = effect.Name };
            effectCombo.SelectionChanged += (s, e) =>
            {
                LightEffect chosen = LightEffects.All.FirstOrDefault(x => x.Name == (string)effectCombo.SelectedItem);
                if (chosen == null || chosen.Id == layer.Effect) return;
                layer.Effect = chosen.Id;
                Commit("Effect");
                RefreshLayers();
                RefreshInspector();
            };
            Inspector.Children.Add(Stacked("Effect", effectCombo));
            Inspector.Children.Add(Caption(effect.Description, 0));
            List<LedTarget> targets = LedTargets.For(Devices);
            List<object> targetItems = TargetItems(targets, layer.Target);
            var targetCombo = new ComboBox { ItemsSource = targetItems };
            targetCombo.SelectedItem = targetItems.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == layer.Target);
            targetCombo.SelectionChanged += (s, e) =>
            {
                if (!(targetCombo.SelectedItem is ComboBoxItem chosen) || !(chosen.Tag is string id) || id == layer.Target) return;
                layer.Target = id;
                Commit("Target");
                RefreshLayers();
                RefreshInspector();
            };
            Inspector.Children.Add(Stacked("LEDs", targetCombo, 12));
            Inspector.Children.Add(Caption(targets.FirstOrDefault(x => x.Id == layer.Target)?.Description ?? "Aimed at a device or group that isn't set up right now.", 0));

            Inspector.Children.Add(Section("When and how"));
            Inspector.Children.Add(new TextBlock { Text = "Show when", Foreground = (Brush)FindResource("Brush.TextSecondary"), Margin = new Thickness(0, 0, 0, 4) });
            var condition = new FormulaBox(this, LayoutRenderer.Strip(layer.Condition), CurrentScope, "Empty means always, e.g. OnPitRoad && PitLimiter");
            condition.Changed += text => { layer.Condition = text.Trim(); CommitSoon("Condition"); };
            _formulas.Add(condition);
            Inspector.Children.Add(condition);
            Inspector.Children.Add(Caption("Empty means always. Try OnPitRoad or AbsActive.", 4));
            var blend = new ComboBox { ItemsSource = LightLayer.Blends, SelectedItem = layer.Blend };
            blend.SelectionChanged += (s, e) => { layer.Blend = (string)blend.SelectedItem; Commit("Blend"); RefreshLayers(); };
            Inspector.Children.Add(Row("Blend", blend, "Over: only lit LEDs cover the layers below. Replace: the whole target is taken over and unlit LEDs go dark."));
            Inspector.Children.Add(Row("Brightness", NumberEditor(layer.Brightness.ToString(CultureInfo.InvariantCulture), 0, 100, 1, v =>
            {
                if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int b)) { layer.Brightness = Math.Max(0, Math.Min(100, b)); CommitSoon("Brightness"); }
            })));

            string source = layer.Get("source", effect.Prop("source")?.Default);
            foreach (IGrouping<string, PropDef> group in effect.Props.GroupBy(p => p.Group))
            {
                var visible = group.Where(p => Relevant(effect, p, source)).ToList();
                if (visible.Count == 0) continue;
                Inspector.Children.Add(Section(group.Key));
                foreach (PropDef def in visible) Inspector.Children.Add(PropertyRow(layer, def));
            }
        }

        /// <summary>Hides properties that do not apply, such as RPM limits when revs come from the car.</summary>
        private static bool Relevant(LightEffect effect, PropDef def, string source)
        {
            if (effect.Id != "revs") return true;
            if (def.Key == "fromRpm" || def.Key == "toRpm") return source == "RPM range";
            if (def.Key == "value" || def.Key == "flashWhen") return source == "Formula";
            return true;
        }

        private FrameworkElement PropertyRow(LightLayer layer, PropDef def)
        {
            string raw = layer.Get(def.Key, def.Default) ?? "";
            void Apply(string value) { layer.Set(def.Key, value); CommitSoon("Edit " + def.Label.ToLowerInvariant()); }
            switch (def.Kind)
            {
                case PropKind.Formula:
                {
                    var panel = new StackPanel { Margin = new Thickness(0, 3, 0, 3) };
                    panel.Children.Add(new TextBlock { Text = def.Label, Foreground = (Brush)FindResource("Brush.TextSecondary"), Margin = new Thickness(0, 0, 0, 4), ToolTip = def.Help });
                    var box = new FormulaBox(this, LayoutRenderer.Strip(raw), CurrentScope, def.Help);
                    box.Changed += text => Apply(text.Trim().Length == 0 ? "" : "=" + text.Trim());
                    _formulas.Add(box);
                    panel.Children.Add(box);
                    return panel;
                }
                case PropKind.Number:
                    return Row(def.Label, NumberEditor(raw, def.Min, def.Max, def.Step, Apply), def.Help);
                case PropKind.Color:
                {
                    var field = new ColorField(this, raw, _colorContext, () => _dashboard.AccentColor, true, string.IsNullOrEmpty(def.Default));
                    field.Changed += v => { Apply(v); RefreshLayers(); };
                    return Row(def.Label, field, def.Help);
                }
                case PropKind.Bool:
                {
                    var toggle = new CheckBox { Style = (Style)FindResource("Switch"), IsChecked = Expr.Bool(raw), VerticalAlignment = VerticalAlignment.Center };
                    toggle.Click += (s, e) => { layer.Set(def.Key, toggle.IsChecked == true ? "true" : "false"); Commit("Edit " + def.Label.ToLowerInvariant()); };
                    return Row(def.Label, toggle, def.Help);
                }
                case PropKind.Choice:
                {
                    var combo = new ComboBox { ItemsSource = def.Choices, SelectedItem = def.Choices.Contains(raw) ? raw : def.Default };
                    combo.SelectionChanged += (s, e) =>
                    {
                        layer.Set(def.Key, (string)combo.SelectedItem);
                        Commit("Edit " + def.Label.ToLowerInvariant());
                        if (def.Key == "source") RefreshInspector();
                    };
                    return Row(def.Label, combo, def.Help);
                }
                default:
                {
                    var box = new TextBox { Text = raw };
                    box.TextChanged += (s, e) => Apply(box.Text);
                    return Row(def.Label, box, def.Help);
                }
            }
        }

        private FrameworkElement NumberEditor(string raw, double min, double max, double step, Action<string> apply)
        {
            var box = new TextBox { Text = raw, Width = 70, FontFamily = (FontFamily)FindResource("Font.Mono"), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Left };
            step = step > 0 ? step : 1;
            bool ranged = !double.IsNaN(min) && !double.IsNaN(max) && max > min;
            Slider slider = null;
            bool syncing = false;
            box.TextChanged += (s, e) =>
            {
                if (!double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) return;
                if (slider != null && !syncing) { syncing = true; slider.Value = Math.Max(min, Math.Min(max, v)); syncing = false; }
                apply(box.Text.Trim());
            };
            box.PreviewMouseWheel += (s, e) =>
            {
                if (!box.IsKeyboardFocusWithin) return;
                double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v);
                v += e.Delta > 0 ? step : -step;
                if (ranged) v = Math.Max(min, Math.Min(max, v));
                box.Text = v.ToString("0.###", CultureInfo.InvariantCulture);
                e.Handled = true;
            };
            if (!ranged) return box;
            double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double initial);
            slider = new Slider { Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, initial)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            slider.ValueChanged += (s, e) =>
            {
                if (syncing) return;
                syncing = true;
                box.Text = (Math.Round(slider.Value / step) * step).ToString("0.###", CultureInfo.InvariantCulture);
                syncing = false;
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.Children.Add(box);
            Grid.SetColumn(slider, 1);
            grid.Children.Add(slider);
            return grid;
        }
    }
}
