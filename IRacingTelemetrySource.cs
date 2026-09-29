using System;
using System.Threading;
using iRacingSDK;

namespace DV5Direct
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
        public int ClassPosition { get; set; }
        public bool OnPitRoad { get; set; }
        public bool PitLimiter { get; set; }
        public double PitSpeedLimitMph { get; set; }
        public double TrackTempC { get; set; } = double.NaN;
        public double AirTempC { get; set; } = double.NaN;
        public bool EngineWarning { get; set; }
        /// <summary>Lap delta history (seconds, positive = faster), oldest first, sampled every 250 ms.</summary>
        public float[] DeltaTrace { get; set; } = new float[0];

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

        /// <summary>Fuel to add to reach the finish in liters, or -1 when unknown.</summary>
        public double FuelToFinish
        {
            get
            {
                double laps = LapsRemaining;
                if (laps < 0 || FuelPerLapAverage <= 0) return -1;
                return Math.Max(0, laps * FuelPerLapAverage - FuelLiters);
            }
        }
    }

    internal sealed class IRacingTelemetrySource : IDisposable
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
        private TelemetrySnapshot _latest = TelemetrySnapshot.Disconnected;
        private bool _listening;
        private Thread _demoThread;
        private volatile bool _stopDemo;
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

        public void StartDemoSequence()
        {
            if (_demoThread != null) return;
            _stopDemo = false;
            _demoThread = new Thread(RunDemoSequence)
            {
                IsBackground = true,
                Name = "DV-5 demo sequence"
            };
            _demoThread.Start();
        }

        public void StartDashboardPreview()
        {
            SetSnapshot(4, 136, RacingFlag.None, BlindSpotZone.None, BlindSpotZone.None, 0.37);
        }

        public void StartTimedDashboardPreview()
        {
            lock (_sync)
            {
                _latest = WithPreviewExtras(new TelemetrySnapshot(
                    true, 4, 136, RacingFlag.None,
                    BlindSpotZone.None, BlindSpotZone.None, 0,
                    SessionProgressMode.TimedRace, 1470, 3600, 8, 0,
                    4, 4, 24.3, 88.104, 0.46,
                    "J. MILLER", -0.46, "M. CARTER", 0.82, 22,
                    false, 87.932, 87.521, 88.204, new bool[0], 0, 6, 6120));
            }
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

        private static TelemetrySnapshot WithPreviewExtras(TelemetrySnapshot snapshot)
        {
            snapshot.Discipline = Disciplines.Road;
            snapshot.BrakeBias = 54.2;
            snapshot.FuelLevelPct = 0.41;
            snapshot.FuelLastLap = 2.41;
            snapshot.FuelPerLapAverage = 2.46;
            snapshot.StintLaps = 8;
            snapshot.RecentLapSeconds = new[] { 88.412, 87.932, 88.105, 87.521, 87.932 };
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
            return snapshot;
        }

        public TelemetrySnapshot GetSnapshot()
        {
            lock (_sync)
            {
                return _latest;
            }
        }

        private void OnNewData(DataSample sample)
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
                    _greenAlertUntilUtc = DateTime.UtcNow.AddMilliseconds(
                        _alertBlinkTotalMilliseconds + _alertHoldMilliseconds);
                _greenWasActive = greenActive;
                double greenRemaining = (_greenAlertUntilUtc - DateTime.UtcNow).TotalMilliseconds;
                bool greenPulseVisible = greenRemaining > 0 &&
                    (greenRemaining <= _alertHoldMilliseconds ||
                     ((Environment.TickCount / _alertBlinkMilliseconds) & 1) == 0);

                bool yellowActive =
                    (sessionFlags & SessionFlags.caution) != 0 ||
                    (sessionFlags & SessionFlags.cautionWaving) != 0 ||
                    (sessionFlags & SessionFlags.yellow) != 0 ||
                    (sessionFlags & SessionFlags.yellowWaving) != 0;
                if (yellowActive && !_yellowWasActive)
                    _yellowAlertUntilUtc = DateTime.UtcNow.AddMilliseconds(
                        _alertBlinkTotalMilliseconds + _alertHoldMilliseconds);
                _yellowWasActive = yellowActive;
                double yellowRemaining = (_yellowAlertUntilUtc - DateTime.UtcNow).TotalMilliseconds;
                bool yellowPulseVisible = yellowRemaining > 0 &&
                    (yellowRemaining <= _alertHoldMilliseconds ||
                     ((Environment.TickCount / _alertBlinkMilliseconds) & 1) == 0);

                bool checkeredActive = (sessionFlags & SessionFlags.checkered) != 0;
                if (checkeredActive && !_checkeredWasActive)
                    _checkeredAlertUntilUtc = DateTime.UtcNow.AddSeconds(10);
                _checkeredWasActive = checkeredActive;
                bool showCheckered = DateTime.UtcNow < _checkeredAlertUntilUtc;

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
                        yellowPulseVisible, showCheckered),
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
                    DateTime.UtcNow < _lapSummaryUntilUtc,
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
                _latest.StintLaps = _stintLaps;
                _latest.RecentLapSeconds = _recentLaps.ToArray();

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

                if (DateTime.UtcNow >= _nextTraceUtc)
                {
                    _nextTraceUtc = DateTime.UtcNow.AddMilliseconds(TraceIntervalMilliseconds);
                    _throttleTrace[_traceHead] = (float)Math.Max(0, Math.Min(1, _latest.Throttle));
                    _brakeTrace[_traceHead] = (float)Math.Max(0, Math.Min(1, _latest.Brake));
                    _traceHead = (_traceHead + 1) % TraceLength;
                    _throttleTraceCopy = Unroll(_throttleTrace, _traceHead);
                    _brakeTraceCopy = Unroll(_brakeTrace, _traceHead);
                    _traceVersion++;
                }
                if (DateTime.UtcNow >= _nextDeltaUtc)
                {
                    _nextDeltaUtc = DateTime.UtcNow.AddMilliseconds(250);
                    _deltaTrace[_deltaHead] = (float)Math.Max(-2, Math.Min(2, lapDelta));
                    _deltaHead = (_deltaHead + 1) % DeltaTraceLength;
                    _deltaTraceCopy = Unroll(_deltaTrace, _deltaHead);
                }
                _latest.DeltaTrace = _deltaTraceCopy;
                _latest.Relatives = BuildRelatives(sample.Telemetry, details);
                _latest.ClassPosition = ReadTelemetryInt(sample.Telemetry, "PlayerCarClassPosition");
                _latest.CarName = GetCarName(details, ReadTelemetryInt(sample.Telemetry, "PlayerCarIdx"));
                _latest.OnPitRoad = sample.Telemetry.OnPitRoad;
                int warnings = ReadTelemetryInt(sample.Telemetry, "EngineWarnings");
                _latest.PitLimiter = (warnings & (int)EngineWarnings.PitSpeedLimiter) != 0;
                _latest.EngineWarning = (warnings & ((int)EngineWarnings.WaterTempWarning | (int)EngineWarnings.OilPressureWarning | (int)EngineWarnings.FuelPressureWarning)) != 0;
                _latest.PitSpeedLimitMph = GetPitSpeedLimitMph(sample.Telemetry);
                _latest.TrackTempC = ReadOptionalDouble(sample.Telemetry, "TrackTempCrew") is double track && track > -50 ? track : double.NaN;
                _latest.AirTempC = ReadOptionalDouble(sample.Telemetry, "AirTemp") is double air && air > -50 ? air : double.NaN;
                _latest.ThrottleTrace = _throttleTraceCopy;
                _latest.BrakeTrace = _brakeTraceCopy;
                _latest.TraceVersion = _traceVersion;
            }
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
                        Gap = -delta * lapSeconds,
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
            }
            if (!lapCompleted) return;

            if (!_lapTouchedPit && _fuelAtLapStart > 0)
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
        }

        private void RunDemoSequence()
        {
            int[] gears = { -1, 0, 1, 2, 3, 4, 5, 6 };
            int[] targetSpeeds = { 0, 0, 18, 38, 61, 86, 112, 140 };

            while (!_stopDemo)
            {
                double speed = 0;
                for (int index = 0; index < gears.Length && !_stopDemo; index++)
                {
                    double startSpeed = speed;
                    double targetSpeed = targetSpeeds[index];
                    const int frames = 8;
                    for (int frame = 1; frame <= frames && !_stopDemo; frame++)
                    {
                        speed = startSpeed + ((targetSpeed - startSpeed) * frame / frames);
                        SetSnapshot(gears[index], speed, RacingFlag.None);
                        Thread.Sleep(100);
                    }
                }

                RacingFlag[] flags =
                {
                    RacingFlag.Green,
                    RacingFlag.White,
                    RacingFlag.Yellow,
                    RacingFlag.Red,
                    RacingFlag.Black,
                    RacingFlag.Meatball,
                    RacingFlag.BlueYellowStripe,
                    RacingFlag.SafetyCar,
                    RacingFlag.Surface,
                    RacingFlag.Checkered
                };

                foreach (RacingFlag flag in flags)
                {
                    int flashCount = flag == RacingFlag.Yellow ? 5 : 3;
                    for (int flash = 0; flash < flashCount && !_stopDemo; flash++)
                    {
                        SetSnapshot(6, speed, flag);
                        Thread.Sleep(220);
                        SetSnapshot(6, speed, RacingFlag.None);
                        Thread.Sleep(160);
                    }
                    SleepWithCancellation(180);
                }

                BlindSpotZone[] radarSweep =
                {
                    BlindSpotZone.Front,
                    BlindSpotZone.Beside,
                    BlindSpotZone.Rear,
                    BlindSpotZone.None
                };
                foreach (BlindSpotZone zone in radarSweep)
                {
                    SetSnapshot(6, speed, RacingFlag.None, zone, BlindSpotZone.None);
                    SleepWithCancellation(550);
                }
                foreach (BlindSpotZone zone in radarSweep)
                {
                    SetSnapshot(6, speed, RacingFlag.None, BlindSpotZone.None, zone);
                    SleepWithCancellation(550);
                }

                for (int rpmStep = 0; rpmStep <= 4 && !_stopDemo; rpmStep++)
                {
                    SetSnapshot(
                        6, speed, RacingFlag.None,
                        BlindSpotZone.None, BlindSpotZone.None,
                        rpmStep / 4.0);
                    SleepWithCancellation(rpmStep == 4 ? 1200 : 450);
                }
                SetSnapshot(6, speed, RacingFlag.None);

                SleepWithCancellation(900);
            }
        }

        private void SetSnapshot(int gear, double speedMph, RacingFlag flag)
        {
            SetSnapshot(
                gear, speedMph, flag, BlindSpotZone.None, BlindSpotZone.None);
        }

        private void SetSnapshot(
            int gear,
            double speedMph,
            RacingFlag flag,
            BlindSpotZone leftBlindSpot,
            BlindSpotZone rightBlindSpot,
            double shiftIndicatorPct = 0)
        {
            lock (_sync)
            {
                _latest = new TelemetrySnapshot(
                    true, gear, speedMph, flag, leftBlindSpot, rightBlindSpot, shiftIndicatorPct,
                    SessionProgressMode.LapRace, 0, 0, 8, 18,
                    4, 4, 24.3, 88.104, 0.46,
                    "J. MILLER", -0.46, "M. CARTER", 0.82, 22,
                    false, 87.932, 87.521, 88.204, new bool[18], 0, 6,
                    shiftIndicatorPct * 8000);
                WithPreviewExtras(_latest);
            }
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
                _lapSummaryUntilUtc = DateTime.UtcNow.AddSeconds(4);
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

            if (aheadIndex >= 0)
            {
                driverAhead = GetDriverName(details, aheadIndex);
                relativeAhead = -(closestAhead * estimatedLapSeconds);
            }
            if (behindIndex >= 0)
            {
                driverBehind = GetDriverName(details, behindIndex);
                relativeBehind = -(closestBehind * estimatedLapSeconds);
            }
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

        private static BlindSpotZone UpdateBlindSpotZone(
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
                clearedAtUtc = DateTime.UtcNow;

            if (clearedAtUtc != DateTime.MinValue &&
                (DateTime.UtcNow - clearedAtUtc).TotalMilliseconds < RearHoldMilliseconds)
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
            bool showCheckered)
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
            if ((flags & SessionFlags.white) != 0) return RacingFlag.White;
            // greenHeld is asserted while the starter is still holding the field.
            // Only the rising edge of the real green bit triggers the GO alert.
            if (greenPulseVisible) return RacingFlag.Green;
            return RacingFlag.None;
        }

        private void SleepWithCancellation(int milliseconds)
        {
            int remaining = milliseconds;
            while (remaining > 0 && !_stopDemo)
            {
                int slice = Math.Min(remaining, 50);
                Thread.Sleep(slice);
                remaining -= slice;
            }
        }

        public void Dispose()
        {
            _stopDemo = true;
            if (_demoThread != null)
            {
                _demoThread.Join(1000);
                _demoThread = null;
            }

            if (_listening)
            {
                iRacing.NewData -= OnNewData;
                iRacing.StopListening();
                _listening = false;
            }
        }
    }
}
