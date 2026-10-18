using System;
using System.Collections.Generic;

namespace AlmostFastDash.IRacing
{
    internal sealed partial class IRacingTelemetrySource : IDisposable
    {
        private readonly RadarTracker _radar = new RadarTracker();

        /// <summary>The cars around you for the radar, with the gaps to the cars in line ahead and behind.</summary>
        private void UpdateRadar(Telemetry t, TelemetrySnapshot s, int carLeftRight)
        {
            var byCar = new Dictionary<int, StandingsEntry>();
            foreach (StandingsEntry row in s.Standings ?? new StandingsEntry[0]) byCar[row.CarIdx] = row;
            var pace = new HashSet<int>();
            foreach (CarDetails car in t.CarDetails ?? new CarDetails[0])
                if (car?.Driver != null && car.Driver.CarIsPaceCar) pace.Add(car.CarIdx);
            double time = ReadTelemetryDouble(t, "SessionTime");
            _radar.Update(t.CarIdxLapDistPct, t.CarIdxOnPitRoad, ReadTelemetryInt(t, "PlayerCarIdx"), s.TrackLengthMiles * 1609.344,
                carLeftRight, time, car =>
            {
                if (byCar.TryGetValue(car.CarIdx, out StandingsEntry row))
                {
                    car.Number = row.CarNumber ?? "";
                    car.ClassColor = row.ClassColor;
                }
                car.PaceCar = pace.Contains(car.CarIdx);
            });
            s.Radar = _radar.Cars;
            _diagnostics?.NoteRadarCars(_radar.Cars);
            s.AheadMeters = _radar.AheadMeters;
            s.BehindMeters = _radar.BehindMeters;
            s.AheadClosing = _radar.AheadClosing;
            s.BehindClosing = _radar.BehindClosing;
        }

        private static string GetCarName(CarDetails[] details, int playerIndex)
        {
            try
            {
                if (details == null) return null;
                foreach (CarDetails detail in details)
                    if (detail != null && detail.CarIdx == playerIndex && detail.Driver != null)
                        return (string.IsNullOrWhiteSpace(detail.Driver.CarScreenNameShort) ? detail.Driver.CarScreenName
                            : detail.Driver.CarScreenNameShort)?.ToUpperInvariant();
            }
            catch { }
            return null;
        }

        /// <summary>Two cars ahead and two behind on track around the player.</summary>
        private static RelativeEntry[] BuildRelatives(Telemetry telemetry, CarDetails[] details)
        {
            try
            {
                float[] distances = telemetry.CarIdxLapDistPct;
                int[] laps = telemetry.CarIdxLap;
                int[] positions = telemetry.CarIdxPosition;
                bool[] pits = telemetry.CarIdxOnPitRoad;
                int player = ReadTelemetryInt(telemetry, "PlayerCarIdx");
                if (distances == null || player < 0 || player >= distances.Length || distances[player] < 0) return new RelativeEntry[0];

                double lapSeconds = 90;
                var byIndex = new System.Collections.Generic.Dictionary<int, CarDetails>();
                if (details != null)
                    foreach (CarDetails d in details)
                        if (d != null)
                        {
                            byIndex[d.CarIdx] = d;
                            if (d.CarIdx == player && d.Driver != null &&
                                d.Driver.CarClassEstLapTime > 10) lapSeconds = d.Driver.CarClassEstLapTime;
                        }

                double playerTotal = (laps != null && player < laps.Length ? laps[player] : 0) + distances[player];
                var ahead = new System.Collections.Generic.List<Tuple<double, int>>();
                var behind = new System.Collections.Generic.List<Tuple<double, int>>();
                for (int i = 0; i < distances.Length; i++)
                {
                    if (i == player || distances[i] < 0) continue;
                    if (byIndex.TryGetValue(i, out CarDetails car) && car.IsPaceCar) continue;
                    double delta = distances[i] - distances[player];
                    if (delta > 0.5) delta -= 1;
                    if (delta < -0.5) delta += 1;
                    if (delta >= 0) ahead.Add(Tuple.Create(delta, i)); else behind.Add(Tuple.Create(delta, i));
                }
                ahead.Sort((a, b) => a.Item1.CompareTo(b.Item1));
                behind.Sort((a, b) => b.Item1.CompareTo(a.Item1));

                float[] estimates = ReadFloats(telemetry, "CarIdxEstTime");
                var rows = new System.Collections.Generic.List<RelativeEntry>();
                for (int k = Math.Min(2, ahead.Count) - 1; k >= 0; k--) rows.Add(Entry(ahead[k].Item2, ahead[k].Item1));
                rows.Add(Entry(player, 0));
                for (int k = 0; k < Math.Min(2, behind.Count); k++) rows.Add(Entry(behind[k].Item2, behind[k].Item1));
                return rows.ToArray();

                RelativeEntry Entry(int index, double delta)
                {
                    byIndex.TryGetValue(index, out CarDetails car);
                    RelativeDriver info = BuildRelative(details, index, GetDriverName(details, index));
                    double total = (laps != null && index < laps.Length ? laps[index] : 0) + distances[index];
                    double lapGap = total - playerTotal - delta;
                    return new RelativeEntry
                    {
                        Position = positions != null && index < positions.Length ? positions[index] : 0,
                        CarNumber = info?.CarNumber ?? car?.CarNumberDisplay,
                        Name = info?.Name ?? ("CAR " + index),
                        Gap = index == player ? 0 : -TrackGap(estimates, player, index, delta, lapSeconds),
                        LapRelation = index == player ? 0 : lapGap > 0.5 ? 1 : lapGap < -0.5 ? -1 : 0,
                        IsPlayer = index == player,
                        InPits = pits != null && index < pits.Length && pits[index],
                        License = info?.License,
                        LicenseColor = info?.LicenseColor ?? System.Drawing.Color.Gray,
                        IRating = info?.IRating ?? 0
                    };
                }
            }
            catch
            {
                return new RelativeEntry[0];
            }
        }

