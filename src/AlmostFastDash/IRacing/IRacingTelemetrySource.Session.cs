using System;
using System.Collections.Generic;

namespace AlmostFastDash.IRacing
{
    internal sealed partial class IRacingTelemetrySource : IDisposable
    {
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
    }
}
