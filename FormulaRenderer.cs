using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;

namespace DV5Direct
{
    /// <summary>
    /// Formula-style steering wheel display: a strict grid of labeled cells,
    /// big gear, and a full-width delta bar. Shift lights are left to the DV-5 LEDs.
    /// </summary>
    internal sealed class FormulaRenderer : IDashboardRenderer
    {
        private static readonly Color Background = Color.FromArgb(4, 5, 7);
        private static readonly Color GridLine = Color.FromArgb(46, 52, 60);
        private static readonly Color Label = Color.FromArgb(215, 222, 230);
        private static readonly Color Faster = Color.FromArgb(40, 215, 110);
        private static readonly Color Slower = Color.FromArgb(235, 50, 60);
        private static readonly Color Amber = Color.FromArgb(255, 196, 45);
        private static readonly Color PositionBlock = Color.FromArgb(226, 182, 16);

        // Row and column edges.
        private const float LedBottom = 0, TopBottom = 104, MidSplit = 246, MidBottom = 392;
        private static readonly float[] Columns = { 0, 196, 336, 518, 658, 854 };

        private readonly int _width, _height;
        private readonly Color _accent;
        private readonly UnitSystem _units;
        private readonly FlagPainter _flags;
        private readonly Font _small, _label, _name, _value, _lap, _rpm, _position, _gear, _delta;
        private readonly StringFormat _center = Format(StringAlignment.Center);
        private readonly StringFormat _near = Format(StringAlignment.Near);
        private readonly StringFormat _far = Format(StringAlignment.Far);

        public FormulaRenderer(int width, int height, DashboardSettings settings, UnitSystem units)
        {
            _width = width;
            _height = height;
            _accent = settings?.Accent ?? Color.FromArgb(56, 189, 248);
            _units = units ?? UnitSystem.Default;
            _flags = new FlagPainter(width, height);
            // Bahnschrift (DIN-style, ships with Windows 10+) gives the motorsport look;
            // fall back to Segoe UI elsewhere.
            string numbers = HasFont("Bahnschrift") ? "Bahnschrift" : "Segoe UI";
            _small = new Font(numbers, 13, FontStyle.Bold, GraphicsUnit.Pixel);
            _label = new Font(numbers, 15, FontStyle.Bold, GraphicsUnit.Pixel);
            _name = new Font(numbers, 21, FontStyle.Bold, GraphicsUnit.Pixel);
            _value = new Font(numbers, 36, FontStyle.Bold, GraphicsUnit.Pixel);
            _lap = new Font(numbers, 42, FontStyle.Bold, GraphicsUnit.Pixel);
            _rpm = new Font(numbers, 50, FontStyle.Bold, GraphicsUnit.Pixel);
            _position = new Font(numbers, 44, FontStyle.Bold, GraphicsUnit.Pixel);
            _gear = new Font(numbers, 170, FontStyle.Bold, GraphicsUnit.Pixel);
            _delta = new Font(numbers, 40, FontStyle.Bold, GraphicsUnit.Pixel);
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
                DrawTopRow(g, t);
                DrawMiddle(g, t, shiftFlashOn);
                DrawDeltaBar(g, t);
                DrawGrid(g);
            }
            return bitmap;
        }

        private void DrawTopRow(Graphics g, TelemetrySnapshot t)
        {
            var lap = new RectangleF(0, LedBottom, 300, TopBottom - LedBottom);
            var rpm = new RectangleF(300, LedBottom, 254, TopBottom - LedBottom);
            var inc = new RectangleF(554, LedBottom, 300, TopBottom - LedBottom);

            Text(g, "CURRENT LAP", _label, Label, new RectangleF(lap.X + 20, lap.Y + 8, 200, 22), _near);
            Text(g, TenthsLapTime(t.CurrentLapSeconds), _lap, Color.White, new RectangleF(lap.X + 16, lap.Y + 28, lap.Width - 36, 60), _far);

            Text(g, t.EngineRpm > 0 ? t.EngineRpm.ToString("0") : "----", _rpm, Color.White, new RectangleF(rpm.X, rpm.Y + 4, rpm.Width, 62), _center);
            Text(g, "RPM", _small, Label, new RectangleF(rpm.X, rpm.Y + 64, rpm.Width, 20), _center);

            Text(g, "INCIDENTS", _label, Label, new RectangleF(inc.X + 20, inc.Y + 8, 200, 22), _near);
            Text(g, t.IncidentPoints + "x", _lap, t.IncidentPoints >= 8 ? Amber : Color.White, new RectangleF(inc.X + 16, inc.Y + 28, inc.Width - 36, 60), _far);
        }

