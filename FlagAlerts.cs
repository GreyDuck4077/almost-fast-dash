using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Linq;

namespace AlmostFastOverlays
{
    /// <summary>What the flag system shows at one moment: a flag over the screen, a frame round the dashboard, or both while one hands over to the other.</summary>
    internal struct FlagView
    {
        /// <summary>The flag covering the screen, or None.</summary>
        public RacingFlag Takeover;
        /// <summary>How opaque that flag is: 0 gone, 1 the whole screen.</summary>
        public float Alpha;
        /// <summary>The flag whose coloured frame runs round the dashboard, or None.</summary>
        public RacingFlag Frame;
        public float FrameAlpha;

        public bool Full => Takeover != RacingFlag.None && Alpha >= 0.999f;
    }

    /// <summary>
    /// Turns the telemetry's flag into what the screen shows. A new flag fades in over the whole screen and
    /// holds steady: the telemetry's on-off flashing is for the lights, so its short gaps are bridged here.
    /// Flags that stay out (black, repairs, blue, surface) step aside after a few seconds to a frame in their
    /// colour round the dashboard; so do a caution period and the final lap once their alert is over. Red
    /// and checkered keep the screen.
    /// </summary>
    internal sealed class FlagTimeline
    {
        public const int FadeIn = 180, FadeOut = 280, TakeoverMilliseconds = 3000, StepAside = 450;
        /// <summary>A flag that stays out and comes back within this long (a blue flag for each faster car) goes straight to its frame.</summary>
        public const int Remember = 20000;

        private readonly int _grace;
        private RacingFlag _flag;
        private int _since, _lastSeen;
        private readonly Dictionary<RacingFlag, int> _seen = new Dictionary<RacingFlag, int>();

        /// <param name="flashMilliseconds">How long each flash of the lights lasts: gaps that short don't end a flag.</param>
        public FlagTimeline(int flashMilliseconds) => _grace = Math.Max(400, flashMilliseconds + 200);

        /// <summary>Flags that can stay out for a long time, so they hand the screen back after their alert.</summary>
        public static bool StaysOut(RacingFlag flag) =>
            flag == RacingFlag.Black || flag == RacingFlag.Meatball || flag == RacingFlag.BlueYellowStripe ||
            flag == RacingFlag.Surface || flag == RacingFlag.SafetyCar;

        /// <summary>Forgets every flag, so the next one alerts in full (the designer's preview loops this way).</summary>
        public void Reset()
        {
            _flag = RacingFlag.None;
            _seen.Clear();
        }

        /// <param name="takeovers">Flags may cover the screen.</param>
        /// <param name="frames">Flags that are out may frame the dashboard.</param>
        public FlagView At(TelemetrySnapshot t, int now, bool takeovers, bool frames)
        {
            RacingFlag flag = t?.Flag ?? RacingFlag.None;
            if (flag != RacingFlag.None)
            {
                if (flag != _flag)
                {
                    // Straight from one flag to another: the new one doesn't fade in again from nothing.
                    _since = _flag != RacingFlag.None && Alpha(now) > 0.5f ? now - FadeIn : now;
                    if (StaysOut(flag) && _seen.TryGetValue(flag, out int last) && now - last < Remember)
                        _since = now - TakeoverMilliseconds - StepAside;
                    _flag = flag;
                }
                _lastSeen = now;
                _seen[flag] = now;
            }
            else if (_flag != RacingFlag.None && now - _lastSeen > _grace + FadeOut)
                _flag = RacingFlag.None;

            var view = new FlagView();
            if (takeovers && _flag != RacingFlag.None)
            {
                view.Alpha = Alpha(now);
                if (view.Alpha > 0) view.Takeover = _flag;
            }
            if (frames)
            {
                if (_flag != RacingFlag.None && (StaysOut(_flag) || !takeovers))
                {
                    view.Frame = _flag;
                    view.FrameAlpha = Ease(Clamp((now - _since) / (float)FadeIn) * Presence(now));
                }
                else if (t != null && t.CautionActive)
                {
                    view.Frame = RacingFlag.Yellow;
                    view.FrameAlpha = 1;
                }
                else if (t != null && t.WhiteFlagActive)
                {
                    view.Frame = RacingFlag.White;
                    view.FrameAlpha = 1;
                }
                if (view.FrameAlpha <= 0) view.Frame = RacingFlag.None;
            }
            return view;
        }

