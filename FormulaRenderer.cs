using System;
using System.Drawing;

namespace DV5Direct
{
    /// <summary>
    /// Formula-style wheel display: a clean grid of labeled cells around a big
    /// gear, with a full-width delta bar along the bottom. Shift lights are left
    /// to the DV-5 LEDs.
    /// </summary>
    internal sealed class FormulaRenderer : IDashboardRenderer
    {
        private static readonly Color PositionBlock = Color.FromArgb(232, 186, 18);
        private const float Gap = 6, Top = 8, TopH = 88, MidTop = 102, MidH = 282, BarTop = 390;
        private static readonly float[] Cols = { 8, 196, 336, 518, 658, 846 };

        private readonly int _width, _height;
        private readonly DashKit _kit;
        private readonly UnitSystem _units;
        private readonly FlagPainter _flags;

        public FormulaRenderer(int width, int height, DashboardSettings settings, UnitSystem units)
        {
            _width = width;
            _height = height;
            _kit = new DashKit(settings?.Accent ?? Color.FromArgb(56, 189, 248));
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

                // Top row: current lap, RPM, incidents.
                var lap = new RectangleF(8, Top, 290, TopH);
                var rpm = new RectangleF(lap.Right + Gap, Top, 250, TopH);
                var inc = new RectangleF(rpm.Right + Gap, Top, 846 - rpm.Right - Gap, TopH);
                Cell(g, lap, "CURRENT LAP", TenthsLapTime(t.CurrentLapSeconds), DashKit.Text, 44);
                DashKit.Panel(g, rpm);
                _kit.Draw(g, t.EngineRpm > 0 ? t.EngineRpm.ToString("0") : "----", _kit.Value(48), shiftFlashOn ? DashKit.Bad : DashKit.Text, new RectangleF(rpm.X, rpm.Y + 4, rpm.Width, 58));
                _kit.Label(g, "RPM", new RectangleF(rpm.X, rpm.Bottom - 26, rpm.Width, 18), StringAlignment.Center, 12);
                Cell(g, inc, "INCIDENTS", t.IncidentPoints + "x", t.IncidentPoints >= 8 ? DashKit.Warn : DashKit.Text, 44);

                // Column A: position block and speed.
                var pos = MidCell(0, true);
                DashKit.Fill(g, pos, PositionBlock, 10);
                _kit.Draw(g, "POSITION", _kit.LabelFont(13), Color.FromArgb(90, 66, 0), new RectangleF(pos.X + 14, pos.Y + 8, pos.Width - 28, 18), StringAlignment.Near);
                string place = t.Position > 0 ? "P" + t.Position : "P--";
                _kit.Draw(g, place, _kit.Value(48), Color.FromArgb(24, 18, 0), new RectangleF(pos.X + 12, pos.Y + 28, pos.Width - 24, pos.Height - 34), StringAlignment.Near);
                if (t.TotalStarters > 0)
                    _kit.Draw(g, "/" + t.TotalStarters, _kit.Value(22), Color.FromArgb(110, 82, 0), new RectangleF(pos.X + 12, pos.Y + 40, pos.Width - 26, pos.Height - 40), StringAlignment.Far);
                Cell(g, MidCell(0, false), _units.SpeedLabel, _units.FormatSpeed(t), DashKit.Text, 44);

                // Column B: gaps.
                bool ahead = t.DriverAhead != "---", behind = t.DriverBehind != "---";
                Cell(g, MidCell(1, true), "AHEAD", ahead ? Fmt.Signed(t.RelativeAheadSeconds) : "--.--", ahead ? DashKit.Good : DashKit.Text, 32, true);
                Cell(g, MidCell(1, false), "BEHIND", behind ? Fmt.Signed(t.RelativeBehindSeconds) : "--.--", behind ? DashKit.Warn : DashKit.Text, 32, true);

                // Column C: the gear, framed by brackets.
                var gear = new RectangleF(Cols[2] + Gap / 2, MidTop, Cols[3] - Cols[2] - Gap, MidH);
                DashKit.Panel(g, gear, 10, shiftFlashOn ? DashKit.Bad : Color.FromArgb(120, _kit.Accent));
                DashKit.Brackets(g, new RectangleF(gear.X + 12, gear.Y + 16, gear.Width - 24, gear.Height - 32), shiftFlashOn ? DashKit.Bad : _kit.Accent);
                _kit.Draw(g, t.Connected ? Fmt.Gear(t.Gear) : "–", _kit.Value(176), shiftFlashOn ? DashKit.Bad : DashKit.Text,
                    new RectangleF(gear.X, gear.Y + 10, gear.Width, gear.Height - 44));
                _kit.Label(g, "GEAR", new RectangleF(gear.X, gear.Bottom - 34, gear.Width, 18), StringAlignment.Center, 12);

                // Column D: fuel.
                double fuelLaps = t.FuelLapsLeft;
                Cell(g, MidCell(3, true), "FUEL " + _units.FuelSuffix, _units.FormatFuel(t.FuelLiters, false), DashKit.Text, 32, true);
                Cell(g, MidCell(3, false), "LAPS LEFT FUEL", fuelLaps < 0 ? "--.-" : fuelLaps.ToString("0.0"), DashKit.FuelLapsColor(fuelLaps), 32, true);

                // Column E: lap counter and brake bias.
                Cell(g, MidCell(4, true), "LAP", Fmt.Lap(t), DashKit.Text, 42);
                Cell(g, MidCell(4, false), "BIAS", t.BrakeBias > 0 ? t.BrakeBias.ToString("0.0") : "--", DashKit.Text, 42);

                // Delta: one bar across the bottom, from the middle. Faster grows
                // right in green, slower grows left in red; full scale is one second.
                var bar = new RectangleF(8, BarTop, 838, _height - BarTop - 8);
                DashKit.Panel(g, bar, 10);
                var inner = new RectangleF(bar.X + 4, bar.Y + 4, bar.Width - 8, bar.Height - 8);
                double delta = t.LapDeltaSeconds;
                float center = inner.X + inner.Width / 2;
                float length = (float)(Math.Min(1, Math.Abs(delta)) * inner.Width / 2);
                if (length > 0.5f)
                {
                    var fill = delta >= 0 ? new RectangleF(center, inner.Y, length, inner.Height) : new RectangleF(center - length, inner.Y, length, inner.Height);
                    DashKit.Fill(g, fill, delta >= 0 ? DashKit.Good : DashKit.Bad, Math.Min(7, length / 2));
                }
                using (var tick = new Pen(DashKit.Text, 3)) g.DrawLine(tick, center, inner.Y, center, inner.Bottom);
            }
            return bitmap;
        }

