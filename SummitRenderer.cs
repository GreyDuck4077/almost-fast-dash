using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace DV5Direct
{
    /// <summary>
    /// Flagship dashboard. Built around what a driver actually glances at:
    /// the shift arc and gear, a live relative of the four nearest cars with lap
    /// status, the delta and its trend, lap times, fuel to the finish, and car
    /// settings. On pit road the arc becomes a pit-speed gauge.
    /// </summary>
    internal sealed class SummitRenderer : IDashboardRenderer, ILiveTraceRenderer
    {
        private static readonly Color Background = Color.FromArgb(3, 7, 11);
        private static readonly Color PanelFill = Color.FromArgb(238, 10, 17, 24);
        private static readonly Color PanelBorder = Color.FromArgb(34, 50, 64);
        private static readonly Color Muted = Color.FromArgb(142, 164, 182);
        private static readonly Color Dim = Color.FromArgb(92, 110, 126);
        private static readonly Color Track = Color.FromArgb(22, 33, 43);
        private static readonly Color Faster = Color.FromArgb(40, 225, 120);
        private static readonly Color Slower = Color.FromArgb(240, 55, 70);
        private static readonly Color Amber = Color.FromArgb(255, 196, 45);
        private static readonly Color LapUp = Color.FromArgb(255, 110, 90);
        private static readonly Color LapDown = Color.FromArgb(90, 160, 255);

        private readonly int _width, _height;
        private readonly Color _accent;
        private readonly UnitSystem _units;
        private readonly FlagPainter _flags;
        private readonly Font _tiny = Fmt.FontOf(11, FontStyle.Bold);
        private readonly Font _label = Fmt.FontOf(13, FontStyle.Bold);
        private readonly Font _row = Fmt.FontOf(17, FontStyle.Bold);
        private readonly Font _rowGap = Fmt.FontOf(19, FontStyle.Bold);
        private readonly Font _bar = Fmt.FontOf(16, FontStyle.Bold);
        private readonly Font _value = Fmt.FontOf(26, FontStyle.Bold);
        private readonly Font _big = Fmt.FontOf(32, FontStyle.Bold);
        private readonly Font _delta = Fmt.FontOf(50, FontStyle.Bold);
        private readonly Font _gear = Fmt.FontOf(132, FontStyle.Bold);
        private readonly Font _speed = Fmt.FontOf(34, FontStyle.Bold);
        private readonly Font _pit = Fmt.FontOf(22, FontStyle.Bold);
        private readonly StringFormat _center = Format(StringAlignment.Center);
        private readonly StringFormat _near = Format(StringAlignment.Near);
        private readonly StringFormat _far = Format(StringAlignment.Far);

        public SummitRenderer(int width, int height, DashboardSettings settings, UnitSystem units)
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
                // Soft accent glow behind the gauge.
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(227, 20, 400, 400);
                    using (var glow = new PathGradientBrush(path) { CenterColor = Color.FromArgb(34, _accent), SurroundColors = new[] { Color.FromArgb(0, _accent) } })
                        g.FillEllipse(glow, 227, 20, 400, 400);
                }

                DrawStatusBar(g, t);
                DrawRelative(g, t, new RectangleF(6, 42, 262, 330));
                if (t.OnPitRoad) DrawPitGauge(g, t); else DrawShiftGauge(g, t, shiftFlashOn, previousGear, gearTransitionProgress);
                DrawDelta(g, t, new RectangleF(586, 42, 262, 158));
                DrawLapTimes(g, t, new RectangleF(586, 206, 262, 166));
                DrawFuel(g, t, new RectangleF(6, 378, 552, 96));
                DrawCar(g, t, new RectangleF(564, 378, 284, 96));
            }
            return bitmap;
        }

        // ------------------------------------------------------------ status bar

        private void DrawStatusBar(Graphics g, TelemetrySnapshot t)
        {
            var bar = new RectangleF(6, 6, _width - 12, 30);
            Fill(g, bar, 8, Color.FromArgb(200, 12, 20, 28));
            using (var line = new Pen(Color.FromArgb(120, _accent), 2)) g.DrawLine(line, bar.X + 12, bar.Bottom, bar.Right - 12, bar.Bottom);

            string kind = t.ProgressMode == SessionProgressMode.Practice ? "PRACTICE" : "RACE";
            string remaining =
                t.ProgressMode == SessionProgressMode.LapRace && t.TotalLaps > 0 ? Math.Max(0, t.TotalLaps - t.LapsCompleted) + " LAPS TO GO" :
                t.ProgressMode == SessionProgressMode.None ? "" : Fmt.Remaining(t.SessionSecondsRemaining);
            float x = bar.X + 12;
            x = Segment(g, x, bar, kind, _accent, _bar);
            x = Segment(g, x + 8, bar, remaining, Color.White, _bar);

            // Right side, drawn right-to-left.
            float right = bar.Right - 12;
            right = SegmentRight(g, right, bar, "LAP " + Fmt.Lap(t), Color.White);
            if (!double.IsNaN(t.TrackTempC)) right = SegmentRight(g, right - 18, bar, "TRACK " + Temperature(t.TrackTempC), Muted);
            if (!double.IsNaN(t.AirTempC)) right = SegmentRight(g, right - 18, bar, "AIR " + Temperature(t.AirTempC), Muted);
            if (t.EngineWarning) right = SegmentRight(g, right - 18, bar, "ENGINE", Slower);

            // Position and incidents centered over the gauge.
            string pos = t.Position > 0 ? "P" + t.Position + (t.TotalStarters > 0 ? " / " + t.TotalStarters : "") : "P--";
            if (t.ClassPosition > 0 && t.ClassPosition != t.Position) pos += "   CLASS P" + t.ClassPosition;
            Text(g, pos + "   ·   " + t.IncidentPoints + "x", _bar, Color.White, new RectangleF(274, bar.Y, 306, bar.Height), _center);
        }

        private float Segment(Graphics g, float x, RectangleF bar, string text, Color color, Font font)
        {
            if (string.IsNullOrEmpty(text)) return x;
            float w = g.MeasureString(text, font).Width;
            Text(g, text, font, color, new RectangleF(x, bar.Y, w + 4, bar.Height), _near);
            return x + w;
        }

        private float SegmentRight(Graphics g, float right, RectangleF bar, string text, Color color)
        {
            float w = g.MeasureString(text, _bar).Width;
            Text(g, text, _bar, color, new RectangleF(right - w - 4, bar.Y, w + 4, bar.Height), _far);
            return right - w;
        }

        private string Temperature(double celsius) =>
            _units.Metric ? celsius.ToString("0") + "°" : (celsius * 9 / 5 + 32).ToString("0") + "°";

        // ------------------------------------------------------------ relative

        private void DrawRelative(Graphics g, TelemetrySnapshot t, RectangleF b)
        {
            DrawPanel(g, b, 14, PanelBorder);
            Text(g, "RELATIVE", _label, Muted, new RectangleF(b.X + 14, b.Y + 6, 120, 22), _near);
            RelativeEntry[] rows = t.Relatives ?? new RelativeEntry[0];
            if (rows.Length == 0)
            {
                Text(g, "Waiting for cars", _label, Dim, new RectangleF(b.X, b.Y + 40, b.Width, b.Height - 60), _center);
                return;
            }

            // The player always sits in the middle slot.
            int playerIndex = Array.FindIndex(rows, r => r.IsPlayer);
            const int slots = 5;
            float rowH = (b.Height - 38) / slots;
            for (int slot = 0; slot < slots; slot++)
            {
                int index = playerIndex + slot - 2;
                if (index < 0 || index >= rows.Length) continue;
                DrawRelativeRow(g, rows[index], new RectangleF(b.X + 6, b.Y + 32 + slot * rowH, b.Width - 12, rowH - 4));
            }
        }

        private void DrawRelativeRow(Graphics g, RelativeEntry r, RectangleF b)
        {
            if (r.IsPlayer)
            {
                Fill(g, b, 8, Color.FromArgb(46, _accent));
                using (var edge = new SolidBrush(_accent)) g.FillRoundedRectangle(edge, new RectangleF(b.X, b.Y + 8, 3, b.Height - 16), 1.5f);
            }
            Color nameColor = r.IsPlayer ? Color.White : r.LapRelation > 0 ? LapUp : r.LapRelation < 0 ? LapDown : Color.FromArgb(225, 232, 240);
            if (r.InPits) nameColor = Dim;

            var pos = new RectangleF(b.X + 8, b.Y + (b.Height - 24) / 2, 30, 24);
            Fill(g, pos, 6, r.IsPlayer ? _accent : Color.FromArgb(30, 42, 54));
            Text(g, r.Position > 0 ? r.Position.ToString() : "-", _label, r.IsPlayer ? Color.FromArgb(3, 20, 20) : Color.White, pos, _center);

            var number = new RectangleF(pos.Right + 6, pos.Y, 34, 24);
            using (var pen = new Pen(Color.FromArgb(60, 78, 94), 1.5f))
            using (GraphicsPath p = Fmt.Rounded(number, 6)) g.DrawPath(pen, p);
            Text(g, r.CarNumber ?? "", _tiny, Muted, number, _center);

            float nameX = number.Right + 8;
            Text(g, r.Name, _row, nameColor, new RectangleF(nameX, b.Y + 2, b.Right - nameX - 64, b.Height * 0.56f), _near);
            string meta = (r.InPits ? "PIT   " : "") + (r.License ?? "") + (r.IRating > 0 ? "  " + (r.IRating / 1000.0).ToString("0.0") + "K" : "");
            if (!string.IsNullOrEmpty(r.License))
                using (var dot = new SolidBrush(r.LicenseColor)) g.FillEllipse(dot, nameX, b.Y + b.Height * 0.66f, 7, 7);
            Text(g, meta, _tiny, Dim, new RectangleF(nameX + 11, b.Y + b.Height * 0.52f, b.Right - nameX - 70, b.Height * 0.4f), _near);

            string gap = r.IsPlayer ? "" : Math.Abs(r.Gap).ToString("0.0");
            Color gapColor = r.Gap < 0 ? Faster : Amber;
            Text(g, gap, _rowGap, r.IsPlayer ? Color.White : gapColor, new RectangleF(b.Right - 70, b.Y, 62, b.Height), _far);
        }

        // ------------------------------------------------------------ gauge

        private const float GaugeCx = 427, GaugeCy = 212, GaugeR = 142, StartAngle = 145, Sweep = 250;

        private void DrawShiftGauge(Graphics g, TelemetrySnapshot t, bool shiftFlashOn, int previousGear, float progress)
        {
            const int segments = 34;
            double pct = Math.Max(0, Math.Min(1, t.ShiftIndicatorPct));
            int lit = (int)Math.Ceiling(pct * segments);
            for (int i = 0; i < segments; i++)
            {
                double at = (i + 0.5) / segments;
                Color color = shiftFlashOn ? Slower : at > 0.86 ? Slower : at > 0.66 ? Amber : _accent;
                ArcSegment(g, i, segments, i < lit ? color : Track, i < lit);
            }
            // Redline hash marks.
            using (var redline = new Pen(Color.FromArgb(160, Slower), 3))
                g.DrawArc(redline, GaugeCx - GaugeR - 18, GaugeCy - GaugeR - 18, (GaugeR + 18) * 2, (GaugeR + 18) * 2, StartAngle + Sweep * 0.86f, Sweep * 0.14f);

            Text(g, t.EngineRpm > 0 ? t.EngineRpm.ToString("0") + " RPM" : "RPM", _label, Muted,
                new RectangleF(GaugeCx - 80, GaugeCy - 96, 160, 22), _center);

            Color gearColor = shiftFlashOn ? Slower : Color.White;
            bool animating = previousGear != t.Gear && progress >= 0 && progress < 1;
            if (animating)
            {
                float direction = t.Gear > previousGear ? -1 : 1;
                using (var oldBrush = new SolidBrush(Color.FromArgb((int)(255 * (1 - progress)), gearColor)))
                using (var newBrush = new SolidBrush(Color.FromArgb((int)(255 * progress), gearColor)))
                {
                    g.DrawString(Fmt.Gear(previousGear), _gear, oldBrush, new RectangleF(GaugeCx - 100, GaugeCy - 90 + direction * -40 * progress, 200, 160), _center);
                    g.DrawString(Fmt.Gear(t.Gear), _gear, newBrush, new RectangleF(GaugeCx - 100, GaugeCy - 90 + direction * 40 * (1 - progress), 200, 160), _center);
                }
            }
            else
            {
                Text(g, t.Connected ? Fmt.Gear(t.Gear) : "–", _gear, gearColor, new RectangleF(GaugeCx - 100, GaugeCy - 90, 200, 160), _center);
            }

            Text(g, _units.FormatSpeed(t), _speed, Color.White, new RectangleF(GaugeCx - 80, GaugeCy + 70, 160, 42), _center);
            Text(g, _units.SpeedLabel, _tiny, Muted, new RectangleF(GaugeCx - 80, GaugeCy + 108, 160, 16), _center);
            if (t.PitLimiter) Text(g, "LIMITER", _label, Amber, new RectangleF(GaugeCx - 80, GaugeCy + 126, 160, 20), _center);
        }

        private void DrawPitGauge(Graphics g, TelemetrySnapshot t)
        {
            // Arc fills to the pit speed limit at 80%, so going over is obvious.
            double limit = t.PitSpeedLimitMph;
            double ratio = limit > 0 ? t.SpeedMph / (limit * 1.25) : 0;
            bool over = limit > 0 && t.SpeedMph > limit + 0.5;
            const int segments = 34;
            int lit = (int)Math.Ceiling(Math.Max(0, Math.Min(1, ratio)) * segments);
            for (int i = 0; i < segments; i++)
            {
                bool beyondLimit = (i + 0.5) / segments > 0.8;
                Color color = over ? Slower : beyondLimit ? Slower : Faster;
                ArcSegment(g, i, segments, i < lit ? color : Track, i < lit);
            }

            Color state = over ? Slower : t.PitLimiter ? Faster : Amber;
            var banner = new RectangleF(GaugeCx - 92, GaugeCy - 100, 184, 34);
            Fill(g, banner, 8, Color.FromArgb(60, state));
            Text(g, t.PitLimiter ? "LIMITER ON" : "LIMITER OFF", _pit, state, banner, _center);

            Text(g, _units.FormatSpeed(t), _gear, over ? Slower : Color.White, new RectangleF(GaugeCx - 130, GaugeCy - 70, 260, 150), _center);
            string limitText = limit > 0 ? "LIMIT " + _units.Speed(limit).ToString("0") + " " + _units.SpeedLabel : "PIT LANE";
            Text(g, limitText, _label, Muted, new RectangleF(GaugeCx - 100, GaugeCy + 84, 200, 22), _center);
        }

        private void ArcSegment(Graphics g, int i, int segments, Color color, bool lit)
        {
            float step = Sweep / segments;
            float start = StartAngle + i * step + 1.2f;
            var rect = new RectangleF(GaugeCx - GaugeR, GaugeCy - GaugeR, GaugeR * 2, GaugeR * 2);
            if (lit)
                using (var glow = new Pen(Color.FromArgb(60, color), 26)) g.DrawArc(glow, rect, start, step - 2.4f);
            using (var pen = new Pen(color, 16)) g.DrawArc(pen, rect, start, step - 2.4f);
        }

        // ------------------------------------------------------------ right column

        private void DrawDelta(Graphics g, TelemetrySnapshot t, RectangleF b)
        {
            double delta = t.LapDeltaSeconds;
            Color tone = delta > 0.005 ? Faster : delta < -0.005 ? Slower : _accent;
            DrawPanel(g, b, 14, Color.FromArgb(150, tone));
            Text(g, "DELTA", _label, Muted, new RectangleF(b.X + 14, b.Y + 6, 100, 22), _near);
            Text(g, "VS BEST", _tiny, Dim, new RectangleF(b.Right - 110, b.Y + 6, 96, 22), _far);
            Text(g, Fmt.Signed(delta), _delta, tone, new RectangleF(b.X, b.Y + 22, b.Width, 64), _center);

            // Trend of the delta over the last 20 seconds; up is faster.
            var plot = new RectangleF(b.X + 14, b.Y + 94, b.Width - 28, b.Height - 106);
            using (var zero = new Pen(Color.FromArgb(50, 70, 86), 1) { DashStyle = DashStyle.Dash })
                g.DrawLine(zero, plot.X, plot.Y + plot.Height / 2, plot.Right, plot.Y + plot.Height / 2);
            float[] trace = t.DeltaTrace;
            if (trace == null || trace.Length < 2) return;
            float max = 0.25f;
            foreach (float v in trace) max = Math.Max(max, Math.Abs(v));
            var points = new PointF[trace.Length];
            for (int i = 0; i < trace.Length; i++)
                points[i] = new PointF(plot.X + plot.Width * i / (trace.Length - 1), plot.Y + plot.Height / 2 - plot.Height / 2 * trace[i] / max);
            using (var fill = new GraphicsPath())
            {
                fill.AddLine(plot.X, plot.Y + plot.Height / 2, points[0].X, points[0].Y);
                fill.AddLines(points);
                fill.AddLine(points[points.Length - 1].X, points[points.Length - 1].Y, plot.Right, plot.Y + plot.Height / 2);
                fill.CloseFigure();
                using (var brush = new SolidBrush(Color.FromArgb(40, tone))) g.FillPath(brush, fill);
            }
            using (var pen = new Pen(tone, 2.5f) { LineJoin = LineJoin.Round }) g.DrawLines(pen, points);
        }

        private void DrawLapTimes(Graphics g, TelemetrySnapshot t, RectangleF b)
        {
            DrawPanel(g, b, 14, PanelBorder);
            float rowH = (b.Height - 12) / 3;
            string[] labels = { "LAST", "BEST", "PREDICTED" };
            double[] values = { t.LastLapSeconds, t.BestLapSeconds, t.PredictedLapSeconds };
            for (int i = 0; i < 3; i++)
            {
                var row = new RectangleF(b.X + 14, b.Y + 6 + i * rowH, b.Width - 28, rowH);
                Color color = Color.White;
                if (i == 1 && values[1] > 0) color = Color.FromArgb(190, 140, 255);
                if (i == 2 && values[2] > 0 && values[1] > 0) color = values[2] <= values[1] ? Faster : Amber;
                if (i == 0 && values[0] > 0 && values[0] <= values[1] + 0.001) color = Color.FromArgb(190, 140, 255);
                Text(g, labels[i], _label, Muted, new RectangleF(row.X, row.Y, 100, row.Height), _near);
                Text(g, Fmt.LapTime(values[i]), _value, color, new RectangleF(row.X + 80, row.Y, row.Width - 80, row.Height), _far);
                if (i < 2)
                    using (var line = new Pen(Color.FromArgb(26, 38, 50), 1)) g.DrawLine(line, row.X, row.Bottom, row.Right, row.Bottom);
            }
        }

        // ------------------------------------------------------------ bottom

        private void DrawFuel(Graphics g, TelemetrySnapshot t, RectangleF b)
        {
            DrawPanel(g, b, 14, PanelBorder);
            double fuelLaps = t.FuelLapsLeft;
            double togo = t.LapsRemaining;
            double add = t.FuelToFinish;
            string[] labels = { "FUEL " + _units.FuelSuffix, "PER LAP", "LAPS LEFT", add > 0.05 ? "ADD" : "TO FINISH" };
            string[] values =
            {
                _units.FormatFuel(t.FuelLiters, false),
                _units.FormatFuel(t.FuelPerLapAverage, false, true),
                fuelLaps < 0 ? "--.-" : fuelLaps.ToString("0.0"),
                add < 0 ? "--.-" : add <= 0.05 ? "OK" : "+" + _units.FormatFuel(add, false)
            };
            Color lapsColor = fuelLaps < 0 ? Color.White : fuelLaps < 2 ? Slower : fuelLaps < 5 ? Amber : Color.White;
            Color[] colors = { Color.White, Color.White, lapsColor, add < 0 ? Color.White : add <= 0.05 ? Faster : Amber };
            float col = (b.Width - 20) / 4;
            for (int i = 0; i < 4; i++)
            {
                var cell = new RectangleF(b.X + 10 + col * i, b.Y + 4, col, 60);
                Text(g, labels[i], _tiny, Muted, new RectangleF(cell.X, cell.Y + 2, cell.Width, 18), _center);
                Text(g, values[i], _big, colors[i], new RectangleF(cell.X, cell.Y + 16, cell.Width, 44), _center);
                if (i > 0) using (var line = new Pen(Color.FromArgb(26, 38, 50), 1)) g.DrawLine(line, cell.X, cell.Y + 8, cell.X, cell.Bottom - 4);
            }

            // Fuel range versus distance to go: the bar fills with laps of fuel,
            // the white tick marks the laps still to run.
            var bar = new RectangleF(b.X + 18, b.Bottom - 22, b.Width - 36, 8);
            Fill(g, bar, 4, Track);
            if (fuelLaps > 0)
            {
                double scale = Math.Max(fuelLaps, togo > 0 ? togo : fuelLaps) * 1.05;
                float fill = (float)(fuelLaps / scale);
                Fill(g, new RectangleF(bar.X, bar.Y, Math.Max(8, bar.Width * fill), bar.Height), 4, fuelLaps < 2 ? Slower : togo > fuelLaps ? Amber : _accent);
                if (togo > 0)
                {
                    float x = bar.X + bar.Width * (float)(togo / scale);
                    using (var tick = new Pen(Color.White, 2)) g.DrawLine(tick, x, bar.Y - 5, x, bar.Bottom + 5);
                }
            }
        }

        private void DrawCar(Graphics g, TelemetrySnapshot t, RectangleF b)
        {
            DrawPanel(g, b, 14, PanelBorder);
            string[] labels = { "TC", "ABS", "BIAS" };
            string[] values = { Setting(t.TractionControl), Setting(t.Abs), t.BrakeBias > 0 ? t.BrakeBias.ToString("0.0") : "--" };
            float col = (b.Width - 20) / 3;
            for (int i = 0; i < 3; i++)
            {
                var cell = new RectangleF(b.X + 10 + col * i, b.Y + 4, col, b.Height - 8);
                Text(g, labels[i], _tiny, Muted, new RectangleF(cell.X, cell.Y + 6, cell.Width, 18), _center);
                Text(g, values[i], _big, Color.White, new RectangleF(cell.X, cell.Y + 24, cell.Width, 52), _center);
                if (i > 0) using (var line = new Pen(Color.FromArgb(26, 38, 50), 1)) g.DrawLine(line, cell.X, cell.Y + 10, cell.X, cell.Bottom - 10);
            }
        }

        // ------------------------------------------------------------ helpers

        private static string Setting(double value) => value < 0 ? "--" : value.ToString(value % 1 == 0 ? "0" : "0.0");

        private static void DrawPanel(Graphics g, RectangleF b, float radius, Color border)
        {
            using (GraphicsPath p = Fmt.Rounded(b, radius))
            using (var fill = new SolidBrush(PanelFill))
            using (var pen = new Pen(border, 1.5f))
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
            foreach (Font font in new[] { _tiny, _label, _row, _rowGap, _bar, _value, _big, _delta, _gear, _speed, _pit })
                font.Dispose();
            _center.Dispose();
            _near.Dispose();
            _far.Dispose();
            _flags.Dispose();
        }
    }
}
