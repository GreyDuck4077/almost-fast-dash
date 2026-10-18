using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace AlmostFastDash.IRacing
{
    internal sealed partial class IRacingTelemetrySource
    {
        private static readonly string[] Corners = { "LF", "RF", "LR", "RR" };

        // Sectors: the one in progress, when it started and whether it was entered cleanly.
        private int _sectorSession = int.MinValue, _sector = -1;
        private double _sectorStartTime, _lastSectorTime;
        private bool _sectorPartial;
        private double[] _sectorTimes = new double[0], _lastLapSectors = new double[0], _bestSectors = new double[0];

        private DateTime _nextStandingsUtc;
        private StandingsEntry[] _standings = new StandingsEntry[0];
        private double _sessionBest, _classBest;

        // Notices: the message showing and what the last sample looked like, to spot changes.
        private string _notice = "", _noticeKind = "";
        private DateTime _noticeUntilUtc;
        private SessionFlags _noticeFlags;
        private int _noticeLaps = -1, _noticeSession = int.MinValue;
        private double _noticeBest;
        private bool _fuelNoticeShown;

        /// <summary>Fills the snapshot's engine, sector, tyre, standings and notice values from a sample.</summary>
        private void UpdateRaceData(Telemetry t, TelemetrySnapshot s, SessionFlags flags, bool blackSuppressed)
        {
            s.OilTempC = Optional(t, "OilTemp");
            s.OilPressureBar = Optional(t, "OilPress");
            s.WaterTempC = Optional(t, "WaterTemp");
            s.FuelPressureBar = Optional(t, "FuelPress");
            s.Voltage = Optional(t, "Voltage");
            s.DrsState = t["DRS_Status"] is int drs ? drs : -1;
            s.FuelCapacityLiters = t.SessionData?.FuelCapacityLiters ?? 0;

            UpdateSectors(t, s);
            s.Tyres = ReadTyres(t);

            if (UtcNow >= _nextStandingsUtc)
            {
                // The leaderboard changes slowly; rebuilding it four times a second is plenty.
                _nextStandingsUtc = UtcNow.AddMilliseconds(250);
                _standings = BuildStandings(t, out _sessionBest, out _classBest);
            }
            s.Standings = _standings;
            // Your own best counts even when the other cars' times aren't known (a solo session, a recording).
            double mine = s.BestLapSeconds;
            s.SessionBestLapSeconds = mine > 0 && (_sessionBest <= 0 || mine < _sessionBest) ? mine : _sessionBest;
            s.ClassBestLapSeconds = mine > 0 && (_classBest <= 0 || mine < _classBest) ? mine : _classBest;

            UpdateTrackMap(t, s);
            UpdateNotice(t, s, flags, blackSuppressed);
            s.Notice = UtcNow < _noticeUntilUtc ? _notice : "";
            s.NoticeKind = s.Notice.Length > 0 ? _noticeKind : "";
        }

        // Tracing the map: where the car is by dead reckoning, and the current lap's samples.
        private string _mapKey;
        private TrackMap _map;
        private double _mapX, _mapY, _mapTime = double.NaN, _mapPct = -1;
        private int _mapResets = int.MinValue;
        private List<double> _lapPct, _lapX, _lapY;

        /// <summary>
        /// Keeps the car positions for the map and, while this track has no map yet, traces one: speed and
        /// heading are added up into a path, and a lap from line to line with no pit stop, tow or pause
        /// becomes the map.
        /// </summary>
        private void UpdateTrackMap(Telemetry t, TelemetrySnapshot s)
        {
            s.CarLapDist = t.CarIdxLapDistPct ?? new float[0];
            s.PlayerCarIdx = ReadTelemetryInt(t, "PlayerCarIdx");
            string key = TrackMap.KeyFor(t.SessionData?.WeekendInfo);
            if (key != _mapKey)
            {
                _mapKey = key;
                _map = TrackMap.Load(key);
                _lapPct = null;
            }
            s.TrackMap = _map;
            if (_map != null || key == null) return;

            double now = Optional(t, "SessionTime"), pct = Optional(t, "LapDistPct"), yaw = Optional(t, "YawNorth");
            double vx = Optional(t, "VelocityX"), vy = Optional(t, "VelocityY");
            int resets = t.EnterExitReset;
            bool usable = !double.IsNaN(now) && !double.IsNaN(pct) && !double.IsNaN(yaw) && !double.IsNaN(vx) && !double.IsNaN(vy)
                && pct >= 0 && !s.OnPitRoad && !s.OutOfCar && resets == _mapResets;
            _mapResets = resets;
            double dt = now - _mapTime;
            _mapTime = now;
            if (!usable || double.IsNaN(dt) || dt <= 0 || dt > 0.5)
            {
                // Anything that breaks the trace starts it again at the next line.
                _lapPct = null;
                _mapPct = usable ? pct : -1;
                s.MapProgress = -1;
                return;
            }
            // Velocity is along the car (x forward, y left) and the heading is a compass bearing (clockwise
            // from north), so forward points (east, north) = (sin, cos) and left = (-cos, sin). Maps come out north up.
            double cos = Math.Cos(yaw), sin = Math.Sin(yaw);
            _mapX += (vx * sin - vy * cos) * dt;
            _mapY += (vx * cos + vy * sin) * dt;
            bool crossed = _mapPct > 0.9 && pct < 0.1;
            _mapPct = pct;
            if (crossed)
            {
                if (_lapPct != null && _lapPct.Count > 0)
                {
                    TrackMap map = TrackMap.FromLap(key, t.SessionData?.WeekendInfo?.TrackName, _lapPct, _lapX, _lapY);
                    if (map != null)
                    {
                        map.Save();
                        _map = map;
                        s.TrackMap = map;
                        s.MapProgress = -1;
                        _lapPct = null;
                        return;
                    }
                }
                _lapPct = new List<double>(4000);
                _lapX = new List<double>(4000);
                _lapY = new List<double>(4000);
            }
            if (_lapPct == null) { s.MapProgress = -1; return; }
            if (_lapPct.Count > 0 && pct < _lapPct[_lapPct.Count - 1] - 0.001) { _lapPct = null; s.MapProgress = -1; return; }
            _lapPct.Add(pct);
            _lapX.Add(_mapX);
            _lapY.Add(_mapY);
            s.MapProgress = pct;
        }

        private static double Optional(Telemetry t, string name)
        {
            object value = t[name];
            return value == null || value is Array ? double.NaN : Convert.ToDouble(value);
        }

        private void UpdateSectors(Telemetry t, TelemetrySnapshot s)
        {
            double[] starts = t.SessionData?.SectorStarts ?? new double[0];
            int count = starts.Length;
            if (count < 2) return;
            if (_sectorSession != t.SessionUniqueID || _sectorTimes.Length != count)
            {
                _sectorSession = t.SessionUniqueID;
                _sector = -1;
                _sectorTimes = Unknown(count);
                _lastLapSectors = Unknown(count);
                _bestSectors = Unknown(count);
                _lastSectorTime = 0;
            }
            double pct = Optional(t, "LapDistPct"), now = Optional(t, "SessionTime");
            if (double.IsNaN(pct) || double.IsNaN(now) || pct < 0) return;
            int index = 0;
            for (int i = 0; i < count; i++) if (pct >= starts[i]) index = i;

            if (_sector < 0)
            {
                // Joined part way through a sector: its time can't be known.
                _sector = index;
                _sectorStartTime = now;
                _sectorPartial = true;
            }
            else if (index != _sector)
            {
                bool next = index == (_sector + 1) % count;
                if (next && !_sectorPartial)
                {
                    double time = now - _sectorStartTime;
                    _sectorTimes[_sector] = time;
                    _lastSectorTime = time;
                    if (_bestSectors[_sector] <= 0 || time < _bestSectors[_sector]) _bestSectors[_sector] = time;
                }
                if (index == 0)
                {
                    _lastLapSectors = _sectorTimes;
                    _sectorTimes = Unknown(count);
                }
                // A jump (a tow, a reset, reversing over a line) makes the next sector untimed.
                _sectorPartial = !next;
                _sector = index;
                _sectorStartTime = now;
            }
            s.Sector = _sector + 1;
            s.SectorCount = count;
            s.SectorRunningSeconds = _sectorPartial ? 0 : Math.Max(0, now - _sectorStartTime);
            s.LastSectorSeconds = _lastSectorTime;
            s.SectorTimes = (double[])_sectorTimes.Clone();
            s.LastLapSectorTimes = (double[])_lastLapSectors.Clone();
            s.BestSectorTimes = (double[])_bestSectors.Clone();
        }

        private static double[] Unknown(int count) => new double[count];

        private static TyreCorner[] ReadTyres(Telemetry t)
        {
            var tyres = new TyreCorner[4];
            for (int i = 0; i < 4; i++)
            {
                string c = Corners[i];
                // iRacing names the three readings left, middle and right as seen from behind the car,
                // so the outer edge is on the left for the left-hand tyres.
                bool leftSide = c[0] == 'L';
                double l = Optional(t, c + "tempCL"), m = Optional(t, c + "tempCM"), r = Optional(t, c + "tempCR");
                double wl = Optional(t, c + "wearL"), wm = Optional(t, c + "wearM"), wr = Optional(t, c + "wearR");
                tyres[i] = new TyreCorner
                {
                    Name = c,
                    TempOuter = leftSide ? l : r, TempMiddle = m, TempInner = leftSide ? r : l,
                    WearOuter = leftSide ? wl : wr, WearMiddle = wm, WearInner = leftSide ? wr : wl,
                    PressureKpa = Optional(t, c + "coldPressure")
                };
            }
            return tyres;
        }

        private StandingsEntry[] BuildStandings(Telemetry t, out double sessionBest, out double classBest)
        {
            sessionBest = 0;
            classBest = 0;
            try
            {
                int[] positions = t.CarIdxPosition;
                if (positions == null) return new StandingsEntry[0];
                int[] classPositions = t["CarIdxClassPosition"] as int[];
                float[] last = t["CarIdxLastLapTime"] as float[], best = t["CarIdxBestLapTime"] as float[], f2 = t["CarIdxF2Time"] as float[];
                int[] laps = t["CarIdxLapCompleted"] as int[];
                float[] distances = t.CarIdxLapDistPct;
                bool[] pits = t.CarIdxOnPitRoad;
                int player = ReadTelemetryInt(t, "PlayerCarIdx");
                bool race = t.Session?.IsRace == true;
                CarDetails[] details = t.CarDetails;
                var byIndex = new Dictionary<int, DriverEntry>();
                if (details != null)
                    foreach (CarDetails d in details)
                        if (d?.Driver != null) byIndex[d.CarIdx] = d.Driver;
                byIndex.TryGetValue(player, out DriverEntry me);

                var rows = new List<StandingsEntry>();
                for (int i = 0; i < positions.Length; i++)
                {
                    byIndex.TryGetValue(i, out DriverEntry driver);
                    if (driver == null || driver.CarIsPaceCar || driver.IsSpectator) continue;
                    double carBest = At(best, i);
                    if (carBest > 0)
                    {
                        if (sessionBest <= 0 || carBest < sessionBest) sessionBest = carBest;
                        if (me != null && driver.CarClassId == me.CarClassId && (classBest <= 0 || carBest < classBest)) classBest = carBest;
                    }
                    // Early in a session iRacing gives no position until a car sets a time; list every car
                    // that is in the session anyway (ranked below).
                    if (positions[i] <= 0 && carBest <= 0 && (distances == null || i >= distances.Length || distances[i] < 0) && i != player) continue;
                    RelativeDriver info = BuildRelative(details, i, GetDriverName(details, i));
                    rows.Add(new StandingsEntry
                    {
                        Position = positions[i], ClassPosition = classPositions != null && i < classPositions.Length ? classPositions[i] : 0, CarIdx = i,
                        Name = info?.Name ?? driver.UserName, CarNumber = driver.CarNumber, IRating = (int)driver.IRating,
                        CarName = string.IsNullOrWhiteSpace(driver.CarScreenNameShort) ? driver.CarScreenName : driver.CarScreenNameShort,
                        ClassName = driver.CarClassShortName ?? "", ClassColor = ParseClassColor(driver.CarClassColor),
                        License = info?.License, LicenseColor = info?.LicenseColor ?? Color.Gray,
                        LastLap = At(last, i), BestLap = carBest, Laps = laps != null && i < laps.Length ? Math.Max(0, laps[i]) : 0,
                        GapToLeader = race ? At(f2, i) : 0, IsPlayer = i == player, InPits = pits != null && i < pits.Length && pits[i]
                    });
                }
                // iRacing's order first; cars it hasn't placed yet follow by best lap, then car number.
                rows.Sort((a, b) =>
                {
                    bool pa = a.Position > 0, pb = b.Position > 0;
                    if (pa != pb) return pa ? -1 : 1;
                    if (pa) return a.Position.CompareTo(b.Position);
                    if ((a.BestLap > 0) != (b.BestLap > 0)) return a.BestLap > 0 ? -1 : 1;
                    if (a.BestLap > 0 && a.BestLap != b.BestLap) return a.BestLap.CompareTo(b.BestLap);
                    return CarNumberOrder(a.CarNumber).CompareTo(CarNumberOrder(b.CarNumber));
                });
                for (int k = 0; k < rows.Count; k++)
                    if (rows[k].Position <= 0) rows[k].Position = k + 1;
                if (rows.Any(r => r.ClassPosition <= 0))
                    foreach (IGrouping<string, StandingsEntry> cls in rows.GroupBy(r => r.ClassName ?? ""))
                    {
                        int n = cls.Where(r => r.ClassPosition > 0).Select(r => r.ClassPosition).DefaultIfEmpty(0).Max();
                        foreach (StandingsEntry r in cls.Where(r => r.ClassPosition <= 0)) r.ClassPosition = ++n;
                    }
                StandingsEntry mine = rows.FirstOrDefault(r => r.IsPlayer);
                float DistanceOf(StandingsEntry r) => distances != null && r.CarIdx < distances.Length ? distances[r.CarIdx] : -1;
                for (int k = 0; k < rows.Count; k++)
                {
                    StandingsEntry row = rows[k];
                    if (!race)
                    {
                        // Outside a race the order is by best lap, and gaps are to the fastest lap.
                        row.GapToLeader = row.BestLap > 0 && rows[0].BestLap > 0 ? row.BestLap - rows[0].BestLap : 0;
                    }
                    row.LapsBehind = race && rows.Count > 0 ? LapsDown(rows[0].Laps, DistanceOf(rows[0]), row.Laps, DistanceOf(row)) : 0;
                    row.Interval = k == 0 ? 0 : Math.Max(0, row.GapToLeader - rows[k - 1].GapToLeader);
                    row.DeltaToPlayer = mine == null ? 0 : row.GapToLeader - mine.GapToLeader;
                }
                return rows.ToArray();
            }
            catch
            {
                return new StandingsEntry[0];
            }
        }

        /// <summary>
        /// How many whole laps a car is behind the leader on track: laps completed plus the way round the current
        /// lap (0 to 1, or below 0 when iRacing doesn't know). Lap counts alone go up as each car crosses the line,
        /// so right after the leader crosses, every car still on its way there would read "+1 LAP".
        /// </summary>
        internal static int LapsDown(int leaderLaps, float leaderDistance, int laps, float distance)
        {
            if (leaderDistance < 0 || distance < 0) return Math.Max(0, leaderLaps - laps);
            return Math.Max(0, (int)Math.Floor(leaderLaps + (double)leaderDistance - (laps + (double)distance)));
        }

        private static int CarNumberOrder(string number) => int.TryParse(number, out int n) ? n : int.MaxValue;

        private static double At(float[] values, int i) => values != null && i < values.Length && values[i] > 0 ? values[i] : 0;

        private static Color ParseClassColor(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return Color.Gray;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text.Substring(2);
            return int.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out int rgb) && rgb != 0
                ? Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255)
                : Color.Gray;
        }

        /// <summary>Raises a short message when a flag comes out, a penalty is given, you set a personal best or fuel runs short.</summary>
        private void UpdateNotice(Telemetry t, TelemetrySnapshot s, SessionFlags flags, bool blackSuppressed)
        {
            if (_noticeSession != t.SessionUniqueID)
            {
                _noticeSession = t.SessionUniqueID;
                _noticeFlags = flags;
                _noticeLaps = s.LapsCompleted;
                _noticeBest = s.BestLapSeconds;
                _fuelNoticeShown = false;
                return;
            }
            SessionFlags raised = flags & ~_noticeFlags;
            _noticeFlags = flags;
            void Show(string text, string kind, double seconds = 5)
            {
                _notice = text;
                _noticeKind = kind;
                _noticeUntilUtc = UtcNow.AddSeconds(seconds);
            }
            if ((raised & (SessionFlags.black | SessionFlags.disqualify)) != 0 && !blackSuppressed) Show("BLACK FLAG · SERVE YOUR PENALTY", "Danger", 8);
            else if ((raised & SessionFlags.repair) != 0) Show("MEATBALL · PIT FOR REPAIRS", "Danger", 8);
            else if ((raised & SessionFlags.checkered) != 0) Show("CHECKERED FLAG", "Info");
            else if ((raised & SessionFlags.white) != 0) Show("WHITE FLAG · FINAL LAP", "Info");
            else if ((raised & (SessionFlags.caution | SessionFlags.cautionWaving)) != 0) Show("FULL COURSE CAUTION", "Warning");
            else if ((raised & (SessionFlags.yellow | SessionFlags.yellowWaving)) != 0) Show("YELLOW FLAG", "Warning");
            else if ((raised & SessionFlags.blue) != 0) Show("BLUE FLAG · LET FASTER CARS BY", "Info");
            else if ((raised & SessionFlags.debris) != 0) Show("DEBRIS ON TRACK", "Warning");
            else if ((raised & SessionFlags.green) != 0) Show("GREEN FLAG", "Good", 3);

            if (s.LapsCompleted != _noticeLaps)
            {
                _noticeLaps = s.LapsCompleted;
                if (s.BestLapSeconds > 0 && (_noticeBest <= 0 || s.BestLapSeconds < _noticeBest - 0.0005) && s.LastLapSeconds > 0 && Math.Abs(s.LastLapSeconds - s.BestLapSeconds) < 0.002)
                    Show("PERSONAL BEST " + Fmt.LapTime(s.BestLapSeconds), "Good");
                _noticeBest = s.BestLapSeconds;
            }
            if (s.OnPitRoad) _fuelNoticeShown = false;
            else if (!_fuelNoticeShown && s.FuelLapsLeft >= 0 && s.FuelLapsLeft < 2 && (s.LapsRemaining < 0 || s.FuelLapsLeft < s.LapsRemaining))
            {
                _fuelNoticeShown = true;
                Show("LOW FUEL · " + s.FuelLapsLeft.ToString("0.0") + " LAPS LEFT", "Warning", 6);
            }
        }

        /// <summary>Believable engine, sector, tyre and leaderboard values for the designer's previews.</summary>
        internal static void PreviewRaceData(TelemetrySnapshot s)
        {
            s.OilTempC = 104;
            s.OilPressureBar = 4.6;
            s.WaterTempC = 88;
            s.FuelPressureBar = 3.9;
            s.Voltage = 13.8;
            s.DrsState = 0;
            s.FuelCapacityLiters = 120;
            s.SessionBestLapSeconds = 87.118;
            s.ClassBestLapSeconds = 87.402;
            s.Sector = 2;
            s.SectorCount = 3;
            s.SectorRunningSeconds = 14.27;
            s.LastSectorSeconds = 29.904;
            s.SectorTimes = new[] { 29.904, 0, 0 };
            s.LastLapSectorTimes = new[] { 30.112, 31.044, 26.776 };
            s.BestSectorTimes = new[] { 29.904, 30.871, 26.598 };
            s.Tyres = new[]
            {
                new TyreCorner { Name = "LF", TempOuter = 84, TempMiddle = 89, TempInner = 93, WearOuter = 0.91, WearMiddle = 0.93, WearInner = 0.9, PressureKpa = 165 },
                new TyreCorner { Name = "RF", TempOuter = 91, TempMiddle = 95, TempInner = 99, WearOuter = 0.86, WearMiddle = 0.9, WearInner = 0.88, PressureKpa = 166 },
                new TyreCorner { Name = "LR", TempOuter = 80, TempMiddle = 84, TempInner = 87, WearOuter = 0.94, WearMiddle = 0.95, WearInner = 0.93, PressureKpa = 158 },
                new TyreCorner { Name = "RR", TempOuter = 86, TempMiddle = 90, TempInner = 92, WearOuter = 0.9, WearMiddle = 0.92, WearInner = 0.91, PressureKpa = 159 }
            };
            // A full 22-car field in race order. The first seven keep car slots 0-6 and R. Alves (lapped, P17) keeps
            // slot 7, matching the radar and relative previews; the rest of the field takes slots 8-21.
            string[] names = { "L. HARTMANN", "S. OKAFOR", "J. MILLER", "YOU", "M. CARTER", "A. DUBOIS", "K. TANAKA", "D. ROSSI", "T. NGUYEN",
                "P. SILVA", "E. LINDQVIST", "H. SATO", "C. BAUER", "F. MORENO", "B. KOVAC", "G. WALSH", "R. ALVES", "O. HAYES", "V. PETROV",
                "I. CASTRO", "W. ZHANG", "N. BRANDT" };
            int[] slots = { 0, 1, 2, 3, 4, 5, 6, 8, 9, 10, 11, 12, 13, 14, 15, 16, 7, 17, 18, 19, 20, 21 };
            string[] numbers = { "7", "12", "05", "22", "31", "44", "9", "27", "3", "18", "61", "14", "21", "50", "73", "11", "88", "2", "66", "19", "8", "95" };
            int[] ratings = { 4410, 3890, 2340, 2815, 3120, 2650, 1980, 3350, 2910, 2480, 2205, 3015, 1890, 2120, 1760, 1645, 1180, 1530, 1395, 1310, 1240, 1050 };
            double[] gaps = { 0, 3.12, 5.48, 5.94, 6.76, 11.2, 14.9, 16.35, 18.02, 21.47, 24.81, 27.36, 30.12, 33.95, 38.4, 44.73, 91.6, 96.2, 101.8,
                108.3, 117.5, 186.9 };
            double[] bests = { 87.118, 87.402, 87.655, 87.521, 87.733, 88.012, 88.24, 88.183, 88.317, 88.402, 88.561, 88.498, 88.702, 88.815, 88.934,
                89.067, 89.105, 89.288, 89.41, 89.623, 89.87, 90.214 };
            int[] lapsDown = { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 2 };
            var rows = new StandingsEntry[names.Length];
            for (int i = 0; i < rows.Length; i++)
            {
                int rating = ratings[i];
                rows[i] = new StandingsEntry
                {
                    Position = i + 1, ClassPosition = i + 1, CarIdx = slots[i], Name = names[i], CarNumber = numbers[i], IRating = rating, CarName = "911 GT3 R",
                    ClassName = "GT3", ClassColor = Color.FromArgb(255, 218, 89), License = rating > 3000 ? "A" : rating > 2000 ? "B" : rating > 1500 ? "C" : "D",
                    LicenseColor = rating > 3000 ? Color.FromArgb(0, 120, 220) : rating > 2000 ? Color.FromArgb(0, 164, 90) : rating > 1500 ? Color.FromArgb(235, 190, 0) : Color.FromArgb(230, 120, 20),
                    LastLap = bests[i] + 0.31, BestLap = bests[i], Laps = 9 - lapsDown[i], LapsBehind = lapsDown[i],
                    GapToLeader = gaps[i], Interval = i == 0 ? 0 : gaps[i] - gaps[i - 1], DeltaToPlayer = gaps[i] - gaps[3], IsPlayer = i == 3, InPits = i == 5
                };
            }
            s.Standings = rows;
            s.TrackMap = TrackMap.Preview();
            s.IsPreview = true;
            s.PlayerCarIdx = 3;
            // By car slot. Only the cars around you (slots 1, 2 and 4) sit inside the traffic widgets' windows; the
            // rest of the field is spread around the lap, clear of them.
            s.CarLapDist = new[] { 0.62f, 0.55f, 0.47f, 0.455f, 0.43f, 0.0f, 0.21f, 0.83f, 0.665f, 0.70f, 0.735f, 0.775f, 0.865f, 0.90f, 0.94f,
                0.975f, 0.04f, 0.085f, 0.13f, 0.17f, 0.25f, 0.285f };
        }
    }
}
