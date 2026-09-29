using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace DV5Direct
{
    /// <summary>Renderers that draw the live pedal trace need a redraw on every trace sample.</summary>
    internal interface ILiveTraceRenderer { }

    /// <summary>
    /// Data-dense practice and racing dashboard: rivals with license and iRating,
    /// live delta, lap times, fuel, a scrolling pedal trace and the car's
    /// traction control, ABS and brake bias around a large gear.
    /// </summary>
    internal sealed class TelemetryRenderer : IDashboardRenderer, ILiveTraceRenderer
    {
        private static readonly Color Background = Color.FromArgb(4, 9, 14);
        private static readonly Color PanelFill = Color.FromArgb(235, 11, 19, 27);
        private static readonly Color PanelBorder = Color.FromArgb(38, 57, 72);
        private static readonly Color Muted = Color.FromArgb(150, 172, 190);
        private static readonly Color Track = Color.FromArgb(24, 37, 48);
        private static readonly Color Faster = Color.FromArgb(40, 225, 120);
        private static readonly Color Slower = Color.FromArgb(240, 55, 70);
        private static readonly Color Amber = Color.FromArgb(255, 196, 45);
        private static readonly Color ThrottleColor = Color.FromArgb(40, 225, 120);
        private static readonly Color BrakeColor = Color.FromArgb(240, 55, 70);

        private const float Left = 6, Center = 308, Right = 552, ColumnWidth = 296, CenterWidth = 238, Gap = 6;
        private readonly int _width, _height;
        private readonly Color _accent;
        private readonly UnitSystem _units;
        private readonly FlagPainter _flags;
        private readonly Font _label = Fmt.FontOf(13, FontStyle.Bold);
        private readonly Font _chip = Fmt.FontOf(13, FontStyle.Bold);
        private readonly Font _name = Fmt.FontOf(21, FontStyle.Bold);
        private readonly Font _number = Fmt.FontOf(20, FontStyle.Bold);
        private readonly Font _gap = Fmt.FontOf(27, FontStyle.Bold);
        private readonly Font _delta = Fmt.FontOf(46, FontStyle.Bold);
        private readonly Font _lapTime = Fmt.FontOf(40, FontStyle.Bold);
        private readonly Font _tile = Fmt.FontOf(36, FontStyle.Bold);
        private readonly Font _small = Fmt.FontOf(22, FontStyle.Bold);
        private readonly Font _rpm = Fmt.FontOf(26, FontStyle.Bold);
        private readonly Font _gear = Fmt.FontOf(150, FontStyle.Bold);
        private readonly Font _speed = Fmt.FontOf(38, FontStyle.Bold);
        private readonly StringFormat _center = Format(StringAlignment.Center);
        private readonly StringFormat _near = Format(StringAlignment.Near);
        private readonly StringFormat _far = Format(StringAlignment.Far);

        public TelemetryRenderer(int width, int height, DashboardSettings settings, UnitSystem units)
        {
            _width = width;
            _height = height;
            _accent = settings?.Accent ?? Color.FromArgb(45, 212, 191);
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

                DrawRelative(g, new RectangleF(Left, 6, ColumnWidth, 92), "BEHIND", t.BehindDetail, t.DriverBehind, t.RelativeBehindSeconds, Amber);
                DrawDelta(g, t, new RectangleF(Center, 6, CenterWidth, 92));
                DrawRelative(g, new RectangleF(Right, 6, ColumnWidth, 92), "AHEAD", t.AheadDetail, t.DriverAhead, t.RelativeAheadSeconds, Faster);

                DrawLeftColumn(g, t);
                DrawCenterColumn(g, t, shiftFlashOn);
                DrawRightColumn(g, t);
            }
            return bitmap;
        }

        // ---------------------------------------------------------------- top row

        private void DrawRelative(Graphics g, RectangleF b, string label, RelativeDriver driver, string fallbackName, double gap, Color tone)
        {
            DrawPanel(g, b, 12, PanelBorder);
            Text(g, label, _label, Muted, new RectangleF(b.X + 14, b.Y + 6, 100, 22), _near);

            // License and iRating chips, right-aligned in the header row.
            float chipRight = b.Right - 12;
            if (driver != null && driver.IRating > 0)
            {
                var chip = new RectangleF(chipRight - 50, b.Y + 8, 50, 20);
                Fill(g, chip, 5, Color.FromArgb(31, 43, 55));
                Text(g, FormatIRating(driver.IRating), _chip, Color.White, chip, _center);
                chipRight = chip.X - 5;
            }
            if (driver != null && !string.IsNullOrEmpty(driver.License))
            {
                var chip = new RectangleF(chipRight - 24, b.Y + 8, 24, 20);
                Fill(g, chip, 5, driver.LicenseColor);
                Text(g, driver.License, _chip, Color.White, chip, _center);
            }

            string number = driver?.CarNumber;
            float nameX = b.X + 14;
            if (!string.IsNullOrEmpty(number))
            {
                var box = new RectangleF(b.X + 12, b.Y + 34, 46, 34);
                Fill(g, box, 6, Color.FromArgb(236, 240, 245));
                Text(g, number, _number, Color.FromArgb(8, 14, 20), box, _center);
                nameX = box.Right + 10;
            }
            Text(g, driver?.Name ?? fallbackName, _name, Color.White, new RectangleF(nameX, b.Y + 32, b.Right - nameX - 92, 38), _near);
            bool hasCar = fallbackName != "---";
            Text(g, hasCar ? Fmt.Signed(gap) : "--.--", _gap, hasCar ? tone : Muted, new RectangleF(b.Right - 104, b.Y + 30, 92, 40), _far);

            using (var bar = new SolidBrush(hasCar ? tone : Track))
                g.FillRoundedRectangle(bar, new RectangleF(b.X + 12, b.Bottom - 14, b.Width - 24, 4), 2);
        }

        private void DrawDelta(Graphics g, TelemetrySnapshot t, RectangleF b)
        {
            double delta = t.LapDeltaSeconds;
            Color tone = delta > 0.005 ? Faster : delta < -0.005 ? Slower : _accent;
            using (GraphicsPath p = Fmt.Rounded(b, 12))
            {
                using (var fill = new LinearGradientBrush(b, Color.FromArgb(120, tone), Color.FromArgb(40, tone), 90f)) g.FillPath(fill, p);
                using (var pen = new Pen(tone, 2)) g.DrawPath(pen, p);
            }
            Text(g, "DELTA", _label, Color.FromArgb(220, 255, 255, 255), new RectangleF(b.X, b.Y + 6, b.Width, 22), _center);
            Text(g, Fmt.Signed(delta), _delta, Color.White, new RectangleF(b.X, b.Y + 24, b.Width, 64), _center);
        }

        // ---------------------------------------------------------------- left

        private void DrawLeftColumn(Graphics g, TelemetrySnapshot t)
        {
            var best = new RectangleF(Left, 104, ColumnWidth, 84);
            DrawPanel(g, best, 12, PanelBorder);
            Label(g, "BEST LAP", best);
            Text(g, Fmt.LapTime(t.BestLapSeconds), _lapTime, Color.White, Below(best), _center);

            var predicted = new RectangleF(Left, 194, ColumnWidth, 84);
            DrawPanel(g, predicted, 12, PanelBorder);
            Label(g, "PREDICTED", predicted);
            Color predictedTone = t.PredictedLapSeconds > 0 && t.BestLapSeconds > 0
                ? (t.PredictedLapSeconds <= t.BestLapSeconds ? Faster : Amber)
                : Color.White;
            Text(g, Fmt.LapTime(t.PredictedLapSeconds), _lapTime, predictedTone, Below(predicted), _center);

            float w = (ColumnWidth - Gap) / 2;
            double fuelLaps = t.FuelLapsLeft;
            Color lapsTone = fuelLaps < 0 ? Color.White : fuelLaps < 2 ? Slower : fuelLaps < 5 ? Amber : Color.White;
            Tile(g, new RectangleF(Left, 284, w, 92), "FUEL " + _units.FuelSuffix, _units.FormatFuel(t.FuelLiters, false), Color.White);
            Tile(g, new RectangleF(Left + w + Gap, 284, w, 92), "AVG / LAP", _units.FormatFuel(t.FuelPerLapAverage, false, true), Color.White);
            Tile(g, new RectangleF(Left, 382, w, 92), "LAPS LEFT", fuelLaps < 0 ? "--.-" : fuelLaps.ToString("0.0"), lapsTone);
            Tile(g, new RectangleF(Left + w + Gap, 382, w, 92), "LAST LAP", _units.FormatFuel(t.FuelLastLap, false, true), Color.White);
        }

        // ---------------------------------------------------------------- center

        private void DrawCenterColumn(Graphics g, TelemetrySnapshot t, bool shiftFlashOn)
        {
            // RPM strip doubles as a shift bar.
            var rpm = new RectangleF(Center, 104, CenterWidth, 40);
            DrawPanel(g, rpm, 10, shiftFlashOn ? Slower : PanelBorder);
            float fill = (float)Math.Max(0, Math.Min(1, t.ShiftIndicatorPct));
            if (fill > 0)
            {
                Color bar = shiftFlashOn ? Slower : fill > 0.85 ? Slower : fill > 0.6 ? Amber : _accent;
                using (var brush = new SolidBrush(Color.FromArgb(shiftFlashOn ? 200 : 90, bar)))
                    g.FillRoundedRectangle(brush, new RectangleF(rpm.X + 3, rpm.Y + 3, Math.Max(10, (rpm.Width - 6) * fill), rpm.Height - 6), 8);
            }
            Text(g, t.EngineRpm > 0 ? t.EngineRpm.ToString("0") : "----", _rpm, Color.White, new RectangleF(rpm.X, rpm.Y, rpm.Width - 40, rpm.Height), _center);
            Text(g, "RPM", _label, Muted, new RectangleF(rpm.Right - 52, rpm.Y, 44, rpm.Height), _far);

            var gearBox = new RectangleF(Center, 150, CenterWidth, 178);
            DrawPanel(g, gearBox, 14, shiftFlashOn ? Slower : _accent, 3);
            Text(g, t.Connected ? Fmt.Gear(t.Gear) : "–", _gear, shiftFlashOn ? Slower : Color.White,
                new RectangleF(gearBox.X, gearBox.Y - 6, gearBox.Width, gearBox.Height + 6), _center);

            // Lap minimum speed, live speed, lap maximum speed.
            var min = new RectangleF(Center, 334, 54, 52);
            var max = new RectangleF(Center + CenterWidth - 54, 334, 54, 52);
            Fill(g, min, 8, Color.FromArgb(70, Slower));
            Fill(g, max, 8, Color.FromArgb(70, Faster));
            Text(g, "MIN", _label, Muted, new RectangleF(min.X, min.Y + 2, min.Width, 16), _center);
            Text(g, "MAX", _label, Muted, new RectangleF(max.X, max.Y + 2, max.Width, 16), _center);
            Text(g, t.Connected ? _units.Speed(t.LapMinSpeedMph).ToString("0") : "--", _small, Color.White, new RectangleF(min.X, min.Y + 16, min.Width, 34), _center);
            Text(g, t.Connected ? _units.Speed(t.LapMaxSpeedMph).ToString("0") : "--", _small, Color.White, new RectangleF(max.X, max.Y + 16, max.Width, 34), _center);
            Text(g, _units.FormatSpeed(t), _speed, Color.White, new RectangleF(min.Right, 330, max.X - min.Right, 42), _center);
            Text(g, _units.SpeedLabel, _label, Muted, new RectangleF(min.Right, 368, max.X - min.Right, 18), _center);

            float half = (CenterWidth - Gap) / 2;
            var pos = new RectangleF(Center, 392, half, 38);
            var lap = new RectangleF(Center + half + Gap, 392, half, 38);
            DrawPanel(g, pos, 10, PanelBorder);
            DrawPanel(g, lap, 10, PanelBorder);
            Text(g, "POS", _label, Muted, new RectangleF(pos.X + 10, pos.Y, 40, pos.Height), _near);
            Text(g, t.Position > 0 ? "P" + t.Position : "P--", _small, Color.White, new RectangleF(pos.X, pos.Y, pos.Width - 10, pos.Height), _far);
            Text(g, "LAP", _label, Muted, new RectangleF(lap.X + 10, lap.Y, 40, lap.Height), _near);
            Text(g, Fmt.Lap(t), _small, Color.White, new RectangleF(lap.X, lap.Y, lap.Width - 10, lap.Height), _far);

            var session = new RectangleF(Center, 436, CenterWidth, 38);
            DrawPanel(g, session, 10, PanelBorder);
            string kind = t.ProgressMode == SessionProgressMode.Practice ? "PRACTICE" : "RACE";
            string remaining =
                t.ProgressMode == SessionProgressMode.LapRace && t.TotalLaps > 0 ? Math.Max(0, t.TotalLaps - t.LapsCompleted) + " TO GO" :
                t.ProgressMode != SessionProgressMode.None && t.ProgressMode != SessionProgressMode.LapRace ? Fmt.Remaining(t.SessionSecondsRemaining) : "";
            Text(g, kind, _label, _accent, new RectangleF(session.X + 12, session.Y, 100, session.Height), _near);
            Text(g, remaining, _small, Color.White, new RectangleF(session.X, session.Y, session.Width - 12, session.Height), _far);
        }

        // ---------------------------------------------------------------- right

        private void DrawRightColumn(Graphics g, TelemetrySnapshot t)
        {
            float half = (ColumnWidth - Gap) / 2;
            var inc = new RectangleF(Right, 104, half, 40);
            var ir = new RectangleF(Right + half + Gap, 104, half, 40);
            DrawPanel(g, inc, 10, PanelBorder);
            DrawPanel(g, ir, 10, PanelBorder);
            Text(g, "INCIDENTS", _label, Muted, new RectangleF(inc.X + 12, inc.Y, 90, inc.Height), _near);
            Text(g, t.IncidentPoints + "x", _small, t.IncidentPoints >= 8 ? Amber : Color.White, new RectangleF(inc.X, inc.Y, inc.Width - 12, inc.Height), _far);
            Text(g, "IRATING", _label, Muted, new RectangleF(ir.X + 12, ir.Y, 90, ir.Height), _near);
            Text(g, t.PlayerIRating > 0 ? FormatIRating(t.PlayerIRating) : "--", _small, Color.White, new RectangleF(ir.X, ir.Y, ir.Width - 12, ir.Height), _far);

            DrawTrace(g, t, new RectangleF(Right, 150, ColumnWidth, 172));

            // Live pedal readout under the trace.
            var thr = new RectangleF(Right, 328, half, 38);
            var brk = new RectangleF(Right + half + Gap, 328, half, 38);
            PedalChip(g, thr, "THROTTLE", t.Throttle, ThrottleColor);
            PedalChip(g, brk, "BRAKE", t.Brake, BrakeColor);

            float w = (ColumnWidth - Gap * 2) / 3;
            Tile(g, new RectangleF(Right, 372, w, 102), "TC", Setting(t.TractionControl), Color.White);
            Tile(g, new RectangleF(Right + w + Gap, 372, w, 102), "ABS", Setting(t.Abs), Color.White);
            Tile(g, new RectangleF(Right + (w + Gap) * 2, 372, w, 102), "BIAS", t.BrakeBias > 0 ? t.BrakeBias.ToString("0.0") : "--", Color.White);
        }

        private void DrawTrace(Graphics g, TelemetrySnapshot t, RectangleF b)
        {
            DrawPanel(g, b, 12, PanelBorder);
            var plot = new RectangleF(b.X + 12, b.Y + 14, b.Width - 24, b.Height - 28);
            using (var grid = new Pen(Color.FromArgb(28, 42, 54), 1))
                for (int i = 0; i <= 4; i++)
                {
                    float y = plot.Y + plot.Height * i / 4;
                    g.DrawLine(grid, plot.X, y, plot.Right, y);
                }
            DrawLine(g, t.BrakeTrace, plot, BrakeColor);
            DrawLine(g, t.ThrottleTrace, plot, ThrottleColor);
        }

        private static void DrawLine(Graphics g, float[] samples, RectangleF plot, Color color)
        {
            if (samples == null || samples.Length < 2) return;
            var points = new PointF[samples.Length];
            for (int i = 0; i < samples.Length; i++)
                points[i] = new PointF(
                    plot.X + plot.Width * i / (samples.Length - 1),
                    plot.Bottom - plot.Height * Math.Max(0, Math.Min(1, samples[i])));
            using (var glow = new Pen(Color.FromArgb(50, color), 7) { LineJoin = LineJoin.Round }) g.DrawLines(glow, points);
            using (var pen = new Pen(color, 3) { LineJoin = LineJoin.Round }) g.DrawLines(pen, points);
            PointF last = points[points.Length - 1];
            using (var dot = new SolidBrush(color)) g.FillEllipse(dot, last.X - 4, last.Y - 4, 8, 8);
        }

        private void PedalChip(Graphics g, RectangleF b, string label, double value, Color tone)
        {
            DrawPanel(g, b, 10, PanelBorder);
            float pct = (float)Math.Max(0, Math.Min(1, value));
            if (pct > 0.01)
                using (var fill = new SolidBrush(Color.FromArgb(70, tone)))
                    g.FillRoundedRectangle(fill, new RectangleF(b.X + 3, b.Y + 3, Math.Max(8, (b.Width - 6) * pct), b.Height - 6), 7);
            Text(g, label, _label, Muted, new RectangleF(b.X + 12, b.Y, 90, b.Height), _near);
            Text(g, (pct * 100).ToString("0") + "%", _small, Color.White, new RectangleF(b.X, b.Y, b.Width - 12, b.Height), _far);
        }

        // ---------------------------------------------------------------- helpers

        private void Tile(Graphics g, RectangleF b, string label, string value, Color tone)
        {
            DrawPanel(g, b, 12, PanelBorder);
            Label(g, label, b);
            Text(g, value, _tile, tone, Below(b), _center);
        }

        private void Label(Graphics g, string text, RectangleF panel) =>
            Text(g, text, _label, Muted, new RectangleF(panel.X, panel.Y + 6, panel.Width, 20), _center);

        private static RectangleF Below(RectangleF panel) => new RectangleF(panel.X, panel.Y + 24, panel.Width, panel.Height - 26);

        private static string Setting(double value) => value < 0 ? "--" : value.ToString(value % 1 == 0 ? "0" : "0.0");

        private static string FormatIRating(int rating) => rating >= 1000 ? (rating / 1000.0).ToString("0.0") + "K" : rating.ToString();

        private static void DrawPanel(Graphics g, RectangleF b, float radius, Color border, float thickness = 2)
        {
            using (GraphicsPath p = Fmt.Rounded(b, radius))
            using (var fill = new SolidBrush(PanelFill))
            using (var pen = new Pen(border, thickness))
            {
                g.FillPath(fill, p);
                g.DrawPath(pen, p);
            }
        }

        private static void Fill(Graphics g, RectangleF b, float radius, Color color)
        {
            using (var brush = new SolidBrush(color)) g.FillRoundedRectangle(brush, b, radius);
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
            foreach (Font font in new[] { _label, _chip, _name, _number, _gap, _delta, _lapTime, _tile, _small, _rpm, _gear, _speed })
                font.Dispose();
            _center.Dispose();
            _near.Dispose();
            _far.Dispose();
            _flags.Dispose();
        }
    }
}
