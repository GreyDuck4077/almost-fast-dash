using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;

namespace AlmostFastOverlays
{
    /// <summary>
    /// Shared visual language for every dashboard: palette, type scale, panels,
    /// chips, bars and gauges. Keeping these in one place is what makes the four
    /// layouts read as one product.
    /// </summary>
    internal sealed class DashKit : IDisposable
    {
        public static readonly Color Background = Color.FromArgb(5, 7, 10);
        public static readonly Color PanelFill = Color.FromArgb(14, 19, 26);
        public static readonly Color PanelTop = Color.FromArgb(20, 27, 36);
        public static readonly Color PanelBorder = Color.FromArgb(32, 41, 53);
        public static readonly Color Track = Color.FromArgb(27, 35, 46);
        public static readonly Color Text = Color.FromArgb(236, 241, 247);
        public static readonly Color Muted = Color.FromArgb(128, 142, 160);
        public static readonly Color Dim = Color.FromArgb(78, 90, 106);
        public static readonly Color Good = Color.FromArgb(46, 222, 124);
        public static readonly Color Bad = Color.FromArgb(244, 63, 78);
        public static readonly Color Warn = Color.FromArgb(255, 190, 46);
        public static readonly Color Purple = Color.FromArgb(186, 128, 255);
        public static readonly Color Blue = Color.FromArgb(76, 158, 255);

        private readonly string _family;
        private readonly Dictionary<string, Font> _fonts = new Dictionary<string, Font>();
        private readonly StringFormat[] _formats;

        public DashKit(Color accent)
        {
            Accent = accent;
            // Bahnschrift is a DIN-style face that ships with Windows 10 and later;
            // it gives the instrument-cluster look. Segoe UI is the fallback.
            using (var installed = new InstalledFontCollection())
                _family = installed.Families.Any(f => f.Name == "Bahnschrift") ? "Bahnschrift" : "Segoe UI";
            _formats = new[] { Format(StringAlignment.Near), Format(StringAlignment.Center), Format(StringAlignment.Far) };
        }

        public Color Accent { get; }

        public static void Prepare(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        }

        // ---------------------------------------------------------------- type

        /// <summary>Numeric and headline font at a pixel size.</summary>
        public Font Value(float px) => Get(px, FontStyle.Bold);

        /// <summary>Label font at a pixel size.</summary>
        public Font LabelFont(float px) => Get(px, FontStyle.Regular);

        private Font Get(float px, FontStyle style)
        {
            string key = px + "|" + style;
            if (!_fonts.TryGetValue(key, out Font font))
                _fonts[key] = font = new Font(_family, px, style, GraphicsUnit.Pixel);
            return font;
        }

        public void Draw(Graphics g, string text, Font font, Color color, RectangleF bounds, StringAlignment align = StringAlignment.Center)
        {
            using (var brush = new SolidBrush(color))
                g.DrawString(text ?? "--", font, brush, bounds, _formats[(int)align]);
        }

        public void Label(Graphics g, string text, RectangleF bounds, StringAlignment align = StringAlignment.Near, float size = 13) =>
            Draw(g, text, LabelFont(size), Muted, bounds, align);

        public float Measure(Graphics g, string text, Font font) => g.MeasureString(text, font).Width;

        // ---------------------------------------------------------------- surfaces

        public static void Canvas(Graphics g, int width, int height, Color accent)
        {
            g.Clear(Background);
            // A faint accent bloom from the top center gives depth without noise.
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(width * 0.15f, -height * 0.55f, width * 0.7f, height * 1.1f);
                using (var bloom = new PathGradientBrush(path)
                {
                    CenterColor = Color.FromArgb(22, accent),
                    SurroundColors = new[] { Color.FromArgb(0, accent) }
                })
                    g.FillPath(bloom, path);
            }
        }