        private void DrawMiddle(Graphics g, TelemetrySnapshot t, bool shiftFlashOn)
        {
            // Column A: position block over speed.
            var pos = Cell(0, true);
            using (var block = new SolidBrush(PositionBlock)) g.FillRectangle(block, pos);
            Text(g, "POSITION", _small, Color.FromArgb(60, 45, 0), new RectangleF(pos.X + 16, pos.Y + 10, pos.Width - 32, 18), _near);
            string place = t.Position > 0 ? "P" + t.Position : "P--";
            Text(g, place, _position, Color.FromArgb(20, 16, 0), new RectangleF(pos.X + 12, pos.Y + 28, pos.Width - 24, 70), _near);
            if (t.TotalStarters > 0)
                Text(g, "/" + t.TotalStarters, _name, Color.FromArgb(70, 55, 0), new RectangleF(pos.X + 12, pos.Y + 40, pos.Width - 26, 60), _far);

            var speed = Cell(0, false);
            Text(g, _units.SpeedLabel, _label, Label, new RectangleF(speed.X + 20, speed.Y + 10, 100, 22), _near);
            Text(g, _units.FormatSpeed(t), _lap, Color.White, new RectangleF(speed.X + 16, speed.Y + 34, speed.Width - 36, 70), _far);

            // Column B: gaps to the cars around you.
            bool ahead = t.DriverAhead != "---", behind = t.DriverBehind != "---";
            Metric(g, Cell(1, true), "AHEAD", ahead ? Fmt.Signed(t.RelativeAheadSeconds) : "--.--", ahead ? Faster : Color.White, true);
            Metric(g, Cell(1, false), "BEHIND", behind ? Fmt.Signed(t.RelativeBehindSeconds) : "--.--", behind ? Amber : Color.White, true);

            // Column C: gear across both rows.
            var gear = new RectangleF(Columns[2], TopBottom, Columns[3] - Columns[2], MidBottom - TopBottom);
            Text(g, t.Connected ? Fmt.Gear(t.Gear) : "–", _gear, shiftFlashOn ? Slower : Color.White,
                new RectangleF(gear.X, gear.Y + 14, gear.Width, gear.Height - 50), _center);
            Text(g, "GEAR", _small, Label, new RectangleF(gear.X, gear.Bottom - 30, gear.Width, 20), _center);

            // Column D: fuel.
            double fuelLaps = t.FuelLapsLeft;
            Color lapsColor = fuelLaps < 0 ? Color.White : fuelLaps < 2 ? Slower : fuelLaps < 5 ? Amber : Color.White;
            Metric(g, Cell(3, true), "FUEL " + _units.FuelSuffix, _units.FormatFuel(t.FuelLiters, false), Color.White, true);
            Metric(g, Cell(3, false), "LAPS LEFT FUEL", fuelLaps < 0 ? "--.-" : fuelLaps.ToString("0.0"), lapsColor, true);

            // Column E: lap counter over brake bias.
            var lap = Cell(4, true);
            Text(g, "LAP", _label, Label, new RectangleF(lap.X + 16, lap.Y + 10, 100, 22), _near);
            Text(g, Fmt.Lap(t), _lap, Color.White, new RectangleF(lap.X + 16, lap.Y + 34, lap.Width - 36, 70), _far);
            var bias = Cell(4, false);
            Text(g, "BIAS", _label, Label, new RectangleF(bias.X + 16, bias.Y + 10, 100, 22), _near);
            Text(g, t.BrakeBias > 0 ? t.BrakeBias.ToString("0.0") : "--", _lap, Color.White, new RectangleF(bias.X + 16, bias.Y + 34, bias.Width - 36, 70), _far);
        }

