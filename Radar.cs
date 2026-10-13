using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace AlmostFastOverlays
{
    /// <summary>One car near you on track, as the radar shows it.</summary>
    internal sealed class RadarCar
    {
        /// <summary>iRacing's index for the car; -1 for a car the spotter reports that the positions don't place.</summary>
        public int CarIdx = -1;
        /// <summary>Metres ahead (positive) or behind (negative) along the track, middle to middle.</summary>
        public double Meters;
        /// <summary>Which lane to draw it in: 0 in line with you, -1 the lane to your left, 1 to your right (-2 and 2 one further).</summary>
        public float Lane;
        /// <summary>The spotter has it alongside.</summary>
        public bool Alongside;
        /// <summary>How fast the gap to it is closing in metres a second (positive: getting closer).</summary>
        public double Closing;
        public bool PaceCar;
        public string Number = "";
        public Color ClassColor = Color.Empty;
    }

    /// <summary>
    /// Builds the radar from iRacing's data. How far each car is ahead or behind comes from every car's
    /// position round the lap, so it is exact. iRacing doesn't say where the others are across the track;
    /// its spotter only says how many cars are alongside on each side. Cars alongside are matched to those
    /// reports (nearest first, and a car keeps the side it was given while it stays alongside). A car that
    /// was alongside keeps its lane for a moment after it clears, then drifts into line with you, where
    /// every other car is drawn.
    /// </summary>
    internal sealed class RadarTracker
    {
        /// <summary>A nominal car length in metres, for showing gaps in car lengths.</summary>
        public const double CarLength = 5.0;
        /// <summary>How far ahead and behind the radar keeps cars, in metres.</summary>
        public const double Reach = 80;
        private const double AlongsideWindow = 7.5, LaneHold = 2.0, LaneSpeed = 1.5, ClosingSmoothing = 0.5;

        private sealed class Seen
        {
            public double Meters = double.NaN, Time, Closing, LaneTime = double.NegativeInfinity;
            public int Side;
            public float Lane;
            public bool Updated;
        }

        private readonly Dictionary<int, Seen> _seen = new Dictionary<int, Seen>();
        private double _time = double.NaN;

        public RadarCar[] Cars { get; private set; } = new RadarCar[0];
        /// <summary>Metres to the nearest car in line ahead and behind (middle to middle), -1 when there is none.</summary>
        public double AheadMeters { get; private set; } = -1;
        public double BehindMeters { get; private set; } = -1;
        /// <summary>How fast those gaps are closing, metres a second.</summary>
        public double AheadClosing { get; private set; }
        public double BehindClosing { get; private set; }

        public void Reset()
        {
            _seen.Clear();
            _time = double.NaN;
            Cars = new RadarCar[0];
            AheadMeters = BehindMeters = -1;
            AheadClosing = BehindClosing = 0;
        }

        /// <param name="lapDist">Every car's position round the lap, 0 to 1 (negative when not on track).</param>
        /// <param name="onPitRoad">Which cars are on pit road (left off the radar); may be null.</param>
        /// <param name="carLeftRight">iRacing's spotter: 1 clear, 2 car left, 3 car right, 4 both, 5 two cars left, 6 two cars right.</param>
        /// <param name="time">The session clock in seconds.</param>
        /// <param name="describe">Fills in a car's number, class colour and whether it's the pace car; may be null.</param>
        public void Update(float[] lapDist, bool[] onPitRoad, int player, double trackMeters, int carLeftRight, double time, Action<RadarCar> describe = null)
        {
            if (lapDist == null || player < 0 || player >= lapDist.Length || lapDist[player] < 0 || trackMeters <= 0 || double.IsNaN(time))
            {
                Reset();
                return;
            }
            double dt = double.IsNaN(_time) ? 0 : time - _time;
            if (dt < 0 || dt > 2) { _seen.Clear(); dt = 0; }
            _time = time;
            foreach (Seen s in _seen.Values) s.Updated = false;

            // Where every car is, and how fast each gap is changing.
            var near = new List<KeyValuePair<int, double>>();
            double here = lapDist[player];
            for (int i = 0; i < lapDist.Length; i++)
            {
                if (i == player || lapDist[i] < 0 || (onPitRoad != null && i < onPitRoad.Length && onPitRoad[i])) continue;
                double pct = lapDist[i] - here;
                if (pct >= 0.5) pct -= 1;
                if (pct < -0.5) pct += 1;
                double meters = pct * trackMeters;
                if (!_seen.TryGetValue(i, out Seen seen)) _seen[i] = seen = new Seen();
                if (dt > 0 && !double.IsNaN(seen.Meters) && Math.Abs(meters - seen.Meters) < 30)
                {
                    double rate = (Math.Abs(seen.Meters) - Math.Abs(meters)) / dt;
                    seen.Closing += (1 - Math.Exp(-dt / ClosingSmoothing)) * (rate - seen.Closing);
                }
                else seen.Closing = 0;
                seen.Meters = meters;
                seen.Time = time;
                seen.Updated = true;
                near.Add(new KeyValuePair<int, double>(i, meters));
            }
            foreach (int gone in _seen.Where(p => !p.Value.Updated).Select(p => p.Key).ToList()) _seen.Remove(gone);

            // Match the spotter's report to the cars closest alongside. A car keeps the side it already has.
            int wantLeft = carLeftRight == 2 || carLeftRight == 4 ? 1 : carLeftRight == 5 ? 2 : 0;
            int wantRight = carLeftRight == 3 || carLeftRight == 4 ? 1 : carLeftRight == 6 ? 2 : 0;
            var candidates = near.Where(p => Math.Abs(p.Value) <= AlongsideWindow).OrderBy(p => Math.Abs(p.Value)).Select(p => p.Key).ToList();
            var left = new List<int>();
            var right = new List<int>();
            bool Recent(Seen s) => time - s.LaneTime < LaneHold;
            foreach (int i in candidates)
            {
                Seen s = _seen[i];
                if (s.Side < 0 && Recent(s) && left.Count < wantLeft) left.Add(i);
                else if (s.Side > 0 && Recent(s) && right.Count < wantRight) right.Add(i);
            }
            foreach (int i in candidates)
            {
                if (left.Contains(i) || right.Contains(i)) continue;
                if (left.Count < wantLeft) left.Add(i);
                else if (right.Count < wantRight) right.Add(i);
            }
            // Two on one side: the one that was further out stays further out.
            left = left.OrderByDescending(i => _seen[i].Side == -2 && Recent(_seen[i]) ? 0 : 1).ToList();
            right = right.OrderByDescending(i => _seen[i].Side == 2 && Recent(_seen[i]) ? 0 : 1).ToList();
            var alongside = new Dictionary<int, int>();
            for (int k = 0; k < left.Count; k++) alongside[left[k]] = -1 - k;
            for (int k = 0; k < right.Count; k++) alongside[right[k]] = 1 + k;

            var cars = new List<RadarCar>();
            double aheadMeters = double.MaxValue, behindMeters = double.MaxValue, aheadClosing = 0, behindClosing = 0;
            foreach (KeyValuePair<int, double> p in near)
            {
                Seen s = _seen[p.Key];
                bool beside = alongside.TryGetValue(p.Key, out int side);
                if (beside)
                {
                    s.Side = side;
                    s.LaneTime = time;
                    s.Lane = side;
                }
                else
                {
                    // Just cleared: hold the lane a moment, then drift into line.
                    float target = Recent(s) ? s.Side : 0;
                    float step = (float)(LaneSpeed * dt);
                    s.Lane = s.Lane < target ? Math.Min(target, s.Lane + step) : Math.Max(target, s.Lane - step);
                    if (!Recent(s) && Math.Abs(s.Lane) < 0.01f) s.Side = 0;
                }
                bool inLine = !beside && Math.Abs(s.Lane) < 0.5f;
                if (inLine && p.Value > 0 && p.Value < aheadMeters) { aheadMeters = p.Value; aheadClosing = s.Closing; }
                if (inLine && p.Value < 0 && -p.Value < behindMeters) { behindMeters = -p.Value; behindClosing = s.Closing; }
                if (Math.Abs(p.Value) > Reach) continue;
                var car = new RadarCar { CarIdx = p.Key, Meters = p.Value, Lane = s.Lane, Alongside = beside, Closing = s.Closing };
                describe?.Invoke(car);
                cars.Add(car);
            }
            // The spotter reports a car the positions can't place (they lag a little): show it beside you anyway.
            for (int k = left.Count; k < wantLeft; k++) cars.Add(new RadarCar { Lane = -1 - k, Alongside = true });
            for (int k = right.Count; k < wantRight; k++) cars.Add(new RadarCar { Lane = 1 + k, Alongside = true });

            Cars = cars.OrderBy(c => c.Meters).ToArray();
            AheadMeters = aheadMeters == double.MaxValue ? -1 : aheadMeters;
            BehindMeters = behindMeters == double.MaxValue ? -1 : behindMeters;
            AheadClosing = aheadClosing;
            BehindClosing = behindClosing;
        }

        /// <summary>Clear track between your car and another, in car lengths (bumper to bumper); -1 for none.</summary>
        public static double Lengths(double meters) => meters < 0 ? -1 : Math.Max(0, (meters - CarLength) / CarLength);

        // ------------------------------------------------------------------ previews

        /// <summary>
        /// A drafting line for the designer's previews: a car tucked in ahead, one behind, one further up on the
        /// left, and a car alongside wherever the preview's blind-spot zones put one.
        /// </summary>
        internal static void Preview(TelemetrySnapshot s)
        {
            var cars = new List<RadarCar>
            {
                new RadarCar { CarIdx = 4, Meters = -10.5, Number = "31", Closing = 0.4 },
                new RadarCar { CarIdx = 2, Meters = 9.2, Number = "05", Closing = 0.2 },
                new RadarCar { CarIdx = 1, Meters = 24, Lane = -1, Number = "12", Closing = -0.6 }
            };
            double Beside(BlindSpotZone zone) => zone == BlindSpotZone.Front ? 2.6 : zone == BlindSpotZone.Rear ? -2.8 : -1.2;
            if (s.LeftBlindSpot != BlindSpotZone.None) cars.Add(new RadarCar { CarIdx = 6, Meters = Beside(s.LeftBlindSpot), Lane = -1, Alongside = true, Number = "9" });
            if (s.RightBlindSpot != BlindSpotZone.None) cars.Add(new RadarCar { CarIdx = 7, Meters = Beside(s.RightBlindSpot), Lane = 1, Alongside = true, Number = "88" });
            s.Radar = cars.OrderBy(c => c.Meters).ToArray();
            s.AheadMeters = 9.2;
            s.BehindMeters = 10.5;
            s.AheadClosing = 0.2;
            s.BehindClosing = 0.4;
        }

        /// <summary>
        /// The designer's animated lap: a car drafting a few lengths ahead, one sitting behind, and one coming
        /// up the left, alongside and on past, with another briefly alongside on the right (matching the
        /// animated lap's blind-spot timing).
        /// </summary>
        internal static void Animate(TelemetrySnapshot s, double seconds)
        {
            double cycle = seconds % 16.0;
            var cars = new List<RadarCar>();
            double ahead = 9.5 + 2.5 * Math.Sin(seconds / 2.3), behind = -(12 + 3 * Math.Sin(seconds / 3.1 + 1));
            double aheadClosing = -2.5 / 2.3 * Math.Cos(seconds / 2.3), behindClosing = -3 / 3.1 * Math.Cos(seconds / 3.1 + 1);
            cars.Add(new RadarCar { CarIdx = 2, Meters = ahead, Number = "05", Closing = aheadClosing });
            cars.Add(new RadarCar { CarIdx = 4, Meters = behind, Number = "31", Closing = behindClosing });
            if (cycle > 2 && cycle < 11)
            {
                double m = cycle < 5 ? -20 + (cycle - 2) * 5 : cycle < 8 ? -5 + (cycle - 5) * 10 / 3 : 5 + (cycle - 8) * 20 / 3;
                cars.Add(new RadarCar { CarIdx = 6, Meters = m, Lane = -1, Alongside = cycle >= 5 && cycle < 8, Number = "9", Closing = 4 });
            }
            if (cycle > 5.5 && cycle < 8)
            {
                double m = 6 - (cycle - 5.5) * 4;
                cars.Add(new RadarCar { CarIdx = 7, Meters = m, Lane = 1, Alongside = cycle >= 6.2 && cycle < 7.4, Number = "88" });
            }
            s.Radar = cars.OrderBy(c => c.Meters).ToArray();
            s.AheadMeters = ahead;
            s.BehindMeters = -behind;
            s.AheadClosing = aheadClosing;
            s.BehindClosing = behindClosing;
        }
    }
}
