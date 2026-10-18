using System;

namespace AlmostFastDash.IRacing
{
    internal sealed partial class IRacingTelemetrySource : IDisposable
    {
        /// <summary>A representative snapshot for gallery thumbnails and layout review.</summary>
        public static TelemetrySnapshot CreatePreviewSnapshot(RacingFlag flag = RacingFlag.None)
        {
            var yellowLaps = new bool[18];
            yellowLaps[3] = true;
            return WithPreviewExtras(new TelemetrySnapshot(
                true, 4, 136, flag, BlindSpotZone.None, BlindSpotZone.None, 0.62,
                SessionProgressMode.LapRace, 0, 0, 8, 18,
                4, 2, 24.3, 58.104, 0.46,
                "J. MILLER", -0.46, "M. CARTER", 0.82, 22,
                false, 87.932, 87.521, 88.204, yellowLaps, 0, 6, 6120));
        }

        /// <summary>Preview of a car alongside on the left and one slightly ahead on the right.</summary>
        public static TelemetrySnapshot CreateSpotterPreviewSnapshot()
        {
            TelemetrySnapshot snapshot = WithPreviewExtras(new TelemetrySnapshot(
                true, 4, 178, RacingFlag.None, BlindSpotZone.Beside, BlindSpotZone.Front, 0.72,
                SessionProgressMode.LapRace, 0, 0, 8, 18,
                4, 2, 24.3, 18.4, 0.12,
                "J. MILLER", -0.21, "M. CARTER", 0.18, 22,
                false, 87.932, 87.521, 88.204, new bool[18], 0, 6, 7900));
            return snapshot;
        }

        /// <summary>Preview of driving down pit lane with the limiter on.</summary>
        public static TelemetrySnapshot CreatePitLanePreviewSnapshot()
        {
            TelemetrySnapshot snapshot = WithPreviewExtras(new TelemetrySnapshot(
                true, 2, 44.2, RacingFlag.None, BlindSpotZone.None, BlindSpotZone.None, 0.3,
                SessionProgressMode.LapRace, 0, 0, 8, 18,
                4, 2, 3.1, 31.2, 0,
                "J. MILLER", -4.1, "M. CARTER", 2.2, 22,
                false, 87.932, 87.521, 88.204, new bool[18], 0, 6, 3900));
            snapshot.OnPitRoad = true;
            snapshot.PitLimiter = true;
            return snapshot;
        }

        internal static TelemetrySnapshot WithPreviewExtras(TelemetrySnapshot snapshot)
        {
            snapshot.Discipline = Disciplines.Road;
            snapshot.BrakeBias = 54.2;
            snapshot.FuelLevelPct = 0.41;
            snapshot.FuelLastLap = 2.41;
            snapshot.FuelPerLapAverage = 2.46;
            snapshot.FuelLapsMeasured = 5;
            snapshot.StintLaps = 8;
            snapshot.RecentLapSeconds = new[] { 88.412, 87.932, 88.105, 87.521, 87.932 };
            // A stint wearing its tyres: up to speed on lap two, then a little slower each lap.
            snapshot.StintLapSeconds = new[] { 88.204, 87.521, 87.566, 87.618, 87.652, 87.711, 87.748, 87.802, 87.861, 87.932 };
            snapshot.AheadDetail = new RelativeDriver { Name = snapshot.DriverAhead, CarNumber = "05", License = "B",
                LicenseColor = System.Drawing.Color.FromArgb(0, 164, 90), IRating = 2340 };
            snapshot.BehindDetail = new RelativeDriver { Name = snapshot.DriverBehind, CarNumber = "31", License = "A",
                LicenseColor = System.Drawing.Color.FromArgb(0, 120, 220), IRating = 3120 };
            snapshot.PlayerIRating = 2815;
            snapshot.TractionControl = 4;
            snapshot.Abs = 3;
            snapshot.LapMinSpeedMph = 71;
            snapshot.LapMaxSpeedMph = 152;
            snapshot.PredictedLapSeconds = 87.061;
            var throttle = new float[120];
            var brake = new float[120];
            for (int i = 0; i < 120; i++)
            {
                // A representative braking zone and corner exit.
                double x = i / 119.0;
                brake[i] = (float)(x > 0.18 && x < 0.40 ? Math.Min(1, Math.Sin((x - 0.18) / 0.22 * Math.PI) * 1.15) * 0.92 : 0);
                throttle[i] = (float)(x < 0.17 ? 1 : x < 0.46 ? 0.02 : x < 0.62 ? (x - 0.46) / 0.16 * 0.7 : x < 0.7 ? 0.7
                    : Math.Min(1, 0.7 + (x - 0.7) * 3));
            }
            snapshot.Throttle = 1;
            snapshot.ClassPosition = 4;
            snapshot.CarName = "PORSCHE 911 GT3 R";
            snapshot.StartPosition = 7;
            snapshot.TrackLengthMiles = 2.5;
            snapshot.ShiftFirstRpm = 3800;
            snapshot.ShiftRpm = 7700;
            snapshot.ShiftLastRpm = 7800;
            snapshot.ShiftBlinkRpm = 7950;
            snapshot.RedlineRpm = 8200;
            snapshot.TrackTempC = 31.4;
            snapshot.AirTempC = 22.0;
            snapshot.PitSpeedLimitMph = 45;
            var delta = new float[80];
            for (int i = 0; i < 80; i++) delta[i] = (float)(0.46 * i / 79.0 + 0.18 * Math.Sin(i / 9.0) - 0.12);
            snapshot.DeltaTrace = delta;
            snapshot.Relatives = new[]
            {
                new RelativeEntry { Position = 2, CarNumber = "12", Name = "S. OKAFOR", Gap = -3.12, License = "A",
                    LicenseColor = System.Drawing.Color.FromArgb(0, 120, 220), IRating = 3890 },
                new RelativeEntry { Position = 3, CarNumber = "05", Name = "J. MILLER", Gap = -0.46, License = "B",
                LicenseColor = System.Drawing.Color.FromArgb(0, 164, 90), IRating = 2340 },
                new RelativeEntry { Position = 4, CarNumber = "22", Name = "YOU", Gap = 0, IsPlayer = true, License = "A",
                LicenseColor = System.Drawing.Color.FromArgb(0, 120, 220), IRating = 2815 },
                new RelativeEntry { Position = 5, CarNumber = "31", Name = "M. CARTER", Gap = 0.82, License = "A",
                LicenseColor = System.Drawing.Color.FromArgb(0, 120, 220), IRating = 3120 },
                new RelativeEntry { Position = 17, CarNumber = "88", Name = "R. ALVES", Gap = 2.40, LapRelation = -1, License = "D",
                LicenseColor = System.Drawing.Color.FromArgb(230, 120, 20), IRating = 1180 }
            };
            snapshot.ThrottleTrace = throttle;
            snapshot.BrakeTrace = brake;
            PreviewRaceData(snapshot);
            RadarTracker.Preview(snapshot);
            return snapshot;
        }
    }
}
