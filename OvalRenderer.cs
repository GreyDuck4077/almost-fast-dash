using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace DV5Direct
{
    /// <summary>
    /// Stock-car oval dashboard. Built around what matters in a pack: a spotter
    /// view of cars alongside, gaps to the cars in front and behind, lap speed in
    /// MPH, positions gained, caution and white-flag state, and whether the fuel
    /// makes it to the end. Gear and RPM take a back seat since ovals barely shift.
    /// </summary>
    internal sealed class OvalRenderer : IDashboardRenderer
    {
        private readonly int _width, _height;
        private readonly DashKit _kit;
        private readonly UnitSystem _units;
        private readonly FlagPainter _flags;

        public OvalRenderer(int width, int height, DashboardSettings settings, UnitSystem units)
        {
            _width = width;
            _height = height;
            _kit = new DashKit(settings?.Accent ?? Color.FromArgb(255, 176, 32));
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
                DrawGaps(g, t, new RectangleF(8, 66, 268, 326));
                if (t.OnPitRoad) PitGauge.Draw(g, _kit, _units, t, new PointF(427, 232), 128);
                else DrawSpotter(g, t, shiftFlashOn, new RectangleF(282, 66, 290, 326));
                DrawLaps(g, t, new RectangleF(578, 66, 268, 172));
                DrawFuel(g, t, new RectangleF(578, 244, 268, 148));
                DrawDelta(g, t, new RectangleF(8, 398, 838, 74));
            }
            return bitmap;
        }

        private void DrawHeader(Graphics g, TelemetrySnapshot t)
        {
            var left = new RectangleF(8, 8, 268, 50);
            DashKit.Panel(g, left);
            _kit.Label(g, DashKit.Session(t) + "   " + DashKit.Remaining(t), new RectangleF(left.X + 14, left.Y + 4, left.Width - 28, 18));
            _kit.Draw(g, "LAP " + Fmt.Lap(t), _kit.Value(24), DashKit.Text, new RectangleF(left.X + 14, left.Y + 20, 150, 28), StringAlignment.Near);

            // Race state banner: caution and white flag stay up for their whole duration.
            var state = new RectangleF(282, 8, 290, 50);
            if (t.CautionActive)
            {
                DashKit.TintedPanel(g, state, DashKit.Warn);
                _kit.Draw(g, "CAUTION", _kit.Value(26), DashKit.Warn, state);
            }
            else if (t.WhiteFlagActive)
            {
                DashKit.TintedPanel(g, state, DashKit.Text);
                _kit.Draw(g, "WHITE FLAG · 1 TO GO", _kit.Value(22), DashKit.Text, state);
            }
            else
            {
                DashKit.Panel(g, state);
                string[] labels = { "INC", "AIR", "TRACK" };
                string[] values = { t.IncidentPoints + "x", DashKit.Temperature(t.AirTempC, _units.Metric), DashKit.Temperature(t.TrackTempC, _units.Metric) };
                float w = state.Width / 3;
                for (int i = 0; i < 3; i++)
                {
                    var cell = new RectangleF(state.X + i * w, state.Y, w, state.Height);
                    _kit.Label(g, labels[i], new RectangleF(cell.X, cell.Y + 4, cell.Width, 16), StringAlignment.Center, 12);
                    _kit.Draw(g, values[i], _kit.Value(22), DashKit.Text, new RectangleF(cell.X, cell.Y + 18, cell.Width, 30));
                }
            }

            var right = new RectangleF(578, 8, 268, 50);
            DashKit.Panel(g, right);
            _kit.Label(g, "POSITION", new RectangleF(right.X + 14, right.Y + 4, 120, 18));
            string pos = t.Position > 0 ? "P" + t.Position : "P--";
            _kit.Draw(g, pos, _kit.Value(26), DashKit.Text, new RectangleF(right.X + 14, right.Y + 19, 90, 30), StringAlignment.Near);
            if (t.TotalStarters > 0)
                _kit.Draw(g, "/" + t.TotalStarters, _kit.Value(18), DashKit.Dim,
                    new RectangleF(right.X + 12 + _kit.Measure(g, pos, _kit.Value(26)), right.Y + 23, 70, 28), StringAlignment.Near);
            int gained = t.StartPosition > 0 && t.Position > 0 ? t.StartPosition - t.Position : 0;
            string gainText = gained == 0 ? "±0" : (gained > 0 ? "▲" : "▼") + Math.Abs(gained);
            Color gainTone = gained > 0 ? DashKit.Good : gained < 0 ? DashKit.Bad : DashKit.Muted;
            if (t.StartPosition > 0)
                _kit.Chip(g, new RectangleF(right.Right - 104, right.Y + 13, 92, 26), gainText + " START", gainTone, Color.FromArgb(36, gainTone), 12);
        }

        private void DrawGaps(Graphics g, TelemetrySnapshot t, RectangleF b)
        {
            var ahead = new RectangleF(b.X, b.Y, b.Width, (b.Height - 6) / 2);
            var behind = new RectangleF(b.X, ahead.Bottom + 6, b.Width, ahead.Height);
            GapCard(g, ahead, "CAR AHEAD", t.AheadDetail, t.DriverAhead, t.RelativeAheadSeconds, DashKit.Good);
            GapCard(g, behind, "CAR BEHIND", t.BehindDetail, t.DriverBehind, t.RelativeBehindSeconds, DashKit.Warn);
        }

        private void GapCard(Graphics g, RectangleF b, string label, RelativeDriver driver, string fallback, double gap, Color tone)
        {
            DashKit.Panel(g, b);
            DashKit.Fill(g, new RectangleF(b.X + 1, b.Y + 14, 3, b.Height - 28), tone, 1.5f);
            _kit.Label(g, label, new RectangleF(b.X + 16, b.Y + 8, 140, 18));
            bool present = fallback != "---";
            var number = new RectangleF(b.X + 16, b.Y + 32, 50, 38);
            DashKit.Fill(g, number, present ? Color.FromArgb(226, 232, 240) : DashKit.Track, 7);
            _kit.Draw(g, present ? driver?.CarNumber ?? "" : "", _kit.Value(22), DashKit.Background, number);
            _kit.Draw(g, present ? driver?.Name ?? fallback : "NO CAR", _kit.Value(19), present ? DashKit.Text : DashKit.Dim,
                new RectangleF(number.Right + 10, number.Y, b.Right - number.Right - 24, number.Height), StringAlignment.Near);
            _kit.Draw(g, present ? Math.Abs(gap).ToString("0.00") : "--.--", _kit.Value(46), present ? tone : DashKit.Dim,
                new RectangleF(b.X + 12, b.Y + 76, b.Width - 60, b.Height - 82), StringAlignment.Near);
            _kit.Label(g, "SEC", new RectangleF(b.Right - 60, b.Bottom - 40, 46, 20), StringAlignment.Far);
        }

        private void DrawSpotter(Graphics g, TelemetrySnapshot t, bool shiftFlashOn, RectangleF b)
        {
            DashKit.Panel(g, b);
            bool left = t.LeftBlindSpot != BlindSpotZone.None, right = t.RightBlindSpot != BlindSpotZone.None;
            string call = left && right ? "THREE WIDE" : left ? "CAR LEFT" : right ? "CAR RIGHT" : "CLEAR";
            Color callTone = left || right ? DashKit.Bad : DashKit.Good;
            _kit.Label(g, "SPOTTER", new RectangleF(b.X + 14, b.Y + 8, 100, 18));
            _kit.Draw(g, call, _kit.Value(24), callTone, new RectangleF(b.X, b.Y + 26, b.Width, 32));

            // Your car in the middle, lanes either side.
            float cx = b.X + b.Width / 2, top = b.Y + 70, carH = 150, carW = 64;
            var lane = new RectangleF(cx - 128, top - 8, 256, carH + 16);
            using (var lanePen = new Pen(Color.FromArgb(40, 255, 255, 255), 1.5f) { DashStyle = DashStyle.Dash })
            {
                g.DrawLine(lanePen, cx - 44, lane.Y, cx - 44, lane.Bottom);
                g.DrawLine(lanePen, cx + 44, lane.Y, cx + 44, lane.Bottom);
            }
            DrawCar(g, new RectangleF(cx - carW / 2, top, carW, carH), _kit.Accent, true);
            if (left) DrawCar(g, SideCar(cx - 88, top, carW, carH, t.LeftBlindSpot), DashKit.Bad, false);
            if (right) DrawCar(g, SideCar(cx + 88, top, carW, carH, t.RightBlindSpot), DashKit.Bad, false);

            // Gear and speed stay available but small.
            var strip = new RectangleF(b.X + 14, b.Bottom - 62, b.Width - 28, 50);
            DashKit.Fill(g, strip, Color.FromArgb(120, 5, 7, 10), 8);
            _kit.Draw(g, t.Connected ? Fmt.Gear(t.Gear) : "–", _kit.Value(34), shiftFlashOn ? DashKit.Bad : DashKit.Text,
                new RectangleF(strip.X + 8, strip.Y, 50, strip.Height));
            _kit.Draw(g, _units.FormatSpeed(t), _kit.Value(34), DashKit.Text, new RectangleF(strip.X + 60, strip.Y, 110, strip.Height), StringAlignment.Far);
            _kit.Label(g, _units.SpeedLabel, new RectangleF(strip.X + 176, strip.Y + 4, 60, strip.Height), StringAlignment.Near);
            DashKit.Bar(g, new RectangleF(strip.Right - 52, strip.Y + strip.Height / 2 - 4, 44, 8), t.ShiftIndicatorPct,
                shiftFlashOn ? DashKit.Bad : _kit.ShiftColor(t.ShiftIndicatorPct));
        }

        private static RectangleF SideCar(float cx, float top, float w, float h, BlindSpotZone zone)
        {
            float offset = zone == BlindSpotZone.Front ? -h * 0.55f : zone == BlindSpotZone.Rear ? h * 0.55f : 0;
            return new RectangleF(cx - w / 2, top + offset, w, h);
        }

        private static void DrawCar(Graphics g, RectangleF b, Color color, bool solid)
        {
            // Clip other cars to the spotter lanes so a car ahead or behind reads as partial.
            var clip = g.Clip;
            g.SetClip(new RectangleF(b.X - 4, 128, b.Width + 8, 172));
            using (GraphicsPath body = DashKit.Rounded(b, b.Width * 0.38f))
            {
                using (var fill = new SolidBrush(solid ? Color.FromArgb(70, color) : Color.FromArgb(150, color))) g.FillPath(fill, body);
                using (var pen = new Pen(color, 2.5f)) g.DrawPath(pen, body);
            }
            using (var glass = new SolidBrush(Color.FromArgb(solid ? 150 : 110, 5, 7, 10)))
            {
                g.FillRectangle(glass, b.X + 10, b.Y + b.Height * 0.26f, b.Width - 20, b.Height * 0.16f);
                g.FillRectangle(glass, b.X + 12, b.Y + b.Height * 0.68f, b.Width - 24, b.Height * 0.1f);
            }
            g.Clip = clip;
        }

        private void DrawLaps(Graphics g, TelemetrySnapshot t, RectangleF b)
        {
            DashKit.Panel(g, b);
            string unit = _units.SpeedLabel;
            Row(g, new RectangleF(b.X, b.Y + 6, b.Width, 54), "LAST", t.LastLapSeconds, t.LapSpeedMph(t.LastLapSeconds), DashKit.Text, unit);
            Row(g, new RectangleF(b.X, b.Y + 62, b.Width, 54), "BEST", t.BestLapSeconds, t.LapSpeedMph(t.BestLapSeconds), DashKit.Purple, unit);
            using (var line = new Pen(DashKit.PanelBorder, 1)) g.DrawLine(line, b.X + 14, b.Y + 60, b.Right - 14, b.Y + 60);
            using (var line = new Pen(DashKit.PanelBorder, 1)) g.DrawLine(line, b.X + 14, b.Y + 118, b.Right - 14, b.Y + 118);
            _kit.Label(g, "CURRENT", new RectangleF(b.X + 14, b.Y + 124, 90, 42));
            _kit.Draw(g, Fmt.LapTime(t.CurrentLapSeconds), _kit.Value(24), DashKit.Warn, new RectangleF(b.X + 90, b.Y + 124, b.Width - 104, 42), StringAlignment.Far);
        }

        private void Row(Graphics g, RectangleF b, string label, double seconds, double mph, Color color, string unit)
        {
            _kit.Label(g, label, new RectangleF(b.X + 14, b.Y, 60, 20));
            _kit.Draw(g, Fmt.LapTime(seconds), _kit.Value(25), color, new RectangleF(b.X + 14, b.Y + 18, 140, 34), StringAlignment.Near);
            string speed = mph > 0 ? _units.Speed(mph).ToString("0.0") : "---.-";
            _kit.Draw(g, speed, _kit.Value(22), color == DashKit.Purple ? DashKit.Purple : DashKit.Text, new RectangleF(b.Right - 150, b.Y + 18, 96, 34), StringAlignment.Far);
            _kit.Label(g, unit, new RectangleF(b.Right - 52, b.Y + 22, 44, 30), StringAlignment.Near, 11);
        }

        private void DrawFuel(Graphics g, TelemetrySnapshot t, RectangleF b)
        {
            DashKit.Panel(g, b);
            double laps = t.FuelLapsLeft, togo = t.LapsRemaining, add = t.FuelToFinish;
            _kit.Metric(g, new RectangleF(b.X, b.Y + 2, b.Width / 2, 64), "FUEL LAPS", laps < 0 ? "--.-" : laps.ToString("0.0"), DashKit.FuelLapsColor(laps), 32, StringAlignment.Near);
            _kit.Metric(g, new RectangleF(b.X + b.Width / 2, b.Y + 2, b.Width / 2, 64), "FUEL " + _units.FuelSuffix, _units.FormatFuel(t.FuelLiters, false), DashKit.Text, 32, StringAlignment.Far);

            string plan;
            Color tone;
            if (laps < 0 || togo < 0) { plan = "NEEDS A CLEAN LAP"; tone = DashKit.Muted; }
            else if (add <= 0.05) { plan = "MAKES IT TO THE END"; tone = DashKit.Good; }
            else { plan = "PIT WITHIN " + Math.Max(0, Math.Floor(laps)) + " LAPS · ADD " + _units.FormatFuel(add); tone = DashKit.Warn; }
            var chip = new RectangleF(b.X + 14, b.Y + 76, b.Width - 28, 30);
            _kit.Chip(g, chip, plan, tone, Color.FromArgb(34, tone), 13);

            var bar = new RectangleF(b.X + 14, b.Bottom - 26, b.Width - 28, 8);
            double scale = Math.Max(1, Math.Max(laps, togo) * 1.05);
            DashKit.Bar(g, bar, laps > 0 ? laps / scale : t.FuelLevelPct, tone == DashKit.Warn ? DashKit.Warn : _kit.Accent);
            if (togo > 0 && laps > 0)
            {
                float x = bar.X + bar.Width * (float)(togo / scale);
                using (var tick = new Pen(DashKit.Text, 2)) g.DrawLine(tick, x, bar.Y - 5, x, bar.Bottom + 5);
            }
        }

        private void DrawDelta(Graphics g, TelemetrySnapshot t, RectangleF b)
        {
            double delta = t.LapDeltaSeconds;
            Color tone = DashKit.DeltaColor(delta);
            DashKit.Panel(g, b);
            _kit.Label(g, "DELTA TO BEST", new RectangleF(b.X + 16, b.Y + 8, 140, 18));
            _kit.Draw(g, Fmt.Signed(delta), _kit.Value(36), tone, new RectangleF(b.X + 16, b.Y + 24, 160, 46), StringAlignment.Near);
            DashKit.CenterBar(g, new RectangleF(b.X + 200, b.Y + b.Height / 2 - 9, b.Width - 224, 18), delta, 1.0, DashKit.Good, DashKit.Bad);
        }

        public void Dispose()
        {
            _kit.Dispose();
            _flags.Dispose();
        }
    }
}
