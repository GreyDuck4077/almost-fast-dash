using System;
using System.Drawing;

namespace DV5Direct
{
    /// <summary>
    /// Endurance and strategy dashboard: fuel time, stops needed and refuel amount
    /// up front, stint length and lap consistency on the right, and the driving
    /// essentials in the middle.
    /// </summary>
    internal sealed class EnduranceRenderer : IDashboardRenderer
    {
        private readonly int _width, _height;
        private readonly DashKit _kit;
        private readonly UnitSystem _units;
        private readonly FlagPainter _flags;
        private static readonly PointF Hub = new PointF(427, 224);

        public EnduranceRenderer(int width, int height, DashboardSettings settings, UnitSystem units)
        {
            _width = width;
            _height = height;
            _kit = new DashKit(settings?.Accent ?? Color.FromArgb(39, 199, 217));
            _units = units ?? UnitSystem.Default;
            _flags = new FlagPainter(width, height);
        }

        public Bitmap Render(TelemetrySnapshot t, bool shiftFlashOn, int previousGear, float gearTransitionProgress)
        {
            var bitmap = new Bitmap(_width, _height);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                DashKit.Prepare(g);
                if (t.Flag != RacingFlag.None) { _flags.Draw(g, t); return bitmap; }
                DashKit.Canvas(g, _width, _height, _kit.Accent);

                DrawHeader(g, t);
                DrawStrategy(g, t, new RectangleF(8, 66, 268, 326));
                if (t.OnPitRoad) PitGauge.Draw(g, _kit, _units, t, Hub, 124);
                else DrawCenter(g, t, shiftFlashOn);
                DrawStint(g, t, new RectangleF(578, 66, 268, 326));
                DrawCarStrip(g, t, new RectangleF(8, 398, 838, 74));
            }
            return bitmap;
        }

        private void DrawHeader(Graphics g, TelemetrySnapshot t)
        {
            var left = new RectangleF(8, 8, 268, 50);
            DashKit.Panel(g, left);
            _kit.Label(g, DashKit.Session(t) + "   " + DashKit.Remaining(t), new RectangleF(left.X + 14, left.Y + 4, left.Width - 28, 18));
            _kit.Draw(g, "LAP " + Fmt.Lap(t), _kit.Value(24), DashKit.Text, new RectangleF(left.X + 14, left.Y + 20, left.Width - 28, 28), StringAlignment.Near);

            // Fuel time: how long the tank lasts at the current pace.
            var center = new RectangleF(282, 8, 290, 50);
            double laps = t.FuelLapsLeft, lap = t.ReferenceLapSeconds;
            double seconds = laps > 0 && lap > 0 ? laps * lap : -1;
            DashKit.Panel(g, center);
            _kit.Label(g, "FUEL TIME", new RectangleF(center.X + 14, center.Y + 4, 120, 18));
            _kit.Draw(g, seconds < 0 ? "--:--" : Fmt.Remaining(seconds), _kit.Value(26), DashKit.FuelLapsColor(laps), new RectangleF(center.X + 14, center.Y + 19, 150, 30), StringAlignment.Near);
            _kit.Label(g, "SESSION", new RectangleF(center.Right - 134, center.Y + 4, 120, 18), StringAlignment.Far);
            _kit.Draw(g, t.ProgressMode == SessionProgressMode.LapRace ? DashKit.Remaining(t) : Fmt.Remaining(t.SessionSecondsRemaining),
                _kit.Value(22), DashKit.Text, new RectangleF(center.Right - 154, center.Y + 21, 140, 28), StringAlignment.Far);

            var right = new RectangleF(578, 8, 268, 50);
            DashKit.Panel(g, right);
            _kit.Label(g, "POSITION", new RectangleF(right.X + 14, right.Y + 4, 120, 18));
            string pos = t.Position > 0 ? "P" + t.Position : "P--";
            _kit.Draw(g, pos, _kit.Value(26), DashKit.Text, new RectangleF(right.X + 14, right.Y + 19, 90, 30), StringAlignment.Near);
            if (t.TotalStarters > 0)
                _kit.Draw(g, "/" + t.TotalStarters, _kit.Value(18), DashKit.Dim,
                    new RectangleF(right.X + 12 + _kit.Measure(g, pos, _kit.Value(26)), right.Y + 23, 70, 28), StringAlignment.Near);
            if (t.ClassPosition > 0)
                _kit.Chip(g, new RectangleF(right.Right - 104, right.Y + 13, 92, 26), "CLASS P" + t.ClassPosition, _kit.Accent, Color.FromArgb(36, _kit.Accent), 12);
        }