        private float Alpha(int now)
        {
            if (_flag == RacingFlag.None) return 0;
            int age = now - _since;
            float alpha = Clamp(age / (float)FadeIn);
            if (StaysOut(_flag)) alpha = Math.Min(alpha, 1 - Clamp((age - TakeoverMilliseconds) / (float)StepAside));
            return Ease(alpha * Presence(now));
        }

        /// <summary>1 while the flag is out (or only between two flashes), falling to 0 once it has been withdrawn.</summary>
        private float Presence(int now)
        {
            int gone = now - _lastSeen;
            return gone <= _grace ? 1 : 1 - Clamp((gone - _grace) / (float)FadeOut);
        }

        private static float Clamp(float v) => v < 0 ? 0 : v > 1 ? 1 : v;

        private static float Ease(float v) => v * v * (3 - 2 * v);
    }

    /// <summary>Each flag's colours and words, shared by the full-screen flag and its frame.</summary>
    internal static class FlagStyle
    {
        public static readonly Color Ink = Color.FromArgb(10, 12, 16);

        /// <summary>The colour of the frame and its tab.</summary>
        public static Color Of(RacingFlag flag)
        {
            switch (flag)
            {
                case RacingFlag.Green: return Color.FromArgb(48, 209, 88);
                case RacingFlag.Yellow:
                case RacingFlag.SafetyCar: return Color.FromArgb(255, 214, 10);
                case RacingFlag.Red: return Color.FromArgb(255, 59, 48);
                case RacingFlag.Meatball: return Color.FromArgb(255, 106, 26);
                case RacingFlag.BlueYellowStripe: return Color.FromArgb(47, 127, 255);
                case RacingFlag.Surface: return Color.FromArgb(255, 176, 0);
                // White, checkered and black: a black frame would vanish into a dark dashboard.
                default: return Color.FromArgb(242, 242, 242);
            }
        }

        /// <summary>The words on the frame's tab.</summary>
        public static string Banner(RacingFlag flag)
        {
            switch (flag)
            {
                case RacingFlag.Green: return "GREEN FLAG";
                case RacingFlag.Yellow: return "CAUTION";
                case RacingFlag.White: return "FINAL LAP";
                case RacingFlag.Red: return "RED FLAG";
                case RacingFlag.Black: return "BLACK FLAG  ·  PIT IN";
                case RacingFlag.Meatball: return "DAMAGE  ·  PIT FOR REPAIRS";
                case RacingFlag.BlueYellowStripe: return "BLUE FLAG  ·  LET THEM BY";
                case RacingFlag.Surface: return "SLIPPERY SURFACE";
                case RacingFlag.SafetyCar: return "SAFETY CAR";
                case RacingFlag.Checkered: return "CHECKERED FLAG";
                default: return "";
            }
        }

        public static Color TabFill(RacingFlag flag) => flag == RacingFlag.Black ? Color.FromArgb(14, 15, 18) : Of(flag);

        public static Color TabInk(RacingFlag flag) =>
            flag == RacingFlag.Black || flag == RacingFlag.Red || flag == RacingFlag.BlueYellowStripe ? Color.White : Ink;
    }

    /// <summary>Tracked capitals for the flag screens: Bahnschrift, the cluster face, with Segoe UI as the fallback.</summary>
    internal static class FlagType
    {
        private static string _bold, _semi;

        public static string Bold => _bold ?? (_bold = Installed("Bahnschrift") ? "Bahnschrift" : "Segoe UI");

        public static string Semi => _semi ?? (_semi = Installed("Bahnschrift SemiBold") ? "Bahnschrift SemiBold" : Installed("Segoe UI Semibold") ? "Segoe UI Semibold" : "Segoe UI");

        private static bool Installed(string family)
        {
            using (var installed = new InstalledFontCollection())
                return installed.Families.Any(f => f.Name == family);
        }

        /// <summary>The text as an outline, letter-spaced by <paramref name="tracking"/> pixels, from the origin.</summary>
        public static GraphicsPath Path(Graphics g, string text, string family, FontStyle style, float size, float tracking)
        {
            // Winding fill: Bahnschrift's letters are built from overlapping outlines, which alternate fill would cut holes in.
            var path = new GraphicsPath(FillMode.Winding);
            using (var fontFamily = new FontFamily(family))
            using (var font = new Font(fontFamily, size, style, GraphicsUnit.Pixel))
            using (var format = (StringFormat)StringFormat.GenericTypographic.Clone())
            {
                format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
                float x = 0;
                foreach (char c in text)
                {
                    string letter = c.ToString();
                    if (c != ' ') path.AddString(letter, fontFamily, (int)style, size, new PointF(x, 0), format);
                    x += g.MeasureString(letter, font, PointF.Empty, format).Width + tracking;
                }
            }
            return path;
        }

