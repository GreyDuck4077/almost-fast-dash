using System;
using System.Collections.Generic;
using System.Linq;

namespace AlmostFastOverlays
{
    internal sealed class DataField
    {
        public string Name, Category, Description;
        /// <summary>A formula that shows the field nicely, offered by the designer's data picker.</summary>
        public string Suggested;
        public Func<EvalScope, object> Get;
    }

    /// <summary>
    /// Every live value a designed dashboard can use. Speeds, fuel and temperatures
    /// are already in the units chosen under Device &amp; app.
    /// </summary>
    internal static class DataCatalog
    {
        public static readonly List<DataField> All = new List<DataField>();
        private static readonly Dictionary<string, DataField> ByName = new Dictionary<string, DataField>(StringComparer.OrdinalIgnoreCase);

        public static readonly string[] Categories = { "Car", "Timing", "Session", "Fuel", "Position", "Nearby cars", "Flags", "Pit", "Conditions" };

        static DataCatalog()
        {
            // ---------------- Car
            Add("Car", "Speed", "Speed in your chosen unit (MPH or km/h).", s => s.T.Connected ? s.Units.Speed(s.T.SpeedMph) : 0, "format(Speed, \"0\")");
            Add("Car", "SpeedUnit", "\"MPH\" or \"KM/H\".", s => s.Units.SpeedLabel);
            Add("Car", "SpeedMph", "Speed in miles per hour.", s => s.T.SpeedMph);
            Add("Car", "SpeedKph", "Speed in kilometres per hour.", s => s.T.SpeedMph * 1.609344);
            Add("Car", "Gear", "Gear number: -1 reverse, 0 neutral.", s => (double)s.T.Gear, "gear(Gear)");
            Add("Car", "GearText", "Gear as shown on a wheel: R, N, 1, 2...", s => s.T.Connected ? Fmt.Gear(s.T.Gear) : "-");
            Add("Car", "MaxGear", "Number of forward gears, once known.", s => (double)s.T.MaxForwardGear);
            Add("Car", "Rpm", "Engine RPM.", s => s.T.EngineRpm, "format(Rpm, \"0\")");
            Add("Car", "ShiftPct", "Shift light progress, 0 to 1. 1 means shift now.", s => s.T.ShiftIndicatorPct);
            Add("Car", "ShiftAlert", "True at the shift point.", s => s.T.ShiftAlert);
            Add("Car", "ShiftFlash", "Flashes on and off at the shift point, in step with the LEDs.", s => s.ShiftFlashOn);
            Add("Car", "RevPct", "Rev-light progress from the car's own shift-light RPMs, 0 to 1 (falls back to ShiftPct).", s => RevFraction(s.T));
            Add("Car", "ShiftFirstRpm", "RPM where the car's first shift light comes on, 0 if unknown.", s => s.T.ShiftFirstRpm);
            Add("Car", "ShiftRpm", "The car's shift point RPM, 0 if unknown.", s => s.T.ShiftRpm);
            Add("Car", "ShiftLastRpm", "RPM where the car's last shift light comes on, 0 if unknown.", s => s.T.ShiftLastRpm);
            Add("Car", "ShiftBlinkRpm", "RPM where the car's shift lights blink, 0 if unknown.", s => s.T.ShiftBlinkRpm);
            Add("Car", "Redline", "The car's redline RPM, 0 if unknown.", s => s.T.RedlineRpm);
            Add("Car", "AbsActive", "True while ABS is working.", s => s.T.AbsActive);
            Add("Car", "Throttle", "Throttle pedal, 0 to 1.", s => s.T.Throttle);
            Add("Car", "Brake", "Brake pedal, 0 to 1.", s => s.T.Brake);
            Add("Car", "BrakeBias", "Brake bias percentage.", s => s.T.BrakeBias, "format(BrakeBias, \"0.0\")");
            Add("Car", "TractionControl", "Traction control setting, -1 when the car has none.", s => s.T.TractionControl, "TractionControl < 0 ? \"--\" : format(TractionControl, \"0\")");
            Add("Car", "Abs", "ABS setting, -1 when the car has none.", s => s.T.Abs, "Abs < 0 ? \"--\" : format(Abs, \"0\")");
            Add("Car", "CarName", "Short car name.", s => s.T.CarName ?? "");
            Add("Car", "EngineWarning", "Water, oil or fuel pressure warning.", s => s.T.EngineWarning);
            Add("Car", "Connected", "True while iRacing is sending data.", s => s.T.Connected);

            // ---------------- Timing
            Add("Timing", "CurrentLap", "Current lap time in seconds.", s => s.T.CurrentLapSeconds, "laptime(CurrentLap)");
            Add("Timing", "LastLap", "Last lap time in seconds.", s => s.T.LastLapSeconds, "laptime(LastLap)");
            Add("Timing", "BestLap", "Your best lap this session.", s => s.T.BestLapSeconds, "laptime(BestLap)");
            Add("Timing", "AverageLap", "Average of your completed laps.", s => s.T.AverageLapSeconds, "laptime(AverageLap)");
            Add("Timing", "PredictedLap", "Predicted time of this lap from the live delta.", s => s.T.PredictedLapSeconds, "laptime(PredictedLap)");
            Add("Timing", "Delta", "Live delta to your best lap in seconds; positive means faster.", s => s.T.LapDeltaSeconds, "signed(Delta, 2)");
            Add("Timing", "LastLapIsBest", "True when the last lap was your best.", s => s.T.LastLapSeconds > 0 && s.T.BestLapSeconds > 0 && s.T.LastLapSeconds <= s.T.BestLapSeconds + 0.001);
            Add("Timing", "LapSummary", "True for a few seconds after you finish a lap.", s => s.T.ShowLapSummary);
            Add("Timing", "LapMinSpeed", "Slowest speed this lap, in your unit.", s => s.Units.Speed(s.T.LapMinSpeedMph), "format(LapMinSpeed, \"0\")");
            Add("Timing", "LapMaxSpeed", "Fastest speed this lap, in your unit.", s => s.Units.Speed(s.T.LapMaxSpeedMph), "format(LapMaxSpeed, \"0\")");
            Add("Timing", "LastLapSpeed", "Average speed of the last lap, in your unit (ovals).", s => s.Units.Speed(s.T.LapSpeedMph(s.T.LastLapSeconds)), "format(LastLapSpeed, \"0.0\")");

            // ---------------- Session
            Add("Session", "SessionType", "\"RACE\" or \"PRACTICE\".", s => DashKit.Session(s.T));
            Add("Session", "IsRace", "True in a race session.", s => s.T.ProgressMode == SessionProgressMode.LapRace || s.T.ProgressMode == SessionProgressMode.TimedRace);
            Add("Session", "Lap", "The lap you are on.", s => (double)Math.Max(1, s.T.LapsCompleted + 1));
            Add("Session", "LapText", "Lap with total when known: 9/18.", s => Fmt.Lap(s.T));
            Add("Session", "LapsCompleted", "Laps completed.", s => (double)s.T.LapsCompleted);
            Add("Session", "TotalLaps", "Race length in laps, 0 for timed races.", s => (double)s.T.TotalLaps);
            Add("Session", "LapsRemaining", "Laps left including this one, estimated in timed races; -1 when unknown.", s => s.T.LapsRemaining);
            Add("Session", "TimeRemaining", "Session time left in seconds.", s => s.T.SessionSecondsRemaining, "clock(TimeRemaining)");
            Add("Session", "RemainingText", "\"7 TO GO\" in lap races, otherwise the time left.", s => DashKit.Remaining(s.T));
            Add("Session", "SessionProgress", "How far through the session you are, 0 to 1.", s => Progress(s.T));
            Add("Session", "Incidents", "Your incident points.", s => (double)s.T.IncidentPoints, "Incidents + \"x\"");
            Add("Session", "Discipline", "Track type: Road, Oval, Dirt Road or Dirt Oval.", s => s.T.Discipline ?? "");
            Add("Session", "StintLaps", "Laps since your last pit stop.", s => (double)s.T.StintLaps);

            // ---------------- Fuel
            Add("Fuel", "Fuel", "Fuel in the tank, in your unit.", s => s.Units.Fuel(s.T.FuelLiters), "fuel(Fuel)");
            Add("Fuel", "FuelUnit", "\"L\" or \"GAL\".", s => s.Units.FuelSuffix);
            Add("Fuel", "FuelPct", "Tank level, 0 to 1.", s => s.T.FuelLevelPct);
            Add("Fuel", "FuelPerLap", "Average fuel per clean lap, in your unit; -1 until known.", s => s.T.FuelPerLapAverage < 0 ? -1 : s.Units.Fuel(s.T.FuelPerLapAverage), "fuel(FuelPerLap, true)");
            Add("Fuel", "FuelLastLap", "Fuel used last lap, in your unit; -1 until known.", s => s.T.FuelLastLap < 0 ? -1 : s.Units.Fuel(s.T.FuelLastLap), "fuel(FuelLastLap, true)");
            Add("Fuel", "FuelLapsLeft", "Laps the fuel lasts; -1 until known.", s => s.T.FuelLapsLeft, "FuelLapsLeft < 0 ? \"--.-\" : format(FuelLapsLeft, \"0.0\")");
            Add("Fuel", "FuelToFinish", "Fuel to add to reach the finish, in your unit; -1 until known.", s => s.T.FuelToFinish < 0 ? -1 : s.Units.Fuel(s.T.FuelToFinish), "FuelToFinish < 0 ? \"--.-\" : FuelToFinish <= 0.05 ? \"NONE\" : \"+\" + fuel(FuelToFinish)");
            Add("Fuel", "FuelEnough", "True when the fuel reaches the finish.", s => s.T.FuelLapsLeft >= 0 && s.T.LapsRemaining >= 0 && s.T.FuelLapsLeft >= s.T.LapsRemaining);

            // ---------------- Position
            Add("Position", "Position", "Overall position, 0 when unknown.", s => (double)s.T.Position, "Position > 0 ? \"P\" + Position : \"P--\"");
            Add("Position", "ClassPosition", "Position in your class.", s => (double)s.T.ClassPosition);
            Add("Position", "Starters", "Cars in the race.", s => (double)s.T.TotalStarters);
            Add("Position", "StartPosition", "Where you started.", s => (double)s.T.StartPosition);
            Add("Position", "PositionsGained", "Places gained since the start (negative when lost).", s => s.T.StartPosition > 0 && s.T.Position > 0 ? (double)(s.T.StartPosition - s.T.Position) : 0);
            Add("Position", "IRating", "Your iRating.", s => (double)s.T.PlayerIRating);

            // ---------------- Nearby cars
            Add("Nearby cars", "AheadName", "Driver just ahead on track.", s => s.T.DriverAhead);
            Add("Nearby cars", "AheadGap", "Seconds to the car ahead.", s => Math.Abs(s.T.RelativeAheadSeconds), "format(AheadGap, \"0.0\")");
            Add("Nearby cars", "AheadNumber", "Car number ahead.", s => s.T.AheadDetail?.CarNumber ?? "");
            Add("Nearby cars", "AheadIRating", "iRating of the car ahead.", s => (double)(s.T.AheadDetail?.IRating ?? 0));
            Add("Nearby cars", "BehindName", "Driver just behind on track.", s => s.T.DriverBehind);
            Add("Nearby cars", "BehindGap", "Seconds to the car behind.", s => Math.Abs(s.T.RelativeBehindSeconds), "format(BehindGap, \"0.0\")");
            Add("Nearby cars", "BehindNumber", "Car number behind.", s => s.T.BehindDetail?.CarNumber ?? "");
            Add("Nearby cars", "BehindIRating", "iRating of the car behind.", s => (double)(s.T.BehindDetail?.IRating ?? 0));
            Add("Nearby cars", "CarLeft", "True while a car is alongside on the left.", s => s.T.LeftBlindSpot != BlindSpotZone.None);
            Add("Nearby cars", "CarRight", "True while a car is alongside on the right.", s => s.T.RightBlindSpot != BlindSpotZone.None);
            Add("Nearby cars", "LeftZone", "Where the car on the left is: Front, Beside, Rear or None.", s => s.T.LeftBlindSpot.ToString());
            Add("Nearby cars", "RightZone", "Where the car on the right is: Front, Beside, Rear or None.", s => s.T.RightBlindSpot.ToString());

            // ---------------- Flags
            Add("Flags", "Flag", "The flag being shown: None, Green, White, Yellow, Red, Black, Meatball, Blue, Surface, Checkered.", s => FlagName(s.T.Flag));
            Add("Flags", "FlagActive", "True while any flag alert is showing.", s => s.T.Flag != RacingFlag.None);
            Add("Flags", "Caution", "True for the whole caution period.", s => s.T.CautionActive);
            Add("Flags", "WhiteFlag", "True on the last lap.", s => s.T.WhiteFlagActive);

            // ---------------- Pit
            Add("Pit", "OnPitRoad", "True on pit road.", s => s.T.OnPitRoad);
            Add("Pit", "PitLimiter", "True with the pit limiter on.", s => s.T.PitLimiter);
            Add("Pit", "PitSpeedLimit", "Pit lane limit in your unit, 0 when unknown.", s => s.Units.Speed(s.T.PitSpeedLimitMph), "format(PitSpeedLimit, \"0\")");
            Add("Pit", "OverPitLimit", "True when faster than the pit limit.", s => s.T.PitSpeedLimitMph > 0 && s.T.SpeedMph > s.T.PitSpeedLimitMph + 0.5);

            // ---------------- Conditions
            Add("Conditions", "AirTemp", "Air temperature in your temperature unit (°C or °F).", s => Temp(s, s.T.AirTempC), "format(AirTemp, \"0\") + \"°\"");
            Add("Conditions", "TempUnit", "\"°C\" or \"°F\".", s => s.Units.TemperatureLabel);
            Add("Conditions", "TrackTemp", "Track temperature in your temperature unit (°C or °F).", s => Temp(s, s.T.TrackTempC), "format(TrackTemp, \"0\") + \"°\"");
            Add("Conditions", "TrackLength", "Track length in miles or km to match your speed unit.", s => s.Units.Metric ? s.T.TrackLengthMiles * 1.609344 : s.T.TrackLengthMiles, "format(TrackLength, \"0.00\")");
        }

