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
        private static readonly DateTime TestNow = new DateTime(2026, 1, 1, 12, 0, 0);

        /// <summary>The first control of a type inside a (not yet shown) piece of UI.</summary>
        private static T FindChild<T>(System.Windows.DependencyObject root) where T : System.Windows.DependencyObject
        {
            foreach (object child in System.Windows.LogicalTreeHelper.GetChildren(root))
            {
                if (child is T found) return found;
                if (child is System.Windows.DependencyObject inner && FindChild<T>(inner) is T deeper) return deeper;
            }
            return null;
        }

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
            DashLayout halo = DesignTemplates.Road.Create();
            var costs = new List<string>();
            foreach (DashWidget widget in halo.Screens[0].Widgets.Concat(new DashWidget[] { null }))
            {
                DashLayout single = DesignTemplates.Road.Create();
                single.Screens[0].Widgets = widget == null ? new List<DashWidget>() : new List<DashWidget> { widget };
                using (var renderer = new LayoutRenderer(854, 480, new DashboardSettings { Layout = Layouts.Custom, Design = single, AccentColor = "#22D3EE" }, _units))
                    costs.Add((widget?.Name ?? "background only") + " " + Time(renderer).ToString("0.0"));
            }
            report.Add("INFO  halo cost by widget (ms): " + string.Join(", ", costs));
            using (var profiled = new LayoutRenderer(854, 480, DashboardSettings.CreateDesigned(DesignTemplates.Road), _units) { Timings = new Dictionary<string, double>() })
            {
                TelemetrySnapshot snapshot = IRacingTelemetrySource.CreatePreviewSnapshot();
                using (profiled.Render(snapshot, false, 4, 1)) { }
                profiled.Timings.Clear();
                var watch = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < 30; i++) using (profiled.Render(snapshot, false, 4, 1)) { }
                double frame = watch.Elapsed.TotalMilliseconds / 30, widgets = profiled.Timings.Values.Sum() / 30;
                report.Add("INFO  halo frame " + frame.ToString("0.00") + " ms: live widgets " + widgets.ToString("0.00") + " ms over " + profiled.Timings.Count + " widgets; slowest " +
                    string.Join(", ", profiled.Timings.OrderByDescending(p => p.Value).Take(8).Select(p => p.Key + " " + (p.Value / 30).ToString("0.00"))));
            }

            using (var designed = new LayoutRenderer(854, 480, DashboardSettings.CreateDesigned(DesignTemplates.Road), _units))
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
                int mismatches = 0, baked = 0, worst = 0;
                // Waving flags follow the clock; hold both renderers at the same moment.
                using (var cached = new LayoutRenderer(854, 480, d, _units) { FlagClock = 0, FixedNow = TestNow })
                using (var plain = new LayoutRenderer(854, 480, d, _units) { CacheStatic = false, FlagClock = 0, FixedNow = TestNow })
                    foreach (TelemetrySnapshot snapshot in states)
                        using (var a = cached.Render(snapshot, false, snapshot.Gear, 1))
                        using (var b = plain.Render(snapshot, false, snapshot.Gear, 1))
                            if (Pixels(a) != Pixels(b))
                            {
                                // Widgets kept as finished pictures can round an edge pixel by 1/255; more is a real difference.
                                int most = Largest(a, b);
                                worst = Math.Max(worst, most);
                                if (most > 2) mismatches++;
                            }
                using (var counter = new LayoutRenderer(854, 480, d, _units))
                {
                    using (counter.Render(states[0], false, 4, 1)) { }
                    baked = d.Design.Screens[0].Widgets.Count(w => counter.IsBaked(d.Design.Screens[0], w));
                }
                Check("static cache matches a plain render: " + template.Name, mismatches == 0, mismatches + " of " + states.Length + " states differ beyond rounding (largest " + worst + "/255); " + baked + " widgets baked");
            }

            // The largest difference in any colour channel between two frames.
            int Largest(System.Drawing.Bitmap x, System.Drawing.Bitmap y)
            {
                int most = 0;
                for (int py = 0; py < x.Height; py++)
                    for (int px = 0; px < x.Width; px++)
                    {
                        System.Drawing.Color p = x.GetPixel(px, py), q = y.GetPixel(px, py);
                        most = Math.Max(most, Math.Max(Math.Abs(p.A - q.A), Math.Max(Math.Abs(p.R - q.R), Math.Max(Math.Abs(p.G - q.G), Math.Abs(p.B - q.B)))));
                    }
                return most;
            }

            // Where two frames differ, and which widgets cover that spot.
            string DiffBox(System.Drawing.Bitmap x, System.Drawing.Bitmap y)
            {
                int l = int.MaxValue, t = int.MaxValue, r = -1, btm = -1, count = 0, most = 0;
                for (int py = 0; py < x.Height; py += 1)
                    for (int px = 0; px < x.Width; px += 1)
                    {
                        System.Drawing.Color p = x.GetPixel(px, py), q = y.GetPixel(px, py);
                        if (p.ToArgb() == q.ToArgb()) continue;
                        count++;
                        most = Math.Max(most, Math.Max(Math.Abs(p.R - q.R), Math.Max(Math.Abs(p.G - q.G), Math.Abs(p.B - q.B))));
                        l = Math.Min(l, px); t = Math.Min(t, py); r = Math.Max(r, px); btm = Math.Max(btm, py);
                    }
                return count + " px within " + l + "," + t + " to " + r + "," + btm + " (largest difference " + most + "/255)";
            }

            // Redrawing only what changed must give exactly the frames a full redraw gives, across an animated lap:
            // on every template, and on one with the Style effects on moving and still widgets.
            DashboardSettings styled = DashboardSettings.CreateDesigned(DesignTemplates.Find("apex"));
            void Style(string name, params string[] props)
            {
                DashWidget w = styled.Design.Screens[0].Widgets.First(x => x.Name == name);
                for (int k = 0; k + 1 < props.Length; k += 2) w.Set(props[k], props[k + 1]);
            }
            Style("Gear", "fxShadow", "8", "fxShadowY", "6", "fxFill", "Gradient", "fxFillColor", "#FFFFFF", "fxFillColor2", "accent");
            Style("Speed unit", "fxOutline", "2", "fxOutlineColor", "#000000");
            Style("Rev ring", "fxGlow", "12", "fxGlowColor", "accent");
            Style("Last lap tile", "fxFill", "Color", "fxFillColor", "purple", "fxFillAmount", "40");
            Style("Delta", "fxShadow", "4", "fxShadowX", "-3", "rotate", "8");
            Style("Speed", "fxOutline", "1.5", "fxGlow", "6", "opacity", "70");
            var lapDesigns = DesignTemplates.All.Select(t => Tuple.Create(t.Name, t.Id, DashboardSettings.CreateDesigned(t))).ToList();
            lapDesigns.Add(Tuple.Create("Style effects", "styled", styled));
            foreach (var template in lapDesigns.Select(e => new { Name = e.Item1, Id = e.Item2, Settings = e.Item3 }))
            {
                DashboardSettings d = template.Settings;
                d.Design.FullScreenFlags = false;
                var lap = new SyntheticTelemetry();
                int mismatches = 0, frames = 0, rounded = 0;
                double share = 0, fast = 0, slow = 0;
                string firstDiff = "", changed = "", costly = "";
                using (var incremental = new LayoutRenderer(854, 480, d, _units) { FlagClock = 0, FixedNow = TestNow, Timings = new Dictionary<string, double>() })
                using (var whole = new LayoutRenderer(854, 480, d, _units) { FlagClock = 0, FixedNow = TestNow, RedrawChangesOnly = false, Timings = new Dictionary<string, double>() })
                    for (int i = 0; i < 90; i++)
                    {
                        TelemetrySnapshot snapshot = i % 30 == 29 ? states[i / 30 % states.Length] : lap.Next(out _, out _, out _);
                        long a0 = System.Diagnostics.Stopwatch.GetTimestamp();
                        using (var a = incremental.Render(snapshot, i % 4 == 0, snapshot.Gear, 1))
                        {
                            long a1 = System.Diagnostics.Stopwatch.GetTimestamp();
                            using (var b = whole.Render(snapshot, i % 4 == 0, snapshot.Gear, 1))
                            {
                                long b1 = System.Diagnostics.Stopwatch.GetTimestamp();
                                if (i > 0) { fast += a1 - a0; slow += b1 - a1; share += incremental.LastRedrawShare; frames++; }
                                if (i == 89) changed = string.Join(", ", incremental.Timings.Where(p => p.Key.StartsWith("changed: ")).OrderByDescending(p => p.Value).Take(14).Select(p => p.Key.Substring(9) + " " + p.Value));
                                if (i == 89) costly = string.Join(", ", whole.Timings.Where(p => !p.Key.StartsWith("changed: ")).OrderByDescending(p => p.Value).Take(6).Select(p => p.Key + " " + (p.Value / 90).ToString("0.00")));
                                // Scaled glows can round 1/255 differently when clipped; anything larger is a real difference.
                                if (Pixels(a) != Pixels(b) && Largest(a, b) <= 2) rounded++;
                                else if (Pixels(a) != Pixels(b))
                                {
                                    if (mismatches++ == 0)
                                    {
                                        firstDiff = "frame " + i + " differs in " + DiffBox(a, b);
                                        string[] at = firstDiff.Split(new[] { " within " }, StringSplitOptions.None)[1].Split(' ')[0].Split(',');
                                        var spot = new System.Drawing.PointF(float.Parse(at[0]), float.Parse(at[1]));
                                        firstDiff += "; widgets there: " + string.Join(", ", d.Design.Screens[0].Widgets
                                            .Where(w => System.Drawing.RectangleF.Inflate(w.Bounds, 24, 24).Contains(spot))
                                            .Select(w => w.Name + " [" + w.Type + " " + w.X + "," + w.Y + " " + w.W + "x" + w.H + "]"));
                                        using (var map = new System.Drawing.Bitmap(a))
                                        {
                                            for (int py = 0; py < a.Height; py++)
                                                for (int px = 0; px < a.Width; px++)
                                                    if (a.GetPixel(px, py).ToArgb() != b.GetPixel(px, py).ToArgb()) map.SetPixel(px, py, System.Drawing.Color.Magenta);
                                            map.Save(Path.Combine(scratchFolder, "diff-" + template.Id + ".png"));
                                        }
                                    }
                                }
                            }
                        }
                    }
                Check("redrawing only changes matches a full redraw: " + template.Name, mismatches == 0, mismatches + " of 90 frames differ" + (rounded > 0 ? ", " + rounded + " within 2/255 rounding" : "") + "; " + firstDiff);
                if (frames > 0)
                    report.Add("INFO  " + template.Id + " animated lap: " + (fast / frames * 1000 / System.Diagnostics.Stopwatch.Frequency).ToString("0.00") + " ms per frame redrawing changes (" +
                        (share / frames * 100).ToString("0") + "% of the screen) vs " + (slow / frames * 1000 / System.Diagnostics.Stopwatch.Frequency).ToString("0.00") + " ms redrawing everything; changed most: " + changed + "; costliest (ms/frame): " + costly);
            }

            // Waving flags: the cloth moves with time, and the runtime is told to redraw it.
            using (var waving = new LayoutRenderer(854, 480, DashboardSettings.CreateRoad(), _units))
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

            // ---------------- cost of glowing gauges whose value changes every frame
            foreach (string block in new[] { "Swoosh rev bar", "Tapered rev ring", "Tach dial", "Arched rev bar" })
            {
                var layout = DesignTemplates.Blank();
                List<DashWidget> parts = DesignBlocks.BuiltIn().First(b => b.Name == block).Create();
                DashWidget gauge = parts[0];
                foreach (DashWidget w in parts) { w.X += 100; w.Y += 60; layout.Screens[0].Widgets.Add(w); }
                using (var renderer = new LayoutRenderer(854, 480, new DashboardSettings { Layout = Layouts.Custom, Design = layout }, _units))
                {
                    TelemetrySnapshot snapshot = IRacingTelemetrySource.CreatePreviewSnapshot();
                    gauge.Set("value", "0.5");
                    using (renderer.Render(snapshot, false, 4, 1, layout.Screens[0])) { }
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    for (int i = 0; i < 120; i++)
                    {
                        gauge.Set("value", ((i * 7 % 100) / 100.0).ToString(System.Globalization.CultureInfo.InvariantCulture) + (gauge.Type == "arc" ? "" : ""));
                        if (gauge.Get("max") != null && gauge.Type == "arc" && block == "Tach dial") gauge.Set("value", (i * 7 % 90 / 10.0).ToString(System.Globalization.CultureInfo.InvariantCulture));
                        using (renderer.Render(snapshot, false, 4, 1, layout.Screens[0])) { }
                    }
                    report.Add("INFO  " + block + " with a changing value: " + (watch.Elapsed.TotalMilliseconds / 120).ToString("0.00") + " ms per frame");
                }
            }

            // ---------------- quick values and race data
            var quickErrors = new List<string>();
            foreach (DesignBlock quick in QuickValues.All())
            {
                var layout = DesignTemplates.Blank();
                foreach (DashWidget w in quick.Create()) { w.X += 100; w.Y += 60; layout.Screens[0].Widgets.Add(w); }
                using (var renderer = new LayoutRenderer(854, 480, new DashboardSettings { Layout = Layouts.Custom, Design = layout }, _units))
                    foreach (TelemetrySnapshot snapshot in new[] { IRacingTelemetrySource.CreatePreviewSnapshot(), TelemetrySnapshot.Disconnected })
                        using (renderer.Render(snapshot, false, 4, 1, layout.Screens[0]))
                            quickErrors.AddRange(renderer.Errors.Values.Select(e => quick.Name + ": " + e));
                if (!QuickValues.Folders.Contains(quick.Folder)) quickErrors.Add(quick.Name + " is in an unknown folder");
            }
            Check("every quick value draws (" + QuickValues.All().Count() + ")", quickErrors.Count == 0, string.Join("; ", quickErrors.Take(3)));
            string Eval(string formula) => Expr.Text(Expr.Evaluate(formula, scope));
            Check("leaderboard formulas", Eval("standing(1, \"Name\")") == "L. HARTMANN" && Eval("standing(Position - 1, \"Number\")") == "05" &&
                Eval("standing(99, \"Exists\")") == "false" && Eval("standing(8, \"GapText\")") == "+1 LAP" && Eval("GapToLeader") == "+5.94", Eval("standing(8, \"GapText\")"));
            Check("sector formulas", Eval("seconds(sectortime(1))") == "29.904" && Eval("seconds(sectortime(2))") == "--.---" && Eval("laptime(IdealLap)") == "1:27.373" && Eval("Sector") == "2");
            Check("engine, tyre and system values", Eval("format(OilTemp, \"0\")") == (_units.Celsius ? "104" : "219") && Eval("format(tyre(\"RF\", \"Wear\"), \"0\")") == "88" &&
                Eval("DrsText") == "OFF" && Expr.Num(Expr.Evaluate("CpuLoad", scope)) >= 0 && Expr.Num(Expr.Evaluate("MemoryLoad", scope)) > 0, Eval("format(OilTemp, \"0\")"));
            Check("unknown engine values show as --", Expr.Text(Expr.Evaluate("format(OilTemp, \"0\")", new EvalScope { T = TelemetrySnapshot.Disconnected, Units = _units, Theme = _layout.Theme })) == "--");

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
            // ---------------- fill directions
            foreach (string mode in new[] { "Fill", "Segments" })
            {
                DashWidget Directed(string order)
                {
                    DashWidget w = FillBar(false);
                    w.Set("fill", order);
                    w.Set("mode", mode);
                    w.Set("value", "0.4");
                    w.Set("bend", "1");
                    return w;
                }
                // The bar runs 100..500 across; at 40% each direction lights a different part of it.
                bool Lit(string order, int x) => Sample(Directed(order), x, 120).R > 200;
                Check("fill directions light the right parts (" + mode + ")",
                    Lit(FillOrder.StartToEnd, 110) && !Lit(FillOrder.StartToEnd, 490) &&
                    Lit(FillOrder.EndToStart, 490) && !Lit(FillOrder.EndToStart, 110) &&
                    Lit(FillOrder.EdgesToCenter, 110) && Lit(FillOrder.EdgesToCenter, 490) && !Lit(FillOrder.EdgesToCenter, 295) &&
                    Lit(FillOrder.CenterOut, 295) && !Lit(FillOrder.CenterOut, 110) && !Lit(FillOrder.CenterOut, 490));
            }
            var oldBar = new DashWidget { Type = "bar", Props = { ["reverse"] = "true" } };
            var oldLights = new DashWidget { Type = "shiftlights", Props = { ["pattern"] = "Center out" } };
            oldBar.Normalize();
            oldLights.Normalize();
            Check("older fill settings carry over", oldBar.Get("fill") == FillOrder.EndToStart && !oldBar.Props.ContainsKey("reverse") && oldLights.Get("fill") == FillOrder.CenterOut);

            DashWidget turnedPanel =WidgetCatalog.Create("panel", 300, 150);
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

            // ---------------- graph and track map
            {
                var layout = DesignTemplates.Blank();
                DashWidget graph = WidgetCatalog.Create("graph", 100, 100);
                graph.Set("value", "=Rpm");
                graph.Set("seconds", "4");
                layout.Screens[0].Widgets.Add(graph);
                using (var renderer = new LayoutRenderer(854, 480, new DashboardSettings { Layout = Layouts.Custom, Design = layout }, _units))
                {
                    var lap = new SyntheticTelemetry();
                    string first = null, last = null;
                    for (int i = 0; i < 60; i++)
                    {
                        renderer.FixedNow = TestNow.AddMilliseconds(i * 50);
                        TelemetrySnapshot snapshot = lap.Next(out _, out _, out _);
                        renderer.StateHash(snapshot, false, -1);
                        using (System.Drawing.Bitmap frame = renderer.Render(snapshot, false, snapshot.Gear, 1, layout.Screens[0]))
                        {
                            if (i == 5) first = Pixels(frame);
                            last = Pixels(frame);
                        }
                    }
                    GraphHistory history = renderer.Graph(new WidgetContext(renderer, null, renderer.Scope(TelemetrySnapshot.Disconnected, false), graph, WidgetCatalog.Find("graph")));
                    Check("graph samples on a steady clock and scrolls", history.Count >= 55 && history.Count <= 61 && first != last, history.Count + " samples");
                }
                var lapFractions = new List<double>(); var xs = new List<double>(); var ys = new List<double>();
                for (int i = 0; i <= 1000; i++)
                {
                    double f = i / 1000.0 * 0.999, a = f * 2 * Math.PI;
                    lapFractions.Add(f); xs.Add(Math.Cos(a) * 300 + 3 * f); ys.Add(Math.Sin(a) * 150 + 2 * f);
                }
                TrackMap traced = TrackMap.FromLap("test", "Test", lapFractions, xs, ys);
                Check("a clean lap becomes a closed track map", traced != null && traced.Valid && Math.Abs(traced.At(0.25).Y - 150) < 6 && Math.Abs(traced.At(0).X - 300) < 1);
                lapFractions.RemoveRange(400, 100); xs.RemoveRange(400, 100); ys.RemoveRange(400, 100);
                Check("a lap with a gap makes no map", TrackMap.FromLap("test", "Test", lapFractions, xs, ys) == null);
            }

            // ---------------- Style effects on a single widget
            {
                var layout = DesignTemplates.Blank();
                layout.Theme.BackgroundStyle = "Solid";
                layout.Theme.Background = "#000000";
                System.Drawing.Bitmap Frame(params string[] props)
                {
                    DashWidget w = WidgetCatalog.Create("text", 300, 200);
                    w.W = 200; w.H = 60;
                    w.Set("text", "88"); w.Set("size", "48"); w.Set("color", "#FFFFFF");
                    for (int k = 0; k + 1 < props.Length; k += 2) w.Set(props[k], props[k + 1]);
                    layout.Screens[0].Widgets.Clear();
                    layout.Screens[0].Widgets.Add(w);
                    using (var renderer = new LayoutRenderer(854, 480, new DashboardSettings { Layout = Layouts.Custom, Design = layout }, _units))
                        return renderer.Render(IRacingTelemetrySource.CreatePreviewSnapshot(), false, 4, 1, layout.Screens[0]);
                }
                bool LitOutside(System.Drawing.Bitmap f) => Enumerable.Range(0, 40).Any(k => f.GetPixel(290 + k * 7, 196).ToArgb() != System.Drawing.Color.Black.ToArgb() || f.GetPixel(290 + k * 7, 266).ToArgb() != System.Drawing.Color.Black.ToArgb());
                using (System.Drawing.Bitmap plainText = Frame())
                using (System.Drawing.Bitmap shadow = Frame("fxShadow", "10", "fxShadowY", "30", "fxShadowColor", "#FF00A0FF"))
                using (System.Drawing.Bitmap glow = Frame("fxGlow", "24", "fxGlowColor", "#FF0000"))
                using (System.Drawing.Bitmap outline = Frame("fxOutline", "3", "fxOutlineColor", "#00FF00"))
                using (System.Drawing.Bitmap recolor = Frame("fxFill", "Color", "fxFillColor", "#FF0000"))
                {
                    Check("plain text stays inside its box", !LitOutside(plainText));
                    Check("a long shadow and a wide glow reach past the widget's box", LitOutside(shadow) && LitOutside(glow));
                    Check("every effect changes the picture", Pixels(shadow) != Pixels(plainText) && Pixels(glow) != Pixels(plainText) && Pixels(outline) != Pixels(plainText) && Pixels(recolor) != Pixels(plainText));
                    // Recolor paints the digits red but leaves the gaps between them untouched.
                    int red = 0, other = 0;
                    for (int py = 200; py < 260; py += 2)
                        for (int px = 300; px < 500; px += 2)
                        {
                            System.Drawing.Color p = recolor.GetPixel(px, py);
                            if (p.R > 200 && p.G < 60 && p.B < 60) red++;
                            else if (p.ToArgb() != System.Drawing.Color.Black.ToArgb() && p.G > 120) other++;
                        }
                    Check("recolor paints only the widget's own pixels", red > 50 && other == 0, red + " red, " + other + " other");
                    Check("outline is drawn in its colour", Enumerable.Range(0, 200).Any(k => outline.GetPixel(300 + k, 230).G > 200 && outline.GetPixel(300 + k, 230).R < 80));
                }
            }

            // ---------------- groups, slots, style copying and editing together
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
                Check("copying a group makes the next Slot, just below", next != null && next.Count == 2 && next.All(w => w.Slot == 2 && w.Group != name.Group) && next.Min(w => w.Y) > plate.Y + plate.H);
                var groupLayout = DesignTemplates.Blank();
                groupLayout.Screens[0].Widgets.AddRange(new[] { name }.Concat(next.Where(w => w.Type == "text")));
                using (var renderer = new LayoutRenderer(854, 480, new DashboardSettings { Layout = Layouts.Custom, Design = groupLayout }, _units))
                {
                    EvalScope s = renderer.Scope(IRacingTelemetrySource.CreatePreviewSnapshot(), false);
                    Check("Slot reads each group's own number", Expr.Text(Expr.Evaluate("standing(Slot, \"Name\")", LayoutRenderer.ForWidget(s, name))) == "L. HARTMANN" &&
                        Expr.Text(Expr.Evaluate("standing(Slot, \"Name\")", LayoutRenderer.ForWidget(s, next.First(w => w.Type == "text")))) == "S. OKAFOR");
                }
                // Copy and paste style.
                name.Set("color", "#FF0000"); name.Set("fxGlow", "6");
                Select(name);
                CopyStyle();
                DashWidget other = next.First(w => w.Type == "text");
                Select(other);
                PasteStyle();
                Check("paste style copies colour and effects, not content", other.Get("color") == "#FF0000" && other.Get("fxGlow") == "6" && other.Get("text") == name.Get("text"));
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

            // ---------------- turned widgets on the canvas
            {
                DashWidget turned = WidgetCatalog.Create("panel", 300, 200);
                turned.W = 200;
                turned.H = 40;
                turned.Set("rotate", "90");
                _screen.Widgets.Add(turned);
                // Turned a quarter about (400, 220) it covers x 380-420, y 120-320.
                Check("a turned widget is picked where it is drawn", WidgetAt(new System.Windows.Point(400, 140)) == turned && WidgetAt(new System.Windows.Point(310, 220)) != turned);
                Select(turned);
                _originals = new Dictionary<DashWidget, System.Windows.Rect> { [turned] = RectOf(turned) };
                _handle = 3; // the right-hand handle, which now sits at the bottom
                DoResize(new System.Windows.Point(400, 360), true, false);
                // 40 longer, with the far end (the top, at y 120) left where it was.
                Check("a turned widget resizes from its own edges", turned.W == 240 && turned.H == 40 && Math.Abs(turned.X - 280) < 0.6 && Math.Abs(turned.Y - 220) < 0.6,
                    turned.X + "," + turned.Y + " " + turned.W + "x" + turned.H);
                _screen.Widgets.Remove(turned);
                Select(null);
            }

            // ---------------- Liquid Glass
            {
                // A striped backdrop, so the rim's bending shows.
                int sw = 400, sh = 300;
                var backdrop = new int[sw * sh];
                for (int i = 0; i < backdrop.Length; i++) backdrop[i] = (i % sw / 10 % 2 == 0) ? unchecked((int)0xFF203040) : unchecked((int)0xFF8090A0);
                using (var shape = new System.Drawing.Drawing2D.GraphicsPath())
                {
                    shape.AddRectangle(new System.Drawing.RectangleF(50, 50, 300, 200));
                    var settings = new GlassSettings { Frost = 0, Depth = 14, Refraction = 1, Shine = 0.7f, Dispersion = 0, Light = 270, Tint = System.Drawing.Color.FromArgb(0, 0, 0, 0) };
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    using (System.Drawing.Bitmap pane = LiquidGlass.Render(backdrop, new System.Drawing.Rectangle(0, 0, sw, sh), new System.Drawing.Rectangle(50, 50, 300, 200), shape, settings))
                    {
                        double ms = watch.Elapsed.TotalMilliseconds;
                        System.Drawing.Color top = pane.GetPixel(150, 1), bottom = pane.GetPixel(150, 198), middle = pane.GetPixel(150, 100), plain = pane.GetPixel(0, 100), mid = pane.GetPixel(150, 100);
                        Check("liquid glass lights the rim facing the light more than the far one", top.GetBrightness() > bottom.GetBrightness() && bottom.GetBrightness() > middle.GetBrightness() - 0.02,
                            top + " " + bottom + " " + middle);
                        // In the middle the backdrop shows straight through; at the rim it is pulled in from further inside.
                        int stripesInside = 0, stripesRim = 0;
                        for (int px = 1; px < 299; px++)
                        {
                            if (Math.Abs(pane.GetPixel(px, 100).R - pane.GetPixel(px - 1, 100).R) > 40) stripesInside++;
                            if (Math.Abs(pane.GetPixel(px, 4).R - pane.GetPixel(px - 1, 4).R) > 40) stripesRim++;
                        }
                        Check("liquid glass shows the backdrop through its middle and bends it at the rim", stripesInside >= 25 && plain.A == 255 && mid.A == 255,
                            "edges inside " + stripesInside + ", along the rim " + stripesRim);
                        report.Add("INFO  a 300x200 pane of Liquid Glass takes " + ms.ToString("0.0") + " ms to make (then it is kept)");
                    }
                }
            }

            {
                // A fixed widget beneath a pane shows through it, frosted.
                System.Drawing.Color Through(bool withShape)
                {
                    DashLayout layout = DesignTemplates.Blank();
                    layout.Theme.BackgroundStyle = "Solid";
                    layout.Theme.Background = "#000000";
                    if (withShape)
                    {
                        DashWidget square = WidgetCatalog.Create("shape", 100, 100);
                        square.W = 160; square.H = 160;
                        square.Set("fill", "#FF00FF");
                        layout.Screens[0].Widgets.Add(square);
                    }
                    DashWidget pane = WidgetCatalog.Create("panel", 50, 50);
                    pane.W = 300; pane.H = 250;
                    pane.Set("style", "Liquid");
                    layout.Screens[0].Widgets.Add(pane);
                    using (var renderer = new LayoutRenderer(854, 480, new DashboardSettings { Layout = Layouts.Custom, Design = layout, AccentColor = "#22D3EE" }, _units))
                    using (System.Drawing.Bitmap frame = renderer.Render(IRacingTelemetrySource.CreatePreviewSnapshot(), false, 4, 1))
                        return frame.GetPixel(180, 180);
                }
                System.Drawing.Color plainGlass = Through(false), overShape = Through(true);
                Check("a fixed widget beneath liquid glass shows through it", overShape.R > plainGlass.R + 40 && overShape.B > plainGlass.B + 40 && overShape.G < overShape.R - 30,
                    plainGlass + " / " + overShape);
            }

            // ---------------- lap falloff and the flagships
            {
                double[] stint = { 88.2, 87.5, 87.6, 87.9 };
                Check("lap falloff is the last lap against the stint's best", Math.Abs(Falloff.Total(stint) - 0.4) < 1e-9 && Falloff.Total(new[] { 88.0 }) < 0);
                Check("the falloff trend runs from the best lap on", Math.Abs(Falloff.PerLap(stint) - 0.2) < 1e-9 && Falloff.PerLap(new[] { 88.0, 87.0 }) == 0, Falloff.PerLap(stint).ToString("0.000"));

                var older = new AppSettings { SchemaVersion = 8 };
                DashboardSettings mine = DashboardSettings.CreateDesigned(DesignTemplates.Find("apex"));
                mine.Id = "dashboard-mine";
                mine.RaceTypes = new List<string> { Disciplines.Road, Disciplines.Oval };
                older.Dashboards.Add(mine);
                older.DefaultDashboardId = mine.Id;
                older.EnsureBuiltInDashboards();
                Check("an upgrade adds the flagships for road and oval and keeps the user's dashboards",
                    older.Dashboards.Count == 3 && older.Dashboards[0].Id == DashboardSettings.RoadId && older.Dashboards[1].Id == DashboardSettings.OvalId &&
                    older.Dashboards.Contains(mine) && mine.RaceTypes.Count == 0 && older.ResolveDashboard(Disciplines.Oval).Id == DashboardSettings.OvalId &&
                    older.GetDefaultDashboard().Id == DashboardSettings.RoadId);
                older.EnsureBuiltInDashboards();
                Check("the flagships are added once", older.Dashboards.Count == 3);

                var firstRoundy = new AppSettings { SchemaVersion = 9 };
                DashboardSettings boxed = DashboardSettings.CreateOval(), edited = DashboardSettings.CreateRoad();
                boxed.Design.Screens[0].Widgets.Add(new DashWidget { Type = "panel", Name = "Around panel" });
                boxed.Design.Screens[0].Widgets.Add(new DashWidget { Type = "panel", Name = "Falloff panel" });
                firstRoundy.Dashboards.Add(edited);
                firstRoundy.Dashboards.Add(boxed);
                firstRoundy.EnsureBuiltInDashboards();
                Check("the first Roundy takes the redesign", !boxed.Design.Screens[0].Widgets.Any(w => w.Name == "Around panel") && boxed.Design.Screens[0].Widgets.Any(w => w.Name == "Rev dial"));
            }

            // ---------------- several widgets resized together
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
                Check("several widgets scale together", a.X == 100 && a.W == 200 && a.H == 100 && c.X == 500 && c.Y == 200 && c.W == 200 && c.H == 100,
                    a.X + "," + a.Y + " " + a.W + "x" + a.H + " / " + c.X + "," + c.Y + " " + c.W + "x" + c.H);
                _screen.Widgets.Remove(a);
                _screen.Widgets.Remove(c);
                Select(null);
            }

            // ---------------- shape tools on every shaped widget
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
                    using (var renderer = new LayoutRenderer(854, 480, new DashboardSettings { Layout = Layouts.Custom, Design = layout }, _units))
                    using (System.Drawing.Bitmap frame = renderer.Render(IRacingTelemetrySource.CreateSpotterPreviewSnapshot(), false, 4, 1, layout.Screens[0]))
                    {
                        errors = renderer.Errors.Count;
                        return Pixels(frame);
                    }
                }
                string a = Draw(plainOne, out int e1), b = Draw(bent, out int e2);
                Check("shape tools change " + shaped.Name, e1 == 0 && e2 == 0 && a != b);
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
            var lit = new DashboardSettings { Name = "Lit", AccentColor = "#00FF00", Design = _layout, Lights = LightPresets.Refined() };
            DesignPackage.Write(file, DesignPackage.From(lit));
            DesignPackage withLights = DesignPackage.Read(file);
            Check("exported dashboards carry their LED lights", withLights.Lights != null && withLights.Lights.Layers.Count == lit.Lights.Layers.Count && withLights.Lights.Layers.Count > 0);
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
