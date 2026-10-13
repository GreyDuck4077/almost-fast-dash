using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace AlmostFastOverlays
{
    /// <summary>Formatting and drawing helpers shared by the dashboard renderers.</summary>
    internal static class Fmt
    {
        public static string Lap(TelemetrySnapshot t) =>
            t.TotalLaps > 0 ? Math.Max(1, t.LapsCompleted + 1) + "/" + t.TotalLaps : Math.Max(1, t.LapsCompleted + 1).ToString();

        public static string Gear(int gear) => gear < 0 ? "R" : gear == 0 ? "N" : gear.ToString();

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