        /// <summary>
        /// How far through the car's own shift lights the revs are: 0 at the first
        /// light, 1 at the last. Uses iRacing's shift percentage when the car has no data.
        /// </summary>
        public static double RevFraction(TelemetrySnapshot t)
        {
            double first = t.ShiftFirstRpm, last = t.ShiftLastRpm > 0 ? t.ShiftLastRpm : t.ShiftRpm;
            if (first <= 0 || last <= first || t.EngineRpm <= 0) return t.ShiftIndicatorPct;
            return Math.Max(0, Math.Min(1, (t.EngineRpm - first) / (last - first)));
        }

        /// <summary>True at the car's shift point: its blink RPM when known, else iRacing's alert.</summary>
        public static bool AtShiftPoint(TelemetrySnapshot t)
        {
            double blink = t.ShiftBlinkRpm > 0 ? t.ShiftBlinkRpm : t.ShiftRpm;
            if (blink <= 0 || t.EngineRpm <= 0) return t.ShiftAlert;
            return t.EngineRpm >= blink;
        }

        private static double Temp(EvalScope s, double celsius) => s.Units.Temperature(celsius);

        private static double Progress(TelemetrySnapshot t)
        {
            if (t.ProgressMode == SessionProgressMode.LapRace && t.TotalLaps > 0) return Math.Min(1, t.LapsCompleted / (double)t.TotalLaps);
            if (t.SessionTotalSeconds > 0) return Math.Max(0, Math.Min(1, 1 - t.SessionSecondsRemaining / t.SessionTotalSeconds));
            return 0;
        }

        public static string FlagName(RacingFlag flag)
        {
            switch (flag)
            {
                case RacingFlag.BlueYellowStripe: return "Blue";
                case RacingFlag.SafetyCar: return "SafetyCar";
                default: return flag.ToString();
            }
        }

        private static void Add(string category, string name, string description, Func<EvalScope, object> get, string suggested = null)
        {
            var field = new DataField { Name = name, Category = category, Description = description, Get = get, Suggested = suggested ?? name };
            All.Add(field);
            ByName[name] = field;
        }

        public static DataField Find(string name) => name != null && ByName.TryGetValue(name, out DataField f) ? f : null;

        public static IEnumerable<DataField> InCategory(string category) => All.Where(f => f.Category == category);
    }
}
