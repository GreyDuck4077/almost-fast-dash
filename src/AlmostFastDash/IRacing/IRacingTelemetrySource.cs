using System;

namespace AlmostFastDash.IRacing
{
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

        public TelemetrySnapshot GetSnapshot()
        {
            lock (_sync)
            {
                return _latest;
            }
        }

        /// <summary>Turns one sample of iRacing's data into the snapshot every dashboard and light reads.</summary>
        internal void OnNewData(DataSample sample)
        {
            if (sample == null || !sample.IsConnected || sample.Telemetry == null)
            {
                lock (_sync) _latest = TelemetrySnapshot.Disconnected;
                return;
            }

            lock (_sync)
            {
                Telemetry telemetry = sample.Telemetry;
                SessionFlags sessionFlags = telemetry.SessionFlags;
                RacingFlag flag = UpdateFlagAlerts(telemetry, sessionFlags, out bool greenActive, out bool yellowActive);
                int carLeftRight = UpdateBlindSpots(telemetry, out BlindSpotZone leftZone, out BlindSpotZone rightZone);

                GetSessionProgress(telemetry, out SessionProgressMode progressMode, out double totalSeconds, out int totalLaps);
                GetNearestRelatives(
                    telemetry,
                    out string driverAhead,
                    out double relativeAhead,
                    out string driverBehind,
                    out double relativeBehind,
                    out int aheadCar,
                    out int behindCar);

                UpdateLapSummary(telemetry, yellowActive);
                int reportedMaxGear = ReadTelemetryInt(telemetry, "DriverCarGearNumForward");
                _maximumObservedGear = Math.Max(_maximumObservedGear, telemetry.Gear);
                int maxForwardGear = reportedMaxGear > 0 ? reportedMaxGear : _maximumObservedGear;

                double lapDelta = telemetry.LapDeltaToSessionBestLap_OK
                    ? -telemetry.LapDeltaToSessionBestLap
                    : 0;

                _latest = new TelemetrySnapshot(
                    true,
                    telemetry.Gear,
                    Math.Max(0, telemetry.Speed * MetersPerSecondToMph),
                    flag,
                    leftZone,
                    rightZone,
                    telemetry.ShiftIndicatorPct,
                    progressMode,
                    telemetry.SessionTimeRemain,
                    totalSeconds,
                    telemetry.LapCompleted,
                    totalLaps,
                    telemetry.PlayerCarPosition,
                    ReadTelemetryInt(telemetry, "PlayerCarMyIncidentCount"),
                    telemetry.FuelLevel,
                    telemetry.LapCurrentLapTime,
                    lapDelta,
                    driverAhead,
                    relativeAhead,
                    driverBehind,
                    relativeBehind,
                    GetTotalStarters(telemetry),
                    UtcNow < _lapSummaryUntilUtc,
                    _lastLapSeconds,
                    telemetry.LapBestLapTime,
                    _completedLapTimeCount > 0
                        ? _completedLapTimeTotal / _completedLapTimeCount
                        : 0,
                    _yellowCompletedLaps,
                    _lapColorVersion,
                    maxForwardGear,
                    telemetry.RPM);
                AddDriverAndCar(telemetry, aheadCar, driverAhead, behindCar, driverBehind);
                UpdateSpeedRange(telemetry);
                UpdateTraces(lapDelta);
                AddSessionDetails(telemetry, sessionFlags, progressMode, greenActive, yellowActive);
                UpdateRaceData(telemetry, _latest, sessionFlags, _suppressHeldBlackFlag);
                UpdateRadar(telemetry, _latest, carLeftRight);
            }
        }

