using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace DV5Direct
{
    internal sealed class GearSpeedRenderer : IDashboardRenderer
    {
        private static readonly Color Background = Color.FromArgb(3, 10, 15);
        private static readonly Color Panel = Color.FromArgb(5, 20, 29);
        private readonly Color Cyan;
        private readonly Color CyanDim;
        private static readonly Color Amber = Color.FromArgb(255, 202, 48);
        private static readonly Color Faster = Color.FromArgb(25, 235, 120);
        private static readonly Color Slower = Color.FromArgb(235, 35, 65);
        private static readonly Color Muted = Color.FromArgb(150, 175, 195);
        private readonly int _width;
        private readonly int _height;
        private readonly Font _topFont = FontOf(17, FontStyle.Bold);
        private readonly Font _labelFont = FontOf(13, FontStyle.Bold);
        private readonly Font _nameFont = FontOf(22, FontStyle.Bold);
        private readonly Font _relativeFont = FontOf(43, FontStyle.Bold);
        private readonly Font _gearFont = FontOf(155, FontStyle.Bold);
        private readonly Font _adjacentGearFont = FontOf(39, FontStyle.Bold);
        private readonly Font _speedFont = FontOf(55, FontStyle.Bold);
        private readonly Font _unitFont = FontOf(17, FontStyle.Bold);
        private readonly Font _metricFont = FontOf(34, FontStyle.Bold);
        private readonly Font _metricCompactFont = FontOf(29, FontStyle.Bold);
        private readonly Font _fuelFont = FontOf(34, FontStyle.Bold);
        private readonly Font _lapTimeFont = FontOf(40, FontStyle.Bold);
        private readonly Font _scaleFont = FontOf(15, FontStyle.Bold);
        private readonly Font _deltaFont = FontOf(22, FontStyle.Bold);
        private readonly Font _summaryLabelFont = FontOf(15, FontStyle.Bold);
        private readonly Font _summaryValueFont = FontOf(27, FontStyle.Bold);
        private readonly Font _summarySideFont = FontOf(42, FontStyle.Bold);
        private readonly Font _flagFont = FontOf(68, FontStyle.Bold);
        private readonly Font _flagSmallFont = FontOf(34, FontStyle.Bold);
        private readonly StringFormat _center = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap,
            Trimming = StringTrimming.EllipsisCharacter
        };

        public GearSpeedRenderer(int width, int height, DashboardSettings settings)
        {
            _width = width;
            _height = height;
            Cyan = settings?.Accent ?? Color.FromArgb(0, 155, 210);
            CyanDim = Color.FromArgb(Cyan.R * 2 / 5, Cyan.G * 2 / 5, Cyan.B * 2 / 5);
        }

        public Bitmap Render(
            TelemetrySnapshot telemetry,
            bool shiftFlashOn,
            int previousGear,
            float gearTransitionProgress)
        {
            var bitmap = new Bitmap(_width, _height);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                if (telemetry.Flag != RacingFlag.None) { DrawFlag(g, telemetry); return bitmap; }
                DrawBackground(g);
                DrawSessionHeader(g, telemetry);
                DrawRelativePanel(g, new RectangleF(5, 48, 285, 158), "BEHIND", telemetry.DriverBehind, telemetry.RelativeBehindSeconds, false);
                DrawRelativePanel(g, new RectangleF(564, 48, 285, 158), "AHEAD", telemetry.DriverAhead, telemetry.RelativeAheadSeconds, true);
                DrawRacePanel(g, telemetry);
                DrawIncidentFuelPanel(g, telemetry);
                DrawCenter(g, telemetry, shiftFlashOn, previousGear, gearTransitionProgress);
                DrawDeltaPanel(g, telemetry);
                if (telemetry.ShowLapSummary) DrawLapSummary(g, telemetry);
            }
            return bitmap;
        }

        private void DrawBackground(Graphics g)
        {
            g.Clear(Background);
            using (var line = new Pen(Color.FromArgb(8, 60, 72), 1))
                for (int x = -_height; x < _width; x += 24) g.DrawLine(line, x, _height, x + _height, 0);
        }

        private void DrawSessionHeader(Graphics g, TelemetrySnapshot t)
        {
            DrawAngularPanel(g, new RectangleF(5, 5, 844, 38), 8);
            DrawText(g, t.ProgressMode == SessionProgressMode.Practice ? "PRACTICE" : "RACE", _topFont, Brushes.White, new RectangleF(20, 5, 110, 38));
            DrawText(g, "LAP " + FormatLap(t), _topFont, Brushes.White, new RectangleF(720, 5, 115, 38));
            RectangleF bar = new RectangleF(135, 17, 575, 14);
            using (var track = new SolidBrush(Color.FromArgb(12, 55, 78))) g.FillRectangle(track, bar);
            if (t.ProgressMode == SessionProgressMode.LapRace && t.TotalLaps > 0)
                DrawLapBlocks(g, bar, t.LapsCompleted, t.TotalLaps, t.YellowCompletedLaps);
            else if (t.ProgressMode == SessionProgressMode.TimedRace && t.SessionTotalSeconds > 0)
            {
                double fraction = Clamp(t.SessionSecondsRemaining / t.SessionTotalSeconds, 0, 1);
                Color progressColor = fraction <= 0.25
                    ? Color.FromArgb(230, 45, 55)
                    : fraction <= 0.60
                        ? Color.FromArgb(255, 210, 0)
                        : Cyan;
                using (var fill = new SolidBrush(progressColor))
                    g.FillRectangle(fill, bar.X, bar.Y, (float)(bar.Width * fraction), bar.Height);
            }
            else if (t.ProgressMode == SessionProgressMode.Practice)
                DrawText(g, FormatRemainingTime(t.SessionSecondsRemaining), _labelFont, Brushes.White, bar);
        }

        private void DrawRelativePanel(Graphics g, RectangleF b, string label, string driver, double relative, bool ahead)
        {
            DrawAngularPanel(g, b, 16);
            using (var cyan = new SolidBrush(Cyan)) DrawText(g, label, _topFont, cyan, new RectangleF(b.X, b.Y + 13, b.Width, 30));
            DrawText(g, Truncate(driver, 17), _nameFont, Brushes.White, new RectangleF(b.X + 8, b.Y + 53, b.Width - 16, 35));
            using (var color = new SolidBrush(ahead ? Faster : Amber))
                DrawText(g, FormatSigned(relative), _relativeFont, color, new RectangleF(b.X + 8, b.Y + 91, b.Width - 16, 58));
        }

        private void DrawRacePanel(Graphics g, TelemetrySnapshot t)
        {
            DrawAngularPanel(g, new RectangleF(5, 211, 285, 130), 14);
            using (var divider = new Pen(CyanDim, 2)) g.DrawLine(divider, 145, 232, 145, 321);
            string position = t.Position > 0
                ? t.Position + (t.TotalStarters > 0 ? "/" + t.TotalStarters : "")
                : "--";
            DrawMetric(g, "POSITION", position, new RectangleF(12, 223, 126, 104));
            DrawMetric(g, "LAP", FormatLap(t), new RectangleF(152, 223, 126, 104));
        }

        private void DrawIncidentFuelPanel(Graphics g, TelemetrySnapshot t)
        {
            DrawAngularPanel(g, new RectangleF(564, 211, 285, 130), 14);
            using (var divider = new Pen(CyanDim, 2)) g.DrawLine(divider, 716, 232, 716, 321);
            DrawMetric(g, "INCIDENTS", t.IncidentPoints.ToString(), new RectangleF(572, 223, 136, 104));
            using (var muted = new SolidBrush(Muted)) DrawText(g, "FUEL", _labelFont, muted, new RectangleF(722, 230, 119, 24));
            DrawText(g, t.FuelLiters.ToString("0.0") + " L", _fuelFont, Brushes.White, new RectangleF(722, 258, 119, 56));
        }

        private void DrawCenter(
            Graphics g,
            TelemetrySnapshot t,
            bool shiftFlashOn,
            int previousGear,
            float transitionProgress)
        {
            Brush primary = shiftFlashOn ? Brushes.Red : Brushes.White;
            bool animating = previousGear != t.Gear && transitionProgress >= 0 && transitionProgress < 1;
            if (!animating)
            {
                using (var adjacent = new SolidBrush(Color.FromArgb(82, 98, 108)))
                {
                    DrawText(g, PreviousGear(t.Gear), _adjacentGearFont, adjacent,
                        new RectangleF(296, 105, 62, 70));
                    DrawText(g, NextGear(t.Gear, t.MaxForwardGear), _adjacentGearFont, adjacent,
                        new RectangleF(496, 105, 62, 70));
                }
                DrawText(g, t.Connected ? FormatGear(t.Gear) : "–", _gearFont, primary,
                    new RectangleF(292, 44, 270, 205));
            }
            else
            {
                DrawGearTransition(g, previousGear, t.Gear, transitionProgress, shiftFlashOn);
            }
            using (var line = new Pen(CyanDim, 2)) g.DrawLine(line, 350, 241, 504, 241);
            DrawText(g, t.Connected ? t.SpeedMph.ToString("0") : "---", _speedFont, primary, new RectangleF(300, 242, 254, 68));
            using (var muted = new SolidBrush(Muted)) DrawText(g, "MPH", _unitFont, muted, new RectangleF(300, 307, 254, 28));
        }

        private void DrawGearTransition(
            Graphics g, int previousGear, int currentGear, float progress, bool shiftFlashOn)
        {
            progress = (float)Clamp(progress, 0, 1);
            float direction = currentGear > previousGear ? -1f : 1f;
            float arc = (float)Math.Sin(progress * Math.PI) * 26f;
            float oldX = 292 + (direction * progress * 108f);
            float newX = 292 - (direction * (1f - progress) * 108f);
            float oldY = 44 + arc;
            float newY = 44 - arc;
            Color baseColor = shiftFlashOn ? Color.Red : Color.White;
            using (var oldBrush = new SolidBrush(Color.FromArgb(
                Math.Max(0, Math.Min(255, (int)(255 * (1f - progress)))), baseColor)))
            using (var newBrush = new SolidBrush(Color.FromArgb(
                Math.Max(0, Math.Min(255, (int)(255 * progress))), baseColor)))
            {
                DrawText(g, FormatGear(previousGear), _gearFont, oldBrush,
                    new RectangleF(oldX, oldY, 270, 205));
                DrawText(g, FormatGear(currentGear), _gearFont, newBrush,
                    new RectangleF(newX, newY, 270, 205));
            }
        }

        private void DrawDeltaPanel(Graphics g, TelemetrySnapshot t)
        {
            DrawAngularPanel(g, new RectangleF(5, 346, 844, 129), 14);
            const float leftOuter = 30;
            const float leftInner = 275;
            const float rightInner = 579;
            const float rightOuter = 824;
            const float barY = 366;
            const float barHeight = 88;

            using (var redTrack = new SolidBrush(Color.FromArgb(70, 105, 15, 30)))
                g.FillRectangle(redTrack, leftOuter, barY, leftInner - leftOuter, barHeight);
            using (var greenTrack = new SolidBrush(Color.FromArgb(70, 0, 90, 55)))
                g.FillRectangle(greenTrack, rightInner, barY, rightOuter - rightInner, barHeight);

            using (var muted = new SolidBrush(Muted))
                DrawText(g, "CURRENT LAP", _labelFont, muted, new RectangleF(leftInner, 372, rightInner - leftInner, 21));
            using (var amber = new SolidBrush(Amber))
                DrawText(g, FormatLapTime(t.CurrentLapSeconds), _lapTimeFont, amber,
                    new RectangleF(leftInner, 393, rightInner - leftInner, 55));

            double delta = Clamp(t.LapDeltaSeconds, -2, 2);
            float length = (float)(Math.Abs(delta) / 2.0 * (rightOuter - rightInner));
            if (delta > 0)
            {
                using (var fill = new SolidBrush(Faster))
                    g.FillRectangle(fill, rightInner, barY, length, barHeight);
                DrawTextWithBackdrop(g, FormatSigned(delta), _deltaFont,
                    new RectangleF(rightInner, barY, rightOuter - rightInner, barHeight));
            }
            else if (delta < 0)
            {
                using (var fill = new SolidBrush(Slower))
                    g.FillRectangle(fill, leftInner - length, barY, length, barHeight);
                DrawTextWithBackdrop(g, FormatSigned(delta), _deltaFont,
                    new RectangleF(leftOuter, barY, leftInner - leftOuter, barHeight));
            }
        }

        private void DrawTextWithBackdrop(Graphics g, string text, Font font, RectangleF bounds)
        {
            SizeF measured = g.MeasureString(text, font);
            const float horizontalPadding = 6;
            const float verticalPadding = 3;
            var backdrop = new RectangleF(
                bounds.X + ((bounds.Width - measured.Width) / 2) - horizontalPadding,
                bounds.Y + ((bounds.Height - measured.Height) / 2) - verticalPadding,
                measured.Width + (horizontalPadding * 2),
                measured.Height + (verticalPadding * 2));
            using (var black = new SolidBrush(Color.FromArgb(225, 0, 0, 0)))
                g.FillRectangle(black, backdrop);
            DrawText(g, text, font, Brushes.White, bounds);
        }

        private void DrawLapSummary(Graphics g, TelemetrySnapshot t)
        {
            RectangleF band = new RectangleF(0, 145, _width, 175);
            g.FillRectangle(Brushes.White, band);
            using (var edge = new Pen(Color.FromArgb(185, 215, 225), 3))
            {
                g.DrawLine(edge, 0, band.Top, _width, band.Top);
                g.DrawLine(edge, 0, band.Bottom, _width, band.Bottom);
            }

            Brush dark = Brushes.Black;
            DrawText(g, "POSITION", _summaryLabelFont, dark, new RectangleF(15, 165, 155, 25));
            string position = t.Position > 0
                ? t.Position + (t.TotalStarters > 0 ? "/" + t.TotalStarters : "")
                : "--";
            DrawText(g, position, _summarySideFont, dark, new RectangleF(15, 193, 155, 75));

            DrawText(g, "LAP", _summaryLabelFont, dark, new RectangleF(684, 165, 155, 25));
            DrawText(g, FormatLap(t), _summarySideFont, dark, new RectangleF(684, 193, 155, 75));

            using (var divider = new Pen(Color.FromArgb(175, 190, 198), 2))
            {
                g.DrawLine(divider, 185, 167, 185, 297);
                g.DrawLine(divider, 669, 167, 669, 297);
            }

            DrawSummaryTime(g, "BEST", t.BestLapSeconds, new RectangleF(200, 160, 145, 125));
            DrawSummaryTime(g, "LAST", t.LastLapSeconds, new RectangleF(355, 160, 145, 125));
            DrawSummaryTime(g, "AVERAGE", t.AverageLapSeconds, new RectangleF(510, 160, 145, 125));
        }

        private void DrawSummaryTime(Graphics g, string label, double seconds, RectangleF bounds)
        {
            DrawText(g, label, _summaryLabelFont, Brushes.Black,
                new RectangleF(bounds.X, bounds.Y, bounds.Width, 28));
            DrawText(g, FormatLapTime(seconds), _summaryValueFont, Brushes.Black,
                new RectangleF(bounds.X, bounds.Y + 28, bounds.Width, 63));
        }

        private void DrawMetric(Graphics g, string label, string value, RectangleF b)
        {
            using (var muted = new SolidBrush(Muted)) DrawText(g, label, _labelFont, muted, new RectangleF(b.X, b.Y, b.Width, 25));
            Font valueFont = value != null && value.Length >= 7 ? _metricCompactFont : _metricFont;
            DrawText(g, value, valueFont, Brushes.White, new RectangleF(b.X, b.Y + 31, b.Width, b.Height - 31));
        }

        private void DrawAngularPanel(Graphics g, RectangleF b, float cut)
        {
            PointF[] p = { new PointF(b.Left+cut,b.Top), new PointF(b.Right-cut,b.Top), new PointF(b.Right,b.Top+cut), new PointF(b.Right,b.Bottom-cut), new PointF(b.Right-cut,b.Bottom), new PointF(b.Left+cut,b.Bottom), new PointF(b.Left,b.Bottom-cut), new PointF(b.Left,b.Top+cut) };
            using (var fill = new SolidBrush(Color.FromArgb(225, Panel))) g.FillPolygon(fill, p);
            using (var border = new Pen(Cyan, 2)) g.DrawPolygon(border, p);
        }

        private void DrawLapBlocks(
            Graphics g, RectangleF bar, int completed, int total, bool[] yellowLaps)
        {
            completed = Math.Max(0, Math.Min(total, completed));
            float gap = total <= 50 ? 2 : 1, block = (bar.Width - ((total - 1) * gap)) / total;
            if (block < 1) { gap = 0; block = bar.Width / total; }
            using (var pending = new SolidBrush(Color.FromArgb(20, 65, 85)))
            using (var complete = new SolidBrush(Cyan))
            using (var yellow = new SolidBrush(Color.FromArgb(255, 215, 0)))
                for (int lap = 0; lap < total; lap++)
                {
                    Brush brush = lap >= completed ? pending :
                        yellowLaps != null && lap < yellowLaps.Length && yellowLaps[lap]
                            ? yellow
                            : complete;
                    g.FillRectangle(brush, bar.X + lap * (block + gap), bar.Y, block, bar.Height);
                }
        }

        private static string FormatLap(TelemetrySnapshot t) => t.TotalLaps > 0 ? Math.Max(1,t.LapsCompleted+1)+"/"+t.TotalLaps : Math.Max(1,t.LapsCompleted+1).ToString();
        private static string FormatLapTime(double seconds) { if (seconds <= 0 || double.IsNaN(seconds)) return "--:--.---"; TimeSpan time=TimeSpan.FromSeconds(seconds); return ((int)time.TotalMinutes).ToString("0")+time.ToString(@"\:ss\.fff"); }
        private static string FormatRemainingTime(double seconds) { if(seconds<0||double.IsNaN(seconds)) return "--:--"; TimeSpan t=TimeSpan.FromSeconds(Math.Ceiling(seconds)); return t.TotalHours>=1?((int)t.TotalHours).ToString("0")+t.ToString(@"\:mm\:ss"):t.ToString(@"mm\:ss"); }
        private static string FormatSigned(double value) => value >= 0 ? "+" + value.ToString("0.00") : value.ToString("0.00");
        private static string FormatGear(int gear) => gear < 0 ? "R" : gear == 0 ? "N" : gear.ToString();
        private static string PreviousGear(int gear) =>
            gear < 0 ? "–" : gear == 0 ? "R" : gear == 1 ? "N" : (gear - 1).ToString();
        private static string NextGear(int gear, int maxForwardGear)
        {
            if (gear < 0) return "N";
            if (gear == 0) return "1";
            return maxForwardGear > 0 && gear >= maxForwardGear ? "–" : (gear + 1).ToString();
        }
        private static string Truncate(string value,int length)=>string.IsNullOrEmpty(value)||value.Length<=length?value:value.Substring(0,length-1)+"…";
        private static double Clamp(double value,double min,double max)=>Math.Max(min,Math.Min(max,value));
        private static Font FontOf(float pixels,FontStyle style)=>new Font("Segoe UI",pixels,style,GraphicsUnit.Pixel);
        private void DrawText(Graphics g,string text,Font font,Brush brush,RectangleF b)=>g.DrawString(text??"---",font,brush,b,_center);

        private void DrawFlag(Graphics g,TelemetrySnapshot telemetry)
        {
            RacingFlag flag = telemetry.Flag;
            switch(flag)
            {
                case RacingFlag.Green: DrawSolidFlag(g,Color.FromArgb(30,205,70),Brushes.Black,"GREEN"); break;
                case RacingFlag.White: DrawSolidFlag(g,Color.White,Brushes.Black,"WHITE"); break;
                case RacingFlag.Yellow: DrawSolidFlag(g,Color.FromArgb(255,220,0),Brushes.Black,"YELLOW"); break;
                case RacingFlag.Red: DrawSolidFlag(g,Color.FromArgb(225,20,35),Brushes.White,"RED"); break;
                case RacingFlag.Black: g.Clear(Color.Black); using(var pen=new Pen(Color.White,8)) g.DrawRectangle(pen,18,18,_width-36,_height-36); DrawText(g,"BLACK",_flagFont,Brushes.White,new RectangleF(0,0,_width,_height)); break;
                case RacingFlag.Meatball: g.Clear(Color.Black); using(var orange=new SolidBrush(Color.FromArgb(245,95,30))) g.FillEllipse(orange,(_width-245)/2,65,245,245); DrawText(g,"MEATBALL",_flagSmallFont,Brushes.White,new RectangleF(0,340,_width,80)); break;
                case RacingFlag.BlueYellowStripe: g.Clear(Color.FromArgb(35,115,210)); using(var stripe=new Pen(Color.FromArgb(255,220,0),90)) g.DrawLine(stripe,-40,_height+40,_width+40,-40); DrawFlagBackdrop(g,"BLUE"); break;
                case RacingFlag.SafetyCar: DrawSolidFlag(g,Color.White,Brushes.Black,"SC"); break;
                case RacingFlag.Surface: for(int x=0;x<_width;x+=95) using(var brush=new SolidBrush((x/95)%2==0?Color.FromArgb(255,220,0):Color.FromArgb(220,25,35))) g.FillRectangle(brush,x,0,95,_height); DrawFlagBackdrop(g,"SURFACE"); break;
                case RacingFlag.Checkered:
                    float cw=_width/10f,ch=_height/6f;
                    for(int r=0;r<6;r++) for(int c=0;c<10;c++)
                        g.FillRectangle(((r+c)&1)==0?Brushes.White:Brushes.Black,c*cw,r*ch,cw+1,ch+1);
                    using(var backdrop=new SolidBrush(Color.FromArgb(225,0,0,0)))
                        g.FillRectangle(backdrop,235,125,384,230);
                    DrawText(g,"FINISH",_flagSmallFont,Brushes.White,new RectangleF(235,145,384,70));
                    DrawText(g,telemetry.Position>0?"P"+telemetry.Position:"P--",_flagFont,Brushes.White,new RectangleF(235,210,384,120));
                    break;
            }
        }

        private void DrawSolidFlag(Graphics g,Color background,Brush foreground,string label) { g.Clear(background); DrawText(g,label,label=="SC"?_gearFont:_flagFont,foreground,new RectangleF(0,0,_width,_height)); }
        private void DrawFlagBackdrop(Graphics g,string label) { using(var backdrop=new SolidBrush(Color.FromArgb(205,0,0,0))) g.FillRectangle(backdrop,205,182,_width-410,116); DrawText(g,label,_flagSmallFont,Brushes.White,new RectangleF(205,182,_width-410,116)); }

        public void Dispose()
        {
            foreach(Font font in new[]{_topFont,_labelFont,_nameFont,_relativeFont,_gearFont,_adjacentGearFont,_speedFont,_unitFont,_metricFont,_metricCompactFont,_fuelFont,_lapTimeFont,_scaleFont,_deltaFont,_summaryLabelFont,_summaryValueFont,_summarySideFont,_flagFont,_flagSmallFont}) font.Dispose();
            _center.Dispose();
        }
    }
}
