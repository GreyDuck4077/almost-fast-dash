using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Linq;

namespace AlmostFastDash.Rendering
{
    /// <summary>Each flag's colors and words, shared by the full-screen flag and its frame.</summary>
    internal static class FlagStyle
    {
        public static readonly Color Ink = Color.FromArgb(10, 12, 16);

        /// <summary>The color of the frame and its tab.</summary>
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
}
