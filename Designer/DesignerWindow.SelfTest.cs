using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AlmostFastOverlays
{
    public partial class DesignerWindow
    {
        /// <summary>
        /// Exercises the designer end to end through its real code paths and reports
        /// each check. Used by --designer-selftest; it saves into the settings next to
        /// the exe it runs from, so run it from a scratch copy.
        /// </summary>
        internal List<string> RunSelfTest(string scratchFolder)
        {
            var report = new List<string>();
            int failures = 0;
            void Check(string name, bool ok, string detail = "")
            {
                if (!ok) failures++;
                report.Add((ok ? "PASS  " : "FAIL  ") + name + (detail.Length > 0 ? "  (" + detail + ")" : ""));
            }

            // ---------------- formulas
            var scope = new EvalScope { T = IRacingTelemetrySource.CreatePreviewSnapshot(), Units = _units, Theme = _layout.Theme, Accent = "#2DD4BF" };
            (string Formula, string Expected)[] formulas =
            {
                ("1 + 2 * 3", "7"), ("(1 + 2) * 3", "9"), ("10 / 4", "2.5"), ("7 % 3", "1"), ("\"a\" + 5", "a5"),
                ("Gear", "4"), ("gear(-1)", "R"), ("GearText", "4"), ("laptime(87.932)", "1:27.932"), ("signed(0.456, 2)", "+0.46"),
                ("signed(-0.1, 1)", "-0.1"), ("Speed > 100 ? \"fast\" : \"slow\"", "fast"), ("clamp(5, 0, 3)", "3"), ("norm(5, 0, 10)", "0.5"),
                ("steps(3, \"a\", 2, \"b\", 5, \"c\")", "b"), ("Gear = 4", "true"), ("Gear == 4 && !OnPitRoad", "true"),
                ("not OnPitRoad and Gear >= 4", "true"), ("Position > 0 ? \"P\" + Position : \"P--\"", "P4"), ("round(2.345, 2)", "2.35"),
                ("format(1234.5, \"0.0\")", "1234.5"), ("pad(7, 2)", "07"), ("upper(\"abc\")", "ABC"), ("$accent", "#2DD4BF"),
                ("alpha($accent, 0.5)", "#2DD4BF"), ("mix(#000000, #FFFFFF, 0.5)", "#7F7F7F"), ("nz(0, 9)", "9"), ("min(3, 1, 2)", "1"),
                ("max(3, 1, 2)", "3"), ("abs(-2)", "2"), ("if(Gear > 3, \"hi\", \"lo\")", "hi"), ("FuelLapsLeft > 0", "true"), ("clock(75)", "01:15")
            };
            foreach (var f in formulas)
            {
                Expr.Compiled compiled = Expr.Compile(f.Formula);
                string actual = compiled.Error ?? Expr.Text(compiled.Run(scope));
                Check("formula " + f.Formula, actual == f.Expected, "got " + actual);
            }
            Check("formula error: incomplete", Expr.Compile("Speed +").Error != null);
            Check("formula error: unknown field", Expr.Compile("Sped * 2").Error?.Contains("Sped") == true);
            Check("formula error: unknown function", Expr.Compile("bogus(1)").Error != null);
            Check("formula error: unclosed text", Expr.Compile("\"abc").Error != null);
            Check("formula: fuel() follows units", Expr.Text(Expr.Evaluate("fuel(6.4)", scope)) == (_units.Gallons ? "6.40" : "6.4"));

            // ---------------- every template renders in every state without widget errors
            var states = new[]
            {
                IRacingTelemetrySource.CreatePreviewSnapshot(), IRacingTelemetrySource.CreatePitLanePreviewSnapshot(), IRacingTelemetrySource.CreateSpotterPreviewSnapshot(),
                TelemetrySnapshot.Disconnected, IRacingTelemetrySource.CreatePreviewSnapshot(RacingFlag.Checkered), ShiftSnapshot()
            };
            var synthetic = new SyntheticTelemetry();
            foreach (DesignTemplate template in DesignTemplates.All)
            {
                DashboardSettings d = DashboardSettings.CreateDesigned(template);
                d.Design.FullScreenFlags = false;
                var errors = new List<string>();
                using (var renderer = new LayoutRenderer(854, 480, d, _units))
                {
                    foreach (TelemetrySnapshot snapshot in states.Concat(new[] { synthetic.Next(out _, out _, out _) }))
                        using (renderer.Render(snapshot, true, snapshot.Gear - 1, 0.5f))
                            errors.AddRange(renderer.Errors.Values);
                }
                Check("template renders cleanly: " + template.Name, errors.Count == 0, string.Join("; ", errors.Distinct().Take(3)));
            }
            // ---------------- cost per frame on the device path (render + change check)
            double Time(IDashboardRenderer renderer)
            {
                TelemetrySnapshot snapshot = IRacingTelemetrySource.CreatePreviewSnapshot();
                using (renderer.Render(snapshot, false, 4, 1)) { }
                var watch = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < 30; i++)
                {
                    (renderer as IStateRenderer)?.StateHash(snapshot, false, -1);
                    using (renderer.Render(snapshot, false, 4, 1)) { }
                }
                return watch.Elapsed.TotalMilliseconds / 30;
            }
            foreach (DesignTemplate template in DesignTemplates.All)
                using (IDashboardRenderer designed = DashboardRendererFactory.Create(854, 480, DashboardSettings.CreateDesigned(template), _units))
                    report.Add("INFO  " + template.Id + ": " + Time(designed).ToString("0.0") + " ms per frame");
            // Where the time goes in the flagship.
            DashLayout halo = DesignTemplates.Find("halo").Create();
            var costs = new List<string>();
            foreach (DashWidget widget in halo.Screens[0].Widgets.Concat(new DashWidget[] { null }))
            {
                DashLayout single = DesignTemplates.Find("halo").Create();
                single.Screens[0].Widgets = widget == null ? new List<DashWidget>() : new List<DashWidget> { widget };
                using (var renderer = new LayoutRenderer(854, 480, new DashboardSettings { Layout = Layouts.Custom, Design = single, AccentColor = "#22D3EE" }, _units))
                    costs.Add((widget?.Name ?? "background only") + " " + Time(renderer).ToString("0.0"));
            }
            report.Add("INFO  halo cost by widget (ms): " + string.Join(", ", costs));

            using (var designed = new LayoutRenderer(854, 480, DashboardSettings.CreateDesigned(DesignTemplates.Find("halo")), _units))
            {
                TelemetrySnapshot snapshot = IRacingTelemetrySource.CreatePreviewSnapshot();
                var watch = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < 200; i++) designed.StateHash(snapshot, false, -1);
                report.Add("INFO  Halo change check: " + (watch.Elapsed.TotalMilliseconds / 200).ToString("0.000") + " ms (runs 20 times a second)");
            }

            // The static-layer cache must not change a single pixel.
            string Pixels(System.Drawing.Bitmap bitmap)
            {
                var data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                try
                {
                    var bytes = new byte[data.Stride * bitmap.Height];
                    System.Runtime.InteropServices.Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
                    using (var sha = System.Security.Cryptography.SHA1.Create()) return Convert.ToBase64String(sha.ComputeHash(bytes));
                }
                finally { bitmap.UnlockBits(data); }
            }
            foreach (DesignTemplate template in DesignTemplates.All)
            {
                DashboardSettings d = DashboardSettings.CreateDesigned(template);
                int mismatches = 0, baked = 0;
                // Waving flags follow the clock; hold both renderers at the same moment.
                using (var cached = new LayoutRenderer(854, 480, d, _units) { FlagClock = 0 })
                using (var plain = new LayoutRenderer(854, 480, d, _units) { CacheStatic = false, FlagClock = 0 })
                    foreach (TelemetrySnapshot snapshot in states)
                        using (var a = cached.Render(snapshot, false, snapshot.Gear, 1))
                        using (var b = plain.Render(snapshot, false, snapshot.Gear, 1))
                            if (Pixels(a) != Pixels(b)) mismatches++;
                using (var counter = new LayoutRenderer(854, 480, d, _units))
                {
                    using (counter.Render(states[0], false, 4, 1)) { }
                    baked = d.Design.Screens[0].Widgets.Count(w => counter.IsBaked(d.Design.Screens[0], w));
                }
                Check("static cache is pixel-identical: " + template.Name, mismatches == 0, mismatches + " of " + states.Length + " states differ; " + baked + " widgets cached");
            }

            // Waving flags: the cloth moves with time, and the runtime is told to redraw it.
            using (var waving = new LayoutRenderer(854, 480, DashboardSettings.CreateHalo(), _units))
            {
                TelemetrySnapshot green = IRacingTelemetrySource.CreatePreviewSnapshot(RacingFlag.Green);
                waving.FlagClock = 0;
                string first;
                int hashA = waving.StateHash(green, false, -1);
                using (var a = waving.Render(green, false, green.Gear, 1)) first = Pixels(a);
                waving.FlagClock = 700;
                int hashB = waving.StateHash(green, false, -1);
                using (var b = waving.Render(green, false, green.Gear, 1))
                    Check("waving flag moves over time and asks for a redraw", first != Pixels(b) && hashA != hashB);
            }

            foreach (WidgetType type in WidgetCatalog.All)
            {
                var layout = DesignTemplates.Blank();
                layout.Screens[0].Widgets.Add(WidgetCatalog.Create(type.Id, 100, 100));
                using (var renderer = new LayoutRenderer(854, 480, new DashboardSettings { Layout = Layouts.Custom, Design = layout }, _units))
                using (renderer.Render(IRacingTelemetrySource.CreatePreviewSnapshot(RacingFlag.Yellow), false, 4, 1, layout.Screens[0]))
                    Check("widget draws with defaults: " + type.Name, renderer.Errors.Count == 0, string.Join("; ", renderer.Errors.Values));
            }

            // ---------------- gauge shapes
            foreach (DesignBlock block in DesignBlocks.BuiltIn())
            {
                var layout = DesignTemplates.Blank();
                foreach (DashWidget w in block.Create()) { w.X += 100; w.Y += 60; layout.Screens[0].Widgets.Add(w); }
                var errors = new List<string>();
                using (var renderer = new LayoutRenderer(854, 480, new DashboardSettings { Layout = Layouts.Custom, Design = layout }, _units))
                    foreach (TelemetrySnapshot snapshot in new[] { IRacingTelemetrySource.CreatePreviewSnapshot(), ShiftSnapshot(), TelemetrySnapshot.Disconnected })
                        using (renderer.Render(snapshot, true, 4, 1, layout.Screens[0]))
                            errors.AddRange(renderer.Errors.Values);
                Check("block draws: " + block.Name, errors.Count == 0, string.Join("; ", errors.Distinct().Take(3)));
            }
            Check("every built-in block is in a folder", DesignBlocks.BuiltIn().All(b => b.Folder != "Other"));

            // ---------------- flip and rotate
            System.Drawing.Color Sample(DashWidget w, int x, int y, bool cache = true)
            {
                var layout = DesignTemplates.Blank();
                layout.Theme.BackgroundStyle = "Solid";
                layout.Theme.Background = "#000000";
                layout.Screens[0].Widgets.Add(w);
                using (var renderer = new LayoutRenderer(854, 480, new DashboardSettings { Layout = Layouts.Custom, Design = layout }, _units) { CacheStatic = cache })
                using (System.Drawing.Bitmap frame = renderer.Render(IRacingTelemetrySource.CreatePreviewSnapshot(), false, 4, 1, layout.Screens[0]))
                    return frame.GetPixel(x, y);
            }
            DashWidget FillBar(bool flip, string rotate = "0")
            {
                DashWidget w = WidgetCatalog.Create("bar", 100, 100);
                w.W = 400; w.H = 40;
                w.Set("value", "0.3"); w.Set("color", "#FF0000"); w.Set("track", "#00000000"); w.Set("flipX", flip ? "true" : "false"); w.Set("rotate", rotate);
                return w;
            }
            Check("flipped bar fills from the other side",
                Sample(FillBar(false), 110, 120).R > 200 && Sample(FillBar(false), 490, 120).R < 50 && Sample(FillBar(true), 490, 120).R > 200 && Sample(FillBar(true), 110, 120).R < 50);
            Check("rotated bar turns about its middle", Sample(FillBar(false, "-90"), 300, 290).R > 200 && Sample(FillBar(false, "-90"), 110, 120).R < 50);
            DashWidget turnedPanel = WidgetCatalog.Create("panel", 300, 150);
            turnedPanel.Set("rotate", "30");
            turnedPanel.Set("opacity", "60");
            Check("rotated widget is cached without clipping", Sample(turnedPanel, 330, 140).ToArgb() == Sample(SettingsStore.Clone(turnedPanel), 330, 140, false).ToArgb());

            var box = new System.Drawing.RectangleF(10, 20, 400, 100);
            BarWarp Warp(double bend, double peak, double taper, double bulge, double skew, double thick, string anchor, bool vertical = false) =>
                new BarWarp(box, vertical, bend, peak, taper, bulge, skew, thick, anchor);
            float Across(BarWarp w, float x)
            {
                System.Drawing.PointF a = w.Map(x, 0), b = w.Map(x, w.Thickness);
                return (float)Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
            }
            BarWarp flatBar = Warp(0, 0.5, 0, 0, 0, 1, "Middle");
            Check("plain bar is not warped", flatBar.IsStraight && flatBar.Map(0, 0) == new System.Drawing.PointF(10, 20) && flatBar.Map(400, 100) == new System.Drawing.PointF(410, 120));
            BarWarp upright = Warp(0, 0.5, 0, 0, 0, 1, "Middle", true);
            Check("vertical bar starts at the bottom", upright.Map(0, 0) == new System.Drawing.PointF(10, 120) && upright.Map(upright.Length, 0) == new System.Drawing.PointF(10, 20));
            bool inside = true;
            foreach (BarWarp w in new[] { Warp(1, 0.5, 0, 0, 0, 0.3, "Middle"), Warp(-0.7, 1, 0.9, 0, 0, 0.5, "Middle"), Warp(0.4, 0, -0.5, 0.8, 30, 0.6, "Bottom edge"),
                Warp(0, 0.5, 0, -1, -45, 1, "Top edge"), Warp(0.8, 0.3, 0.6, 0.5, 15, 1, "Middle", true) })
                for (int i = 0; i <= 40; i++)
                    foreach (float y in new[] { 0f, w.Thickness })
                    {
                        System.Drawing.PointF p = w.Map(w.Length * i / 40, y);
                        if (p.X < box.X - 1 || p.X > box.Right + 1 || p.Y < box.Y - 1 || p.Y > box.Bottom + 1) inside = false;
                    }
            Check("bent, tapered and slanted bars stay inside their box", inside);
            BarWarp tapered = Warp(0, 0.5, 0.8, 0, 0, 1, "Middle");
            Check("taper starts thin and ends full", Across(tapered, 0) < Across(tapered, tapered.Length) * 0.4f);
            BarWarp swoosh = Warp(0.6, 1, 0, 0, 0, 0.3, "Middle");
            Check("bend peaking at the end rises toward the end", swoosh.Map(swoosh.Length, swoosh.Thickness / 2).Y < swoosh.Map(0, swoosh.Thickness / 2).Y - 30);
            BarWarp arched = Warp(0, 0.5, 0, 1, 0, 1, "Bottom edge");
            Check("bulge from the bottom edge keeps the base flat and arches the top",
                Math.Abs(arched.Map(0, arched.Thickness).Y - arched.Map(arched.Length / 2, arched.Thickness).Y) < 0.5f && Across(arched, arched.Length / 2) > Across(arched, 0) * 1.8f);
            BarWarp slanted = Warp(0, 0.5, 0, 0, 25, 1, "Middle");
            Check("slant leans the top forward", slanted.Map(0, 0).X > slanted.Map(0, slanted.Thickness).X + 20);
            var ring = new ArcRing { Center = new System.Drawing.PointF(150, 150), RadiusX = 120, RadiusY = 80, Thickness = 20, Taper = 0.8f, Start = 135, Sweep = 270 };
            Check("tapered ring grows along its sweep", !ring.Classic && ring.ThicknessAt(135) < 6 && Math.Abs(ring.ThicknessAt(405) - 20) < 0.01f);
            System.Drawing.RectangleF ringBounds;
            using (var band = ring.Band(135, 270, true)) ringBounds = band.GetBounds();
            Check("oval ring follows its box", ringBounds.Width > ringBounds.Height * 1.3f && ringBounds.Right < 150 + 120 + 11 && ringBounds.Bottom < 150 + 80 + 11);

            // ---------------- editing operations
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

            // ---------------- screens
            int screens = _layout.Screens.Count;
            AddScreen(true, "Pit lane", "OnPitRoad");
            Check("add conditional screen", _layout.Screens.Count == screens + 1 && _screen.Condition == "OnPitRoad");
            using (var renderer = new LayoutRenderer(854, 480, new DashboardSettings { Layout = Layouts.Custom, Design = _layout }, _units))
            {
                Check("conditional screen wins on pit road", renderer.ActiveScreen(renderer.Scope(IRacingTelemetrySource.CreatePitLanePreviewSnapshot(), false)).Name == "Pit lane");
                Check("main screen otherwise", renderer.ActiveScreen(renderer.Scope(IRacingTelemetrySource.CreatePreviewSnapshot(), false)).Name != "Pit lane");
                int racing = renderer.StateHash(IRacingTelemetrySource.CreatePreviewSnapshot(), false, -1);
                Check("change detection is stable", racing == renderer.StateHash(IRacingTelemetrySource.CreatePreviewSnapshot(), false, -1));
                Check("change detection sees a new state", racing != renderer.StateHash(ShiftSnapshot(), false, -1));
            }
            DeleteScreen();
            Check("delete screen", _layout.Screens.Count == screens);

            // ---------------- save, reload, export, import
            SaveDesign(true);
            AppSettings reloaded = SettingsStore.Load();
            DashboardSettings again = reloaded.Dashboards.FirstOrDefault(d => d.Id == _dashboard.Id);
            Check("save and reload keeps the design", again?.Design != null && again.Design.AllWidgets().Count() == _layout.AllWidgets().Count(),
                again == null ? "dashboard missing" : again.Design.AllWidgets().Count() + " vs " + _layout.AllWidgets().Count());
            Check("reloaded design keeps formulas", again?.Design.AllWidgets().Any(w => w.Get("value") == "=format(Rpm, \"0\")") == true);
            using (IDashboardRenderer runtime = DashboardRendererFactory.Create(854, 480, again, _units))
                Check("runtime uses the designed renderer", runtime is LayoutRenderer);
            string file = Path.Combine(scratchFolder, "selftest.afodash");
            DesignPackage.Write(file, new DesignPackage { Name = "Self test", AccentColor = "#FF0000", Design = _layout });
            DesignPackage package = DesignPackage.Read(file);
            Check("export and import", package.Design.AllWidgets().Count() == _layout.AllWidgets().Count() && package.AccentColor == "#FF0000");
            Check("dirty flag clears after save", !Dirty);

            // ---------------- light layers
            foreach (string line in LightsSelfTest.Run(SettingsStore.Load()))
            {
                if (line.StartsWith("FAIL")) failures++;
                report.Add(line);
            }

            int checks = report.Count(line => !line.StartsWith("INFO"));
            report.Insert(0, failures == 0 ? "ALL " + checks + " CHECKS PASSED" : failures + " OF " + checks + " CHECKS FAILED");
            return report;
        }
    }
}
