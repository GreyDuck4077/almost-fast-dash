using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DrawingColor = System.Drawing.Color;

namespace AlmostFastDash.Lights
{
    public partial class LightsWindow : Window
    {
        // ==================================================================
        // Layer stack
        // ==================================================================

        private void RefreshLayers()
        {
            LayerList.Children.Clear();
            if (_profile.Layers.Count == 0)
            {
                LayerList.Children.Add(new TextBlock { Text = "No layers yet. Click Add, or pick a preset.", Style = (Style)FindResource("T.Caption"), Margin = new Thickness(8, 12, 8, 0) });
                return;
            }
            for (int i = 0; i < _profile.Layers.Count; i++) LayerList.Children.Add(LayerCard(_profile.Layers[i], i));
        }

        private Border LayerCard(LightLayer layer, int index)
        {
            bool selected = layer == _selected;
            LightEffect effect = LightEffects.Find(layer.Effect);
            LedTarget target = LedTargets.Find(layer.Target, Devices);

            var grid = new Grid { Margin = new Thickness(10, 9, 10, 9) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // An eye, as in the screen designer's layers: lit when the layer is on.
            var enabled = new Button
            {
                Style = (Style)FindResource("Btn.Ghost"), Width = 28, Height = 28, Padding = new Thickness(0), Margin = new Thickness(-4, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
                ToolTip = layer.Enabled ? "On: click to turn this layer off" : "Off: click to turn this layer on",
                Content = new TextBlock
                {
                    Text = layer.Enabled ? "" : "", Style = (Style)FindResource("T.Icon"), FontSize = 13,
                    Foreground = (Brush)FindResource(layer.Enabled ? "Brush.Accent" : "Brush.TextTertiary")
                }
            };
            enabled.Click += (s, e) => { layer.Enabled = !layer.Enabled; Commit(layer.Enabled ? "Enable layer" : "Disable layer"); RefreshLayers(); e.Handled = true; };
            grid.Children.Add(enabled);

            var text = new StackPanel { Opacity = layer.Enabled ? 1 : 0.45 };
            var title = new StackPanel { Orientation = Orientation.Horizontal };
            title.Children.Add(new TextBlock { Text = (index + 1) + "", Foreground = (Brush)FindResource("Brush.TextTertiary"), FontSize = 12, Width = 18, VerticalAlignment = VerticalAlignment.Center });
            title.Children.Add(new TextBlock { Text = layer.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(title);
            text.Children.Add(new TextBlock
            {
                Text = (effect?.Name ?? layer.Effect) + " · " + (target?.Name ?? layer.Target) + (layer.Blend == "Replace" ? " · replaces" : ""),
                Style = (Style)FindResource("T.Caption"), FontSize = 12, Margin = new Thickness(18, 1, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap
            });
            if (!string.IsNullOrWhiteSpace(layer.Condition))
                text.Children.Add(new TextBlock
                {
                    Text = "ƒ when " + layer.Condition, FontFamily = (FontFamily)FindResource("Font.Mono"), FontSize = 12, Foreground = (Brush)FindResource("Brush.Accent"),
                    Margin = new Thickness(18, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap
                });
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            var swatch = new Border { Width = 10, Height = 26, CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Center, Background = SwatchFor(layer), Margin = new Thickness(8, 0, 0, 0) };
            Grid.SetColumn(swatch, 2);
            grid.Children.Add(swatch);

            var card = new Border
            {
                Child = grid, CornerRadius = new CornerRadius(9), Margin = new Thickness(0, 0, 0, 6), Cursor = Cursors.Hand, AllowDrop = true,
                Background = (Brush)FindResource(selected ? "Brush.AccentSubtle" : "Brush.Surface"),
                BorderBrush = (Brush)FindResource(selected ? "Brush.AccentBorder" : "Brush.Border"), BorderThickness = new Thickness(1)
            };
            Point? down = null;
            card.MouseLeftButtonDown += (s, e) => down = e.GetPosition(card);
            card.MouseMove += (s, e) =>
            {
                if (down == null || e.LeftButton != MouseButtonState.Pressed) return;
                if (Math.Abs(e.GetPosition(card).Y - down.Value.Y) < 6) return;
                down = null;
                DragDrop.DoDragDrop(card, new DataObject("afo-light-layer", layer.Id), DragDropEffects.Move);
            };
            card.MouseLeftButtonUp += (s, e) =>
            {
                if (down == null) return;
                down = null;
                Select(layer);
            };
            card.DragOver += (s, e) => { e.Effects = e.Data.GetDataPresent("afo-light-layer") ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; };
            card.Drop += (s, e) =>
            {
                if (!(e.Data.GetData("afo-light-layer") is string id) || id == layer.Id) return;
                LightLayer moving = _profile.Layers.FirstOrDefault(l => l.Id == id);
                if (moving == null) return;
                bool above = e.GetPosition(card).Y < card.ActualHeight / 2;
                _profile.Layers.Remove(moving);
                int at = _profile.Layers.IndexOf(layer);
                _profile.Layers.Insert(above ? at : at + 1, moving);
                Commit("Reorder");
                RefreshLayers();
            };
            var menu = new ContextMenu();
            menu.Items.Add(MenuItem("Duplicate", () => { Select(layer); Duplicate(); }));
            menu.Items.Add(MenuItem("Move to top", () => { _profile.Layers.Remove(layer); _profile.Layers.Insert(0, layer); Commit("Reorder"); RefreshLayers(); }));
            menu.Items.Add(MenuItem("Move to bottom", () => { _profile.Layers.Remove(layer); _profile.Layers.Add(layer); Commit("Reorder"); RefreshLayers(); }));
            menu.Items.Add(MenuItem("Delete", () => { Select(layer); DeleteSelected(); }));
            card.ContextMenu = menu;
            return card;
        }

        /// <summary>A small color strip showing what a layer lights up with.</summary>
        private Brush SwatchFor(LightLayer layer)
        {
            var scope = CurrentScope();
            Color C(string key, string fallback)
            {
                LightEffect effect = LightEffects.Find(layer.Effect);
                string raw = layer.Get(key, effect?.Prop(key)?.Default ?? fallback);
                DrawingColor? c = Expr.Color(LayoutRenderer.Strip(raw), scope) ?? Expr.Color(fallback, scope);
                return c.HasValue ? Color.FromRgb(c.Value.R, c.Value.G, c.Value.B) : Colors.Gray;
            }
            switch (layer.Effect)
            {
                case "revs":
                case "meter":
                case "gradient":
                    return new LinearGradientBrush(new GradientStopCollection { new GradientStop(C("high", "bad"), 0), new GradientStop(C("mid", "warn"), 0.5), new GradientStop(C("low", "good"), 1) }, 90);
                case "center":
                    return new LinearGradientBrush(C("positive", "good"), C("negative", "bad"), 90);
                case "flag":
                    return new LinearGradientBrush(new GradientStopCollection { new GradientStop(Colors.Yellow, 0), new GradientStop(Colors.White, 0.5), new GradientStop(Color.FromRgb(0, 90, 255), 1) }, 90);
                case "blank":
                    return new SolidColorBrush(Color.FromRgb(20, 20, 24));
                default:
                    return new SolidColorBrush(C("color", "accent"));
            }
        }

        private static MenuItem MenuItem(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (s, e) => action();
            return item;
        }

        private void Select(LightLayer layer)
        {
            FlushCommit();
            _selected = layer;
            RefreshLayers();
            RefreshInspector();
        }

        private void AddLayer_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = PlacementMode.Bottom };
            foreach (LightEffect effect in LightEffects.All)
            {
                LightEffect fx = effect;
                var header = new StackPanel { Margin = new Thickness(0, 1, 0, 1), MaxWidth = 320 };
                header.Children.Add(new TextBlock { Text = fx.Name, FontWeight = FontWeights.SemiBold });
                header.Children.Add(new TextBlock { Text = fx.Description, Style = (Style)FindResource("T.Caption"), TextWrapping = TextWrapping.Wrap });
                MenuItem item = MenuItem("", () => AddLayer(fx));
                item.Header = header;
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        }

        private void AddLayer(LightEffect effect)
        {
            string target;
            switch (effect.Id)
            {
                case "spotter": target = "sides"; break;
                case "flag": target = "brow"; break;
                case "center": target = "brow-mirror"; break;
                case "meter": case "gradient": case "breathe": target = "brow"; break;
                default: target = "shift"; break;
            }
            var layer = new LightLayer { Name = effect.Name, Effect = effect.Id, Target = target };
            int at = _selected == null ? 0 : Math.Max(0, _profile.Layers.IndexOf(_selected));
            _profile.Layers.Insert(at, layer);
            _selected = layer;
            Commit("Add " + effect.Name);
            RefreshLayers();
            RefreshInspector();
            StatusText.Text = "Added \"" + effect.Name + "\" above the selected layer.";
        }

        private void Duplicate()
        {
            if (_selected == null) return;
            LightLayer copy = SettingsStore.Clone(_selected);
            copy.Id = DashScreen.NewId("layer");
            copy.Name = _selected.Name + " copy";
            _profile.Layers.Insert(_profile.Layers.IndexOf(_selected), copy);
            _selected = copy;
            Commit("Duplicate");
            RefreshLayers();
            RefreshInspector();
        }

        private void DeleteSelected()
        {
            if (_selected == null) return;
            int index = _profile.Layers.IndexOf(_selected);
            _profile.Layers.Remove(_selected);
            _selected = _profile.Layers.Count == 0 ? null : _profile.Layers[Math.Min(index, _profile.Layers.Count - 1)];
            Commit("Delete layer");
            RefreshLayers();
            RefreshInspector();
        }

        private void Move(int step)
        {
            if (_selected == null) return;
            int index = _profile.Layers.IndexOf(_selected), target = Math.Max(0, Math.Min(_profile.Layers.Count - 1, index + step));
            if (index == target) return;
            _profile.Layers.RemoveAt(index);
            _profile.Layers.Insert(target, _selected);
            Commit("Reorder");
            RefreshLayers();
        }

        private void Up_Click(object sender, RoutedEventArgs e) => Move(-1);

        private void Down_Click(object sender, RoutedEventArgs e) => Move(1);

        private void Duplicate_Click(object sender, RoutedEventArgs e) => Duplicate();

        private void Delete_Click(object sender, RoutedEventArgs e) => DeleteSelected();

        private void Presets_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = PlacementMode.Bottom };
            foreach (LightPreset preset in LightPresets.All)
            {
                LightPreset p = preset;
                var header = new StackPanel { Margin = new Thickness(0, 1, 0, 1), MaxWidth = 340 };
                header.Children.Add(new TextBlock { Text = p.Name, FontWeight = FontWeights.SemiBold });
                header.Children.Add(new TextBlock { Text = p.Description, Style = (Style)FindResource("T.Caption"), TextWrapping = TextWrapping.Wrap });
                var item = new MenuItem { Header = header };
                item.Click += (s, a) =>
                {
                    if (_profile.Layers.Count > 0 && MessageBox.Show(this, "Replace these layers with the \"" + p.Name + "\" preset? You can undo this.",
                            "Light presets", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
                    LightProfile previous = _profile;
                    _profile = p.Create(_dashboard);
                    // A preset changes the layers, not the colors chosen for them.
                    _profile.ThemeId = previous.ThemeId;
                    _profile.CustomLow = previous.CustomLow;
                    _profile.CustomMid = previous.CustomMid;
                    _profile.CustomHigh = previous.CustomHigh;
                    _profile.Normalize();
                    _selected = _profile.Layers.FirstOrDefault();
                    _renderer = null;
                    Commit("Preset " + p.Name);
                    LoadOutputControls();
                    RefreshLayers();
                    RefreshInspector();
                };
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        }

        // ==================================================================
        // Inspector
        // ==================================================================

        // Targets kept for layers that already use them, but not offered: the device and group
        // entries and the split light bar cover them more plainly.
        private static readonly HashSet<string> RetiredTargets = new HashSet<string> { "dv5", "brow-left", "brow-right" };

        /// <summary>
        /// The LEDs a layer can light, in two plain sections: by role across every device (only roles
        /// some device has), then each device on its own, whole or one group.
        /// </summary>
        private List<object> TargetItems(List<LedTarget> targets, string current)
        {
            var items = new List<object>();
            ComboBoxItem Header(string text) => new ComboBoxItem
            {
                Content = text.ToUpperInvariant(), IsEnabled = false, Focusable = false, FontSize = 12, Margin = new Thickness(0, items.Count == 0 ? 0 : 6, 0, 0),
                Foreground = (Brush)FindResource("Brush.TextTertiary")
            };
            ComboBoxItem Item(string text, string id) => new ComboBoxItem { Content = text, Tag = id, Padding = new Thickness(18, 6, 10, 6) };

            items.Add(Header("By role, on every device"));
            foreach (LedTarget target in targets.Where(t => !t.Id.StartsWith("device:") && !t.Id.StartsWith("group:")))
                if (target.Id == current || (target.Strips.Count > 0 && !RetiredTargets.Contains(target.Id)))
                    items.Add(Item(target.Name, target.Id));
            foreach (DeviceSettings device in Devices.Where(d => d.Enabled))
            {
                items.Add(Header(device.Name));
                items.Add(Item("All " + device.LedCount + " LEDs", "device:" + device.Id));
                foreach (LedGroup group in device.Groups)
                    items.Add(Item(group.Name + "  ·  " + (group.From == group.To ? "LED " + group.From : "LEDs " + group.From + "–" + group.To), "group:" + device.Id + "/" + group.Id));
            }
            if (!items.OfType<ComboBoxItem>().Any(i => (string)i.Tag == current))
                items.Add(Item("A device or group that isn't set up", current));
            return items;
        }
    }
}
