using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace AlmostFastDash.CommandLine
{
    /// <summary>
    /// Replays an iRacing telemetry recording (.ibt) through the same telemetry and light
    /// code the dashboard runs, and writes what lit each LED group and for how long.
    /// Answers "what was that light?" after a race. Run with --replay-ibt file.ibt report.txt.
    /// </summary>
    /// <summary>
    /// Checks the gap to the car ahead against what really happened. Every couple of seconds it notes the
    /// car just ahead on track, where it is, and the two estimates of the gap: from iRacing's per-car time
    /// estimates, and from distance times a lap time. Afterwards the true gap is how long you took to reach
    /// that spot, read from your own path through the recording.
    /// </summary>
    internal sealed class GapCheck
    {
        private readonly List<double> _time = new List<double>(), _distance = new List<double>();
        private readonly List<double[]> _samples = new List<double[]>();
        private double _nextSample;

        public void Observe(Telemetry telemetry, double now, TelemetrySnapshot snapshot)
        {
            float[] pct = telemetry.CarIdxLapDistPct, estimates = telemetry["CarIdxEstTime"] as float[];
            int[] laps = telemetry["CarIdxLap"] as int[];
            int me = Convert.ToInt32(telemetry["PlayerCarIdx"] ?? -1);
            if (pct == null || laps == null || me < 0 || me >= pct.Length || pct[me] < 0) return;
            double mine = laps[me] + pct[me];
            if (_distance.Count == 0 || mine >= _distance[_distance.Count - 1]) { _time.Add(now); _distance.Add(mine); }
            bool green = !snapshot.CautionActive && !snapshot.OnPitRoad && snapshot.SpeedMph > 40;
            if (!green || now < _nextSample) return;
            _nextSample = now + 2;
            int ahead = -1;
            double lead = 1;
            for (int i = 0; i < pct.Length; i++)
            {
                if (i == me || pct[i] < 0) continue;
                double d = pct[i] - pct[me];
                if (d < 0) d += 1;
                if (d > 0.0005 && d < lead) { lead = d; ahead = i; }
            }
            if (ahead < 0 || lead > 0.5) return;
            double lap = 0;
            foreach (CarDetails c in telemetry.CarDetails ?? new CarDetails[0])
                if (c != null && c.CarIdx == me && c.Driver != null) lap = c.Driver.CarClassEstLapTime;
            if (lap <= 10) return;
            _samples.Add(new[] { now, mine + lead, IRacingTelemetrySource.TrackGap(estimates, me, ahead, lead, lap), lead * lap });
        }

        public IEnumerable<string> Report()
        {
            var estimateErrors = new List<double>();
            var distanceErrors = new List<double>();
            foreach (double[] s in _samples)
            {
                // When did you reach the spot the car ahead was at?
                int k = _distance.BinarySearch(s[1]);
                if (k < 0) k = ~k;
                if (k <= 0 || k >= _distance.Count) continue;
                double f = (s[1] - _distance[k - 1]) / Math.Max(1e-9, _distance[k] - _distance[k - 1]);
                double truth = _time[k - 1] + f * (_time[k] - _time[k - 1]) - s[0];
                if (truth <= 0 || truth > 10) continue;
                estimateErrors.Add(Math.Abs(s[2] - truth));
                distanceErrors.Add(Math.Abs(s[3] - truth));
            }
            if (estimateErrors.Count == 0) { yield return "no green-flag running behind another car to check"; yield break; }
            string Line(string name, List<double> errors)
            {
                var sorted = errors.OrderBy(e => e).ToList();
                return string.Format(CultureInfo.InvariantCulture,
                    "{0}: off by {1:0.000} s on average, {2:0.000} s typically (median), {3:0.000} s at worst",
                    name, errors.Average(), sorted[sorted.Count / 2], sorted[sorted.Count - 1]);
            }
            yield return estimateErrors.Count + " moments checked";
            yield return Line("iRacing's time estimates (what the dashboard now shows)", estimateErrors);
            yield return Line("distance times a lap time (how it was worked out before)", distanceErrors);
        }
    }

    internal static class RecordingReplay
    {
        public static int Run(string recording, string reportPath)
        {
            AppSettings settings = SettingsStore.Load();
            List<DeviceSettings> devices = settings.General.Devices;
            var source = new IRacingTelemetrySource(null, null);
            DateTime origin = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            double sessionTime = 0;
            source.Clock = () => origin.AddSeconds(sessionTime);

            LightRenderer renderer = null;
            DashboardSettings dashboard = null;
            var activity = new LightActivity();
            var spans = new List<LightSpan>();
            var flags = new List<string>();
            RacingFlag lastFlag = RacingFlag.None;
            uint lastRawFlags = 0;
            bool lastOut = false;
            double first = double.NaN, last = 0;
            var race = new List<string>();
            string lastNotice = "";
            int lastLaps = -1;
            TelemetrySnapshot final = null;
            var gps = new List<System.Drawing.PointF>();
            var gaps = new GapCheck();

            foreach (Telemetry telemetry in Telemetry.ReadRecording(recording))
            {
                sessionTime = Convert.ToDouble(telemetry["SessionTime"] ?? 0d);
                if (double.IsNaN(first)) first = sessionTime;
                last = sessionTime;
                source.OnNewData(new DataSample { IsConnected = true, Telemetry = telemetry });
                TelemetrySnapshot snapshot = source.GetSnapshot();

                DashboardSettings now = settings.ResolveDashboard(snapshot.Discipline);
                if (now != dashboard)
                {
                    dashboard = now;
                    source.ApplyAlertSettings(dashboard.Alerts);
                    renderer = new LightRenderer(dashboard.Lights, dashboard.AccentColor, new UnitSystem(settings.General), devices);
                    flags.Add(Clock(sessionTime) + "  dashboard: " + dashboard.Name);
                }

                uint raw = (uint)telemetry.SessionFlags;
                if (raw != lastRawFlags || snapshot.Flag != lastFlag)
                {
                    flags.Add(Clock(sessionTime) + "  iRacing flags 0x" + raw.ToString("X8") + " (" + FlagNames(raw) +
                        ")  ->  dashboard flag " + snapshot.Flag);
                    lastRawFlags = raw;
                    lastFlag = snapshot.Flag;
                }
                if (snapshot.OutOfCar != lastOut)
                {
                    flags.Add(Clock(sessionTime) +
                        (snapshot.OutOfCar ? "  out of the car or watching a replay: lights off" : "  back in the car"));
                    lastOut = snapshot.OutOfCar;
                }

                if (snapshot.Notice != lastNotice)
                {
                    if (snapshot.Notice.Length > 0) race.Add(Clock(sessionTime) + "  notice: " + snapshot.Notice + " (" +
                        snapshot.NoticeKind + ")");
                    lastNotice = snapshot.Notice;
                }
                if (snapshot.LapsCompleted != lastLaps)
                {
                    lastLaps = snapshot.LapsCompleted;
                    race.Add(Clock(sessionTime) +
                        string.Format(CultureInfo.InvariantCulture,
                        "  lap {0} done: last {1}, sectors {2} | best sectors {3} | session best {4}, P{5} of {6} on the leaderboard",
                        snapshot.LapsCompleted, Fmt.LapTime(snapshot.LastLapSeconds), Times(snapshot.LastLapSectorTimes),
                        Times(snapshot.BestSectorTimes),
                        Fmt.LapTime(snapshot.SessionBestLapSeconds), snapshot.Standings.FirstOrDefault(r => r.IsPlayer)?.Position ?? 0,
                        snapshot.Standings.Length) +
                        string.Format(CultureInfo.InvariantCulture,
                        "; stint {0} green laps, falloff {1}; fuel {2} L a lap over {3}, {4:0.0} laps to go, add {5} L",
                            snapshot.StintLapSeconds.Length,
                            snapshot.StintLapSeconds.Length >= 2 ? "+" + Falloff.Total(snapshot.StintLapSeconds)
                            .ToString("0.000", CultureInfo.InvariantCulture) : "--",
                            snapshot.FuelPerLapAverage > 0 ? snapshot.FuelPerLapAverage.ToString("0.000", CultureInfo.InvariantCulture)
                            : "--",
                            snapshot.FuelLapsMeasured, snapshot.LapsToGo,
                            snapshot.FuelToFinish >= 0 ? snapshot.FuelToFinish.ToString("0.00", CultureInfo.InvariantCulture) : "--"));
                }
                final = snapshot;
                if (telemetry["Lat"] is double lat && telemetry["Lon"] is double lon &&
                    lat != 0) gps.Add(new System.Drawing.PointF((float)lon, (float)lat));
                gaps.Observe(telemetry, sessionTime, snapshot);

                LightFrame frame = renderer.Compose(snapshot.ForLights(), (int)(sessionTime * 1000));
                spans.AddRange(activity.Observe(frame, devices, sessionTime, null, renderer.Profile.Gamma));
            }
            spans.AddRange(activity.Flush());

            var revs = new HashSet<string>(settings.Dashboards.Where(d => d.Lights != null).SelectMany(d => d.Lights.Layers)
                .Where(l => l.Effect == "revs").Select(l => l.Name));
            var report = new StringBuilder();
            report.AppendLine("Replay of " + Path.GetFileName(recording));
            report.AppendLine("Session time " + Clock(first) + " to " + Clock(last) + "; times below are session time (m:ss).");
            report.AppendLine();
            report.AppendLine("FLAGS AND STATE");
            foreach (string line in flags) report.AppendLine("  " + line);
            report.AppendLine();
            report.AppendLine("RACE DATA");
            foreach (string line in race) report.AppendLine("  " + line);
            if (final != null)
            {
                report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  at the end: oil {0:0.0} C {1:0.00} bar, water {2:0.0} C, fuel pressure {3:0.00} bar, {4:0.0} V, DRS {5}, tank " +
                        "{6:0.0} L, {7} sectors",
                    final.OilTempC, final.OilPressureBar, final.WaterTempC, final.FuelPressureBar, final.Voltage, final.DrsState,
                    final.FuelCapacityLiters, final.SectorCount));
                foreach (TyreCorner t in final.Tyres)
                    report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "  tire {0}: {1:0}/{2:0}/{3:0} C (outer/middle/inner), wear {4:0.00}, {5:0} kPa", t.Name, t.TempOuter,
                        t.TempMiddle, t.TempInner, t.Wear, t.PressureKpa));
                foreach (StandingsEntry r in final.Standings.Take(6))
                    report.AppendLine(string.Format(CultureInfo.InvariantCulture, "  P{0} #{1} {2} ({3}) best {4} last {5} gap {6}",
                        r.Position, r.CarNumber, r.Name, r.ClassName, Fmt.LapTime(r.BestLap), Fmt.LapTime(r.LastLap),
                        RaceFields.GapText(r)));
            }
            report.AppendLine("  track map: " + (final?.TrackMap != null ? "traced (" + final.TrackMap.Name + ")" : "not traced"));
            if (final != null)
                report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  at the end: {0} green laps this stint, falloff {1}, {2:+0.000;-0.000} s a lap; fuel {3:0.000} L a lap over {4} " +
                        "laps, {5:0.00} laps to go, fuel to add {6:0.00} L",
                    final.StintLapSeconds.Length, final.StintLapSeconds.Length >= 2 ? Falloff.Total(final.StintLapSeconds)
                    .ToString("0.000", CultureInfo.InvariantCulture) + " s" : "--",
                    Falloff.PerLap(final.StintLapSeconds), final.FuelPerLapAverage, final.FuelLapsMeasured, final.LapsToGo,
                    final.FuelToFinish));
            report.AppendLine();
            report.AppendLine("GAP ACCURACY (the car just ahead on track, against when you actually reached its spot; green-flag " +
                "running, gaps under 10 s)");
            foreach (string line in gaps.Report()) report.AppendLine("  " + line);
            if (final?.TrackMap != null) SaveMapCheck(final.TrackMap, gps, Path.ChangeExtension(reportPath, ".map.png"));
            report.AppendLine();
            report.AppendLine("LIGHTS LONGER THAN 1 SECOND (rev lights left out)");
            foreach (LightSpan span in spans.Where(s => s.Seconds >= 1 && !revs.Contains(s.Layer)).OrderBy(s => s.From))
                report.AppendLine(string.Format(CultureInfo.InvariantCulture, "  {0} to {1}  {2,5:0.0}s  {3} - {4}: {5} ({6})",
                    Clock(span.From), Clock(span.To), span.Seconds, span.Device, span.Group, span.Layer, span.Color));
            report.AppendLine();
            report.AppendLine("HOW OFTEN EACH LAYER LIT");
            foreach (var layer in spans.GroupBy(s => s.Layer).OrderBy(g => g.Key))
                report.AppendLine(string.Format(CultureInfo.InvariantCulture, "  {0}: {1} times, {2:0.0}s in all, longest {3:0.0}s",
                    layer.Key, layer.Count(), layer.Sum(s => s.Seconds), layer.Max(s => s.Seconds)));
            File.WriteAllText(reportPath, report.ToString());
            Console.WriteLine(report.ToString());
            return 0;
        }

        /// <summary>The traced map (left) beside the recording's GPS track (right), north up, to check the trace's shape and
        /// handedness.</summary>
        private static void SaveMapCheck(TrackMap map, List<System.Drawing.PointF> gps, string path)
        {
            using (var image = new System.Drawing.Bitmap(800, 400))
            using (var g = System.Drawing.Graphics.FromImage(image))
            {
                g.Clear(System.Drawing.Color.Black);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                void Draw(IList<System.Drawing.PointF> points, float left, System.Drawing.Color color)
                {
                    if (points.Count < 2) return;
                    float minX = points.Min(p => p.X), maxX = points.Max(p => p.X), minY = points.Min(p => p.Y),
                        maxY = points.Max(p => p.Y);
                    float scale = Math.Min(360 / Math.Max(1e-6f, maxX - minX), 360 / Math.Max(1e-6f, maxY - minY));
                    var screen = points.Select(p => new System.Drawing.PointF(left + 20 + (p.X - minX) * scale, 20 + (maxY - p.Y) * scale))
                        .ToArray();
                    using (var pen = new System.Drawing.Pen(color, 2)) g.DrawLines(pen, screen);
                    using (var start = new System.Drawing.SolidBrush(System.Drawing.Color.Yellow)) g.FillEllipse(start, screen[0].X - 5,
                        screen[0].Y - 5, 10, 10);
                }
                Draw(Enumerable.Range(0, TrackMap.Points + 1)
                    .Select(i => new System.Drawing.PointF(map.X[i % TrackMap.Points], map.Y[i % TrackMap.Points])).ToList(), 0,
                    System.Drawing.Color.White);
                // Longitude shrinks with latitude; scale it so the GPS shape isn't stretched.
                if (gps.Count > 1)
                {
                    double cos = Math.Cos(gps.Average(p => p.Y) * Math.PI / 180);
                    Draw(gps.Select(p => new System.Drawing.PointF((float)(p.X * cos), p.Y)).ToList(), 400, System.Drawing.Color.OrangeRed);
                }
                image.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        private static string Times(double[] times) => times == null || times.Length == 0 ? "none"
            : string.Join(" ", times.Select(t => t > 0 ? t.ToString("0.000", CultureInfo.InvariantCulture) : "-"));

        private static string Clock(double seconds) =>
            ((int)(seconds / 60)).ToString(CultureInfo.InvariantCulture) + ":" + (seconds % 60)
            .ToString("00.0", CultureInfo.InvariantCulture);

        internal static string FlagNames(uint raw)
        {
            string[] names = { "checkered", "white", "green", "yellow", "red", "blue", "debris", "crossed", "yellowWaving", "oneLapToGreen",
                "greenHeld", "tenToGo", "fiveToGo", "randomWaving", "caution", "cautionWaving", "black", "disqualify", "servicible",
                "furled", "repair" };
            var on = new List<string>();
            for (int i = 0; i < names.Length; i++) if ((raw & (1u << i)) != 0) on.Add(names[i]);
            if ((raw & 0xF0000000) != 0) on.Add("start lights");
            return on.Count == 0 ? "none" : string.Join(", ", on);
        }
    }
}
