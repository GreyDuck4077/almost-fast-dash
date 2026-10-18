using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace AlmostFastDash.Output
{
    /// <summary>
    /// Dashboard screen sizes: the common ones offered when a new dashboard is made, the size of a
    /// connected wheel screen when one is found, and fitting a frame to a screen of another size.
    /// </summary>
    internal static class ScreenSizes
    {
        public const int DefaultWidth = 854, DefaultHeight = 480;
        public const int Smallest = 64, Largest = 3840;

        public sealed class Preset
        {
            public int Width, Height;
            public string Note;
            public string Label => Width + " × " + Height + (string.IsNullOrEmpty(Note) ? "" : "  ·  " + Note);
        }

        /// <summary>Sizes of common wheel, dash and monitor screens.</summary>
        public static readonly Preset[] Presets =
        {
            new Preset { Width = 854, Height = 480, Note = "5\" wheel screens" },
            new Preset { Width = 800, Height = 480, Note = "5\" and 7\" dash displays" },
            new Preset { Width = 1024, Height = 600, Note = "7\" displays" },
            new Preset { Width = 1280, Height = 480, Note = "wide bar displays" },
            new Preset { Width = 480, Height = 320, Note = "3.5\" displays" },
            new Preset { Width = 1280, Height = 720, Note = "HD" },
            new Preset { Width = 1920, Height = 1080, Note = "Full HD" },
        };

        public static int Clamp(int pixels) => Math.Max(Smallest, Math.Min(Largest, pixels));

        /// <summary>
        /// The size of the wheel screen plugged in right now, or null when none the app can drive is found.
        /// Looking doesn't open the screen, so it works while the dashboard is showing on it.
        /// </summary>
        public static Size? Detect()
        {
            try
            {
                if (WheelScreen.IsPresent()) return LastScreen(out _);
            }
            catch { }
            return null;
        }

        /// <summary>
        /// The wheel screen the dashboard last connected to: its picture size, and its model in <paramref name="model"/>.
        /// Until one has connected, the 5" M-PRO.
        /// </summary>
        public static Size LastScreen(out string model)
        {
            model = ScreenModel.FiveInch.Name;
            var size = new Size(ScreenModel.FiveInch.Width, ScreenModel.FiveInch.Height);
            try
            {
                string[] lines = System.IO.File.ReadAllLines(ScreenSender.InfoPath);
                string[] parts = lines.Length > 1 ? lines[1].Split('x') : new string[0];
                if (parts.Length == 2 && int.TryParse(parts[0], out int width) && int.TryParse(parts[1], out int height) && width > 0 && height > 0)
                {
                    model = lines[0];
                    size = new Size(width, height);
                }
            }
            catch { }
            return size;
        }

        /// <summary>
        /// A frame drawn for one size, made to fit a screen of another: scaled to fit without stretching,
        /// centred, with the background color of its own edge filling the space either side.
        /// </summary>
        public static Bitmap Fit(Bitmap frame, int width, int height)
        {
            var fitted = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            float scale = Math.Min(width / (float)frame.Width, height / (float)frame.Height);
            float w = frame.Width * scale, h = frame.Height * scale;
            using (Graphics g = Graphics.FromImage(fitted))
            {
                g.Clear(frame.GetPixel(0, frame.Height / 2));
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingMode = CompositingMode.SourceCopy;
                using (var edges = new ImageAttributes())
                {
                    edges.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(frame, new Rectangle((int)Math.Round((width - w) / 2), (int)Math.Round((height - h) / 2), (int)Math.Round(w), (int)Math.Round(h)),
                        0, 0, frame.Width, frame.Height, GraphicsUnit.Pixel, edges);
                }
            }
            return fitted;
        }
    }
}
