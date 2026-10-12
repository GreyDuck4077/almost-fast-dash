using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace AlmostFastOverlays
{
    /// <summary>
    /// Replays an iRacing telemetry recording (.ibt) through the same telemetry and light
    /// code the dashboard runs, and writes what lit each LED group and for how long.
    /// Answers "what was that light?" after a race. Run with --replay-ibt file.ibt report.txt.
    /// </summary>
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
                    renderer = new LightRenderer(dashboard.Lights ?? LightPresets.FromClassic(dashboard), dashboard.AccentColor, new UnitSystem(settings.General), devices);
                    flags.Add(Clock(sessionTime) + "  dashboard: " + dashboard.Name);
                }

                uint raw = (uint)telemetry.SessionFlags;
                if (raw != lastRawFlags || snapshot.Flag != lastFlag)
                {
                    flags.Add(Clock(sessionTime) + "  iRacing flags 0x" + raw.ToString("X8") + " (" + FlagNames(raw) + ")  ->  dashboard flag " + snapshot.Flag);
                    lastRawFlags = raw;
                    lastFlag = snapshot.Flag;
                }
                if (snapshot.OutOfCar != lastOut)
                {
                    flags.Add(Clock(sessionTime) + (snapshot.OutOfCar ? "  out of the car or watching a replay: lights off" : "  back in the car"));
                    lastOut = snapshot.OutOfCar;
                }

                LightFrame frame = renderer.Compose(snapshot.ForLights(), (int)(sessionTime * 1000));
                spans.AddRange(activity.Observe(frame, devices, sessionTime, null, renderer.Profile.Gamma));
            }
            spans.AddRange(activity.Flush());

            var revs = new HashSet<string>(settings.Dashboards.Where(d => d.Lights != null).SelectMany(d => d.Lights.Layers).Where(l => l.Effect == "revs").Select(l => l.Name));
            var report = new StringBuilder();
            report.AppendLine("Replay of " + Path.GetFileName(recording));
            report.AppendLine("Session time " + Clock(first) + " to " + Clock(last) + "; times below are session time (m:ss).");
            report.AppendLine();
            report.AppendLine("FLAGS AND STATE");
            foreach (string line in flags) report.AppendLine("  " + line);
            report.AppendLine();
            report.AppendLine("LIGHTS LONGER THAN 1 SECOND (rev lights left out)");
            foreach (LightSpan span in spans.Where(s => s.Seconds >= 1 && !revs.Contains(s.Layer)).OrderBy(s => s.From))
                report.AppendLine(string.Format(CultureInfo.InvariantCulture, "  {0} to {1}  {2,5:0.0}s  {3} - {4}: {5} ({6})",
                    Clock(span.From), Clock(span.To), span.Seconds, span.Device, span.Group, span.Layer, span.Colour));
            report.AppendLine();
            report.AppendLine("HOW OFTEN EACH LAYER LIT");
            foreach (var layer in spans.GroupBy(s => s.Layer).OrderBy(g => g.Key))
                report.AppendLine(string.Format(CultureInfo.InvariantCulture, "  {0}: {1} times, {2:0.0}s in all, longest {3:0.0}s",
                    layer.Key, layer.Count(), layer.Sum(s => s.Seconds), layer.Max(s => s.Seconds)));
            File.WriteAllText(reportPath, report.ToString());
            Console.WriteLine(report.ToString());
            return 0;
        }

        private static string Clock(double seconds) =>
            ((int)(seconds / 60)).ToString(CultureInfo.InvariantCulture) + ":" + (seconds % 60).ToString("00.0", CultureInfo.InvariantCulture);

        internal static string FlagNames(uint raw)
        {
            string[] names = { "checkered", "white", "green", "yellow", "red", "blue", "debris", "crossed", "yellowWaving", "oneLapToGreen",
                "greenHeld", "tenToGo", "fiveToGo", "randomWaving", "caution", "cautionWaving", "black", "disqualify", "servicible", "furled", "repair" };
            var on = new List<string>();
            for (int i = 0; i < names.Length; i++) if ((raw & (1u << i)) != 0) on.Add(names[i]);
            if ((raw & 0xF0000000) != 0) on.Add("start lights");
            return on.Count == 0 ? "none" : string.Join(", ", on);
        }
    }
}
