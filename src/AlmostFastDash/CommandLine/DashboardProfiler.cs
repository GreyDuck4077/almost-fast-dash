using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace AlmostFastDash.CommandLine
{
    /// <summary>
    /// Where a dashboard's drawing time goes (--profile-dashboards): plays the animated lap through every template
    /// and saved dashboard at 30 frames a second, exactly as the wheel loop does (a fingerprint every tick, a
    /// redraw of what changed when it did), and reports the time per frame and its costliest parts.
    /// </summary>
    internal static class DashboardProfiler
    {
        private const int Frames = 480, Warmup = 30;

        public static int Run(string reportPath)
        {
            // Other programs on the PC shouldn't move the figures: run ahead of them, and judge by the median.
            try
            {
                Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
                System.Threading.Thread.CurrentThread.Priority = System.Threading.ThreadPriority.Highest;
            }
            catch { }
            var report = new StringBuilder();
            report.AppendLine("Dashboard profile: the animated lap, " + Frames + " ticks at 30 a second (16 s), as the wheel draws it");
            report.AppendLine();
            AppSettings settings = SettingsStore.Load();
            var units = new UnitSystem(settings.General);
            var dashboards = DesignTemplates.All.Select(t => Tuple.Create("Template " + t.Name, DashboardSettings.CreateDesigned(t)))
                .Concat(settings.Dashboards.Where(d => d.Design != null).Select(d => Tuple.Create("Saved " + d.Name, d)));
            var totals = new List<double>();
            foreach (Tuple<string, DashboardSettings> item in dashboards)
            {
                string line = Profile(item.Item1, item.Item2, units, out double average);
                report.Append(line);
                totals.Add(average);
            }
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "Over all: {0:0.00} ms a drawn frame (the average of each dashboard's median)", totals.Average()));
            File.WriteAllText(reportPath, report.ToString());
            return 0;
        }

        private static string Profile(string name, DashboardSettings dashboard, UnitSystem units, out double average)
        {
            var text = new StringBuilder();
            System.Drawing.Size size = dashboard.ScreenSize();
            double clock = 0;
            var synthetic = new SyntheticTelemetry { Clock = () => clock };
            var timings = new Dictionary<string, double>();
            var drawn = new List<double>();
            double hashing = 0, share = 0;
            int? last = null;
            double toMs = 1000.0 / Stopwatch.Frequency;
            using (var renderer = new LayoutRenderer(size.Width, size.Height, dashboard, units) { SharedOutput = true,
                ReuseFingerprints = true })
            {
                for (int frame = -Warmup; frame < Frames; frame++)
                {
                    clock = (frame + Warmup) / 30.0;
                    TelemetrySnapshot snapshot = synthetic.Next(out bool flash, out int previousGear, out float progress);
                    if (frame == 0) renderer.Timings = timings;
                    int gearFrame = progress < 1 ? (int)(progress * 14) : -1;
                    long started = Stopwatch.GetTimestamp();
                    int hash = renderer.StateHash(snapshot, flash, gearFrame);
                    long hashed = Stopwatch.GetTimestamp();
                    if (frame >= 0) hashing += (hashed - started) * toMs;
                    if (hash == last) continue;
                    renderer.Render(snapshot, flash, previousGear, progress);
                    last = hash;
                    if (frame < 0) continue;
                    drawn.Add((Stopwatch.GetTimestamp() - hashed) * toMs);
                    share += renderer.LastRedrawShare;
                }
            }
            drawn.Sort();
            int count = Math.Max(1, drawn.Count);
            average = drawn.Count == 0 ? 0 : drawn[drawn.Count / 2];
            text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0}: {1} of {2} ticks drawn; {3:0.00} ms a frame (median; mean {4:0.00}, 95% under {5:0.00}, most {6:0.00}); " +
                    "fingerprint {7:0.00} ms a tick; {8:0}% of the screen redrawn",
                name, drawn.Count, Frames, average, drawn.Count == 0 ? 0 : drawn.Average(),
                drawn.Count == 0 ? 0 : drawn[(int)(drawn.Count * 0.95)],
                drawn.Count == 0 ? 0 : drawn.Last(), hashing / Frames, 100 * share / count));
            foreach (KeyValuePair<string, double> part in timings.Where(p => !p.Key.StartsWith("changed: ")).OrderByDescending(p => p.Value)
                .Take(8))
                text.AppendLine(string.Format(CultureInfo.InvariantCulture, "    {0,-28} {1,6:0.00} ms a frame", part.Key,
                    part.Value / count));
            text.AppendLine();
            return text.ToString();
        }
    }
}
