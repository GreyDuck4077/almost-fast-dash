using System;
using System.Drawing;

namespace DV5Direct
{
    /// <summary>
    /// Flagship road and GT dashboard. Gear sits between an RPM arc and a pedal
    /// arc; the relative, fuel plan, lap times and delta surround it. On pit road
    /// the center becomes a pit-speed gauge.
    /// </summary>
    internal sealed class SummitRenderer : IDashboardRenderer, ILiveTraceRenderer
    {
        private readonly int _width, _height;
        private readonly DashKit _kit;
        private readonly UnitSystem _units;
        private readonly FlagPainter _flags;
        private static readonly PointF Hub = new PointF(427, 232);

        public SummitRenderer(int width, int height, DashboardSettings settings, UnitSystem units)
        {
            _width = width;
            _height = height;
            _kit = new DashKit(settings?.Accent ?? Color.FromArgb(45, 212, 191));
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
                RelativeTable.Draw(g, _kit, t, new RectangleF(8, 66, 268, 326));
                if (t.OnPitRoad) PitGauge.Draw(g, _kit, _units, t, Hub, 128);
                else DrawCluster(g, t, shiftFlashOn, previousGear, gearTransitionProgress);
                DrawFuel(g, t, new RectangleF(578, 66, 268, 170));
                DrawLapTimes(g, t, new RectangleF(578, 242, 268, 150));
                DrawFooter(g, t);
            }
            return bitmap;
        }

        private void DrawHeader(Graphics g, TelemetrySnapshot t)
        {
            var left = new RectangleF(8, 8, 228, 50);
            DashKit.Panel(g, left);
            _kit.Label(g, DashKit.Session(t) + "   " + DashKit.Remaining(t), new RectangleF(left.X + 14, left.Y + 4, left.Width - 28, 18));
            _kit.Draw(g, "LAP " + Fmt.Lap(t), _kit.Value(24), DashKit.Text, new RectangleF(left.X + 14, left.Y + 20, left.Width - 28, 28), StringAlignment.Near);

            // Car settings strip, labels color-coded like a wheel's rotaries.
            string[] labels = { "TC", "ABS", "BIAS", "INC" };
            Color[] tones = { DashKit.Blue, DashKit.Warn, DashKit.Bad, DashKit.Muted };
            string[] values = { DashKit.Setting(t.TractionControl), DashKit.Setting(t.Abs), t.BrakeBias > 0 ? t.BrakeBias.ToString("0.0") : "--", t.IncidentPoints + "x" };
            float x = 242, w = (612 - 242 - 3 * 6) / 4f;
            for (int i = 0; i < 4; i++)
            {
                var cell = new RectangleF(x + i * (w + 6), 8, w, 50);
                DashKit.Panel(g, cell);
                _kit.Draw(g, labels[i], _kit.Value(12), tones[i], new RectangleF(cell.X, cell.Y + 4, cell.Width, 16));
                _kit.Draw(g, values[i], _kit.Value(24), DashKit.Text, new RectangleF(cell.X, cell.Y + 18, cell.Width, 30));
            }

            var right = new RectangleF(618, 8, 228, 50);
            DashKit.Panel(g, right);
            _kit.Label(g, "POSITION", new RectangleF(right.X + 14, right.Y + 4, 120, 18));
            string pos = t.Position > 0 ? "P" + t.Position : "P--";
            _kit.Draw(g, pos, _kit.Value(26), DashKit.Text, new RectangleF(right.X + 14, right.Y + 19, 90, 30), StringAlignment.Near);
            if (t.TotalStarters > 0)
                _kit.Draw(g, "/" + t.TotalStarters, _kit.Value(18), DashKit.Dim,
                    new RectangleF(right.X + 12 + _kit.Measure(g, pos, _kit.Value(26)), right.Y + 23, 70, 28), StringAlignment.Near);
            int gained = t.StartPosition > 0 && t.Position > 0 ? t.StartPosition - t.Position : 0;
            if (gained != 0)
                _kit.Chip(g, new RectangleF(right.Right - 62, right.Y + 16, 50, 24), (gained > 0 ? "▲" : "▼") + Math.Abs(gained),
                    gained > 0 ? DashKit.Good : DashKit.Bad, Color.FromArgb(40, gained > 0 ? DashKit.Good : DashKit.Bad));
        }

