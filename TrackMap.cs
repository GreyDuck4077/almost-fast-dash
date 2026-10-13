using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Web.Script.Serialization;

namespace AlmostFastOverlays
{
    /// <summary>
    /// A track's outline as a closed loop of points, evenly spaced by how far around the lap they
    /// are, so a car's lap fraction gives its place on the map. iRacing doesn't publish track
    /// shapes, so the app traces one from a clean lap of your own driving and keeps it per track
    /// in the TrackMaps folder next to the exe.
    /// </summary>
    internal sealed class TrackMap
    {
        public const int Points = 400;
        public static readonly string Folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TrackMaps");

        public string Key { get; set; }
        public string Name { get; set; }
        public float[] X { get; set; }
        public float[] Y { get; set; }

        /// <summary>A point on the loop at a lap fraction (0 to 1), between the stored points.</summary>
        public PointF At(double fraction)
        {
            fraction -= Math.Floor(fraction);
            double at = fraction * Points;
            int i = (int)at % Points, j = (i + 1) % Points;
            float t = (float)(at - Math.Floor(at));
            return new PointF(X[i] + (X[j] - X[i]) * t, Y[i] + (Y[j] - Y[i]) * t);
        }

        public bool Valid => X != null && Y != null && X.Length == Points && Y.Length == Points;

        public static string KeyFor(WeekendInfo weekend)
        {
            if (weekend == null || weekend.TrackId <= 0) return null;
            return weekend.TrackId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private static readonly Dictionary<string, TrackMap> Loaded = new Dictionary<string, TrackMap>();

        /// <summary>The saved map for a track, or null; read from disk once.</summary>
        public static TrackMap Load(string key)
        {
            if (key == null) return null;
            lock (Loaded)
            {
                if (Loaded.TryGetValue(key, out TrackMap cached)) return cached;
                TrackMap map = null;
                try
                {
                    string path = Path.Combine(Folder, key + ".json");
                    if (File.Exists(path)) map = new JavaScriptSerializer().Deserialize<TrackMap>(File.ReadAllText(path));
                    if (map != null && !map.Valid) map = null;
                }
                catch { map = null; }
                Loaded[key] = map;
                return map;
            }
        }

        public void Save()
        {
            lock (Loaded) Loaded[Key] = this;
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(Path.Combine(Folder, Key + ".json"), new JavaScriptSerializer().Serialize(this));
            }
            catch { }
        }

        /// <summary>
        /// Builds a map from one lap's samples (lap fraction and position in metres), or null when the
        /// lap had gaps. The small drift dead reckoning gathers over a lap is spread back out so the
        /// loop closes.
        /// </summary>
        public static TrackMap FromLap(string key, string name, List<double> fractions, List<double> xs, List<double> ys)
        {
            int n = fractions.Count;
            if (n < 200) return null;
            // A lap that misses part of the track (a pause, a tow) can't make a map.
            for (int i = 1; i < n; i++) if (fractions[i] - fractions[i - 1] > 0.03 || fractions[i] < fractions[i - 1] - 0.001) return null;
            if (fractions[0] > 0.03 || fractions[n - 1] < 0.97) return null;
            var x = new float[Points];
            var y = new float[Points];
            int k = 0;
            for (int p = 0; p < Points; p++)
            {
                double at = (double)p / Points;
                while (k < n - 2 && fractions[k + 1] < at) k++;
                double span = fractions[k + 1] - fractions[k], t = span <= 0 ? 0 : Math.Max(0, Math.Min(1, (at - fractions[k]) / span));
                x[p] = (float)(xs[k] + (xs[k + 1] - xs[k]) * t);
                y[p] = (float)(ys[k] + (ys[k + 1] - ys[k]) * t);
            }
            // Where the loop should meet itself: carry on from the last sample to the line.
            double endX = xs[n - 1] + (xs[n - 1] - xs[n - 2]) * ((1 - fractions[n - 1]) / Math.Max(1e-6, fractions[n - 1] - fractions[n - 2]));
            double endY = ys[n - 1] + (ys[n - 1] - ys[n - 2]) * ((1 - fractions[n - 1]) / Math.Max(1e-6, fractions[n - 1] - fractions[n - 2]));
            double gapX = endX - x[0], gapY = endY - y[0];
            double length = 0;
            for (int p = 1; p < Points; p++) length += Math.Sqrt((x[p] - x[p - 1]) * (x[p] - x[p - 1]) + (y[p] - y[p - 1]) * (y[p] - y[p - 1]));
            // More than a tenth of the lap adrift means the trace went wrong.
            if (length < 200 || Math.Sqrt(gapX * gapX + gapY * gapY) > length * 0.1) return null;
            for (int p = 0; p < Points; p++)
            {
                float share = (float)p / Points;
                x[p] -= (float)(gapX * share);
                y[p] -= (float)(gapY * share);
            }
            return new TrackMap { Key = key, Name = name, X = x, Y = y };
        }

        // A real circuit for previews: Oulton Park, traced by the app from a lap of the user's own driving.
        private static readonly short[] PreviewX = { 591, 598, 604, 610, 617, 625, 632, 641, 649, 654, 652, 638, 616, 593, 570, 547, 524, 501, 479, 456, 433, 409, 385, 360, 335, 309, 283, 258, 234, 213, 197, 184, 173, 160, 139, 111, 87, 68, 53, 37, 21, 9, 2, 0, 4, 13, 23, 33, 43, 53, 63, 71, 77, 82, 86, 88, 91, 92, 93, 93, 90, 85, 79, 71, 65, 61, 64, 74, 88, 106, 128, 154, 180, 206, 232, 258, 284, 310, 335, 361, 386, 411, 437, 463, 488, 514, 540, 565, 587, 595, 595, 591, 586, 581, 575, 573, 574, 577, 581, 586 };
        private static readonly short[] PreviewY = { 607, 582, 556, 531, 505, 480, 454, 429, 404, 376, 349, 323, 305, 293, 279, 266, 253, 240, 226, 212, 200, 188, 177, 168, 160, 154, 149, 141, 129, 112, 90, 67, 43, 19, 2, 3, 17, 36, 58, 79, 100, 123, 149, 175, 201, 226, 250, 275, 299, 323, 348, 374, 400, 426, 452, 478, 505, 531, 557, 584, 610, 636, 662, 687, 713, 739, 765, 788, 810, 829, 842, 850, 855, 857, 859, 865, 872, 879, 886, 893, 900, 907, 914, 920, 927, 932, 936, 935, 920, 895, 869, 842, 816, 790, 765, 738, 712, 686, 659, 633 };

        /// <summary>The map the designer's previews show: a real circuit.</summary>
        public static TrackMap Preview()
        {
            var x = new float[Points];
            var y = new float[Points];
            int n = PreviewX.Length;
            for (int p = 0; p < Points; p++)
            {
                double at = (double)p * n / Points;
                int i = (int)at, j = (i + 1) % n;
                float t = (float)(at - i);
                x[p] = PreviewX[i] + (PreviewX[j] - PreviewX[i]) * t;
                y[p] = PreviewY[i] + (PreviewY[j] - PreviewY[i]) * t;
            }
            return new TrackMap { Key = "preview", Name = "Oulton Park (preview)", X = x, Y = y };
        }
    }
}