        public static SizeF Measure(Graphics g, string text, string family, FontStyle style, float size, float tracking)
        {
            using (GraphicsPath path = Path(g, text, family, style, size, tracking))
                return path.GetBounds().Size;
        }

        /// <summary>Fills text centred on a point by its ink, shrunk to <paramref name="maxWidth"/> if it is wider.</summary>
        public static void Draw(Graphics g, string text, string family, FontStyle style, float size, float tracking, PointF center, Color color, float maxWidth = 0)
        {
            if (string.IsNullOrEmpty(text)) return;
            using (GraphicsPath path = Path(g, text, family, style, size, tracking))
            {
                RectangleF ink = path.GetBounds();
                float scale = maxWidth > 0 && ink.Width > maxWidth ? maxWidth / ink.Width : 1;
                using (var m = new Matrix())
                {
                    m.Translate(center.X, center.Y);
                    m.Scale(scale, scale);
                    m.Translate(-(ink.X + ink.Width / 2), -(ink.Y + ink.Height / 2));
                    path.Transform(m);
                }
                using (var brush = new SolidBrush(color)) g.FillPath(brush, path);
            }
        }
    }

    /// <summary>
    /// The full-screen flags: each a cloth in the flag's colours with a title and what to do, set in
    /// tracked capitals. Patterned flags carry their words on a dark glass panel. The finish shows the
    /// result. Drawn to fit any screen size; the last picture is kept until the flag or result changes.
    /// </summary>
    internal sealed class FlagPainter : IDisposable
    {
        private readonly int _w, _h;
        private readonly float _s, _cx, _cy;
        private Bitmap _picture;
        private string _pictureKey;

        public FlagPainter(int width, int height)
        {
            _w = width;
            _h = height;
            _s = Math.Min(width / 854f, height / 480f);
            _cx = width / 2f;
            _cy = height / 2f;
        }

        /// <summary>What a flag's picture depends on: the finish also shows the result.</summary>
        public static string Key(RacingFlag flag, TelemetrySnapshot t) =>
            flag != RacingFlag.Checkered || t == null ? flag.ToString()
                : flag + "|" + t.Position + "|" + t.ClassPosition + "|" + t.StartPosition + "|" + t.BestLapSeconds.ToString("R");

        /// <summary>The flag's picture, drawn once and kept while it shows the same thing. Owned by the painter.</summary>
        public Bitmap Picture(RacingFlag flag, TelemetrySnapshot t)
        {
            string key = Key(flag, t);
            if (_picture != null && key == _pictureKey) return _picture;
            if (_picture == null) _picture = new Bitmap(_w, _h, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(_picture))
            {
                DashKit.Prepare(g);
                _canvas = _picture;
                Draw(g, flag, t);
            }
            _pictureKey = key;
            return _picture;
        }

        /// <summary>The bitmap being drawn, so glass can frost what is already on it.</summary>
        private Bitmap _canvas;

        private void Draw(Graphics g, RacingFlag flag, TelemetrySnapshot t)
        {
            t = t ?? TelemetrySnapshot.Disconnected;
            switch (flag)
            {
                case RacingFlag.Green: Solid(g, Color.FromArgb(34, 196, 84), FlagStyle.Ink, "GREEN", "GO  ·  GO  ·  GO"); break;
                case RacingFlag.Yellow: Solid(g, Color.FromArgb(255, 212, 0), FlagStyle.Ink, "CAUTION", "SLOW DOWN  ·  NO OVERTAKING"); break;
                case RacingFlag.White: Solid(g, Color.FromArgb(244, 244, 240), FlagStyle.Ink, "FINAL LAP", "WHITE FLAG"); break;
                case RacingFlag.Red: Solid(g, Color.FromArgb(222, 30, 38), Color.White, "RED FLAG", "SESSION STOPPED"); break;
                case RacingFlag.SafetyCar: Solid(g, Color.FromArgb(255, 212, 0), FlagStyle.Ink, "SAFETY CAR", "HOLD YOUR POSITION"); break;
                case RacingFlag.Black: Black(g); break;
                case RacingFlag.Meatball: Meatball(g); break;
                case RacingFlag.BlueYellowStripe: Blue(g); break;
                case RacingFlag.Surface: Surface(g); break;
                case RacingFlag.Checkered: Checkered(g, t); break;
                default: g.Clear(Color.Black); break;
            }
        }