        private void DrawCluster(Graphics g, TelemetrySnapshot t, bool shiftFlashOn, int previousGear, float progress)
        {
            // RPM on the left arc, filling upward; pedals on the right.
            DashKit.SegmentArc(g, Hub, 138, 118, 124, 22, t.ShiftIndicatorPct, _kit.ShiftColor, 13, shiftFlashOn, DashKit.Bad);
            DashKit.SmoothArc(g, Hub, 140, 62, -124, t.Throttle, DashKit.Good, 9);
            DashKit.SmoothArc(g, Hub, 124, 62, -124, t.Brake, DashKit.Bad, 6);

            _kit.Draw(g, _units.FormatSpeed(t), _kit.Value(34), DashKit.Text, new RectangleF(Hub.X - 80, 84, 160, 40));
            _kit.Label(g, _units.SpeedLabel, new RectangleF(Hub.X - 60, 120, 120, 16), StringAlignment.Center, 12);

            Color gearColor = shiftFlashOn ? DashKit.Bad : DashKit.Text;
            var gearBox = new RectangleF(Hub.X - 90, Hub.Y - 82, 180, 170);
            if (previousGear != t.Gear && progress >= 0 && progress < 1)
            {
                float dir = t.Gear > previousGear ? -1 : 1;
                _kit.Draw(g, Fmt.Gear(previousGear), _kit.Value(150), Color.FromArgb((int)(255 * (1 - progress)), gearColor),
                    new RectangleF(gearBox.X, gearBox.Y - dir * 34 * progress, gearBox.Width, gearBox.Height));
                _kit.Draw(g, Fmt.Gear(t.Gear), _kit.Value(150), Color.FromArgb((int)(255 * progress), gearColor),
                    new RectangleF(gearBox.X, gearBox.Y + dir * 34 * (1 - progress), gearBox.Width, gearBox.Height));
            }
            else
            {
                _kit.Draw(g, t.Connected ? Fmt.Gear(t.Gear) : "–", _kit.Value(150), gearColor, gearBox);
                string prev = t.Gear < 0 ? "" : t.Gear == 0 ? "R" : t.Gear == 1 ? "N" : (t.Gear - 1).ToString();
                string next = t.Gear < 0 ? "N" : t.Gear == 0 ? "1" : t.MaxForwardGear > 0 && t.Gear >= t.MaxForwardGear ? "" : (t.Gear + 1).ToString();
                _kit.Draw(g, prev, _kit.Value(30), DashKit.Dim, new RectangleF(Hub.X - 116, Hub.Y - 22, 40, 40));
                _kit.Draw(g, next, _kit.Value(30), DashKit.Dim, new RectangleF(Hub.X + 76, Hub.Y - 22, 40, 40));
            }

            _kit.Draw(g, t.EngineRpm > 0 ? t.EngineRpm.ToString("0") : "----", _kit.Value(24), DashKit.Text, new RectangleF(Hub.X - 80, 336, 160, 30));
            _kit.Label(g, "RPM", new RectangleF(Hub.X - 60, 362, 120, 16), StringAlignment.Center, 12);
            _kit.Draw(g, "THR", _kit.Value(11), DashKit.Good, new RectangleF(Hub.X + 92, 346, 40, 14));
            _kit.Draw(g, "BRK", _kit.Value(11), DashKit.Bad, new RectangleF(Hub.X + 76, 362, 40, 14));
        }

        private void DrawFuel(Graphics g, TelemetrySnapshot t, RectangleF b)
        {
            DashKit.Panel(g, b);
            double laps = t.FuelLapsLeft, add = t.FuelToFinish;
            _kit.Metric(g, new RectangleF(b.X, b.Y + 2, b.Width / 2, 62), "FUEL " + _units.FuelSuffix, _units.FormatFuel(t.FuelLiters, false), DashKit.Text, 30, StringAlignment.Near);
            _kit.Metric(g, new RectangleF(b.X + b.Width / 2, b.Y + 2, b.Width / 2, 62), "EST LAPS", laps < 0 ? "--.-" : laps.ToString("0.0"), DashKit.FuelLapsColor(laps), 30, StringAlignment.Far);

            double togo = t.LapsRemaining;
            var bar = new RectangleF(b.X + 14, b.Y + 74, b.Width - 28, 8);
            double scale = Math.Max(1, Math.Max(laps, togo) * 1.05);
            DashKit.Bar(g, bar, laps > 0 ? laps / scale : t.FuelLevelPct, laps >= 0 && laps < togo ? DashKit.Warn : _kit.Accent);
            if (togo > 0 && laps > 0)
            {
                float x = bar.X + bar.Width * (float)(togo / scale);
                using (var tick = new Pen(DashKit.Text, 2)) g.DrawLine(tick, x, bar.Y - 5, x, bar.Bottom + 5);
            }

            _kit.Metric(g, new RectangleF(b.X, b.Y + 94, b.Width / 2, 70), "AVG / LAP", _units.FormatFuel(t.FuelPerLapAverage, false, true), DashKit.Text, 26, StringAlignment.Near);
            string addText = add < 0 ? "--.-" : add <= 0.05 ? "NONE" : "+" + _units.FormatFuel(add, false);
            _kit.Metric(g, new RectangleF(b.X + b.Width / 2, b.Y + 94, b.Width / 2, 70), "REFUEL", addText,
                add < 0 ? DashKit.Text : add <= 0.05 ? DashKit.Good : DashKit.Warn, 26, StringAlignment.Far);
        }

