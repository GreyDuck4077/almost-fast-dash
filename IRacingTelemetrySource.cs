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
    }

    internal sealed class IRacingTelemetrySource : IDisposable
    {
        private const double MetersPerSecondToMph = 2.2369362920544;
        private readonly object _sync = new object();
        private readonly DiagnosticLogger _diagnostics;
        private readonly int _alertBlinkMilliseconds;
        private readonly int _alertBlinkTotalMilliseconds;
        private readonly int _alertHoldMilliseconds;
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

        private const double AlongsideDistancePct = 0.0008;
        private const int RearHoldMilliseconds = 750;

        public IRacingTelemetrySource(DiagnosticLogger diagnostics, DashboardSettings dashboardSettings)
        {
            _diagnostics = diagnostics;
            AlertSettings alerts = dashboardSettings?.Alerts ?? new AlertSettings();
            _alertBlinkMilliseconds = Math.Max(50, alerts.BlinkMilliseconds);
            _alertBlinkTotalMilliseconds = Math.Max(1, alerts.BlinkCount) * _alertBlinkMilliseconds * 2;
            _alertHoldMilliseconds = Math.Max(0, alerts.HoldMilliseconds);
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
                _latest = new TelemetrySnapshot(
                    true, 4, 136, RacingFlag.None,
                    BlindSpotZone.None, BlindSpotZone.None, 0,
                    SessionProgressMode.TimedRace, 1470, 3600, 8, 0,
                    4, 4, 24.3, 88.104, 0.46,
                    "J. MILLER", -0.46, "M. CARTER", 0.82, 22,
                    false, 87.932, 87.521, 88.204, new bool[0], 0, 6);
            }
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
                    out relativeBehind);

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
            }
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
                return;
            }

            if (_lastCompletedLap < 0)
            {
                _lastCompletedLap = telemetry.LapCompleted;
                return;
            }

            if (telemetry.LapCompleted > _lastCompletedLap)
            {
                _lastCompletedLap = telemetry.LapCompleted;
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
            }
            else if (yellowActive)
            {
                _currentLapHadYellow = true;
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
            out double relativeBehind)
        {
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
            int aheadIndex = -1;
            int behindIndex = -1;
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