        // ------------------------------------------------------------------ the flags

        private void Solid(Graphics g, Color color, Color ink, string title, string subtitle)
        {
            Cloth(g, color);
            Words(g, title, subtitle, ink, _cy, 100, 25);
        }

        private void Black(Graphics g)
        {
            Cloth(g, Color.FromArgb(20, 22, 26));
            float s = _s, inset = 22 * s;
            using (var pen = new Pen(Color.FromArgb(244, 244, 240), 8 * s))
            using (GraphicsPath border = DashKit.Rounded(new RectangleF(inset, inset, _w - inset * 2, _h - inset * 2), 26 * s))
                g.DrawPath(pen, border);
            Words(g, "BLACK FLAG", "PIT TO SERVE YOUR PENALTY", Color.White, _cy, 92, 24);
        }

        private void Meatball(Graphics g)
        {
            Cloth(g, Color.FromArgb(20, 22, 26));
            float s = _s, r = 78 * s;
            var center = new PointF(_cx, _cy - 72 * s);
            // A warm glow round the disc, then the disc lit from the top left.
            using (var brush = new SolidBrush(Color.FromArgb(7, 255, 106, 26)))
                for (int i = 16; i >= 1; i--)
                {
                    float glow = r + i * 2.6f * s;
                    g.FillEllipse(brush, center.X - glow, center.Y - glow, glow * 2, glow * 2);
                }
            using (var disc = new GraphicsPath())
            {
                disc.AddEllipse(center.X - r, center.Y - r, r * 2, r * 2);
                using (var brush = new PathGradientBrush(disc)
                {
                    CenterPoint = new PointF(center.X - r * 0.3f, center.Y - r * 0.35f),
                    CenterColor = Color.FromArgb(255, 150, 72),
                    SurroundColors = new[] { Color.FromArgb(222, 78, 12) }
                })
                    g.FillPath(brush, disc);
            }
            Words(g, "DAMAGE", "PIT FOR REPAIRS", Color.White, _cy + 96 * s, 68, 22);
        }

        private void Blue(Graphics g)
        {
            Cloth(g, Color.FromArgb(28, 98, 232));
            float s = _s;
            GraphicsState state = g.Save();
            g.TranslateTransform(_cx, _cy);
            g.RotateTransform((float)(-Math.Atan2(_h, _w) * 180 / Math.PI));
            float length = (float)Math.Sqrt(_w * _w + _h * _h) + 40, half = 64 * s;
            using (var stripe = new LinearGradientBrush(new RectangleF(-length / 2, -half - 1, length, half * 2 + 2), Color.FromArgb(255, 222, 40), Color.FromArgb(240, 186, 0), 90f))
                g.FillRectangle(stripe, -length / 2, -half, length, half * 2);
            g.Restore(state);
            Vignette(g, 90);
            Panel(g, "BLUE FLAG", "LET THE FASTER CAR BY");
        }

        private void Surface(Graphics g)
        {
            // Yellow with red stripes, yellow at both edges.
            const int Stripes = 9;
            float width = _w / (float)Stripes;
            for (int i = 0; i < Stripes; i++)
                using (var brush = new SolidBrush(i % 2 == 0 ? Color.FromArgb(255, 212, 0) : Color.FromArgb(226, 36, 42)))
                    g.FillRectangle(brush, (float)Math.Floor(i * width), 0, (float)Math.Ceiling(width) + 1, _h);
            Shading(g);
            Vignette(g, 100);
            Panel(g, "SURFACE", "SLIPPERY TRACK AHEAD");
        }