        private void DrawLapTimes(Graphics g, TelemetrySnapshot t, RectangleF b)
        {
            DashKit.Panel(g, b);
            string[] labels = { "LAST", "BEST", "PREDICTED" };
            double[] values = { t.LastLapSeconds, t.BestLapSeconds, t.PredictedLapSeconds };
            Color[] colors =
            {
                values[0] > 0 && values[1] > 0 && values[0] <= values[1] + 0.001 ? DashKit.Purple : DashKit.Text,
                values[1] > 0 ? DashKit.Purple : DashKit.Text,
                values[2] > 0 && values[1] > 0 ? (values[2] <= values[1] ? DashKit.Good : DashKit.Warn) : DashKit.Text
            };
            float rowH = (b.Height - 8) / 3;
            for (int i = 0; i < 3; i++)
            {
                var row = new RectangleF(b.X + 14, b.Y + 4 + i * rowH, b.Width - 28, rowH);
                _kit.Label(g, labels[i], new RectangleF(row.X, row.Y, 110, row.Height));
                _kit.Draw(g, Fmt.LapTime(values[i]), _kit.Value(25), colors[i], new RectangleF(row.X + 90, row.Y, row.Width - 90, row.Height), StringAlignment.Far);
                if (i < 2) using (var line = new Pen(DashKit.PanelBorder, 1)) g.DrawLine(line, row.X, row.Bottom, row.Right, row.Bottom);
            }
        }

        private void DrawFooter(Graphics g, TelemetrySnapshot t)
        {
            var temps = new RectangleF(8, 398, 190, 74);
            DashKit.Panel(g, temps);
            _kit.Label(g, "AIR", new RectangleF(temps.X + 14, temps.Y + 6, 60, 16));
            _kit.Label(g, "TRACK", new RectangleF(temps.X + 100, temps.Y + 6, 70, 16));
            _kit.Draw(g, DashKit.Temperature(t.AirTempC, _units.Metric), _kit.Value(28), DashKit.Text, new RectangleF(temps.X + 14, temps.Y + 24, 80, 40), StringAlignment.Near);
            _kit.Draw(g, DashKit.Temperature(t.TrackTempC, _units.Metric), _kit.Value(28), DashKit.Text, new RectangleF(temps.X + 100, temps.Y + 24, 80, 40), StringAlignment.Near);

            double delta = t.LapDeltaSeconds;
            Color tone = DashKit.DeltaColor(delta);
            var box = new RectangleF(204, 398, 446, 74);
            DashKit.TintedPanel(g, box, tone == DashKit.Text ? _kit.Accent : tone);
            _kit.Label(g, "DELTA", new RectangleF(box.X + 16, box.Y + 6, 80, 18));
            _kit.Label(g, "VS BEST", new RectangleF(box.Right - 96, box.Y + 6, 80, 18), StringAlignment.Far);
            _kit.Draw(g, Fmt.Signed(delta), _kit.Value(36), tone, new RectangleF(box.X, box.Y + 2, box.Width, 46));
            DashKit.CenterBar(g, new RectangleF(box.X + 20, box.Bottom - 18, box.Width - 40, 8), delta, 1.0, DashKit.Good, DashKit.Bad);

            var clock = new RectangleF(656, 398, 190, 74);
            DashKit.Panel(g, clock);
            _kit.Label(g, "LOCAL TIME", new RectangleF(clock.X + 14, clock.Y + 6, 120, 16));
            _kit.Draw(g, DateTime.Now.ToString("HH:mm"), _kit.Value(30), DashKit.Text, new RectangleF(clock.X + 14, clock.Y + 24, clock.Width - 28, 40), StringAlignment.Near);
            if (t.EngineWarning)
                _kit.Chip(g, new RectangleF(clock.Right - 84, clock.Y + 30, 72, 24), "ENGINE", DashKit.Text, DashKit.Bad, 12);
        }

