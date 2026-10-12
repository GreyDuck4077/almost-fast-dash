using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace AlmostFastOverlays
{
    public partial class DesignerWindow
    {
        // ==================================================================
        // Left tabs
        // ==================================================================

        private void LeftTab_Checked(object sender, RoutedEventArgs e)
        {
            if (WidgetsPanel == null) return;
            WidgetsPanel.Visibility = TabWidgets.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            LayersPanel.Visibility = TabLayers.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            ThemePanel.Visibility = TabTheme.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            if (TabLayers.IsChecked == true) RefreshLayers();
        }

        private TextBlock Glyph(string glyph, double size = 14, string brush = null)
        {
            var icon = new TextBlock { Text = glyph, Style = (Style)FindResource("T.Icon"), FontSize = size };
            if (brush != null) icon.Foreground = (Brush)FindResource(brush);
            return icon;
        }

        private TextBlock SectionTitle(string text) => new TextBlock { Text = text.ToUpperInvariant(), Style = (Style)FindResource("T.Overline"), Margin = new Thickness(2, 16, 0, 8) };

        // ==================================================================
        // Toolbox: widgets and blocks
        // ==================================================================

        private void BuildToolbox()
        {
            Toolbox.Children.Clear();
            var search = new TextBox { ToolTip = "Search widgets and blocks" };
            var hint = new TextBlock
            {
                Text = "Search widgets and blocks", IsHitTestVisible = false, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("Brush.TextTertiary")
            };
            search.TextChanged += (s, e) => hint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            var searchHost = new Grid { Margin = new Thickness(0, 6, 0, 2) };
            searchHost.Children.Add(search);
            searchHost.Children.Add(hint);
            Toolbox.Children.Add(searchHost);
            var results = new StackPanel();
            Toolbox.Children.Add(results);
            search.TextChanged += (s, e) => FillToolbox(results, search.Text.Trim());
            FillToolbox(results, "");
        }

        private void FillToolbox(StackPanel host, string filter)
        {
            host.Children.Clear();
            bool Match(string name, string description) =>
                filter.Length == 0 || name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 || (description ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;

            // Quick values and blocks sit in folders; a search opens every folder with a match.
            List<DesignBlock> quick = QuickValues.All().Where(b => Match(b.Name, b.Description) || Match(b.Folder, null)).ToList();
            if (quick.Count > 0)
            {
                host.Children.Add(SectionTitle(QuickValues.Section));
                foreach (IGrouping<string, DesignBlock> folder in quick.GroupBy(b => b.Folder).OrderBy(f => Array.IndexOf(QuickValues.Folders, f.Key)))
                    host.Children.Add(BlockFolder(folder.Key, folder.ToList(), filter.Length > 0, QuickValues.Section));
            }
            List<DesignBlock> blocks = DesignBlocks.BuiltIn().Concat(DesignBlocks.UserMade().Select(b => { b.Folder = "My blocks"; return b; }))
                .Where(b => Match(b.Name, b.Description) || Match(b.Folder, null)).ToList();
            if (blocks.Count > 0)
            {
                host.Children.Add(SectionTitle("Ready-made blocks"));
                foreach (IGrouping<string, DesignBlock> folder in blocks.GroupBy(b => b.Folder).OrderBy(f => FolderOrder(f.Key)))
                    host.Children.Add(BlockFolder(folder.Key, folder.ToList(), filter.Length > 0, "Ready-made blocks"));
            }

            foreach (string category in WidgetCatalog.Categories)
            {
                List<WidgetType> types = WidgetCatalog.All.Where(t => t.Category == category && Match(t.Name, t.Description)).ToList();
                if (types.Count == 0) continue;
                host.Children.Add(SectionTitle(category));
                var grid = new UniformGrid { Columns = 2 };
                foreach (WidgetType type in types) grid.Children.Add(WidgetTile(type));
                host.Children.Add(grid);
            }
        }

        private Border WidgetTile(WidgetType type)
        {
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(new TextBlock { Text = type.Icon, Style = (Style)FindResource("T.Icon"), FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center, Foreground = (Brush)FindResource("Brush.Accent") });
            stack.Children.Add(new TextBlock { Text = type.Name, FontSize = 12, Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            var tile = new Border
            {
                Height = 70, Margin = new Thickness(3), CornerRadius = new CornerRadius(9), Cursor = Cursors.Hand,
                Background = (Brush)FindResource("Brush.Surface"), BorderBrush = (Brush)FindResource("Brush.Border"), BorderThickness = new Thickness(1),
                Child = stack, ToolTip = type.Name + "\n" + type.Description + "\n\nClick to add, or drag onto the screen."
            };
            HookTile(tile, () => AddWidgetAtCenter(type.Id), () => new DataObject("afo-widget", type.Id));
            return tile;
        }

        private static readonly string[] Folders = { "Rev gauges", "Cards", "Warnings and spotter", "My blocks" };
        private static readonly HashSet<string> OpenFolders = new HashSet<string>();

        private static int FolderOrder(string folder)
        {
            int i = Array.IndexOf(Folders, folder);
            return i < 0 ? Folders.Length : i;
        }

        /// <summary>A folder of blocks that opens and closes with a click; it remembers being open while the designer runs.</summary>
        private StackPanel BlockFolder(string name, List<DesignBlock> blocks, bool searching, string section)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
            string key = section + "/" + name;
            bool open = searching || OpenFolders.Contains(key);
            var content = new StackPanel { Visibility = open ? Visibility.Visible : Visibility.Collapsed, Margin = new Thickness(10, 0, 0, 4) };
            // A closed folder's rows are only built the first time it opens.
            void Fill() { if (content.Children.Count == 0) foreach (DesignBlock block in blocks) content.Children.Add(BlockRow(block)); }
            if (open) Fill();

            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock chevron = Glyph(open ? "" : "", 10, "Brush.TextTertiary");
            chevron.HorizontalAlignment = HorizontalAlignment.Center;
            chevron.VerticalAlignment = VerticalAlignment.Center;
            header.Children.Add(chevron);
            var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            title.Children.Add(Glyph("", 14, "Brush.Accent"));
            title.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, FontSize = 12.5, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(title, 1);
            header.Children.Add(title);
            var count = new TextBlock { Text = blocks.Count.ToString(), Style = (Style)FindResource("T.Caption"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(count, 2);
            header.Children.Add(count);
            var row = new Border
            {
                Margin = new Thickness(0, 2, 0, 2), Padding = new Thickness(2, 9, 10, 9), CornerRadius = new CornerRadius(8), Cursor = Cursors.Hand,
                Background = (Brush)FindResource("Brush.Surface"), BorderBrush = (Brush)FindResource("Brush.Border"), BorderThickness = new Thickness(1), Child = header
            };
            row.MouseEnter += (s, e) => row.Background = (Brush)FindResource("Brush.SurfaceHover");
            row.MouseLeave += (s, e) => row.Background = (Brush)FindResource("Brush.Surface");
            row.MouseLeftButtonUp += (s, e) =>
            {
                bool nowOpen = content.Visibility != Visibility.Visible;
                if (nowOpen) Fill();
                content.Visibility = nowOpen ? Visibility.Visible : Visibility.Collapsed;
                chevron.Text = nowOpen ? "" : "";
                if (searching) return;
                if (nowOpen) OpenFolders.Add(key); else OpenFolders.Remove(key);
            };
            panel.Children.Add(row);
            panel.Children.Add(content);
            return panel;
        }

        private Border BlockRow(DesignBlock block)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var icon = Glyph(block.Icon, 16, "Brush.Accent");
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            grid.Children.Add(icon);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = block.Name, FontWeight = FontWeights.SemiBold, FontSize = 12.5 });
            text.Children.Add(new TextBlock { Text = block.Description, Style = (Style)FindResource("T.Caption"), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap });
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            var row = new Border
            {
                Margin = new Thickness(0, 2, 0, 2), Padding = new Thickness(4, 8, 8, 8), CornerRadius = new CornerRadius(8), Cursor = Cursors.Hand,
                Background = (Brush)FindResource("Brush.Surface"), BorderBrush = (Brush)FindResource("Brush.Border"), BorderThickness = new Thickness(1),
                Child = grid, ToolTip = block.Description + "\n\nClick to add, or drag onto the screen."
            };
            HookTile(row, () =>
            {
                List<DashWidget> widgets = block.Create();
                Rect bounds = Union(widgets);
                foreach (DashWidget w in widgets)
                {
                    w.X += Math.Round((CanvasW - bounds.Width) / 2 - bounds.X);
                    w.Y += Math.Round((CanvasH - bounds.Height) / 2 - bounds.Y);
                }
                AddWidgets(widgets, "Add " + block.Name);
            }, () => new DataObject("afo-block", block));
            if (block.UserMade)
            {
                var menu = new ContextMenu();
                menu.Items.Add(MenuItem("Delete block", null, () =>
                {
                    try { System.IO.File.Delete(block.FilePath); } catch { }
                    BuildToolbox();
                }));
                row.ContextMenu = menu;
            }
            return row;
        }

        /// <summary>Click adds; dragging past a few pixels starts drag-and-drop onto the canvas.</summary>
        private void HookTile(Border tile, Action click, Func<DataObject> data)
        {
            Point? down = null;
            tile.MouseEnter += (s, e) => tile.Background = (Brush)FindResource("Brush.SurfaceHover");
            tile.MouseLeave += (s, e) => tile.Background = (Brush)FindResource("Brush.Surface");
            tile.MouseLeftButtonDown += (s, e) => { down = e.GetPosition(tile); tile.CaptureMouse(); e.Handled = true; };
            tile.MouseMove += (s, e) =>
            {
                if (down == null || e.LeftButton != MouseButtonState.Pressed) return;
                Vector moved = e.GetPosition(tile) - down.Value;
                if (Math.Abs(moved.X) < 5 && Math.Abs(moved.Y) < 5) return;
                down = null;
                tile.ReleaseMouseCapture();
                DragDrop.DoDragDrop(tile, data(), DragDropEffects.Copy);
            };
            tile.MouseLeftButtonUp += (s, e) =>
            {
                tile.ReleaseMouseCapture();
                if (down != null) { down = null; click(); }
            };
        }

        private void SaveSelectionAsBlock()
        {
            if (_selection.Count == 0) return;
            string name = Prompt("Save as block", "Name this block so you can reuse it in any design:", _selection.Count == 1 ? _selection[0].Name : "My block");
            if (string.IsNullOrWhiteSpace(name)) return;
            try
            {
                DesignBlocks.Save(name.Trim(), _screen.Widgets.Where(_selection.Contains).ToList());
                BuildToolbox();
                StatusText.Text = "Saved block \"" + name.Trim() + "\". Find it under Add › My blocks.";
            }
            catch (Exception exception)
            {
                StatusText.Text = "Could not save the block: " + exception.Message;
            }
        }

        /// <summary>A small themed text prompt.</summary>
        private string Prompt(string title, string message, string initial)
        {
            var box = new TextBox { Text = initial ?? "", Margin = new Thickness(0, 12, 0, 16) };
            var ok = new Button { Content = "Save", Style = (Style)FindResource("Btn.Primary"), IsDefault = true, MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
            var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90 };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            var body = new StackPanel { Margin = new Thickness(22) };
            body.Children.Add(new TextBlock { Text = message, Style = (Style)FindResource("T.Secondary") });
            body.Children.Add(box);
            body.Children.Add(buttons);
            var dialog = new Window
            {
                Title = title, Owner = this, Width = 420, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = (Brush)FindResource("Brush.Surface"),
                Foreground = (Brush)FindResource("Brush.Text"), FontFamily = (FontFamily)FindResource("Font.Ui"), Content = body
            };
            ok.Click += (s, e) => dialog.DialogResult = true;
            dialog.Loaded += (s, e) => { box.Focus(); box.SelectAll(); };
            return dialog.ShowDialog() == true ? box.Text : null;
        }

        // ==================================================================
        // Layers
        // ==================================================================

        private void RefreshLayers()
        {
            if (LayersList == null || LayersPanel.Visibility != Visibility.Visible) return;
            LayersList.Children.Clear();
            if (_screen.Widgets.Count == 0)
            {
                LayersList.Children.Add(new TextBlock { Text = "This screen is empty. Add widgets from the Add tab.", Style = (Style)FindResource("T.Caption"), Margin = new Thickness(8, 12, 8, 0) });
                return;
            }
            for (int i = _screen.Widgets.Count - 1; i >= 0; i--) LayersList.Children.Add(LayerRow(_screen.Widgets[i]));
        }

        private Border LayerRow(DashWidget widget)
        {
            bool selected = _selection.Contains(widget);
            WidgetType type = WidgetCatalog.Find(widget.Type);
            var grid = new Grid { Height = 30 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock typeIcon = Glyph(type?.Icon ?? "", 13, selected ? "Brush.Accent" : "Brush.TextTertiary");
            typeIcon.HorizontalAlignment = HorizontalAlignment.Center;
            grid.Children.Add(typeIcon);

            var name = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(widget.Name) ? type?.Name : widget.Name, VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis, Opacity = widget.Hidden ? 0.45 : 1, Tag = "error-" + widget.Id
            };
            if (!string.IsNullOrWhiteSpace(widget.Get("visible")))
                name.ToolTip = "Shows when " + widget.Get("visible");
            Grid.SetColumn(name, 1);
            grid.Children.Add(name);

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
            Grid.SetColumn(toggles, 2);
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
                if (e.ClickCount == 2) { RenameLayer(widget, name); e.Handled = true; return; }
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

        private Button SmallToggle(string glyph, string tip, bool active, Action action)
        {
            var button = new Button
            {
                Style = (Style)FindResource("Btn.Ghost"), Width = 26, Height = 24, Padding = new Thickness(0), ToolTip = tip,
                Content = Glyph(glyph, 12, active ? "Brush.Warning" : "Brush.TextTertiary")
            };
            button.Click += (s, e) => { action(); e.Handled = true; };
            return button;
        }

        private void RenameLayer(DashWidget widget, TextBlock label)
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

        // ==================================================================
        // Theme
        // ==================================================================

        private static readonly (string Name, string Accent, string[] Colors)[] ThemePresets =
        {
            // background, background2, style, panel, paneltop, border, track, text, muted, dim
            ("Midnight", null, new[] { "#05070A", "#0E1622", "Bloom", "#0E131A", "#141B24", "#202935", "#1B232E", "#ECF1F7", "#808EA0", "#4E5A6A" }),
            ("Carbon", "#FF3B30", new[] { "#0A0A0A", "#1C1C1C", "Gradient", "#141414", "#1E1E1E", "#2C2C2C", "#262626", "#F2F2F2", "#8C8C8C", "#555555" }),
            ("Deep ocean", "#38BDF8", new[] { "#020812", "#06243F", "Vignette", "#0A1828", "#0F2236", "#173452", "#12263C", "#E6F4FF", "#7FA3C2", "#47647F" }),
            ("Neon", "#FF2BD6", new[] { "#020205", "#140A24", "Vignette", "#B0100A1C", "#C0180F2A", "#40FF2BD6", "#1E1530", "#FFFFFF", "#9D8FB8", "#5C5070" }),
            ("Le Mans", "#C8F04B", new[] { "#071008", "#10200F", "Gradient", "#0D1A0F", "#132415", "#22361F", "#1A2B19", "#F0F7E8", "#8AA084", "#566A50" }),
            ("Daylight", "#0A84FF", new[] { "#E9EDF2", "#FFFFFF", "Gradient", "#FFFFFF", "#F6F8FA", "#D3DAE3", "#DCE2EA", "#0E141B", "#5B6878", "#9AA5B3" }),
        };

        private void ApplyThemePreset((string Name, string Accent, string[] Colors) preset)
        {
            LayoutTheme theme = _layout.Theme;
            string[] c = preset.Colors;
            theme.Background = c[0]; theme.Background2 = c[1]; theme.BackgroundStyle = c[2]; theme.Panel = c[3]; theme.PanelTop = c[4];
            theme.Border = c[5]; theme.Track = c[6]; theme.Text = c[7]; theme.Muted = c[8]; theme.Dim = c[9];
            if (preset.Accent != null) _dashboard.AccentColor = preset.Accent;
            _rendererDirty = true;
            Commit("Theme " + preset.Name);
            BuildThemeEditor();
            RequestRender();
        }

        private void BuildThemeEditor()
        {
            if (ThemeEditor == null) return;
            ThemeEditor.Children.Clear();
            LayoutTheme theme = _layout.Theme;

            ThemeEditor.Children.Add(SectionTitle("Presets"));
            var presets = new WrapPanel();
            foreach (var preset in ThemePresets)
            {
                var swatch = new Border
                {
                    Width = 72, Height = 44, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 6, 6), Cursor = Cursors.Hand,
                    BorderBrush = (Brush)FindResource("Brush.BorderStrong"), BorderThickness = new Thickness(1), ToolTip = "Apply the " + preset.Name + " theme",
                    Background = new LinearGradientBrush(ToMedia(preset.Colors[1]), ToMedia(preset.Colors[0]), 90)
                };
                var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                label.Children.Add(new Border { Width = 26, Height = 4, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(ToMedia(preset.Accent ?? _dashboard.AccentColor)), Margin = new Thickness(0, 0, 0, 4) });
                label.Children.Add(new TextBlock { Text = preset.Name, FontSize = 10.5, Foreground = new SolidColorBrush(ToMedia(preset.Colors[7])) });
                swatch.Child = label;
                var p = preset;
                swatch.MouseLeftButtonUp += (s, e) => ApplyThemePreset(p);
                presets.Children.Add(swatch);
            }
            ThemeEditor.Children.Add(presets);

            ThemeEditor.Children.Add(SectionTitle("Brand"));
            ThemeEditor.Children.Add(ThemeColorRow("Accent", () => _dashboard.AccentColor, v => { _dashboard.AccentColor = v; _rendererDirty = true; }, "Used by $accent and the LEDs' accent everywhere in this dashboard."));
            var fonts = FontCombo(theme.Font, false);
            fonts.SelectionChanged += (s, e) => { if (fonts.SelectedItem is string f) { theme.Font = f; _rendererDirty = true; Commit("Font"); RequestRender(); } };
            ThemeEditor.Children.Add(LabeledRow("Font", fonts, "The default font for every widget set to \"theme\"."));

            ThemeEditor.Children.Add(SectionTitle("Background"));
            var style = new ComboBox { ItemsSource = LayoutTheme.BackgroundStyles, SelectedItem = theme.BackgroundStyle };
            style.SelectionChanged += (s, e) => { theme.BackgroundStyle = (string)style.SelectedItem; Commit("Background"); RequestRender(); };
            ThemeEditor.Children.Add(LabeledRow("Style", style, "Bloom glows with the accent; Gradient and Vignette blend the two colors."));
            ThemeEditor.Children.Add(ThemeColorRow("Base", () => theme.Background, v => theme.Background = v));
            ThemeEditor.Children.Add(ThemeColorRow("Second", () => theme.Background2, v => theme.Background2 = v));

            ThemeEditor.Children.Add(SectionTitle("Surfaces"));
            ThemeEditor.Children.Add(ThemeColorRow("Panel", () => theme.Panel, v => theme.Panel = v, "$panel"));
            ThemeEditor.Children.Add(ThemeColorRow("Panel top", () => theme.PanelTop, v => theme.PanelTop = v, "$paneltop: the top of Soft panels."));
            ThemeEditor.Children.Add(ThemeColorRow("Border", () => theme.Border, v => theme.Border = v, "$border"));
            ThemeEditor.Children.Add(ThemeColorRow("Track", () => theme.Track, v => theme.Track = v, "$track: the unlit part of bars and gauges."));

            ThemeEditor.Children.Add(SectionTitle("Text"));
            ThemeEditor.Children.Add(ThemeColorRow("Text", () => theme.Text, v => theme.Text = v, "$text"));
            ThemeEditor.Children.Add(ThemeColorRow("Muted", () => theme.Muted, v => theme.Muted = v, "$muted: labels."));
            ThemeEditor.Children.Add(ThemeColorRow("Dim", () => theme.Dim, v => theme.Dim = v, "$dim"));

            ThemeEditor.Children.Add(SectionTitle("Status colors"));
            ThemeEditor.Children.Add(ThemeColorRow("Good", () => theme.Good, v => theme.Good = v, "$good: gaining, throttle, OK."));
            ThemeEditor.Children.Add(ThemeColorRow("Bad", () => theme.Bad, v => theme.Bad = v, "$bad: losing, brake, danger."));
            ThemeEditor.Children.Add(ThemeColorRow("Warning", () => theme.Warn, v => theme.Warn = v, "$warn"));
            ThemeEditor.Children.Add(ThemeColorRow("Info", () => theme.Info, v => theme.Info = v, "$info"));
            ThemeEditor.Children.Add(ThemeColorRow("Best lap", () => theme.Purple, v => theme.Purple = v, "$purple"));

            ThemeEditor.Children.Add(SectionTitle("Flags"));
            var flags = new CheckBox { Style = (Style)FindResource("Switch"), Content = "Full-screen flag alerts", IsChecked = _layout.FullScreenFlags };
            flags.Click += (s, e) => { _layout.FullScreenFlags = flags.IsChecked == true; Commit("Flags"); RequestRender(); };
            ThemeEditor.Children.Add(flags);
            ThemeEditor.Children.Add(new TextBlock
            {
                Style = (Style)FindResource("T.Caption"), Margin = new Thickness(0, 6, 0, 0),
                Text = "On: flags take over the whole screen. Off: show them with a Flag widget, or react with FlagActive in a formula."
            });
            var waving = new CheckBox { Style = (Style)FindResource("Switch"), Content = "Wave in a soft breeze", IsChecked = _layout.WavingFlags, Margin = new Thickness(0, 10, 0, 0) };
            waving.Click += (s, e) => { _layout.WavingFlags = waving.IsChecked == true; Commit("Flags"); RequestRender(); };
            ThemeEditor.Children.Add(waving);
        }

        private Grid ThemeColorRow(string label, Func<string> get, Action<string> set, string help = null)
        {
            var editor = new ColorField(this, get(), _layout, () => _dashboard.AccentColor, allowTokens: false);
            editor.Changed += value =>
            {
                set(value);
                _rendererDirty = true;
                CommitSoon("Theme color");
                RequestRender();
            };
            return LabeledRow(label, editor, help);
        }

        private Grid LabeledRow(string label, FrameworkElement editor, string help = null)
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("Brush.TextSecondary"), ToolTip = help, TextTrimming = TextTrimming.CharacterEllipsis };
            row.Children.Add(text);
            Grid.SetColumn(editor, 1);
            row.Children.Add(editor);
            return row;
        }

        private static List<string> _fontNames;

        internal static List<string> FontNames()
        {
            if (_fontNames != null) return _fontNames;
            var preferred = new[] { "Bahnschrift", "Bahnschrift SemiBold", "Bahnschrift Condensed", "Bahnschrift SemiBold Condensed", "Bahnschrift Light", "Segoe UI", "Segoe UI Black", "Segoe UI Semibold", "Consolas", "Arial Black", "Impact" };
            using (var installed = new System.Drawing.Text.InstalledFontCollection())
            {
                var names = installed.Families.Select(f => f.Name).ToList();
                _fontNames = preferred.Where(names.Contains).Concat(names.Where(n => !preferred.Contains(n)).OrderBy(n => n)).ToList();
            }
            return _fontNames;
        }

        internal ComboBox FontCombo(string current, bool includeTheme)
        {
            var items = new List<string>();
            if (includeTheme) items.Add("theme");
            items.AddRange(FontNames());
            var combo = new ComboBox { ItemsSource = items, IsTextSearchEnabled = true, MaxDropDownHeight = 420 };
            combo.SelectedItem = items.FirstOrDefault(i => string.Equals(i, current, StringComparison.OrdinalIgnoreCase)) ?? (includeTheme ? "theme" : items.FirstOrDefault());
            combo.ItemTemplate = null;
            return combo;
        }

        internal static Color ToMedia(string hex)
        {
            System.Drawing.Color? c = Expr.ParseHex(hex);
            return c.HasValue ? Color.FromArgb(c.Value.A, c.Value.R, c.Value.G, c.Value.B) : Colors.Transparent;
        }
    }
}