        private void Checkered(Graphics g, TelemetrySnapshot t)
        {
            float s = _s, square = _h / 6f;
            int columns = (int)Math.Ceiling(_w / square) + 1;
            float left = (_w - columns * square) / 2;
            using (var light = new SolidBrush(Color.FromArgb(238, 238, 234)))
            using (var dark = new SolidBrush(Color.FromArgb(14, 15, 18)))
                for (int line = 0; line < 6; line++)
                    for (int column = 0; column < columns; column++)
                        g.FillRectangle(((line + column) & 1) == 0 ? light : dark, left + column * square, line * square, square + 0.5f, square + 0.5f);
            Shading(g);
            Vignette(g, 170);

            bool placed = t.Position > 0;
            var stats = new List<Tuple<string, string, Color>>();
            if (placed && t.ClassPosition > 0 && t.ClassPosition != t.Position)
                stats.Add(Tuple.Create("CLASS", "P" + t.ClassPosition, Color.White));
            if (t.BestLapSeconds > 0)
                stats.Add(Tuple.Create("BEST LAP", Fmt.LapTime(t.BestLapSeconds), Color.White));
            if (placed && t.StartPosition > 0)
            {
                int gained = t.StartPosition - t.Position;
                stats.Add(Tuple.Create("PLACES", gained > 0 ? "+" + gained : gained < 0 ? "−" + -gained : "±0",
                    gained > 0 ? DashKit.Good : gained < 0 ? DashKit.Bad : Color.FromArgb(170, 178, 190)));
            }

            float width = 560 * s, height = (stats.Count > 0 ? 296 : 214) * s;
            var box = new RectangleF(_cx - width / 2, _cy - height / 2, width, height);
            Glass(g, box, dense: true);
            var label = Color.FromArgb(150, 160, 174);
            if (placed)
            {
                FlagType.Draw(g, "FINISH", FlagType.Semi, FontStyle.Regular, 19 * s, 7 * s, new PointF(_cx, box.Top + 40 * s), label);
                FlagType.Draw(g, "P" + t.Position, FlagType.Bold, FontStyle.Bold, 112 * s, 3 * s, new PointF(_cx, box.Top + 118 * s), Color.White);
            }
            else
                FlagType.Draw(g, "FINISH", FlagType.Bold, FontStyle.Bold, 88 * s, 8 * s, new PointF(_cx, box.Top + 100 * s), Color.White);
            if (stats.Count == 0) return;

            float rule = box.Top + 190 * s;
            using (var pen = new Pen(Color.FromArgb(40, 255, 255, 255), Math.Max(1, s)))
            {
                g.DrawLine(pen, box.Left + 36 * s, rule, box.Right - 36 * s, rule);
                for (int i = 1; i < stats.Count; i++)
                {
                    float x = box.Left + box.Width * i / stats.Count;
                    g.DrawLine(pen, x, rule + 22 * s, x, box.Bottom - 22 * s);
                }
            }
            float row = (rule + box.Bottom) / 2;
            for (int i = 0; i < stats.Count; i++)
            {
                float x = box.Left + box.Width * (i + 0.5f) / stats.Count;
                FlagType.Draw(g, stats[i].Item1, FlagType.Semi, FontStyle.Regular, 14 * s, 2.5f * s, new PointF(x, row - 19 * s), label);
                FlagType.Draw(g, stats[i].Item2, FlagType.Bold, FontStyle.Bold, 32 * s, 0.5f * s, new PointF(x, row + 15 * s), stats[i].Item3, box.Width / stats.Count - 20 * s);
            }
        }

        // ------------------------------------------------------------------ pieces

        /// <summary>The flag's colour, a little lighter at the top, with a soft vignette.</summary>
        private void Cloth(Graphics g, Color color)
        {
            using (var brush = new LinearGradientBrush(new RectangleF(0, -1, _w, _h + 2), Shade(color, 0.12f), Shade(color, -0.12f), 90f))
                g.FillRectangle(brush, 0, 0, _w, _h);
            Vignette(g, color.GetBrightness() > 0.2f ? 80 : 120);
        }

        /// <summary>Light from above on a patterned flag.</summary>
        private void Shading(Graphics g)
        {
            using (var brush = new LinearGradientBrush(new RectangleF(0, -1, _w, _h + 2), Color.FromArgb(34, 255, 255, 255), Color.FromArgb(44, 0, 0, 0), 90f))
                g.FillRectangle(brush, 0, 0, _w, _h);
        }

        /// <summary>Darkens toward the edges and corners.</summary>
        private void Vignette(Graphics g, int strength)
        {
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(-_w * 0.3f, -_h * 0.45f, _w * 1.6f, _h * 1.9f);
                using (var brush = new PathGradientBrush(path)
                {
                    CenterColor = Color.FromArgb(0, 0, 0, 0),
                    SurroundColors = new[] { Color.FromArgb(strength, 0, 0, 0) },
                    Blend = new Blend { Positions = new[] { 0f, 0.5f, 1f }, Factors = new[] { 0f, 0.8f, 1f } }
                })
                    g.FillRectangle(brush, 0, 0, _w, _h);
            }
        }

