using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace AlmostFastDash.IRacing
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
    /// position round the lap, so it is exact. iRacing doesn't say where the others are across the track, so
    /// lanes are worked out from what is certain:
    /// - The spotter says how many cars are alongside on each side. Those are matched to the nearest cars,
    ///   and a car keeps the side it was given while it stays alongside.
    /// - Two cars less than a car length apart along the track can't be in the same lane. So a row of cars
    ///   (a two-by-two formation, a pack two or three wide) is spread across lanes. The other line goes on
    ///   the side the spotter last saw a car beside you, or straddles your lane when that's not known.
    /// - A car keeps its lane while that evidence lasts, and afterwards for 15 seconds while it's within
    ///   three car lengths of you (your bubble) or 6 seconds further off. Then it drifts into line with
    ///   you, which is where a car is drawn when nothing says otherwise.
    /// </summary>
    internal sealed class RadarTracker
    {
        /// <summary>A nominal car length in metres, for showing gaps in car lengths.</summary>
        public const double CarLength = 5.0;
        /// <summary>How far ahead and behind the radar keeps cars, in metres.</summary>
        public const double Reach = 80;
        /// <summary>Two cars closer than this along the track (middle to middle) must be side by side.</summary>
        public const double Overlap = 4.6;
        private const double AlongsideWindow = 7.5, LaneHold = 6.0, LaneSpeed = 3.0, ClosingSmoothing = 0.5;
        /// <summary>Your bubble: cars this close (three car lengths) keep their lane for <see cref="BubbleHold"/> seconds.</summary>
        private const double Bubble = 15, BubbleHold = 15;
        /// <summary>How long the side the other line is on is remembered after the spotter last saw a car beside you.</summary>
        private const double LineMemory = 90;

        private sealed class Seen
        {
            public double Meters = double.NaN, Time, Closing, LaneTime = double.NegativeInfinity;
            /// <summary>The lane the car was last placed in, and the one it is drawn in (sliding toward it).</summary>
            public int Side;
            public float Lane;
            public bool Updated;
        }

        private readonly Dictionary<int, Seen> _seen = new Dictionary<int, Seen>();
        private double _time = double.NaN;
        /// <summary>Which side of you the other line is on (-1 left, 1 right, 0 not known), and when that was last seen.</summary>
        private int _otherLine;
        private double _otherLineTime = double.NegativeInfinity;

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
            _otherLine = 0;
            _otherLineTime = double.NegativeInfinity;
            Cars = new RadarCar[0];
            AheadMeters = BehindMeters = -1;
            AheadClosing = BehindClosing = 0;
        }

        /// <param name="lapDist">Every car's position round the lap, 0 to 1 (negative when not on track).</param>
        /// <param name="onPitRoad">Which cars are on pit road (left off the radar); may be null.</param>
        /// <param name="carLeftRight">iRacing's spotter: 1 clear, 2 car left, 3 car right, 4 both, 5 two cars left, 6 two cars right.</param>
        /// <param name="time">The session clock in seconds.</param>
        /// <param name="describe">Fills in a car's number, class color and whether it's the pace car; may be null.</param>
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

            // 1. Match the spotter's report to the cars closest alongside. A car keeps the side it already has.
            int wantLeft = carLeftRight == 2 || carLeftRight == 4 ? 1 : carLeftRight == 5 ? 2 : 0;
            int wantRight = carLeftRight == 3 || carLeftRight == 4 ? 1 : carLeftRight == 6 ? 2 : 0;
            // A car on one side only says which side the other line is on.
            if ((wantLeft > 0) != (wantRight > 0))
            {
                _otherLine = wantLeft > 0 ? -1 : 1;
                _otherLineTime = time;
            }
            else if (time - _otherLineTime > LineMemory) _otherLine = 0;
            var candidates = near.Where(p => Math.Abs(p.Value) <= AlongsideWindow).OrderBy(p => Math.Abs(p.Value)).Select(p => p.Key).ToList();
            var left = new List<int>();
            var right = new List<int>();
            // Your bubble: a car within three car lengths keeps its lane far longer than one further off.
            bool Recent(Seen s) => time - s.LaneTime < (Math.Abs(s.Meters) < Bubble ? BubbleHold : LaneHold);
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

            // 2. Every other car, nearest first, goes in a lane no car within a car length of it is in: the lane it
            // had while the evidence for it lasts, else in line with you, else the other line's side.
            var along = near.ToDictionary(p => p.Key, p => p.Value);
            var placed = new List<KeyValuePair<double, int>> { new KeyValuePair<double, int>(0, 0) };
            foreach (KeyValuePair<int, int> a in alongside) placed.Add(new KeyValuePair<double, int>(along[a.Key], a.Value));
            int line = _otherLine != 0 ? _otherLine : 1;
            var lanes = new Dictionary<int, int>(alongside);
            foreach (KeyValuePair<int, double> p in near.Where(p => !alongside.ContainsKey(p.Key)).OrderBy(p => Math.Abs(p.Value)))
            {
                Seen s = _seen[p.Key];
                double at = p.Value;
                bool Free(int lane) => !placed.Any(q => q.Value == lane && Math.Abs(q.Key - at) < Overlap);
                int chosen;
                if (s.Side != 0 && Recent(s) && Free(s.Side)) chosen = s.Side;
                else if (Free(0)) chosen = 0;
                else
                {
                    // Too close to a car in your lane to be in it: fresh evidence it's in the other line.
                    chosen = new[] { line, -line, 2 * line, -2 * line }.FirstOrDefault(Free);
                    if (chosen != 0) s.LaneTime = time;
                }
                lanes[p.Key] = chosen;
                placed.Add(new KeyValuePair<double, int>(at, chosen));
            }

            // A car out of line that is still level with a car in your lane (or with you) keeps its evidence fresh.
            // Not knowing which side the other line is on, such a row of two straddles your lane.
            var drawn = lanes.ToDictionary(p => p.Key, p => (float)p.Value);
            foreach (int car in lanes.Keys.Where(k => lanes[k] != 0 && !alongside.ContainsKey(k)).ToList())
            {
                List<int> partners = lanes.Keys.Where(k => lanes[k] == 0 && !alongside.ContainsKey(k) && Math.Abs(along[k] - along[car]) < Overlap).ToList();
                if (partners.Count > 0 || Math.Abs(along[car]) < Overlap) _seen[car].LaneTime = time;
                if (_otherLine != 0 || Math.Abs(lanes[car]) != 1) continue;
                foreach (int partner in partners)
                {
                    drawn[car] = lanes[car] * 0.5f;
                    drawn[partner] = -lanes[car] * 0.5f;
                }
            }

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
                    // Slide toward the lane it's placed in, so lane changes read as moves rather than jumps.
                    s.Side = lanes[p.Key];
                    float target = drawn[p.Key], step = (float)(LaneSpeed * dt);
                    s.Lane = s.Lane < target ? Math.Min(target, s.Lane + step) : Math.Max(target, s.Lane - step);
                }
                bool inLine = !beside && lanes[p.Key] == 0;
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
        /// A two-wide pack for the designer's previews: a row of two just ahead and a row of two just behind (the
        /// other line on your left), the next car up the road, and a car alongside wherever the preview's
        /// blind-spot zones put one.
        /// </summary>
        internal static void Preview(TelemetrySnapshot s)
        {
            var cars = new List<RadarCar>
            {
                new RadarCar { CarIdx = 4, Meters = -6.8, Number = "31", Closing = 0.4 },
                new RadarCar { CarIdx = 5, Meters = -6.3, Lane = -1, Number = "44", Closing = 0.3 },
                new RadarCar { CarIdx = 2, Meters = 7.2, Number = "05", Closing = 0.2 },
                new RadarCar { CarIdx = 1, Meters = 7.7, Lane = -1, Number = "12", Closing = -0.1 },
                new RadarCar { CarIdx = 0, Meters = 21.5, Number = "7", Closing = -0.6 }
            };
            double Beside(BlindSpotZone zone) => zone == BlindSpotZone.Front ? 2.6 : zone == BlindSpotZone.Rear ? -2.8 : -0.8;
            if (s.LeftBlindSpot != BlindSpotZone.None) cars.Add(new RadarCar { CarIdx = 6, Meters = Beside(s.LeftBlindSpot), Lane = -1, Alongside = true, Number = "9" });
            if (s.RightBlindSpot != BlindSpotZone.None) cars.Add(new RadarCar { CarIdx = 7, Meters = Beside(s.RightBlindSpot), Lane = 1, Alongside = true, Number = "88" });
            s.Radar = cars.OrderBy(c => c.Meters).ToArray();
            s.AheadMeters = 7.2;
            s.BehindMeters = 6.8;
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
            double ahead = 7.5 + 2 * Math.Sin(seconds / 2.3), behind = -(7 + 1.5 * Math.Sin(seconds / 3.1 + 1));
            double aheadClosing = -2 / 2.3 * Math.Cos(seconds / 2.3), behindClosing = -1.5 / 3.1 * Math.Cos(seconds / 3.1 + 1);
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
