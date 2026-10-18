using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AlmostFastDash.Designer
{
    public partial class DesignerWindow
    {
        /// <summary>Checks: groups, slots, style copying and editing together.</summary>
        private void CheckGroupsAndSlots()
        {
            {
                DashWidget name = WidgetCatalog.Create("text", 600, 300), plate = WidgetCatalog.Create("panel", 590, 296);
                name.W = 200; name.H = 30; plate.W = 220; plate.H = 40;
                name.Set("text", "=standing(Slot, \"Name\")");
                _screen.Widgets.Add(plate);
                _screen.Widgets.Add(name);
                _selection.Clear(); _selection.Add(plate); _selection.Add(name);
                GroupSelection();
                Check("group: widgets share a group", !string.IsNullOrEmpty(name.Group) && name.Group == plate.Group);
                Select(null);
                // A click on one member takes the whole group.
                Check("a click selects the whole group", GroupOf(name).Count == 2);
                _selection.Clear(); _selection.AddRange(GroupOf(name));
                foreach (DashWidget w in _selection) w.Slot = 1;
                List<DashWidget> next = DuplicateSelection();
                Check("copying a group makes the next Slot, just below",
                    next != null && next.Count == 2 && next.All(w => w.Slot == 2 && w.Group != name.Group) &&
                    next.Min(w => w.Y) > plate.Y + plate.H);
                var groupLayout = DesignTemplates.Blank();
                groupLayout.Screens[0].Widgets.AddRange(new[] { name }.Concat(next.Where(w => w.Type == "text")));
                using (var renderer = new LayoutRenderer(854, 480, new DashboardSettings { Layout = Layouts.Custom, Design = groupLayout },
                    _units))
                {
                    EvalScope s = renderer.Scope(IRacingTelemetrySource.CreatePreviewSnapshot(), false);
                    Check("Slot reads each group's own number",
                        Expr.Text(Expr.Evaluate("standing(Slot, \"Name\")", LayoutRenderer.ForWidget(s, name))) == "L. HARTMANN" &&
                        Expr.Text(Expr.Evaluate("standing(Slot, \"Name\")",
                        LayoutRenderer.ForWidget(s, next.First(w => w.Type == "text")))) == "S. OKAFOR");
                }
                // Copy and paste style.
                name.Set("color", "#FF0000"); name.Set("fxGlow", "6");
                Select(name);
                CopyStyle();
                DashWidget other = next.First(w => w.Type == "text");
                Select(other);
                PasteStyle();
                Check("paste style copies color and effects, not content",
                    other.Get("color") == "#FF0000" && other.Get("fxGlow") == "6" && other.Get("text") == name.Get("text"));
                // Editing several together.
                _selection.Clear(); _selection.Add(name); _selection.Add(other);
                RefreshInspector();
                System.Windows.FrameworkElement row = PropertyRow(name, WidgetCatalog.Find("text").Prop("size"), _selection.ToList());
                var sizeBox = FindChild<System.Windows.Controls.TextBox>(row);
                if (sizeBox != null) sizeBox.Text = "41";
                Check("one edit changes every selected widget", name.Get("size") == "41" && other.Get("size") == "41");
                // The rotate handle sets a turn.
                Select(plate);
                _originals = new Dictionary<DashWidget, System.Windows.Rect> { [plate] = RectOf(plate) };
                _drag = DragMode.Rotate;
                Check("the rotate handle sits above the widget", RotateHandlePoint(plate).Y < plate.Y);
                FlushPendingCommit();
                foreach (DashWidget w in new[] { name, plate }.Concat(next)) _screen.Widgets.Remove(w);
                _drag = DragMode.None;
                Select(null);
                Commit("Self-test cleanup");
            }
        }

        /// <summary>Checks: the tool rail: shapes and text drawn on the canvas.</summary>
        private void CheckToolRail()
        {
            {
                int before = _screen.Widgets.Count;
                DashWidget Draw(Tool tool, System.Windows.Point from, System.Windows.Point to)
                {
                    SetTool(tool);
                    _drawStart = from;
                    _drawEnd = to;
                    FinishDrawing();
                    return _selection.Count == 1 ? _selection[0] : null;
                }
                string Box(DashWidget w) => w == null ? "none" : w.Get("shape") + " " + w.X + "," + w.Y + " " + w.W + "x" + w.H;
                DashWidget rectangle = Draw(Tool.Rectangle, new System.Windows.Point(100, 80), new System.Windows.Point(220, 140));
                DashWidget ellipse = Draw(Tool.Ellipse, new System.Windows.Point(300, 300), new System.Windows.Point(260, 250));
                DashWidget level = Draw(Tool.Line, new System.Windows.Point(50, 400), new System.Windows.Point(250, 400));
                DashWidget rising = Draw(Tool.Line, new System.Windows.Point(50, 400), new System.Windows.Point(150, 300));
                DashWidget clicked = Draw(Tool.Rectangle, new System.Windows.Point(600, 100), new System.Windows.Point(601, 101));
                DashWidget text = Draw(Tool.Text, new System.Windows.Point(400, 200), new System.Windows.Point(400, 200));
                Check("tools: a drawn rectangle is a square-cornered shape exactly where it was drawn",
                    rectangle?.Type == "shape" && rectangle.Get("shape") == "Rectangle" && rectangle.Get("radius") == "0" &&
                    rectangle.X == 100 && rectangle.Y == 80 && rectangle.W == 120 && rectangle.H == 60, Box(rectangle));
                Check("tools: an ellipse drawn up and to the left lands where it was drawn",
                    ellipse?.Get("shape") == "Ellipse" && ellipse.X == 260 && ellipse.Y == 250 && ellipse.W == 40 && ellipse.H == 50,
                    Box(ellipse));
                Check("tools: a level line keeps a thin box, a sloping one runs corner to corner",
                    level?.Get("shape") == "Line" && level.W == 200 && level.H == 12 && level.Y == 394 &&
                    rising?.Get("shape") == "Diagonal up" && rising.X == 50 && rising.Y == 300 && rising.W == 100 && rising.H == 100,
                    Box(level) + "; " + Box(rising));
                Check("tools: a click without a drag makes one of the usual size",
                    clicked != null && clicked.X == 600 && clicked.Y == 100 && clicked.W == WidgetCatalog.Find("shape").DefaultW,
                    Box(clicked));
                Check("tools: Text makes a text box where it's clicked, each gets a name, and the rail goes back to Move",
                    text?.Type == "text" && text.X == 400 && rectangle.Name == "Rectangle 1" && _tool == Tool.Move &&
                    _screen.Widgets.Count == before + 6,
                    (text?.Type ?? "none") + " " + rectangle?.Name + " " + _tool);
                var undone = new List<int>();
                for (int i = 0; i < 6; i++) { Undo(); undone.Add(_screen.Widgets.Count); }
                Check("tools: each drawing is one step to undo", _screen.Widgets.Count == before,
                    _screen.Widgets.Count + " vs " + before + "; after each undo: " + string.Join(",", undone) + "; undo steps " +
                    _undo.Count);
            }
        }

        /// <summary>Checks: drawing: fills, patterns, paths, the Pen and editing points.</summary>
        private void CheckDrawing()
        {
            {
                // Every pattern tile holds both its colors; every fill style makes a brush.
                var tileProblems = new List<string>();
                foreach (string pattern in Fills.Patterns)
                {
                    System.Drawing.Bitmap tile = Fills.Tile(pattern, System.Drawing.Color.FromArgb(255, 250, 40, 40),
                        System.Drawing.Color.FromArgb(255, 20, 20, 240));
                    bool red = false, blue = false;
                    for (int y = 0; y < tile.Height; y += 2)
                        for (int tx = 0; tx < tile.Width; tx += 2)
                        {
                            System.Drawing.Color c = tile.GetPixel(tx, y);
                            if (c.R > 150 && c.B < 120) red = true;
                            if (c.B > 150 && c.R < 120) blue = true;
                        }
                    if (!red || !blue) tileProblems.Add(pattern);
                }
                foreach (string style in Fills.Styles)
                    using (System.Drawing.Brush brush = Fills.Brush(style, System.Drawing.Color.Red, System.Drawing.Color.Blue, 45,
                        "Carbon", 100, 45, new System.Drawing.RectangleF(10, 10, 100, 60)))
                        if (brush == null) tileProblems.Add(style);
                Check("drawing: every pattern shows both its colors, and every fill style makes a brush", tileProblems.Count == 0,
                    string.Join(", ", tileProblems));

                // Points as text and back, and every shape that turns into a path.
                List<PathNode> parsed = PathNodes.Parse("0,0 1,0.5|0.9,0.2|1.1,0.8 0,1");
                Check("drawing: path points read and written the same",
                    parsed.Count == 3 && parsed[1].In.HasValue && parsed[1].Out.Value.X == 1.1f &&
                    PathNodes.Format(parsed) == "0,0 1,0.5|0.9,0.2|1.1,0.8 0,1", PathNodes.Format(parsed));
                var converts = new[] { "Rectangle", "Ellipse", "Polygon", "Star", "Triangle", "Line", "Diagonal down", "Diagonal up",
                    "Chevron" };
                List<string> notConverted = converts.Where(k => PathNodes.FromShape(k, 5, 0.5f, out _) == null).ToList();
                Check("drawing: rectangles, ellipses, polygons, stars, triangles, lines and chevrons convert to paths; a star has twice " +
                    "its points",
                    notConverted.Count == 0 && PathNodes.FromShape("Star", 5, 0.5f, out _).Count == 10 &&
                    PathNodes.FromShape("Brackets", 5, 0.5f, out _) == null,
                    string.Join(", ", notConverted));

                // A filled star is solid in the middle; a closed path is filled, an open one isn't.
                var drawn = new DashLayout { Width = 200, Height = 200 };
                drawn.Theme.BackgroundStyle = "None";
                drawn.Screens.Add(new DashScreen { Name = "Main" });
                DashWidget star = WidgetCatalog.Create("shape", 0, 0);
                star.W = star.H = 100;
                star.Set("shape", "Star"); star.Set("sides", "5"); star.Set("fill", "#FF0000"); star.Set("strokeWidth", "0");
                DashWidget closedPath = WidgetCatalog.Create("path", 100, 100);
                closedPath.W = closedPath.H = 100;
                closedPath.Set("points", "0,0 1,0 0.5,1"); closedPath.Set("fill", "#0000FF"); closedPath.Set("strokeWidth", "0");
                drawn.Screens[0].Widgets.Add(star);
                drawn.Screens[0].Widgets.Add(closedPath);
                System.Drawing.Color starMiddle, pathMiddle, openMiddle;
                using (var renderer = new LayoutRenderer(200, 200, new DashboardSettings { Layout = Layouts.Custom, Design = drawn },
                    _units))
                using (System.Drawing.Bitmap frame = renderer.Render(IRacingTelemetrySource.CreatePreviewSnapshot(), false, 4, 1,
                    drawn.Screens[0]))
                {
                    starMiddle = frame.GetPixel(50, 55);
                    pathMiddle = frame.GetPixel(150, 130);
                }
                closedPath.Set("closed", "false");
                using (var renderer = new LayoutRenderer(200, 200, new DashboardSettings { Layout = Layouts.Custom, Design = drawn },
                    _units))
                using (System.Drawing.Bitmap frame = renderer.Render(IRacingTelemetrySource.CreatePreviewSnapshot(), false, 4, 1,
                    drawn.Screens[0]))
                    openMiddle = frame.GetPixel(150, 130);
                Check("drawing: a star fills its middle, a closed path fills, an open one doesn't",
                    starMiddle.R > 200 && starMiddle.A > 200 && pathMiddle.B > 200 && pathMiddle.A > 200 && openMiddle.A < 10,
                    starMiddle + " " + pathMiddle + " " + openMiddle);

                // The Pen: three clicks and a click on the first point make a closed path round them.
                int before = _screen.Widgets.Count;
                SetTool(Tool.Pen);
                _penNodes.Add(new PathNode { P = new System.Drawing.PointF(100, 100) });
                _penNodes.Add(new PathNode { P = new System.Drawing.PointF(300, 100), In = new System.Drawing.PointF(260, 60),
                    Out = new System.Drawing.PointF(340, 140) });
                _penNodes.Add(new PathNode { P = new System.Drawing.PointF(200, 250) });
                FinishPen(true);
                DashWidget penned = _selection.Count == 1 ? _selection[0] : null;
                Check("drawing: the Pen makes a closed path whose box takes in its points and handles",
                    penned?.Type == "path" && Expr.Bool(penned.Get("closed", "")) && penned.X == 100 && penned.Y == 60 &&
                    penned.W == 240 && penned.H == 190 &&
                    PathNodes.Parse(penned.Get("points", "")).Count == 3 && _tool == Tool.Move,
                    penned == null ? "none" : penned.X + "," + penned.Y + " " + penned.W + "x" + penned.H + " " + penned.Get("points", ""));

                // Editing: dragging the bottom point down grows the box to follow it, and the other points stay put.
                BeginEditPath(penned);
                _editNodes[2].P = new System.Drawing.PointF(200, 300);
                ApplyEdit();
                List<PathNode> onScreen = PathNodes.OnScreen(PathNodes.Parse(penned.Get("points", "")), RectOfF(penned));
                Check("drawing: a moved point moves only itself, and the box follows",
                    penned.H == 240 && Math.Abs(onScreen[0].P.X - 100) < 0.6 && Math.Abs(onScreen[0].P.Y - 100) < 0.6 &&
                    Math.Abs(onScreen[2].P.Y - 300) < 0.6,
                    penned.H + " " + onScreen[0].P + " " + onScreen[2].P);
                EndEditPath();

                // A rectangle converted to a path keeps its place, its layer and its colors.
                DashWidget rect = WidgetCatalog.Create("shape", 40, 40);
                rect.Set("fill", "#123456");
                AddWidgets(new[] { rect }, "Add");
                int layer = _screen.Widgets.IndexOf(rect);
                ConvertToPath(rect);
                DashWidget converted = _screen.Widgets[layer];
                Check("drawing: a rectangle converted to a path keeps its place, layer and colors",
                    converted.Type == "path" && converted.X == 40 && converted.W == rect.W && converted.Get("fill") == "#123456" &&
                    PathNodes.Parse(converted.Get("points", "")).Count == 4,
                    converted.Type + " " + converted.Get("fill"));
                EndEditPath();
                while (_screen.Widgets.Count > before) Undo();

                // Recolor with a pattern: a panel painted in stripes shows both stripe colors.
                var striped = new DashLayout { Width = 160, Height = 160 };
                striped.Screens.Add(new DashScreen { Name = "Main" });
                DashWidget panel = WidgetCatalog.Create("shape", 0, 0);
                panel.W = panel.H = 160;
                panel.Set("fill", "#FFFFFF"); panel.Set("radius", "0");
                panel.Set("fxFill", "Pattern"); panel.Set("fxFillColor", "#FF0000"); panel.Set("fxPatternBack",
                    "#0000FF"); panel.Set("fxPatternAngle", "0");
                striped.Screens[0].Widgets.Add(panel);
                bool redStripe = false, blueStripe = false;
                using (var renderer = new LayoutRenderer(160, 160, new DashboardSettings { Layout = Layouts.Custom, Design = striped },
                    _units))
                using (System.Drawing.Bitmap frame = renderer.Render(IRacingTelemetrySource.CreatePreviewSnapshot(), false, 4, 1,
                    striped.Screens[0]))
                    for (int y = 20; y < 140; y += 2)
                    {
                        System.Drawing.Color c = frame.GetPixel(80, y);
                        if (c.R > 200 && c.B < 60) redStripe = true;
                        if (c.B > 200 && c.R < 60) blueStripe = true;
                    }
                Check("drawing: Recolor with a pattern paints any widget in it", redStripe && blueStripe,
                    "red " + redStripe + ", blue " + blueStripe);
            }
        }

        /// <summary>Checks: modules: kept, shared, brought back and restyled.</summary>
        private void CheckModules()
        {
            {
                void ClearTestModules()
                {
                    foreach (string folder in new[] { DesignModule.Folder, DesignModule.OldBlocksFolder })
                        if (System.IO.Directory.Exists(folder))
                            foreach (string old in System.IO.Directory.GetFiles(folder, "Self test *"))
                                System.IO.File.Delete(old);
                }
                ClearTestModules();
                string png;
                using (var dot = new System.Drawing.Bitmap(2, 2))
                using (var stream = new System.IO.MemoryStream())
                {
                    dot.SetPixel(0, 0, System.Drawing.Color.Red);
                    dot.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                    png = Convert.ToBase64String(stream.ToArray());
                }
                var source = new DashLayout();
                source.Screens.Add(new DashScreen { Name = "Main" });
                source.Images["img-module"] = png;
                source.Images["img-unused"] = png;
                DashWidget Part(string type, double x, double y, double w, double h, params string[] props)
                {
                    DashWidget part = WidgetCatalog.Create(type, x, y);
                    part.W = w; part.H = h;
                    for (int i = 0; i + 1 < props.Length; i += 2) part.Set(props[i], props[i + 1]);
                    source.Screens[0].Widgets.Add(part);
                    return part;
                }
                Part("shape", 100, 60, 200, 120, "clip", "true", "fill", "#112233", "strokeWidth", "4");
                Part("image", 120, 80, 40, 40, "image", "img-module");
                Part("text", 140, 100, 120, 40, "text", "RPM", "size", "20").Locked = true;
                DesignModule made = DesignModule.From("Self test module", source.Screens[0].Widgets, source, "#2DD4BF", _units);
                System.Drawing.Size picture;
                using (var thumb = new System.Drawing.Bitmap(new System.IO.MemoryStream(Convert.FromBase64String(made.Thumbnail)))) picture = thumb.Size;
                Check("modules: a module starts at 0,0, unlocked, with only the images it shows and a picture of it",
                    made.Widgets[0].X == 0 && made.Widgets[0].Y == 0 && made.Widgets[1].X == 20 && made.Widgets[2].Y == 40 &&
                    !made.Widgets.Any(w => w.Locked) &&
                    made.Widgets[0].Get("clip") == "false" && source.Screens[0].Widgets[0].Get("clip") == "true" &&
                    made.Images.Count == 1 && made.Images.ContainsKey("img-module") && picture.Width == 200 && picture.Height == 120,
                    made.Widgets[1].X + "," + made.Widgets[2].Y + " images " + made.Images.Count + " picture " + picture);

                string scratch = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "afd-modules-" + Guid.NewGuid().ToString("N")
                    .Substring(0, 8));
                System.IO.Directory.CreateDirectory(scratch);
                string moduleFile = System.IO.Path.Combine(scratch, "shared" + DesignModule.Extension),
                    foreignFile = System.IO.Path.Combine(scratch, "foreign" + DesignModule.Extension);
                DesignModule.Write(moduleFile, made);
                DesignModule read = DesignModule.Read(moduleFile);
                System.IO.File.WriteAllText(foreignFile, "{\"Format\":\"AlmostFastOverlays.Dashboard\",\"Widgets\":[]}");
                bool refused = false;
                try { DesignModule.Read(foreignFile); } catch (System.IO.InvalidDataException) { refused = true; }
                Check("modules: a module file reads back exactly as written, and other files are refused",
                    SettingsStore.ToJson(read) == SettingsStore.ToJson(made) && refused, "refused " + refused);

                DesignModule first = DesignModule.Import(moduleFile), firstAgain = DesignModule.Import(moduleFile);
                made.Description = "Another one by the same name";
                string otherFile = System.IO.Path.Combine(scratch, "other" + DesignModule.Extension);
                DesignModule.Write(otherFile, made);
                DesignModule other = DesignModule.Import(otherFile), otherAgain = DesignModule.Import(otherFile);
                List<DesignModule> library = DesignModule.Library();
                Check("modules: importing a module twice keeps one; another of the same name is numbered beside it",
                    first.FilePath == firstAgain.FilePath && other.Name == "Self test module (2)" &&
                    otherAgain.FilePath == other.FilePath &&
                    library.Count(m => m.Name.StartsWith("Self test module")) == 2,
                    first.Name + " / " + other.Name + " / " + otherAgain.Name + ", " +
                    library.Count(m => m.Name.StartsWith("Self test module")) + " in the library");

                System.IO.Directory.CreateDirectory(DesignModule.OldBlocksFolder);
                System.IO.File.WriteAllText(System.IO.Path.Combine(DesignModule.OldBlocksFolder, "Self test block.afoblock"),
                    SettingsStore.ToJson(made.Widgets));
                DesignModule oldBlock = DesignModule.Library().FirstOrDefault(m => m.Name == "Self test block");
                Check("modules: blocks saved by earlier versions are still in the Library",
                    oldBlock != null && oldBlock.OldBlock && oldBlock.Widgets.Count == 3,
                    oldBlock == null ? "missing" : oldBlock.Widgets.Count + " widgets");

                // Placed on a bigger screen with an unrelated image under the same id.
                var target = new DashLayout { Width = 1280, Height = 720 };
                target.Screens.Add(new DashScreen { Name = "Main" });
                target.Images["img-module"] = "unrelated";
                List<DashWidget> placed = read.Place(target);
                string imageId = placed[1].Get("image");
                Check("modules: on a bigger screen it scales evenly, text and lines too; a taken image id is renamed; it comes in as one " +
                    "group",
                    placed[0].W == 300 && placed[0].H == 180 && placed[2].Get("size") == "30" && placed[0].Get("strokeWidth") == "6" &&
                    imageId != "img-module" && target.Images.TryGetValue(imageId, out string carried) && carried == png &&
                    target.Images["img-module"] == "unrelated" &&
                    placed.All(w => w.Group == placed[0].Group && w.Group.Length > 0 && w.GroupName == "Self test module") &&
                    placed[0].Id != read.Widgets[0].Id,
                    placed[0].W + "x" + placed[0].H + " size " + placed[2].Get("size") + " line " + placed[0].Get("strokeWidth") +
                    " image " + imageId);

                // Restyling what's placed: one color swatch changes every use, and Scale takes text and lines along.
                DashWidget red = WidgetCatalog.Create("shape", 100, 100), alsoRed = WidgetCatalog.Create("shape", 300, 140);
                red.Set("fill", "#FF0000"); red.Set("stroke", "#00FF00"); red.Set("strokeWidth", "3");
                alsoRed.Set("fill", "#ff0000"); alsoRed.Set("stroke", "#00FF00"); alsoRed.Set("strokeWidth", "3");
                AddWidgets(new[] { red, alsoRed }, "Self test colors");
                ColorField swatch = Inspector.Children.OfType<System.Windows.Controls.Grid>()
                    .SelectMany(g => g.Children.OfType<ColorField>())
                    .FirstOrDefault(f => string.Equals(f.Value, "#FF0000", StringComparison.OrdinalIgnoreCase));
                swatch?.SetValue("#0000FF", true);
                Check("modules: Selection colors lists a color once and changes every use of it",
                    swatch != null && red.Get("fill") == "#0000FF" && alsoRed.Get("fill") == "#0000FF" && red.Get("stroke") == "#00FF00",
                    swatch == null ? "no swatch" : red.Get("fill") + " " + alsoRed.Get("fill"));
                System.Windows.Rect unscaled = Union(new[] { red, alsoRed });
                ScaleSelection(2);
                System.Windows.Rect scaled = Union(new[] { red, alsoRed });
                Check("modules: Scale doubles the selection about its middle, line widths included",
                    Math.Abs(scaled.Width - unscaled.Width * 2) <= 2 &&
                    Math.Abs(scaled.X + scaled.Width / 2 - (unscaled.X + unscaled.Width / 2)) <= 1 &&
                    Math.Abs(scaled.Y + scaled.Height / 2 - (unscaled.Y + unscaled.Height / 2)) <= 1 && red.Get("strokeWidth") == "6",
                    unscaled + " → " + scaled + ", line " + red.Get("strokeWidth"));
                _screen.Widgets.Remove(red);
                _screen.Widgets.Remove(alsoRed);
                _selection.Clear();
                SelectionChanged();
                ClearTestModules();
                try { System.IO.Directory.Delete(scratch, true); } catch { }
            }
        }

        /// <summary>Checks: repeat: rows and columns, and around a circle.</summary>
        private void CheckRepeat()
        {
            {
                DashWidget Piece(double x, double y, double w, double h, string name = "LED", string rotate = "0")
                {
                    DashWidget piece = WidgetCatalog.Create("shape", x, y);
                    piece.Name = name; piece.X = x; piece.Y = y; piece.W = w; piece.H = h;
                    piece.Set("rotate", rotate);
                    return piece;
                }
                string Where(List<List<DashWidget>> sets) => string.Join(" ",
                    sets.Select(s => s[0].X + "," + s[0].Y + "@" + s[0].Get("rotate")));

                DashWidget cell = Piece(100, 100, 40, 20);
                List<List<DashWidget>> gridSets = Repeater.Build(new[] { cell },
                    new RepeatSpec { Across = 3, Down = 2, GapX = 10, GapY = 6 });
                Check("repeat: a grid lays copies across then down, the gaps between them",
                    gridSets.Count == 5 && Where(gridSets) == "150,100@0 200,100@0 100,126@0 150,126@0 200,126@0", Where(gridSets));

                DashWidget tick = Piece(190, 80, 20, 40);
                List<List<DashWidget>> round = Repeater.Build(new[] { tick },
                    new RepeatSpec { Circle = true, Count = 4, Sweep = 360, Radius = 100, Start = -90, Follow = true });
                Check("repeat: all the way round, the original stays at the top and copies turn to follow the circle",
                    Where(round) == "290,180@90 190,280@180 90,180@-90", Where(round));
                List<List<DashWidget>> half = Repeater.Build(new[] { tick },
                    new RepeatSpec { Circle = true, Count = 5, Sweep = 180, Radius = 100, Start = -90, Follow = true });
                Check("repeat: half a circle ends where the arc ends",
                    half.Count == 4 && Where(half.Skip(3).ToList()) == "190,280@180", Where(half));
                List<List<DashWidget>> keptUpright = Repeater.Build(new[] { tick },
                    new RepeatSpec { Circle = true, Count = 4, Sweep = 360, Radius = 100, Start = -90, Follow = false, TurnEach = 10 });
                Check("repeat: kept upright, copies only take the extra turn each",
                    Where(keptUpright) == "290,180@10 190,280@20 90,180@30", Where(keptUpright));
                DashWidget live = Piece(190, 80, 20, 40, "Needle", "=Speed");
                List<List<DashWidget>> turned = Repeater.Build(new[] { live },
                    new RepeatSpec { Circle = true, Count = 2, Sweep = 180, Radius = 100, Start = 0, Follow = true });
                Check("repeat: a formula's turn is kept and added to", turned[0][0].Get("rotate") == "=(Speed) + 180",
                    turned[0][0].Get("rotate"));

                // Numbered: each gets a group and the next Slot, named to match.
                DashWidget led = Piece(10, 10, 12, 12);
                var leds = new List<DashWidget> { led };
                List<List<DashWidget>> numbered = Repeater.Build(leds, new RepeatSpec { Across = 4, Down = 1, GapX = 4 });
                Repeater.Group(leds, numbered, true, "LED");
                Check("repeat: numbered copies count up as Slot, each in a group of its own",
                    led.Slot == 1 && led.GroupName == "LED" && numbered.Select(s => s[0].Slot).SequenceEqual(new double[] { 2, 3, 4 }) &&
                    numbered[2][0].GroupName == "LED" && numbered.Select(s => s[0].Group).Concat(new[] { led.Group }).Distinct()
                    .Count() == 4 && led.Group.Length > 0,
                    led.GroupName + " / " + string.Join(", ", numbered.Select(s => s[0].GroupName + " " + s[0].Slot)));
                DashWidget card = Piece(0, 0, 200, 30, "Name"), position = Piece(0, 0, 30, 30, "Position");
                card.Group = position.Group = "g-card"; card.GroupName = position.GroupName = "Driver"; card.Slot = position.Slot = 3;
                var driverRow = new List<DashWidget> { card, position };
                List<List<DashWidget>> rows = Repeater.Build(driverRow, new RepeatSpec { Across = 1, Down = 3, GapY = 4 });
                Repeater.Group(driverRow, rows, true, "Repeat");
                Check("repeat: a numbered group counts on from its own Slot, and each copy is one group",
                    card.Slot == 3 && card.GroupName == "Driver" && rows[0][0].Slot == 4 && rows[1][1].Slot == 5 &&
                    rows[1][1].GroupName == "Driver" &&
                    rows[0][0].Group == rows[0][1].Group && rows[0][0].Group != rows[1][0].Group && rows[0][0].Group != card.Group &&
                    rows[1][0].Y == 68,
                    string.Join(", ", rows.Select(s => s[0].GroupName + " " + s[0].Slot + " at " + s[0].Y)));
            }
        }

        /// <summary>Checks: module options, and guides.</summary>
        private void CheckModuleOptionsAndGuides()
        {
            {
                var optioned = new DashLayout { Width = 60, Height = 40 };
                optioned.Theme.BackgroundStyle = "None";
                optioned.Screens.Add(new DashScreen { Name = "Main" });
                DashWidget paint = WidgetCatalog.Create("shape", 0, 0), label = WidgetCatalog.Create("text", 30, 0);
                paint.W = 30; paint.H = 40; paint.Set("strokeWidth", "0"); paint.Set("radius", "0"); paint.Set("fill",
                    ModuleOptions.Formula("Paint"));
                label.W = 30; label.H = 40; label.Set("size", ModuleOptions.Formula("Size")); label.Set("text", "8");
                foreach (DashWidget w in new[] { paint,
                    label }) { w.Group = "g-options"; w.GroupName = "Gauge"; optioned.Screens[0].Widgets.Add(w); }
                ModuleOptions.Set(optioned.Screens[0].Widgets, "Paint", "#FF0000");
                ModuleOptions.Set(optioned.Screens[0].Widgets, "Size", "20");
                EvalScope optionScope = new EvalScope { T = IRacingTelemetrySource.CreatePreviewSnapshot(), Accent = "#2DD4BF" };
                object size = Expr.Evaluate("option(\"Size\") * 2", LayoutRenderer.ForWidget(optionScope, label));
                Check("options: option() reads the group's value, as a number when it is one", Expr.Num(size) == 40, Expr.Text(size));

                System.Drawing.Color before, after;
                using (var renderer = new LayoutRenderer(60, 40, new DashboardSettings { Layout = Layouts.Custom, Design = optioned },
                    _units) { FixedNow = TestNow, FlagClock = 0 })
                {
                    using (System.Drawing.Bitmap frame = renderer.Render(optionScope.T, false, 4, 1,
                        optioned.Screens[0])) before = frame.GetPixel(10,
                        20);
                    ModuleOptions.Set(optioned.Screens[0].Widgets, "Paint", "accent");
                    using (System.Drawing.Bitmap frame = renderer.Render(optionScope.T, false, 4, 1,
                        optioned.Screens[0])) after = frame.GetPixel(10,
                        20);
                }
                Check("options: changing an option redraws what's linked to it, theme colors included",
                    before.R > 240 && before.G < 20 && after.G > 180 && after.R < 80, before + " → " + after);

                DashLayout.ScaleWidgets(optioned.Screens[0].Widgets, 2, 2);
                Check("options: an option linked to a size scales with the module, on every widget of the group",
                    paint.Get(ModuleOptions.Prefix + "Size") == "40" && label.Get(ModuleOptions.Prefix + "Size") == "40" &&
                    label.Get(ModuleOptions.Prefix + "Paint") == "accent",
                    paint.Get(ModuleOptions.Prefix + "Size") + " " + label.Get(ModuleOptions.Prefix + "Size"));
                ModuleOptions.Remove(optioned.Screens[0].Widgets, "Paint");
                Check("options: ending an option leaves its settings with its value",
                    paint.Get("fill") == "accent" && paint.Get(ModuleOptions.Prefix + "Paint") == null && label.Get(ModuleOptions.Prefix +
                    "Paint") == null,
                    paint.Get("fill"));

                // Guides: widgets snap to them, and they move with the screen size.
                // At 100%, where snapping reaches 6 pixels, on an empty screen so only the guide is near.
                double zoom = _zoom;
                SetZoom(1);
                List<DashWidget> keep = _screen.Widgets.ToList();
                _screen.Widgets.Clear();
                _layout.GuidesX.Add(333);
                double nudge = SnapAxis(new[] { 331.0 }, true, new System.Windows.Rect(331, 0, 10, 10));
                _layout.GuidesX.Remove(333);
                _screen.Widgets.AddRange(keep);
                SetZoom(zoom);
                var sized = new DashLayout { Width = 800, Height = 480, GuidesX = new List<double> { 400 },
                    GuidesY = new List<double> { 120 } };
                sized.ScaleTo(1600, 960);
                Check("guides: widgets snap to a guide, and guides follow the screen to a new size",
                    Math.Abs(nudge - 2) < 0.001 && sized.GuidesX[0] == 800 && sized.GuidesY[0] == 240,
                    nudge + " " + sized.GuidesX[0] + " " + sized.GuidesY[0]);
            }
        }

        /// <summary>Checks: the Library explains itself.</summary>
        private void CheckLibraryExplainsItself()
        {
            {
                List<string> unexplained = WidgetCatalog.All.SelectMany(t => t.Props.Where(p => !p.Hidden &&
                    string.IsNullOrWhiteSpace(p.Help))
                    .Select(p => t.Id + "." + p.Key))
                    .ToList();
                List<string> undescribed = WidgetCatalog.All.Where(t => string.IsNullOrWhiteSpace(t.Description)).Select(t => t.Id)
                    .Concat(QuickValues.All().Concat(DesignBlocks.BuiltIn()).Where(b => string.IsNullOrWhiteSpace(b.Description))
                        .Select(b => b.Name))
                        .ToList();
                Check("library: every widget, block and setting says what it does", unexplained.Count == 0 && undescribed.Count == 0,
                    string.Join(", ", unexplained.Concat(undescribed).Take(12)));
            }
        }

        /// <summary>Checks: dropping onto a panel builds its views.</summary>
        private void CheckDroppingOntoPanels()
        {
            {
                DashWidget pane = WidgetCatalog.Create("panel", 500, 300);
                pane.W = 300; pane.H = 120; pane.Name = "Engine panel";
                _screen.Widgets.Add(pane);
                DashWidget oil = WidgetCatalog.Create("metric", 520, 310), water = WidgetCatalog.Create("metric", 900, 310),
                    volts = WidgetCatalog.Create("metric", 600, 330);
                bool found = PanelAt(new System.Windows.Point(650, 360)) == pane && PanelAt(new System.Windows.Point(100, 100)) == null;
                DropOnPanel(pane, new List<DashWidget> { oil }, "Add oil", false);
                bool joined = !string.IsNullOrEmpty(pane.Group) && oil.Group == pane.Group &&
                    string.IsNullOrEmpty(pane.Get("tapName", "")) && string.IsNullOrEmpty(oil.Get("visible", ""));
                DropOnPanel(pane, new List<DashWidget> { water }, "Add water", true);
                DropOnPanel(pane, new List<DashWidget> { volts }, "Add volts", false);
                Check("dropping onto a panel joins it, and a new view writes the tap setup", found && joined &&
                    pane.Get("tapName") == "engine" && pane.Get("tapViews") == "2" && oil.Get("visible") == "tap(\"engine\") == 0" &&
                    water.Get("visible") == "tap(\"engine\") == 1" && volts.Get("visible") == "tap(\"engine\") == 1" &&
                    water.X + water.W <= pane.X + pane.W && new[] { water, volts }.All(w => w.Group == pane.Group) &&
                    _designTaps["engine"] == 1,
                    pane.Get("tapName", "") + " " + oil.Get("visible", "") + " / " + water.Get("visible", "") + " / " +
                    volts.Get("visible", "") + " water at " + water.X);
                foreach (DashWidget w in new[] { pane, oil, water, volts }) _screen.Widgets.Remove(w);
                _designTaps.Clear();
                Select(null);
                Commit("Self-test cleanup");
            }
        }

        /// <summary>Checks: several widgets resized together.</summary>
        private void CheckResizingTogether()
        {
            {
                DashWidget a = WidgetCatalog.Create("panel", 100, 100), c = WidgetCatalog.Create("panel", 100, 100);
                a.X = 100; a.Y = 100; a.W = 100; a.H = 50;
                c.X = 300; c.Y = 150; c.W = 100; c.H = 50;
                _screen.Widgets.Add(a);
                _screen.Widgets.Add(c);
                Select(a);
                _selection.Add(c);
                // Together they span 100-400 by 100-200; the bottom-right handle sits at (400, 200).
                Check("several selected widgets get handles around them all", HandleAt(new System.Windows.Point(400, 200)) == 4);
                _originals = new[] { a, c }.ToDictionary(w => w, RectOf);
                _handle = 4;
                DoResize(new System.Windows.Point(700, 300), true, false);
                Check("several widgets scale together",
                    a.X == 100 && a.W == 200 && a.H == 100 && c.X == 500 && c.Y == 200 && c.W == 200 && c.H == 100,
                    a.X + "," + a.Y + " " + a.W + "x" + a.H + " / " + c.X + "," + c.Y + " " + c.W + "x" + c.H);
                _screen.Widgets.Remove(a);
                _screen.Widgets.Remove(c);
                Select(null);
            }
        }

        /// <summary>Checks: shape tools on every shaped widget.</summary>
        private void CheckShapeTools()
        {
            foreach (WidgetType shaped in WidgetCatalog.All.Where(t => t.Prop("bend") != null))
            {
                var layout = DesignTemplates.Blank();
                layout.Theme.BackgroundStyle = "Solid";
                DashWidget plainOne = WidgetCatalog.Create(shaped.Id, 100, 100), bent = WidgetCatalog.Create(shaped.Id, 100, 100);
                foreach (DashWidget w in new[] { plainOne, bent }) { w.W = 300; w.H = shaped.Id == "blindspot" ? 200 : 80; }
                bent.Set("bend", "60");
                bent.Set("taper", "50");
                string Draw(DashWidget w, out int errors)
                {
                    layout.Screens[0].Widgets.Clear();
                    layout.Screens[0].Widgets.Add(w);
                    using (var renderer = new LayoutRenderer(854, 480, new DashboardSettings { Layout = Layouts.Custom, Design = layout },
                        _units))
                    using (System.Drawing.Bitmap frame = renderer.Render(IRacingTelemetrySource.CreateSpotterPreviewSnapshot(), false, 4,
                        1, layout.Screens[0]))
                    {
                        errors = renderer.Errors.Count;
                        return Pixels(frame);
                    }
                }
                string a = Draw(plainOne, out int e1), b = Draw(bent, out int e2);
                Check("shape tools change " + shaped.Name, e1 == 0 && e2 == 0 && a != b);
            }
        }

        /// <summary>Checks: editing operations.</summary>
        private void CheckEditingOperations()
        {
            int count = _screen.Widgets.Count;
            AddWidgetAtCenter("metric");
            Check("add widget", _screen.Widgets.Count == count + 1 && _selection.Count == 1);
            DashWidget added = _selection[0];
            Undo();
            Check("undo add", _screen.Widgets.Count == count);
            Redo();
            Check("redo add", _screen.Widgets.Count == count + 1);
            added = _screen.Widgets.Last();
            Select(added);
            double x = added.X;
            Nudge(5, 0);
            Nudge(5, 0);
            Check("nudge", Math.Abs(added.X - (x + 10)) < 0.01);
            Undo();
            Check("nudges merge into one undo step", Math.Abs(_screen.Widgets.Last().X - x) < 0.01);
            added = _screen.Widgets.Last();
            Select(added);
            DuplicateSelection();
            Check("duplicate", _screen.Widgets.Count == count + 2 && _selection[0] != added && _selection[0].Id != added.Id);
            CopySelection();
            Paste();
            Check("copy and paste", _screen.Widgets.Count == count + 3);
            DeleteSelection();
            Check("delete", _screen.Widgets.Count == count + 2);
            Select(added);
            AlignSelection("left");
            Check("align left on screen", added.X == 0);
            AlignSelection("bottom");
            Check("align bottom on screen", Math.Abs(added.Y + added.H - CanvasH) < 0.01);
            Reorder(int.MinValue);
            Check("send to back", _screen.Widgets[0] == added);
            Reorder(int.MaxValue);
            Check("bring to front", _screen.Widgets.Last() == added);
            added.Set("value", "=format(Rpm, \"0\")");
            Commit("Edit value");
            Check("property formula stored", added.Get("value") == "=format(Rpm, \"0\")");
            ToggleLock();
            Check("lock", added.Locked && WidgetAt(new System.Windows.Point(added.X + 2, added.Y + 2)) != added);
            ToggleLock();
        }

        /// <summary>Checks: screens.</summary>
        private void CheckScreens()
        {
            int screens = _layout.Screens.Count;
            AddScreen(true, "Pit lane", "OnPitRoad");
            Check("add conditional screen", _layout.Screens.Count == screens + 1 && _screen.Condition == "OnPitRoad");
            using (var renderer = new LayoutRenderer(854, 480, new DashboardSettings { Layout = Layouts.Custom, Design = _layout }, _units))
            {
                Check("conditional screen wins on pit road",
                    renderer.ActiveScreen(renderer.Scope(IRacingTelemetrySource.CreatePitLanePreviewSnapshot(), false)).Name == "Pit lane");
                Check("main screen otherwise",
                    renderer.ActiveScreen(renderer.Scope(IRacingTelemetrySource.CreatePreviewSnapshot(), false)).Name != "Pit lane");
                int racing = renderer.StateHash(IRacingTelemetrySource.CreatePreviewSnapshot(), false, -1);
                Check("change detection is stable",
                    racing == renderer.StateHash(IRacingTelemetrySource.CreatePreviewSnapshot(), false, -1));
                Check("change detection sees a new state", racing != renderer.StateHash(ShiftSnapshot(), false, -1));
            }
            DeleteScreen();
            Check("delete screen", _layout.Screens.Count == screens);
        }

        /// <summary>Checks: save, reload, export, import.</summary>
        private void CheckSaveAndExport()
        {
            string scratchFolder = _scratchFolder;
            SaveDesign(true);
            AppSettings reloaded = SettingsStore.Load();
            DashboardSettings again = reloaded.Dashboards.FirstOrDefault(d => d.Id == _dashboard.Id);
            Check("save and reload keeps the design", again?.Design != null && again.Design.AllWidgets().Count() == _layout.AllWidgets()
                .Count(),
                again == null ? "dashboard missing" : again.Design.AllWidgets().Count() + " vs " + _layout.AllWidgets().Count());
            Check("reloaded design keeps formulas", again?.Design.AllWidgets().Any(w => w.Get("value") == "=format(Rpm, \"0\")") == true);
            using (IDashboardRenderer runtime = DashboardRendererFactory.Create(854, 480, again, _units))
                Check("runtime uses the designed renderer", runtime is LayoutRenderer);
            string file = Path.Combine(scratchFolder, "selftest.afodash");
            DesignPackage.Write(file, new DesignPackage { Name = "Self test", AccentColor = "#FF0000", Design = _layout });
            DesignPackage package = DesignPackage.Read(file);
            Check("export and import", package.Design.AllWidgets().Count() == _layout.AllWidgets()
                .Count() && package.AccentColor == "#FF0000");
            var lit = new DashboardSettings { Name = "Lit", AccentColor = "#00FF00", Design = _layout, Lights = LightPresets.Refined() };
            DesignPackage.Write(file, DesignPackage.From(lit));
            DesignPackage withLights = DesignPackage.Read(file);
            Check("exported dashboards carry their LED lights",
                withLights.Lights != null && withLights.Lights.Layers.Count == lit.Lights.Layers.Count &&
                withLights.Lights.Layers.Count > 0);
            Check("dirty flag clears after save", !Dirty);
        }
    }
}