        /// <summary>A title, a short rule and what to do, stacked and centred on <paramref name="middle"/>.</summary>
        private void Words(Graphics g, string title, string subtitle, Color ink, float middle, float titleSize, float subtitleSize)
        {
            float s = _s, t = titleSize * s, u = subtitleSize * s, rule = 3.5f * s;
            float titleCap = t * 0.7f, subtitleCap = u * 0.7f, gap1 = t * 0.25f, gap2 = u * 0.9f;
            float top = middle - (titleCap + gap1 + rule + gap2 + subtitleCap) / 2;
            float maxWidth = _w - 110 * s;
            FlagType.Draw(g, title, FlagType.Bold, FontStyle.Bold, t, t * 0.05f, new PointF(_cx, top + titleCap / 2), ink, maxWidth);
            float ruleY = top + titleCap + gap1;
            using (var brush = new SolidBrush(Color.FromArgb(140, ink)))
                g.FillRoundedRectangle(brush, new RectangleF(_cx - 28 * s, ruleY, 56 * s, rule), rule / 2);
            FlagType.Draw(g, subtitle, FlagType.Semi, FontStyle.Regular, u, u * 0.16f, new PointF(_cx, ruleY + rule + gap2 + subtitleCap / 2), Color.FromArgb(210, ink), maxWidth);
        }

        /// <summary>A glass panel sized to its words, centred on the flag.</summary>
        private void Panel(Graphics g, string title, string subtitle)
        {
            float s = _s;
            SizeF a = FlagType.Measure(g, title, FlagType.Bold, FontStyle.Bold, 66 * s, 66 * s * 0.05f);
            SizeF b = FlagType.Measure(g, subtitle, FlagType.Semi, FontStyle.Regular, 21 * s, 21 * s * 0.16f);
            float width = Math.Min(_w - 60 * s, Math.Max(420 * s, Math.Max(a.Width, b.Width) + 104 * s)), height = 168 * s;
            Glass(g, new RectangleF(_cx - width / 2, _cy - height / 2, width, height));
            Words(g, title, subtitle, Color.White, _cy, 66, 21);
        }

        /// <summary>
        /// Dark frosted glass (what's behind it, blurred and smoked) with a soft shadow and a rim lit from above.
        /// Dense glass smokes more, for a busy pattern behind it.
        /// </summary>
        private void Glass(Graphics g, RectangleF box, bool dense = false)
        {
            float s = _s, r = 26 * s;
            // Blur what's behind by shrinking it a long way and stretching it back.
            Rectangle area = Rectangle.Intersect(Rectangle.Round(box), new Rectangle(0, 0, _w, _h));
            Bitmap frost = null;
            if (_canvas != null && area.Width > 8 && area.Height > 8)
            {
                g.Flush();
                frost = new Bitmap(Math.Max(2, area.Width / 16), Math.Max(2, area.Height / 16), PixelFormat.Format32bppArgb);
                using (Graphics small = Graphics.FromImage(frost))
                {
                    small.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    small.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    small.DrawImage(_canvas, new Rectangle(0, 0, frost.Width, frost.Height), area, GraphicsUnit.Pixel);
                }
            }
            for (int i = 4; i >= 1; i--)
            {
                float spread = i * 4 * s;
                using (var shadow = new SolidBrush(Color.FromArgb(22, 0, 0, 0)))
                    g.FillRoundedRectangle(shadow, new RectangleF(box.X - spread, box.Y - spread + 8 * s, box.Width + spread * 2, box.Height + spread * 2), r + spread);
            }
            if (frost != null)
                using (frost)
                using (GraphicsPath clip = DashKit.Rounded(box, r))
                using (var edges = new ImageAttributes())
                {
                    edges.SetWrapMode(WrapMode.TileFlipXY);
                    GraphicsState state = g.Save();
                    g.SetClip(clip, CombineMode.Intersect);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(frost, area, 0, 0, frost.Width, frost.Height, GraphicsUnit.Pixel, edges);
                    g.Restore(state);
                }
            using (var fill = new LinearGradientBrush(new RectangleF(box.X, box.Y - 1, box.Width, box.Height + 2), Color.FromArgb(frost == null ? 228 : dense ? 214 : 176, 26, 29, 35), Color.FromArgb(frost == null ? 240 : dense ? 232 : 208, 9, 10, 13), 90f))
                g.FillRoundedRectangle(fill, box, r);
            using (GraphicsPath path = DashKit.Rounded(box, r))
            using (var rim = new LinearGradientBrush(new RectangleF(box.X, box.Y - 1, box.Width, box.Height + 2), Color.FromArgb(120, 255, 255, 255), Color.FromArgb(16, 255, 255, 255), 90f))
            using (var pen = new Pen(rim, Math.Max(1, 1.5f * s)))
                g.DrawPath(pen, path);
        }