        private void Cell(Graphics g, RectangleF b, string label, string value, Color color, float size, bool centered = false)
        {
            DashKit.Panel(g, b);
            if (centered)
            {
                _kit.Draw(g, label, _kit.Value(12), _kit.Accent, new RectangleF(b.X, b.Y + 8, b.Width, 16));
                _kit.Draw(g, value, _kit.Value(size), color, new RectangleF(b.X, b.Y + 26, b.Width, b.Height - 32));
            }
            else
            {
                _kit.Label(g, label, new RectangleF(b.X + 14, b.Y + 8, b.Width - 28, 18));
                _kit.Draw(g, value, _kit.Value(size), color, new RectangleF(b.X + 14, b.Y + 24, b.Width - 28, b.Height - 28), StringAlignment.Far);
            }
        }

        private static RectangleF MidCell(int column, bool top)
        {
            float h = (MidH - Gap) / 2;
            float left = Cols[column] + (column == 0 ? 0 : Gap / 2);
            float right = Cols[column + 1] - (column == 4 ? 0 : Gap / 2);
            return new RectangleF(left, top ? MidTop : MidTop + h + Gap, right - left, h);
        }

        private static string TenthsLapTime(double seconds)
        {
            if (seconds <= 0 || double.IsNaN(seconds)) return "0:00.0";
            TimeSpan time = TimeSpan.FromSeconds(Math.Floor(seconds * 10) / 10);
            return ((int)time.TotalMinutes) + time.ToString(@"\:ss\.f");
        }

        public void Dispose()
        {
            _kit.Dispose();
            _flags.Dispose();
        }
    }
}
