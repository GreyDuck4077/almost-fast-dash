using System;
using System.Collections.Generic;

namespace AlmostFastDash.IRacing
{
    internal enum RacingFlag
    {
        None,
        Green,
        White,
        Yellow,
        Red,
        Black,
        Meatball,
        BlueYellowStripe,
        SafetyCar,
        Surface,
        Checkered
    }

    internal enum BlindSpotZone
    {
        None,
        Front,
        Beside,
        Rear
    }

    internal enum SessionProgressMode
    {
        None,
        TimedRace,
        LapRace,
        Practice
    }

    /// <summary>Details shown for the nearest car ahead or behind.</summary>
    internal sealed class RelativeDriver
    {
        public string Name { get; set; }
        public string CarNumber { get; set; }
        public string License { get; set; }
        public System.Drawing.Color LicenseColor { get; set; } = System.Drawing.Color.Gray;
        public int IRating { get; set; }
    }

    /// <summary>One row of the on-track relative: nearby cars sorted by track position.</summary>
    internal sealed class RelativeEntry
    {
        public int Position { get; set; }
        public string CarNumber { get; set; }
        public string Name { get; set; }
        /// <summary>Seconds on track to this car; negative ahead, positive behind, 0 for the player.</summary>
        public double Gap { get; set; }
        /// <summary>+1 when the car is a lap or more ahead in the race, -1 when a lap or more down.</summary>
        public int LapRelation { get; set; }
        public bool IsPlayer { get; set; }
        public bool InPits { get; set; }
        public string License { get; set; }
        public System.Drawing.Color LicenseColor { get; set; } = System.Drawing.Color.Gray;
        public int IRating { get; set; }
    }

    /// <summary>Where the driver is in iRacing, for showing the dashboard only at some of them.</summary>
    internal enum CarState
    {
        /// <summary>Driving, or sitting in the car (in the pits too).</summary>
        InCar,
        /// <summary>Got out of the car: in the sim's menus, or watching yourself.</summary>
        OutOfCar,
        /// <summary>On the garage and setup screen.</summary>
        Garage,
        /// <summary>Watching another driver, live or in a replay.</summary>
        Spectating
    }

    internal sealed class TelemetrySnapshot
    {
        public static readonly TelemetrySnapshot Disconnected = new TelemetrySnapshot(
            false, 0, 0, RacingFlag.None, BlindSpotZone.None, BlindSpotZone.None, 0,
            SessionProgressMode.None, 0, 0, 0, 0,
            0, 0, 0, 0, 0, "---", 0, "---", 0, 0,
            false, 0, 0, 0, new bool[0], 0, 0);

        public TelemetrySnapshot(
            bool connected,
            int gear,
            double speedMph,
            RacingFlag flag,
            BlindSpotZone leftBlindSpot,
            BlindSpotZone rightBlindSpot,
            double shiftIndicatorPct,
            SessionProgressMode progressMode,
            double sessionSecondsRemaining,
            double sessionTotalSeconds,
            int lapsCompleted,
            int totalLaps,
            int position,
            int incidentPoints,
            double fuelLiters,
            double currentLapSeconds,
            double lapDeltaSeconds,
            string driverAhead,
            double relativeAheadSeconds,
            string driverBehind,
            double relativeBehindSeconds,
            int totalStarters,
            bool showLapSummary,
            double lastLapSeconds,
            double bestLapSeconds,
            double averageLapSeconds,
            bool[] yellowCompletedLaps,
            int lapColorVersion,
            int maxForwardGear,
            double engineRpm = 0)
        {
            Connected = connected;
            Gear = gear;
            SpeedMph = speedMph;
            Flag = flag;
            LeftBlindSpot = leftBlindSpot;
            RightBlindSpot = rightBlindSpot;
            ShiftIndicatorPct = Math.Max(0, Math.Min(1, shiftIndicatorPct));
            ProgressMode = progressMode;
            SessionSecondsRemaining = Math.Max(0, sessionSecondsRemaining);
            SessionTotalSeconds = Math.Max(0, sessionTotalSeconds);
            LapsCompleted = Math.Max(0, lapsCompleted);
            TotalLaps = Math.Max(0, totalLaps);
            Position = Math.Max(0, position);
            IncidentPoints = Math.Max(0, incidentPoints);
            FuelLiters = Math.Max(0, fuelLiters);
            CurrentLapSeconds = Math.Max(0, currentLapSeconds);
            LapDeltaSeconds = lapDeltaSeconds;
            DriverAhead = string.IsNullOrWhiteSpace(driverAhead) ? "---" : driverAhead;
            RelativeAheadSeconds = relativeAheadSeconds;
            DriverBehind = string.IsNullOrWhiteSpace(driverBehind) ? "---" : driverBehind;
            RelativeBehindSeconds = relativeBehindSeconds;
            TotalStarters = Math.Max(0, totalStarters);
            ShowLapSummary = showLapSummary;
            LastLapSeconds = Math.Max(0, lastLapSeconds);
            BestLapSeconds = Math.Max(0, bestLapSeconds);
            AverageLapSeconds = Math.Max(0, averageLapSeconds);
            YellowCompletedLaps = yellowCompletedLaps ?? new bool[0];
            LapColorVersion = lapColorVersion;
            MaxForwardGear = Math.Max(0, maxForwardGear);
            EngineRpm = Math.Max(0, engineRpm);
        }

        public bool Connected { get; }
        public int Gear { get; }
        public double SpeedMph { get; }
        public RacingFlag Flag { get; }
        public BlindSpotZone LeftBlindSpot { get; }
        public BlindSpotZone RightBlindSpot { get; }
        public double ShiftIndicatorPct { get; }
        public bool ShiftAlert => ShiftIndicatorPct >= 0.98;
        public SessionProgressMode ProgressMode { get; }
        public double SessionSecondsRemaining { get; }
        public double SessionTotalSeconds { get; }
        public int LapsCompleted { get; }
        public int TotalLaps { get; }
        public int Position { get; }
        public int IncidentPoints { get; }
        public double FuelLiters { get; }
        public double CurrentLapSeconds { get; }
        public double LapDeltaSeconds { get; }
        public string DriverAhead { get; }
        public double RelativeAheadSeconds { get; }
        public string DriverBehind { get; }
        public double RelativeBehindSeconds { get; }
        public int TotalStarters { get; }
        public bool ShowLapSummary { get; }
        public double LastLapSeconds { get; }
        public double BestLapSeconds { get; }
        public double AverageLapSeconds { get; }
        public bool[] YellowCompletedLaps { get; }
        public int LapColorVersion { get; }
        public int MaxForwardGear { get; }
        public double EngineRpm { get; }

