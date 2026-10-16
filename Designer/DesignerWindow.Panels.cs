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
        // Panels: the library on the left, properties or the theme on the right (layers are always there)
        // ==================================================================

        private void RightTab_Checked(object sender, RoutedEventArgs e)
        {
            if (InspectorScroller == null || ThemePanel == null) return;
            bool theme = TabTheme.IsChecked == true;
            InspectorScroller.Visibility = theme ? Visibility.Collapsed : Visibility.Visible;
            ThemePanel.Visibility = theme ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>The library panel stays as it was left, for every design opened in this session.</summary>
        private static bool _libraryOpen = true;

        private void LibraryToggle_Click(object sender, RoutedEventArgs e) => ShowLibrary(LibraryToggle.IsChecked == true);

        private void LibraryHide_Click(object sender, RoutedEventArgs e) => ShowLibrary(false);

        private void ShowLibrary(bool open)
        {
            _libraryOpen = open;
            LibraryToggle.IsChecked = open;
            LibraryPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            LibraryColumn.Width = new GridLength(open ? 272 : 0);
        }

        private void GroupLayers_Click(object sender, RoutedEventArgs e) => GroupSelection();

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

            // Your own modules come first once there are some; until then their hint waits at the end.
            bool haveModules = Modules().Count > 0;
            if (haveModules) AddModulesSection(host, filter);

            // Live data and blocks sit in folders; a search opens every folder with a match.
            List<DesignBlock> quick = QuickValues.All().Where(b => Match(b.Name, b.Description) || Match(b.Folder, null)).ToList();
            if (quick.Count > 0)
            {
                host.Children.Add(SectionTitle(QuickValues.Section));
                foreach (IGrouping<string, DesignBlock> folder in quick.GroupBy(b => b.Folder).OrderBy(f => Array.IndexOf(QuickValues.Folders, f.Key)))
                    host.Children.Add(BlockFolder(folder.Key, folder.ToList(), filter.Length > 0, QuickValues.Section));
            }
            List<DesignBlock> blocks = DesignBlocks.BuiltIn().Where(b => Match(b.Name, b.Description) || Match(b.Folder, null)).ToList();
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
            if (!haveModules) AddModulesSection(host, filter);
            if (host.Children.Count == 0)
                host.Children.Add(new TextBlock
                {
                    Text = "Nothing in the Library matches \"" + filter + "\". Try a shorter word, such as fuel, gap or rpm.",
                    Style = (Style)FindResource("T.Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 12, 4, 0)
                });
        }

        /// <summary>A widget in the library: a compact row, its icon beside its name, so the whole list scrolls half as far.</summary>
        private Border WidgetTile(WidgetType type)
        {
            var row = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
            var icon = new TextBlock { Text = type.Icon, Style = (Style)FindResource("T.Icon"), FontSize = 14, Width = 18, Foreground = (Brush)FindResource("Brush.Accent"), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);
            row.Children.Add(new TextBlock { Text = type.Name, FontSize = 12, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            var tile = new Border
            {
                Height = 34, Margin = new Thickness(2), Padding = new Thickness(9, 0, 6, 0), CornerRadius = new CornerRadius(7), Cursor = Cursors.Hand,
                Background = (Brush)FindResource("Brush.Surface"), BorderBrush = (Brush)FindResource("Brush.Border"), BorderThickness = new Thickness(1),
                Child = row, ToolTip = type.Name + "\n" + type.Description + "\n\nClick to add, or drag onto the screen."
            };
            HookTile(tile, () => AddWidgetAtCenter(type.Id), () => new DataObject("afo-widget", type.Id));
            return tile;
        }

        private static readonly string[] Folders = { "Rev gauges", "Cards", "Warnings and spotter" };
        private static readonly HashSet<string> OpenFolders = new HashSet<string>(), ClosedFolders = new HashSet<string>();

        private static int FolderOrder(string folder)
        {
            int i = Array.IndexOf(Folders, folder);
            return i < 0 ? Folders.Length : i;
        }

        private StackPanel BlockFolder(string name, List<DesignBlock> blocks, bool searching, string section) =>
            LibraryFolder(name, blocks.Count, searching, section, false, content => { foreach (DesignBlock block in blocks) content.Children.Add(BlockRow(block)); });

        /// <summary>
        /// A folder in the Library that opens and closes with a click and remembers it while the designer runs. What's
        /// in it is only built the first time it opens.
        /// </summary>
        private StackPanel LibraryFolder(string name, int items, bool searching, string section, bool openAtFirst, Action<StackPanel> fill)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
            string key = section + "/" + name;
            bool open = searching || (openAtFirst ? !ClosedFolders.Contains(key) : OpenFolders.Contains(key));
            var content = new StackPanel { Visibility = open ? Visibility.Visible : Visibility.Collapsed, Margin = new Thickness(10, 0, 0, 4) };
            void Fill() { if (content.Children.Count == 0) fill(content); }
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
            var count = new TextBlock { Text = items.ToString(), Style = (Style)FindResource("T.Caption"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(count, 2);
            header.Children.Add(count);
            var row = new Border
            {
                Margin = new Thickness(0, 2, 0, 2), Padding = new Thickness(2, 6, 10, 6), CornerRadius = new CornerRadius(8), Cursor = Cursors.Hand,
                Background = (Brush)FindResource("Brush.Surface"), BorderBrush = (Brush)FindResource("Brush.Border"), BorderThickness = new Thickness(1), Child = header
            };
            row.MouseEnter += (s, e) => row.Background = (Brush)FindResource("Brush.SurfaceHover");
            row.MouseLeave += (s, e) => row.Background = (Brush)FindResource("Brush.Surface");
            row.MouseLeftButtonUp += (s, e) =>
            {
                bool nowOpen = content.Visibility != Visibility.Visible;
                if (nowOpen) Fill();
                content.Visibility = nowOpen ? Visibility.Visible : Visibility.Collapsed;
                if (nowOpen) Motion.Stagger(content.Children, 22, 180);
                chevron.Text = nowOpen ? "" : "";
                if (searching) return;
                if (nowOpen) { OpenFolders.Add(key); ClosedFolders.Remove(key); }
                else { OpenFolders.Remove(key); ClosedFolders.Add(key); }
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
            text.Children.Add(new TextBlock { Text = block.Description, Style = (Style)FindResource("T.Caption"), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap });
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            var row = new Border
            {
                Margin = new Thickness(0, 2, 0, 2), Padding = new Thickness(4, 5, 8, 5), CornerRadius = new CornerRadius(8), Cursor = Cursors.Hand,
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
            if (LayersList == null) return;
            LayersList.Children.Clear();
            _layerThumbs.Clear();
            int count = _screen.Widgets.Count;
            LayerCount.Text = count == 0 ? "" : count + (count == 1 ? " layer" : " layers");
            if (count == 0)
            {
                LayersList.Children.Add(new TextBlock { Text = "This screen is empty. Drag something in from the Library, or draw with the tools on the left.", Style = (Style)FindResource("T.Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 8, 8, 0) });
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
                label.Children.Add(new TextBlock { Text = preset.Name, FontSize = 12, Foreground = new SolidColorBrush(ToMedia(preset.Colors[7])) });
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
                Text = "On: a new flag fills the screen for a few seconds, then hands it back. Red and checkered stay until they end. Off: show flags with a Flag widget, or react with FlagActive in a formula."
            });
            var waving = new CheckBox { Style = (Style)FindResource("Switch"), Content = "Wave in a soft breeze", IsChecked = _layout.WavingFlags, Margin = new Thickness(0, 10, 0, 0) };
            waving.Click += (s, e) => { _layout.WavingFlags = waving.IsChecked == true; Commit("Flags"); RequestRender(); };
            ThemeEditor.Children.Add(waving);
            var frame = new CheckBox { Style = (Style)FindResource("Switch"), Content = "Frame the dashboard while a flag is out", IsChecked = _layout.FlagFrame, Margin = new Thickness(0, 10, 0, 0) };
            frame.Click += (s, e) => { _layout.FlagFrame = frame.IsChecked == true; Commit("Flags"); RequestRender(); };
            ThemeEditor.Children.Add(frame);
            ThemeEditor.Children.Add(new TextBlock
            {
                Style = (Style)FindResource("T.Caption"), Margin = new Thickness(0, 6, 0, 0),
                Text = "A glow in the flag's color runs round the edge, with a tab at the top: during a caution, on the final lap, and while a black, repair or blue flag is out."
            });
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
