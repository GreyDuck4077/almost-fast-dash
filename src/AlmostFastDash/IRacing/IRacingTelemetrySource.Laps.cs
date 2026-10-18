using System;
using System.Collections.Generic;

namespace AlmostFastDash.IRacing
{
    internal sealed partial class IRacingTelemetrySource : IDisposable
    {
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
    }
}
