using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AlmostFastDash.Checks
{
    /// <summary>
    /// Pack racing played through the real <see cref="RadarTracker"/>: cars with known lanes move around you, iRacing's
    /// spotter is worked out from them as the sim would, and every frame each car the radar draws is compared with where
    /// it really is. A car drawn on the wrong side, or out of line when it's in line, is the mistake that matters.
    /// </summary>
    internal static class RadarScenarios
    {
        private const double Track = 2400, Step = 1 / 30.0, Spotter = 4.8;

        internal sealed class Result
        {
            public bool Knowable;
            public double MaxWrong;
            public string Name;
            /// <summary>Car-frames scored, ones drawn on the wrong side, and ones drawn in line while really beside.</summary>
            public int Frames, Wrong, InLine;
            public string FirstWrong = "";
            public double WrongShare => Frames == 0 ? 0 : (double)Wrong / Frames;
        }

        private sealed class Car
        {
            public int Index;
            public string Name;
            /// <summary>Metres ahead (+) or behind (-) of you, and the lane (-1 left, 0 yours, 1 right) at a time.</summary>
            public Func<double, double> Meters;
            public Func<double, int> Lane;
        }

        private sealed class Scenario
        {
            public string Name;
            public double Seconds;
            /// <summary>False where nothing iRacing sends can tell which side a car is on, so the radar can only guess.</summary>
            public bool Knowable = true;
            /// <summary>The share of frames it may draw a car on the wrong side: none where every side can be known, a
            /// little where a car passes your follower before anything says which side it's on.</summary>
            public double MaxWrong = 0.01;
            public Car[] Cars;
        }

        /// <summary>A value that eases from a to b between two times.</summary>
        private static double Move(double t, double t0, double t1, double a, double b) =>
            t <= t0 ? a : t >= t1 ? b : a + (b - a) * (t - t0) / (t1 - t0);

        private static Car C(int index, string name, Func<double, double> meters, Func<double, int> lane) =>
            new Car { Index = index, Name = name, Meters = meters, Lane = lane };

        private static IEnumerable<Scenario> All()
        {
            // 1-2. You're followed in line; a faster car comes up the other line, past your follower and past you.
            foreach (int side in new[] { 1, -1 })
                yield return new Scenario
                {
                    Name = "passer up the " + (side > 0 ? "right" : "left") + " past your follower",
                    Seconds = 40,
                    MaxWrong = 0.08,
                    Cars = new[]
                    {
                        C(1, "follower", t => -8 + Math.Sin(t / 2) * 0.6, t => 0),
                        C(2, "passer", t => Move(t, 0, 36, -60, 40), t => side)
                    }
                };
            // 3. A car that passed alongside on the right drops back behind you; your follower closes up level with it.
            yield return new Scenario
            {
                Name = "car on the right drops back; follower closes up level",
                Seconds = 50,
                Cars = new[]
                {
                    C(1, "follower", t => Move(t, 10, 40, -25, -6.5) + Math.Sin(t) * 0.4, t => 0),
                    C(2, "dropping back", t => Move(t, 0, 8, -1, -6), t => 1)
                }
            };
            // 4. A car comes up the left and sits level with your follower, a while after a car went by on your left.
            yield return new Scenario
            {
                Name = "car comes up the left and sits level with your follower",
                Seconds = 70,
                Cars = new[]
                {
                    C(1, "follower", t => -8 + Math.Sin(t / 1.7) * 0.8, t => 0),
                    C(2, "on the left", t => Move(t, 10, 30, -50, -8.3) + Math.Sin(t / 1.3 + 2) * 0.8, t => -1),
                    C(3, "earlier passer", t => Move(t, 0, 12, -3, 70), t => -1)
                }
            };
            // 4b. The same with nothing ever seen on the left: which side the other car is on can't be known.
            yield return new Scenario
            {
                Name = "car comes up the left level with your follower, no side ever seen",
                Seconds = 70,
                Knowable = false,
                Cars = new[]
                {
                    C(1, "follower", t => -8 + Math.Sin(t / 1.7) * 0.8, t => 0),
                    C(2, "on the left", t => Move(t, 10, 30, -50, -8.3) + Math.Sin(t / 1.3 + 2) * 0.8, t => -1)
                }
            };
            // 5. Your follower pulls out to the right and passes you; the car behind it moves up into line.
            yield return new Scenario
            {
                Name = "follower pulls out right and passes; the next car moves up",
                Seconds = 40,
                Cars = new[]
                {
                    C(1, "follower", t => Move(t, 8, 30, -8, 25), t => t < 6 ? 0 : 1),
                    C(2, "next in line", t => Move(t, 10, 30, -16, -8), t => 0)
                }
            };
            // 6. The other line is ahead of your follower, then your follower catches up level with it.
            yield return new Scenario
            {
                Name = "car on the right ahead of your follower; follower catches up",
                Seconds = 60,
                Cars = new[]
                {
                    C(1, "follower", t => Move(t, 30, 50, -20, -7) + Math.Sin(t) * 0.3, t => 0),
                    C(2, "right line", t => Move(t, 0, 6, 0, -7) + Math.Sin(t / 1.5) * 0.3, t => 1)
                }
            };
            // 7. A car passes you on the left, then moves down into line in front of you.
            yield return new Scenario
            {
                Name = "passer on the left moves into line ahead",
                Seconds = 45,
                MaxWrong = 0.16,
                Cars = new[]
                {
                    C(1, "passer", t => Move(t, 0, 16, -30, 12), t => t < 20 ? -1 : 0),
                    C(2, "follower", t => -9 + Math.Sin(t / 2), t => 0)
                }
            };
            // 8. A car comes up the right beside your follower, then the two swap who's nearer again and again (a
            // drafting fight), never quite reaching you.
            yield return new Scenario
            {
                Name = "two behind trading the lead, other car on the right",
                Seconds = 80,
                Cars = new[]
                {
                    C(1, "in line", t => -9 + (t < 20 ? 0 : 2 * Math.Sin((t - 20) / 3)), t => 0),
                    C(2, "on the right", t => (t < 20 ? Move(t, 0, 20, -50, -9) : -9 - 2 * Math.Sin((t - 20) / 3)), t => 1),
                    C(3, "earlier passer", t => Move(t, 0, 12, -3, 70), t => 1)
                }
            };
            // 9. Bump drafting: your follower glued to your bumper while a car comes alongside on the right.
            yield return new Scenario
            {
                Name = "bump-drafting follower while a car comes alongside on the right",
                Seconds = 40,
                Cars = new[]
                {
                    C(1, "follower", t => -4.9 + Math.Sin(t * 2) * 0.15, t => 0),
                    C(2, "passer", t => Move(t, 0, 36, -40, 30), t => 1)
                }
            };
            // 10. Two cars nose to tail behind you, bump drafting each other, while a car goes by on the left.
            yield return new Scenario
            {
                Name = "two cars nose to tail behind you while a car goes by on the left",
                Seconds = 40,
                MaxWrong = 0.08,
                Cars = new[]
                {
                    C(1, "first behind", t => -7 + Math.Sin(t / 2) * 0.5, t => 0),
                    C(2, "second behind", t => -11.8 + Math.Sin(t / 2) * 0.5 + Math.Sin(t * 3) * 0.12, t => 0),
                    C(3, "passer", t => Move(t, 0, 36, -40, 30), t => -1)
                }
            };
            // 11. A car on the right only just overlapping your follower (half a car length back), after a car went by on
            // the right.
            yield return new Scenario
            {
                Name = "car on the right half a length behind your follower",
                Seconds = 60,
                Cars = new[]
                {
                    C(1, "follower", t => -8 + Math.Sin(t / 2) * 0.5, t => 0),
                    C(2, "on the right", t => Move(t, 10, 25, -40, -10.8) + Math.Sin(t / 1.7) * 0.4, t => 1),
                    C(3, "earlier passer", t => Move(t, 0, 12, -3, 70), t => 1)
                }
            };
        }

        internal static List<Result> Run()
        {
            var results = new List<Result>();
            var noise = new Random(7);
            foreach (Scenario scenario in All())
            {
                var radar = new RadarTracker();
                var result = new Result { Name = scenario.Name, Knowable = scenario.Knowable, MaxWrong = scenario.MaxWrong };
                int count = scenario.Cars.Max(c => c.Index) + 1;
                for (double t = 0; t <= scenario.Seconds; t += Step)
                {
                    var lapDist = Enumerable.Repeat(-1f, count).ToArray();
                    lapDist[0] = 0.5f;
                    int left = 0, right = 0;
                    foreach (Car car in scenario.Cars)
                    {
                        double m = car.Meters(t);
                        int lane = car.Lane(t);
                        // Positions arrive a little rough, as they do from the sim.
                        lapDist[car.Index] = (float)(0.5 + (m + (noise.NextDouble() - 0.5) * 0.4) / Track);
                        if (Math.Abs(m) < Spotter && lane < 0) left++;
                        if (Math.Abs(m) < Spotter && lane > 0) right++;
                    }
                    int spotter = left == 0 && right == 0 ? 1 : left > 0 && right > 0 ? 4 : left >= 2 ? 5 : right >= 2 ? 6 : left == 1 ? 2 : 3;
                    radar.Update(lapDist, null, 0, Track, spotter, t);
                    // Scored once the scene has settled, for cars close enough to matter.
                    if (t < 1) continue;
                    foreach (Car car in scenario.Cars)
                    {
                        double m = car.Meters(t);
                        if (m < -25 || m > 15) continue;
                        RadarCar drawn = radar.Cars.FirstOrDefault(x => x.CarIdx == car.Index);
                        if (drawn == null) continue;
                        int truth = car.Lane(t);
                        result.Frames++;
                        bool wrong = truth == 0 ? Math.Abs(drawn.Lane) >= 0.75 : Math.Sign(drawn.Lane) == -truth && Math.Abs(drawn.Lane) >= 0.25;
                        if (wrong)
                        {
                            result.Wrong++;
                            if (result.FirstWrong.Length == 0)
                                result.FirstWrong = "at " + t.ToString("0.0") + " s " + car.Name + " drawn " + drawn.Lane.ToString("0.0") + ", really " + truth;
                        }
                        else if (truth != 0 && Math.Abs(drawn.Lane) < 0.25) result.InLine++;
                    }
                }
                results.Add(result);
            }
            return results;
        }

        internal static string Report(List<Result> results)
        {
            var text = new StringBuilder();
            foreach (Result r in results)
                text.AppendLine(string.Format("{0,5:0.0}% wrong side, {1,5:0.0}% drawn in line while beside  {2}{3}", r.WrongShare * 100,
                    r.Frames == 0 ? 0 : 100.0 * r.InLine / r.Frames, r.Name, r.FirstWrong.Length > 0 ? "  (first: " + r.FirstWrong + ")" : "") + (r.Knowable ? "" : "  [can't be known]"));
            return text.ToString();
        }
    }
}