        private void Metric(Graphics g, RectangleF cell, string label, string value, Color color, bool accentLabel)
        {
            Text(g, label, _small, accentLabel ? _accent : Label, new RectangleF(cell.X, cell.Y + 10, cell.Width, 18), _center);
            Text(g, value, _value, color, new RectangleF(cell.X, cell.Y + 32, cell.Width, cell.Height - 40), _center);
        }

        private void DrawDeltaBar(Graphics g, TelemetrySnapshot t)
        {
            // One bar across the whole bottom, starting in the middle: faster grows
            // right in green, slower grows left in red. Full scale is one second.
            var bar = new RectangleF(0, MidBottom, _width, _height - MidBottom);
            using (var track = new SolidBrush(Color.FromArgb(18, 22, 27))) g.FillRectangle(track, bar);
            double delta = t.LapDeltaSeconds;
            float center = bar.Width / 2;
            float length = (float)(Math.Min(1, Math.Abs(delta)) * center);
            if (length > 0.5f)
                using (var fill = new SolidBrush(delta >= 0 ? Faster : Slower))
                    g.FillRectangle(fill, delta >= 0 ? center : center - length, bar.Y, length, bar.Height);
            using (var tick = new Pen(Color.White, 3)) g.DrawLine(tick, center, bar.Y, center, bar.Bottom);
        }

        private void DrawGrid(Graphics g)
        {
            using (var pen = new Pen(GridLine, 2))
            {
                g.DrawLine(pen, 0, LedBottom, _width, LedBottom);
                g.DrawLine(pen, 0, TopBottom, _width, TopBottom);
                g.DrawLine(pen, 0, MidBottom, _width, MidBottom);
                g.DrawLine(pen, 300, LedBottom, 300, TopBottom);
                g.DrawLine(pen, 554, LedBottom, 554, TopBottom);
                foreach (float x in Columns.Skip(1).Take(4)) g.DrawLine(pen, x, TopBottom, x, MidBottom);
                g.DrawLine(pen, Columns[0], MidSplit, Columns[2], MidSplit);
                g.DrawLine(pen, Columns[3], MidSplit, Columns[5], MidSplit);
                g.DrawRectangle(pen, 1, LedBottom, _width - 2, _height - LedBottom - 1);
            }
        }

        private static RectangleF Cell(int column, bool top) => new RectangleF(
            Columns[column], top ? TopBottom : MidSplit,
            Columns[column + 1] - Columns[column], top ? MidSplit - TopBottom : MidBottom - MidSplit);

        private static string TenthsLapTime(double seconds)
        {
            if (seconds <= 0 || double.IsNaN(seconds)) return "0:00.0";
            TimeSpan time = TimeSpan.FromSeconds(Math.Floor(seconds * 10) / 10);
            return ((int)time.TotalMinutes) + time.ToString(@"\:ss\.f");
        }

        private static bool HasFont(string name)
        {
            using (var fonts = new InstalledFontCollection())
                return fonts.Families.Any(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        private static void Text(Graphics g, string text, Font font, Color color, RectangleF bounds, StringFormat format)
        {
            using (var brush = new SolidBrush(color)) g.DrawString(text ?? "--", font, brush, bounds, format);
        }

        private static StringFormat Format(StringAlignment alignment) => new StringFormat
        {
            Alignment = alignment,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap,
            Trimming = StringTrimming.EllipsisCharacter
        };

        public void Dispose()
        {
            foreach (Font font in new[] { _small, _label, _name, _value, _lap, _rpm, _position, _gear, _delta }) font.Dispose();
            _center.Dispose();
            _near.Dispose();
            _far.Dispose();
            _flags.Dispose();
        }
    }
}
