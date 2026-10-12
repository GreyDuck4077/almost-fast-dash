using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace AlmostFastOverlays
{
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

            UpdateNotice(t, s, flags, blackSuppressed);
            s.Notice = UtcNow < _noticeUntilUtc ? _notice : "";
            s.NoticeKind = s.Notice.Length > 0 ? _noticeKind : "";
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
                for (int k = 0; k < rows.Count; k++)
                {
                    StandingsEntry row = rows[k];
                    if (!race)
                    {
                        // Outside a race the order is by best lap, and gaps are to the fastest lap.
                        row.GapToLeader = row.BestLap > 0 && rows[0].BestLap > 0 ? row.BestLap - rows[0].BestLap : 0;
                    }
                    row.LapsBehind = race && rows.Count > 0 ? Math.Max(0, rows[0].Laps - row.Laps) : 0;
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
            string[] names = { "L. HARTMANN", "S. OKAFOR", "J. MILLER", "YOU", "M. CARTER", "A. DUBOIS", "K. TANAKA", "R. ALVES" };
            string[] numbers = { "7", "12", "05", "22", "31", "44", "9", "88" };
            int[] ratings = { 4410, 3890, 2340, 2815, 3120, 2650, 1980, 1180 };
            double[] gaps = { 0, 3.12, 5.48, 5.94, 6.76, 11.2, 14.9, 31.6 };
            double[] bests = { 87.118, 87.402, 87.655, 87.521, 87.733, 88.012, 88.240, 89.105 };
            var rows = new StandingsEntry[names.Length];
            for (int i = 0; i < rows.Length; i++)
                rows[i] = new StandingsEntry
                {
                    Position = i + 1, ClassPosition = i + 1, CarIdx = i, Name = names[i], CarNumber = numbers[i], IRating = ratings[i], CarName = "911 GT3 R",
                    ClassName = "GT3", ClassColor = Color.FromArgb(255, 218, 89), License = ratings[i] > 3000 ? "A" : "B",
                    LicenseColor = ratings[i] > 3000 ? Color.FromArgb(0, 120, 220) : Color.FromArgb(0, 164, 90),
                    LastLap = bests[i] + 0.31, BestLap = bests[i], Laps = i == 7 ? 8 : 9, LapsBehind = i == 7 ? 1 : 0,
                    GapToLeader = gaps[i], Interval = i == 0 ? 0 : gaps[i] - gaps[i - 1], DeltaToPlayer = gaps[i] - gaps[3], IsPlayer = i == 3, InPits = i == 5
                };
            s.Standings = rows;
        }
    }
}
