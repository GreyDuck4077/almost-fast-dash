using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AlmostFastOverlays
{
    /// <summary>One car in the standings.</summary>
    internal sealed class StandingsEntry
    {
        public int CarIdx;
        public int Position, ClassPosition, StartPosition;
        public string CarNumber, Name, Team, License, CarName, Make;
        public System.Drawing.Color LicenseColor = System.Drawing.Color.Gray;
        public int IRating, Incidents;
        /// <summary>Seconds behind the class leader (race) or off the class best lap (practice/qualifying); -1 unknown.</summary>
        public double Gap = -1;
        /// <summary>Seconds to the car ahead in class; -1 unknown.</summary>
        public double Interval = -1;
        public int LapsDown;
        public int LapsCompleted;
        public double BestLap, LastLap;
        public bool IsPlayer, IsClassFastest, LastIsPersonalBest, LastIsClassBest;
        public bool InPits, OutLap, InWorld = true;
        /// <summary>Laps completed plus how far round the current lap; for the relative.</summary>
        public double Progress;
        /// <summary>Fraction of the lap, 0-1; -1 when the car isn't on track.</summary>
        public double TrackPct = -1;
        public System.Drawing.Color ClassColor = System.Drawing.Color.FromArgb(45, 212, 191);
        public int PositionsGained => StartPosition > 0 && Position > 0 ? StartPosition - Position : 0;
    }

    internal sealed class StandingsClass
    {
        public int Id;
        public string Name;
        public System.Drawing.Color Color = System.Drawing.Color.FromArgb(45, 212, 191);
        public double RelSpeed;
        public int StrengthOfField;
        public List<StandingsEntry> Entries = new List<StandingsEntry>();
    }

    internal sealed class StandingsSnapshot
    {
        public bool Connected;
        /// <summary>"R", "Q", "P" or "W".</summary>
        public string SessionLetter = "P";
        public bool IsRace;
        public double SessionElapsed, SessionTotal, SessionRemaining;
        public int TotalLaps, LeaderLap;
        public int PlayerCarIdx = -1;
        /// <summary>A lap time for the player's class, used to turn track distance into seconds.</summary>
        public double PlayerLapTime;
        public int CarCount;
        public bool InGarage;
        public List<StandingsClass> Classes = new List<StandingsClass>();

        public StandingsClass PlayerClass => Classes.FirstOrDefault(c => c.Entries.Any(e => e.IsPlayer));
        public static readonly StandingsSnapshot Empty = new StandingsSnapshot();
    }

    /// <summary>
    /// Builds the standings from live telemetry. Keeps a little state per car to
    /// spot out laps and starting positions.
    /// </summary>
    internal sealed class StandingsBuilder
    {
        private readonly bool[] _wasOnPitRoad = new bool[64];
        private readonly int[] _outLapUntil = new int[64];
        private readonly int[] _startPosition = new int[64];
        private int _session = -1;

        public StandingsSnapshot Build(Telemetry telemetry, int playerCarIdx)
        {
            var snapshot = new StandingsSnapshot { Connected = true, PlayerCarIdx = playerCarIdx };
            SessionData data = telemetry.SessionData;
            if (data == null) return snapshot;
            int sessionNum = Convert.ToInt32(telemetry["SessionNum"] ?? 0);
            if (telemetry.SessionUniqueID != _session)
            {
                // New session: forget out laps and grid positions.
                Array.Clear(_startPosition, 0, 64);
                Array.Clear(_outLapUntil, 0, 64);
                Array.Clear(_wasOnPitRoad, 0, 64);
                _session = telemetry.SessionUniqueID;
            }
            SessionEntry session = data.Session(sessionNum);
            string type = session?.SessionType ?? "";
            snapshot.IsRace = session?.IsRace ?? false;
            snapshot.SessionLetter = snapshot.IsRace ? "R" : type.IndexOf("Qual", StringComparison.OrdinalIgnoreCase) >= 0 ? "Q" : type.IndexOf("Warm", StringComparison.OrdinalIgnoreCase) >= 0 ? "W" : "P";
            snapshot.SessionTotal = session != null && session.IsLimitedTime ? session._SessionTime : 0;
            snapshot.SessionRemaining = Math.Max(0, telemetry.SessionTimeRemain);
            snapshot.SessionElapsed = Convert.ToDouble(telemetry["SessionTime"] ?? 0.0);
            snapshot.TotalLaps = session != null && session.IsLimitedSessionLaps ? session._SessionLaps : 0;
            snapshot.InGarage = telemetry["IsInGarage"] is bool garage && garage;

            int[] position = telemetry.CarIdxPosition, laps = telemetry.CarIdxLap;
            int[] classPosition = telemetry["CarIdxClassPosition"] as int[];
            int[] lapCompleted = telemetry["CarIdxLapCompleted"] as int[];
            int[] surface = telemetry["CarIdxTrackSurface"] as int[];
            float[] f2 = telemetry["CarIdxF2Time"] as float[];
            float[] last = telemetry["CarIdxLastLapTime"] as float[];
            float[] best = telemetry["CarIdxBestLapTime"] as float[];
            float[] pct = telemetry.CarIdxLapDistPct;
            bool[] pits = telemetry.CarIdxOnPitRoad;
            T At<T>(T[] array, int i, T fallback) => array != null && i >= 0 && i < array.Length ? array[i] : fallback;

            var entries = new List<Tuple<StandingsEntry, int, double, double>>();
            foreach (CarDetails car in data.Cars)
            {
                DriverEntry d = car.Driver;
                if (d == null || d.CarIsPaceCar || d.IsSpectator || d.CarIdx < 0 || d.CarIdx >= 64) continue;
                int i = d.CarIdx;
                bool onPitRoad = At(pits, i, false);
                int completed = At(lapCompleted, i, At(laps, i, 0));
                if (_wasOnPitRoad[i] && !onPitRoad) _outLapUntil[i] = completed + 1;
                _wasOnPitRoad[i] = onPitRoad;
                int pos = At(position, i, 0);
                if (snapshot.IsRace && pos > 0 && _startPosition[i] == 0 && completed <= 0) _startPosition[i] = pos;
                var entry = new StandingsEntry
                {
                    CarIdx = i,
                    Position = pos,
                    ClassPosition = At(classPosition, i, 0),
                    StartPosition = _startPosition[i],
                    CarNumber = d.CarNumber,
                    Name = d.UserName ?? ("Car " + d.CarNumber),
                    Team = d.TeamName,
                    License = string.IsNullOrEmpty(d.LicString) ? "" : d.LicString.Substring(0, 1),
                    LicenseColor = ParseIRacingColor(d.LicColor, System.Drawing.Color.Gray),
                    IRating = (int)d.IRating,
                    Incidents = d.Incidents,
                    CarName = d.CarScreenNameShort ?? d.CarScreenName,
                    Make = Make(d.CarScreenName ?? d.CarScreenNameShort),
                    LapsCompleted = completed,
                    BestLap = Math.Max(0, At(best, i, 0f)),
                    LastLap = Math.Max(0, At(last, i, 0f)),
                    IsPlayer = i == playerCarIdx,
                    InPits = onPitRoad || At(surface, i, 3) == 1,
                    OutLap = !onPitRoad && completed < _outLapUntil[i],
                    InWorld = At(surface, i, 3) >= 0
                };
                double lapPct = At(pct, i, -1f);
                entry.TrackPct = entry.InWorld && lapPct >= 0 ? lapPct : -1;
                entry.Progress = completed + Math.Max(0, lapPct);
                if (i == playerCarIdx && d.CarClassEstLapTime > 10) snapshot.PlayerLapTime = d.CarClassEstLapTime;
                entries.Add(Tuple.Create(entry, d.CarClassId, (double)At(f2, i, -1f), completed + Math.Max(0, (double)At(pct, i, 0f))));
                StandingsClass group = snapshot.Classes.FirstOrDefault(c => c.Id == d.CarClassId);
                if (group == null)
                {
                    group = new StandingsClass
                    {
                        Id = d.CarClassId,
                        Name = string.IsNullOrWhiteSpace(d.CarClassShortName) ? (d.CarScreenNameShort ?? "Class") : d.CarClassShortName,
                        Color = ParseIRacingColor(d.CarClassColor, System.Drawing.Color.FromArgb(45, 212, 191)),
                        RelSpeed = d.CarClassRelSpeed
                    };
                    snapshot.Classes.Add(group);
                }
                entry.ClassColor = group.Color;
                group.Entries.Add(entry);
            }
            StandingsEntry player = snapshot.Classes.SelectMany(c => c.Entries).FirstOrDefault(e => e.IsPlayer);
            if (player != null && player.BestLap > 0) snapshot.PlayerLapTime = player.BestLap;

            foreach (StandingsClass group in snapshot.Classes)
            {
                // Order: by official position where iRacing has one, unplaced cars after by best lap then number.
                group.Entries = group.Entries
                    .OrderBy(e => e.ClassPosition > 0 ? 0 : e.Position > 0 ? 1 : 2)
                    .ThenBy(e => e.ClassPosition > 0 ? e.ClassPosition : e.Position > 0 ? e.Position : 0)
                    .ThenBy(e => e.BestLap > 0 ? e.BestLap : double.MaxValue)
                    .ThenBy(e => ParseNumber(e.CarNumber))
                    .ToList();
                for (int k = 0; k < group.Entries.Count; k++) if (group.Entries[k].ClassPosition <= 0) group.Entries[k].ClassPosition = k + 1;

                double classBest = group.Entries.Where(e => e.BestLap > 0).Select(e => e.BestLap).DefaultIfEmpty(0).Min();
                StandingsEntry leader = group.Entries.FirstOrDefault();
                var leaderInfo = entries.FirstOrDefault(x => x.Item1 == leader);
                StandingsEntry ahead = null;
                foreach (StandingsEntry e in group.Entries)
                {
                    var info = entries.First(x => x.Item1 == e);
                    e.IsClassFastest = classBest > 0 && Math.Abs(e.BestLap - classBest) < 0.0005;
                    e.LastIsClassBest = e.LastLap > 0 && classBest > 0 && Math.Abs(e.LastLap - classBest) < 0.0005;
                    e.LastIsPersonalBest = e.LastLap > 0 && e.BestLap > 0 && Math.Abs(e.LastLap - e.BestLap) < 0.0005;
                    if (snapshot.IsRace)
                    {
                        double progressBehind = leaderInfo == null ? 0 : leaderInfo.Item4 - info.Item4;
                        e.LapsDown = e == leader ? 0 : Math.Max(0, (int)Math.Floor(progressBehind));
                        e.Gap = e == leader ? 0 : info.Item3 >= 0 && leaderInfo != null && leaderInfo.Item3 >= 0 ? Math.Max(0, info.Item3 - leaderInfo.Item3) : -1;
                        var aheadInfo = ahead == null ? null : entries.First(x => x.Item1 == ahead);
                        e.Interval = ahead == null ? 0 : aheadInfo.Item3 >= 0 && info.Item3 >= 0 ? Math.Max(0, info.Item3 - aheadInfo.Item3) : -1;
                    }
                    else
                    {
                        e.Gap = e.BestLap > 0 && classBest > 0 ? e.BestLap - classBest : -1;
                        e.Interval = e.BestLap > 0 && ahead != null && ahead.BestLap > 0 ? e.BestLap - ahead.BestLap : -1;
                    }
                    ahead = e;
                }
                group.StrengthOfField = StrengthOfField(group.Entries.Select(e => e.IRating));
            }
            snapshot.Classes = snapshot.Classes.OrderByDescending(c => c.RelSpeed).ThenBy(c => c.Id).ToList();
            snapshot.CarCount = snapshot.Classes.Sum(c => c.Entries.Count);
            snapshot.LeaderLap = snapshot.Classes.SelectMany(c => c.Entries).Select(e => e.LapsCompleted).DefaultIfEmpty(0).Max() + 1;
            return snapshot;
        }

        /// <summary>iRacing's strength-of-field average: the iRating at which a car would expect to finish mid-pack.</summary>
        public static int StrengthOfField(IEnumerable<int> ratings)
        {
            var list = ratings.Where(r => r > 0).ToList();
            if (list.Count == 0) return 0;
            double sum = list.Sum(r => Math.Pow(2, -r / 1600.0));
            return (int)Math.Round(1600 / Math.Log(2) * Math.Log(list.Count / sum));
        }

        internal static System.Drawing.Color ParseIRacingColor(string value, System.Drawing.Color fallback)
        {
            string hex = (value ?? "").Trim();
            if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) hex = hex.Substring(2);
            if (int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb) && hex.Length > 0)
            {
                var c = System.Drawing.Color.FromArgb(255, (rgb >> 16) & 0xff, (rgb >> 8) & 0xff, rgb & 0xff);
                return c.R + c.G + c.B < 40 ? fallback : c;
            }
            return fallback;
        }

        private static int ParseNumber(string number) => int.TryParse(number, out int n) ? n : 999;

        private static readonly Dictionary<string, string> Makes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Porsche"] = "POR", ["BMW"] = "BMW", ["Mercedes"] = "MER", ["Mercedes-AMG"] = "AMG", ["Audi"] = "AUD", ["Ferrari"] = "FER",
            ["Lamborghini"] = "LAM", ["McLaren"] = "MCL", ["Chevrolet"] = "CHV", ["Ford"] = "FRD", ["Toyota"] = "TOY", ["Acura"] = "ACU",
            ["Cadillac"] = "CAD", ["Dallara"] = "DAL", ["Ligier"] = "LIG", ["Aston"] = "AST", ["Lexus"] = "LEX", ["Mazda"] = "MAZ",
            ["Honda"] = "HON", ["Hyundai"] = "HYU", ["Renault"] = "REN", ["Nissan"] = "NIS", ["Corvette"] = "COR", ["Radical"] = "RAD",
            ["Oreca"] = "ORE", ["Mustang"] = "FRD", ["Camaro"] = "CHV", ["Supra"] = "TOY", ["Ray"] = "RAY", ["Williams"] = "WIL", ["Lotus"] = "LOT",
            ["Volkswagen"] = "VW", ["Kia"] = "KIA", ["Subaru"] = "SUB", ["Pontiac"] = "PON", ["Dodge"] = "DOD", ["Skip"] = "SKP", ["Super"] = "SF"
        };

        /// <summary>A short maker tag from the car name, such as "POR" for "Porsche 911 GT3 R".</summary>
        public static string Make(string carName)
        {
            if (string.IsNullOrWhiteSpace(carName)) return "";
            foreach (string word in carName.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries))
                if (Makes.TryGetValue(word, out string tag)) return tag;
            string first = carName.Split(' ')[0];
            return first.Length <= 3 ? first.ToUpperInvariant() : first.Substring(0, 3).ToUpperInvariant();
        }
    }

    /// <summary>A realistic made-up multiclass field for previews and layout editing.</summary>
    internal static class StandingsSample
    {
        private static readonly string[] First = { "Alex", "Jordan", "Sam", "Riley", "Casey", "Morgan", "Jamie", "Taylor", "Drew", "Quinn", "Avery", "Rowan", "Elliot", "Harper", "Blake", "Kai", "Noah", "Mila", "Leo", "Ivy", "Theo", "Nina", "Owen", "Zara" };
        private static readonly string[] Last = { "Marsh", "Vale", "Okafor", "Lindqvist", "Sato", "Mercer", "Brandt", "Novak", "Reyes", "Hart", "Cole", "Quinn", "Fischer", "Moreau", "Kowalski", "Tanaka", "Silva", "Duarte", "Haugen", "Pike", "Rossi", "Weber", "Laurent", "Ortega" };

        public static StandingsSnapshot Create(int seed = 0, double elapsed = 1208)
        {
            var random = new Random(7);
            var snapshot = new StandingsSnapshot { Connected = true, SessionLetter = "R", IsRace = true, SessionElapsed = elapsed, SessionTotal = 2700, SessionRemaining = 2700 - elapsed, PlayerCarIdx = 20 };
            var classes = new[]
            {
                new { Name = "GTP", Color = System.Drawing.Color.FromArgb(255, 218, 89), Count = 4, Speed = 120.0, Lap = 98.0, Cars = new[] { "Porsche 963", "BMW M Hybrid V8", "Cadillac V-Series.R", "Acura ARX-06" } },
                new { Name = "LMP2", Color = System.Drawing.Color.FromArgb(51, 206, 255), Count = 5, Speed = 110.0, Lap = 102.5, Cars = new[] { "Dallara P217" } },
                new { Name = "GT3", Color = System.Drawing.Color.FromArgb(255, 82, 140), Count = 14, Speed = 100.0, Lap = 107.8, Cars = new[] { "Porsche 911 GT3 R", "BMW M4 GT3", "Mercedes-AMG GT3", "Audi R8 LMS GT3", "Ferrari 296 GT3", "McLaren 720S GT3" } },
            };
            int carIdx = 1, name = 0, overall = 1;
            foreach (var c in classes)
            {
                var group = new StandingsClass { Id = carIdx, Name = c.Name, Color = c.Color, RelSpeed = c.Speed };
                double gap = 0;
                for (int i = 0; i < c.Count; i++, carIdx++, name++)
                {
                    double lapNoise = random.NextDouble() * 1.4;
                    gap += i == 0 ? 0 : 0.4 + random.NextDouble() * (i > 8 ? 7 : 3);
                    var entry = new StandingsEntry
                    {
                        CarIdx = carIdx, Position = overall++, ClassPosition = i + 1, StartPosition = i + 1 + (i % 3 == 1 ? 2 : i % 4 == 2 ? -1 : 0),
                        CarNumber = (carIdx * 7 % 99 + 1).ToString(),
                        Name = First[name % First.Length] + " " + Last[(name * 5) % Last.Length],
                        License = i % 5 == 4 ? "B" : "A", LicenseColor = i % 5 == 4 ? System.Drawing.Color.FromArgb(0, 164, 90) : System.Drawing.Color.FromArgb(0, 120, 220),
                        IRating = 1600 + (int)(random.NextDouble() * 5200), CarName = c.Cars[i % c.Cars.Length], Make = StandingsBuilder.Make(c.Cars[i % c.Cars.Length]),
                        BestLap = c.Lap + lapNoise, LastLap = c.Lap + lapNoise + random.NextDouble() * 0.9, Gap = gap, LapsCompleted = 11 - (i > 11 ? 1 : 0),
                        LapsDown = i > 12 ? 1 : 0, IsPlayer = carIdx == 20,
                        InPits = carIdx == 18, OutLap = carIdx == 16, InWorld = carIdx != 23
                    };
                    group.Entries.Add(entry);
                }
                // A little life: the preview reshuffles neighbouring places over time to show the animation.
                if (seed > 0 && group.Entries.Count > 3)
                {
                    int swap = 1 + seed % (group.Entries.Count - 2);
                    StandingsEntry a = group.Entries[swap], b = group.Entries[swap + 1];
                    group.Entries[swap] = b;
                    group.Entries[swap + 1] = a;
                    int pa = a.ClassPosition;
                    a.ClassPosition = b.ClassPosition;
                    b.ClassPosition = pa;
                    int oa = a.Position;
                    a.Position = b.Position;
                    b.Position = oa;
                    double ga = a.Gap;
                    a.Gap = b.Gap;
                    b.Gap = ga;
                }
                double best = group.Entries.Min(e => e.BestLap);
                StandingsEntry before = null;
                // Where each car is on track; time moves on with the seed, so faster
                // classes visibly come through the relative.
                double now = elapsed + seed * 6;
                foreach (StandingsEntry e in group.Entries)
                {
                    e.ClassColor = c.Color;
                    e.Progress = Math.Max(0, (now - e.Gap) / c.Lap);
                    e.TrackPct = e.InWorld ? e.Progress - Math.Floor(e.Progress) : -1;
                    e.IsClassFastest = Math.Abs(e.BestLap - best) < 0.0005;
                    e.Interval = before == null ? 0 : Math.Max(0, e.Gap - before.Gap);
                    e.LastIsPersonalBest = e.ClassPosition % 4 == 2;
                    if (e.LastIsPersonalBest) e.LastLap = e.BestLap;
                    before = e;
                }
                group.StrengthOfField = StandingsBuilder.StrengthOfField(group.Entries.Select(e => e.IRating));
                snapshot.Classes.Add(group);
            }
            snapshot.CarCount = snapshot.Classes.Sum(g => g.Entries.Count);
            snapshot.LeaderLap = 12;
            snapshot.PlayerLapTime = 107.8;
            return snapshot;
        }
    }
}