        public static void Panel(Graphics g, RectangleF b, float radius = 10, Color? border = null, Color? fill = null)
        {
            using (GraphicsPath p = Rounded(b, radius))
            {
                if (fill.HasValue)
                    using (var brush = new SolidBrush(fill.Value)) g.FillPath(brush, p);
                else
                    using (var brush = new LinearGradientBrush(new RectangleF(b.X, b.Y - 1, b.Width, b.Height + 2), PanelTop, PanelFill, 90f))
                        g.FillPath(brush, p);
                using (var pen = new Pen(border ?? PanelBorder, 1.5f)) g.DrawPath(pen, p);
            }
        }

        /// <summary>A panel tinted by a status color, used for delta and warnings.</summary>
        public static void TintedPanel(Graphics g, RectangleF b, Color tone, float radius = 10)
        {
            using (GraphicsPath p = Rounded(b, radius))
            {
                using (var brush = new LinearGradientBrush(new RectangleF(b.X, b.Y - 1, b.Width, b.Height + 2),
                    Color.FromArgb(70, tone), Color.FromArgb(22, tone), 90f))
                    g.FillPath(brush, p);
                using (var pen = new Pen(Color.FromArgb(150, tone), 1.5f)) g.DrawPath(pen, p);
            }
        }

        public static void Fill(Graphics g, RectangleF b, Color color, float radius = 6)
        {
            using (var brush = new SolidBrush(color))
            {
                if (radius <= 0) g.FillRectangle(brush, b);
                else using (GraphicsPath p = Rounded(b, radius)) g.FillPath(brush, p);
            }
        }

