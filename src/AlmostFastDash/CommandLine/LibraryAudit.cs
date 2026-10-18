using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Text;

namespace AlmostFastDash.CommandLine
{
    /// <summary>
    /// The Library as a newcomer meets it, for review: every widget as it first comes in, every quick value and
    /// block, on contact sheets by section, and a list of every widget's settings (--render-library).
    /// </summary>
    internal static class LibraryAudit
    {
        private const int Cell = 300, CellH = 210, Columns = 4;

        public static int Run(string directory)
        {
            Directory.CreateDirectory(directory);
            var units = new UnitSystem(SettingsStore.Load().General);
            LayoutTheme theme = DashboardSettings.CreateDesigned(DesignTemplates.Find("apex")).Design.Theme;

            foreach (string category in WidgetCatalog.Categories)
            {
                var items = WidgetCatalog.All.Where(t => t.Category == category)
                    .Select(t => (t.Name, (Func<List<DashWidget>>)(() => new List<DashWidget> { WidgetCatalog.Create(t.Id, 0, 0) })))
                        .ToList();
                Sheet(Path.Combine(directory, "Widgets - " + category + ".png"), category, items, theme, units);
            }
            foreach (IGrouping<string, DesignBlock> folder in QuickValues.All().GroupBy(b => b.Folder))
                Sheet(Path.Combine(directory, "Quick values - " + folder.Key + ".png"), "Quick values: " + folder.Key,
                    folder.Select(b => (b.Name, b.Create)).ToList(), theme, units);
            foreach (IGrouping<string, DesignBlock> folder in DesignBlocks.BuiltIn().GroupBy(b => b.Folder))
                Sheet(Path.Combine(directory, "Blocks - " + folder.Key + ".png"), "Blocks: " + folder.Key,
                    folder.Select(b => (b.Name, b.Create)).ToList(), theme, units);
            File.WriteAllText(Path.Combine(directory, "library.txt"), Inventory(), Encoding.UTF8);
            return 0;
        }

        private static void Sheet(string path, string title, List<(string Name, Func<List<DashWidget>> Create)> items, LayoutTheme theme,
            UnitSystem units)
        {
            int rows = (items.Count + Columns - 1) / Columns;
            using (var sheet = new Bitmap(Columns * Cell, 40 + rows * CellH, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(sheet))
            using (var titleFont = new Font("Segoe UI", 15, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var nameFont = new Font("Segoe UI", 12, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                g.Clear(Color.FromArgb(18, 20, 24));
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawString(title, titleFont, Brushes.White, 10, 10);
                for (int i = 0; i < items.Count; i++)
                {
                    int x = i % Columns * Cell, y = 40 + i / Columns * CellH;
                    string note = "";
                    try
                    {
                        List<DashWidget> widgets = items[i].Create();
                        double left = widgets.Min(w => w.X), top = widgets.Min(w => w.Y);
                        foreach (DashWidget w in widgets) { w.X -= left - 12; w.Y -= top - 12; }
                        int width = (int)Math.Ceiling(widgets.Max(w => w.X + w.W)) + 12,
                            height = (int)Math.Ceiling(widgets.Max(w => w.Y + w.H)) + 12;
                        var design = new DashLayout { Width = width, Height = height, Theme = SettingsStore.Clone(theme),
                            FlagFrame = false, FullScreenFlags = false };
                        design.Screens.Add(new DashScreen { Name = "Audit", Widgets = widgets });
                        var settings = new DashboardSettings { Layout = Layouts.Custom, Design = design };
                        using (var renderer = new LayoutRenderer(width, height, settings, units))
                        {
                            Bitmap frame = renderer.Render(IRacingTelemetrySource.CreatePreviewSnapshot(), false, 4, 1, design.Screens[0]);
                            double scale = Math.Min(1, Math.Min((Cell - 16) / (double)width, (CellH - 40) / (double)height));
                            int w = (int)(width * scale), h = (int)(height * scale);
                            g.DrawImage(frame, new Rectangle(x + (Cell - w) / 2, y + 6 + (CellH - 40 - h) / 2, w, h));
                            note = width - 24 + " × " + (height - 24) + (scale < 1 ? "  (" + Math.Round(scale * 100) + "%)" : "");
                            if (renderer.Errors.Count > 0) note += "  ⚠ " + renderer.Errors.Values.First();
                        }
                    }
                    catch (Exception exception) { note = "⚠ " + exception.Message; }
                    g.DrawString(items[i].Name, nameFont, Brushes.White, x + 8, y + CellH - 32);
                    using (var dim = new SolidBrush(Color.FromArgb(140, 150, 165)))
                        g.DrawString(note, nameFont, dim, x + 8, y + CellH - 17);
                }
                sheet.Save(path, ImageFormat.Png);
            }
        }

        /// <summary>Every widget's settings: label, kind, group, default and whether it says what it does.</summary>
        private static string Inventory()
        {
            var text = new StringBuilder();
            foreach (WidgetType type in WidgetCatalog.All.OrderBy(t => Array.IndexOf(WidgetCatalog.Categories, t.Category)))
            {
                text.AppendLine("== " + type.Category + " › " + type.Name + "  [" + type.Id + "]  " + type.DefaultW + "×" +
                    type.DefaultH);
                text.AppendLine("   " + type.Description);
                foreach (IGrouping<string, PropDef> group in type.Props.Where(p => !p.Hidden).GroupBy(p => p.Group))
                {
                    text.AppendLine("   -- " + group.Key);
                    foreach (PropDef p in group)
                        text.AppendLine("      " + p.Label.PadRight(28) + p.Kind.ToString().PadRight(8) + " = " + (p.Default ?? "")
                            .PadRight(14) +
                            (p.Choices != null ? " {" + string.Join("|", p.Choices) + "}" : "") +
                            (p.DependsOn != null ? "  when " + p.DependsOn : "") +
                            (string.IsNullOrWhiteSpace(p.Help) ? "" : "  ✓help") + "   [" + p.Key + "]");
                }
            }
            return text.ToString();
        }
    }
}
