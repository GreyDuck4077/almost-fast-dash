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
            "CarIdxPosition", "CarIdxClassPosition", "CarIdxLastLapTime", "CarIdxBestLapTime", "CarIdxF2Time", "CarIdxEstTime", "CarIdxLapCompleted", "CarIdxLapDistPct", "CarIdxOnPitRoad",
            "LFtempCL", "LFtempCM", "LFtempCR", "LFwearM", "LFcoldPressure", "RRtempCM", "RRwearM", "RRcoldPressure", "dcBrakeBias", "dcTractionControl", "dcABS", "CarLeftRight"
        };

        public static int Run(string reportPath, int seconds)
        {
            var report = new StringBuilder();
            var source = new IRacingTelemetrySource(null, null);
            Telemetry latest = null;
            int samples = 0;
            // The radar against iRacing's spotter: every change of what the spotter reports, and what the radar drew.
            var radar = new List<string>();
            int lastSpotter = -1, alongsideSamples = 0, unplaced = 0;
            void OnData(DataSample sample)
            {
                if (sample?.IsConnected != true || sample.Telemetry == null) return;
                latest = sample.Telemetry;
                samples++;
                source.OnNewData(sample);
                int spotter = sample.Telemetry["CarLeftRight"] is int value ? value : 0;
                RadarCar[] cars = source.GetSnapshot().Radar ?? new RadarCar[0];
                RadarCar[] beside = cars.Where(c => c.Alongside).ToArray();
                if (beside.Length > 0) alongsideSamples++;
                if (beside.Any(c => c.CarIdx < 0)) unplaced++;
                if (spotter == lastSpotter || radar.Count >= 60) return;
                lastSpotter = spotter;
                double time = sample.Telemetry["SessionTime"] is double clock ? clock : 0;
                radar.Add(string.Format(CultureInfo.InvariantCulture, "    {0,7:0.0} s  spotter {1}  ->  {2}", time, spotter,
                    beside.Length == 0 ? "nothing alongside" : string.Join(", ", beside.Select(c => (c.CarIdx < 0 ? "unplaced car" : "#" + c.Number) +
                        (c.Lane < 0 ? " left " : " right ") + c.Meters.ToString("+0.0;-0.0", CultureInfo.InvariantCulture) + " m"))));
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
                report.AppendLine(string.Format(CultureInfo.InvariantCulture, "  tire {0}: {1:0}/{2:0}/{3:0} C, wear {4:0.00}, {5:0} kPa", tyre.Name, tyre.TempOuter, tyre.TempMiddle, tyre.TempInner, tyre.Wear, tyre.PressureKpa));
            report.AppendLine(string.Format(CultureInfo.InvariantCulture, "  race: {0:0.00} laps to go, fuel per lap {1:0.000} L over {2} clean laps{3}, fuel to add {4:0.00} L",
                s.LapsToGo, s.FuelPerLapAverage, s.FuelLapsMeasured, s.FuelLapsMeasured < 3 ? " (early estimate)" : "", s.FuelToFinish));
            report.AppendLine("  around you (gap from iRacing's per-car time estimates; by distance in brackets, as it was worked out before):");
            float[] estimates = t["CarIdxEstTime"] as float[], distances = t.CarIdxLapDistPct;
            int me = s.PlayerCarIdx;
            double lap = s.ReferenceLapSeconds > 0 ? s.ReferenceLapSeconds : 90;
            foreach (RelativeEntry row in s.Relatives ?? new RelativeEntry[0])
            {
                string plain = "";
                int index = Array.FindIndex(s.Standings, r => r.CarNumber == row.CarNumber);
                int car = index >= 0 ? s.Standings[index].CarIdx : -1;
                if (!row.IsPlayer && distances != null && car >= 0 && me >= 0 && car < distances.Length && me < distances.Length)
                {
                    double delta = distances[car] - distances[me];
                    if (delta > 0.5) delta -= 1;
                    if (delta < -0.5) delta += 1;
                    plain = string.Format(CultureInfo.InvariantCulture, " ({0:+0.00;-0.00} s)", -delta * lap);
                }
                report.AppendLine(string.Format(CultureInfo.InvariantCulture, "    #{0} {1}: {2:+0.00;-0.00} s{3}{4}", row.CarNumber, row.Name, row.Gap, plain, row.IsPlayer ? "  <- you" : ""));
            }
            report.AppendLine(string.Format(CultureInfo.InvariantCulture, "  radar: {0} cars within {1:0} m, in line ahead {2:0.0} m (closing {3:0.0} m/s), behind {4:0.0} m (closing {5:0.0} m/s)",
                s.Radar.Length, RadarTracker.Reach, s.AheadMeters, s.AheadClosing, s.BehindMeters, s.BehindClosing));
            report.AppendLine(string.Format(CultureInfo.InvariantCulture, "  radar against the spotter (1 clear, 2 left, 3 right, 4 both, 5 two left, 6 two right): a car alongside in {0} samples, " +
                "{1} of them with a car the positions couldn't place", alongsideSamples, unplaced));
            foreach (string line in radar) report.AppendLine(line);
            string screen = ScreenStats.Read();
            report.AppendLine("  screen: " + (screen ?? "no figures (the dashboard isn't running, or no wheel screen is connected)"));
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