        // Extended values are filled in after construction by the telemetry
        // source; the shared Disconnected instance keeps their defaults.
        public string Discipline { get; set; }
        public double Brake { get; set; }
        public double Throttle { get; set; }
        public double BrakeBias { get; set; }
        public double FuelLevelPct { get; set; }
        /// <summary>Fuel used on the last clean lap in liters, or -1 when unknown.</summary>
        public double FuelLastLap { get; set; } = -1;
        /// <summary>Average fuel per clean lap in liters, or -1 when unknown.</summary>
        public double FuelPerLapAverage { get; set; } = -1;
        public int StintLaps { get; set; }
        public double[] RecentLapSeconds { get; set; } = new double[0];
        /// <summary>Green-flag lap times of this stint, oldest first (no pit lane or caution laps): the lap falloff.</summary>
        public double[] StintLapSeconds { get; set; } = new double[0];
        public RelativeDriver AheadDetail { get; set; }
        public RelativeDriver BehindDetail { get; set; }
        public int PlayerIRating { get; set; }
        /// <summary>In-car traction control and ABS settings; negative when the car has none.</summary>
        public double TractionControl { get; set; } = -1;
        public double Abs { get; set; } = -1;
        public double LapMinSpeedMph { get; set; }
        public double LapMaxSpeedMph { get; set; }
        /// <summary>Predicted time of the current lap from the live delta, or 0 when unknown.</summary>
        public double PredictedLapSeconds { get; set; }
        /// <summary>Throttle and brake history (0-1), oldest first, sampled every 50 ms.</summary>
        public float[] ThrottleTrace { get; set; } = new float[0];
        public float[] BrakeTrace { get; set; } = new float[0];
        public int TraceVersion { get; set; }
        public RelativeEntry[] Relatives { get; set; } = new RelativeEntry[0];
        public string CarName { get; set; }
        /// <summary>True for the whole caution period, not just the on-screen alert pulse.</summary>
        public bool CautionActive { get; set; }
        public bool WhiteFlagActive { get; set; }
        /// <summary>Position when the race went green, or 0 when unknown.</summary>
        public int StartPosition { get; set; }
        public double TrackLengthMiles { get; set; }
        /// <summary>Average speed of a lap in MPH from the track length, or 0 when unknown.</summary>
        public double LapSpeedMph(double lapSeconds) =>
            TrackLengthMiles > 0 && lapSeconds > 0 ? TrackLengthMiles / (lapSeconds / 3600.0) : 0;
        public int ClassPosition { get; set; }
        public bool OnPitRoad { get; set; }
        public bool PitLimiter { get; set; }
        public double PitSpeedLimitMph { get; set; }
        public double TrackTempC { get; set; } = double.NaN;
        public double AirTempC { get; set; } = double.NaN;
        public bool EngineWarning { get; set; }
        /// <summary>True while ABS is working on this sample.</summary>
        public bool AbsActive { get; set; }
        /// <summary>True when connected but the driver isn't driving: out of the car, or watching a replay.</summary>
        public bool OutOfCar { get; set; }

        /// <summary>In the car, out of it, in the garage, or watching another driver.</summary>
        public CarState CarState { get; set; }

        /// <summary>What the LEDs follow: nothing while out of the car or in a replay, so they go dark instead of replaying alerts.</summary>
        public TelemetrySnapshot ForLights() => Connected && OutOfCar ? Disconnected : this;
        /// <summary>The car's shift-light RPMs from iRacing; 0 when the car does not report them.</summary>
        public double ShiftFirstRpm { get; set; }
        public double ShiftRpm { get; set; }
        public double ShiftLastRpm { get; set; }
        public double ShiftBlinkRpm { get; set; }
        public double RedlineRpm { get; set; }
        /// <summary>Lap delta history (seconds, positive = faster), oldest first, sampled every 250 ms.</summary>
        public float[] DeltaTrace { get; set; } = new float[0];

        // Engine. Temperatures in °C and pressures in bar; NaN when the car doesn't report them.
        public double OilTempC { get; set; } = double.NaN;
        public double OilPressureBar { get; set; } = double.NaN;
        public double WaterTempC { get; set; } = double.NaN;
        public double FuelPressureBar { get; set; } = double.NaN;
        public double Voltage { get; set; } = double.NaN;
        /// <summary>DRS: -1 when the car has none, 0 off, 1 available soon, 2 available now, 3 open.</summary>
        public int DrsState { get; set; } = -1;
        public double FuelCapacityLiters { get; set; }

        // Timing against the whole field, and sectors. Times are seconds, 0 when unknown.
        public double SessionBestLapSeconds { get; set; }
        public double ClassBestLapSeconds { get; set; }
        /// <summary>The sector you are in, from 1; 0 when the track's sectors are unknown.</summary>
        public int Sector { get; set; }
        public int SectorCount { get; set; }
        public double SectorRunningSeconds { get; set; }
        public double LastSectorSeconds { get; set; }
        /// <summary>Sector times of the lap in progress, of the last lap and your best for each sector.</summary>
        public double[] SectorTimes { get; set; } = new double[0];
        public double[] LastLapSectorTimes { get; set; } = new double[0];
        public double[] BestSectorTimes { get; set; } = new double[0];

        /// <summary>Front left, front right, rear left, rear right.</summary>
        public TyreCorner[] Tyres { get; set; } = new TyreCorner[0];
        /// <summary>Every car in race order.</summary>
        public StandingsEntry[] Standings { get; set; } = new StandingsEntry[0];
        /// <summary>A short race message (flags, penalties, a personal best), empty when there is none.</summary>
        public string Notice { get; set; } = "";
        /// <summary>The kind of notice: Info, Good, Warning or Danger, for coloring it.</summary>
        public string NoticeKind { get; set; } = "";

        /// <summary>Made-up data for previews (the designer, gallery cards), not a live session.</summary>
        public bool IsPreview { get; set; }

