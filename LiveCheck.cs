using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace AlmostFastOverlays
{
    /// <summary>
    /// Watches a live iRacing session for a while and writes what it sends for every value the
    /// dashboards use, and what the app makes of it: proof that leaderboard, sector, tyre and engine
    /// data work on real sessions. Run with --live-check report.txt while in the car.
    /// </summary>
    internal static class LiveCheck
    {
        private static readonly string[] Variables =
        {
            "OilTemp", "OilPress", "WaterTemp", "FuelPress", "Voltage", "DRS_Status", "LapDistPct", "SessionTime", "PlayerCarIdx",
            "CarIdxPosition", "CarIdxClassPosition", "CarIdxLastLapTime", "CarIdxBestLapTime", "CarIdxF2Time", "CarIdxLapCompleted", "CarIdxLapDistPct", "CarIdxOnPitRoad",
            "LFtempCL", "LFtempCM", "LFtempCR", "LFwearM", "LFcoldPressure", "RRtempCM", "RRwearM", "RRcoldPressure", "dcBrakeBias", "dcTractionControl", "dcABS", "CarLeftRight"
        };

        public static int Run(string reportPath, int seconds)
        {
            var report = new StringBuilder();
            var source = new IRacingTelemetrySource(null, null);
            Telemetry latest = null;
            int samples = 0;
            void OnData(DataSample sample)
            {
                if (sample?.IsConnected != true || sample.Telemetry == null) return;
                latest = sample.Telemetry;
                samples++;
                source.OnNewData(sample);
            }
            iRacing.NewData += OnData;
            iRacing.StartListening();
            try
            {
                // Up to a minute for iRacing, then the requested time of data.
                DateTime giveUp = DateTime.UtcNow.AddSeconds(60);
                while (latest == null && DateTime.UtcNow < giveUp) Thread.Sleep(250);
                if (latest == null)
                {
                    File.WriteAllText(reportPath, "iRacing was not running or not sending data. Start a session, get in the car and run this again.");
                    return 1;
                }
                Thread.Sleep(Math.Max(5, seconds) * 1000);
            }
            finally
            {
                iRacing.NewData -= OnData;
                iRacing.StopListening();
            }

            Telemetry t = latest;
            TelemetrySnapshot s = source.GetSnapshot();
            report.AppendLine("Live check, " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + ": " + samples + " samples over " + seconds + " s");
            report.AppendLine("Session: " + (t.Session?.SessionType ?? "?") + ", car " + s.CarName + ", " + (t.CarDetails?.Length ?? 0) + " drivers listed");
            report.AppendLine();
            report.AppendLine("WHAT iRACING SENDS");
            foreach (string name in Variables)
            {
                object value = t[name];
                string shown = value == null ? "MISSING" : value is Array array ? Summarise(array) : Convert.ToString(value, CultureInfo.InvariantCulture);
                report.AppendLine("  " + name.PadRight(22) + shown);
            }
            report.AppendLine();
            report.AppendLine("WHAT THE DASHBOARD SHOWS");
            report.AppendLine(string.Format(CultureInfo.InvariantCulture, "  engine: oil {0:0.0} C {1:0.00} bar, water {2:0.0} C, fuel {3:0.00} bar, {4:0.0} V, DRS {5}",
                s.OilTempC, s.OilPressureBar, s.WaterTempC, s.FuelPressureBar, s.Voltage, s.DrsState));
            report.AppendLine(string.Format(CultureInfo.InvariantCulture, "  fuel: {0:0.0} L of {1:0.0} L, sector {2} of {3}, session best {4}, class best {5}",
                s.FuelLiters, s.FuelCapacityLiters, s.Sector, s.SectorCount, Fmt.LapTime(s.SessionBestLapSeconds), Fmt.LapTime(s.ClassBestLapSeconds)));
            foreach (TyreCorner tyre in s.Tyres)
                report.AppendLine(string.Format(CultureInfo.InvariantCulture, "  tyre {0}: {1:0}/{2:0}/{3:0} C, wear {4:0.00}, {5:0} kPa", tyre.Name, tyre.TempOuter, tyre.TempMiddle, tyre.TempInner, tyre.Wear, tyre.PressureKpa));
            report.AppendLine("  leaderboard: " + s.Standings.Length + " cars");
            foreach (StandingsEntry r in s.Standings.Take(12))
                report.AppendLine(string.Format(CultureInfo.InvariantCulture, "    P{0} (class P{1}) #{2} {3} [{4}] best {5} last {6} gap {7} interval {8:0.00}{9}{10}",
                    r.Position, r.ClassPosition, r.CarNumber, r.Name, r.ClassName, Fmt.LapTime(r.BestLap), Fmt.LapTime(r.LastLap), RaceFields.GapText(r), r.Interval,
                    r.IsPlayer ? "  <- you" : "", r.InPits ? "  (pits)" : ""));
            File.WriteAllText(reportPath, report.ToString(), Encoding.UTF8);
            return 0;
        }

        private static string Summarise(Array array)
        {
            var values = new List<string>();
            int shown = 0, set = 0;
            foreach (object v in array)
            {
                double d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
                if (d != 0 && d != -1) set++;
                if (shown < 8) { values.Add(Convert.ToString(v, CultureInfo.InvariantCulture)); shown++; }
            }
            return "[" + array.Length + " cars, " + set + " with a value] " + string.Join(" ", values) + (array.Length > 8 ? " ..." : "");
        }
    }
}
