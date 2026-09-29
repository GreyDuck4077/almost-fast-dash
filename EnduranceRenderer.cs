using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace DV5Direct
{
    /// <summary>
    /// Strategy dashboard for long races: live fuel use, laps of fuel, fuel to the
    /// finish, stint length and recent lap consistency around the core driving data.
    /// </summary>
    internal sealed class EnduranceRenderer : IDashboardRenderer
    {
        private static readonly Color Background = Color.FromArgb(4, 9, 14);
        private static readonly Color PanelFill = Color.FromArgb(235, 11, 19, 27);
        private static readonly Color PanelBorder = Color.FromArgb(38, 57, 72);
        private static readonly Color Muted = Color.FromArgb(150, 172, 190);
        private static readonly Color Track = Color.FromArgb(24, 37, 48);
        private static readonly Color Good = Color.FromArgb(40, 225, 120);
        private static readonly Color Caution = Color.FromArgb(255, 196, 45);
        private static readonly Color Critical = Color.FromArgb(240, 55, 70);

        private const float Margin = 8, Gap = 8;
        private readonly int _width, _height;
        private readonly Color _accent;
        private readonly UnitSystem _units;
        private readonly FlagPainter _flags;
        private readonly Font _label = Fmt.FontOf(14, FontStyle.Bold);
        private readonly Font _caption = Fmt.FontOf(13, FontStyle.Bold);
        private readonly Font _value = Fmt.FontOf(34, FontStyle.Bold);
        private readonly Font _strategy = Fmt.FontOf(40, FontStyle.Bold);
        private readonly Font _big = Fmt.FontOf(46, FontStyle.Bold);
        private readonly Font _gear = Fmt.FontOf(62, FontStyle.Bold);
        private readonly StringFormat _center = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap,
            Trimming = StringTrimming.EllipsisCharacter
        };

        public EnduranceRenderer(int width, int height, DashboardSettings settings, UnitSystem units)
        {
            _width = width;
            _height = height;
            _accent = settings?.Accent ?? Color.FromArgb(39, 199, 217);
            _units = units ?? UnitSystem.Default;
            _flags = new FlagPainter(width, height);
        }

        public Bitmap Render(TelemetrySnapshot t, bool shiftFlashOn, int previousGear, float gearTransitionProgress)
        {
            var bitmap = new Bitmap(_width, _height);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                if (t.Flag != RacingFlag.None) { _flags.Draw(g, t); return bitmap; }

                g.Clear(Background);
                using (var stripe = new Pen(Color.FromArgb(9, _accent), 1))
                    for (int x = -_height; x < _width; x += 26) g.DrawLine(stripe, x, _height, x + _height, 0);

                DrawTopRow(g, t, shiftFlashOn);
                DrawShiftBar(g, t, shiftFlashOn);
                DrawStrategyRow(g, t);
                DrawFuelPanel(g, t, new RectangleF(Margin, 272, 282, 200));
                DrawLapPanel(g, t, new RectangleF(Margin + 282 + Gap, 272, 340, 200));
                DrawStintPanel(g, t, new RectangleF(Margin + 282 + Gap + 340 + Gap, 272, _width - Margin * 2 - 282 - 340 - Gap * 2, 200));
            }
            return bitmap;
        }

        private void DrawTopRow(Graphics g, TelemetrySnapshot t, bool shiftFlashOn)
        {
            float w = (_width - Margin * 2 - Gap * 5) / 6f;
            string[] labels = { "LAP", "POSITION", "GEAR", _units.SpeedLabel, "BRAKE BIAS", "INCIDENTS" };
            string[] values =
            {
                Fmt.Lap(t),
                t.Position > 0 ? "P" + t.Position : "P--",
                t.Connected ? Fmt.Gear(t.Gear) : "–",
                _units.FormatSpeed(t),
                t.BrakeBias > 0 ? t.BrakeBias.ToString("0.0") : "--.-",
                t.IncidentPoints + "x"
            };
            for (int i = 0; i < 6; i++)
            {
                var box = new RectangleF(Margin + (w + Gap) * i, 8, w, 92);
                bool gear = i == 2;
                DrawPanel(g, box, 12, gear ? (shiftFlashOn ? Critical : _accent) : PanelBorder);
                Label(g, labels[i], new RectangleF(box.X, box.Y + 6, box.Width, 22));
                using (var brush = new SolidBrush(gear && shiftFlashOn ? Critical : Color.White))
                    Text(g, values[i], gear ? _gear : _value, brush, new RectangleF(box.X, box.Y + 26, box.Width, box.Height - 30));
            }
        }

        private void DrawShiftBar(Graphics g, TelemetrySnapshot t, bool shiftFlashOn)
        {
            const int segments = 24;
            float x = Margin, total = _width - Margin * 2, gap = 3;
            float w = (total - gap * (segments - 1)) / segments;
            int active = (int)Math.Ceiling(t.ShiftIndicatorPct * segments);
            for (int i = 0; i < segments; i++)
            {
                Color color = shiftFlashOn ? Critical : i < segments * 0.6 ? _accent : i < segments * 0.85 ? Caution : Critical;
                using (var brush = new SolidBrush(i < active ? color : Track))
                    g.FillRectangle(brush, x + i * (w + gap), 108, w, 9);
            }
        }

        private void DrawStrategyRow(Graphics g, TelemetrySnapshot t)
        {
            float w = (_width - Margin * 2 - Gap * 3) / 4f, y = 126, h = 138;
            double lapsRemaining = t.LapsRemaining;
            double fuelLaps = t.FuelLapsLeft;
            double toFinish = t.FuelToFinish;

            string perLapCaption = t.FuelLastLap >= 0 ? "LAST " + _units.FormatFuel(t.FuelLastLap, true, true) : "NEEDS A CLEAN LAP";
            Strategy(g, Slot(0), "FUEL PER LAP", _units.FormatFuel(t.FuelPerLapAverage, false, true), perLapCaption, _accent);

            Color fuelLapsColor = fuelLaps < 0 ? _accent : fuelLaps < 2 ? Critical : fuelLaps < 5 ? Caution : Good;
            Strategy(g, Slot(1), "LAPS OF FUEL", fuelLaps < 0 ? "--.-" : fuelLaps.ToString("0.0"),
                fuelLaps >= 0 && fuelLaps < 2 ? "BOX SOON" : "AT AVERAGE USE", fuelLapsColor);

            string togoCaption = t.ProgressMode == SessionProgressMode.TimedRace
                ? Fmt.Remaining(t.SessionSecondsRemaining) + " LEFT"
                : t.TotalLaps > 0 ? "OF " + t.TotalLaps : "OPEN SESSION";
            string togo = lapsRemaining < 0 ? "--" : (t.ProgressMode == SessionProgressMode.TimedRace ? "~" : "") + lapsRemaining.ToString("0");
            Strategy(g, Slot(2), "LAPS TO GO", togo, togoCaption, _accent);

            Color finishColor = toFinish < 0 ? _accent : toFinish <= 0.05 ? Good : Caution;
            string finishCaption = toFinish < 0 ? "WAITING FOR DATA" : toFinish <= 0.05 ? "NO STOP NEEDED" : "TO REACH THE FINISH";
            Strategy(g, Slot(3), "ADD FUEL", toFinish < 0 ? "--.-" : _units.FormatFuel(toFinish, false), finishCaption, finishColor);

            RectangleF Slot(int i) => new RectangleF(Margin + (w + Gap) * i, y, w, h);
        }

        private void Strategy(Graphics g, RectangleF b, string label, string value, string caption, Color status)
        {
            DrawPanel(g, b, 12, PanelBorder);
            Label(g, label, new RectangleF(b.X, b.Y + 10, b.Width, 22));
            using (var brush = new SolidBrush(status == _accent ? Color.White : status))
                Text(g, value, _strategy, brush, new RectangleF(b.X, b.Y + 36, b.Width, 54));
            using (var brush = new SolidBrush(Muted)) Text(g, caption, _caption, brush, new RectangleF(b.X + 6, b.Y + 90, b.Width - 12, 20));
            using (var pen = new Pen(status, 3)) g.DrawLine(pen, b.X + 16, b.Bottom - 12, b.Right - 16, b.Bottom - 12);
        }

        private void DrawFuelPanel(Graphics g, TelemetrySnapshot t, RectangleF b)
        {
            DrawPanel(g, b, 14, PanelBorder);
            Label(g, "FUEL", new RectangleF(b.X, b.Y + 14, b.Width, 22));
            Text(g, _units.FormatFuel(t.FuelLiters), _big, Brushes.White, new RectangleF(b.X, b.Y + 40, b.Width, 70));

            var bar = new RectangleF(b.X + 24, b.Y + 128, b.Width - 48, 16);
            using (var track = new SolidBrush(Track)) g.FillRoundedRectangle(track, bar, 8);
            double pct = Math.Max(0, Math.Min(1, t.FuelLevelPct));
            double fuelLaps = t.FuelLapsLeft;
            Color color = fuelLaps >= 0 && fuelLaps < 2 ? Critical : fuelLaps >= 0 && fuelLaps < 5 ? Caution : _accent;
            if (pct > 0.01)
                using (var fill = new SolidBrush(color))
                    g.FillRoundedRectangle(fill, new RectangleF(bar.X, bar.Y, Math.Max(16, (float)(bar.Width * pct)), bar.Height), 8);
            using (var brush = new SolidBrush(Muted))
                Text(g, pct > 0 ? (pct * 100).ToString("0") + "% OF TANK" : "TANK LEVEL", _caption, brush, new RectangleF(b.X, b.Y + 152, b.Width, 26));
        }

        private void DrawLapPanel(Graphics g, TelemetrySnapshot t, RectangleF b)
        {
            DrawPanel(g, b, 14, PanelBorder);
            Label(g, "CURRENT LAP", new RectangleF(b.X, b.Y + 14, b.Width, 22));
            using (var amber = new SolidBrush(Caution))
                Text(g, Fmt.LapTime(t.CurrentLapSeconds), _big, amber, new RectangleF(b.X, b.Y + 40, b.Width, 70));

            double delta = Math.Max(-2, Math.Min(2, t.LapDeltaSeconds));
            float center = b.X + b.Width / 2, half = b.Width / 2 - 28, y = b.Y + 136;
            float length = (float)(Math.Abs(delta) / 2 * half);
            using (var track = new Pen(Track, 14)) g.DrawLine(track, b.X + 28, y, b.Right - 28, y);
            if (delta > 0) using (var pen = new Pen(Good, 14)) g.DrawLine(pen, center, y, center + length, y);
            else if (delta < 0) using (var pen = new Pen(Critical, 14)) g.DrawLine(pen, center - length, y, center, y);
            using (var tick = new Pen(Color.White, 3)) g.DrawLine(tick, center, y - 12, center, y + 12);
            using (var brush = new SolidBrush(Muted))
                Text(g, Fmt.Signed(delta) + "  VS SESSION BEST", _caption, brush, new RectangleF(b.X, b.Y + 158, b.Width, 26));
        }

        private void DrawStintPanel(Graphics g, TelemetrySnapshot t, RectangleF b)
        {
            DrawPanel(g, b, 14, PanelBorder);
            Label(g, "STINT", new RectangleF(b.X, b.Y + 14, b.Width, 22));
            Text(g, t.StintLaps.ToString(), _big, Brushes.White, new RectangleF(b.X, b.Y + 36, b.Width, 62));
            using (var brush = new SolidBrush(Muted)) Text(g, "LAPS", _caption, brush, new RectangleF(b.X, b.Y + 92, b.Width, 18));

            // Last five laps: taller is faster; the quickest of them is highlighted.
            double[] laps = t.RecentLapSeconds ?? new double[0];
            float chartX = b.X + 22, chartW = b.Width - 44, baseY = b.Bottom - 20, chartH = 58;
            float barW = (chartW - 4 * 8) / 5f;
            double best = double.MaxValue, worst = 0;
            foreach (double lap in laps) { if (lap > 0) { best = Math.Min(best, lap); worst = Math.Max(worst, lap); } }
            for (int i = 0; i < 5; i++)
            {
                float x = chartX + i * (barW + 8);
                int index = laps.Length - 5 + i;
                double lap = index >= 0 ? laps[index] : 0;
                float height = 6;
                Color color = Track;
                if (lap > 0)
                {
                    double spread = Math.Max(0.5, worst - best);
                    height = 14 + (float)((1 - (lap - best) / spread) * (chartH - 14));
                    color = Math.Abs(lap - best) < 0.001 ? _accent : Color.FromArgb(90, 112, 128);
                }
                using (var brush = new SolidBrush(color))
                    g.FillRoundedRectangle(brush, new RectangleF(x, baseY - height, barW, height), 3);
            }
        }

        private void DrawPanel(Graphics g, RectangleF b, float radius, Color border)
        {
            using (GraphicsPath p = Fmt.Rounded(b, radius))
            using (var fill = new SolidBrush(PanelFill))
            using (var pen = new Pen(border, 2))
            {
                g.FillPath(fill, p);
                g.DrawPath(pen, p);
            }
        }

        private void Label(Graphics g, string text, RectangleF b)
        {
            using (var brush = new SolidBrush(Muted)) Text(g, text, _label, brush, b);
        }

        private void Text(Graphics g, string text, Font font, Brush brush, RectangleF b) =>
            g.DrawString(text ?? "--", font, brush, b, _center);

        public void Dispose()
        {
            foreach (Font font in new[] { _label, _caption, _value, _strategy, _big, _gear }) font.Dispose();
            _center.Dispose();
            _flags.Dispose();
        }
    }
}
