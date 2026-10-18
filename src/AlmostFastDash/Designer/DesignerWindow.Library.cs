using System;
using System.Collections.Generic;
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
    }
}
