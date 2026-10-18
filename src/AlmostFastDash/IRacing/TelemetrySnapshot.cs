using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

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

    /// <summary>
    /// One tyre. iRacing reports tyre temperatures, wear and pressures from the pit crew, so they
    /// update when the car stops in the pits rather than live. Temperatures are °C, pressure kPa,
    /// wear the share of tread left (1 is new); NaN when unknown.
    /// </summary>
    internal sealed class TyreCorner
    {
        public string Name;
        public double TempInner = double.NaN, TempMiddle = double.NaN, TempOuter = double.NaN;
        public double WearInner = double.NaN, WearMiddle = double.NaN, WearOuter = double.NaN;
        public double PressureKpa = double.NaN;

        public double Temp => Average(TempInner, TempMiddle, TempOuter);
        public double Wear => Average(WearInner, WearMiddle, WearOuter);

        private static double Average(params double[] values)
        {
            double[] known = values.Where(v => !double.IsNaN(v)).ToArray();
            return known.Length == 0 ? double.NaN : known.Average();
        }
    }

    /// <summary>One car on the leaderboard.</summary>
    internal sealed class StandingsEntry
    {
        public int Position, ClassPosition, CarIdx, Laps, LapsBehind, IRating;
        public string Name, CarNumber, CarName, ClassName, License;
        public Color ClassColor = Color.Gray, LicenseColor = Color.Gray;
        public double LastLap, BestLap;
        /// <summary>Seconds behind the leader, behind the car one place ahead, and to you (negative when ahead of you).</summary>
        public double GapToLeader, Interval, DeltaToPlayer;
        public bool IsPlayer, InPits;
    }
}
