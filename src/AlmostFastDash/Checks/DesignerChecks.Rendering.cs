using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AlmostFastDash.Designer
{
    public partial class DesignerWindow
    {
        /// <summary>Checks: every template renders in every state without widget errors.</summary>
        private void CheckTemplatesRender()
        {
            TelemetrySnapshot[] states = _states;
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
        }

        /// <summary>Checks: cost per frame on the device path (render + change check).</summary>
        private void CheckFrameCost()
        {
            List<string> report = _report;
            string scratchFolder = _scratchFolder;
            TelemetrySnapshot[] states = _states;
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
                using (IDashboardRenderer designed = DashboardRendererFactory.Create(854, 480, DashboardSettings.CreateDesigned(template),
                    _units))
                    report.Add("INFO  " + template.Id + ": " + Time(designed).ToString("0.0") + " ms per frame");
            // Where the time goes in the flagship.
            DashLayout halo = DesignTemplates.Road.Create();
            var costs = new List<string>();
            foreach (DashWidget widget in halo.Screens[0].Widgets.Concat(new DashWidget[] { null }))
            {
                DashLayout single = DesignTemplates.Road.Create();
                single.Screens[0].Widgets = widget == null ? new List<DashWidget>() : new List<DashWidget> { widget };
                using (var renderer = new LayoutRenderer(854, 480,
                    new DashboardSettings { Layout = Layouts.Custom, Design = single, AccentColor = "#22D3EE" }, _units))
                    costs.Add((widget?.Name ?? "background only") + " " + Time(renderer).ToString("0.0"));
            }
            report.Add("INFO  halo cost by widget (ms): " + string.Join(", ", costs));
            using (var profiled = new LayoutRenderer(854, 480, DashboardSettings.CreateDesigned(DesignTemplates.Road),
                _units) { Timings = new Dictionary<string, double>() })
            {
                TelemetrySnapshot snapshot = IRacingTelemetrySource.CreatePreviewSnapshot();
                using (profiled.Render(snapshot, false, 4, 1)) { }
                profiled.Timings.Clear();
                var watch = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < 30; i++) using (profiled.Render(snapshot, false, 4, 1)) { }
                double frame = watch.Elapsed.TotalMilliseconds / 30, widgets = profiled.Timings.Values.Sum() / 30;
                report.Add("INFO  halo frame " + frame.ToString("0.00") + " ms: live widgets " + widgets.ToString("0.00") + " ms over " +
                    profiled.Timings.Count + " widgets; slowest " +
                    string.Join(", ", profiled.Timings.OrderByDescending(p => p.Value).Take(8)
                    .Select(p => p.Key + " " + (p.Value / 30).ToString("0.00"))));
            }

            using (var designed = new LayoutRenderer(854, 480, DashboardSettings.CreateDesigned(DesignTemplates.Road), _units))
            {
                TelemetrySnapshot snapshot = IRacingTelemetrySource.CreatePreviewSnapshot();
                var watch = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < 200; i++) designed.StateHash(snapshot, false, -1);
                report.Add("INFO  Halo change check: " + (watch.Elapsed.TotalMilliseconds / 200)
                    .ToString("0.000") + " ms (runs 20 times a second)");
            }

            // The static-layer cache must not change a single pixel.
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
                Check("static cache matches a plain render: " + template.Name, mismatches == 0,
                    mismatches + " of " + states.Length + " states differ beyond rounding (largest " + worst + "/255); " + baked +
                    " widgets baked");
            }

            // The largest difference in any color channel between two frames.
            int Largest(System.Drawing.Bitmap x, System.Drawing.Bitmap y)
            {
                int most = 0;
                for (int py = 0; py < x.Height; py++)
                    for (int px = 0; px < x.Width; px++)
                    {
                        System.Drawing.Color p = x.GetPixel(px, py), q = y.GetPixel(px, py);
                        most = Math.Max(most,
                            Math.Max(Math.Abs(p.A - q.A),
                            Math.Max(Math.Abs(p.R - q.R), Math.Max(Math.Abs(p.G - q.G), Math.Abs(p.B - q.B)))));
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
                using (var incremental = new LayoutRenderer(854, 480, d, _units) { FlagClock = 0, FixedNow = TestNow,
                    Timings = new Dictionary<string, double>() })
                using (var whole = new LayoutRenderer(854, 480, d, _units) { FlagClock = 0, FixedNow = TestNow, RedrawChangesOnly = false,
                    Timings = new Dictionary<string, double>() })
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
                                if (i == 89) changed = string.Join(", ", incremental.Timings.Where(p => p.Key.StartsWith("changed: "))
                                    .OrderByDescending(p => p.Value).Take(14).Select(p => p.Key.Substring(9) + " " + p.Value));
                                if (i == 89) costly = string.Join(", ", whole.Timings.Where(p => !p.Key.StartsWith("changed: "))
                                    .OrderByDescending(p => p.Value).Take(6).Select(p => p.Key + " " + (p.Value / 90).ToString("0.00")));
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
                                        string[] at = firstDiff.Split(new[] { " within " },
                                            StringSplitOptions.None)[1].Split(' ')[0].Split(',');
                                        var spot = new System.Drawing.PointF(float.Parse(at[0]), float.Parse(at[1]));
                                        firstDiff += "; widgets there: " + string.Join(", ", d.Design.Screens[0].Widgets
                                            .Where(w => System.Drawing.RectangleF.Inflate(w.Bounds, 24, 24).Contains(spot))
                                            .Select(w => w.Name + " [" + w.Type + " " + w.X + "," + w.Y + " " + w.W + "x" + w.H + "]"));
                                        using (var map = new System.Drawing.Bitmap(a))
                                        {
                                            for (int py = 0; py < a.Height; py++)
                                                for (int px = 0; px < a.Width; px++)
                                                    if (a.GetPixel(px, py).ToArgb() != b.GetPixel(px, py).ToArgb()) map.SetPixel(px, py,
                                                        System.Drawing.Color.Magenta);
                                            map.Save(Path.Combine(scratchFolder, "diff-" + template.Id + ".png"));
                                        }
                                    }
                                }
                            }
                        }
                    }
                Check("redrawing only changes matches a full redraw: " + template.Name, mismatches == 0,
                    mismatches + " of 90 frames differ" + (rounded > 0 ? ", " + rounded + " within 3/255 rounding" : "") + "; " +
                    firstDiff);
                if (frames > 0)
                    report.Add("INFO  " + template.Id + " animated lap: " + (fast / frames * 1000 / System.Diagnostics.Stopwatch.Frequency)
                        .ToString("0.00") + " ms per frame redrawing changes (" +
                        (share / frames * 100)
                        .ToString("0") + "% of the screen) vs " + (slow / frames * 1000 / System.Diagnostics.Stopwatch.Frequency)
                        .ToString("0.00") + " ms redrawing everything; changed most: " + changed + "; costliest (ms/frame): " + costly);
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
        }

        /// <summary>Checks: flags: alert, then step aside.</summary>
        private void CheckFlags()
        {
            List<string> report = _report;
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
                    fading.Alpha > 0 && fading.Alpha < 1 && later.Alpha == 0 && later.Frame == RacingFlag.Yellow,
                    "alpha " + fading.Alpha + " then " + later.Alpha + ", frame " + later.Frame);

                var black = new FlagTimeline(150);
                black.At(Flagged(RacingFlag.Black), 0, true, true);
                FlagView alert = black.At(Flagged(RacingFlag.Black), 1000, true, true);
                FlagView aside = black.At(Flagged(RacingFlag.Black), 5000, true, true);
                Check("flag: black takes the screen, then steps aside to a frame",
                    alert.Full && aside.Alpha == 0 && aside.Frame == RacingFlag.Black);

                var blue = new FlagTimeline(150);
                blue.At(Flagged(RacingFlag.BlueYellowStripe), 0, true, true);
                blue.At(Flagged(RacingFlag.BlueYellowStripe), 4000, true, true);
                blue.At(Flagged(RacingFlag.None), 6000, true, true);
                FlagView gone = blue.At(Flagged(RacingFlag.None), 7000, true, true);
                blue.At(Flagged(RacingFlag.BlueYellowStripe), 10000, true, true);
                FlagView back = blue.At(Flagged(RacingFlag.BlueYellowStripe), 10500, true, true);
                Check("flag: a blue flag back within seconds goes straight to its frame",
                    gone.Frame == RacingFlag.None && back.Alpha == 0 && back.Frame == RacingFlag.BlueYellowStripe);

                var red = new FlagTimeline(150);
                red.At(Flagged(RacingFlag.Red), 0, true, true);
                Check("flag: red keeps the screen", red.At(Flagged(RacingFlag.Red), 60000, true, true).Full);

                var framesOnly = new FlagTimeline(150);
                framesOnly.At(Flagged(RacingFlag.Green), 0, false, true);
                FlagView framed = framesOnly.At(Flagged(RacingFlag.Green), 1000, false, true);
                Check("flag: with full-screen flags off, a flag shows as a frame", framed.Alpha == 0 && framed.Frame == RacingFlag.Green);
                Check("flag: with frames off, a caution shows no frame", new FlagTimeline(150)
                    .At(Flagged(RacingFlag.None, true), 0, true, false).Frame == RacingFlag.None);

                // On a dashboard: the repair flag covers it, then hands it back with a frame, and leaves nothing behind when it goes.
                DashboardSettings road = DashboardSettings.CreateDesigned(DesignTemplates.Road);
                using (var flagged = new LayoutRenderer(854, 480, road, _units) { FixedNow = TestNow, FlagClock = 0 })
                using (var clean = new LayoutRenderer(854, 480, DashboardSettings.CreateDesigned(DesignTemplates.Road),
                    _units) { FixedNow = TestNow, FlagClock = 0 })
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
                    using (var b = flagged.Render(repair, false, 4, 1)) handedBack = b.GetPixel(427, 240)
                        .ToArgb() == middle && b.GetPixel(1, 240).ToArgb() != edge;
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
                using (var timed = new LayoutRenderer(854, 480, DashboardSettings.CreateDesigned(DesignTemplates.Road),
                    _units) { FixedNow = TestNow, FlagClock = 0 })
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
                    report.Add("INFO  caution frame over the road template: " + Cost(yellow)
                        .ToString("0.00") + " ms per frame vs " + Cost(plain).ToString("0.00") + " ms without");
                }
                TelemetrySnapshot finish = Flagged(RacingFlag.Checkered), better = Flagged(RacingFlag.Checkered);
                better.StartPosition = 12;
                Check("flag: the finish screen changes with the result",
                    FlagPainter.Key(RacingFlag.Checkered, finish) != FlagPainter.Key(RacingFlag.Checkered, better));
            }

            foreach (WidgetType type in WidgetCatalog.All)
            {
                var layout = DesignTemplates.Blank();
                layout.Screens[0].Widgets.Add(WidgetCatalog.Create(type.Id, 100, 100));
                using (var renderer = new LayoutRenderer(854, 480, new DashboardSettings { Layout = Layouts.Custom, Design = layout },
                    _units))
                using (renderer.Render(IRacingTelemetrySource.CreatePreviewSnapshot(RacingFlag.Yellow), false, 4, 1, layout.Screens[0]))
                    Check("widget draws with defaults: " + type.Name, renderer.Errors.Count == 0,
                        string.Join("; ", renderer.Errors.Values));
            }
        }

        /// <summary>Checks: gauge shapes.</summary>
        private void CheckGaugeShapes()
        {
            foreach (DesignBlock block in DesignBlocks.BuiltIn())
            {
                var layout = DesignTemplates.Blank();
                foreach (DashWidget w in block.Create()) { w.X += 100; w.Y += 60; layout.Screens[0].Widgets.Add(w); }
                var errors = new List<string>();
                using (var renderer = new LayoutRenderer(854, 480, new DashboardSettings { Layout = Layouts.Custom, Design = layout },
                    _units))
                    foreach (TelemetrySnapshot snapshot in new[] { IRacingTelemetrySource.CreatePreviewSnapshot(), ShiftSnapshot(),
                        TelemetrySnapshot.Disconnected })
                        using (renderer.Render(snapshot, true, 4, 1, layout.Screens[0]))
                            errors.AddRange(renderer.Errors.Values);
                Check("block draws: " + block.Name, errors.Count == 0, string.Join("; ", errors.Distinct().Take(3)));
            }
            Check("every built-in block is in a folder", DesignBlocks.BuiltIn().All(b => b.Folder != "Other"));
        }

        /// <summary>Checks: cost of glowing gauges whose value changes every frame.</summary>
        private void CheckGlowingGaugeCost()
        {
            List<string> report = _report;
            foreach (string block in new[] { "Swoosh rev bar", "Tapered rev ring", "Tach dial", "Arched rev bar" })
            {
                var layout = DesignTemplates.Blank();
                List<DashWidget> parts = DesignBlocks.BuiltIn().First(b => b.Name == block).Create();
                DashWidget gauge = parts[0];
                foreach (DashWidget w in parts) { w.X += 100; w.Y += 60; layout.Screens[0].Widgets.Add(w); }
                using (var renderer = new LayoutRenderer(854, 480, new DashboardSettings { Layout = Layouts.Custom, Design = layout },
                    _units))
                {
                    TelemetrySnapshot snapshot = IRacingTelemetrySource.CreatePreviewSnapshot();
                    gauge.Set("value", "0.5");
                    using (renderer.Render(snapshot, false, 4, 1, layout.Screens[0])) { }
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    for (int i = 0; i < 120; i++)
                    {
                        gauge.Set("value", ((i * 7 % 100) / 100.0)
                            .ToString(System.Globalization.CultureInfo.InvariantCulture) + (gauge.Type == "arc" ? "" : ""));
                        if (gauge.Get("max") != null && gauge.Type == "arc" && block == "Tach dial") gauge.Set("value", (i * 7 % 90 / 10.0)
                            .ToString(System.Globalization.CultureInfo.InvariantCulture));
                        using (renderer.Render(snapshot, false, 4, 1, layout.Screens[0])) { }
                    }
                    report.Add("INFO  " + block + " with a changing value: " + (watch.Elapsed.TotalMilliseconds / 120)
                        .ToString("0.00") + " ms per frame");
                }
            }
        }

        /// <summary>Checks: flip and rotate.</summary>
        private void CheckFlipAndRotate()
        {
            Check("flipped bar fills from the other side",
                Sample(FillBar(false), 110, 120).R > 200 && Sample(FillBar(false), 490, 120).R < 50 &&
                Sample(FillBar(true), 490, 120).R > 200 && Sample(FillBar(true), 110, 120).R < 50);
            Check("rotated bar turns about its middle",
                Sample(FillBar(false, "-90"), 300, 290).R > 200 && Sample(FillBar(false, "-90"), 110, 120).R < 50);
        }

        /// <summary>Checks: fill directions.</summary>
        private void CheckFillDirections()
        {
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
            Check("older fill settings carry over",
                oldBar.Get("fill") == FillOrder.EndToStart && !oldBar.Props.ContainsKey("reverse") &&
                oldLights.Get("fill") == FillOrder.CenterOut);

            DashWidget turnedPanel =WidgetCatalog.Create("panel", 300, 150);
            turnedPanel.Set("rotate", "30");
            turnedPanel.Set("opacity", "60");
            Check("rotated widget is cached without clipping", Sample(turnedPanel, 330, 140)
                .ToArgb() == Sample(SettingsStore.Clone(turnedPanel), 330, 140, false).ToArgb());

            var box = new System.Drawing.RectangleF(10, 20, 400, 100);
            BarWarp Warp(double bend, double peak, double taper, double bulge, double skew, double thick, string anchor,
                bool vertical = false) =>
                new BarWarp(box, vertical, bend, peak, taper, bulge, skew, thick, anchor);
            float Across(BarWarp w, float x)
            {
                System.Drawing.PointF a = w.Map(x, 0), b = w.Map(x, w.Thickness);
                return (float)Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
            }
            BarWarp flatBar = Warp(0, 0.5, 0, 0, 0, 1, "Middle");
            Check("plain bar is not warped",
                flatBar.IsStraight && flatBar.Map(0, 0) == new System.Drawing.PointF(10, 20) && flatBar.Map(400,
                100) == new System.Drawing.PointF(410, 120));
            BarWarp upright = Warp(0, 0.5, 0, 0, 0, 1, "Middle", true);
            Check("vertical bar starts at the bottom",
                upright.Map(0, 0) == new System.Drawing.PointF(10, 120) && upright.Map(upright.Length, 0) == new System.Drawing.PointF(10,
                20));
            bool inside = true;
            foreach (BarWarp w in new[] { Warp(1, 0.5, 0, 0, 0, 0.3, "Middle"), Warp(-0.7, 1, 0.9, 0, 0, 0.5, "Middle"),
                Warp(0.4, 0, -0.5, 0.8, 30, 0.6, "Bottom edge"),
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
            Check("bend peaking at the end rises toward the end",
                swoosh.Map(swoosh.Length, swoosh.Thickness / 2).Y < swoosh.Map(0, swoosh.Thickness / 2).Y - 30);
            BarWarp arched = Warp(0, 0.5, 0, 1, 0, 1, "Bottom edge");
            Check("bulge from the bottom edge keeps the base flat and arches the top",
                Math.Abs(arched.Map(0, arched.Thickness).Y - arched.Map(arched.Length / 2, arched.Thickness).Y) < 0.5f &&
                Across(arched, arched.Length / 2) > Across(arched, 0) * 1.8f);
            BarWarp slanted = Warp(0, 0.5, 0, 0, 25, 1, "Middle");
            Check("slant leans the top forward", slanted.Map(0, 0).X > slanted.Map(0, slanted.Thickness).X + 20);
            var ring = new ArcRing { Center = new System.Drawing.PointF(150, 150), RadiusX = 120, RadiusY = 80, Thickness = 20,
                Taper = 0.8f, Start = 135, Sweep = 270 };
            Check("tapered ring grows along its sweep",
                !ring.Classic && ring.ThicknessAt(135) < 6 && Math.Abs(ring.ThicknessAt(405) - 20) < 0.01f);
            System.Drawing.RectangleF ringBounds;
            using (var band = ring.Band(135, 270, true)) ringBounds = band.GetBounds();
            Check("oval ring follows its box",
                ringBounds.Width > ringBounds.Height * 1.3f && ringBounds.Right < 150 + 120 + 11 && ringBounds.Bottom < 150 + 80 + 11);
        }

        /// <summary>Checks: graph and track map.</summary>
        private void CheckGraphAndTrackMap()
        {
            {
                var layout = DesignTemplates.Blank();
                DashWidget graph = WidgetCatalog.Create("graph", 100, 100);
                graph.Set("value", "=Rpm");
                graph.Set("seconds", "4");
                layout.Screens[0].Widgets.Add(graph);
                using (var renderer = new LayoutRenderer(854, 480, new DashboardSettings { Layout = Layouts.Custom, Design = layout },
                    _units))
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
                    GraphHistory history = renderer.Graph(new WidgetContext(renderer, null,
                        renderer.Scope(TelemetrySnapshot.Disconnected, false), graph, WidgetCatalog.Find("graph")));
                    Check("graph samples on a steady clock and scrolls", history.Count >= 55 && history.Count <= 61 && first != last,
                        history.Count + " samples");
                }
                var lapFractions = new List<double>(); var xs = new List<double>(); var ys = new List<double>();
                for (int i = 0; i <= 1000; i++)
                {
                    double f = i / 1000.0 * 0.999, a = f * 2 * Math.PI;
                    lapFractions.Add(f); xs.Add(Math.Cos(a) * 300 + 3 * f); ys.Add(Math.Sin(a) * 150 + 2 * f);
                }
                TrackMap traced = TrackMap.FromLap("test", "Test", lapFractions, xs, ys);
                Check("a clean lap becomes a closed track map",
                    traced != null && traced.Valid && Math.Abs(traced.At(0.25).Y - 150) < 6 && Math.Abs(traced.At(0).X - 300) < 1);
                lapFractions.RemoveRange(400, 100); xs.RemoveRange(400, 100); ys.RemoveRange(400, 100);
                Check("a lap with a gap makes no map", TrackMap.FromLap("test", "Test", lapFractions, xs, ys) == null);
            }
        }

        /// <summary>Checks: Style effects on a single widget.</summary>
        private void CheckStyleEffects()
        {
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
                    using (var renderer = new LayoutRenderer(854, 480, new DashboardSettings { Layout = Layouts.Custom, Design = layout },
                        _units))
                        return renderer.Render(IRacingTelemetrySource.CreatePreviewSnapshot(), false, 4, 1, layout.Screens[0]);
                }
                bool LitOutside(System.Drawing.Bitmap f) => Enumerable.Range(0, 40)
                    .Any(k => f.GetPixel(290 + k * 7, 196).ToArgb() != System.Drawing.Color.Black.ToArgb() || f.GetPixel(290 + k * 7, 266)
                    .ToArgb() != System.Drawing.Color.Black.ToArgb());
                using (System.Drawing.Bitmap plainText = Frame())
                using (System.Drawing.Bitmap shadow = Frame("fxShadow", "10", "fxShadowY", "30", "fxShadowColor", "#FF00A0FF"))
                using (System.Drawing.Bitmap glow = Frame("fxGlow", "24", "fxGlowColor", "#FF0000"))
                using (System.Drawing.Bitmap outline = Frame("fxOutline", "3", "fxOutlineColor", "#00FF00"))
                using (System.Drawing.Bitmap recolor = Frame("fxFill", "Color", "fxFillColor", "#FF0000"))
                {
                    Check("plain text stays inside its box", !LitOutside(plainText));
                    Check("a long shadow and a wide glow reach past the widget's box", LitOutside(shadow) && LitOutside(glow));
                    Check("every effect changes the picture",
                        Pixels(shadow) != Pixels(plainText) && Pixels(glow) != Pixels(plainText) && Pixels(outline) != Pixels(plainText) &&
                        Pixels(recolor) != Pixels(plainText));
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
                    Check("outline is drawn in its color", Enumerable.Range(0, 200)
                        .Any(k => outline.GetPixel(300 + k, 230).G > 200 && outline.GetPixel(300 + k, 230).R < 80));
                }
            }
        }

        /// <summary>Checks: turned widgets on the canvas.</summary>
        private void CheckTurnedWidgets()
        {
            {
                DashWidget turned = WidgetCatalog.Create("panel", 300, 200);
                turned.W = 200;
                turned.H = 40;
                turned.Set("rotate", "90");
                _screen.Widgets.Add(turned);
                // Turned a quarter about (400, 220) it covers x 380-420, y 120-320.
                Check("a turned widget is picked where it is drawn",
                    WidgetAt(new System.Windows.Point(400, 140)) == turned && WidgetAt(new System.Windows.Point(310, 220)) != turned);
                Select(turned);
                _originals = new Dictionary<DashWidget, System.Windows.Rect> { [turned] = RectOf(turned) };
                _handle = 3; // the right-hand handle, which now sits at the bottom
                DoResize(new System.Windows.Point(400, 360), true, false);
                // 40 longer, with the far end (the top, at y 120) left where it was.
                Check("a turned widget resizes from its own edges",
                    turned.W == 240 && turned.H == 40 && Math.Abs(turned.X - 280) < 0.6 && Math.Abs(turned.Y - 220) < 0.6,
                    turned.X + "," + turned.Y + " " + turned.W + "x" + turned.H);
                _screen.Widgets.Remove(turned);
                Select(null);
            }
        }

        /// <summary>Checks: Liquid Glass.</summary>
        private void CheckLiquidGlass()
        {
            List<string> report = _report;
            {
                // A striped backdrop, so the rim's bending shows.
                int sw = 400, sh = 300;
                var backdrop = new int[sw * sh];
                for (int i = 0; i < backdrop.Length; i++) backdrop[i] = (i % sw / 10 % 2 == 0) ? unchecked((int)0xFF203040)
                    : unchecked((int)0xFF8090A0);
                using (var shape = new System.Drawing.Drawing2D.GraphicsPath())
                {
                    shape.AddRectangle(new System.Drawing.RectangleF(50, 50, 300, 200));
                    var settings = new GlassSettings { Frost = 0, Depth = 14, Refraction = 1, Shine = 0.7f, Dispersion = 0, Light = 270,
                        Tint = System.Drawing.Color.FromArgb(0, 0, 0, 0) };
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    using (System.Drawing.Bitmap pane = LiquidGlass.Render(backdrop, new System.Drawing.Rectangle(0, 0, sw, sh),
                        new System.Drawing.Rectangle(50, 50, 300, 200), shape, settings))
                    {
                        double ms = watch.Elapsed.TotalMilliseconds;
                        System.Drawing.Color top = pane.GetPixel(150, 1), bottom = pane.GetPixel(150, 198),
                            middle = pane.GetPixel(150, 100), plain = pane.GetPixel(0, 100), mid = pane.GetPixel(150, 100);
                        Check("liquid glass lights the rim facing the light more than the far one",
                            top.GetBrightness() > bottom.GetBrightness() && bottom.GetBrightness() > middle.GetBrightness() - 0.02,
                            top + " " + bottom + " " + middle);
                        // In the middle the backdrop shows straight through; at the rim it is pulled in from further inside.
                        int stripesInside = 0, stripesRim = 0;
                        for (int px = 1; px < 299; px++)
                        {
                            if (Math.Abs(pane.GetPixel(px, 100).R - pane.GetPixel(px - 1, 100).R) > 40) stripesInside++;
                            if (Math.Abs(pane.GetPixel(px, 4).R - pane.GetPixel(px - 1, 4).R) > 40) stripesRim++;
                        }
                        Check("liquid glass shows the backdrop through its middle and bends it at the rim",
                            stripesInside >= 25 && plain.A == 255 && mid.A == 255,
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
                    using (var renderer = new LayoutRenderer(854, 480,
                        new DashboardSettings { Layout = Layouts.Custom, Design = layout, AccentColor = "#22D3EE" }, _units))
                    using (System.Drawing.Bitmap frame = renderer.Render(IRacingTelemetrySource.CreatePreviewSnapshot(), false, 4, 1))
                        return frame.GetPixel(180, 180);
                }
                System.Drawing.Color plainGlass = Through(false), overShape = Through(true);
                Check("a fixed widget beneath liquid glass shows through it",
                    overShape.R > plainGlass.R + 40 && overShape.B > plainGlass.B + 40 && overShape.G < overShape.R - 30,
                    plainGlass + " / " + overShape);
            }
        }

        /// <summary>Checks: the instrument finish.</summary>
        private void CheckInstrumentFinish()
        {
            {
                // A scale that lights up as it's passed: a tick reached shows bright, one still ahead stays dim.
                DashLayout scale = DesignTemplates.Blank();
                scale.Theme.BackgroundStyle = "Solid";
                scale.Theme.Background = "#000000";
                DashWidget dial = WidgetCatalog.Create("arc", 200, 40);
                dial.W = 400; dial.H = 400;
                string[] look = { "value", "0.5", "style", "Track only", "ticks", "10", "minorTicks", "0", "tickLength", "20",
                    "tickColor", "#FFFFFF",
                    "colorMode", "Solid", "color", "#FFFFFF", "litScale", "true", "glow", "0", "rim", "#FF0000", "rimWidth", "2",
                    "start", "180", "sweep", "180" };
                for (int i = 0; i + 1 < look.Length; i += 2) dial.Set(look[i], look[i + 1]);
                scale.Screens[0].Widgets.Add(dial);
                using (var renderer = new LayoutRenderer(854, 480,
                    new DashboardSettings { Layout = Layouts.Custom, Design = scale, AccentColor = "#22D3EE" }, _units))
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
                        for (int px = 395; px < 460; px++) { var pixel = frame.GetPixel(px,
                            py); if (pixel.R > 120 && pixel.G < 60) { redRim = true; break; } }
                    Check("a lit scale shows marks already passed brighter than those ahead, inside a hairline bezel",
                        leftTick > rightTick + 60 && redRim,
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
                Check("a saved flagship takes the new finish and keeps its layout",
                    sweep.Get("rim") == "#2E2722" && sweep.Get("litScale") == "true" && sweep.Get("thickness") == "10" &&
                    sweep.X == 30 && race.Widgets.Count(w => w.Name == "Gear light") == 1 &&
                    race.Widgets.FindIndex(w => w.Name == "Gear light") == race.Widgets.IndexOf(sweep) - 1);

                // Halo and Oval round their corners; a first-generation Neon is rebuilt.
                var older = new AppSettings { SchemaVersion = 14 };
                DashboardSettings haloDash = DashboardSettings.CreateDesigned(DesignTemplates.Find("blank"));
                haloDash.Id = "dashboard-halo";
                DashScreen haloMain = haloDash.Design.Screens[0];
                DashWidget tile = new DashWidget { Type = "panel", Name = "Fuel tile", X = 600, Y = 100, W = 196, H = 60 },
                    chip = new DashWidget { Type = "panel", Name = "Caution chip", X = 360, Y = 80, W = 116, H = 24 };
                tile.Set("corners", "Cut diagonal");
                chip.Set("corners", "Cut diagonal");
                DashWidget revs = new DashWidget { Type = "arc", Name = "Rev lights left" },
                    pit = new DashWidget { Type = "arc", Name = "Pit speed" };
                DashWidget kink = new DashWidget { Type = "shape", Name = "Hairline left angle" },
                    baseLine = new DashWidget { Type = "shape", Name = "Hairline left base", X = 18, Y = 54, W = 158, H = 2 };
                haloMain.Widgets.AddRange(new[] { tile, chip, revs, pit, kink, baseLine });
                DashboardSettings oldNeon = DashboardSettings.CreateDesigned(DesignTemplates.Find("blank"));
                oldNeon.Id = "dashboard-oldneon";
                foreach (string name in new[] { "Rev ring", "Fuel ring", "Fuel pct",
                    "Divider" }) oldNeon.Design.Screens[0].Widgets.Add(new DashWidget { Type = "text", Name = name });
                older.Dashboards.Add(haloDash);
                older.Dashboards.Add(oldNeon);
                older.EnsureBuiltInDashboards();
                Check("Halo and Oval take rounded corners, pills and straight hairlines; a first Neon is rebuilt",
                    tile.Get("corners") == "Round" && tile.Get("radius") == "16" && chip.Get("radius") == "12" &&
                    revs.Get("rim") != null && pit.Get("needle") == "Marker" &&
                    !haloMain.Widgets.Contains(kink) && baseLine.W == 252 &&
                    oldNeon.Design.AllWidgets().Any(w => w.Name == "Shift horizon") && !oldNeon.Design.AllWidgets()
                    .Any(w => w.Name == "Fuel ring"),
                    tile.Get("corners", "") + " " + tile.Get("radius", "") + " / " + chip.Get("radius", ""));

                // Schema 16: Halo and Oval are reworked; their names, colors, track types and flag choices stay, and nothing else changes.
                var reworked = new AppSettings { SchemaVersion = 15 };
                DashboardSettings oldHalo = DashboardSettings.CreateDesigned(DesignTemplates.Find("blank")),
                    oldOval = DashboardSettings.CreateDesigned(DesignTemplates.Find("blank")),
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
                    oldHalo.Design.AllWidgets().Any(w => w.Type == "traffic") && oldOval.Design.AllWidgets()
                    .Any(w => w.Type == "radar") && oldHalo.Name == "Halo" &&
                    oldHalo.AccentColor == "#30D158" && !oldHalo.Design.FlagFrame && oldOval.RaceTypes.Contains(Disciplines.Oval) &&
                    ownDesign.Design.AllWidgets()
                    .Count() == ownWidgets);

                // Schema 17: brighter labels where the color is still a template's; a color of the user's own stays.
                var dimmer = new AppSettings { SchemaVersion = 16 };
                DashboardSettings dimOval = DashboardSettings.CreateDesigned(DesignTemplates.Find("oval")),
                    ownColors = DashboardSettings.CreateDesigned(DesignTemplates.Find("blank"));
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
                    dimOval.Design.Theme.Muted == "#B6C5DD" && caption.Get("color") == "muted" && caption.Get("size") == "11" &&
                    ownColors.Design.Theme.Muted == "#123456",
                    dimOval.Design.Theme.Muted + " " + caption.Get("color") + " " + caption.Get("size") + " " +
                    ownColors.Design.Theme.Muted);
            }
        }

        /// <summary>Checks: masks: clipping, fades and blending.</summary>
        private void CheckMasks()
        {
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
                    using (var renderer = new LayoutRenderer(layout.Width, layout.Height,
                        new DashboardSettings { Layout = Layouts.Custom, Design = layout }, _units) { RedrawChangesOnly = changesOnly })
                    using (System.Drawing.Bitmap frame = renderer.Render(snapshot ?? IRacingTelemetrySource.CreatePreviewSnapshot(), false,
                        4, 1, layout.Screens[0]))
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
                TelemetrySnapshot racing = IRacingTelemetrySource.CreatePreviewSnapshot(),
                    pitting = IRacingTelemetrySource.CreatePitLanePreviewSnapshot();
                int mismatched = 0;
                using (var incremental = new LayoutRenderer(100, 100, new DashboardSettings { Layout = Layouts.Custom, Design = clipping },
                    _units) { FixedNow = TestNow, FlagClock = 0 })
                using (var whole = new LayoutRenderer(100, 100, new DashboardSettings { Layout = Layouts.Custom, Design = clipping },
                    _units) { FixedNow = TestNow, FlagClock = 0, RedrawChangesOnly = false })
                    foreach (TelemetrySnapshot snapshot in new[] { racing, pitting, racing, pitting })
                        using (System.Drawing.Bitmap a = incremental.Render(snapshot, false, snapshot.Gear, 1, clipping.Screens[0]))
                        using (System.Drawing.Bitmap b = whole.Render(snapshot, false, snapshot.Gear, 1, clipping.Screens[0]))
                            if (Pixels(a) != Pixels(b)) mismatched++;
                Check("masks: a clipped layer follows its base appearing and going, redrawing only what changed", mismatched == 0,
                    mismatched + " of 4 frames differ");
                clipBase.Set("visible", "");

                // A fade to the left: clear at the left edge, solid past the fade.
                var fading = new DashLayout { Width = 100, Height = 40 };
                fading.Theme.BackgroundStyle = "None";
                fading.Screens.Add(new DashScreen { Name = "Main" });
                Box(fading.Screens[0], "Faded", 0, 0, 100, 40, "fill", "#FFFFFF", "fxFade", "Left", "fxFadeLength", "40");
                int edge, middle, solid;
                using (System.Drawing.Bitmap frame = Draw(fading)) { edge = frame.GetPixel(1, 20).A; middle = frame.GetPixel(20,
                    20).A; solid = frame.GetPixel(80, 20).A; }
                Check("masks: a fade runs from clear at its edge to solid", edge < 20 && middle > 60 && middle < 200 && solid == 255,
                    edge + " " + middle + " " + solid);

                // Blend modes on black and white beneath.
                uint white = 0xFFFFFFFF, black = 0xFF000000, red = 0xFFFF0000;
                Check("masks: Multiply keeps a color on white and black on black; Screen keeps a color on black; Add brightens",
                    LayoutRenderer.BlendPixel(white, red, "Multiply") == red &&
                    LayoutRenderer.BlendPixel(black, red, "Multiply") == black &&
                    LayoutRenderer.BlendPixel(black, red, "Screen") == red &&
                    LayoutRenderer.BlendPixel(0xFF404040, 0xFF404040, "Add") == 0xFF808080,
                    LayoutRenderer.BlendPixel(white, red, "Multiply")
                    .ToString("X8") + " " + LayoutRenderer.BlendPixel(0xFF404040, 0xFF404040, "Add").ToString("X8"));
                var blending = new DashLayout { Width = 100, Height = 50 };
                blending.Theme.BackgroundStyle = "Solid";
                blending.Theme.Background = "#000000";
                blending.Screens.Add(new DashScreen { Name = "Main" });
                Box(blending.Screens[0], "White", 0, 0, 50, 50, "fill", "#FFFFFF");
                Box(blending.Screens[0], "Shade", 0, 0, 100, 50, "fill", "#4080FF", "fxBlend", "Multiply");
                Box(blending.Screens[0], "Light", 0, 40, 100, 10, "fill", "#404040", "fxBlend", "Screen");
                System.Drawing.Color onWhite, onBlack, screened;
                using (System.Drawing.Bitmap frame = Draw(blending)) { onWhite = frame.GetPixel(25, 20); onBlack = frame.GetPixel(75,
                    20); screened = frame.GetPixel(75, 45); }
                Check("masks: blended layers mix with what's beneath them as drawn",
                    Math.Abs(onWhite.R - 0x40) < 3 && Math.Abs(onWhite.B - 0xFF) < 3 && onBlack.R < 3 && onBlack.B < 3 &&
                    Math.Abs(screened.R - 0x40) < 3,
                    onWhite + " " + onBlack + " " + screened);
            }
        }
    }
}