        private void DrawStrategy(Graphics g, TelemetrySnapshot t, RectangleF b)
        {
            DashKit.Panel(g, b);
            _kit.Label(g, "FUEL STRATEGY", new RectangleF(b.X + 14, b.Y + 8, 180, 18));
            double laps = t.FuelLapsLeft, add = t.FuelToFinish;
            // Tank capacity follows from the level and its percentage.
            double capacity = t.FuelLevelPct > 0.02 ? t.FuelLiters / t.FuelLevelPct : 0;
            int stops = add <= 0.05 || capacity <= 0 ? 0 : (int)Math.Ceiling(add / capacity);

            float colW = (b.Width - 12) / 2, rowH = 70, top = b.Y + 30;
            string[] labels = { "FUEL " + _units.FuelSuffix, "EST LAPS", "AVG / LAP (5)", "LAST LAP", "REFUEL", "STOPS" };
            string[] values =
            {
                _units.FormatFuel(t.FuelLiters, false),
                laps < 0 ? "--.-" : laps.ToString("0.0"),
                _units.FormatFuel(t.FuelPerLapAverage, false, true),
                _units.FormatFuel(t.FuelLastLap, false, true),
                add < 0 ? "--.-" : add <= 0.05 ? "NONE" : _units.FormatFuel(add, false),
                add < 0 ? "-" : stops.ToString()
            };
            Color[] colors =
            {
                DashKit.Text, DashKit.FuelLapsColor(laps), DashKit.Text, DashKit.Text,
                add < 0 ? DashKit.Text : add <= 0.05 ? DashKit.Good : DashKit.Warn,
                stops > 0 ? DashKit.Warn : DashKit.Text
            };
            for (int i = 0; i < 6; i++)
            {
                var cell = new RectangleF(b.X + 6 + (i % 2) * colW, top + (i / 2) * rowH, colW, rowH);
                _kit.Metric(g, cell, labels[i], values[i], colors[i], 30, i % 2 == 0 ? StringAlignment.Near : StringAlignment.Far, 12);
            }
            DashKit.Bar(g, new RectangleF(b.X + 14, b.Bottom - 30, b.Width - 28, 10), t.FuelLevelPct, DashKit.FuelLapsColor(laps) == DashKit.Text ? _kit.Accent : DashKit.FuelLapsColor(laps));
        }

        private void DrawCenter(Graphics g, TelemetrySnapshot t, bool shiftFlashOn)
        {
            DashKit.SegmentArc(g, Hub, 132, 200, 140, 24, t.ShiftIndicatorPct, _kit.ShiftColor, 12, shiftFlashOn, DashKit.Bad);
            _kit.Draw(g, t.EngineRpm > 0 ? t.EngineRpm.ToString("0") : "----", _kit.Value(20), DashKit.Muted, new RectangleF(Hub.X - 70, 104, 140, 26));
            _kit.Draw(g, t.Connected ? Fmt.Gear(t.Gear) : "–", _kit.Value(140), shiftFlashOn ? DashKit.Bad : DashKit.Text,
                new RectangleF(Hub.X - 90, Hub.Y - 84, 180, 160));
            _kit.Draw(g, _units.FormatSpeed(t), _kit.Value(34), DashKit.Text, new RectangleF(Hub.X - 80, 300, 160, 40));
            _kit.Label(g, _units.SpeedLabel, new RectangleF(Hub.X - 60, 336, 120, 16), StringAlignment.Center, 12);

            double delta = t.LapDeltaSeconds;
            Color tone = DashKit.DeltaColor(delta);
            var chip = new RectangleF(Hub.X - 90, 358, 180, 34);
            DashKit.TintedPanel(g, chip, tone == DashKit.Text ? _kit.Accent : tone, 8);
            _kit.Draw(g, "Δ " + Fmt.Signed(delta), _kit.Value(22), tone, chip);
        }

