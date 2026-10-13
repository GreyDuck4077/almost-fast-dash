using System;
using System.Collections.Generic;

namespace AlmostFastOverlays
{
    /// <summary>
    /// The direction anything that fills up does it in: on-screen bars and shift lights, and LED strips.
    /// One set of names and one set of rules, so every fill offers the same four choices.
    /// </summary>
    internal static class FillOrder
    {
        public const string StartToEnd = "Start to end", EndToStart = "End to start", EdgesToCenter = "Edges to center", CenterOut = "Center out";
        public static readonly string[] All = { StartToEnd, EndToStart, EdgesToCenter, CenterOut };

        /// <summary>Today's name for a direction, including the names older versions saved.</summary>
        public static string Normalize(string name)
        {
            switch (name)
            {
                case "Left to right": return StartToEnd;
                case "Right to left": return EndToStart;
                case "Center outward": return CenterOut;
                default: return Array.IndexOf(All, name) >= 0 ? name : StartToEnd;
            }
        }

        /// <summary>Filling from both ends at once, or from the middle out to both ends.</summary>
        public static bool Symmetric(string order) => order == EdgesToCenter || order == CenterOut;

        /// <summary>How many distinct steps n lights fill in: half as many when both sides fill together.</summary>
        public static int Steps(string order, int n) => Symmetric(order) ? (n + 1) / 2 : n;

        /// <summary>The step at which light i of n comes on, 0 first.</summary>
        public static int Rank(string order, int i, int n)
        {
            switch (order)
            {
                case EndToStart: return n - 1 - i;
                case EdgesToCenter: return Math.Min(i, n - 1 - i);
                case CenterOut: return (int)Math.Floor(Math.Abs(i - (n - 1) / 2.0));
                default: return i;
            }
        }

        /// <summary>
        /// The lit stretches of a continuous bar (0 to 1 along it) at a fill fraction. Each runs from
        /// the point the fill grows out of (From) to its leading edge (To), so From can be the larger.
        /// </summary>
        public static List<(float From, float To)> Spans(string order, double fraction)
        {
            float f = (float)Math.Max(0, Math.Min(1, double.IsNaN(fraction) ? 0 : fraction));
            switch (order)
            {
                case EndToStart: return new List<(float, float)> { (1, 1 - f) };
                case EdgesToCenter: return new List<(float, float)> { (0, f / 2), (1, 1 - f / 2) };
                case CenterOut: return new List<(float, float)> { (0.5f, 0.5f - f / 2), (0.5f, 0.5f + f / 2) };
                default: return new List<(float, float)> { (0, f) };
            }
        }

        /// <summary>Where a marker at a fraction sits along the bar: one place, or a mirrored pair when both sides fill.</summary>
        public static float[] Marks(string order, double fraction)
        {
            var marks = new List<float>();
            foreach ((float _, float to) in Spans(order, fraction)) marks.Add(to);
            return marks.ToArray();
        }
    }
}