        private static RelativeDriver BuildRelative(CarDetails[] details, int carIndex, string displayName)
        {
            if (details == null || carIndex < 0) return null;
            foreach (CarDetails detail in details)
            {
                if (detail == null || detail.CarIdx != carIndex) continue;
                var result = new RelativeDriver { Name = displayName, CarNumber = detail.CarNumberDisplay };
                try
                {
                    var driver = detail.Driver;
                    if (driver != null)
                    {
                        result.IRating = (int)driver.IRating;
                        string lic = driver.LicString ?? "";
                        result.License = lic.Length > 0 ? lic.Substring(0, 1) : null;
                        result.LicenseColor = ParseLicenseColor(driver.LicColor);
                        if (string.IsNullOrEmpty(result.CarNumber)) result.CarNumber = driver.CarNumber;
                    }
                }
                catch { }
                return result;
            }
            return null;
        }

        private static System.Drawing.Color ParseLicenseColor(string value)
        {
            try
            {
                string hex = (value ?? "").Trim();
                if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) hex = hex.Substring(2);
                int rgb = Convert.ToInt32(hex, 16);
                return System.Drawing.Color.FromArgb(255, (rgb >> 16) & 0xff, (rgb >> 8) & 0xff, rgb & 0xff);
            }
            catch
            {
                return System.Drawing.Color.Gray;
            }
        }

        private static void GetNearestRelatives(
            Telemetry telemetry,
            out string driverAhead,
            out double relativeAhead,
            out string driverBehind,
            out double relativeBehind,
            out int aheadIndex,
            out int behindIndex)
        {
            aheadIndex = -1;
            behindIndex = -1;
            driverAhead = "---";
            driverBehind = "---";
            relativeAhead = 0;
            relativeBehind = 0;

            float[] distances = telemetry.CarIdxLapDistPct;
            if (distances == null || distances.Length == 0) return;
            int playerIndex = ReadTelemetryInt(telemetry, "PlayerCarIdx");
            if (playerIndex < 0 || playerIndex >= distances.Length) return;

            double estimatedLapSeconds = 90;
            CarDetails[] details = telemetry.CarDetails;
            if (details != null)
            {
                foreach (CarDetails detail in details)
                {
                    if (detail != null && detail.CarIdx == playerIndex && detail.Driver != null &&
                        detail.Driver.CarClassEstLapTime > 10)
                    {
                        estimatedLapSeconds = detail.Driver.CarClassEstLapTime;
                        break;
                    }
                }
            }

            double closestAhead = double.MaxValue;
            double closestBehind = double.MinValue;
            for (int index = 0; index < distances.Length; index++)
            {
                if (index == playerIndex || distances[index] < 0) continue;
                double delta = distances[index] - distances[playerIndex];
                if (delta > 0.5) delta -= 1;
                if (delta < -0.5) delta += 1;
                if (delta > 0 && delta < closestAhead)
                {
                    closestAhead = delta;
                    aheadIndex = index;
                }
                else if (delta < 0 && delta > closestBehind)
                {
                    closestBehind = delta;
                    behindIndex = index;
                }
            }

            float[] estimates = ReadFloats(telemetry, "CarIdxEstTime");
            if (aheadIndex >= 0)
            {
                driverAhead = GetDriverName(details, aheadIndex);
                relativeAhead = -TrackGap(estimates, playerIndex, aheadIndex, closestAhead, estimatedLapSeconds);
            }
            if (behindIndex >= 0)
            {
                driverBehind = GetDriverName(details, behindIndex);
                relativeBehind = -TrackGap(estimates, playerIndex, behindIndex, closestBehind, estimatedLapSeconds);
            }
        }

