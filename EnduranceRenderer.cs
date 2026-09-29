using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace DV5Direct
{
    internal sealed class EnduranceRenderer : IDashboardRenderer
    {
        private readonly int _width, _height;
        private readonly Color _accent;
        private readonly Font _label = FontOf(14, FontStyle.Bold);
        private readonly Font _value = FontOf(35, FontStyle.Bold);
        private readonly Font _gear = FontOf(58, FontStyle.Bold);
        private readonly Font _small = FontOf(19, FontStyle.Bold);
        private readonly Font _unit = FontOf(12, FontStyle.Bold);
        private readonly StringFormat _center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        public EnduranceRenderer(int width, int height, DashboardSettings settings)
        {
            _width = width; _height = height;
            _accent = settings?.Accent ?? Color.FromArgb(39, 199, 217);
        }

        public Bitmap Render(TelemetrySnapshot t, bool shiftFlashOn, int previousGear, float gearTransitionProgress)
        {
            var bitmap = new Bitmap(_width, _height);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.FromArgb(4, 9, 14));
                using (var stripe = new Pen(Color.FromArgb(10, _accent), 1))
                    for (int x = -480; x < _width; x += 26) g.DrawLine(stripe, x, _height, x + 480, 0);

                float gap = 9, margin = 9;
                float tileW = (_width - margin * 2 - gap * 5) / 6f;
                string lap = t.TotalLaps > 0 ? Math.Max(1, t.LapsCompleted + 1) + "/" + t.TotalLaps : Math.Max(1, t.LapsCompleted + 1).ToString();
                DrawMetric(g, Box(margin + (tileW + gap) * 0, 8, tileW, 102), "LAP", lap, _accent);
                DrawMetric(g, Box(margin + (tileW + gap) * 1, 8, tileW, 102), "RPM", t.Connected ? Math.Round(t.EngineRpm).ToString("0") : "---", Color.FromArgb(45, 220, 115));
                DrawMetric(g, Box(margin + (tileW + gap) * 2, 8, tileW, 102), "GEAR", FormatGear(t.Gear), Color.FromArgb(240, 70, 115), true);
                DrawMetric(g, Box(margin + (tileW + gap) * 3, 8, tileW, 102), "SPEED", t.Connected ? t.SpeedMph.ToString("0") : "---", Color.FromArgb(210, 225, 60), false, "MPH");
                DrawMetric(g, Box(margin + (tileW + gap) * 4, 8, tileW, 102), "BRAKE BIAS", "--.-", Color.FromArgb(145, 105, 235));
                DrawMetric(g, Box(margin + (tileW + gap) * 5, 8, tileW, 102), "POSITION", t.Position > 0 ? "P" + t.Position : "P--", Color.FromArgb(45, 155, 230));

                float strategyY = 120, strategyH = 165;
                float strategyW = (_width - margin * 2 - gap * 4) / 5f;
                DrawStrategy(g, Box(margin + (strategyW + gap) * 0, strategyY, strategyW, strategyH), "FUEL\nLAST LAP", "--.- L", "LAST");
                DrawStrategy(g, Box(margin + (strategyW + gap) * 1, strategyY, strategyW, strategyH), "FUEL\nAVERAGE", "--.- L", "AVG");
                DrawStrategy(g, Box(margin + (strategyW + gap) * 2, strategyY, strategyW, strategyH), "LAP\nPREDICTION", t.CurrentLapSeconds > 0 ? FormatTime(t.CurrentLapSeconds + t.LapDeltaSeconds) : "--:--.---", "PACE");
                DrawStrategy(g, Box(margin + (strategyW + gap) * 3, strategyY, strategyW, strategyH), "ESTIMATED\nREFUEL", "--.- L", "FUEL");
                DrawStrategy(g, Box(margin + (strategyW + gap) * 4, strategyY, strategyW, strategyH), "TOTAL RACE\nPREDICTION", "--.- L", "RACE");

                RectangleF lower = Box(margin, 295, _width - margin * 2, 176);
                DrawPanel(g, lower, 16);
                float col = lower.Width / 3f;
                using (var divider = new Pen(Color.FromArgb(45, 70, 88), 2))
                { g.DrawLine(divider, lower.X + col, lower.Y + 18, lower.X + col, lower.Bottom - 18); g.DrawLine(divider, lower.X + col * 2, lower.Y + 18, lower.X + col * 2, lower.Bottom - 18); }
                DrawLabel(g, "FUEL REMAINING", new RectangleF(lower.X, lower.Y + 14, col, 25));
                DrawText(g, t.FuelLiters.ToString("0.0") + " L", _value, Brushes.White, new RectangleF(lower.X, lower.Y + 39, col, 62));
                DrawFuelBar(g, new RectangleF(lower.X + 24, lower.Y + 119, col - 48, 18), t.FuelLiters);
                DrawLabel(g, "AVERAGE CONSUMPTION", new RectangleF(lower.X + col, lower.Y + 14, col, 25));
                DrawText(g, "--.- L / LAP", _small, Brushes.White, new RectangleF(lower.X + col, lower.Y + 47, col, 38));
                DrawLabel(g, "LAST 5 LAPS", new RectangleF(lower.X + col, lower.Y + 93, col, 24));
                using (var dot = new SolidBrush(_accent)) for (int i = 0; i < 5; i++) g.FillEllipse(dot, lower.X + col + col/2 - 34 + i*17, lower.Y + 130, 8, 8);
                DrawLabel(g, "CURRENT STINT LAPS", new RectangleF(lower.X + col * 2, lower.Y + 14, col, 25));
                DrawText(g, Math.Max(0, t.LapsCompleted).ToString(), _value, Brushes.White, new RectangleF(lower.X + col * 2, lower.Y + 42, col, 60));
                using (var badge = new SolidBrush(_accent)) g.FillRoundedRectangle(badge, new RectangleF(lower.X + col*2 + col/2 - 44, lower.Y + 119, 88, 28), 7);
                DrawText(g, "STINT", _unit, Brushes.Black, new RectangleF(lower.X + col*2 + col/2 - 44, lower.Y + 119, 88, 28));
            }
            return bitmap;
        }

        private void DrawMetric(Graphics g, RectangleF b, string label, string value, Color underline, bool large = false, string unit = null)
        {
            DrawPanel(g, b, 12); DrawLabel(g, label, new RectangleF(b.X, b.Y + 7, b.Width, 22));
            DrawText(g, value, large ? _gear : _value, Brushes.White, new RectangleF(b.X, b.Y + 28, b.Width, 57));
            if (unit != null) DrawText(g, unit, _unit, Brushes.White, new RectangleF(b.Right - 43, b.Y + 61, 39, 20));
            using (var pen = new Pen(underline, 3)) g.DrawLine(pen, b.X + 14, b.Bottom - 11, b.Right - 14, b.Bottom - 11);
        }

        private void DrawStrategy(Graphics g, RectangleF b, string label, string value, string marker)
        {
            DrawPanel(g, b, 12); DrawText(g, label, _label, Brushes.White, new RectangleF(b.X + 4, b.Y + 10, b.Width - 8, 46));
            using (var accent = new SolidBrush(_accent)) DrawText(g, marker, _small, accent, new RectangleF(b.X, b.Y + 62, b.Width, 40));
            DrawText(g, value, _small, Brushes.White, new RectangleF(b.X, b.Y + 104, b.Width, 36));
            using (var pen = new Pen(Color.FromArgb(255, 190, 55), 3)) g.DrawLine(pen, b.X + 15, b.Bottom - 12, b.Right - 15, b.Bottom - 12);
        }

        private void DrawPanel(Graphics g, RectangleF b, float radius)
        {
            using (GraphicsPath p = Rounded(b, radius)) using (var fill = new SolidBrush(Color.FromArgb(225, 12, 20, 28))) using (var border = new Pen(Color.FromArgb(42, 62, 78), 2)) { g.FillPath(fill, p); g.DrawPath(border, p); }
        }
        private void DrawFuelBar(Graphics g, RectangleF b, double fuel) { using (var track = new SolidBrush(Color.FromArgb(25, 37, 48))) g.FillRectangle(track, b); float pct=(float)Math.Max(0,Math.Min(1,fuel/100.0)); using(var fill=new SolidBrush(_accent))g.FillRectangle(fill,b.X,b.Y,b.Width*pct,b.Height); }
        private void DrawLabel(Graphics g,string text,RectangleF b){using(var brush=new SolidBrush(Color.FromArgb(165,190,210)))DrawText(g,text,_label,brush,b);}
        private void DrawText(Graphics g,string text,Font font,Brush brush,RectangleF b)=>g.DrawString(text??"--",font,brush,b,_center);
        private static RectangleF Box(float x,float y,float w,float h)=>new RectangleF(x,y,w,h);
        private static string FormatGear(int gear)=>gear<0?"R":gear==0?"N":gear.ToString();
        private static string FormatTime(double s){if(s<=0)return"--:--.---";TimeSpan t=TimeSpan.FromSeconds(s);return((int)t.TotalMinutes).ToString("0")+t.ToString(@"\:ss\.fff");}
        private static Font FontOf(float px,FontStyle style)=>new Font("Segoe UI",px,style,GraphicsUnit.Pixel);
        private static GraphicsPath Rounded(RectangleF b,float r){var p=new GraphicsPath();float d=r*2;p.AddArc(b.X,b.Y,d,d,180,90);p.AddArc(b.Right-d,b.Y,d,d,270,90);p.AddArc(b.Right-d,b.Bottom-d,d,d,0,90);p.AddArc(b.X,b.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
        public void Dispose(){_label.Dispose();_value.Dispose();_gear.Dispose();_small.Dispose();_unit.Dispose();_center.Dispose();}
    }

    internal static class GraphicsExtensions
    {
        public static void FillRoundedRectangle(this Graphics g, Brush brush, RectangleF rectangle, float radius) { using (GraphicsPath path = new GraphicsPath()) { float d=radius*2; path.AddArc(rectangle.X,rectangle.Y,d,d,180,90);path.AddArc(rectangle.Right-d,rectangle.Y,d,d,270,90);path.AddArc(rectangle.Right-d,rectangle.Bottom-d,d,d,0,90);path.AddArc(rectangle.X,rectangle.Bottom-d,d,d,90,90);path.CloseFigure();g.FillPath(brush,path); } }
    }
}
