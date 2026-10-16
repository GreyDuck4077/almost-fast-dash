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

            // The largest difference in any color channel between two frames.
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
                // Three seconds of the lap at the wheel's 30 a second, the same frames every run.
                double lapClock = 0;
                var lap = new SyntheticTelemetry { Clock = () => lapClock };
                int mismatches = 0, frames = 0, rounded = 0;
                double share = 0, fast = 0, slow = 0;
                string firstDiff = "", changed = "", costly = "";
                using (var incremental = new LayoutRenderer(854, 480, d, _units) { FlagClock = 0, FixedNow = TestNow, Timings = new Dictionary<string, double>() })
                using (var whole = new LayoutRenderer(854, 480, d, _units) { FlagClock = 0, FixedNow = TestNow, RedrawChangesOnly = false, Timings = new Dictionary<string, double>() })
                    for (int i = 0; i < 90; i++)
                    {
                        lapClock = i / 30.0;
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
                                // Rounding, not a real difference: scaled glows can round 1/255 differently when clipped, and a
                                // widget laid down from its finished picture is blended twice (into its picture, then onto the
                                // frame), which can move anti-aliased edges by up to 3/255. That's below one step of the wheel
                                // screen's RGB565. Anything larger is a real difference.
                                if (Pixels(a) != Pixels(b) && Largest(a, b) <= 3) rounded++;
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
                Check("redrawing only changes matches a full redraw: " + template.Name, mismatches == 0, mismatches + " of 90 frames differ" + (rounded > 0 ? ", " + rounded + " within 3/255 rounding" : "") + "; " + firstDiff);
                if (frames > 0)
                    report.Add("INFO  " + template.Id + " animated lap: " + (fast / frames * 1000 / System.Diagnostics.Stopwatch.Frequency).ToString("0.00") + " ms per frame redrawing changes (" +
                        (share / frames * 100).ToString("0") + "% of the screen) vs " + (slow / frames * 1000 / System.Diagnostics.Stopwatch.Frequency).ToString("0.00") + " ms redrawing everything; changed most: " + changed + "; costliest (ms/frame): " + costly);
            }

            // Waving flags: the cloth moves with time, and the runtime is told to redraw it.
            using (var waving = new LayoutRenderer(854, 480, DashboardSettings.CreateRoad(), _units))
            {
                TelemetrySnapshot green = IRacingTelemetrySource.CreatePreviewSnapshot(RacingFlag.Green);
                // The flag comes out at 0 and has faded in well before a second.
                waving.FlagClock = 0;
                waving.StateHash(green, false, -1);
                waving.FlagClock = 1000;
                string first;
                int hashA = waving.StateHash(green, false, -1);
                using (var a = waving.Render(green, false, green.Gear, 1)) first = Pixels(a);
                waving.FlagClock = 1700;
                int hashB = waving.StateHash(green, false, -1);
                using (var b = waving.Render(green, false, green.Gear, 1))
                    Check("waving flag moves over time and asks for a redraw", first != Pixels(b) && hashA != hashB);
            }

            // ---------------- flags: alert, then step aside
            {
                TelemetrySnapshot Flagged(RacingFlag flag, bool caution = false)
                {
                    TelemetrySnapshot snapshot = IRacingTelemetrySource.CreatePreviewSnapshot(flag);
                    snapshot.CautionActive = caution;
                    return snapshot;
                }
                // The telemetry flashes a new caution on and off every 150 ms for the lights; the screen holds steady.
                var caution = new FlagTimeline(150);
                bool steady = true;
                for (int ms = 0; ms <= 1800; ms += 50)
                {
                    FlagView view = caution.At(Flagged(((ms / 150) & 1) == 0 ? RacingFlag.Yellow : RacingFlag.None, true), ms, true, true);
                    if (ms >= FlagTimeline.FadeIn && !view.Full) steady = false;
                }
                Check("flag: a flashing yellow holds the screen steady", steady);
                FlagView fading = caution.At(Flagged(RacingFlag.None, true), 1800 + 500, true, true);
                FlagView later = caution.At(Flagged(RacingFlag.None, true), 1800 + 2000, true, true);
                Check("flag: after the caution's alert the dashboard returns with a yellow frame",
                    fading.Alpha > 0 && fading.Alpha < 1 && later.Alpha == 0 && later.Frame == RacingFlag.Yellow, "alpha " + fading.Alpha + " then " + later.Alpha + ", frame " + later.Frame);

                var black = new FlagTimeline(150);
                black.At(Flagged(RacingFlag.Black), 0, true, true);
                FlagView alert = black.At(Flagged(RacingFlag.Black), 1000, true, true);
                FlagView aside = black.At(Flagged(RacingFlag.Black), 5000, true, true);
                Check("flag: black takes the screen, then steps aside to a frame", alert.Full && aside.Alpha == 0 && aside.Frame == RacingFlag.Black);

                var blue = new FlagTimeline(150);
                blue.At(Flagged(RacingFlag.BlueYellowStripe), 0, true, true);
                blue.At(Flagged(RacingFlag.BlueYellowStripe), 4000, true, true);
                blue.At(Flagged(RacingFlag.None), 6000, true, true);
                FlagView gone = blue.At(Flagged(RacingFlag.None), 7000, true, true);
                blue.At(Flagged(RacingFlag.BlueYellowStripe), 10000, true, true);
                FlagView back = blue.At(Flagged(RacingFlag.BlueYellowStripe), 10500, true, true);
                Check("flag: a blue flag back within seconds goes straight to its frame", gone.Frame == RacingFlag.None && back.Alpha == 0 && back.Frame == RacingFlag.BlueYellowStripe);

                var red = new FlagTimeline(150);
                red.At(Flagged(RacingFlag.Red), 0, true, true);
                Check("flag: red keeps the screen", red.At(Flagged(RacingFlag.Red), 60000, true, true).Full);

                var framesOnly = new FlagTimeline(150);
                framesOnly.At(Flagged(RacingFlag.Green), 0, false, true);
                FlagView framed = framesOnly.At(Flagged(RacingFlag.Green), 1000, false, true);
                Check("flag: with full-screen flags off, a flag shows as a frame", framed.Alpha == 0 && framed.Frame == RacingFlag.Green);
                Check("flag: with frames off, a caution shows no frame", new FlagTimeline(150).At(Flagged(RacingFlag.None, true), 0, true, false).Frame == RacingFlag.None);

                // On a dashboard: the repair flag covers it, then hands it back with a frame, and leaves nothing behind when it goes.
                DashboardSettings road = DashboardSettings.CreateDesigned(DesignTemplates.Road);
                using (var flagged = new LayoutRenderer(854, 480, road, _units) { FixedNow = TestNow, FlagClock = 0 })
                using (var clean = new LayoutRenderer(854, 480, DashboardSettings.CreateDesigned(DesignTemplates.Road), _units) { FixedNow = TestNow, FlagClock = 0 })
                using (var reference = clean.Render(Flagged(RacingFlag.None), false, 4, 1))
                {
                    TelemetrySnapshot repair = Flagged(RacingFlag.Meatball);
                    int middle = reference.GetPixel(427, 240).ToArgb(), edge = reference.GetPixel(1, 240).ToArgb();
                    flagged.StateHash(repair, false, -1);
                    flagged.FlagClock = 1000;
                    bool covered;
                    using (var a = flagged.Render(repair, false, 4, 1)) covered = a.GetPixel(427, 240).ToArgb() != middle;
                    flagged.FlagClock = 5000;
                    int breathA = flagged.StateHash(repair, false, -1);
                    bool handedBack;
                    using (var b = flagged.Render(repair, false, 4, 1)) handedBack = b.GetPixel(427, 240).ToArgb() == middle && b.GetPixel(1, 240).ToArgb() != edge;
                    flagged.FlagClock = 5000 + 600;
                    int breathB = flagged.StateHash(repair, false, -1);
                    Check("flag: repairs alert, then hand the dashboard back framed", covered && handedBack);
                    Check("flag: the frame asks for redraws as it breathes", breathA != breathB);
                    flagged.FlagClock = 20000;
                    using (var c = flagged.Render(Flagged(RacingFlag.None), false, 4, 1))
                        Check("flag: the frame leaves nothing behind when the flag goes", Pixels(c) == Pixels(reference));
                }

                // Every flag screen draws at another screen size, and the finish shows the result.
                foreach (RacingFlag flag in Enum.GetValues(typeof(RacingFlag)).Cast<RacingFlag>().Where(f => f != RacingFlag.None))
                    using (var painter = new FlagPainter(1024, 600))
                    {
                        System.Drawing.Bitmap picture = painter.Picture(flag, Flagged(flag));
                        Check("flag screen draws at 1024x600: " + flag, picture.Width == 1024 && picture.GetPixel(512, 300).A == 255);
                    }
                using (var timed = new LayoutRenderer(854, 480, DashboardSettings.CreateDesigned(DesignTemplates.Road), _units) { FixedNow = TestNow, FlagClock = 0 })
                {
                    TelemetrySnapshot yellow = Flagged(RacingFlag.None, true), plain = Flagged(RacingFlag.None);
                    double Cost(TelemetrySnapshot snapshot)
                    {
                        using (timed.Render(snapshot, false, 4, 1)) { }
                        var watch = System.Diagnostics.Stopwatch.StartNew();
                        for (int i = 1; i <= 30; i++)
                        {
                            timed.FlagClock = i * 100;
                            using (timed.Render(snapshot, false, 4, 1)) { }
                        }
                        return watch.Elapsed.TotalMilliseconds / 30;
                    }
                    report.Add("INFO  caution frame over the road template: " + Cost(yellow).ToString("0.00") + " ms per frame vs " + Cost(plain).ToString("0.00") + " ms without");
                }
                TelemetrySnapshot finish = Flagged(RacingFlag.Checkered), better = Flagged(RacingFlag.Checkered);
                better.StartPosition = 12;
                Check("flag: the finish screen changes with the result", FlagPainter.Key(RacingFlag.Checkered, finish) != FlagPainter.Key(RacingFlag.Checkered, better));
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
                Eval("standing(99, \"Exists\")") == "false" && Eval("standing(8, \"GapText\")") == "+16.35" && Eval("standing(17, \"GapText\")") == "+1 LAP" &&
                Eval("standing(22, \"GapText\")") == "+2 LAPS" && Eval("GapToLeader") == "+5.94", Eval("standing(17, \"GapText\")"));
            // Leader just over the line and a car not there yet: same lap. A whole lap behind on track: lapped.
            Check("a car is a lap down only when a whole lap behind on track, not while it's yet to cross the line",
                IRacingTelemetrySource.LapsDown(18, 0.01f, 17, 0.95f) == 0 && IRacingTelemetrySource.LapsDown(18, 0.05f, 17, 0.10f) == 0 &&
                IRacingTelemetrySource.LapsDown(18, 0.50f, 17, 0.40f) == 1 && IRacingTelemetrySource.LapsDown(18, 0.60f, 16, 0.50f) == 2 &&
                IRacingTelemetrySource.LapsDown(18, -1, 17, 0.95f) == 1);
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
                    Check("outline is drawn in its color", Enumerable.Range(0, 200).Any(k => outline.GetPixel(300 + k, 230).G > 200 && outline.GetPixel(300 + k, 230).R < 80));
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
                Check("paste style copies color and effects, not content", other.Get("color") == "#FF0000" && other.Get("fxGlow") == "6" && other.Get("text") == name.Get("text"));
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

            // ---------------- trustworthy numbers: gaps, laps to go, fuel, settings
            {
                // A 90-second lap: the car ahead is 3% of a lap on, but 3.5 s away through the slow part of the track.
                float[] estimates = { 30f, 33.5f, 88f, 1f };
                double slow = IRacingTelemetrySource.TrackGap(estimates, 0, 1, 0.03, 90);
                double acrossLine = IRacingTelemetrySource.TrackGap(estimates, 2, 3, 0.03, 90);
                double missing = IRacingTelemetrySource.TrackGap(new[] { 30f, -1f }, 0, 1, 0.03, 90);
                double nonsense = IRacingTelemetrySource.TrackGap(new[] { 30f, 70f }, 0, 1, 0.03, 90);
                Check("gaps follow iRacing's per-car time estimates, across the line too, and fall back when they can't be trusted",
                    Math.Abs(slow - 3.5) < 0.001 && Math.Abs(acrossLine - 3) < 0.001 && Math.Abs(missing - 2.7) < 0.001 && Math.Abs(nonsense - 2.7) < 0.001,
                    slow + " " + acrossLine + " " + missing + " " + nonsense);

                TelemetrySnapshot race = IRacingTelemetrySource.CreatePreviewSnapshot();
                race.PlayerCarIdx = 0;
                race.CarLapDist = new[] { 0.4f };
                race.FuelPerLapAverage = 3;
                race.FuelLapsMeasured = 5;
                double expectLaps = Math.Max(0, race.TotalLaps - race.LapsCompleted - 0.4);
                double expectAdd = Math.Max(0, expectLaps * 3 - race.FuelLiters), expectMargin = Math.Max(0, (expectLaps + 1) * 3 - race.FuelLiters);
                Check("fuel to add counts only the rest of this lap, and a safety margin adds whole laps",
                    race.ProgressMode == SessionProgressMode.LapRace && Math.Abs(race.LapsToGo - expectLaps) < 1e-5 && expectAdd > 0 &&
                    Math.Abs(race.FuelToFinish - expectAdd) < 1e-4 && Math.Abs(race.FuelToFinishWith(1) - expectMargin) < 1e-4,
                    race.LapsToGo + " laps, add " + race.FuelToFinish + ", with a lap's margin " + race.FuelToFinishWith(1));

                var timed = new TelemetrySnapshot(true, 4, 120, RacingFlag.None, BlindSpotZone.None, BlindSpotZone.None, 0.5,
                    SessionProgressMode.TimedRace, 250, 1800, 12, 0, 4, 2, 20, 10, 0, "A", -1, "B", 1, 10, false, 100, 100, 100, new bool[0], 0, 6, 6000);
                timed.PlayerCarIdx = 0;
                timed.CarLapDist = new[] { 0.5f };
                // 250 s left on 100 s laps, half way round: the rest of this lap (50 s), then two more to pass zero; that one is the last.
                Check("a timed race runs to the end of the lap in progress when the clock runs out", Math.Abs(timed.LapsToGo - 2.5) < 1e-9, timed.LapsToGo.ToString("0.00"));

                var oldFuel = new AppSettings { SchemaVersion = 12 };
                DashboardSettings withOld = DashboardSettings.CreateDesigned(DesignTemplates.Find("spotter"));
                DashWidget refuel = withOld.Design.AllWidgets().First(w => w.Get("value", "") == DesignTemplates.FuelToAdd);
                refuel.Set("value", AppSettings.OldFuelToAdd);
                refuel.Set("label", "FUEL TO END");
                refuel.Props.Remove("unit");
                oldFuel.Dashboards.Add(withOld);
                oldFuel.EnsureBuiltInDashboards();
                Check("saved designs' fuel to add gains its unit and a clear label", refuel.Get("value") == DesignTemplates.FuelToAdd &&
                    refuel.Get("unit") == DesignTemplates.FuelToAddUnit && refuel.Get("label") == DesignTemplates.FuelToAddLabel);

                // A new install sees the welcome card, not What's new; an upgrade (no version seen yet) sees What's new.
                AppSettings fresh = AppSettings.CreateDefault();
                Check("a new install starts with the welcome card, an update with What's new", fresh.General.ShowWelcome && fresh.General.SeenVersion == WhatsNew.Version &&
                    new GeneralSettings().SeenVersion != WhatsNew.Version && WhatsNew.Highlights.Length > 0, fresh.General.SeenVersion + " / " + WhatsNew.Version);

                // Schema 20: the shipped pit-lane label in US spelling; a label of the user's own stays.
                var british = new AppSettings { SchemaVersion = 19 };
                DashboardSettings pitPage = DashboardSettings.CreateDesigned(DesignTemplates.Find("apex"));
                DashWidget tiresLabel = pitPage.Design.AllWidgets().First(w => w.Get("text") == "TIRES · LAST STOP");
                tiresLabel.Set("text", "TYRES · LAST STOP");
                tiresLabel.Name = "Tyres label";
                DashWidget own = WidgetCatalog.Create("text", 0, 0);
                own.Set("text", "MY TYRES");
                pitPage.Design.Screens[0].Widgets.Add(own);
                british.Dashboards.Add(pitPage);
                british.EnsureBuiltInDashboards();
                Check("saved designs' pit-lane label reads TIRES, and the user's own words stay", tiresLabel.Get("text") == "TIRES · LAST STOP" &&
                    tiresLabel.Name == "Tires label" && own.Get("text") == "MY TYRES", tiresLabel.Get("text") + " / " + tiresLabel.Name);

                // A damaged settings file comes back from the newest backup that reads.
                AppSettings current = SettingsStore.Load();
                SettingsStore.Save(current);
                SettingsStore.Save(current);
                string good = System.IO.File.ReadAllText(SettingsStore.FilePath);
                System.IO.File.WriteAllText(SettingsStore.FilePath, "{ this is not settings");
                AppSettings recovered = SettingsStore.Load();
                bool restoredFile = System.IO.File.ReadAllText(SettingsStore.FilePath).Length > 100;
                Check("a damaged settings file is recovered from its backup", recovered != null && recovered.Dashboards.Count == current.Dashboards.Count && restoredFile &&
                    System.IO.File.Exists(SettingsStore.FilePath + ".bak") && System.IO.Directory.GetFiles(SettingsStore.BackupFolder, "settings-*.json").Length > 0);
                System.IO.File.WriteAllText(SettingsStore.FilePath, good);
                foreach (string unreadable in System.IO.Directory.GetFiles(System.IO.Path.GetDirectoryName(SettingsStore.FilePath), "settings.json.unreadable-*"))
                    System.IO.File.Delete(unreadable);

                // A new install's units match its region, and only a new install shows the welcome card.
                GeneralSettings UnitsFor(string region)
                {
                    var general = new GeneralSettings();
                    general.UseRegionUnits(new System.Globalization.RegionInfo(region));
                    return general;
                }
                GeneralSettings us = UnitsFor("US"), uk = UnitsFor("GB"), de = UnitsFor("DE");
                Check("a new install's units match its region (US, UK, elsewhere)",
                    us.SpeedUnit == "MPH" && us.FuelUnit == "Gallons" && us.TemperatureUnit == "Fahrenheit" &&
                    uk.SpeedUnit == "MPH" && uk.FuelUnit == "Liters" && uk.TemperatureUnit == "Celsius" &&
                    de.SpeedUnit == "KPH" && de.FuelUnit == "Liters" && de.TemperatureUnit == "Celsius");
                // Settings saved before the welcome card don't have the field, so they read as a fresh GeneralSettings.
                Check("only a new install shows the welcome card", AppSettings.CreateDefault().General.ShowWelcome && !new GeneralSettings().ShowWelcome);
            }

            // ---------------- the instrument finish
            {
                // A scale that lights up as it's passed: a tick reached shows bright, one still ahead stays dim.
                DashLayout scale = DesignTemplates.Blank();
                scale.Theme.BackgroundStyle = "Solid";
                scale.Theme.Background = "#000000";
                DashWidget dial = WidgetCatalog.Create("arc", 200, 40);
                dial.W = 400; dial.H = 400;
                string[] look = { "value", "0.5", "style", "Track only", "ticks", "10", "minorTicks", "0", "tickLength", "20", "tickColor", "#FFFFFF",
                    "colorMode", "Solid", "color", "#FFFFFF", "litScale", "true", "glow", "0", "rim", "#FF0000", "rimWidth", "2", "start", "180", "sweep", "180" };
                for (int i = 0; i + 1 < look.Length; i += 2) dial.Set(look[i], look[i + 1]);
                scale.Screens[0].Widgets.Add(dial);
                using (var renderer = new LayoutRenderer(854, 480, new DashboardSettings { Layout = Layouts.Custom, Design = scale, AccentColor = "#22D3EE" }, _units))
                using (System.Drawing.Bitmap frame = renderer.Render(IRacingTelemetrySource.CreatePreviewSnapshot(), false, 4, 1))
                {
                    // The half-dial runs from the left (0) over the top to the right (1); the middle tick at the top is reached.
                    int Brightest(int x0, int y0, int x1, int y1)
                    {
                        int best = 0;
                        for (int py = y0; py <= y1; py++)
                            for (int px = x0; px <= x1; px++) best = Math.Max(best, (int)frame.GetPixel(px, py).G);
                        return best;
                    }
                    int leftTick = Brightest(222, 232, 250, 248), rightTick = Brightest(550, 232, 578, 248);
                    bool redRim = false;
                    for (int py = 40; py < 60 && !redRim; py++)
                        for (int px = 395; px < 460; px++) { var pixel = frame.GetPixel(px, py); if (pixel.R > 120 && pixel.G < 60) { redRim = true; break; } }
                    Check("a lit scale shows marks already passed brighter than those ahead, inside a hairline bezel", leftTick > rightTick + 60 && redRim,
                        "passed " + leftTick + ", ahead " + rightTick + ", bezel " + redRim);
                }

                // A saved Roady from before takes the new finish but keeps where its widgets sit.
                var before = new AppSettings { SchemaVersion = 13 };
                DashboardSettings roady = DashboardSettings.CreateRoad();
                DashScreen race = roady.Design.Screens.First(s => s.Name == "Race");
                race.Widgets.RemoveAll(w => w.Name == "Gear light");
                DashWidget sweep = race.Widgets.First(w => w.Name == "Rev sweep");
                sweep.Props.Remove("rim");
                sweep.Props.Remove("litScale");
                sweep.Set("thickness", "14");
                sweep.X = 30;
                before.Dashboards.Add(roady);
                before.EnsureBuiltInDashboards();
                Check("a saved flagship takes the new finish and keeps its layout", sweep.Get("rim") == "#2E2722" && sweep.Get("litScale") == "true" && sweep.Get("thickness") == "10" &&
                    sweep.X == 30 && race.Widgets.Count(w => w.Name == "Gear light") == 1 && race.Widgets.FindIndex(w => w.Name == "Gear light") == race.Widgets.IndexOf(sweep) - 1);

                // Halo and Oval round their corners; a first-generation Neon is rebuilt.
                var older = new AppSettings { SchemaVersion = 14 };
                DashboardSettings haloDash = DashboardSettings.CreateDesigned(DesignTemplates.Find("blank"));
                haloDash.Id = "dashboard-halo";
                DashScreen haloMain = haloDash.Design.Screens[0];
                DashWidget tile = new DashWidget { Type = "panel", Name = "Fuel tile", X = 600, Y = 100, W = 196, H = 60 }, chip = new DashWidget { Type = "panel", Name = "Caution chip", X = 360, Y = 80, W = 116, H = 24 };
                tile.Set("corners", "Cut diagonal");
                chip.Set("corners", "Cut diagonal");
                DashWidget revs = new DashWidget { Type = "arc", Name = "Rev lights left" }, pit = new DashWidget { Type = "arc", Name = "Pit speed" };
                DashWidget kink = new DashWidget { Type = "shape", Name = "Hairline left angle" }, baseLine = new DashWidget { Type = "shape", Name = "Hairline left base", X = 18, Y = 54, W = 158, H = 2 };
                haloMain.Widgets.AddRange(new[] { tile, chip, revs, pit, kink, baseLine });
                DashboardSettings oldNeon = DashboardSettings.CreateDesigned(DesignTemplates.Find("blank"));
                oldNeon.Id = "dashboard-oldneon";
                foreach (string name in new[] { "Rev ring", "Fuel ring", "Fuel pct", "Divider" }) oldNeon.Design.Screens[0].Widgets.Add(new DashWidget { Type = "text", Name = name });
                older.Dashboards.Add(haloDash);
                older.Dashboards.Add(oldNeon);
                older.EnsureBuiltInDashboards();
                Check("Halo and Oval take rounded corners, pills and straight hairlines; a first Neon is rebuilt",
                    tile.Get("corners") == "Round" && tile.Get("radius") == "16" && chip.Get("radius") == "12" && revs.Get("rim") != null && pit.Get("needle") == "Marker" &&
                    !haloMain.Widgets.Contains(kink) && baseLine.W == 252 &&
                    oldNeon.Design.AllWidgets().Any(w => w.Name == "Shift horizon") && !oldNeon.Design.AllWidgets().Any(w => w.Name == "Fuel ring"),
                    tile.Get("corners", "") + " " + tile.Get("radius", "") + " / " + chip.Get("radius", ""));

                // Schema 16: Halo and Oval are reworked; their names, colors, track types and flag choices stay, and nothing else changes.
                var reworked = new AppSettings { SchemaVersion = 15 };
                DashboardSettings oldHalo = DashboardSettings.CreateDesigned(DesignTemplates.Find("blank")), oldOval = DashboardSettings.CreateDesigned(DesignTemplates.Find("blank")),
                    ownDesign = DashboardSettings.CreateDesigned(DesignTemplates.Find("blank"));
                oldHalo.Id = "dashboard-halo";
                oldHalo.Name = "Halo";
                oldHalo.AccentColor = "#30D158";
                oldHalo.Design.FlagFrame = false;
                oldOval.Id = "dashboard-oval";
                oldOval.Name = "Oval";
                oldOval.RaceTypes = new List<string> { Disciplines.Oval };
                ownDesign.Id = "dashboard-own";
                int ownWidgets = ownDesign.Design.AllWidgets().Count();
                reworked.Dashboards.AddRange(new[] { oldHalo, oldOval, ownDesign });
                reworked.EnsureBuiltInDashboards();
                Check("Halo and Oval take the reworked designs, keeping their names, colors, track types and flag choices",
                    oldHalo.Design.AllWidgets().Any(w => w.Type == "traffic") && oldOval.Design.AllWidgets().Any(w => w.Type == "radar") && oldHalo.Name == "Halo" &&
                    oldHalo.AccentColor == "#30D158" && !oldHalo.Design.FlagFrame && oldOval.RaceTypes.Contains(Disciplines.Oval) && ownDesign.Design.AllWidgets().Count() == ownWidgets);

                // Schema 17: brighter labels where the color is still a template's; a color of the user's own stays.
                var dimmer = new AppSettings { SchemaVersion = 16 };
                DashboardSettings dimOval = DashboardSettings.CreateDesigned(DesignTemplates.Find("oval")), ownColors = DashboardSettings.CreateDesigned(DesignTemplates.Find("blank"));
                dimOval.Id = "dashboard-dim";
                dimOval.Design.Theme.Muted = "#8DA0BF";
                DashWidget caption = dimOval.Design.AllWidgets().First(w => w.Name == "Ahead lengths caption");
                caption.Set("color", "dim");
                caption.Set("size", "10");
                ownColors.Id = "dashboard-colors";
                ownColors.Design.Theme.Muted = "#123456";
                dimmer.Dashboards.AddRange(new[] { dimOval, ownColors });
                dimmer.EnsureBuiltInDashboards();
                Check("labels brighten where the color is still a template's, and a color of your own stays",
                    dimOval.Design.Theme.Muted == "#B6C5DD" && caption.Get("color") == "muted" && caption.Get("size") == "11" && ownColors.Design.Theme.Muted == "#123456",
                    dimOval.Design.Theme.Muted + " " + caption.Get("color") + " " + caption.Get("size") + " " + ownColors.Design.Theme.Muted);
            }

            // ---------------- radar and traffic
            {
                const double Track = 1000;
                float At(double meters) => (float)(0.5 + meters / Track);

                var radar = new RadarTracker();
                radar.Update(new[] { 0.5f, At(10), At(0.5), -1f }, new[] { false, false, true, false }, 0, Track, 1, 100);
                Check("radar: a car ten metres ahead is a car length clear, and a car in the pits isn't shown",
                    radar.Cars.Length == 1 && Math.Abs(radar.Cars[0].Meters - 10) < 0.05 && Math.Abs(RadarTracker.Lengths(radar.AheadMeters) - 1) < 0.02,
                    radar.Cars.Length + " cars, ahead " + radar.AheadMeters.ToString("0.00"));

                var wrap = new RadarTracker();
                wrap.Update(new[] { 0.995f, 0.004f }, null, 0, Track, 1, 0);
                Check("radar: a car just over the line is still just ahead", wrap.Cars.Length == 1 && Math.Abs(wrap.Cars[0].Meters - 9) < 0.1);

                var sides = new RadarTracker();
                float[] three = { 0.5f, At(1.5), At(-2), At(30) };
                sides.Update(three, null, 0, Track, 2, 0);
                RadarCar first = sides.Cars.First(x => x.CarIdx == 1);
                sides.Update(three, null, 0, Track, 4, 0.05);
                RadarCar kept = sides.Cars.First(x => x.CarIdx == 1), second = sides.Cars.First(x => x.CarIdx == 2);
                Check("radar: a car left goes to the nearest car; a second alongside goes right and the first stays left",
                    first.Lane == -1 && first.Alongside && kept.Lane == -1 && second.Lane == 1 && second.Alongside && !sides.Cars.First(x => x.CarIdx == 3).Alongside);

                var ghost = new RadarTracker();
                ghost.Update(new[] { 0.5f, At(40) }, null, 0, Track, 3, 0);
                Check("radar: a car the spotter reports that positions can't place still shows alongside", ghost.Cars.Any(x => x.CarIdx < 0 && x.Lane == 1 && x.Alongside));

                // A car that was alongside on the left and moves a car length and a half ahead: in your bubble it keeps
                // its lane for a good while (ten seconds on), then drifts into line once nothing says otherwise.
                var drift = new RadarTracker();
                drift.Update(new[] { 0.5f, At(1.5) }, null, 0, Track, 2, 0);
                for (int i = 1; i <= 200; i++) drift.Update(new[] { 0.5f, At(8) }, null, 0, Track, 1, i * 0.05);
                float held = drift.Cars[0].Lane;
                for (int i = 201; i <= 500; i++) drift.Update(new[] { 0.5f, At(8) }, null, 0, Track, 1, i * 0.05);
                Check("radar: a car in your bubble keeps its lane, then drifts into line ahead", held == -1 && drift.Cars[0].Lane == 0 && Math.Abs(drift.AheadMeters - 8) < 0.05,
                    "after 10 s " + held + ", after 25 s " + drift.Cars[0].Lane + ", ahead " + drift.AheadMeters.ToString("0.00"));

                // Two by two: rows of two cars ahead and behind. Each row is spread across two lanes, the other line on the side
                // the spotter sees a car beside you (here, your row partner on the left).
                var formation = new RadarTracker();
                float[] rows = { 0.5f, At(-0.4), At(9.0), At(9.6), At(18.2), At(18.5), At(-9.0), At(-8.7) };
                for (int i = 0; i <= 30; i++) formation.Update(rows, null, 0, Track, 2, i * 0.05);
                int Lane(RadarTracker r, int car) => (int)Math.Round(r.Cars.First(x => x.CarIdx == car).Lane);
                bool pairsSplit = new[] { Tuple.Create(2, 3), Tuple.Create(4, 5), Tuple.Create(6, 7) }.All(p => Lane(formation, p.Item1) != Lane(formation, p.Item2));
                bool onTheLeft = new[] { 2, 3, 4, 5, 6, 7 }.All(car => Lane(formation, car) == 0 || Lane(formation, car) == -1);
                Check("radar: a two-by-two formation shows two wide, with the other line on the side the spotter saw",
                    pairsSplit && onTheLeft && Lane(formation, 1) == -1 && Math.Abs(formation.AheadMeters - 9.0) < 0.7,
                    string.Join(" ", formation.Cars.Select(x => "#" + x.CarIdx + "@" + x.Meters.ToString("0.0") + ":" + x.Lane.ToString("0.0"))));

                // With nothing beside you to say which side the other line is on, a row of two straddles your lane.
                var unknown = new RadarTracker();
                for (int i = 0; i <= 30; i++) unknown.Update(new[] { 0.5f, At(9.0), At(9.4) }, null, 0, Track, 1, i * 0.05);
                float l1 = unknown.Cars.First(x => x.CarIdx == 1).Lane, l2 = unknown.Cars.First(x => x.CarIdx == 2).Lane;
                Check("radar: a row of two with no side known straddles your lane", Math.Abs(Math.Abs(l1) - 0.5f) < 0.01f && Math.Abs(l1 + l2) < 0.01f, l1 + " " + l2);

                // No template has text below the readable floor.
                var small = new List<string>();
                foreach (DesignTemplate template in DesignTemplates.All)
                    foreach (DashWidget w in template.Create().AllWidgets())
                        foreach (string key in new[] { "size", "labelSize", "valueSize", "unitSize", "numberSize" })
                        {
                            string raw = w.Get(key, null);
                            if (raw == null || raw.StartsWith("=") || w.Type == "shape" || w.Type == "gear" || w.Type == "radar" || w.Type == "laphistory") continue;
                            if (double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) && v > 0 && v < ReadableText.Floor)
                                small.Add(template.Name + "/" + w.Name + "." + key + "=" + raw);
                        }
                Check("every template's text is at least " + ReadableText.Floor + " px", small.Count == 0, string.Join(", ", small.Take(6)));

                // Every piece of text on every template, as actually drawn, is big enough and stands out from what's behind it.
                foreach (DesignTemplate template in DesignTemplates.All)
                {
                    List<DashboardAudit.Finding> findings = DashboardAudit.Run(DashboardSettings.CreateDesigned(template), _units);
                    Check("readable at a glance: " + template.Name, findings.Count == 0, string.Join("; ", findings.Take(4)));
                }

                // Cars in single file stay in line.
                var single = new RadarTracker();
                for (int i = 0; i <= 30; i++) single.Update(new[] { 0.5f, At(6), At(12), At(-6) }, null, 0, Track, 1, i * 0.05);
                Check("radar: single file stays in line", single.Cars.All(x => x.Lane == 0));

                var closing = new RadarTracker();
                for (int i = 0; i <= 120; i++) closing.Update(new[] { 0.5f, At(30 - i * 2.0 / 60) }, null, 0, Track, 1, i / 60.0);
                Check("radar: a gap shrinking two metres a second closes at about 2 m/s", Math.Abs(closing.AheadClosing - 2) < 0.3, closing.AheadClosing.ToString("0.00"));

                // The radar redraws as a car moves, and not otherwise.
                var radarLayout = DesignTemplates.Blank();
                radarLayout.Screens[0].Widgets.Add(WidgetCatalog.Create("radar", 280, 20));
                radarLayout.FullScreenFlags = false;
                using (var radarRenderer = new LayoutRenderer(854, 480, new DashboardSettings { Layout = Layouts.Custom, Design = radarLayout }, _units) { FixedNow = TestNow, FlagClock = 0 })
                {
                    TelemetrySnapshot still = IRacingTelemetrySource.CreatePreviewSnapshot(), moved = IRacingTelemetrySource.CreatePreviewSnapshot();
                    moved.Radar[0].Meters += 1;
                    int a = radarRenderer.StateHash(still, false, -1), same = radarRenderer.StateHash(IRacingTelemetrySource.CreatePreviewSnapshot(), false, -1), b = radarRenderer.StateHash(moved, false, -1);
                    Check("radar: redraws when a car moves, and only then", a == same && a != b);
                }

                // A prototype 2.5 seconds behind a GT car is a faster class closing.
                TelemetrySnapshot traffic = IRacingTelemetrySource.CreatePreviewSnapshot();
                traffic.Standings = traffic.Standings.Concat(new[] { new StandingsEntry { CarIdx = traffic.CarLapDist.Length, Position = traffic.Standings.Length + 1, ClassName = "LMP2", ClassColor = System.Drawing.Color.RoyalBlue, BestLap = 80.1, CarNumber = "30" } }).ToArray();
                traffic.CarLapDist = traffic.CarLapDist.Concat(new[] { (float)(0.455 - 2.5 / traffic.ReferenceLapSeconds) }).ToArray();
                double faster = RadarPainters.FasterClassBehind(traffic, out string fasterClass);
                Check("traffic: a faster class 2.5 s behind is flagged", Math.Abs(faster - 2.5) < 0.1 && fasterClass == "LMP2", faster.ToString("0.00") + " " + fasterClass);
                Check("traffic: one class alone has no faster class", RadarPainters.FasterClassBehind(IRacingTelemetrySource.CreatePreviewSnapshot(), out _) < 0);
            }

            // ---------------- the dashboard on the PC
            {
                // Where the window and the overlay sit: saved per mode, kept on a display, shaped to the dashboard.
                var work = new System.Drawing.Rectangle(0, 0, 3440, 1392);
                var displays = new[] { new System.Drawing.Rectangle(0, 0, 3440, 1440) };
                var dash = new System.Drawing.Size(854, 480);
                string places = WindowPlacement.Merge(WindowPlacement.Merge("", "Window", new System.Drawing.Rectangle(10, 20, 854, 480)), "Overlay", new System.Drawing.Rectangle(1398, 961, 644, 362));
                places = WindowPlacement.Merge(places, "Window", new System.Drawing.Rectangle(30, 40, 1114, 626));
                Check("window places: each mode saved on its own line, the newest kept",
                    WindowPlacement.Parse(places, "Window") == new System.Drawing.Rectangle(30, 40, 1114, 626) &&
                    WindowPlacement.Parse(places, "Overlay") == new System.Drawing.Rectangle(1398, 961, 644, 362) &&
                    WindowPlacement.Parse(places, "FullScreen") == null && places.Split('\n').Count(l => l.Length > 0) == 2, places.Replace("\n", " | "));
                System.Drawing.Rectangle overlay = WindowPlacement.Resolve(null, "Overlay", dash, displays, work);
                System.Drawing.Rectangle offScreen = WindowPlacement.Resolve(new System.Drawing.Rectangle(5000, 200, 800, 450), "Window", dash, displays, work);
                System.Drawing.Rectangle reshaped = WindowPlacement.Resolve(new System.Drawing.Rectangle(100, 100, 800, 800), "Window", dash, displays, work);
                Check("window places: over the game starts low in the middle, a place off every display starts over, and another dashboard keeps the width",
                    overlay == new System.Drawing.Rectangle(1398, 961, 644, 362) && offScreen == WindowPlacement.Default("Window", 854 / 480.0, work) &&
                    reshaped == new System.Drawing.Rectangle(100, 100, 800, 450), overlay + " " + offScreen + " " + reshaped);
                DateTime now = DateTime.UtcNow;
                Check("over the game: unlocked only while the settings window keeps asking", OverlayUnlock.IsFresh(now.AddSeconds(-1), now) && !OverlayUnlock.IsFresh(now.AddSeconds(-4), now));
                Check("dashboard on the PC: settings from before keep their full-screen display",
                    new GeneralSettings { MonitorDevice = @"\\.\DISPLAY2" }.EffectivePcOutput() == "FullScreen" && new GeneralSettings().EffectivePcOutput() == "Off" &&
                    new GeneralSettings { PcOutput = "Overlay", MonitorDevice = @"\\.\DISPLAY2" }.EffectivePcOutput() == "Overlay");
                // Show when: where the driver is, from iRacing's own flags, and whether the dashboard shows there.
                Check("show when: in the car, out of it, in the garage, or watching another driver",
                    IRacingTelemetrySource.CarStateOf(true, false, false, 3, 3) == CarState.InCar &&
                    IRacingTelemetrySource.CarStateOf(false, false, false, 3, 3) == CarState.OutOfCar &&
                    IRacingTelemetrySource.CarStateOf(false, false, false, -1, 3) == CarState.OutOfCar &&
                    IRacingTelemetrySource.CarStateOf(false, true, false, 3, 3) == CarState.Garage &&
                    IRacingTelemetrySource.CarStateOf(false, false, false, 7, 3) == CarState.Spectating &&
                    IRacingTelemetrySource.CarStateOf(true, false, true, 7, 3) == CarState.Spectating &&
                    IRacingTelemetrySource.CarStateOf(true, false, true, 3, 3) == CarState.OutOfCar);
                var onlyInCar = new GeneralSettings { ShowOutOfCar = false, ShowInGarage = false, ShowSpectating = false };
                TelemetrySnapshot live = IRacingTelemetrySource.CreatePreviewSnapshot();
                live.IsPreview = false;
                live.CarState = CarState.Garage;
                TelemetrySnapshot driving = IRacingTelemetrySource.CreatePreviewSnapshot();
                driving.IsPreview = false;
                Check("show when: hidden only where it's turned off, never before iRacing runs or in a preview",
                    !Program.Shown(live, onlyInCar) && Program.Shown(driving, onlyInCar) && Program.Shown(live, new GeneralSettings()) &&
                    Program.Shown(TelemetrySnapshot.Disconnected, onlyInCar) && Program.Shown(IRacingTelemetrySource.CreatePreviewSnapshot(), onlyInCar));
                Check("wheel screen: see-through pixels show over black", FrameEncoder.Rgb565(0) == 0 && FrameEncoder.Rgb565(unchecked((int)0x80FFFFFF)) == FrameEncoder.Rgb565(unchecked((int)0xFF808080)) &&
                    FrameEncoder.Rgb565(unchecked((int)0xFF3366CC)) == (ushort)((0x33 >> 3) << 11 | (0x66 >> 2) << 5 | (0xCC >> 3)));

                // A see-through background: nothing drawn where no widget is.
                DashboardSettings clear = DashboardSettings.CreateDesigned(DesignTemplates.All.First());
                clear.Design.Theme.BackgroundStyle = "None";
                using (var renderer = new LayoutRenderer(854, 480, clear, _units) { FlagClock = 0, FixedNow = TestNow })
                using (System.Drawing.Bitmap frame = renderer.Render(IRacingTelemetrySource.CreatePreviewSnapshot(), false, 4, 1))
                {
                    int clearPixels = 0, solid = 0;
                    for (int y = 0; y < frame.Height; y += 8)
                        for (int px = 0; px < frame.Width; px += 8)
                        {
                            int alpha = frame.GetPixel(px, y).A;
                            if (alpha == 0) clearPixels++;
                            else if (alpha == 255) solid++;
                        }
                    Check("a None background is see-through between the widgets", clearPixels > 100 && solid > 100, clearPixels + " clear, " + solid + " solid");
                }

                // The one shared output, copied into only where things changed, must match a whole frame every time,
                // also after a flag was drawn over it and has gone.
                DashboardSettings roady = DashboardSettings.CreateDesigned(DesignTemplates.All.First());
                double clock = 0;
                var lap = new SyntheticTelemetry { Clock = () => clock };
                TelemetrySnapshot yellow = IRacingTelemetrySource.CreatePreviewSnapshot(RacingFlag.Yellow);
                int differ = 0, quiet = 0, frames = 0;
                bool changedInside = true;
                using (var shared = new LayoutRenderer(854, 480, roady, _units) { SharedOutput = true, ReuseFingerprints = true, FlagClock = 0, FixedNow = TestNow })
                using (var whole = new LayoutRenderer(854, 480, roady, _units) { FlagClock = 0, FixedNow = TestNow })
                    for (int i = 0; i < 40; i++)
                    {
                        clock = i / 30.0;
                        TelemetrySnapshot snapshot = i >= 15 && i < 20 ? yellow : lap.Next(out _, out _, out _);
                        shared.StateHash(snapshot, false, -1);
                        System.Drawing.Bitmap a = shared.Render(snapshot, false, snapshot.Gear, 1);
                        using (System.Drawing.Bitmap b = whole.Render(snapshot, false, snapshot.Gear, 1))
                            if (Pixels(a) != Pixels(b)) differ++;
                        System.Drawing.Rectangle changed = shared.LastChanged;
                        changedInside &= changed.IsEmpty || new System.Drawing.Rectangle(0, 0, 854, 480).Contains(changed);
                        frames++;
                        // The same state again: nothing changed.
                        if (i == 25)
                        {
                            shared.StateHash(snapshot, false, -1);
                            shared.Render(snapshot, false, snapshot.Gear, 1);
                            if (shared.LastChanged.IsEmpty) quiet++;
                        }
                    }
                Check("shared output copied only where things changed matches a whole frame, flags and all", differ == 0 && quiet == 1 && changedInside,
                    differ + " of " + frames + " frames differ; unchanged frame reported empty: " + (quiet == 1));
            }

            // ---------------- the tool rail: shapes and text drawn on the canvas
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
                    ellipse?.Get("shape") == "Ellipse" && ellipse.X == 260 && ellipse.Y == 250 && ellipse.W == 40 && ellipse.H == 50, Box(ellipse));
                Check("tools: a level line keeps a thin box, a sloping one runs corner to corner",
                    level?.Get("shape") == "Line" && level.W == 200 && level.H == 12 && level.Y == 394 &&
                    rising?.Get("shape") == "Diagonal up" && rising.X == 50 && rising.Y == 300 && rising.W == 100 && rising.H == 100, Box(level) + "; " + Box(rising));
                Check("tools: a click without a drag makes one of the usual size",
                    clicked != null && clicked.X == 600 && clicked.Y == 100 && clicked.W == WidgetCatalog.Find("shape").DefaultW, Box(clicked));
                Check("tools: Text makes a text box where it's clicked, each gets a name, and the rail goes back to Move",
                    text?.Type == "text" && text.X == 400 && rectangle.Name == "Rectangle 1" && _tool == Tool.Move && _screen.Widgets.Count == before + 6,
                    (text?.Type ?? "none") + " " + rectangle?.Name + " " + _tool);
                var undone = new List<int>();
                for (int i = 0; i < 6; i++) { Undo(); undone.Add(_screen.Widgets.Count); }
                Check("tools: each drawing is one step to undo", _screen.Widgets.Count == before, _screen.Widgets.Count + " vs " + before + "; after each undo: " + string.Join(",", undone) + "; undo steps " + _undo.Count);
            }

            // ---------------- drawing: fills, patterns, paths, the Pen and editing points
            {
                // Every pattern tile holds both its colors; every fill style makes a brush.
                var tileProblems = new List<string>();
                foreach (string pattern in Fills.Patterns)
                {
                    System.Drawing.Bitmap tile = Fills.Tile(pattern, System.Drawing.Color.FromArgb(255, 250, 40, 40), System.Drawing.Color.FromArgb(255, 20, 20, 240));
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
                    using (System.Drawing.Brush brush = Fills.Brush(style, System.Drawing.Color.Red, System.Drawing.Color.Blue, 45, "Carbon", 100, 45, new System.Drawing.RectangleF(10, 10, 100, 60)))
                        if (brush == null) tileProblems.Add(style);
                Check("drawing: every pattern shows both its colors, and every fill style makes a brush", tileProblems.Count == 0, string.Join(", ", tileProblems));

                // Points as text and back, and every shape that turns into a path.
                List<PathNode> parsed = PathNodes.Parse("0,0 1,0.5|0.9,0.2|1.1,0.8 0,1");
                Check("drawing: path points read and written the same", parsed.Count == 3 && parsed[1].In.HasValue && parsed[1].Out.Value.X == 1.1f &&
                    PathNodes.Format(parsed) == "0,0 1,0.5|0.9,0.2|1.1,0.8 0,1", PathNodes.Format(parsed));
                var converts = new[] { "Rectangle", "Ellipse", "Polygon", "Star", "Triangle", "Line", "Diagonal down", "Diagonal up", "Chevron" };
                List<string> notConverted = converts.Where(k => PathNodes.FromShape(k, 5, 0.5f, out _) == null).ToList();
                Check("drawing: rectangles, ellipses, polygons, stars, triangles, lines and chevrons convert to paths; a star has twice its points",
                    notConverted.Count == 0 && PathNodes.FromShape("Star", 5, 0.5f, out _).Count == 10 && PathNodes.FromShape("Brackets", 5, 0.5f, out _) == null,
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
                using (var renderer = new LayoutRenderer(200, 200, new DashboardSettings { Layout = Layouts.Custom, Design = drawn }, _units))
                using (System.Drawing.Bitmap frame = renderer.Render(IRacingTelemetrySource.CreatePreviewSnapshot(), false, 4, 1, drawn.Screens[0]))
                {
                    starMiddle = frame.GetPixel(50, 55);
                    pathMiddle = frame.GetPixel(150, 130);
                }
                closedPath.Set("closed", "false");
                using (var renderer = new LayoutRenderer(200, 200, new DashboardSettings { Layout = Layouts.Custom, Design = drawn }, _units))
                using (System.Drawing.Bitmap frame = renderer.Render(IRacingTelemetrySource.CreatePreviewSnapshot(), false, 4, 1, drawn.Screens[0]))
                    openMiddle = frame.GetPixel(150, 130);
                Check("drawing: a star fills its middle, a closed path fills, an open one doesn't",
                    starMiddle.R > 200 && starMiddle.A > 200 && pathMiddle.B > 200 && pathMiddle.A > 200 && openMiddle.A < 10,
                    starMiddle + " " + pathMiddle + " " + openMiddle);

                // The Pen: three clicks and a click on the first point make a closed path round them.
                int before = _screen.Widgets.Count;
                SetTool(Tool.Pen);
                _penNodes.Add(new PathNode { P = new System.Drawing.PointF(100, 100) });
                _penNodes.Add(new PathNode { P = new System.Drawing.PointF(300, 100), In = new System.Drawing.PointF(260, 60), Out = new System.Drawing.PointF(340, 140) });
                _penNodes.Add(new PathNode { P = new System.Drawing.PointF(200, 250) });
                FinishPen(true);
                DashWidget penned = _selection.Count == 1 ? _selection[0] : null;
                Check("drawing: the Pen makes a closed path whose box takes in its points and handles",
                    penned?.Type == "path" && Expr.Bool(penned.Get("closed", "")) && penned.X == 100 && penned.Y == 60 && penned.W == 240 && penned.H == 190 &&
                    PathNodes.Parse(penned.Get("points", "")).Count == 3 && _tool == Tool.Move,
                    penned == null ? "none" : penned.X + "," + penned.Y + " " + penned.W + "x" + penned.H + " " + penned.Get("points", ""));

                // Editing: dragging the bottom point down grows the box to follow it, and the other points stay put.
                BeginEditPath(penned);
                _editNodes[2].P = new System.Drawing.PointF(200, 300);
                ApplyEdit();
                List<PathNode> onScreen = PathNodes.OnScreen(PathNodes.Parse(penned.Get("points", "")), RectOfF(penned));
                Check("drawing: a moved point moves only itself, and the box follows",
                    penned.H == 240 && Math.Abs(onScreen[0].P.X - 100) < 0.6 && Math.Abs(onScreen[0].P.Y - 100) < 0.6 && Math.Abs(onScreen[2].P.Y - 300) < 0.6,
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
                    converted.Type == "path" && converted.X == 40 && converted.W == rect.W && converted.Get("fill") == "#123456" && PathNodes.Parse(converted.Get("points", "")).Count == 4,
                    converted.Type + " " + converted.Get("fill"));
                EndEditPath();
                while (_screen.Widgets.Count > before) Undo();

                // Recolor with a pattern: a panel painted in stripes shows both stripe colors.
                var striped = new DashLayout { Width = 160, Height = 160 };
                striped.Screens.Add(new DashScreen { Name = "Main" });
                DashWidget panel = WidgetCatalog.Create("shape", 0, 0);
                panel.W = panel.H = 160;
                panel.Set("fill", "#FFFFFF"); panel.Set("radius", "0");
                panel.Set("fxFill", "Pattern"); panel.Set("fxFillColor", "#FF0000"); panel.Set("fxPatternBack", "#0000FF"); panel.Set("fxPatternAngle", "0");
                striped.Screens[0].Widgets.Add(panel);
                bool redStripe = false, blueStripe = false;
                using (var renderer = new LayoutRenderer(160, 160, new DashboardSettings { Layout = Layouts.Custom, Design = striped }, _units))
                using (System.Drawing.Bitmap frame = renderer.Render(IRacingTelemetrySource.CreatePreviewSnapshot(), false, 4, 1, striped.Screens[0]))
                    for (int y = 20; y < 140; y += 2)
                    {
                        System.Drawing.Color c = frame.GetPixel(80, y);
                        if (c.R > 200 && c.B < 60) redStripe = true;
                        if (c.B > 200 && c.R < 60) blueStripe = true;
                    }
                Check("drawing: Recolor with a pattern paints any widget in it", redStripe && blueStripe, "red " + redStripe + ", blue " + blueStripe);
            }

            // ---------------- masks: clipping, fades and blending
            {
                DashWidget Box(DashScreen on, string name, double x, double y, double w, double h, params string[] props)
                {
                    DashWidget s = WidgetCatalog.Create("shape", x, y);
                    s.Name = name; s.W = w; s.H = h;
                    s.Set("radius", "0"); s.Set("strokeWidth", "0");
                    for (int i = 0; i + 1 < props.Length; i += 2) s.Set(props[i], props[i + 1]);
                    on.Widgets.Add(s);
                    return s;
                }
                System.Drawing.Bitmap Draw(DashLayout layout, TelemetrySnapshot snapshot = null, bool changesOnly = true)
                {
                    using (var renderer = new LayoutRenderer(layout.Width, layout.Height, new DashboardSettings { Layout = Layouts.Custom, Design = layout }, _units) { RedrawChangesOnly = changesOnly })
                    using (System.Drawing.Bitmap frame = renderer.Render(snapshot ?? IRacingTelemetrySource.CreatePreviewSnapshot(), false, 4, 1, layout.Screens[0]))
                        return new System.Drawing.Bitmap(frame);
                }
                var clipping = new DashLayout { Width = 100, Height = 100 };
                clipping.Theme.BackgroundStyle = "None";
                clipping.Screens.Add(new DashScreen { Name = "Main" });
                DashWidget clipBase = Box(clipping.Screens[0], "Base", 0, 0, 50, 100, "fill", "#FF0000");
                DashWidget stripes = Box(clipping.Screens[0], "Over", 0, 0, 100, 100, "fill", "#0000FF", "clip", "true");
                DashWidget second = Box(clipping.Screens[0], "Over too", 0, 60, 100, 40, "fill", "#00FF00", "clip", "true");
                System.Drawing.Color firstInside, firstOutside, secondInside, secondOutside;
                using (System.Drawing.Bitmap frame = Draw(clipping))
                {
                    firstInside = frame.GetPixel(25, 30); firstOutside = frame.GetPixel(75, 30);
                    secondInside = frame.GetPixel(25, 80); secondOutside = frame.GetPixel(75, 80);
                }
                Check("masks: a clipped layer shows only where the layer below is drawn, and two in a row share it",
                    firstInside.B > 240 && firstInside.R < 20 && firstOutside.A == 0 && secondInside.G > 240 && secondOutside.A == 0,
                    firstInside + " " + firstOutside + " " + secondInside + " " + secondOutside);
                clipBase.Hidden = true;
                System.Drawing.Color hiddenBase;
                using (System.Drawing.Bitmap frame = Draw(clipping)) hiddenBase = frame.GetPixel(25, 30);
                clipBase.Hidden = false;
                Check("masks: hiding the base hides what clips to it", hiddenBase.A == 0, hiddenBase.ToString());

                // A base that comes and goes with live data: redrawing only what changed matches a full redraw.
                clipBase.Set("visible", "=!OnPitRoad");
                TelemetrySnapshot racing = IRacingTelemetrySource.CreatePreviewSnapshot(), pitting = IRacingTelemetrySource.CreatePitLanePreviewSnapshot();
                int mismatched = 0;
                using (var incremental = new LayoutRenderer(100, 100, new DashboardSettings { Layout = Layouts.Custom, Design = clipping }, _units) { FixedNow = TestNow, FlagClock = 0 })
                using (var whole = new LayoutRenderer(100, 100, new DashboardSettings { Layout = Layouts.Custom, Design = clipping }, _units) { FixedNow = TestNow, FlagClock = 0, RedrawChangesOnly = false })
                    foreach (TelemetrySnapshot snapshot in new[] { racing, pitting, racing, pitting })
                        using (System.Drawing.Bitmap a = incremental.Render(snapshot, false, snapshot.Gear, 1, clipping.Screens[0]))
                        using (System.Drawing.Bitmap b = whole.Render(snapshot, false, snapshot.Gear, 1, clipping.Screens[0]))
                            if (Pixels(a) != Pixels(b)) mismatched++;
                Check("masks: a clipped layer follows its base appearing and going, redrawing only what changed", mismatched == 0, mismatched + " of 4 frames differ");
                clipBase.Set("visible", "");

                // A fade to the left: clear at the left edge, solid past the fade.
                var fading = new DashLayout { Width = 100, Height = 40 };
                fading.Theme.BackgroundStyle = "None";
                fading.Screens.Add(new DashScreen { Name = "Main" });
                Box(fading.Screens[0], "Faded", 0, 0, 100, 40, "fill", "#FFFFFF", "fxFade", "Left", "fxFadeLength", "40");
                int edge, middle, solid;
                using (System.Drawing.Bitmap frame = Draw(fading)) { edge = frame.GetPixel(1, 20).A; middle = frame.GetPixel(20, 20).A; solid = frame.GetPixel(80, 20).A; }
                Check("masks: a fade runs from clear at its edge to solid", edge < 20 && middle > 60 && middle < 200 && solid == 255, edge + " " + middle + " " + solid);

                // Blend modes on black and white beneath.
                uint white = 0xFFFFFFFF, black = 0xFF000000, red = 0xFFFF0000;
                Check("masks: Multiply keeps a color on white and black on black; Screen keeps a color on black; Add brightens",
                    LayoutRenderer.BlendPixel(white, red, "Multiply") == red && LayoutRenderer.BlendPixel(black, red, "Multiply") == black &&
                    LayoutRenderer.BlendPixel(black, red, "Screen") == red && LayoutRenderer.BlendPixel(0xFF404040, 0xFF404040, "Add") == 0xFF808080,
                    LayoutRenderer.BlendPixel(white, red, "Multiply").ToString("X8") + " " + LayoutRenderer.BlendPixel(0xFF404040, 0xFF404040, "Add").ToString("X8"));
                var blending = new DashLayout { Width = 100, Height = 50 };
                blending.Theme.BackgroundStyle = "Solid";
                blending.Theme.Background = "#000000";
                blending.Screens.Add(new DashScreen { Name = "Main" });
                Box(blending.Screens[0], "White", 0, 0, 50, 50, "fill", "#FFFFFF");
                Box(blending.Screens[0], "Shade", 0, 0, 100, 50, "fill", "#4080FF", "fxBlend", "Multiply");
                Box(blending.Screens[0], "Light", 0, 40, 100, 10, "fill", "#404040", "fxBlend", "Screen");
                System.Drawing.Color onWhite, onBlack, screened;
                using (System.Drawing.Bitmap frame = Draw(blending)) { onWhite = frame.GetPixel(25, 20); onBlack = frame.GetPixel(75, 20); screened = frame.GetPixel(75, 45); }
                Check("masks: blended layers mix with what's beneath them as drawn",
                    Math.Abs(onWhite.R - 0x40) < 3 && Math.Abs(onWhite.B - 0xFF) < 3 && onBlack.R < 3 && onBlack.B < 3 && Math.Abs(screened.R - 0x40) < 3,
                    onWhite + " " + onBlack + " " + screened);
            }

            // ---------------- modules: kept, shared, brought back and restyled
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
                    made.Widgets[0].X == 0 && made.Widgets[0].Y == 0 && made.Widgets[1].X == 20 && made.Widgets[2].Y == 40 && !made.Widgets.Any(w => w.Locked) &&
                    made.Widgets[0].Get("clip") == "false" && source.Screens[0].Widgets[0].Get("clip") == "true" &&
                    made.Images.Count == 1 && made.Images.ContainsKey("img-module") && picture.Width == 200 && picture.Height == 120,
                    made.Widgets[1].X + "," + made.Widgets[2].Y + " images " + made.Images.Count + " picture " + picture);

                string scratch = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "afd-modules-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                System.IO.Directory.CreateDirectory(scratch);
                string moduleFile = System.IO.Path.Combine(scratch, "shared" + DesignModule.Extension), foreignFile = System.IO.Path.Combine(scratch, "foreign" + DesignModule.Extension);
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
                    first.FilePath == firstAgain.FilePath && other.Name == "Self test module (2)" && otherAgain.FilePath == other.FilePath &&
                    library.Count(m => m.Name.StartsWith("Self test module")) == 2,
                    first.Name + " / " + other.Name + " / " + otherAgain.Name + ", " + library.Count(m => m.Name.StartsWith("Self test module")) + " in the library");

                System.IO.Directory.CreateDirectory(DesignModule.OldBlocksFolder);
                System.IO.File.WriteAllText(System.IO.Path.Combine(DesignModule.OldBlocksFolder, "Self test block.afoblock"), SettingsStore.ToJson(made.Widgets));
                DesignModule oldBlock = DesignModule.Library().FirstOrDefault(m => m.Name == "Self test block");
                Check("modules: blocks saved by earlier versions are still in the Library", oldBlock != null && oldBlock.OldBlock && oldBlock.Widgets.Count == 3,
                    oldBlock == null ? "missing" : oldBlock.Widgets.Count + " widgets");

                // Placed on a bigger screen with an unrelated image under the same id.
                var target = new DashLayout { Width = 1280, Height = 720 };
                target.Screens.Add(new DashScreen { Name = "Main" });
                target.Images["img-module"] = "unrelated";
                List<DashWidget> placed = read.Place(target);
                string imageId = placed[1].Get("image");
                Check("modules: on a bigger screen it scales evenly, text and lines too; a taken image id is renamed; it comes in as one group",
                    placed[0].W == 300 && placed[0].H == 180 && placed[2].Get("size") == "30" && placed[0].Get("strokeWidth") == "6" &&
                    imageId != "img-module" && target.Images.TryGetValue(imageId, out string carried) && carried == png && target.Images["img-module"] == "unrelated" &&
                    placed.All(w => w.Group == placed[0].Group && w.Group.Length > 0 && w.GroupName == "Self test module") && placed[0].Id != read.Widgets[0].Id,
                    placed[0].W + "x" + placed[0].H + " size " + placed[2].Get("size") + " line " + placed[0].Get("strokeWidth") + " image " + imageId);

                // Restyling what's placed: one color swatch changes every use, and Scale takes text and lines along.
                DashWidget red = WidgetCatalog.Create("shape", 100, 100), alsoRed = WidgetCatalog.Create("shape", 300, 140);
                red.Set("fill", "#FF0000"); red.Set("stroke", "#00FF00"); red.Set("strokeWidth", "3");
                alsoRed.Set("fill", "#ff0000"); alsoRed.Set("stroke", "#00FF00"); alsoRed.Set("strokeWidth", "3");
                AddWidgets(new[] { red, alsoRed }, "Self test colors");
                ColorField swatch = Inspector.Children.OfType<System.Windows.Controls.Grid>().SelectMany(g => g.Children.OfType<ColorField>())
                    .FirstOrDefault(f => string.Equals(f.Value, "#FF0000", StringComparison.OrdinalIgnoreCase));
                swatch?.SetValue("#0000FF", true);
                Check("modules: Selection colors lists a color once and changes every use of it",
                    swatch != null && red.Get("fill") == "#0000FF" && alsoRed.Get("fill") == "#0000FF" && red.Get("stroke") == "#00FF00",
                    swatch == null ? "no swatch" : red.Get("fill") + " " + alsoRed.Get("fill"));
                System.Windows.Rect unscaled = Union(new[] { red, alsoRed });
                ScaleSelection(2);
                System.Windows.Rect scaled = Union(new[] { red, alsoRed });
                Check("modules: Scale doubles the selection about its middle, line widths included",
                    Math.Abs(scaled.Width - unscaled.Width * 2) <= 2 && Math.Abs(scaled.X + scaled.Width / 2 - (unscaled.X + unscaled.Width / 2)) <= 1 &&
                    Math.Abs(scaled.Y + scaled.Height / 2 - (unscaled.Y + unscaled.Height / 2)) <= 1 && red.Get("strokeWidth") == "6",
                    unscaled + " → " + scaled + ", line " + red.Get("strokeWidth"));
                _screen.Widgets.Remove(red);
                _screen.Widgets.Remove(alsoRed);
                _selection.Clear();
                SelectionChanged();
                ClearTestModules();
                try { System.IO.Directory.Delete(scratch, true); } catch { }
            }

            // ---------------- repeat: rows and columns, and around a circle
            {
                DashWidget Piece(double x, double y, double w, double h, string name = "LED", string rotate = "0")
                {
                    DashWidget piece = WidgetCatalog.Create("shape", x, y);
                    piece.Name = name; piece.X = x; piece.Y = y; piece.W = w; piece.H = h;
                    piece.Set("rotate", rotate);
                    return piece;
                }
                string Where(List<List<DashWidget>> sets) => string.Join(" ", sets.Select(s => s[0].X + "," + s[0].Y + "@" + s[0].Get("rotate")));

                DashWidget cell = Piece(100, 100, 40, 20);
                List<List<DashWidget>> gridSets = Repeater.Build(new[] { cell }, new RepeatSpec { Across = 3, Down = 2, GapX = 10, GapY = 6 });
                Check("repeat: a grid lays copies across then down, the gaps between them",
                    gridSets.Count == 5 && Where(gridSets) == "150,100@0 200,100@0 100,126@0 150,126@0 200,126@0", Where(gridSets));

                DashWidget tick = Piece(190, 80, 20, 40);
                List<List<DashWidget>> round = Repeater.Build(new[] { tick }, new RepeatSpec { Circle = true, Count = 4, Sweep = 360, Radius = 100, Start = -90, Follow = true });
                Check("repeat: all the way round, the original stays at the top and copies turn to follow the circle",
                    Where(round) == "290,180@90 190,280@180 90,180@-90", Where(round));
                List<List<DashWidget>> half = Repeater.Build(new[] { tick }, new RepeatSpec { Circle = true, Count = 5, Sweep = 180, Radius = 100, Start = -90, Follow = true });
                Check("repeat: half a circle ends where the arc ends",
                    half.Count == 4 && Where(half.Skip(3).ToList()) == "190,280@180", Where(half));
                List<List<DashWidget>> keptUpright = Repeater.Build(new[] { tick }, new RepeatSpec { Circle = true, Count = 4, Sweep = 360, Radius = 100, Start = -90, Follow = false, TurnEach = 10 });
                Check("repeat: kept upright, copies only take the extra turn each",
                    Where(keptUpright) == "290,180@10 190,280@20 90,180@30", Where(keptUpright));
                DashWidget live = Piece(190, 80, 20, 40, "Needle", "=Speed");
                List<List<DashWidget>> turned = Repeater.Build(new[] { live }, new RepeatSpec { Circle = true, Count = 2, Sweep = 180, Radius = 100, Start = 0, Follow = true });
                Check("repeat: a formula's turn is kept and added to", turned[0][0].Get("rotate") == "=(Speed) + 180", turned[0][0].Get("rotate"));

                // Numbered: each gets a group and the next Slot, named to match.
                DashWidget led = Piece(10, 10, 12, 12);
                var leds = new List<DashWidget> { led };
                List<List<DashWidget>> numbered = Repeater.Build(leds, new RepeatSpec { Across = 4, Down = 1, GapX = 4 });
                Repeater.Group(leds, numbered, true, "LED");
                Check("repeat: numbered copies count up as Slot, each in a group of its own",
                    led.Slot == 1 && led.GroupName == "LED" && numbered.Select(s => s[0].Slot).SequenceEqual(new double[] { 2, 3, 4 }) &&
                    numbered[2][0].GroupName == "LED" && numbered.Select(s => s[0].Group).Concat(new[] { led.Group }).Distinct().Count() == 4 && led.Group.Length > 0,
                    led.GroupName + " / " + string.Join(", ", numbered.Select(s => s[0].GroupName + " " + s[0].Slot)));
                DashWidget card = Piece(0, 0, 200, 30, "Name"), position = Piece(0, 0, 30, 30, "Position");
                card.Group = position.Group = "g-card"; card.GroupName = position.GroupName = "Driver"; card.Slot = position.Slot = 3;
                var driverRow = new List<DashWidget> { card, position };
                List<List<DashWidget>> rows = Repeater.Build(driverRow, new RepeatSpec { Across = 1, Down = 3, GapY = 4 });
                Repeater.Group(driverRow, rows, true, "Repeat");
                Check("repeat: a numbered group counts on from its own Slot, and each copy is one group",
                    card.Slot == 3 && card.GroupName == "Driver" && rows[0][0].Slot == 4 && rows[1][1].Slot == 5 && rows[1][1].GroupName == "Driver" &&
                    rows[0][0].Group == rows[0][1].Group && rows[0][0].Group != rows[1][0].Group && rows[0][0].Group != card.Group && rows[1][0].Y == 68,
                    string.Join(", ", rows.Select(s => s[0].GroupName + " " + s[0].Slot + " at " + s[0].Y)));
            }

            // ---------------- module options, and guides
            {
                var optioned = new DashLayout { Width = 60, Height = 40 };
                optioned.Theme.BackgroundStyle = "None";
                optioned.Screens.Add(new DashScreen { Name = "Main" });
                DashWidget paint = WidgetCatalog.Create("shape", 0, 0), label = WidgetCatalog.Create("text", 30, 0);
                paint.W = 30; paint.H = 40; paint.Set("strokeWidth", "0"); paint.Set("radius", "0"); paint.Set("fill", ModuleOptions.Formula("Paint"));
                label.W = 30; label.H = 40; label.Set("size", ModuleOptions.Formula("Size")); label.Set("text", "8");
                foreach (DashWidget w in new[] { paint, label }) { w.Group = "g-options"; w.GroupName = "Gauge"; optioned.Screens[0].Widgets.Add(w); }
                ModuleOptions.Set(optioned.Screens[0].Widgets, "Paint", "#FF0000");
                ModuleOptions.Set(optioned.Screens[0].Widgets, "Size", "20");
                EvalScope optionScope = new EvalScope { T = IRacingTelemetrySource.CreatePreviewSnapshot(), Accent = "#2DD4BF" };
                object size = Expr.Evaluate("option(\"Size\") * 2", LayoutRenderer.ForWidget(optionScope, label));
                Check("options: option() reads the group's value, as a number when it is one", Expr.Num(size) == 40, Expr.Text(size));

                System.Drawing.Color before, after;
                using (var renderer = new LayoutRenderer(60, 40, new DashboardSettings { Layout = Layouts.Custom, Design = optioned }, _units) { FixedNow = TestNow, FlagClock = 0 })
                {
                    using (System.Drawing.Bitmap frame = renderer.Render(optionScope.T, false, 4, 1, optioned.Screens[0])) before = frame.GetPixel(10, 20);
                    ModuleOptions.Set(optioned.Screens[0].Widgets, "Paint", "accent");
                    using (System.Drawing.Bitmap frame = renderer.Render(optionScope.T, false, 4, 1, optioned.Screens[0])) after = frame.GetPixel(10, 20);
                }
                Check("options: changing an option redraws what's linked to it, theme colors included",
                    before.R > 240 && before.G < 20 && after.G > 180 && after.R < 80, before + " → " + after);

                DashLayout.ScaleWidgets(optioned.Screens[0].Widgets, 2, 2);
                Check("options: an option linked to a size scales with the module, on every widget of the group",
                    paint.Get(ModuleOptions.Prefix + "Size") == "40" && label.Get(ModuleOptions.Prefix + "Size") == "40" && label.Get(ModuleOptions.Prefix + "Paint") == "accent",
                    paint.Get(ModuleOptions.Prefix + "Size") + " " + label.Get(ModuleOptions.Prefix + "Size"));
                ModuleOptions.Remove(optioned.Screens[0].Widgets, "Paint");
                Check("options: ending an option leaves its settings with its value", paint.Get("fill") == "accent" && paint.Get(ModuleOptions.Prefix + "Paint") == null && label.Get(ModuleOptions.Prefix + "Paint") == null,
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
                var sized = new DashLayout { Width = 800, Height = 480, GuidesX = new List<double> { 400 }, GuidesY = new List<double> { 120 } };
                sized.ScaleTo(1600, 960);
                Check("guides: widgets snap to a guide, and guides follow the screen to a new size",
                    Math.Abs(nudge - 2) < 0.001 && sized.GuidesX[0] == 800 && sized.GuidesY[0] == 240, nudge + " " + sized.GuidesX[0] + " " + sized.GuidesY[0]);
            }

            // ---------------- the Library explains itself
            {
                List<string> unexplained = WidgetCatalog.All.SelectMany(t => t.Props.Where(p => !p.Hidden && string.IsNullOrWhiteSpace(p.Help)).Select(p => t.Id + "." + p.Key)).ToList();
                List<string> undescribed = WidgetCatalog.All.Where(t => string.IsNullOrWhiteSpace(t.Description)).Select(t => t.Id)
                    .Concat(QuickValues.All().Concat(DesignBlocks.BuiltIn()).Where(b => string.IsNullOrWhiteSpace(b.Description)).Select(b => b.Name)).ToList();
                Check("library: every widget, block and setting says what it does", unexplained.Count == 0 && undescribed.Count == 0,
                    string.Join(", ", unexplained.Concat(undescribed).Take(12)));
            }

            // ---------------- the formula reference
            {
                // It's made from the live lists, so it covers everything; these keep its own parts complete and working.
                List<string> noExample = Functions.All.Where(f => !FormulaReferenceWindow.Examples.ContainsKey(f.Name)).Select(f => f.Name).ToList();
                List<string> broken = FormulaReferenceWindow.Examples.Where(e => Expr.Compile(e.Value).Error != null)
                    .Select(e => e.Key + ": " + Expr.Compile(e.Value).Error).ToList();
                Check("formula reference: every function has a worked example, and every example works", noExample.Count == 0 && broken.Count == 0,
                    string.Join("; ", noExample.Select(n => "no example for " + n).Concat(broken)));
                List<string> bare = DataCatalog.All.Where(f => string.IsNullOrWhiteSpace(f.Description)).Select(f => f.Name)
                    .Concat(Functions.All.Where(f => string.IsNullOrWhiteSpace(f.Description)).Select(f => f.Name + "()")).ToList();
                List<string> recipes = DataPicker.Recipes.Where(r => Expr.Compile(r.Formula).Error != null).Select(r => r.Name).ToList();
                Check("formula reference: every data field and function is described, and every recipe works", bare.Count == 0 && recipes.Count == 0,
                    string.Join(", ", bare.Concat(recipes)));
            }

            // ---------------- touch
            {
                // Reports the wheel screen sent while it was tapped at its top-left, top-right and bottom-right, then lifted.
                byte[] Report(params byte[] start) => start.Concat(new byte[] { 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF }).ToArray();
                WheelScreen.ReadTouch(Report(0, 0, 1, 0x80, 0x48, 0x03, 0x28), 14, ScreenModel.FiveInch, out bool downA, out System.Drawing.Point topLeft);
                WheelScreen.ReadTouch(Report(0, 0, 1, 0x80, 0x2E, 0x00, 0x2D), 14, ScreenModel.FiveInch, out bool downB, out System.Drawing.Point topRight);
                WheelScreen.ReadTouch(Report(0, 0, 1, 0x81, 0xB4, 0x00, 0x25), 14, ScreenModel.FiveInch, out bool downC, out System.Drawing.Point bottomRight);
                WheelScreen.ReadTouch(Report(0, 0, 0, 0x40, 0x48, 0x03, 0x28), 14, ScreenModel.FiveInch, out bool lifted, out _);
                Check("touch reports read as the corners that were tapped", downA && downB && downC && !lifted &&
                    topLeft == new System.Drawing.Point(45, 72) && topRight == new System.Drawing.Point(808, 46) && bottomRight == new System.Drawing.Point(816, 436),
                    topLeft + " " + topRight + " " + bottomRight);
                // A landscape panel's touches need no turning.
                WheelScreen.ReadTouch(Report(0, 0, 1, 0x80, 0x48, 0x00, 0x28), 14, ScreenModel.For(0x7, 0), out bool downWide, out System.Drawing.Point wide);
                Check("touches on a landscape panel read straight across", downWide && wide == new System.Drawing.Point(72, 40), wide.ToString());

                // ---------------- sending to the screen
                var random = new Random(5);
                int[] Picture(int w, int h)
                {
                    var pixels = new int[w * h];
                    for (int i = 0; i < pixels.Length; i++) pixels[i] = random.Next() | unchecked((int)0xFF000000);
                    return pixels;
                }
                ScreenModel five = ScreenModel.FiveInch;
                int[] picture = Picture(five.Width, five.Height);
                // The original whole-frame conversion, pixel by pixel: RGB565, turned a quarter onto the portrait panel.
                var reference = new byte[five.Width * five.Height * 2];
                for (int row = 0; row < five.Height; row++)
                    for (int column = 0; column < five.Width; column++)
                    {
                        int c = picture[row * five.Width + column];
                        int pixel = (((c >> 16) & 0xFF) >> 3 << 11) | (((c >> 8) & 0xFF) >> 2 << 5) | ((c & 0xFF) >> 3);
                        int index = ((five.Width - 1 - column) * five.Height + row) * 2;
                        reference[index] = (byte)pixel;
                        reference[index + 1] = (byte)(pixel >> 8);
                    }
                var encoded = new byte[five.Width * five.Height * 2 + five.FramePadding];
                FrameEncoder.Encode(picture, five.Width, new System.Drawing.Rectangle(0, 0, five.Width, five.Height), five, encoded);
                Check("a whole frame encodes exactly as the screen takes it", reference.SequenceEqual(encoded.Take(reference.Length)));

                // Any part, put where Encode says it goes on the panel, matches the whole frame there.
                bool PartsLand(ScreenModel model)
                {
                    int[] pixels = Picture(model.Width, model.Height);
                    var whole = new byte[model.PanelWidth * model.PanelHeight * 2];
                    FrameEncoder.Encode(pixels, model.Width, new System.Drawing.Rectangle(0, 0, model.Width, model.Height), model, whole);
                    var part = new byte[whole.Length];
                    foreach (System.Drawing.Rectangle area in new[] { new System.Drawing.Rectangle(0, 0, 16, 16), new System.Drawing.Rectangle(37, 5, 101, 64), new System.Drawing.Rectangle(model.Width - 48, model.Height - 32, 48, 32) })
                    {
                        System.Drawing.Rectangle panel = FrameEncoder.Encode(pixels, model.Width, area, model, part);
                        if (panel.Width * panel.Height != area.Width * area.Height) return false;
                        for (int row = 0; row < panel.Height; row++)
                            for (int column = 0; column < panel.Width * 2; column++)
                                if (part[row * panel.Width * 2 + column] != whole[((panel.Y + row) * model.PanelWidth + panel.X) * 2 + column]) return false;
                    }
                    return true;
                }
                Check("parts of a frame land where they belong on portrait and landscape panels",
                    PartsLand(five) && PartsLand(ScreenModel.For(0x7, 0)) && PartsLand(ScreenModel.For(0x1005, 0)) && PartsLand(ScreenModel.For(0x403, 0)));

                // Nothing changed; two small changes far apart; most of the screen.
                int[] shown = (int[])picture.Clone(), next = (int[])picture.Clone();
                bool nothing = FrameEncoder.Changes(next, shown, five.Width, five.Height).Count == 0;
                next[10 * five.Width + 20] ^= 1;
                next[400 * five.Width + 700] ^= 1;
                List<System.Drawing.Rectangle> two = FrameEncoder.Changes(next, shown, five.Width, five.Height);
                bool small = two.Count == 2 && two.Any(r => r.Contains(20, 10) && r.Width * r.Height <= 256) && two.Any(r => r.Contains(700, 400) && r.Width * r.Height <= 256);
                for (int i = 0; i < next.Length * 3 / 4; i++) next[i] ^= 1;
                List<System.Drawing.Rectangle> most = FrameEncoder.Changes(next, shown, five.Width, five.Height);
                Check("only what changed is sent, and most of the screen goes as a whole frame",
                    nothing && small && most.Count == 1 && most[0] == new System.Drawing.Rectangle(0, 0, five.Width, five.Height), string.Join(" ", two));

                // Every M-PRO model a screen can report.
                ScreenModel oled = ScreenModel.For(0x1005, 0), wide68 = ScreenModel.For(0x7, 0), ten = ScreenModel.For(0xA, 0), newer = ScreenModel.For(0x99, 0);
                Check("M-PRO models: their sizes, which way the picture goes, and the 5\"'s padding",
                    five.Width == 854 && five.Height == 480 && five.Turned && ScreenModel.For(0x5, 0).FramePadding == 320 && ScreenModel.For(0x5, 3).FramePadding == 0 &&
                    oled.Width == 1280 && oled.Height == 720 && oled.Turned && !oled.WholeFrameCommand &&
                    wide68.Width == 800 && wide68.Height == 480 && !wide68.Turned && ten.Width == 1024 && ten.Height == 600 &&
                    newer.Width == 800 && newer.Height == 480 && ScreenModel.For(0xFFFFFFFF, 0) == five);

                DashboardSettings roady = DashboardSettings.CreateDesigned(DesignTemplates.Road);
                using (var renderer = new LayoutRenderer(854, 480, roady, _units))
                {
                    TelemetrySnapshot snapshot = IRacingTelemetrySource.CreatePreviewSnapshot();
                    DashWidget fuelPanel = roady.Design.Screens[0].Widgets.First(w => w.Name == "Fuel panel");
                    int before = renderer.StateHash(snapshot, false, -1);
                    using (renderer.Render(snapshot, false, 4, 1)) { }
                    bool hit = renderer.Tap(fuelPanel.X + 20, fuelPanel.Y + 20, snapshot);
                    int after = renderer.StateHash(snapshot, false, -1);
                    bool missed = !renderer.Tap(427, 150, snapshot);
                    renderer.Tap(fuelPanel.X + 20, fuelPanel.Y + 20, snapshot);
                    Check("a tap on a glass panel moves it to its next view and back, and a tap elsewhere does nothing",
                        hit && missed && before != after && renderer.Taps.TryGetValue("fuel", out int view) && view == 0);
                }

                var saved = new AppSettings { SchemaVersion = 11 };
                DashboardSettings oldRoady = DashboardSettings.CreateRoad(), oldRoundy = DashboardSettings.CreateOval();
                foreach (DashboardSettings d in new[] { oldRoady, oldRoundy })
                    foreach (DashWidget w in d.Design.AllWidgets().ToList())
                    {
                        if (!string.IsNullOrEmpty(w.Get("tapName", ""))) { w.Props.Remove("tapName"); w.Props.Remove("tapViews"); }
                        if ((w.Get("visible", "") ?? "").Contains("tap(")) d.Design.Screens.ForEach(s => s.Widgets.Remove(w));
                    }
                foreach (DashScreen s in oldRoady.Design.Screens.Concat(oldRoundy.Design.Screens))
                    foreach (DashWidget w in s.Widgets) if ((w.Get("visible", "") ?? "").Contains("tap(")) w.Props.Remove("visible");
                saved.Dashboards.Add(oldRoady);
                saved.Dashboards.Add(oldRoundy);
                int widgets = oldRoady.Design.AllWidgets().Count();
                saved.EnsureBuiltInDashboards();
                saved.EnsureBuiltInDashboards();
                Check("saved flagships gain their tap views once", oldRoady.Design.AllWidgets().Count(w => !string.IsNullOrEmpty(w.Get("tapName", ""))) == 3 &&
                    oldRoundy.Design.AllWidgets().Count(w => !string.IsNullOrEmpty(w.Get("tapName", ""))) == 1 && oldRoady.Design.AllWidgets().Count() == widgets + 8,
                    widgets + " -> " + oldRoady.Design.AllWidgets().Count());
            }

            // ---------------- dropping onto a panel builds its views
            {
                DashWidget pane = WidgetCatalog.Create("panel", 500, 300);
                pane.W = 300; pane.H = 120; pane.Name = "Engine panel";
                _screen.Widgets.Add(pane);
                DashWidget oil = WidgetCatalog.Create("metric", 520, 310), water = WidgetCatalog.Create("metric", 900, 310), volts = WidgetCatalog.Create("metric", 600, 330);
                bool found = PanelAt(new System.Windows.Point(650, 360)) == pane && PanelAt(new System.Windows.Point(100, 100)) == null;
                DropOnPanel(pane, new List<DashWidget> { oil }, "Add oil", false);
                bool joined = !string.IsNullOrEmpty(pane.Group) && oil.Group == pane.Group && string.IsNullOrEmpty(pane.Get("tapName", "")) && string.IsNullOrEmpty(oil.Get("visible", ""));
                DropOnPanel(pane, new List<DashWidget> { water }, "Add water", true);
                DropOnPanel(pane, new List<DashWidget> { volts }, "Add volts", false);
                Check("dropping onto a panel joins it, and a new view writes the tap setup", found && joined &&
                    pane.Get("tapName") == "engine" && pane.Get("tapViews") == "2" && oil.Get("visible") == "tap(\"engine\") == 0" &&
                    water.Get("visible") == "tap(\"engine\") == 1" && volts.Get("visible") == "tap(\"engine\") == 1" &&
                    water.X + water.W <= pane.X + pane.W && new[] { water, volts }.All(w => w.Group == pane.Group) && _designTaps["engine"] == 1,
                    pane.Get("tapName", "") + " " + oil.Get("visible", "") + " / " + water.Get("visible", "") + " / " + volts.Get("visible", "") + " water at " + water.X);
                foreach (DashWidget w in new[] { pane, oil, water, volts }) _screen.Widgets.Remove(w);
                _designTaps.Clear();
                Select(null);
                Commit("Self-test cleanup");
            }

            // ---------------- screen sizes
            {
                DashLayout sized = DesignTemplates.Blank();
                DashWidget label = WidgetCatalog.Create("text", 427, 240);
                label.W = 100; label.H = 50;
                label.Set("size", "20");
                label.Set("color", "=Speed > 100 ? $bad : $text");
                DashWidget bar = WidgetCatalog.Create("bar", 0, 0);
                bar.Set("radius", "-1");
                bar.Set("thickness", "60");
                sized.Screens[0].Widgets.Add(label);
                sized.Screens[0].Widgets.Add(bar);
                sized.ScaleTo(1280, 720);
                Check("a design moves to another screen size, scaling what's on it", sized.Width == 1280 && sized.Height == 720 &&
                    label.X == 640 && label.Y == 360 && label.W == 150 && label.H == 75 && label.Get("size") == "30" && label.Get("color").StartsWith("=") &&
                    bar.Get("radius") == "-1" && bar.Get("thickness") == "60", label.X + "," + label.Y + " " + label.W + "x" + label.H + " size " + label.Get("size"));

                DashboardSettings wide = DashboardSettings.CreateDesigned(DesignTemplates.Oval, 1024, 600);
                using (var renderer = new LayoutRenderer(wide.ScreenSize().Width, wide.ScreenSize().Height, wide, _units))
                using (System.Drawing.Bitmap frame = renderer.Render(IRacingTelemetrySource.CreatePreviewSnapshot(), false, 4, 1))
                using (System.Drawing.Bitmap fitted = ScreenSizes.Fit(frame, 854, 480))
                    Check("a dashboard is drawn at its own size and fitted to a screen of another", frame.Width == 1024 && frame.Height == 600 && fitted.Width == 854 && fitted.Height == 480
                        && renderer.Errors.Count == 0, string.Join("; ", renderer.Errors.Values));

                // The designer's canvas follows the design's size, and back again on undo.
                int before = _frame.PixelWidth;
                _layout.ScaleTo(1024, 600);
                Commit("Self-test resize");
                RenderNow();
                bool followed = _frame.PixelWidth == 1024 && _frame.PixelHeight == 600 && Math.Abs(Overlay.Width - 1024) < 0.1;
                Undo_Click(null, null);
                RenderNow();
                Check("the designer's canvas follows the screen size, and undo puts it back", followed && _frame.PixelWidth == before && _layout.Width == before, _frame.PixelWidth + "x" + _frame.PixelHeight);
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
                var softRoady = new AppSettings { SchemaVersion = 10 };
                DashboardSettings soft = DashboardSettings.CreateRoad();
                foreach (DashWidget w in soft.Design.Screens.SelectMany(sc => sc.Widgets).Where(w => w.Type == "panel" && w.Get("style") == "Liquid")) w.Set("style", "Soft");
                softRoady.Dashboards.Add(soft);
                softRoady.EnsureBuiltInDashboards();
                Check("Roady's panels become Liquid glass once", soft.Design.Screens.SelectMany(sc => sc.Widgets).Count(w => w.Type == "panel" && w.Get("style") == "Liquid") >= 7
                    && !soft.Design.Screens.SelectMany(sc => sc.Widgets).Any(w => w.Get("style") == "Soft"));
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