        /// <summary>This track's traced outline, or null until a clean lap has been driven here.</summary>
        public TrackMap TrackMap { get; set; }
        /// <summary>How far through tracing the map the current lap is (0 to 1); -1 when not tracing.</summary>
        public double MapProgress { get; set; } = -1;
        /// <summary>Every car's lap fraction by car index (-1 when not on track), and which car is yours.</summary>
        public float[] CarLapDist { get; set; } = new float[0];
        public int PlayerCarIdx { get; set; } = -1;
        /// <summary>The cars near you for the radar (see <see cref="RadarTracker"/>), nearest behind first.</summary>
        public RadarCar[] Radar { get; set; } = new RadarCar[0];
        /// <summary>Metres to the nearest car in line ahead and behind, middle to middle; -1 when there is none.</summary>
        public double AheadMeters { get; set; } = -1;
        public double BehindMeters { get; set; } = -1;
        /// <summary>How fast those gaps are closing in metres a second (positive: getting closer).</summary>
        public double AheadClosing { get; set; }
        public double BehindClosing { get; set; }

        public double ReferenceLapSeconds =>
            AverageLapSeconds > 0 ? AverageLapSeconds : BestLapSeconds > 0 ? BestLapSeconds : LastLapSeconds;

        /// <summary>Laps left in the session including the current one, or -1 when unknown.</summary>
        public double LapsRemaining
        {
            get
            {
                if (ProgressMode == SessionProgressMode.LapRace && TotalLaps > 0)
                    return Math.Max(0, TotalLaps - LapsCompleted);
                if (ProgressMode == SessionProgressMode.TimedRace && ReferenceLapSeconds > 0)
                    return Math.Ceiling(SessionSecondsRemaining / ReferenceLapSeconds);
                return -1;
            }
        }

        /// <summary>Laps the remaining fuel lasts at the average rate, or -1 when unknown.</summary>
        public double FuelLapsLeft => FuelPerLapAverage > 0 ? FuelLiters / FuelPerLapAverage : -1;

        /// <summary>How far round the current lap the player is, 0 to 1.</summary>
        public double LapFraction =>
            PlayerCarIdx >= 0 && CarLapDist != null && PlayerCarIdx < CarLapDist.Length && CarLapDist[PlayerCarIdx] >= 0 ? Math.Min(1, CarLapDist[PlayerCarIdx]) : 0;

        /// <summary>
        /// Laps still to drive to the flag, counting only what's left of this one (3.4 is the last 0.4 of this
        /// lap and three more), or -1 when unknown. A timed race ends when the leader next crosses the line
        /// after the clock runs out, so the lap in progress at zero is finished. That is reckoned at your own
        /// pace, which is right for the leader's lap and errs toward more fuel for anyone behind.
        /// </summary>
        public double LapsToGo
        {
            get
            {
                double rest = 1 - LapFraction;
                if (ProgressMode == SessionProgressMode.LapRace && TotalLaps > 0)
                    return Math.Max(0, TotalLaps - LapsCompleted - LapFraction);
                if (ProgressMode == SessionProgressMode.TimedRace && ReferenceLapSeconds > 0)
                {
                    double seconds = Math.Max(0, SessionSecondsRemaining), lap = ReferenceLapSeconds;
                    return seconds <= rest * lap ? rest : rest + Math.Ceiling((seconds - rest * lap) / lap);
                }
                return -1;
            }
        }

        /// <summary>Fuel to add to reach the finish in liters, or -1 when unknown.</summary>
        public double FuelToFinish => FuelToFinishWith(0);

        /// <summary>Fuel to add to reach the finish with a margin of <paramref name="reserveLaps"/> more laps, in liters; -1 when unknown.</summary>
        public double FuelToFinishWith(double reserveLaps)
        {
            double laps = LapsToGo;
            if (laps < 0 || FuelPerLapAverage <= 0) return -1;
            return Math.Max(0, (laps + Math.Max(0, reserveLaps)) * FuelPerLapAverage - FuelLiters);
        }

        /// <summary>How many clean laps the fuel use is averaged over; under three it is an early estimate.</summary>
        public int FuelLapsMeasured { get; set; }
    }

    internal sealed partial class IRacingTelemetrySource : IDisposable
    {
        private const double MetersPerSecondToMph = 2.2369362920544;
        private readonly object _sync = new object();
        private readonly DiagnosticLogger _diagnostics;
        private int _alertBlinkMilliseconds;
        private int _alertBlinkTotalMilliseconds;
        private int _alertHoldMilliseconds;
        private int _disciplineSessionId = -1;
        private string _discipline;
        private double _fuelAtLapStart = -1;
        private bool _lapTouchedPit;
        private double _lastLapFuel = -1;
        private readonly System.Collections.Generic.List<double> _fuelPerLap = new System.Collections.Generic.List<double>();
        private readonly System.Collections.Generic.List<double> _recentLaps = new System.Collections.Generic.List<double>();
        private int _stintLaps;
        private const int FuelAverageLaps = 5;
        // Green-flag laps of this stint (no pit lane, no caution), for lap falloff. A lap's time is taken
        // once iRacing updates LapLastLapTime, which can lag the lap counter by a few frames.
        private readonly System.Collections.Generic.List<double> _stintGreenLaps = new System.Collections.Generic.List<double>();
        private double[] _stintGreenArray = new double[0];
        private bool _pendingLap, _pendingLapClean;
        private double _pendingLapSeen;
        private int _pendingLapAt;
        private const int StintLapsKept = 40;
        private TelemetrySnapshot _latest = TelemetrySnapshot.Disconnected;
        private bool _listening;
        private bool _hasResetCounter;
        private int _lastEnterExitReset;
        private bool _suppressHeldBlackFlag;
        private BlindSpotZone _lastLeftBlindSpot;
        private BlindSpotZone _lastRightBlindSpot;
        private DateTime _leftClearedAtUtc;
        private DateTime _rightClearedAtUtc;
        private bool _greenWasActive;
        private DateTime _greenAlertUntilUtc;
        private bool _yellowWasActive;
        private DateTime _yellowAlertUntilUtc;
        private int _lastSessionUniqueId = -1;
        private int _lastCompletedLap = -1;
        private double _completedLapTimeTotal;
        private int _completedLapTimeCount;
        private double _lastLapSeconds;
        private DateTime _lapSummaryUntilUtc;
        private readonly bool[] _yellowCompletedLaps = new bool[2048];
        private bool _currentLapHadYellow;
        private int _lapColorVersion;
        private bool _checkeredWasActive;
        private DateTime _checkeredAlertUntilUtc;
        private int _checkeredSession = int.MinValue;
        private bool _whiteWasActive;
        private DateTime _whiteAlertUntilUtc;
        private int _maximumObservedGear;
        private const int TraceLength = 120;
        private const int TraceIntervalMilliseconds = 50;
        private readonly float[] _throttleTrace = new float[TraceLength];
        private readonly float[] _brakeTrace = new float[TraceLength];
        private int _traceHead;
        private int _traceVersion;
        private DateTime _nextTraceUtc;
        private float[] _throttleTraceCopy = new float[0];
        private float[] _brakeTraceCopy = new float[0];
        private int _speedLap = -1;
        private int _startPositionSession = -1;
        private int _startPosition;
        private int _trackLengthSession = -1;
        private double _trackLengthMiles;
        private const int DeltaTraceLength = 80;
        private readonly float[] _deltaTrace = new float[DeltaTraceLength];
        private int _deltaHead;
        private DateTime _nextDeltaUtc;
        private float[] _deltaTraceCopy = new float[0];
        private int _pitLimitSession = -1;
        private double _pitLimitMph;
        private double _lapMinSpeed;
        private double _lapMaxSpeed;

