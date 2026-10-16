using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace AlmostFastOverlays
{
    /// <summary>
    /// A module: widgets someone built (a rev gauge, a card, a strip) kept to reuse and share as one .afomodule
    /// file, with the images they show, a picture of them for the Library, and who made them. A module is data
    /// only, widgets and formulas that can't reach outside the dashboard, so it's safe to pass around.
    /// </summary>
    internal sealed class DesignModule
    {
        public const string FileFormat = "AlmostFastDash.Module";
        public const string Extension = ".afomodule";
        /// <summary>A module this big would be photos, not widgets; refusing it keeps imports quick.</summary>
        private const long MaxFileBytes = 16L * 1024 * 1024;
        /// <summary>The thumbnail's largest size: twice what the Library shows, so it stays sharp on a high-DPI display.</summary>
        private const int ThumbWidth = 320, ThumbHeight = 180;

        public static readonly string Folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Modules");
        /// <summary>Where blocks were saved before modules: still listed, so nothing saved is lost.</summary>
        public static readonly string OldBlocksFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Blocks");

        public string Format { get; set; } = FileFormat;
        public int Version { get; set; } = 1;
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Author { get; set; } = "";
        public string Category { get; set; } = "";
        /// <summary>When it was saved, as yyyy-MM-dd.</summary>
        public string Saved { get; set; } = "";
        /// <summary>The screen size it was built on; placed on a screen of another size, it's scaled to match.</summary>
        public int ScreenWidth { get; set; } = ScreenSizes.DefaultWidth;
        public int ScreenHeight { get; set; } = ScreenSizes.DefaultHeight;
        /// <summary>The widgets, bottom layer first, placed from 0,0.</summary>
        public List<DashWidget> Widgets { get; set; } = new List<DashWidget>();
        /// <summary>The images its widgets show: base64 PNG by id, as in a design.</summary>
        public Dictionary<string, string> Images { get; set; } = new Dictionary<string, string>();
        /// <summary>A picture of it for the Library: base64 PNG.</summary>
        public string Thumbnail { get; set; } = "";

        /// <summary>The file it was read from.</summary>
        [ScriptIgnore] public string FilePath;
        /// <summary>Saved as a block by an earlier version (a list of widgets, no picture or details).</summary>
        [ScriptIgnore] public bool OldBlock;

        public SizeF Size()
        {
            if (Widgets.Count == 0) return SizeF.Empty;
            return new SizeF((float)Widgets.Max(w => w.X + w.W), (float)Widgets.Max(w => w.Y + w.H));
        }

        // ------------------------------------------------------------------ making one

        /// <summary>
        /// A module of <paramref name="widgets"/> (in screen order) from <paramref name="design"/>: copies moved to
        /// 0,0, unlocked, with the images they show and a thumbnail drawn in the design's theme.
        /// </summary>
        public static DesignModule From(string name, IEnumerable<DashWidget> widgets, DashLayout design, string accent, UnitSystem units)
        {
            List<DashWidget> copies = widgets.Select(SettingsStore.Clone).ToList();
            if (copies.Count == 0) throw new ArgumentException("A module needs at least one widget.");
            double left = copies.Min(w => w.X), top = copies.Min(w => w.Y);
            foreach (DashWidget w in copies)
            {
                w.X -= left;
                w.Y -= top;
                w.Locked = false;
            }
            // The bottom layer's own base stays behind, so it can't clip to anything in the module.
            if (Expr.Bool(copies[0].Get("clip", "false"))) copies[0].Set("clip", "false");
            var module = new DesignModule
            {
                Name = name.Trim(), Saved = DateTime.Now.ToString("yyyy-MM-dd"), Widgets = copies,
                ScreenWidth = design.Width, ScreenHeight = design.Height
            };
            foreach (string value in copies.SelectMany(w => w.Props.Values))
                if (value != null && design.Images.TryGetValue(value, out string image)) module.Images[value] = image;
            module.Thumbnail = RenderThumbnail(module, design.Theme, accent, units);
            return module;
        }

        /// <summary>Draws the module on its theme's background and returns it as a base64 PNG, scaled to fit the thumbnail size.</summary>
        public static string RenderThumbnail(DesignModule module, LayoutTheme theme, string accent, UnitSystem units)
        {
            SizeF size = module.Size();
            int width = Math.Max(8, (int)Math.Ceiling(size.Width)), height = Math.Max(8, (int)Math.Ceiling(size.Height));
            var design = new DashLayout
            {
                Width = width, Height = height, Theme = SettingsStore.Clone(theme ?? new LayoutTheme()), Images = module.Images,
                FullScreenFlags = false, FlagFrame = false, WavingFlags = false
            };
            // A see-through theme draws the module over the app's own dark background, as the Library shows it.
            if (design.Theme.BackgroundStyle == "None") design.Theme.BackgroundStyle = "Solid";
            design.Screens.Add(new DashScreen { Name = "Module", Widgets = module.Widgets.Select(SettingsStore.Clone).ToList() });
            var settings = new DashboardSettings { Layout = Layouts.Custom, Design = design, AccentColor = accent ?? "#2DD4BF" };
            using (var renderer = new LayoutRenderer(width, height, settings, units ?? UnitSystem.Default))
            {
                Bitmap frame = renderer.Render(IRacingTelemetrySource.CreatePreviewSnapshot(), false, 4, 1, design.Screens[0]);
                double scale = Math.Min(1, Math.Min(ThumbWidth / (double)width, ThumbHeight / (double)height));
                int w = Math.Max(1, (int)Math.Round(width * scale)), h = Math.Max(1, (int)Math.Round(height * scale));
                using (var thumb = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(thumb))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.DrawImage(frame, new Rectangle(0, 0, w, h), 0, 0, width, height, GraphicsUnit.Pixel);
                    }
                    using (var stream = new MemoryStream())
                    {
                        thumb.Save(stream, ImageFormat.Png);
                        return Convert.ToBase64String(stream.ToArray());
                    }
                }
            }
        }

        // ------------------------------------------------------------------ placing one

        /// <summary>
        /// Fresh copies of the widgets to place on <paramref name="design"/>: new ids, groups of their own, scaled to
        /// the design's screen size (evenly, so round gauges stay round), and their images added to the design.
        /// A module of several loose widgets comes in as one group named after it.
        /// </summary>
        public List<DashWidget> Place(DashLayout design)
        {
            List<DashWidget> widgets = Widgets.Select(SettingsStore.Clone).ToList();
            var groups = new Dictionary<string, string>();
            foreach (DashWidget w in widgets)
            {
                w.Id = DashScreen.NewId("w");
                w.Normalize();
                if (string.IsNullOrEmpty(w.Group)) continue;
                if (!groups.TryGetValue(w.Group, out string id)) groups[w.Group] = id = DashScreen.NewId("g");
                w.Group = id;
            }
            if (widgets.Count > 1 && groups.Count == 0)
            {
                string id = DashScreen.NewId("g");
                foreach (DashWidget w in widgets) { w.Group = id; w.GroupName = Name; w.Slot = 0; }
            }
            double scale = Math.Min(design.Width / (double)Math.Max(1, ScreenWidth), design.Height / (double)Math.Max(1, ScreenHeight));
            if (Math.Abs(scale - 1) > 0.01) DashLayout.ScaleWidgets(widgets, scale, scale);

            // Images: one already in the design under the same id is reused; a different one under that id gets a new id.
            var renamed = new Dictionary<string, string>();
            foreach (KeyValuePair<string, string> image in Images ?? new Dictionary<string, string>())
            {
                string id = image.Key;
                if (design.Images.TryGetValue(id, out string existing) && existing != image.Value)
                {
                    id = "img-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                    renamed[image.Key] = id;
                }
                design.Images[id] = image.Value;
            }
            if (renamed.Count > 0)
                foreach (DashWidget w in widgets)
                    foreach (string key in w.Props.Keys.ToList())
                        if (w.Props[key] != null && renamed.TryGetValue(w.Props[key], out string id)) w.Props[key] = id;
            return widgets;
        }

        // ------------------------------------------------------------------ files

        public static void Write(string path, DesignModule module) => File.WriteAllText(path, SettingsStore.ToJson(module));

        public static DesignModule Read(string path)
        {
            var info = new FileInfo(path);
            if (info.Length > MaxFileBytes) throw new InvalidDataException("This file is too big to be a module.");
            DesignModule module;
            try
            {
                module = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 64 }.Deserialize<DesignModule>(File.ReadAllText(path));
            }
            catch (Exception exception) when (!(exception is IOException))
            {
                throw new InvalidDataException("This isn't an Almost Fast Dash module.");
            }
            if (module == null || module.Format != FileFormat || module.Widgets == null || module.Widgets.Count == 0)
                throw new InvalidDataException("This isn't an Almost Fast Dash module.");
            module.Widgets.RemoveAll(w => w == null);
            foreach (DashWidget w in module.Widgets) w.Normalize();
            if (module.Images == null) module.Images = new Dictionary<string, string>();
            if (string.IsNullOrWhiteSpace(module.Name)) module.Name = Path.GetFileNameWithoutExtension(path);
            module.Description = module.Description ?? "";
            module.Author = module.Author ?? "";
            module.Category = module.Category ?? "";
            module.Thumbnail = module.Thumbnail ?? "";
            module.FilePath = path;
            return module;
        }

        /// <summary>A block saved by an earlier version: a list of widgets.</summary>
        private static DesignModule ReadOldBlock(string path)
        {
            var widgets = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<List<DashWidget>>(File.ReadAllText(path));
            if (widgets == null || widgets.Count == 0) return null;
            foreach (DashWidget w in widgets) w.Normalize();
            return new DesignModule
            {
                Name = Path.GetFileNameWithoutExtension(path), Description = "Saved as a block in an earlier version.", Widgets = widgets,
                FilePath = path, OldBlock = true
            };
        }

        /// <summary>Every module in the Modules folder, and blocks saved by earlier versions, by category then name.</summary>
        public static List<DesignModule> Library()
        {
            var modules = new List<DesignModule>();
            if (Directory.Exists(Folder))
                foreach (string path in Directory.GetFiles(Folder, "*" + Extension))
                    try { modules.Add(Read(path)); } catch { }
            if (Directory.Exists(OldBlocksFolder))
                foreach (string path in Directory.GetFiles(OldBlocksFolder, "*.afoblock"))
                    try { DesignModule old = ReadOldBlock(path); if (old != null) modules.Add(old); } catch { }
            return modules.OrderBy(m => m.Category ?? "", StringComparer.OrdinalIgnoreCase).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>The library file for a module of this name.</summary>
        public static string PathFor(string name)
        {
            string file = DesignerWindow.SafeFileName((name ?? "").Trim()).Trim().TrimEnd('.');
            return Path.Combine(Folder, (file.Length == 0 ? "Module" : file) + Extension);
        }

        /// <summary>Saves a module into the library (replacing one of the same name) and returns its file.</summary>
        public static string Save(DesignModule module)
        {
            Directory.CreateDirectory(Folder);
            string path = PathFor(module.Name);
            Write(path, module);
            module.FilePath = path;
            return path;
        }

        /// <summary>
        /// Adds a shared module file to the library and returns it. The same module already there is reused;
        /// another with the same name is kept, and this one is numbered beside it.
        /// </summary>
        public static DesignModule Import(string source)
        {
            DesignModule module = Read(source);
            Directory.CreateDirectory(Folder);
            string json = SettingsStore.ToJson(module), name = module.Name, path = PathFor(name);
            for (int n = 2; File.Exists(path); n++)
            {
                try
                {
                    // The same module, perhaps numbered when it came in before.
                    DesignModule there = Read(path);
                    there.Name = module.Name;
                    if (SettingsStore.ToJson(there) == json) return Read(path);
                }
                catch { }
                name = module.Name + " (" + n + ")";
                path = PathFor(name);
            }
            module.Name = name;
            Write(path, module);
            module.FilePath = path;
            return module;
        }
    }
}
