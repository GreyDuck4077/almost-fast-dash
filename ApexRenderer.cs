using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace DV5Direct
{
    internal sealed class ApexRenderer : IDashboardRenderer
    {
        private readonly int _width, _height;
        private readonly Color _accent, _dim;
        private readonly Font _tiny = FontOf(11, FontStyle.Bold);
        private readonly Font _label = FontOf(14, FontStyle.Bold);
        private readonly Font _small = FontOf(19, FontStyle.Bold);
        private readonly Font _medium = FontOf(29, FontStyle.Bold);
        private readonly Font _speed = FontOf(43, FontStyle.Bold);
        private readonly Font _gear = FontOf(125, FontStyle.Bold);
        private readonly Font _flag = FontOf(68, FontStyle.Bold);
        private readonly StringFormat _center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };

        public ApexRenderer(int width, int height, DashboardSettings settings)
        {
            _width = width; _height = height; _accent = settings?.Accent ?? Color.FromArgb(53, 208, 197);
            _dim = Color.FromArgb(_accent.R / 3, _accent.G / 3, _accent.B / 3);
        }

        public Bitmap Render(TelemetrySnapshot t, bool shiftFlashOn, int previousGear, float gearTransitionProgress)
        {
            var bitmap = new Bitmap(_width, _height);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                if (t.Flag != RacingFlag.None) { DrawFlag(g, t); return bitmap; }
                DrawBackground(g); DrawHeader(g, t); DrawRpmRibbon(g, t, shiftFlashOn);
                DrawLeft(g, t); DrawFocus(g, t, shiftFlashOn); DrawRight(g, t); DrawTiming(g, t);
            }
            return bitmap;
        }

        private void DrawBackground(Graphics g)
        {
            g.Clear(Color.FromArgb(3, 9, 14));
            using (var glow = new LinearGradientBrush(new Rectangle(0, 0, _width, _height), Color.FromArgb(18, _accent), Color.Transparent, 90f)) g.FillRectangle(glow, 0, 0, _width, _height);
            using (var line = new Pen(Color.FromArgb(8, 90, 110), 1)) for (int x=-480;x<_width;x+=28) g.DrawLine(line,x,_height,x+480,0);
        }

        private void DrawHeader(Graphics g, TelemetrySnapshot t)
        {
            DrawPanel(g, new RectangleF(7, 6, 840, 38), 10);
            DrawText(g, t.ProgressMode == SessionProgressMode.Practice ? "PRACTICE" : "RACE", _label, Brushes.White, new RectangleF(20, 6, 110, 38));
            using (var accent = new SolidBrush(_accent)) DrawText(g, "APEX", _tiny, accent, new RectangleF(370, 6, 114, 38));
            DrawText(g, "LAP " + Lap(t), _label, Brushes.White, new RectangleF(715, 6, 115, 38));
        }

        private void DrawRpmRibbon(Graphics g, TelemetrySnapshot t, bool shift)
        {
            const int segments=16; float gap=4, x=28, y=53, total=798, w=(total-gap*(segments-1))/segments;
            double pct = Math.Max(0, Math.Min(1, t.ShiftIndicatorPct)); int active=(int)Math.Ceiling(pct*segments);
            for(int i=0;i<segments;i++)
            {
                Color color = shift ? Color.Red : i < 10 ? _accent : i < 14 ? Color.FromArgb(255,190,45) : Color.FromArgb(240,55,70);
                using(var brush=new SolidBrush(i<active?color:Color.FromArgb(18,34,43)))
                {
                    PointF[] p={new PointF(x+i*(w+gap)+4,y),new PointF(x+i*(w+gap)+w,y),new PointF(x+i*(w+gap)+w-4,y+25),new PointF(x+i*(w+gap),y+25)};
                    g.FillPolygon(brush,p);
                }
            }
            DrawText(g, t.EngineRpm > 0 ? t.EngineRpm.ToString("0") + " RPM" : "RPM ---", _tiny, Brushes.White, new RectangleF(330, 79, 194, 21));
        }

        private void DrawLeft(Graphics g, TelemetrySnapshot t)
        {
            RectangleF upper=new RectangleF(7,108,238,112), lower=new RectangleF(7,228,238,122);
            DrawPanel(g,upper,14); DrawPanel(g,lower,14);
            DrawLabel(g,"POSITION",new RectangleF(15,116,108,22)); DrawText(g,t.Position>0?"P"+t.Position:"P--",_medium,Brushes.White,new RectangleF(13,139,110,58));
            DrawLabel(g,"LAP",new RectangleF(125,116,108,22)); DrawText(g,Lap(t),_medium,Brushes.White,new RectangleF(123,139,110,58));
            using(var divider=new Pen(_dim,2))g.DrawLine(divider,123,125,123,201);
            DrawLabel(g,"CAR AHEAD",new RectangleF(15,238,220,21)); DrawText(g,t.DriverAhead,_small,Brushes.White,new RectangleF(17,260,216,34));
            using(var accent=new SolidBrush(Color.FromArgb(50,235,135)))DrawText(g,Signed(t.RelativeAheadSeconds),_medium,accent,new RectangleF(17,297,216,43));
        }

        private void DrawFocus(Graphics g, TelemetrySnapshot t, bool shift)
        {
            PointF[] hex={new PointF(309,109),new PointF(545,109),new PointF(586,151),new PointF(586,305),new PointF(545,347),new PointF(309,347),new PointF(268,305),new PointF(268,151)};
            using(var fill=new SolidBrush(Color.FromArgb(230,5,15,22)))g.FillPolygon(fill,hex);
            using(var border=new Pen(shift?Color.Red:_accent,3))g.DrawPolygon(border,hex);
            using(var inner=new Pen(_dim,1))g.DrawPolygon(inner,new[]{new PointF(322,120),new PointF(532,120),new PointF(572,161),new PointF(572,295),new PointF(532,336),new PointF(322,336),new PointF(282,295),new PointF(282,161)});
            DrawLabel(g,"GEAR",new RectangleF(350,119,154,23));
            DrawText(g,t.Connected?Gear(t.Gear):"–",_gear,shift?Brushes.Red:Brushes.White,new RectangleF(320,130,214,139));
            using(var line=new Pen(_dim,2))g.DrawLine(line,341,273,513,273);
            DrawText(g,t.Connected?t.SpeedMph.ToString("0"):"---",_speed,shift?Brushes.Red:Brushes.White,new RectangleF(330,278,194,48));
            DrawText(g,"MPH",_tiny,new SolidBrush(Color.FromArgb(160,185,202)),new RectangleF(330,323,194,20));
        }

        private void DrawRight(Graphics g, TelemetrySnapshot t)
        {
            RectangleF upper=new RectangleF(609,108,238,112),lower=new RectangleF(609,228,238,122);
            DrawPanel(g,upper,14);DrawPanel(g,lower,14);
            DrawLabel(g,"FUEL",new RectangleF(617,116,108,22));DrawText(g,t.FuelLiters.ToString("0.0")+" L",_medium,Brushes.White,new RectangleF(613,139,116,58));
            DrawLabel(g,"INCIDENTS",new RectangleF(731,116,108,22));DrawText(g,t.IncidentPoints.ToString(),_medium,Brushes.White,new RectangleF(731,139,108,58));
            using(var divider=new Pen(_dim,2))g.DrawLine(divider,729,125,729,201);
            DrawLabel(g,"CAR BEHIND",new RectangleF(617,238,220,21));DrawText(g,t.DriverBehind,_small,Brushes.White,new RectangleF(619,260,216,34));
            using(var amber=new SolidBrush(Color.FromArgb(255,198,50)))DrawText(g,Signed(t.RelativeBehindSeconds),_medium,amber,new RectangleF(619,297,216,43));
        }

        private void DrawTiming(Graphics g, TelemetrySnapshot t)
        {
            RectangleF b=new RectangleF(7,360,840,113);DrawPanel(g,b,15);
            DrawLabel(g,"CURRENT LAP",new RectangleF(310,368,234,20));
            using(var amber=new SolidBrush(Color.FromArgb(255,202,48)))DrawText(g,Time(t.CurrentLapSeconds),_medium,amber,new RectangleF(300,389,254,45));
            double delta=Math.Max(-2,Math.Min(2,t.LapDeltaSeconds));float center=427,max=360,len=(float)(Math.Abs(delta)/2*max);
            using(var track=new Pen(Color.FromArgb(38,58,70),16))g.DrawLine(track,55,450,799,450);
            if(delta>0)using(var green=new Pen(Color.FromArgb(30,230,125),16))g.DrawLine(green,center,450,center+len,450);
            else if(delta<0)using(var red=new Pen(Color.FromArgb(235,50,70),16))g.DrawLine(red,center-len,450,center,450);
            using(var tick=new Pen(Color.White,3))g.DrawLine(tick,center,438,center,462);
            DrawText(g,Signed(delta),_small,Brushes.White,new RectangleF(delta>=0?center+18:center-118,431,100,38));
        }

        private void DrawFlag(Graphics g,TelemetrySnapshot t)
        {
            Color bg=t.Flag==RacingFlag.Yellow?Color.FromArgb(255,220,0):t.Flag==RacingFlag.Green?Color.FromArgb(35,205,80):t.Flag==RacingFlag.Red?Color.FromArgb(225,25,38):t.Flag==RacingFlag.White?Color.White:Color.Black;
            g.Clear(bg);Brush fg=(t.Flag==RacingFlag.Yellow||t.Flag==RacingFlag.Green||t.Flag==RacingFlag.White)?Brushes.Black:Brushes.White;
            string label=t.Flag==RacingFlag.BlueYellowStripe?"BLUE":t.Flag==RacingFlag.Surface?"SURFACE":t.Flag==RacingFlag.Meatball?"MEATBALL":t.Flag==RacingFlag.Checkered?"FINISH":t.Flag.ToString().ToUpperInvariant();
            DrawText(g,label,_flag,fg,new RectangleF(0,0,_width,_height));
        }

        private void DrawPanel(Graphics g,RectangleF b,float r){using(GraphicsPath p=Rounded(b,r))using(var fill=new SolidBrush(Color.FromArgb(230,10,20,29)))using(var pen=new Pen(Color.FromArgb(48,72,88),2)){g.FillPath(fill,p);g.DrawPath(pen,p);}}
        private void DrawLabel(Graphics g,string text,RectangleF b){using(var brush=new SolidBrush(Color.FromArgb(145,175,195)))DrawText(g,text,_label,brush,b);}
        private void DrawText(Graphics g,string text,Font f,Brush brush,RectangleF b)=>g.DrawString(text??"---",f,brush,b,_center);
        private static string Lap(TelemetrySnapshot t)=>t.TotalLaps>0?Math.Max(1,t.LapsCompleted+1)+"/"+t.TotalLaps:Math.Max(1,t.LapsCompleted+1).ToString();
        private static string Gear(int g)=>g<0?"R":g==0?"N":g.ToString();private static string Signed(double v)=>v>=0?"+"+v.ToString("0.00"):v.ToString("0.00");
        private static string Time(double s){if(s<=0)return"--:--.---";TimeSpan t=TimeSpan.FromSeconds(s);return((int)t.TotalMinutes).ToString("0")+t.ToString(@"\:ss\.fff");}
        private static Font FontOf(float px,FontStyle style)=>new Font("Segoe UI",px,style,GraphicsUnit.Pixel);
        private static GraphicsPath Rounded(RectangleF b,float r){var p=new GraphicsPath();float d=r*2;p.AddArc(b.X,b.Y,d,d,180,90);p.AddArc(b.Right-d,b.Y,d,d,270,90);p.AddArc(b.Right-d,b.Bottom-d,d,d,0,90);p.AddArc(b.X,b.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
        public void Dispose(){_tiny.Dispose();_label.Dispose();_small.Dispose();_medium.Dispose();_speed.Dispose();_gear.Dispose();_flag.Dispose();_center.Dispose();}
    }
}