        private void DrawStint(Graphics g, TelemetrySnapshot t, RectangleF b)
        {
            DashKit.Panel(g, b);
            _kit.Label(g, "STINT", new RectangleF(b.X + 14, b.Y + 8, 100, 18));
            _kit.Draw(g, t.StintLaps.ToString(), _kit.Value(40), DashKit.Text, new RectangleF(b.X + 14, b.Y + 24, 80, 46), StringAlignment.Near);
            _kit.Label(g, "LAPS", new RectangleF(b.X + 14 + _kit.Measure(g, t.StintLaps.ToString(), _kit.Value(40)), b.Y + 42, 60, 22));
            _kit.Label(g, "BEST", new RectangleF(b.Right - 150, b.Y + 8, 136, 18), StringAlignment.Far);
            _kit.Draw(g, Fmt.LapTime(t.BestLapSeconds), _kit.Value(24), DashKit.Purple, new RectangleF(b.Right - 170, b.Y + 26, 156, 34), StringAlignment.Far);

            // Last five laps: bar length shows pace relative to the quickest of them.
            _kit.Label(g, "LAST 5 LAPS", new RectangleF(b.X + 14, b.Y + 80, 140, 18));
            double[] laps = t.RecentLapSeconds ?? new double[0];
            double best = double.MaxValue, worst = 0;
            foreach (double lap in laps) if (lap > 0) { best = Math.Min(best, lap); worst = Math.Max(worst, lap); }
            float rowH = 44, top = b.Y + 104;
            for (int i = 0; i < 5; i++)
            {
                int index = laps.Length - 1 - i;
                double lap = index >= 0 ? laps[index] : 0;
                var row = new RectangleF(b.X + 14, top + i * rowH, b.Width - 28, rowH - 8);
                bool quickest = lap > 0 && Math.Abs(lap - best) < 0.001;
                _kit.Draw(g, lap > 0 ? Fmt.LapTime(lap) : "--:--.---", _kit.Value(19), quickest ? DashKit.Purple : DashKit.Text,
                    new RectangleF(row.X, row.Y, 110, row.Height), StringAlignment.Near);
                double spread = Math.Max(0.5, worst - best);
                double fraction = lap > 0 ? 0.25 + 0.75 * (1 - (lap - best) / spread) : 0;
                DashKit.Bar(g, new RectangleF(row.X + 116, row.Y + row.Height / 2 - 5, row.Width - 170, 10), fraction, quickest ? DashKit.Purple : _kit.Accent);
                string gap = lap > 0 && !quickest ? "+" + (lap - best).ToString("0.00") : "";
                _kit.Draw(g, gap, _kit.Value(13), DashKit.Muted, new RectangleF(row.Right - 52, row.Y, 52, row.Height), StringAlignment.Far);
            }
        }

        private void DrawCarStrip(Graphics g, TelemetrySnapshot t, RectangleF b)
        {
            string[] labels = { "TC", "ABS", "BIAS", "INC", "AIR", "TRACK" };
            Color[] tones = { DashKit.Blue, DashKit.Warn, DashKit.Bad, DashKit.Muted, DashKit.Muted, DashKit.Muted };
            string[] values =
            {
                DashKit.Setting(t.TractionControl), DashKit.Setting(t.Abs), t.BrakeBias > 0 ? t.BrakeBias.ToString("0.0") : "--",
                t.IncidentPoints + "x", DashKit.Temperature(t.AirTempC, _units.Metric), DashKit.Temperature(t.TrackTempC, _units.Metric)
            };
            float w = (b.Width - 5 * 6) / 6;
            for (int i = 0; i < 6; i++)
            {
                var cell = new RectangleF(b.X + i * (w + 6), b.Y, w, b.Height);
                DashKit.Panel(g, cell);
                _kit.Draw(g, labels[i], _kit.Value(12), tones[i], new RectangleF(cell.X, cell.Y + 6, cell.Width, 16));
                _kit.Draw(g, values[i], _kit.Value(30), DashKit.Text, new RectangleF(cell.X, cell.Y + 22, cell.Width, cell.Height - 26));
            }
        }

        public void Dispose()
        {
            _kit.Dispose();
            _flags.Dispose();
        }
    }
}