        /// <summary>
        /// Follows green, yellow, white and checkered flags so each alerts once, as it comes out, and a black
        /// flag iRacing keeps raised after a tow is let go. Returns the flag the dashboards show right now.
        /// </summary>
        private RacingFlag UpdateFlagAlerts(Telemetry telemetry, SessionFlags sessionFlags, out bool greenActive, out bool yellowActive)
        {
            greenActive = (sessionFlags & SessionFlags.green) != 0;
            if (greenActive && !_greenWasActive)
                _greenAlertUntilUtc = UtcNow.AddMilliseconds(
                    _alertBlinkTotalMilliseconds + _alertHoldMilliseconds);
            _greenWasActive = greenActive;
            double greenRemaining = (_greenAlertUntilUtc - UtcNow).TotalMilliseconds;
            bool greenPulseVisible = greenRemaining > 0 &&
                (greenRemaining <= _alertHoldMilliseconds ||
                 ((TickNow / _alertBlinkMilliseconds) & 1) == 0);

            yellowActive =
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
            if (checkeredActive && !_checkeredWasActive && telemetry.SessionUniqueID != _checkeredSession)
            {
                _checkeredSession = telemetry.SessionUniqueID;
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
                _lastEnterExitReset = telemetry.EnterExitReset;
                _hasResetCounter = true;
            }
            else if (telemetry.EnterExitReset != _lastEnterExitReset)
            {
                // iRacing may keep its session-level black bit asserted after a
                // tow/reset. Clear the driver alert immediately and ignore that
                // held bit until iRacing drops it, which rearms the next penalty.
                _lastEnterExitReset = telemetry.EnterExitReset;
                _suppressHeldBlackFlag = true;
            }

            if (!blackFlagActive)
                _suppressHeldBlackFlag = false;

            return GetRacingFlag(
                sessionFlags, _suppressHeldBlackFlag, greenPulseVisible,
                yellowPulseVisible, showCheckered, whitePulseVisible);
        }

        /// <summary>Which side a car is on, from iRacing's spotter, and how close. Returns the spotter's raw value.</summary>
        private int UpdateBlindSpots(Telemetry telemetry, out BlindSpotZone leftZone, out BlindSpotZone rightZone)
        {
            int carLeftRight = ReadTelemetryInt(telemetry, "CarLeftRight");
            bool carOnLeft = carLeftRight == 2 || carLeftRight == 4 || carLeftRight == 5;
            bool carOnRight = carLeftRight == 3 || carLeftRight == 4 || carLeftRight == 6;
            double? nearestDelta;
            BlindSpotZone nearestZone = GetNearestOpponentZone(telemetry, out nearestDelta);
            leftZone = UpdateBlindSpotZone(
                carOnLeft, nearestZone, ref _lastLeftBlindSpot, ref _leftClearedAtUtc);
            rightZone = UpdateBlindSpotZone(
                carOnRight, nearestZone, ref _lastRightBlindSpot, ref _rightClearedAtUtc);
            _diagnostics?.RecordRadar(carLeftRight, nearestDelta, leftZone, rightZone);
            return carLeftRight;
        }

        /// <summary>Pedals, fuel, the stint, the cars either side of you and the car's own settings.</summary>
        private void AddDriverAndCar(Telemetry telemetry, int aheadCar, string driverAhead, int behindCar, string driverBehind)
        {
            _latest.Discipline = GetDiscipline(telemetry);
            _latest.Brake = ReadTelemetryDouble(telemetry, "Brake");
            _latest.Throttle = ReadTelemetryDouble(telemetry, "Throttle");
            _latest.BrakeBias = ReadTelemetryDouble(telemetry, "dcBrakeBias");
            _latest.FuelLevelPct = ReadTelemetryDouble(telemetry, "FuelLevelPct");
            _latest.FuelLastLap = _lastLapFuel;
            _latest.FuelPerLapAverage = _fuelPerLap.Count > 0 ? Average(_fuelPerLap) : -1;
            _latest.FuelLapsMeasured = _fuelPerLap.Count;
            _latest.StintLaps = _stintLaps;
            _latest.RecentLapSeconds = _recentLaps.ToArray();
            _latest.StintLapSeconds = _stintGreenArray;

            CarDetails[] details = telemetry.CarDetails;
            _latest.AheadDetail = BuildRelative(details, aheadCar, driverAhead);
            _latest.BehindDetail = BuildRelative(details, behindCar, driverBehind);
            _latest.PlayerIRating = BuildRelative(details, ReadTelemetryInt(telemetry, "PlayerCarIdx"), null)?.IRating ?? 0;
            _latest.TractionControl = ReadOptionalDouble(telemetry, "dcTractionControl");
            _latest.Abs = ReadOptionalDouble(telemetry, "dcABS");
            double best = telemetry.LapBestLapTime;
            _latest.PredictedLapSeconds = best > 0 && telemetry.LapDeltaToBestLap_OK
                ? best + telemetry.LapDeltaToBestLap
                : 0;
        }