        public void Dispose()
        {
            _kit.Dispose();
            _flags.Dispose();
        }
    }

    /// <summary>Five-row on-track relative shared by the Summit and Endurance layouts.</summary>
    internal static class RelativeTable
    {
        public static void Draw(Graphics g, DashKit kit, TelemetrySnapshot t, RectangleF b)
        {
            DashKit.Panel(g, b);
            kit.Label(g, "RELATIVE", new RectangleF(b.X + 14, b.Y + 6, 120, 18));
            RelativeEntry[] rows = t.Relatives ?? new RelativeEntry[0];
            int player = Array.FindIndex(rows, r => r.IsPlayer);
            if (player < 0)
            {
                kit.Label(g, "Waiting for cars", new RectangleF(b.X, b.Y + 30, b.Width, b.Height - 40), StringAlignment.Center);
                return;
            }
            float rowH = (b.Height - 32) / 5;
            for (int slot = 0; slot < 5; slot++)
            {
                int index = player + slot - 2;
                if (index < 0 || index >= rows.Length) continue;
                Row(g, kit, rows[index], new RectangleF(b.X + 6, b.Y + 28 + slot * rowH, b.Width - 12, rowH - 4));
            }
        }

        private static void Row(Graphics g, DashKit kit, RelativeEntry r, RectangleF b)
        {
            if (r.IsPlayer)
            {
                DashKit.Fill(g, b, Color.FromArgb(40, kit.Accent), 8);
                DashKit.Fill(g, new RectangleF(b.X, b.Y + 10, 3, b.Height - 20), kit.Accent, 1.5f);
            }
            Color nameColor = r.IsPlayer ? DashKit.Text
                : r.InPits ? DashKit.Dim
                : r.LapRelation > 0 ? Color.FromArgb(255, 120, 100)
                : r.LapRelation < 0 ? DashKit.Blue
                : Color.FromArgb(214, 222, 232);

            var pos = new RectangleF(b.X + 8, b.Y + (b.Height - 26) / 2, 30, 26);
            DashKit.Fill(g, pos, r.IsPlayer ? kit.Accent : DashKit.Track, 6);
            kit.Draw(g, r.Position > 0 ? r.Position.ToString() : "-", kit.Value(15), r.IsPlayer ? DashKit.Background : DashKit.Text, pos);

            var number = new RectangleF(pos.Right + 6, pos.Y, 32, 26);
            DashKit.Fill(g, number, Color.FromArgb(226, 232, 240), 6);
            kit.Draw(g, r.CarNumber ?? "", kit.Value(13), DashKit.Background, number);

            float nameX = number.Right + 8;
            kit.Draw(g, r.IsPlayer ? "YOU" : r.Name, kit.Value(16), nameColor, new RectangleF(nameX, b.Y + 3, b.Right - nameX - 58, b.Height * 0.55f), StringAlignment.Near);
            if (!string.IsNullOrEmpty(r.License))
            {
                var lic = new RectangleF(nameX, b.Y + b.Height * 0.58f, 34, 15);
                DashKit.Fill(g, lic, Color.FromArgb(170, r.LicenseColor), 4);
                kit.Draw(g, r.License + " " + DashKit.IRating(r.IRating), kit.Value(10), DashKit.Text, new RectangleF(lic.X, lic.Y, 60, lic.Height), StringAlignment.Near);
            }
            if (r.InPits) kit.Draw(g, "PIT", kit.Value(11), DashKit.Warn, new RectangleF(nameX + 64, b.Y + b.Height * 0.56f, 40, 16), StringAlignment.Near);

            if (!r.IsPlayer)
                kit.Draw(g, Math.Abs(r.Gap).ToString("0.0"), kit.Value(19), r.Gap < 0 ? DashKit.Good : DashKit.Warn,
                    new RectangleF(b.Right - 60, b.Y, 52, b.Height), StringAlignment.Far);
        }
    }

    /// <summary>Pit-lane speed gauge that replaces a dashboard's center on pit road.</summary>
    internal static class PitGauge
    {
        public static void Draw(Graphics g, DashKit kit, UnitSystem units, TelemetrySnapshot t, PointF hub, float radius)
        {
            double limit = t.PitSpeedLimitMph;
            bool over = limit > 0 && t.SpeedMph > limit + 0.5;
            // The limit sits at 80% of the arc so going over is obvious.
            double ratio = limit > 0 ? t.SpeedMph / (limit * 1.25) : 0;
            DashKit.SegmentArc(g, hub, radius, 145, 250, 30, ratio, at => over || at > 0.8 ? DashKit.Bad : DashKit.Good, 13);

            Color state = over ? DashKit.Bad : t.PitLimiter ? DashKit.Good : DashKit.Warn;
            var banner = new RectangleF(hub.X - 90, hub.Y - 96, 180, 32);
            kit.Chip(g, banner, t.PitLimiter ? "LIMITER ON" : "LIMITER OFF", state, Color.FromArgb(50, state), 18);
            kit.Draw(g, units.FormatSpeed(t), kit.Value(120), over ? DashKit.Bad : DashKit.Text, new RectangleF(hub.X - 130, hub.Y - 64, 260, 140));
            string limitText = limit > 0 ? "PIT LIMIT " + units.Speed(limit).ToString("0") + " " + units.SpeedLabel : "PIT LANE";
            kit.Label(g, limitText, new RectangleF(hub.X - 110, hub.Y + 80, 220, 20), StringAlignment.Center);
        }
    }
}