        private const double AlongsideDistancePct = 0.0008;
        private const int RearHoldMilliseconds = 750;

        /// <summary>The clock alerts and holds are timed by; a recording replay sets it to the recorded session time.</summary>
        internal Func<DateTime> Clock;
        private DateTime UtcNow => Clock == null ? DateTime.UtcNow : Clock();
        private int TickNow => Clock == null ? Environment.TickCount : (int)(Clock().Ticks / TimeSpan.TicksPerMillisecond);

        public IRacingTelemetrySource(DiagnosticLogger diagnostics, DashboardSettings dashboardSettings)
        {
            _diagnostics = diagnostics;
            ApplyAlertSettings(dashboardSettings?.Alerts);
        }

        public void ApplyAlertSettings(AlertSettings alerts)
        {
            alerts = alerts ?? new AlertSettings();
            lock (_sync)
            {
                _alertBlinkMilliseconds = Math.Max(50, alerts.BlinkMilliseconds);
                _alertBlinkTotalMilliseconds = Math.Max(1, alerts.BlinkCount) * _alertBlinkMilliseconds * 2;
                _alertHoldMilliseconds = Math.Max(0, alerts.HoldMilliseconds);
            }
        }

        public void Start()
        {
            if (_listening) return;
            iRacing.NewData += OnNewData;
            iRacing.StartListening();
            _listening = true;
        }

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
            snapshot.AheadDetail = new RelativeDriver { Name = snapshot.DriverAhead, CarNumber = "05", License = "B", LicenseColor = System.Drawing.Color.FromArgb(0, 164, 90), IRating = 2340 };
            snapshot.BehindDetail = new RelativeDriver { Name = snapshot.DriverBehind, CarNumber = "31", License = "A", LicenseColor = System.Drawing.Color.FromArgb(0, 120, 220), IRating = 3120 };
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
                throttle[i] = (float)(x < 0.17 ? 1 : x < 0.46 ? 0.02 : x < 0.62 ? (x - 0.46) / 0.16 * 0.7 : x < 0.7 ? 0.7 : Math.Min(1, 0.7 + (x - 0.7) * 3));
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
                new RelativeEntry { Position = 2, CarNumber = "12", Name = "S. OKAFOR", Gap = -3.12, License = "A", LicenseColor = System.Drawing.Color.FromArgb(0, 120, 220), IRating = 3890 },
                new RelativeEntry { Position = 3, CarNumber = "05", Name = "J. MILLER", Gap = -0.46, License = "B", LicenseColor = System.Drawing.Color.FromArgb(0, 164, 90), IRating = 2340 },
                new RelativeEntry { Position = 4, CarNumber = "22", Name = "YOU", Gap = 0, IsPlayer = true, License = "A", LicenseColor = System.Drawing.Color.FromArgb(0, 120, 220), IRating = 2815 },
                new RelativeEntry { Position = 5, CarNumber = "31", Name = "M. CARTER", Gap = 0.82, License = "A", LicenseColor = System.Drawing.Color.FromArgb(0, 120, 220), IRating = 3120 },
                new RelativeEntry { Position = 17, CarNumber = "88", Name = "R. ALVES", Gap = 2.40, LapRelation = -1, License = "D", LicenseColor = System.Drawing.Color.FromArgb(230, 120, 20), IRating = 1180 }
            };
            snapshot.ThrottleTrace = throttle;
            snapshot.BrakeTrace = brake;
            PreviewRaceData(snapshot);
            RadarTracker.Preview(snapshot);
            return snapshot;
        }

        public TelemetrySnapshot GetSnapshot()
        {
            lock (_sync)
            {
                return _latest;
            }
        }

        internal void OnNewData(DataSample sample)
        {
            if (sample == null || !sample.IsConnected || sample.Telemetry == null)
            {
                lock (_sync) _latest = TelemetrySnapshot.Disconnected;
                return;
            }

            lock (_sync)
            {
                SessionFlags sessionFlags = sample.Telemetry.SessionFlags;
                bool greenActive = (sessionFlags & SessionFlags.green) != 0;
                if (greenActive && !_greenWasActive)
                    _greenAlertUntilUtc = UtcNow.AddMilliseconds(
                        _alertBlinkTotalMilliseconds + _alertHoldMilliseconds);
                _greenWasActive = greenActive;
                double greenRemaining = (_greenAlertUntilUtc - UtcNow).TotalMilliseconds;
                bool greenPulseVisible = greenRemaining > 0 &&
                    (greenRemaining <= _alertHoldMilliseconds ||
                     ((TickNow / _alertBlinkMilliseconds) & 1) == 0);

                bool yellowActive =
                    (sessionFlags & SessionFlags.caution) != 0 ||
                    (sessionFlags & SessionFlags.cautionWaving) != 0 ||
                    (sessionFlags & SessionFlags.yellow) != 0 ||
                    (sessionFlags & SessionFlags.yellowWaving) != 0;
                if (yellowActive && !_yellowWasActive)
                    _yellowAlertUntilUtc = UtcNow.AddMilliseconds(
                        _alertBlinkTotalMilliseconds + _alertHoldMilliseconds);
                _yellowWasActive = yellowActive;
                double yellowRemaining = (_yellowAlertUntilUtc - UtcNow).TotalMilliseconds;
                bool yellowPulseVisible = yellowRemaining > 0 &&
                    (yellowRemaining <= _alertHoldMilliseconds ||
                     ((TickNow / _alertBlinkMilliseconds) & 1) == 0);

                bool checkeredActive = (sessionFlags & SessionFlags.checkered) != 0;
                // The checkered flag is shown once per session: iRacing can raise it again after the
                // finish (replays, leaving the car), and it should never hold the lights afterwards.
                if (checkeredActive && !_checkeredWasActive && sample.Telemetry.SessionUniqueID != _checkeredSession)
                {
                    _checkeredSession = sample.Telemetry.SessionUniqueID;
                    _checkeredAlertUntilUtc = UtcNow.AddSeconds(10);
                }
                _checkeredWasActive = checkeredActive;
                bool showCheckered = UtcNow < _checkeredAlertUntilUtc;

                // iRacing holds the white flag for the whole final lap. Like yellow, alert
                // on its rising edge only, then give the screen and lights back.
                bool whiteActive = (sessionFlags & SessionFlags.white) != 0;
                if (whiteActive && !_whiteWasActive)
                    _whiteAlertUntilUtc = UtcNow.AddMilliseconds(
                        _alertBlinkTotalMilliseconds + _alertHoldMilliseconds);
                _whiteWasActive = whiteActive;
                double whiteRemaining = (_whiteAlertUntilUtc - UtcNow).TotalMilliseconds;
                bool whitePulseVisible = whiteRemaining > 0 &&
                    (whiteRemaining <= _alertHoldMilliseconds ||
                     ((TickNow / _alertBlinkMilliseconds) & 1) == 0);

                bool blackFlagActive =
                    (sessionFlags & SessionFlags.black) != 0 ||
                    (sessionFlags & SessionFlags.disqualify) != 0;

                if (!_hasResetCounter)
                {
                    _lastEnterExitReset = sample.Telemetry.EnterExitReset;
                    _hasResetCounter = true;
                }
                else if (sample.Telemetry.EnterExitReset != _lastEnterExitReset)
                {
                    // iRacing may keep its session-level black bit asserted after a
                    // tow/reset. Clear the driver alert immediately and ignore that
                    // held bit until iRacing drops it, which rearms the next penalty.
                    _lastEnterExitReset = sample.Telemetry.EnterExitReset;
                    _suppressHeldBlackFlag = true;
                }

                if (!blackFlagActive)
                    _suppressHeldBlackFlag = false;

                int carLeftRight = ReadTelemetryInt(sample.Telemetry, "CarLeftRight");
                bool carOnLeft = carLeftRight == 2 || carLeftRight == 4 || carLeftRight == 5;
                bool carOnRight = carLeftRight == 3 || carLeftRight == 4 || carLeftRight == 6;
                double? nearestDelta;
                BlindSpotZone nearestZone = GetNearestOpponentZone(sample.Telemetry, out nearestDelta);
                BlindSpotZone leftZone = UpdateBlindSpotZone(
                    carOnLeft, nearestZone, ref _lastLeftBlindSpot, ref _leftClearedAtUtc);
                BlindSpotZone rightZone = UpdateBlindSpotZone(
                    carOnRight, nearestZone, ref _lastRightBlindSpot, ref _rightClearedAtUtc);
                _diagnostics?.RecordRadar(carLeftRight, nearestDelta, leftZone, rightZone);

                SessionProgressMode progressMode;
                double totalSeconds;
                int totalLaps;
                GetSessionProgress(sample.Telemetry, out progressMode, out totalSeconds, out totalLaps);
                string driverAhead;
                string driverBehind;
                double relativeAhead;
                double relativeBehind;
                GetNearestRelatives(
                    sample.Telemetry,
                    out driverAhead,
                    out relativeAhead,
                    out driverBehind,
                    out relativeBehind,
                    out int aheadCar,
                    out int behindCar);

                UpdateLapSummary(sample.Telemetry, yellowActive);
                int reportedMaxGear = ReadTelemetryInt(sample.Telemetry, "DriverCarGearNumForward");
                _maximumObservedGear = Math.Max(_maximumObservedGear, sample.Telemetry.Gear);
                int maxForwardGear = reportedMaxGear > 0 ? reportedMaxGear : _maximumObservedGear;

                double lapDelta = sample.Telemetry.LapDeltaToSessionBestLap_OK
                    ? -sample.Telemetry.LapDeltaToSessionBestLap
                    : 0;

                _latest = new TelemetrySnapshot(
                    true,
                    sample.Telemetry.Gear,
                    Math.Max(0, sample.Telemetry.Speed * MetersPerSecondToMph),
                    GetRacingFlag(
                        sessionFlags, _suppressHeldBlackFlag, greenPulseVisible,
                        yellowPulseVisible, showCheckered, whitePulseVisible),
                    leftZone,
                    rightZone,
                    sample.Telemetry.ShiftIndicatorPct,
                    progressMode,
                    sample.Telemetry.SessionTimeRemain,
                    totalSeconds,
                    sample.Telemetry.LapCompleted,
                    totalLaps,
                    sample.Telemetry.PlayerCarPosition,
                    ReadTelemetryInt(sample.Telemetry, "PlayerCarMyIncidentCount"),
                    sample.Telemetry.FuelLevel,
                    sample.Telemetry.LapCurrentLapTime,
                    lapDelta,
                    driverAhead,
                    relativeAhead,
                    driverBehind,
                    relativeBehind,
                    GetTotalStarters(sample.Telemetry),
                    UtcNow < _lapSummaryUntilUtc,
                    _lastLapSeconds,
                    sample.Telemetry.LapBestLapTime,
                    _completedLapTimeCount > 0
                        ? _completedLapTimeTotal / _completedLapTimeCount
                        : 0,
                    _yellowCompletedLaps,
                    _lapColorVersion,
                    maxForwardGear,
                    sample.Telemetry.RPM);
                _latest.Discipline = GetDiscipline(sample.Telemetry);
                _latest.Brake = ReadTelemetryDouble(sample.Telemetry, "Brake");
                _latest.Throttle = ReadTelemetryDouble(sample.Telemetry, "Throttle");
                _latest.BrakeBias = ReadTelemetryDouble(sample.Telemetry, "dcBrakeBias");
                _latest.FuelLevelPct = ReadTelemetryDouble(sample.Telemetry, "FuelLevelPct");
                _latest.FuelLastLap = _lastLapFuel;
                _latest.FuelPerLapAverage = _fuelPerLap.Count > 0 ? Average(_fuelPerLap) : -1;
                _latest.FuelLapsMeasured = _fuelPerLap.Count;
                _latest.StintLaps = _stintLaps;
                _latest.RecentLapSeconds = _recentLaps.ToArray();
                _latest.StintLapSeconds = _stintGreenArray;

                CarDetails[] details = sample.Telemetry.CarDetails;
                _latest.AheadDetail = BuildRelative(details, aheadCar, driverAhead);
                _latest.BehindDetail = BuildRelative(details, behindCar, driverBehind);
                _latest.PlayerIRating = BuildRelative(details, ReadTelemetryInt(sample.Telemetry, "PlayerCarIdx"), null)?.IRating ?? 0;
                _latest.TractionControl = ReadOptionalDouble(sample.Telemetry, "dcTractionControl");
                _latest.Abs = ReadOptionalDouble(sample.Telemetry, "dcABS");
                double best = sample.Telemetry.LapBestLapTime;
                _latest.PredictedLapSeconds = best > 0 && sample.Telemetry.LapDeltaToBestLap_OK
                    ? best + sample.Telemetry.LapDeltaToBestLap
                    : 0;

                double speed = _latest.SpeedMph;
                if (sample.Telemetry.Lap != _speedLap)
                {
                    _speedLap = sample.Telemetry.Lap;
                    _lapMinSpeed = speed;
                    _lapMaxSpeed = speed;
                }
                _lapMinSpeed = Math.Min(_lapMinSpeed, speed);
                _lapMaxSpeed = Math.Max(_lapMaxSpeed, speed);
                _latest.LapMinSpeedMph = _lapMinSpeed;
                _latest.LapMaxSpeedMph = _lapMaxSpeed;

                if (UtcNow >= _nextTraceUtc)
                {
                    _nextTraceUtc = UtcNow.AddMilliseconds(TraceIntervalMilliseconds);
                    _throttleTrace[_traceHead] = (float)Math.Max(0, Math.Min(1, _latest.Throttle));
                    _brakeTrace[_traceHead] = (float)Math.Max(0, Math.Min(1, _latest.Brake));
                    _traceHead = (_traceHead + 1) % TraceLength;
                    _throttleTraceCopy = Unroll(_throttleTrace, _traceHead);
                    _brakeTraceCopy = Unroll(_brakeTrace, _traceHead);
                    _traceVersion++;
                }
                if (UtcNow >= _nextDeltaUtc)
                {
                    _nextDeltaUtc = UtcNow.AddMilliseconds(250);
                    _deltaTrace[_deltaHead] = (float)Math.Max(-2, Math.Min(2, lapDelta));
                    _deltaHead = (_deltaHead + 1) % DeltaTraceLength;
                    _deltaTraceCopy = Unroll(_deltaTrace, _deltaHead);
                }
                _latest.DeltaTrace = _deltaTraceCopy;
                _latest.Relatives = BuildRelatives(sample.Telemetry, details);
                _latest.ClassPosition = ReadTelemetryInt(sample.Telemetry, "PlayerCarClassPosition");
                _latest.CarName = GetCarName(details, ReadTelemetryInt(sample.Telemetry, "PlayerCarIdx"));
                _latest.CautionActive = yellowActive;
                _latest.WhiteFlagActive = (sessionFlags & SessionFlags.white) != 0;
                _latest.TrackLengthMiles = GetTrackLengthMiles(sample.Telemetry);
                if (_startPositionSession != sample.Telemetry.SessionUniqueID)
                {
                    _startPositionSession = sample.Telemetry.SessionUniqueID;
                    _startPosition = 0;
                }
                // Capture the grid slot as the field takes the green on lap 1.
                if (_startPosition == 0 && progressMode != SessionProgressMode.Practice &&
                    sample.Telemetry.PlayerCarPosition > 0 && sample.Telemetry.LapCompleted <= 0 && greenActive)
                    _startPosition = sample.Telemetry.PlayerCarPosition;
                _latest.StartPosition = _startPosition;
                _latest.OnPitRoad = sample.Telemetry.OnPitRoad;
                int warnings = ReadTelemetryInt(sample.Telemetry, "EngineWarnings");
                _latest.PitLimiter = (warnings & (int)EngineWarnings.PitSpeedLimiter) != 0;
                _latest.EngineWarning = (warnings & ((int)EngineWarnings.WaterTempWarning | (int)EngineWarnings.OilPressureWarning | (int)EngineWarnings.FuelPressureWarning)) != 0;
                _latest.AbsActive = sample.Telemetry["BrakeABSactive"] is bool abs && abs;
                _latest.OutOfCar = (sample.Telemetry["IsOnTrack"] is bool onTrack && !onTrack) ||
                    (sample.Telemetry["IsReplayPlaying"] is bool replay && replay);
                _latest.CarState = CarStateOf(sample.Telemetry["IsOnTrack"] is bool inCar && inCar, sample.Telemetry["IsInGarage"] is bool garage && garage,
                    sample.Telemetry["IsReplayPlaying"] is bool replaying && replaying,
                    sample.Telemetry["CamCarIdx"] == null ? -1 : ReadTelemetryInt(sample.Telemetry, "CamCarIdx"), ReadTelemetryInt(sample.Telemetry, "PlayerCarIdx"));
                SessionData session = sample.Telemetry.SessionData;
                if (session != null)
                {
                    _latest.ShiftFirstRpm = session.ShiftFirstRpm;
                    _latest.ShiftRpm = session.ShiftRpm;
                    _latest.ShiftLastRpm = session.ShiftLastRpm;
                    _latest.ShiftBlinkRpm = session.ShiftBlinkRpm;
                    _latest.RedlineRpm = session.RedlineRpm;
                }
                _latest.PitSpeedLimitMph = GetPitSpeedLimitMph(sample.Telemetry);
                _latest.TrackTempC = ReadOptionalDouble(sample.Telemetry, "TrackTempCrew") is double track && track > -50 ? track : double.NaN;
                _latest.AirTempC = ReadOptionalDouble(sample.Telemetry, "AirTemp") is double air && air > -50 ? air : double.NaN;
                _latest.ThrottleTrace = _throttleTraceCopy;
                _latest.BrakeTrace = _brakeTraceCopy;
                _latest.TraceVersion = _traceVersion;
                UpdateRaceData(sample.Telemetry, _latest, sessionFlags, _suppressHeldBlackFlag);
                UpdateRadar(sample.Telemetry, _latest, carLeftRight);
            }
        }

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
            _radar.Update(t.CarIdxLapDistPct, t.CarIdxOnPitRoad, ReadTelemetryInt(t, "PlayerCarIdx"), s.TrackLengthMiles * 1609.344, carLeftRight, time, car =>
            {
                if (byCar.TryGetValue(car.CarIdx, out StandingsEntry row))
                {
                    car.Number = row.CarNumber ?? "";
                    car.ClassColor = row.ClassColor;
                }
                car.PaceCar = pace.Contains(car.CarIdx);
            });
            s.Radar = _radar.Cars;
            s.AheadMeters = _radar.AheadMeters;
            s.BehindMeters = _radar.BehindMeters;
            s.AheadClosing = _radar.AheadClosing;
            s.BehindClosing = _radar.BehindClosing;
        }

        private double GetTrackLengthMiles(Telemetry telemetry)
        {
            if (_trackLengthSession == telemetry.SessionUniqueID) return _trackLengthMiles;
            _trackLengthSession = telemetry.SessionUniqueID;
            _trackLengthMiles = 0;
            try
            {
                // Reported as text such as "2.50 km".
                string text = telemetry.SessionData?.WeekendInfo?.TrackLength ?? "";
                string[] parts = text.Trim().Split(' ');
                if (double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value))
                    _trackLengthMiles = text.ToLowerInvariant().Contains("km") ? value / 1.609344 : value;
            }
            catch { }
            return _trackLengthMiles;
        }

        private static string GetCarName(CarDetails[] details, int playerIndex)
        {
            try
            {
                if (details == null) return null;
                foreach (CarDetails detail in details)
                    if (detail != null && detail.CarIdx == playerIndex && detail.Driver != null)
                        return (string.IsNullOrWhiteSpace(detail.Driver.CarScreenNameShort) ? detail.Driver.CarScreenName : detail.Driver.CarScreenNameShort)?.ToUpperInvariant();
            }
            catch { }
            return null;
        }

        private double GetPitSpeedLimitMph(Telemetry telemetry)
        {
            if (_pitLimitSession == telemetry.SessionUniqueID) return _pitLimitMph;
            _pitLimitSession = telemetry.SessionUniqueID;
            _pitLimitMph = 0;
            try
            {
                // Reported as text such as "72.42 kph".
                string text = telemetry.SessionData?.WeekendInfo?.TrackPitSpeedLimit ?? "";
                string[] parts = text.Trim().Split(' ');
                if (double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value))
                    _pitLimitMph = text.ToLowerInvariant().Contains("kph") ? value / 1.609344 : value;
            }
            catch { }
            return _pitLimitMph;
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
                            if (d.CarIdx == player && d.Driver != null && d.Driver.CarClassEstLapTime > 10) lapSeconds = d.Driver.CarClassEstLapTime;
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

        private static float[] Unroll(float[] ring, int head)
        {
            var result = new float[ring.Length];
            Array.Copy(ring, head, result, 0, ring.Length - head);
            Array.Copy(ring, 0, result, ring.Length - head, head);
            return result;
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

        private static double ReadOptionalDouble(Telemetry telemetry, string name)
        {
            try
            {
                object value = telemetry[name];
                return value == null ? -1 : Convert.ToDouble(value);
            }
            catch
            {
                return -1;
            }
        }

        private string GetDiscipline(Telemetry telemetry)
        {
            if (_discipline != null && _disciplineSessionId == telemetry.SessionUniqueID) return _discipline;
            try
            {
                var weekend = telemetry.SessionData?.WeekendInfo;
                _discipline = weekend == null ? null : Disciplines.FromIRacing(weekend.Category, weekend.TrackType);
                _disciplineSessionId = telemetry.SessionUniqueID;
            }
            catch
            {
                _discipline = null;
            }
            return _discipline;
        }

        private static double Average(System.Collections.Generic.List<double> values)
        {
            double total = 0;
            foreach (double value in values) total += value;
            return total / values.Count;
        }

        private void TrackFuelAndStint(Telemetry telemetry, bool lapCompleted)
        {
            double fuel = telemetry.FuelLevel;
            bool onPitRoad = telemetry.OnPitRoad;
            // Fuel rising mid-lap means a refuel; either way the lap is not representative.
            if (onPitRoad || (_fuelAtLapStart >= 0 && fuel > _fuelAtLapStart + 0.05))
            {
                _lapTouchedPit = true;
                _stintLaps = 0;
                if (_stintGreenLaps.Count > 0) SetStintLaps(clear: true);
            }
            if (_pendingLap && (Math.Abs(telemetry.LapLastLapTime - _pendingLapSeen) > 0.0005 || unchecked(TickNow - _pendingLapAt) > 1500))
            {
                _pendingLap = false;
                if (_pendingLapClean && telemetry.LapLastLapTime > 0)
                {
                    _stintGreenLaps.Add(telemetry.LapLastLapTime);
                    if (_stintGreenLaps.Count > StintLapsKept) _stintGreenLaps.RemoveAt(0);
                    SetStintLaps(clear: false);
                }
            }
            if (!lapCompleted) return;

            _pendingLap = true;
            _pendingLapClean = !_lapTouchedPit && !onPitRoad && !_currentLapHadYellow;
            _pendingLapSeen = telemetry.LapLastLapTime;
            _pendingLapAt = TickNow;

            // Only green-flag laps measure fuel use: a caution lap burns less, and counting it would make the
            // fuel to the finish come out short once racing resumes.
            if (!_lapTouchedPit && !_currentLapHadYellow && _fuelAtLapStart > 0)
            {
                double used = _fuelAtLapStart - fuel;
                if (used > 0.01)
                {
                    _lastLapFuel = used;
                    _fuelPerLap.Add(used);
                    if (_fuelPerLap.Count > FuelAverageLaps) _fuelPerLap.RemoveAt(0);
                }
            }
            if (telemetry.LapLastLapTime > 0)
            {
                _recentLaps.Add(telemetry.LapLastLapTime);
                if (_recentLaps.Count > 5) _recentLaps.RemoveAt(0);
            }
            if (!onPitRoad) _stintLaps++;
            _fuelAtLapStart = fuel;
            _lapTouchedPit = onPitRoad;
        }

        private void ResetFuelAndStint(Telemetry telemetry)
        {
            _fuelAtLapStart = telemetry.FuelLevel;
            _lapTouchedPit = telemetry.OnPitRoad;
            _lastLapFuel = -1;
            _fuelPerLap.Clear();
            _recentLaps.Clear();
            _stintLaps = 0;
            _pendingLap = false;
            SetStintLaps(clear: true);
        }

        private void SetStintLaps(bool clear)
        {
            if (clear) _stintGreenLaps.Clear();
            _stintGreenArray = _stintGreenLaps.ToArray();
        }

        private void UpdateLapSummary(Telemetry telemetry, bool yellowActive)
        {
            if (telemetry.SessionUniqueID != _lastSessionUniqueId)
            {
                _lastSessionUniqueId = telemetry.SessionUniqueID;
                _lastCompletedLap = telemetry.LapCompleted;
                _completedLapTimeTotal = 0;
                _completedLapTimeCount = 0;
                _lastLapSeconds = 0;
                _lapSummaryUntilUtc = DateTime.MinValue;
                Array.Clear(_yellowCompletedLaps, 0, _yellowCompletedLaps.Length);
                _currentLapHadYellow = yellowActive;
                _lapColorVersion++;
                _maximumObservedGear = 0;
                ResetFuelAndStint(telemetry);
                return;
            }

            if (_lastCompletedLap < 0)
            {
                _lastCompletedLap = telemetry.LapCompleted;
                ResetFuelAndStint(telemetry);
                return;
            }

            if (telemetry.LapCompleted > _lastCompletedLap)
            {
                _lastCompletedLap = telemetry.LapCompleted;
                TrackFuelAndStint(telemetry, true);
                int completedLapIndex = telemetry.LapCompleted - 1;
                if (_currentLapHadYellow && completedLapIndex >= 0 &&
                    completedLapIndex < _yellowCompletedLaps.Length)
                {
                    _yellowCompletedLaps[completedLapIndex] = true;
                    _lapColorVersion++;
                }
                _currentLapHadYellow = yellowActive;
                if (telemetry.LapLastLapTime > 0)
                {
                    _lastLapSeconds = telemetry.LapLastLapTime;
                    _completedLapTimeTotal += telemetry.LapLastLapTime;
                    _completedLapTimeCount++;
                }
                _lapSummaryUntilUtc = UtcNow.AddSeconds(4);
            }
            else if (telemetry.LapCompleted < _lastCompletedLap)
            {
                _lastCompletedLap = telemetry.LapCompleted;
                _completedLapTimeTotal = 0;
                _completedLapTimeCount = 0;
                _lastLapSeconds = 0;
                _lapSummaryUntilUtc = DateTime.MinValue;
                Array.Clear(_yellowCompletedLaps, 0, _yellowCompletedLaps.Length);
                _currentLapHadYellow = yellowActive;
                _lapColorVersion++;
                ResetFuelAndStint(telemetry);
            }
            else
            {
                if (yellowActive) _currentLapHadYellow = true;
                TrackFuelAndStint(telemetry, false);
            }
        }

        private static int GetTotalStarters(Telemetry telemetry)
        {
            try
            {
                var options = telemetry.SessionData?.WeekendInfo?.WeekendOptions;
                return options == null ? 0 : (int)options.NumStarters;
            }
            catch
            {
                return 0;
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

        private static void GetSessionProgress(
            Telemetry telemetry,
            out SessionProgressMode mode,
            out double totalSeconds,
            out int totalLaps)
        {
            mode = SessionProgressMode.None;
            totalSeconds = 0;
            totalLaps = 0;
            var session = telemetry.Session;
            if (session == null) return;

            if (!session.IsRace)
            {
                mode = SessionProgressMode.Practice;
                totalSeconds = session._SessionTime;
                return;
            }

            if (session.IsLimitedSessionLaps)
            {
                mode = SessionProgressMode.LapRace;
                totalLaps = session._SessionLaps;
                return;
            }

            if (session.IsLimitedTime)
            {
                mode = SessionProgressMode.TimedRace;
                totalSeconds = session._SessionTime;
            }
        }

        private static float[] ReadFloats(Telemetry telemetry, string name)
        {
            try { return telemetry[name] as float[]; }
            catch { return null; }
        }

        /// <summary>
        /// Where the driver is, from iRacing's IsOnTrack (in the car with its physics running), IsInGarage (the
        /// garage screen), IsReplayPlaying and CamCarIdx (the car the camera follows).
        /// </summary>
        internal static CarState CarStateOf(bool onTrack, bool inGarage, bool replay, int cameraCar, int playerCar)
        {
            if (inGarage) return CarState.Garage;
            if (onTrack && !replay) return CarState.InCar;
            return cameraCar >= 0 && cameraCar != playerCar ? CarState.Spectating : CarState.OutOfCar;
        }

        private static int ReadTelemetryInt(Telemetry telemetry, string name)
        {
            try
            {
                object value = telemetry[name];
                return value == null ? 0 : Convert.ToInt32(value);
            }
            catch
            {
                return 0;
            }
        }

        private static double ReadTelemetryDouble(Telemetry telemetry, string name)
        {
            try
            {
                object value = telemetry[name];
                double result = value == null ? 0 : Convert.ToDouble(value);
                return double.IsNaN(result) || double.IsInfinity(result) ? 0 : result;
            }
            catch
            {
                return 0;
            }
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

        private static RacingFlag GetRacingFlag(
            SessionFlags flags,
            bool suppressBlackFlag,
            bool greenPulseVisible,
            bool yellowPulseVisible,
            bool showCheckered,
            bool whitePulseVisible)
        {
            if ((flags & SessionFlags.red) != 0) return RacingFlag.Red;
            if (!suppressBlackFlag &&
                ((flags & SessionFlags.disqualify) != 0 || (flags & SessionFlags.black) != 0))
                return RacingFlag.Black;
            if ((flags & SessionFlags.repair) != 0) return RacingFlag.Meatball;
            if (showCheckered) return RacingFlag.Checkered;
            if ((flags & SessionFlags.debris) != 0) return RacingFlag.Surface;
            if ((flags & SessionFlags.blue) != 0) return RacingFlag.BlueYellowStripe;
            // A caution can last for several pace laps. Alert on its rising edge,
            // then restore the useful dashboard while the raw state remains held.
            if (yellowPulseVisible) return RacingFlag.Yellow;
            if (whitePulseVisible) return RacingFlag.White;
            // greenHeld is asserted while the starter is still holding the field.
            // Only the rising edge of the real green bit triggers the GO alert.
            if (greenPulseVisible) return RacingFlag.Green;
            return RacingFlag.None;
        }

        public void Dispose()
        {
            if (_listening)
            {
                iRacing.NewData -= OnNewData;
                iRacing.StopListening();
                _listening = false;
            }
        }
    }
}