        /// <summary>Bracket-style corners on the left and right edges of a region.</summary>
        public static void Brackets(Graphics g, RectangleF b, Color color, float arm = 16, float width = 2.5f)
        {
            using (var pen = new Pen(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                g.DrawLines(pen, new[] { new PointF(b.X + arm, b.Y), new PointF(b.X, b.Y), new PointF(b.X, b.Bottom), new PointF(b.X + arm, b.Bottom) });
                g.DrawLines(pen, new[] { new PointF(b.Right - arm, b.Y), new PointF(b.Right, b.Y), new PointF(b.Right, b.Bottom), new PointF(b.Right - arm, b.Bottom) });
            }
        }

        public void Chip(Graphics g, RectangleF b, string text, Color foreground, Color background, float size = 13)
        {
            Fill(g, b, background, Math.Min(b.Height / 2, 7));
            Draw(g, text, Value(size), foreground, b);
        }

        /// <summary>A labelled metric: small label on top, large value below.</summary>
        public void Metric(Graphics g, RectangleF b, string label, string value, Color color, float valueSize, StringAlignment align = StringAlignment.Center, float labelSize = 13)
        {
            float inset = align == StringAlignment.Center ? 0 : 14;
            var labelBox = new RectangleF(b.X + inset, b.Y + 6, b.Width - inset * 2, labelSize + 8);
            Label(g, label, labelBox, align, labelSize);
            Draw(g, value, Value(valueSize), color, new RectangleF(b.X + inset, labelBox.Bottom - 2, b.Width - inset * 2, b.Bottom - labelBox.Bottom), align);
        }

        public static void Bar(Graphics g, RectangleF b, double fraction, Color color, Color? track = null)
        {
            float r = b.Height / 2;
            Fill(g, b, track ?? Track, r);
            float w = (float)(Math.Max(0, Math.Min(1, fraction)) * b.Width);
            if (w >= 1) Fill(g, new RectangleF(b.X, b.Y, Math.Max(w, b.Height), b.Height), color, r);
        }

        /// <summary>Bar that grows from the center: right (positive) or left (negative).</summary>
        public static void CenterBar(Graphics g, RectangleF b, double value, double fullScale, Color positive, Color negative)
        {
            Fill(g, b, Track, b.Height / 2);
            float half = b.Width / 2, center = b.X + half;
            float len = (float)(Math.Min(1, Math.Abs(value) / fullScale) * half);
            if (len >= 1)
            {
                var fill = value >= 0 ? new RectangleF(center, b.Y, len, b.Height) : new RectangleF(center - len, b.Y, len, b.Height);
                Fill(g, fill, value >= 0 ? positive : negative, Math.Min(b.Height / 2, len / 2));
            }
            using (var tick = new Pen(Text, 2)) g.DrawLine(tick, center, b.Y - 3, center, b.Bottom + 3);
        }

        /// <summary>Segmented arc; lit segments glow. Angles in degrees, clockwise from 3 o'clock.</summary>
        public static void SegmentArc(Graphics g, PointF center, float radius, float start, float sweep, int segments,
            double fraction, Func<double, Color> colorAt, float thickness, bool overrideAll = false, Color? overrideColor = null)
        {
            int lit = (int)Math.Ceiling(Math.Max(0, Math.Min(1, fraction)) * segments);
            float step = sweep / segments;
            var rect = new RectangleF(center.X - radius, center.Y - radius, radius * 2, radius * 2);
            for (int i = 0; i < segments; i++)
            {
                bool on = overrideAll || i < lit;
                Color color = overrideAll && overrideColor.HasValue ? overrideColor.Value : colorAt((i + 0.5) / segments);
                float a = start + i * step + step * 0.12f, s = step * 0.76f;
                if (on)
                    using (var glow = new Pen(Color.FromArgb(55, color), thickness + 9)) g.DrawArc(glow, rect, a, s);
                using (var pen = new Pen(on ? color : Track, thickness)) g.DrawArc(pen, rect, a, s);
            }
        }

        /// <summary>Smooth arc with rounded caps filled to a fraction.</summary>
        public static void SmoothArc(Graphics g, PointF center, float radius, float start, float sweep, double fraction, Color color, float thickness)
        {
            var rect = new RectangleF(center.X - radius, center.Y - radius, radius * 2, radius * 2);
            using (var track = new Pen(Track, thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(track, rect, start, sweep);
            float s = (float)(Math.Max(0, Math.Min(1, fraction)) * sweep);
            if (Math.Abs(s) < 0.5f) return;
            using (var pen = new Pen(color, thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(pen, rect, start, s);
        }

        // ---------------------------------------------------------------- colors

        public Color ShiftColor(double at) => at > 0.86 ? Bad : at > 0.66 ? Warn : Accent;

        public static Color DeltaColor(double delta) => delta > 0.005 ? Good : delta < -0.005 ? Bad : Text;

        public static Color FuelLapsColor(double laps) => laps < 0 ? Text : laps < 2 ? Bad : laps < 5 ? Warn : Text;

        // ---------------------------------------------------------------- helpers

        public static GraphicsPath Rounded(RectangleF b, float r) => Fmt.Rounded(b, Math.Max(0.5f, Math.Min(r, Math.Min(b.Width, b.Height) / 2)));

        public static string Setting(double value) => value < 0 ? "--" : value.ToString(value % 1 == 0 ? "0" : "0.0");

        public static string IRating(int rating) => rating <= 0 ? "" : rating >= 1000 ? (rating / 1000.0).ToString("0.0") + "k" : rating.ToString();

        public static string Temperature(double celsius, bool metric) =>
            double.IsNaN(celsius) ? "--°" : (metric ? celsius : celsius * 9 / 5 + 32).ToString("0") + "°";

        public static string Session(TelemetrySnapshot t) => t.ProgressMode == SessionProgressMode.Practice ? "PRACTICE" : "RACE";

        public static string Remaining(TelemetrySnapshot t) =>
            t.ProgressMode == SessionProgressMode.LapRace && t.TotalLaps > 0 ? Math.Max(0, t.TotalLaps - t.LapsCompleted) + " TO GO" :
            t.ProgressMode == SessionProgressMode.None ? "" : Fmt.Remaining(t.SessionSecondsRemaining);

        private static StringFormat Format(StringAlignment alignment) => new StringFormat
        {
            Alignment = alignment,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap,
            Trimming = StringTrimming.EllipsisCharacter
        };

        public void Dispose()
        {
            foreach (Font font in _fonts.Values) font.Dispose();
            _fonts.Clear();
            foreach (StringFormat format in _formats) format.Dispose();
        }
    }
}