        /// <summary>Lightens toward white (positive) or darkens toward black (negative).</summary>
        private static Color Shade(Color c, float amount) => amount >= 0
            ? Color.FromArgb(c.A, c.R + (int)((255 - c.R) * amount), c.G + (int)((255 - c.G) * amount), c.B + (int)((255 - c.B) * amount))
            : Color.FromArgb(c.A, (int)(c.R * (1 + amount)), (int)(c.G * (1 + amount)), (int)(c.B * (1 + amount)));

        public void Dispose() => _picture?.Dispose();
    }

    /// <summary>
    /// The frame round the dashboard while a flag is out: a soft glow in the flag's colour along the screen's
    /// edge, breathing slowly, with a tab at the top saying what it is. Glow and tab are drawn once per flag;
    /// each frame only mixes the glow into the edge bands (well under a millisecond) and sets the tab on top.
    /// </summary>
    internal sealed class FlagFramePainter : IDisposable
    {
        private const int BreathSteps = 24, BreathMilliseconds = 2400;
        private readonly int _w, _h, _band;
        private readonly float _s;
        private int[] _glow;
        private Bitmap _tab;
        private Point _tabAt;
        private RacingFlag _flag;

        public FlagFramePainter(int width, int height)
        {
            _w = width;
            _h = height;
            _s = Math.Min(width / 854f, height / 480f);
            _band = Math.Min(Math.Min(width, height) / 2, (int)Math.Ceiling(42 * _s));
        }

        /// <summary>Where the slow breath is, in steps: it changes about ten times a second, so the runtime redraws for it.</summary>
        public static int Breath(int now) => (int)((uint)now % BreathMilliseconds * BreathSteps / BreathMilliseconds);

        public void Draw(Bitmap target, RacingFlag flag, float alpha, int now)
        {
            if (flag == RacingFlag.None || alpha <= 0) return;
            if (flag != _flag || _glow == null) Prepare(flag);
            float breath = 0.8f + 0.2f * (float)(0.5 + 0.5 * Math.Cos(Breath(now) * 2 * Math.PI / BreathSteps));
            MixGlow(target, (int)Math.Round(Math.Max(0, Math.Min(1, alpha * breath)) * 256));
            using (Graphics g = Graphics.FromImage(target))
            {
                if (alpha >= 0.999f) g.DrawImageUnscaled(_tab, _tabAt);
                else
                    using (var attributes = new ImageAttributes())
                    {
                        attributes.SetColorMatrix(new ColorMatrix { Matrix33 = alpha });
                        g.DrawImage(_tab, new Rectangle(_tabAt, _tab.Size), 0, 0, _tab.Width, _tab.Height, GraphicsUnit.Pixel, attributes);
                    }
            }
        }

        /// <summary>Lays the glow over the edge bands at a strength out of 256.</summary>
        private void MixGlow(Bitmap target, int strength)
        {
            if (strength <= 0) return;
            int w = _w, h = _h, band = _band;
            BitmapData data = target.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                var row = new int[w];
                for (int y = 0; y < h; y++)
                {
                    IntPtr line = data.Scan0 + y * data.Stride;
                    if (y < band || y >= h - band)
                        Mix(line, row, y * w, 0, w, strength);
                    else
                    {
                        Mix(line, row, y * w, 0, band, strength);
                        Mix(line, row, y * w, w - band, band, strength);
                    }
                }
            }
            finally { target.UnlockBits(data); }
        }

        private void Mix(IntPtr line, int[] row, int rowStart, int x, int count, int strength)
        {
            IntPtr at = line + x * 4;
            System.Runtime.InteropServices.Marshal.Copy(at, row, x, count);
            for (int i = x; i < x + count; i++)
            {
                int glow = _glow[rowStart + i];
                int a = (int)((uint)glow >> 24) * strength >> 8;
                if (a == 0) continue;
                int under = row[i], keep = 255 - a;
                int r = (((glow >> 16) & 255) * a + ((under >> 16) & 255) * keep) / 255;
                int g = (((glow >> 8) & 255) * a + ((under >> 8) & 255) * keep) / 255;
                int b = ((glow & 255) * a + (under & 255) * keep) / 255;
                int alpha = a + (int)((uint)under >> 24) * keep / 255;
                row[i] = (alpha << 24) | (r << 16) | (g << 8) | b;
            }
            System.Runtime.InteropServices.Marshal.Copy(row, x, at, count);
        }

        private void Prepare(RacingFlag flag)
        {
            Color color = FlagStyle.Of(flag);
            float s = _s, radius = 16 * s, depth = 34 * s, stroke = 3.5f * s, inset = 2 + stroke / 2;
            using (var glow = new Bitmap(_w, _h, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(glow))
                {
                    g.Clear(Color.Transparent);
                    DashKit.Prepare(g);
                    // Light falling off inward from the edge.
                    int rings = (int)Math.Ceiling(depth / 1.5f);
                    for (int i = rings; i >= 0; i--)
                    {
                        float ringInset = inset + i * 1.5f, f = i / (float)rings;
                        int a = (int)(165 * Math.Exp(-3.4 * f) * (1 - f));
                        if (a <= 0) continue;
                        using (var pen = new Pen(Color.FromArgb(a, color), 1.6f))
                        using (GraphicsPath ring = DashKit.Rounded(new RectangleF(ringInset, ringInset, _w - ringInset * 2, _h - ringInset * 2), Math.Max(2, radius - i * 0.6f)))
                            g.DrawPath(pen, ring);
                    }
                    // The edge itself, with the corners outside its curve filled so the frame meets the screen's edge.
                    using (GraphicsPath inside = DashKit.Rounded(new RectangleF(inset, inset, _w - inset * 2, _h - inset * 2), radius))
                    using (var outside = new Region(new Rectangle(0, 0, _w, _h)))
                    {
                        outside.Exclude(inside);
                        using (var brush = new SolidBrush(Color.FromArgb(170, color))) g.FillRegion(brush, outside);
                        using (var pen = new Pen(color, stroke)) g.DrawPath(pen, inside);
                        if (flag == RacingFlag.Checkered)
                            using (var squares = new Pen(Color.FromArgb(14, 15, 18), stroke) { DashPattern = new[] { 1f, 1f } })
                                g.DrawPath(squares, inside);
                    }
                }
                BitmapData data = glow.LockBits(new Rectangle(0, 0, _w, _h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                if (_glow == null) _glow = new int[_w * _h];
                try { System.Runtime.InteropServices.Marshal.Copy(data.Scan0, _glow, 0, _glow.Length); }
                finally { glow.UnlockBits(data); }
            }
            _tab?.Dispose();
            _tab = Tab(flag, out _tabAt);
            _flag = flag;
        }

        /// <summary>A tab hanging from the top edge, in the flag's colour, saying what's out.</summary>
        private Bitmap Tab(RacingFlag flag, out Point at)
        {
            string text = FlagStyle.Banner(flag);
            float s = _s, size = 16 * s, tracking = 2.4f * s, height = 32 * s, r = 12 * s, margin = 4 * s;
            SizeF ink;
            using (var measure = new Bitmap(1, 1))
            using (Graphics g = Graphics.FromImage(measure))
                ink = FlagType.Measure(g, text, FlagType.Semi, FontStyle.Regular, size, tracking);
            float width = ink.Width + 44 * s;
            at = new Point((int)Math.Floor(_w / 2f - width / 2 - margin), 0);
            var tab = new Bitmap((int)Math.Ceiling(width + margin * 2) + 1, (int)Math.Ceiling(height + margin) + 1, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(tab))
            {
                DashKit.Prepare(g);
                // In the tab's own pixels; the top corners sit above it, so only the lower ones show round.
                float left = _w / 2f - width / 2 - at.X;
                var box = new RectangleF(left, -r, width, height + r);
                using (var shadow = new SolidBrush(Color.FromArgb(100, 0, 0, 0)))
                    g.FillRoundedRectangle(shadow, new RectangleF(box.X - 2 * s, box.Y, box.Width + 4 * s, box.Height + 3 * s), r + 2 * s);
                using (var fill = new SolidBrush(FlagStyle.TabFill(flag)))
                    g.FillRoundedRectangle(fill, box, r);
                if (flag == RacingFlag.Black)
                    using (GraphicsPath outline = DashKit.Rounded(box, r))
                    using (var pen = new Pen(Color.FromArgb(242, 242, 242), Math.Max(1, 1.5f * s)))
                        g.DrawPath(pen, outline);
                FlagType.Draw(g, text, FlagType.Semi, FontStyle.Regular, size, tracking, new PointF(left + width / 2, height / 2 + 0.5f * s), FlagStyle.TabInk(flag));
            }
            return tab;
        }

        public void Dispose() => _tab?.Dispose();
    }
}