        /// <summary>
        /// Seconds from the player to another car on track: positive when it is ahead. iRacing gives every
        /// car an estimated time to reach where it is on the lap (CarIdxEstTime), built from the track's own
        /// speed profile. The difference between two cars' estimates follows the slow corners and the fast
        /// straights between them. Distance times a whole-lap average assumes the lap is equally fast all
        /// round, so it is only used when the estimates are missing or don't make sense.
        /// </summary>
        /// <param name="lapDelta">The other car's lead on the lap, -0.5 to 0.5 of a lap.</param>
        internal static double TrackGap(float[] estimates, int player, int other, double lapDelta, double lapSeconds)
        {
            double plain = lapDelta * lapSeconds;
            if (estimates == null || player < 0 || other < 0 || player >= estimates.Length || other >= estimates.Length) return plain;
            double mine = estimates[player], theirs = estimates[other];
            if (mine < 0 || theirs < 0 || (mine == 0 && theirs == 0)) return plain;
            double gap = theirs - mine;
            // Across the start/finish line the estimates restart from zero.
            if (lapDelta > 0 && gap < 0) gap += lapSeconds;
            else if (lapDelta < 0 && gap > 0) gap -= lapSeconds;
            // A gap far from what the distance suggests means an estimate that can't be trusted right now.
            if (Math.Sign(gap) != Math.Sign(lapDelta) || Math.Abs(gap - plain) > lapSeconds * 0.25) return plain;
            return gap;
        }

        private static string GetDriverName(CarDetails[] details, int carIndex)
        {
            if (details == null) return "CAR " + carIndex;
            foreach (CarDetails detail in details)
            {
                if (detail == null || detail.CarIdx != carIndex || detail.IsPaceCar) continue;
                string name = detail.UserName;
                if (string.IsNullOrWhiteSpace(name)) return "CAR " + carIndex;
                string[] parts = name.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 1) return parts[0].ToUpperInvariant();
                return (parts[0][0] + ". " + parts[parts.Length - 1]).ToUpperInvariant();
            }
            return "CAR " + carIndex;
        }

        private static BlindSpotZone GetNearestOpponentZone(Telemetry telemetry, out double? nearestDeltaResult)
        {
            nearestDeltaResult = null;
            float[] distances = telemetry.CarIdxLapDistPct;
            if (distances == null || distances.Length == 0)
                return BlindSpotZone.Beside;

            int playerIndex = ReadTelemetryInt(telemetry, "PlayerCarIdx");
            if (playerIndex < 0 || playerIndex >= distances.Length)
                return BlindSpotZone.Beside;

            double playerDistance = distances[playerIndex];
            double nearestDelta = double.MaxValue;
            for (int index = 0; index < distances.Length; index++)
            {
                if (index == playerIndex || distances[index] < 0) continue;
                double delta = distances[index] - playerDistance;
                if (delta > 0.5) delta -= 1.0;
                if (delta < -0.5) delta += 1.0;
                if (Math.Abs(delta) < Math.Abs(nearestDelta)) nearestDelta = delta;
            }

            if (nearestDelta == double.MaxValue) return BlindSpotZone.Beside;
            nearestDeltaResult = nearestDelta;
            if (nearestDelta > AlongsideDistancePct) return BlindSpotZone.Front;
            if (nearestDelta < -AlongsideDistancePct) return BlindSpotZone.Rear;
            return BlindSpotZone.Beside;
        }

        private BlindSpotZone UpdateBlindSpotZone(
            bool occupied,
            BlindSpotZone measuredZone,
            ref BlindSpotZone previousZone,
            ref DateTime clearedAtUtc)
        {
            if (occupied)
            {
                previousZone = measuredZone;
                clearedAtUtc = DateTime.MinValue;
                return measuredZone;
            }

            if (previousZone != BlindSpotZone.None && clearedAtUtc == DateTime.MinValue)
                clearedAtUtc = UtcNow;

            if (clearedAtUtc != DateTime.MinValue &&
                (UtcNow - clearedAtUtc).TotalMilliseconds < RearHoldMilliseconds)
                return BlindSpotZone.Rear;

            previousZone = BlindSpotZone.None;
            clearedAtUtc = DateTime.MinValue;
            return BlindSpotZone.None;
        }
    }
}
