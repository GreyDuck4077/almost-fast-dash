using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;

namespace AlmostFastOverlays
{
    /// <summary>
    /// Turns dashboard frames into what an M-PRO screen takes, doing as little as it can: it finds which parts of
    /// a frame changed since the one before, and encodes just those as 16-bit colour (RGB565), turned a quarter
    /// onto portrait panels.
    /// </summary>
    internal static unsafe class FrameEncoder
    {
        /// <summary>Changes are looked for in tiles this many pixels square.</summary>
        internal const int Tile = 16;

        /// <summary>
        /// What sending one more area costs on top of its pixels (a command and a transfer of its own), counted in
        /// pixels at the screen's USB speed.
        /// </summary>
        internal const int AreaCost = 6000;

        /// <summary>At most this many areas a frame; past it, the cheapest to join are joined.</summary>
        internal const int MostAreas = 8;

        /// <summary>A frame's 32-bit pixels, as GDI+ keeps them, copied out row after row.</summary>
        public static void Copy(Bitmap frame, int[] pixels)
        {
            BitmapData data = frame.LockBits(new Rectangle(0, 0, frame.Width, frame.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int row = frame.Width * 4;
                fixed (int* target = pixels)
                {
                    if (data.Stride == row)
                        Buffer.MemoryCopy((void*)data.Scan0, target, (long)pixels.Length * 4, (long)row * frame.Height);
                    else
                        for (int y = 0; y < frame.Height; y++)
                            Buffer.MemoryCopy((byte*)data.Scan0 + (long)y * data.Stride, (byte*)target + (long)y * row, row, row);
                }
            }
            finally
            {
                frame.UnlockBits(data);
            }
        }

        /// <summary>
        /// Where <paramref name="now"/> differs from <paramref name="before"/>: tiles that changed, joined into a few
        /// rectangles wherever one costs less to send than two, or the whole picture once that's cheaper. Empty
        /// when nothing changed.
        /// </summary>
        public static List<Rectangle> Changes(int[] now, int[] before, int width, int height)
        {
            int columns = (width + Tile - 1) / Tile, rows = (height + Tile - 1) / Tile;
            var changed = new bool[columns * rows];
            fixed (int* a = now, b = before)
            {
                for (int ty = 0; ty < rows; ty++)
                {
                    int y0 = ty * Tile, y1 = Math.Min(height, y0 + Tile);
                    for (int tx = 0; tx < columns; tx++)
                    {
                        int x0 = tx * Tile, n = Math.Min(Tile, width - x0);
                        bool differs = false;
                        for (int y = y0; y < y1 && !differs; y++)
                        {
                            int* p = a + y * width + x0, q = b + y * width + x0;
                            int k = 0;
                            // Two pixels at a time.
                            for (; k + 1 < n; k += 2)
                                if (*(long*)(p + k) != *(long*)(q + k)) { differs = true; break; }
                            if (!differs && k < n && p[k] != q[k]) differs = true;
                        }
                        changed[ty * columns + tx] = differs;
                    }
                }
            }

            // Runs of changed tiles along each row, stacked into one rectangle where the run below covers the same columns.
            var areas = new List<Rectangle>();
            var open = new Dictionary<long, int>();
            for (int ty = 0; ty < rows; ty++)
            {
                var next = new Dictionary<long, int>();
                for (int tx = 0; tx < columns; tx++)
                {
                    if (!changed[ty * columns + tx]) continue;
                    int start = tx;
                    while (tx + 1 < columns && changed[ty * columns + tx + 1]) tx++;
                    long span = ((long)start << 32) | (uint)tx;
                    var run = Rectangle.FromLTRB(start * Tile, ty * Tile, Math.Min(width, (tx + 1) * Tile), Math.Min(height, (ty + 1) * Tile));
                    if (open.TryGetValue(span, out int index)) areas[index] = Rectangle.Union(areas[index], run);
                    else
                    {
                        areas.Add(run);
                        index = areas.Count - 1;
                    }
                    next[span] = index;
                }
                open = next;
            }
            Join(areas, width, height);
            return areas;
        }

        /// <summary>
        /// Joins areas wherever one box costs no more to send than the two apart, then the cheapest pairs until
        /// there are few enough; the whole picture once it's most of the screen anyway.
        /// </summary>
        internal static void Join(List<Rectangle> areas, int width, int height)
        {
            if (areas.Count == 0) return;
            long Cost(Rectangle r) => (long)r.Width * r.Height + AreaCost;
            // Scattered changes everywhere: one box round them all is as good as any.
            if (areas.Count > 64)
            {
                Rectangle all = areas.Aggregate(Rectangle.Union);
                areas.Clear();
                areas.Add(all);
            }
            bool joined = true;
            while (joined)
            {
                joined = false;
                for (int i = 0; i < areas.Count && !joined; i++)
                    for (int j = i + 1; j < areas.Count; j++)
                    {
                        Rectangle union = Rectangle.Union(areas[i], areas[j]);
                        if (Cost(union) > Cost(areas[i]) + Cost(areas[j])) continue;
                        areas[i] = union;
                        areas.RemoveAt(j);
                        joined = true;
                        break;
                    }
            }
            while (areas.Count > MostAreas)
            {
                int bestI = 0, bestJ = 1;
                long least = long.MaxValue;
                for (int i = 0; i < areas.Count; i++)
                    for (int j = i + 1; j < areas.Count; j++)
                    {
                        long extra = Cost(Rectangle.Union(areas[i], areas[j])) - Cost(areas[i]) - Cost(areas[j]);
                        if (extra < least) { least = extra; bestI = i; bestJ = j; }
                    }
                areas[bestI] = Rectangle.Union(areas[bestI], areas[bestJ]);
                areas.RemoveAt(bestJ);
            }
            long sent = areas.Sum(r => (long)r.Width * r.Height) + (areas.Count - 1) * (long)AreaCost;
            if (sent >= (long)width * height * 7 / 10)
            {
                areas.Clear();
                areas.Add(new Rectangle(0, 0, width, height));
            }
        }

        /// <summary>
        /// The panel's pixels for an area of the picture, row after row as the screen takes them, written to
        /// <paramref name="target"/>; returns where that area is on the panel. A portrait panel gets the picture
        /// turned a quarter: the picture's right edge runs along the panel's top.
        /// </summary>
        public static Rectangle Encode(int[] pixels, int width, Rectangle area, ScreenModel model, byte[] target)
        {
            fixed (int* source = pixels)
            fixed (byte* bytes = target)
            {
                ushort* to = (ushort*)bytes;
                if (!model.Turned)
                {
                    for (int y = area.Top; y < area.Bottom; y++)
                    {
                        int* from = source + y * width + area.Left;
                        for (int x = 0; x < area.Width; x++) *to++ = Rgb565(from[x]);
                    }
                    return area;
                }
                // Each panel row is one column of the picture, from its right edge leftwards; along a row, the
                // picture's rows top to bottom.
                for (int x = area.Right - 1; x >= area.Left; x--)
                {
                    int* from = source + area.Top * width + x;
                    for (int y = 0; y < area.Height; y++, from += width) *to++ = Rgb565(*from);
                }
                return new Rectangle(area.Top, model.Width - area.Right, area.Height, area.Width);
            }
        }

        /// <summary>32-bit colour to 16-bit: the top 5 bits of red, 6 of green and 5 of blue.</summary>
        internal static ushort Rgb565(int argb) => (ushort)(((argb >> 8) & 0xF800) | ((argb >> 5) & 0x07E0) | ((argb >> 3) & 0x001F));
    }
}
