using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace DV5Direct
{
    /// <summary>
    /// Full-screen racing flag alerts shared by every dashboard layout so flags
    /// look and behave the same whichever dashboard is active.
    /// </summary>
    internal sealed class FlagPainter : IDisposable
    {
        private readonly int _width, _height;
        private readonly Font _flagFont = Fmt.FontOf(68, FontStyle.Bold);
        private readonly Font _flagSmallFont = Fmt.FontOf(34, FontStyle.Bold);
        private readonly Font _scFont = Fmt.FontOf(155, FontStyle.Bold);
        private readonly StringFormat _center = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap
        };

        public FlagPainter(int width, int height)
        {
            _width = width;
            _height = height;
        }

        public void Draw(Graphics g, TelemetrySnapshot telemetry)
        {
            switch (telemetry.Flag)
            {
                case RacingFlag.Green: Solid(g, Color.FromArgb(30, 205, 70), Brushes.Black, "GREEN"); break;
                case RacingFlag.White: Solid(g, Color.White, Brushes.Black, "WHITE"); break;
                case RacingFlag.Yellow: Solid(g, Color.FromArgb(255, 220, 0), Brushes.Black, "YELLOW"); break;
                case RacingFlag.Red: Solid(g, Color.FromArgb(225, 20, 35), Brushes.White, "RED"); break;
                case RacingFlag.Black:
                    g.Clear(Color.Black);
                    using (var pen = new Pen(Color.White, 8)) g.DrawRectangle(pen, 18, 18, _width - 36, _height - 36);
                    Text(g, "BLACK", _flagFont, Brushes.White, new RectangleF(0, 0, _width, _height));
                    break;
                case RacingFlag.Meatball:
                    g.Clear(Color.Black);
                    using (var orange = new SolidBrush(Color.FromArgb(245, 95, 30))) g.FillEllipse(orange, (_width - 245) / 2, 65, 245, 245);
                    Text(g, "MEATBALL", _flagSmallFont, Brushes.White, new RectangleF(0, 340, _width, 80));
                    break;
                case RacingFlag.BlueYellowStripe:
                    g.Clear(Color.FromArgb(35, 115, 210));
                    using (var stripe = new Pen(Color.FromArgb(255, 220, 0), 90)) g.DrawLine(stripe, -40, _height + 40, _width + 40, -40);
                    Backdrop(g, "BLUE");
                    break;
                case RacingFlag.SafetyCar:
                    g.Clear(Color.White);
                    Text(g, "SC", _scFont, Brushes.Black, new RectangleF(0, 0, _width, _height));
                    break;
                case RacingFlag.Surface:
                    for (int x = 0; x < _width; x += 95)
                        using (var brush = new SolidBrush((x / 95) % 2 == 0 ? Color.FromArgb(255, 220, 0) : Color.FromArgb(220, 25, 35)))
                            g.FillRectangle(brush, x, 0, 95, _height);
                    Backdrop(g, "SURFACE");
                    break;
                case RacingFlag.Checkered:
                    float cw = _width / 10f, ch = _height / 6f;
                    for (int r = 0; r < 6; r++)
                        for (int c = 0; c < 10; c++)
                            g.FillRectangle(((r + c) & 1) == 0 ? Brushes.White : Brushes.Black, c * cw, r * ch, cw + 1, ch + 1);
                    using (var backdrop = new SolidBrush(Color.FromArgb(225, 0, 0, 0))) g.FillRectangle(backdrop, 235, 125, 384, 230);
                    Text(g, "FINISH", _flagSmallFont, Brushes.White, new RectangleF(235, 145, 384, 70));
                    Text(g, telemetry.Position > 0 ? "P" + telemetry.Position : "P--", _flagFont, Brushes.White, new RectangleF(235, 210, 384, 120));
                    break;
            }
        }

        private void Solid(Graphics g, Color background, Brush foreground, string label)
        {
            g.Clear(background);
            Text(g, label, _flagFont, foreground, new RectangleF(0, 0, _width, _height));
        }

        private void Backdrop(Graphics g, string label)
        {
            var box = new RectangleF(205, 182, _width - 410, 116);
            using (var backdrop = new SolidBrush(Color.FromArgb(205, 0, 0, 0))) g.FillRectangle(backdrop, box);
            Text(g, label, _flagSmallFont, Brushes.White, box);
        }

        private void Text(Graphics g, string text, Font font, Brush brush, RectangleF bounds) =>
            g.DrawString(text, font, brush, bounds, _center);

        public void Dispose()
        {
            _flagFont.Dispose();
            _flagSmallFont.Dispose();
            _scFont.Dispose();
            _center.Dispose();
        }
    }

    /// <summary>Formatting and drawing helpers shared by the dashboard renderers.</summary>
    internal static class Fmt
    {
        public static Font FontOf(float pixels, FontStyle style) => new Font("Segoe UI", pixels, style, GraphicsUnit.Pixel);

        public static string Lap(TelemetrySnapshot t) =>
            t.TotalLaps > 0 ? Math.Max(1, t.LapsCompleted + 1) + "/" + t.TotalLaps : Math.Max(1, t.LapsCompleted + 1).ToString();

        public static string Gear(int gear) => gear < 0 ? "R" : gear == 0 ? "N" : gear.ToString();

        public static string Signed(double value) => value >= 0 ? "+" + value.ToString("0.00") : value.ToString("0.00");

        public static string LapTime(double seconds)
        {
            if (seconds <= 0 || double.IsNaN(seconds)) return "--:--.---";
            TimeSpan time = TimeSpan.FromSeconds(seconds);
            return ((int)time.TotalMinutes).ToString("0") + time.ToString(@"\:ss\.fff");
        }

        public static string Remaining(double seconds)
        {
            if (seconds < 0 || double.IsNaN(seconds)) return "--:--";
            TimeSpan t = TimeSpan.FromSeconds(Math.Ceiling(seconds));
            return t.TotalHours >= 1 ? ((int)t.TotalHours).ToString("0") + t.ToString(@"\:mm\:ss") : t.ToString(@"mm\:ss");
        }

        public static GraphicsPath Rounded(RectangleF b, float r)
        {
            var p = new GraphicsPath();
            float d = r * 2;
            p.AddArc(b.X, b.Y, d, d, 180, 90);
            p.AddArc(b.Right - d, b.Y, d, d, 270, 90);
            p.AddArc(b.Right - d, b.Bottom - d, d, d, 0, 90);
            p.AddArc(b.X, b.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    internal static class GraphicsExtensions
    {
        public static void FillRoundedRectangle(this Graphics g, Brush brush, RectangleF rectangle, float radius)
        {
            using (GraphicsPath path = Fmt.Rounded(rectangle, radius)) g.FillPath(brush, path);
        }
    }
}
