using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
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

        /// <summary>A made-up circuit for the designer's previews.</summary>
        public static TrackMap Preview()
        {
            var x = new float[Points];
            var y = new float[Points];
            for (int p = 0; p < Points; p++)
            {
                double a = 2 * Math.PI * p / Points;
                // A long back straight, a hairpin and a fast sweep: recognisably a circuit.
                x[p] = (float)(Math.Cos(a) * 520 + Math.Cos(2 * a) * 90 + Math.Sin(3 * a) * 40);
                y[p] = (float)(Math.Sin(a) * 260 - Math.Sin(2 * a) * 120 + Math.Cos(3 * a) * 30);
            }
            return new TrackMap { Key = "preview", Name = "Preview circuit", X = x, Y = y };
        }
    }
}
