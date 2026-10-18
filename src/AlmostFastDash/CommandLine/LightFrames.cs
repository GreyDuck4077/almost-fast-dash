using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;

namespace AlmostFastDash.CommandLine
{
    /// <summary>
    /// Writes every dashboard's composed LED colors through a rev sweep and the preview states,
    /// LED by LED, for checking lights without hardware. Run with --light-frames report.txt.
    /// </summary>
    internal static class LightFrames
    {
        public static int Run(string reportPath)
        {
            AppSettings settings = SettingsStore.Load();
            List<DeviceSettings> devices = settings.General.Devices;
            var report = new StringBuilder();
            foreach (DashboardSettings dashboard in settings.Dashboards)
            {
                report.AppendLine("== " + dashboard.Name);
                LightProfile profile = dashboard.Lights;
                var states = new List<Tuple<string, TelemetrySnapshot>>();
                foreach (double fraction in new[] { 0.0, 0.2, 0.4, 0.6, 0.8, 0.95 })
                    states.Add(Tuple.Create("revs " + (int)(fraction * 100) + "%", Revs(fraction)));
                foreach (string state in new[] { "Shift point", "Car left", "Car right", "Yellow flag" })
                    states.Add(Tuple.Create(state, LightsWindow.Scenario(state)));
                foreach (var state in states)
                {
                    // A fresh renderer per state, so fades from the previous state don't show.
                    LightFrame frame = new LightRenderer(profile, dashboard.AccentColor, new UnitSystem(settings.General), devices).Compose(state.Item2, 0);
                    report.AppendLine("  " + state.Item1);
                    for (int d = 0; d < devices.Count && d < frame.Leds.Length; d++)
                        report.AppendLine("    " + devices[d].Name.PadRight(8) + string.Join(" ", frame.Leds[d].Select((c, i) => (i + 1) + ":" + Short(c, profile.Gamma))));
                }
            }
            File.WriteAllText(reportPath, report.ToString());
            return 0;
        }

        private static TelemetrySnapshot Revs(double fraction)
        {
            // The preview car's shift lights run from 3800 to 7700 RPM.
            double rpm = 3800 + (7700 - 3800) * fraction;
            return IRacingTelemetrySource.WithPreviewExtras(new TelemetrySnapshot(
                true, 4, 136, RacingFlag.None, BlindSpotZone.None, BlindSpotZone.None, fraction, SessionProgressMode.LapRace, 0, 0, 8, 18, 4, 2, 24.3, 58.104, 0.46,
                "J. MILLER", -0.46, "M. CARTER", 0.82, 22, false, 87.932, 87.521, 88.204, new bool[18], 0, 6, rpm));
        }

        private static string Short(Color c, bool gamma)
        {
            if ((c.R | c.G | c.B) == 0) return "--";
            string name = LightActivity.ColorName(gamma ? Color.FromArgb(Ungamma(c.R), Ungamma(c.G), Ungamma(c.B)) : c);
            return name.StartsWith("#") ? name : name.Substring(0, Math.Min(3, name.Length));
        }

        private static int Ungamma(int v) => (int)Math.Round(255 * Math.Pow(v / 255.0, 1 / 2.2));
    }
}
