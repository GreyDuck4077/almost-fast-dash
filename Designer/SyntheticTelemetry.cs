using System;

namespace AlmostFastOverlays
{
    /// <summary>
    /// A looping, made-up lap for the designer: revs climb through the gears, the
    /// pedals and delta move and a car comes alongside, so every widget can be
    /// seen working without iRacing running.
    /// </summary>
    internal sealed class SyntheticTelemetry
    {
        private const int TraceLength = 120;
        private readonly float[] _throttle = new float[TraceLength];
        private readonly float[] _brake = new float[TraceLength];
        private readonly float[] _delta = new float[80];
        private readonly DateTime _start = DateTime.UtcNow;
        private int _version;
        private int _lastTraceTick = -1, _lastDeltaTick = -1;

        public TelemetrySnapshot Next(out bool shiftFlash, out int previousGear, out float gearProgress)
        {
            double t = (DateTime.UtcNow - _start).TotalSeconds;
            double cycle = t % 16.0;

            // Accelerate through gears 2-6 for 11 s, brake hard for 3 s, coast 2 s.
            int gear;
            double shift, speed, throttle, brake;
            if (cycle < 11)
            {
                double g = cycle / 2.2;
                gear = 2 + Math.Min(4, (int)g);
                shift = g - Math.Floor(g);
                if (gear == 6) shift = Math.Min(1, (cycle - 8.8) / 2.2);
                speed = 60 + cycle * 9.5;
                throttle = 1;
                brake = 0;
            }
            else if (cycle < 14)
            {
                double p = (cycle - 11) / 3;
                gear = 6 - (int)(p * 4);
                shift = 0.25;
                speed = 164 - p * 104;
                throttle = 0;
                brake = p < 0.15 ? p / 0.15 : Math.Max(0.15, 1 - (p - 0.15));
            }
            else
            {
                gear = 2;
                shift = 0.1;
                speed = 60;
                throttle = (cycle - 14) / 2;
                brake = 0;
            }
            shiftFlash = shift >= 0.98 && ((Environment.TickCount / 140) & 1) == 0;
            double gearPos = cycle < 11 ? cycle / 2.2 : 0;
            previousGear = gear - 1;
            gearProgress = cycle < 11 && gear > 2 ? (float)Math.Min(1, (gearPos - Math.Floor(gearPos)) * 2.2 / 0.42) : 1;

            int traceTick = (int)(t * 20);
            if (traceTick != _lastTraceTick)
            {
                _lastTraceTick = traceTick;
                Array.Copy(_throttle, 1, _throttle, 0, TraceLength - 1);
                Array.Copy(_brake, 1, _brake, 0, TraceLength - 1);
                _throttle[TraceLength - 1] = (float)throttle;
                _brake[TraceLength - 1] = (float)brake;
                _version++;
            }
            int deltaTick = (int)(t * 4);
            if (deltaTick != _lastDeltaTick)
            {
                _lastDeltaTick = deltaTick;
                Array.Copy(_delta, 1, _delta, 0, _delta.Length - 1);
                _delta[_delta.Length - 1] = (float)(0.35 * Math.Sin(t / 3.1) + 0.12 * Math.Sin(t * 1.7));
            }

            double delta = 0.35 * Math.Sin(t / 3.1) + 0.12 * Math.Sin(t * 1.7);
            BlindSpotZone left = cycle > 5 && cycle < 8 ? (cycle < 6 ? BlindSpotZone.Rear : cycle < 7 ? BlindSpotZone.Beside : BlindSpotZone.Front) : BlindSpotZone.None;
            BlindSpotZone right = cycle > 6.2 && cycle < 7.4 ? BlindSpotZone.Beside : BlindSpotZone.None;
            double rpm = 3800 + shift * 4200;

            var snapshot = new TelemetrySnapshot(
                true, gear, speed, RacingFlag.None, left, right, shift,
                SessionProgressMode.LapRace, 0, 0, 8, 18,
                4, 2, 24.3 - t % 60 * 0.04, 30 + cycle * 3.6, delta,
                "J. MILLER", -0.46, "M. CARTER", 0.82, 22,
                false, 87.932, 87.521, 88.204, new bool[18], 0, 6, rpm);
            IRacingTelemetrySource.WithPreviewExtras(snapshot);
            snapshot.Throttle = throttle;
            snapshot.Brake = brake;
            snapshot.ThrottleTrace = (float[])_throttle.Clone();
            snapshot.BrakeTrace = (float[])_brake.Clone();
            snapshot.DeltaTrace = (float[])_delta.Clone();
            snapshot.TraceVersion = _version;
            return snapshot;
        }
    }
}
