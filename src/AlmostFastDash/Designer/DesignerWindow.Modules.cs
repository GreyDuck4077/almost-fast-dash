using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AlmostFastDash.Designer
{
    public partial class DesignerWindow
    {
        // ==================================================================
        // Modules: your own widgets kept to reuse and share, and tools for restyling what's placed
        // ==================================================================

        private static readonly string[] SuggestedCategories = { "Rev gauges", "Cards", "Gauges", "Strips", "Warnings", "Backgrounds" };
        private static readonly Dictionary<string, ImageSource> ModulePictures = new Dictionary<string, ImageSource>();
        /// <summary>The library's modules, read when the Library is first built and after each change.</summary>
        private List<DesignModule> _modules;

        private List<DesignModule> Modules()
        {
            if (_modules == null)
            {
                try { _modules = DesignModule.Library(); }
                catch { _modules = new List<DesignModule>(); }
            }
            return _modules;
        }

        private void ModulesChanged()
        {
            _modules = null;
            BuildToolbox();
        }

        /// <summary>The Library's "My modules": a picture of each, grouped by category when there's more than one.</summary>
        private void AddModulesSection(StackPanel host, string filter)
        {
            List<DesignModule> all = Modules();
            List<DesignModule> shown = all.Where(m => filter.Length == 0 ||
                new[] { m.Name, m.Description, m.Author, m.Category }.Any(t => (t ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            if (filter.Length > 0 && shown.Count == 0) return;

            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(SectionTitle("My modules"));
            var import = new Button
            {
                Style = (Style)FindResource("Btn.Ghost"), Content = "Import…", Height = 26, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(0, 8, 0, 4),
                VerticalAlignment = VerticalAlignment.Bottom, ToolTip = "Import shared modules (.afomodule), or drop them on the screen."
            };
            import.Click += (s, e) => ImportModules();
            Grid.SetColumn(import, 1);
            header.Children.Add(import);
            host.Children.Add(header);
            if (all.Count == 0)
            {
                host.Children.Add(new TextBlock
                {
                    Text = "Select widgets and press Ctrl+M to save them here as a module.",
                    Style = (Style)FindResource("T.Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 4, 6)
                });
                return;
            }
            void Cards(Panel into, IEnumerable<DesignModule> modules)
            {
                var grid = new UniformGrid { Columns = 2 };
                foreach (DesignModule module in modules) grid.Children.Add(ModuleCard(module));
                into.Children.Add(grid);
            }
            // One category shows its pictures straight away; several get folders, open to start with.
            var categories = shown.GroupBy(m => (m.Category ?? "").Trim(), StringComparer.OrdinalIgnoreCase).OrderBy(c => c.Key.Length == 0 ? 1 : 0).ToList();
            if (categories.Count == 1) Cards(host, shown);
            else
                foreach (IGrouping<string, DesignModule> category in categories)
                    host.Children.Add(LibraryFolder(category.Key.Length == 0 ? "Other" : category.Key, category.Count(), filter.Length > 0, "My modules", true,
                        content => Cards(content, category)));
        }

        private Border ModuleCard(DesignModule module)
        {
            var stack = new StackPanel();
            stack.Children.Add(new Border
            {
                Height = 62, CornerRadius = new CornerRadius(6), Background = (Brush)FindResource("Brush.Bg"), ClipToBounds = true,
                Child = new Image { Source = ModulePicture(module), Stretch = Stretch.Uniform, Margin = new Thickness(3) }
            });
            stack.Children.Add(new TextBlock { Text = module.Name, FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 5, 2, 0), TextTrimming = TextTrimming.CharacterEllipsis });
            string by = string.IsNullOrWhiteSpace(module.Author) ? "" : "By " + module.Author.Trim();
            var card = new Border
            {
                Margin = new Thickness(2), Padding = new Thickness(5, 5, 5, 6), CornerRadius = new CornerRadius(8), Cursor = Cursors.Hand,
                Background = (Brush)FindResource("Brush.Surface"), BorderBrush = (Brush)FindResource("Brush.Border"), BorderThickness = new Thickness(1), Child = stack,
                ToolTip = module.Name + (module.Description.Length > 0 ? "\n" + module.Description : "") + (by.Length > 0 ? "\n" + by : "") +
                    "\n\nClick or drag to add. Right-click for more."
            };
            HookTile(card, () => PlaceModule(module, null, false), () => new DataObject("afo-module", module));
            var menu = new ContextMenu();
            menu.Items.Add(MenuItem("Add to the screen", null, () => PlaceModule(module, null, false)));
            menu.Items.Add(MenuItem("Export to a file to share…", null, () => ExportModule(module)));
            menu.Items.Add(MenuItem("Edit details…", null, () => EditModuleDetails(module)));
            menu.Items.Add(MenuItem("Show the file", null, () =>
            {
                try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + module.FilePath + "\""); } catch { }
            }));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItem("Delete…", null, () => DeleteModule(module)));
            card.ContextMenu = menu;
            return card;
        }

        /// <summary>The module's picture; one saved without (an old block) is drawn now in this design's theme.</summary>
        private ImageSource ModulePicture(DesignModule module)
        {
            if (string.IsNullOrEmpty(module.Thumbnail))
            {
                try { module.Thumbnail = DesignModule.RenderThumbnail(module, _layout.Theme, _dashboard.AccentColor, _units); }
                catch { return null; }
            }
            if (ModulePictures.TryGetValue(module.Thumbnail, out ImageSource known)) return known;
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.StreamSource = new MemoryStream(Convert.FromBase64String(module.Thumbnail));
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                image.Freeze();
                if (ModulePictures.Count > 200) ModulePictures.Clear();
                return ModulePictures[module.Thumbnail] = image;
            }
            catch { return null; }
        }

        /// <summary>
        /// Puts a module on the screen, centred on <paramref name="at"/> (or the screen), selected as one group so it
        /// can be moved, scaled and recolored at once.
        /// </summary>
        private void PlaceModule(DesignModule module, Point? at, bool askPanel)
        {
            List<DashWidget> widgets;
            try { widgets = module.Place(_layout); }
            catch (Exception exception)
            {
                StatusText.Text = "Could not add the module: " + exception.Message;
                return;
            }
            Rect bounds = Union(widgets);
            Point centre = at ?? new Point(CanvasW / 2, CanvasH / 2);
            foreach (DashWidget w in widgets)
            {
                w.X += Math.Round(centre.X - bounds.Width / 2 - bounds.X);
                w.Y += Math.Round(centre.Y - bounds.Height / 2 - bounds.Y);
            }
            _rendererDirty = true;
            string label = "Add " + module.Name;
            DashWidget panel = askPanel && at.HasValue ? PanelAt(at.Value) : null;
            if (panel != null && !widgets.Any(w => w.Type == "panel")) AskPanelDrop(panel, widgets, label);
            else AddWidgets(widgets, label);
            StatusText.Text = "Added " + module.Name + "." + (widgets.Count > 1 ? " Restyle its colors in Properties." : "");
        }

        // ------------------------------------------------------------------ saving one

        private void SaveSelectionAsModule()
        {
            if (_selection.Count == 0) { StatusText.Text = "Select the widgets to keep as a module first."; return; }
            List<DashWidget> widgets = _screen.Widgets.Where(_selection.Contains).ToList();
            string suggested = SelectionIsOneGroup(out _) && !string.IsNullOrWhiteSpace(widgets[0].GroupName) ? widgets[0].GroupName
                : widgets.Count == 1 && !string.IsNullOrWhiteSpace(widgets[0].Name) ? widgets[0].Name : "My module";
            DesignModule module;
            try { module = DesignModule.From(suggested, widgets, _layout, _dashboard.AccentColor, _units); }
            catch (Exception exception)
            {
                StatusText.Text = "Could not make a module: " + exception.Message;
                return;
            }
            // The last name given as the maker, so it only needs typing once.
            DesignModule latest = Modules().Where(m => !string.IsNullOrWhiteSpace(m.Author)).OrderByDescending(m => m.Saved).FirstOrDefault();
            module.Author = latest?.Author ?? "";
            if (!ModuleDialog(module, "Save as module", true, out bool exportToo)) return;
            string path = DesignModule.PathFor(module.Name);
            if (File.Exists(path) && MessageBox.Show(this, "You already have a module called \"" + module.Name + "\". Replace it?", "Save as module",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            try
            {
                DesignModule.Save(module);
                ModulesChanged();
                StatusText.Text = "Saved \"" + module.Name + "\". It's in the Library under My modules.";
                if (exportToo) ExportModule(module);
            }
            catch (Exception exception)
            {
                StatusText.Text = "Could not save the module: " + exception.Message;
            }
        }

        /// <summary>The module's name, category, description and maker, beside its picture. False when cancelled.</summary>
        private bool ModuleDialog(DesignModule module, string title, bool saving, out bool exportToo)
        {
            exportToo = false;
            var body = new Grid { Margin = new Thickness(22) };
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            body.ColumnDefinitions.Add(new ColumnDefinition());
            var picture = new StackPanel();
            picture.Children.Add(new Border
            {
                Background = (Brush)FindResource("Brush.Bg"), CornerRadius = new CornerRadius(10), Padding = new Thickness(8), Height = 150,
                BorderBrush = (Brush)FindResource("Brush.Border"), BorderThickness = new Thickness(1),
                Child = new Image { Source = ModulePicture(module), Stretch = Stretch.Uniform }
            });
            System.Drawing.SizeF size = module.Size();
            picture.Children.Add(Caption(module.Widgets.Count + (module.Widgets.Count == 1 ? " widget" : " widgets") + ", " + Math.Round(size.Width) + " × " + Math.Round(size.Height) +
                (module.Images.Count > 0 ? ", " + module.Images.Count + (module.Images.Count == 1 ? " image" : " images") : "") + ".", 8));
            picture.Children.Add(Caption("Modules hold only widgets and formulas, so they're safe to share.", 8));
            body.Children.Add(picture);

            var fields = new StackPanel();
            Grid.SetColumn(fields, 2);
            body.Children.Add(fields);
            TextBlock Label(string text, double top = 12) => new TextBlock { Text = text, Style = (Style)FindResource("T.Secondary"), Margin = new Thickness(0, top, 0, 4) };
            var name = new TextBox { Text = module.Name, MaxLength = 60 };
            fields.Children.Add(Label("Name", 0));
            fields.Children.Add(name);
            var category = new TextBox { Text = module.Category, MaxLength = 40 };
            fields.Children.Add(Label("Category"));
            fields.Children.Add(category);
            var chips = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            foreach (string suggestion in SuggestedCategories.Concat(Modules().Select(m => (m.Category ?? "").Trim())).Where(c => c.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(12))
            {
                var chip = new Button { Content = suggestion, Style = (Style)FindResource("Btn.Ghost"), Height = 24, FontSize = 12, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(0, 0, 4, 4) };
                chip.Click += (s, e) => category.Text = suggestion;
                chips.Children.Add(chip);
            }
            fields.Children.Add(chips);
            var description = new TextBox { Text = module.Description, MaxLength = 400, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 62, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            fields.Children.Add(Label("What it shows"));
            fields.Children.Add(description);
            var author = new TextBox { Text = module.Author, MaxLength = 60 };
            fields.Children.Add(Label("Made by (optional)"));
            fields.Children.Add(author);
            CheckBox export = null;
            if (saving)
            {
                export = new CheckBox { Style = (Style)FindResource("Switch"), VerticalAlignment = VerticalAlignment.Center };
                var exportRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
                exportRow.Children.Add(export);
                exportRow.Children.Add(new TextBlock { Text = "Also export a file to share", Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
                fields.Children.Add(exportRow);
            }
            var ok = new Button { Content = saving ? "Save module" : "Save", Style = (Style)FindResource("Btn.Primary"), IsDefault = true, MinWidth = 110, Margin = new Thickness(8, 0, 0, 0) };
            var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90 };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            fields.Children.Add(buttons);

            var dialog = new Window
            {
                Title = title, Owner = this, Width = 660, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = (Brush)FindResource("Brush.Surface"),
                Foreground = (Brush)FindResource("Brush.Text"), FontFamily = (FontFamily)FindResource("Font.Ui"), Content = body
            };
            ok.Click += (s, e) =>
            {
                if (name.Text.Trim().Length == 0) { name.Focus(); return; }
                dialog.DialogResult = true;
            };
            dialog.Loaded += (s, e) => { name.Focus(); name.SelectAll(); };
            if (dialog.ShowDialog() != true) return false;
            module.Name = name.Text.Trim();
            module.Category = category.Text.Trim();
            module.Description = description.Text.Trim();
            module.Author = author.Text.Trim();
            exportToo = export?.IsChecked == true;
            return true;
        }

        private void EditModuleDetails(DesignModule module)
        {
            string oldPath = module.FilePath;
            if (!ModuleDialog(module, "Module details", false, out _)) return;
            string path = DesignModule.PathFor(module.Name);
            bool moved = !string.Equals(Path.GetFullPath(path), Path.GetFullPath(oldPath ?? path), StringComparison.OrdinalIgnoreCase);
            if (moved && File.Exists(path) && MessageBox.Show(this, "You already have a module called \"" + module.Name + "\". Replace it?", "Module details",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            {
                ModulesChanged();
                return;
            }
            try
            {
                if (string.IsNullOrEmpty(module.Saved)) module.Saved = DateTime.Now.ToString("yyyy-MM-dd");
                DesignModule.Save(module);
                // Renamed, or a block from an earlier version now saved as a module: the old file goes to the Recycle Bin.
                if (moved && oldPath != null && File.Exists(oldPath)) Recycle(oldPath);
                ModulesChanged();
                StatusText.Text = "Saved the details of \"" + module.Name + "\".";
            }
            catch (Exception exception)
            {
                StatusText.Text = "Could not save the module: " + exception.Message;
                ModulesChanged();
            }
        }

        private void DeleteModule(DesignModule module)
        {
            if (MessageBox.Show(this, "Delete \"" + module.Name + "\" from your modules? It goes to the Recycle Bin, so you can bring it back.", "Delete module",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            try
            {
                Recycle(module.FilePath);
                ModulesChanged();
                StatusText.Text = "Deleted \"" + module.Name + "\". It's in the Recycle Bin.";
            }
            catch (Exception exception)
            {
                StatusText.Text = "Could not delete the module: " + exception.Message;
            }
        }

        private static void Recycle(string path) =>
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);

        // ------------------------------------------------------------------ sharing

        private void ExportModule(DesignModule module)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export module", Filter = "Almost Fast Dash module (*" + DesignModule.Extension + ")|*" + DesignModule.Extension,
                FileName = SafeFileName(module.Name) + DesignModule.Extension
            };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                ModulePicture(module);
                DesignModule.Write(dialog.FileName, module);
                StatusText.Text = "Exported \"" + module.Name + "\". Anyone can import it into their Library.";
            }
            catch (Exception exception)
            {
                StatusText.Text = "Could not export the module: " + exception.Message;
            }
        }

        private void ImportModules()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import modules", Multiselect = true,
                Filter = "Almost Fast Dash module (*" + DesignModule.Extension + ")|*" + DesignModule.Extension + "|All files|*.*"
            };
            if (dialog.ShowDialog(this) == true) ImportModuleFiles(dialog.FileNames, null);
        }

        /// <summary>Adds shared module files to the Library, and places them on the screen when they were dropped there.</summary>
        private void ImportModuleFiles(IEnumerable<string> paths, Point? placeAt)
        {
            var added = new List<DesignModule>();
            var problems = new List<string>();
            foreach (string path in paths)
            {
                try { added.Add(DesignModule.Import(path)); }
                catch (Exception exception) { problems.Add(Path.GetFileName(path) + " (" + exception.Message.TrimEnd('.') + ")"); }
            }
            ModulesChanged();
            if (added.Count > 0 && !_libraryOpen) ShowLibrary(true);
            if (placeAt.HasValue) foreach (DesignModule module in added) PlaceModule(module, placeAt, false);
            string message = added.Count == 0 ? "" : "Imported " + string.Join(", ", added.Select(m => "\"" + m.Name + "\"")) + (placeAt.HasValue ? "." : ". Find it in the Library under My modules.");
            if (problems.Count > 0) message += (message.Length > 0 ? " " : "") + "Couldn't import " + string.Join(", ", problems) + ".";
            StatusText.Text = message;
        }

        /// <summary>The module files in something dragged in from Explorer.</summary>
        private static List<string> ModuleFiles(IDataObject data) =>
            data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files
                ? files.Where(f => f.EndsWith(DesignModule.Extension, StringComparison.OrdinalIgnoreCase)).ToList()
                : new List<string>();

        // ------------------------------------------------------------------ restyling what's placed

        /// <summary>
        /// Every color the selected widgets use, one swatch each: changing it changes every widget that uses it,
        /// so a placed module (or any group) is recolored in one go. Colors behind a formula aren't listed.
        /// </summary>
        private void AddSelectionColors(IList<DashWidget> widgets)
        {
            var uses = new Dictionary<string, List<(DashWidget Widget, string Key, string Label)>>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();
            foreach (DashWidget w in widgets)
            {
                WidgetType type = WidgetCatalog.Find(w.Type);
                if (type == null) continue;
                // A line with no width shows no color.
                bool noLine = type.Prop("strokeWidth") is PropDef width && double.TryParse(w.Get("strokeWidth", width.Default), NumberStyles.Float, CultureInfo.InvariantCulture, out double lineWidth) && lineWidth <= 0;
                foreach (PropDef def in type.Props.Where(p => p.Kind == PropKind.Color && !p.Hidden && p.Applies(w, type)))
                {
                    if (noLine && def.Key.StartsWith("stroke")) continue;
                    string raw = (w.Get(def.Key, def.Default) ?? "").Trim();
                    if (raw.Length == 0 || raw.StartsWith("=")) continue;
                    string key = raw.StartsWith("#") ? raw.ToUpperInvariant() : raw.ToLowerInvariant();
                    if (!uses.TryGetValue(key, out var list)) { uses[key] = list = new List<(DashWidget, string, string)>(); order.Add(key); }
                    list.Add((w, def.Key, def.Label));
                }
            }
            if (order.Count == 0) return;
            Inspector.Children.Add(SectionTitle("Colors"));
            Inspector.Children.Add(Caption("Change a color here and it changes everywhere it's used.", 0));
            foreach (string color in order.OrderByDescending(c => uses[c].Count).Take(16))
            {
                var targets = uses[color];
                var field = new ColorField(this, color, _layout, () => _dashboard.AccentColor, true, false);
                field.Changed += value =>
                {
                    if (string.IsNullOrWhiteSpace(value)) return;
                    foreach (var target in targets) target.Widget.Set(target.Key, value);
                    RequestRender();
                    CommitSoon("Recolor");
                };
                List<string> labels = targets.Select(t => t.Label).Distinct().ToList();
                string label = string.Join(", ", labels.Take(2)) + (labels.Count > 2 ? "…" : "");
                Grid row = LabeledRow(label, field, targets.Count + (targets.Count == 1 ? " setting: " : " settings: ") + string.Join(", ", labels));
                Inspector.Children.Add(row);
            }
        }

        /// <summary>
        /// Scales the selection about its middle, text, lines, corners and glows included, as if it had been
        /// built at that size.
        /// </summary>
        private void ScaleSelection(double factor)
        {
            if (_selection.Count == 0 || factor <= 0 || Math.Abs(factor - 1) < 0.001) return;
            Rect before = Union(_selection);
            DashLayout.ScaleWidgets(_selection, factor, factor);
            Rect after = Union(_selection);
            double dx = Math.Round(before.X + before.Width / 2 - (after.X + after.Width / 2)), dy = Math.Round(before.Y + before.Height / 2 - (after.Y + after.Height / 2));
            foreach (DashWidget w in _selection) { w.X += dx; w.Y += dy; }
            _rendererDirty = true;
            Commit("Scale");
            SelectionChanged();
            RequestRender();
            StatusText.Text = "Scaled to " + Math.Round(factor * 100) + "%, text and lines included.";
        }

        /// <summary>A percentage box and a button that scales the selection, everything in it included.</summary>
        private FrameworkElement ScaleRow()
        {
            var box = new TextBox { Text = "100", FontFamily = (FontFamily)FindResource("Font.Mono"), FontSize = 12, Width = 56, VerticalContentAlignment = VerticalAlignment.Center };
            AttachWheel(box, 5, v => box.Text = Math.Max(5, v).ToString("0", CultureInfo.InvariantCulture));
            var apply = new Button { Content = "Scale", Height = 30, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(8, 0, 0, 0) };
            void Go()
            {
                if (!double.TryParse(box.Text.Trim().TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out double percent) || percent < 5 || percent > 1000)
                {
                    StatusText.Text = "Scale by a percentage from 5 to 1000.";
                    return;
                }
                ScaleSelection(percent / 100);
            }
            apply.Click += (s, e) => Go();
            box.KeyDown += (s, e) => { if (e.Key == Key.Enter) { Go(); e.Handled = true; } };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(box);
            row.Children.Add(new TextBlock { Text = "%", Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("Brush.TextSecondary") });
            row.Children.Add(apply);
            Grid labeled = LabeledRow("Scale", row, "Resizes the selection about its middle, text and lines included.");
            return labeled;
        }
    }
}
