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

            // ---------------- standings overlay
            Check("strength of field: equal ratings give that rating", StandingsBuilder.StrengthOfField(new[] { 2000, 2000, 2000 }) == 2000);
            int sof = StandingsBuilder.StrengthOfField(new[] { 1000, 3000 });
            Check("strength of field leans to the lower rating like iRacing", sof > 1000 && sof < 2000, sof.ToString());
            StandingsSnapshot field = StandingsSample.Create();
            Check("sample field: classes fastest first, player in GT3", field.Classes.Select(c => c.Name).SequenceEqual(new[] { "GTP", "LMP2", "GT3" }) && field.PlayerClass?.Name == "GT3");
            Check("class fastest lap is marked once per class", field.Classes.All(c => c.Entries.Count(e => e.IsClassFastest) == 1));
            Check("car makes are recognised", StandingsBuilder.Make("Porsche 911 GT3 R (992)") == "POR" && StandingsBuilder.Make("Mercedes-AMG GT3 2020") == "MER" && StandingsBuilder.Make("Dallara P217") == "DAL");
            Check("iRacing colours parse", StandingsBuilder.ParseIRacingColor("0xffda59", System.Drawing.Color.Black).ToArgb() == System.Drawing.Color.FromArgb(255, 218, 89).ToArgb());
            var errorsInView = new List<string>();
            var standingsView = new StandingsView();
            foreach (string combo in new[] { "", "colGained,colInterval", "colLicense,colMake,colBest", "header,classHeaders" })
            {
                var config = new OverlayConfig();
                foreach (string key in combo.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) config.Set(key, config.Bool(key) ? "false" : "true");
                for (int seed = 0; seed < 4; seed++)
                {
                    try { standingsView.Update(StandingsSample.Create(seed), config); }
                    catch (Exception exception) { errorsInView.Add(exception.Message); }
                }
            }
            try { standingsView.Update(StandingsSnapshot.Empty, new OverlayConfig()); } catch (Exception exception) { errorsInView.Add(exception.Message); }
            Check("standings view updates with any columns, shuffles and no data", errorsInView.Count == 0, string.Join("; ", errorsInView.Take(2)));
            var overlaySettings = new OverlaySettings();
            overlaySettings.Normalize();
            Check("overlay settings default to a standings overlay", overlaySettings.Find("standings") != null && overlaySettings.Find("standings").Int("aheadBehind") == 3);
            Check("relative and toasts overlays are added", overlaySettings.Find("relative")?.Type == "relative" && overlaySettings.Find("toasts")?.Type == "toasts");

            // Column order and headers.
            OverlayConfig ordered = overlaySettings.Find("standings");
            ordered.Set("columnOrder", "colBest,fixed-hdrPos,fixed-hdrName");
            var orderedKeys = OverlayTypes.Find("standings").OrderedColumns(ordered).Select(o => o.Key).ToList();
            Check("column order follows the saved order, others after", orderedKeys.Take(3).SequenceEqual(new[] { "colBest", "fixed-hdrPos", "fixed-hdrName" }) && orderedKeys.Count == 10, string.Join(",", orderedKeys));
            ordered.Set("hdrBest", "true");
            errorsInView.Clear();
            try { standingsView.Update(StandingsSample.Create(), ordered); } catch (Exception exception) { errorsInView.Add(exception.Message); }
            Check("standings draws reordered columns with headers", errorsInView.Count == 0, string.Join("; ", errorsInView));

            // Relative.
            OverlayConfig relativeConfig = overlaySettings.Find("relative");
            var relativeView = new StandingsView();
            errorsInView.Clear();
            for (int seed = 0; seed < 6; seed++)
                try { relativeView.Update(StandingsSample.Create(seed), relativeConfig); } catch (Exception exception) { errorsInView.Add(exception.Message); }
            foreach (string key in new[] { "colNumber", "colPosition", "colLast", "hdrGap", "hdrName" })
            {
                relativeConfig.Set(key, relativeConfig.Bool(key) ? "false" : "true");
                try { relativeView.Update(StandingsSample.Create(), relativeConfig); } catch (Exception exception) { errorsInView.Add(exception.Message); }
            }
            Check("relative updates as the field moves and with any columns", errorsInView.Count == 0, string.Join("; ", errorsInView.Take(2)));

            // Toasts: a pass, being passed, and contact.
            var tracker = new ToastTracker();
            OverlayConfig toastConfig = overlaySettings.Find("toasts");
            StandingsSnapshot race = StandingsSample.Create();
            TelemetrySnapshot connected = IRacingTelemetrySource.CreatePreviewSnapshot();
            DateTime clock = new DateTime(2026, 1, 1);
            var seen = new List<Toast>();
            void Step(double seconds) { clock = clock.AddSeconds(seconds); seen.AddRange(tracker.Update(connected, race, toastConfig, clock)); }
            Step(0);
            StandingsClass gt3 = race.PlayerClass;
            StandingsEntry player = gt3.Entries.First(e => e.IsPlayer), victim = gt3.Entries.First(e => e.ClassPosition == player.ClassPosition - 1);
            int was = player.ClassPosition;
            player.ClassPosition = was - 1;
            victim.ClassPosition = was;
            Step(0.1);
            Step(0.5);
            Check("no toast until a position change settles", seen.Count == 0);
            Step(0.5);
            Check("passing a car makes a green toast naming it", seen.Count == 1 && seen[0].Badge == "▲ 1" && seen[0].Title == "P" + (was - 1) && seen[0].Detail == "Passed " + victim.Name,
                seen.Count == 0 ? "none" : seen[0].Badge + " " + seen[0].Title + " " + seen[0].Detail);
            player.ClassPosition = was;
            victim.ClassPosition = was - 1;
            Step(0.1);
            Step(1);
            Check("being passed makes a red toast", seen.Count == 2 && seen[1].Badge == "▼ 1" && seen[1].Detail == "Passed by " + victim.Name);
            int before = connected.IncidentPoints;
            TelemetrySnapshot hit = IRacingTelemetrySource.CreatePreviewSnapshot();
            typeof(TelemetrySnapshot).GetField("<IncidentPoints>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(hit, before + 4);
            if (hit.IncidentPoints == before + 4)
            {
                connected = hit;
                Step(0.1);
                Step(1.5);
                Check("4x incident makes a contact toast with the nearest driver", seen.Count == 3 && seen[2].Badge == "4x" && seen[2].Title.StartsWith("Contact w/ "),
                    seen.Count < 3 ? "none" : seen[2].Badge + " " + seen[2].Title);
            }
            else report.Add("INFO  incident toast not checked: incident count can't be set on a sample");

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
