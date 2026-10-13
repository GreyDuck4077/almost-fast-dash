using System;

namespace AlmostFastOverlays
{
    /// <summary>
    /// Lap falloff from a stint's green-flag laps (oldest first): how far the last lap has dropped from
    /// the stint's best, and the trend since that best as seconds per lap.
    /// </summary>
    internal static class Falloff
    {
        public static double Best(double[] laps)
        {
            double best = 0;
            if (laps != null)
                foreach (double lap in laps)
                    if (lap > 0 && (best == 0 || lap < best)) best = lap;
            return best;
        }

        /// <summary>The last lap minus the stint's best; -1 until there are two laps.</summary>
        public static double Total(double[] laps)
        {
            if (laps == null || laps.Length < 2) return -1;
            return Math.Max(0, laps[laps.Length - 1] - Best(laps));
        }

        /// <summary>
        /// A least-squares slope through the laps from the stint's best onward (the laps before it were
        /// still coming up to speed); 0 until there are three.
        /// </summary>
        public static double PerLap(double[] laps)
        {
            if (laps == null || laps.Length < 3) return 0;
            int from = 0;
            for (int i = 1; i < laps.Length; i++)
                if (laps[i] < laps[from]) from = i;
            int n = laps.Length - from;
            if (n < 3) from = Math.Max(0, laps.Length - 3);
            n = laps.Length - from;
            double mx = (n - 1) / 2.0, my = 0;
            for (int i = 0; i < n; i++) my += laps[from + i];
            my /= n;
            double sxy = 0, sxx = 0;
            for (int i = 0; i < n; i++)
            {
                double dx = i - mx;
                sxy += dx * (laps[from + i] - my);
                sxx += dx * dx;
            }
            return sxx < 1e-9 ? 0 : sxy / sxx;
        }
    }
}