        /// <summary>The slowest and fastest speed so far this lap.</summary>
        private void UpdateSpeedRange(Telemetry telemetry)
        {
            double speed = _latest.SpeedMph;
            if (telemetry.Lap != _speedLap)
            {
                _speedLap = telemetry.Lap;
                _lapMinSpeed = speed;
                _lapMaxSpeed = speed;
            }
            _lapMinSpeed = Math.Min(_lapMinSpeed, speed);
            _lapMaxSpeed = Math.Max(_lapMaxSpeed, speed);
            _latest.LapMinSpeedMph = _lapMinSpeed;
            _latest.LapMaxSpeedMph = _lapMaxSpeed;
        }

        /// <summary>The pedal and delta histories behind the trace widgets, sampled on a steady clock.</summary>
        private void UpdateTraces(double lapDelta)
        {
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
            _latest.ThrottleTrace = _throttleTraceCopy;
            _latest.BrakeTrace = _brakeTraceCopy;
            _latest.TraceVersion = _traceVersion;
        }

        /// <summary>The track, the session, where you are in it, the car's warning lights and its shift points.</summary>
        private void AddSessionDetails(Telemetry telemetry, SessionFlags sessionFlags, SessionProgressMode progressMode, bool greenActive,
            bool yellowActive)
        {
            CarDetails[] details = telemetry.CarDetails;
            _latest.Relatives = BuildRelatives(telemetry, details);
            _latest.ClassPosition = ReadTelemetryInt(telemetry, "PlayerCarClassPosition");
            _latest.CarName = GetCarName(details, ReadTelemetryInt(telemetry, "PlayerCarIdx"));
            _latest.CautionActive = yellowActive;
            _latest.WhiteFlagActive = (sessionFlags & SessionFlags.white) != 0;
            _latest.TrackLengthMiles = GetTrackLengthMiles(telemetry);
            if (_startPositionSession != telemetry.SessionUniqueID)
            {
                _startPositionSession = telemetry.SessionUniqueID;
                _startPosition = 0;
            }
            // Capture the grid slot as the field takes the green on lap 1.
            if (_startPosition == 0 && progressMode != SessionProgressMode.Practice &&
                telemetry.PlayerCarPosition > 0 && telemetry.LapCompleted <= 0 && greenActive)
                _startPosition = telemetry.PlayerCarPosition;
            _latest.StartPosition = _startPosition;
            _latest.OnPitRoad = telemetry.OnPitRoad;
            int warnings = ReadTelemetryInt(telemetry, "EngineWarnings");
            _latest.PitLimiter = (warnings & (int)EngineWarnings.PitSpeedLimiter) != 0;
            _latest.EngineWarning = (warnings & ((int)EngineWarnings.WaterTempWarning | (int)EngineWarnings.OilPressureWarning | (int)EngineWarnings.FuelPressureWarning)) != 0;
            _latest.AbsActive = telemetry["BrakeABSactive"] is bool abs && abs;
            _latest.OutOfCar = (telemetry["IsOnTrack"] is bool onTrack && !onTrack) ||
                (telemetry["IsReplayPlaying"] is bool replay && replay);
            _latest.CarState = CarStateOf(telemetry["IsOnTrack"] is bool inCar && inCar, telemetry["IsInGarage"] is bool garage && garage,
                telemetry["IsReplayPlaying"] is bool replaying && replaying,
                telemetry["CamCarIdx"] == null ? -1 : ReadTelemetryInt(telemetry, "CamCarIdx"),
                ReadTelemetryInt(telemetry, "PlayerCarIdx"));
            SessionData session = telemetry.SessionData;
            if (session != null)
            {
                _latest.ShiftFirstRpm = session.ShiftFirstRpm;
                _latest.ShiftRpm = session.ShiftRpm;
                _latest.ShiftLastRpm = session.ShiftLastRpm;
                _latest.ShiftBlinkRpm = session.ShiftBlinkRpm;
                _latest.RedlineRpm = session.RedlineRpm;
            }
            _latest.PitSpeedLimitMph = GetPitSpeedLimitMph(telemetry);
            _latest.TrackTempC = ReadOptionalDouble(telemetry, "TrackTempCrew") is double track && track > -50 ? track : double.NaN;
            _latest.AirTempC = ReadOptionalDouble(telemetry, "AirTemp") is double air && air > -50 ? air : double.NaN;
        }

        private static float[] Unroll(float[] ring, int head)
        {
            var result = new float[ring.Length];
            Array.Copy(ring, head, result, 0, ring.Length - head);
            Array.Copy(ring, 0, result, ring.Length - head, head);
            return result;
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
