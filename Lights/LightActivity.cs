using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace AlmostFastOverlays
{
    /// <summary>One light layer lighting one LED group for a stretch of time.</summary>
    internal sealed class LightSpan
    {
        public string Device, Group, Layer, Colour, Context;
        public DateTime Started;
        public double From, To;
        public double Seconds => To - From;
    }

    /// <summary>
    /// Follows which layer lights each LED group, frame by frame, and hands back each stretch
    /// once it ends. Used by the light log and the recording replay to answer "what was that light?".
    /// </summary>
    internal sealed class LightActivity
    {
        private const double GapSeconds = 0.6;
        private readonly Dictionary<string, LightSpan> _open = new Dictionary<string, LightSpan>();

        /// <param name="now">Seconds on any steady clock.</param>
        /// <param name="context">What was going on (flag, in or out of the car), kept with stretches that start now.</param>
        /// <param name="gamma">Whether the frame went through gamma, so colours are named as they were designed.</param>
        /// <returns>Stretches that have ended.</returns>
        public List<LightSpan> Observe(LightFrame frame, IReadOnlyList<DeviceSettings> devices, double now, string context, bool gamma = true)
        {
            var seen = new HashSet<string>();
            for (int d = 0; d < devices.Count && d < frame.Owners.Length; d++)
            {
                DeviceSettings device = devices[d];
                if (device == null || !device.Enabled) continue;
                foreach (LedGroup group in device.Groups)
                    foreach (int index in group.Indexes())
                    {
                        if (index >= frame.Owners[d].Length) continue;
                        string layer = frame.Owners[d][index];
                        Color c = frame.Leds[d][index];
                        if (layer == null || (c.R | c.G | c.B) == 0) continue;
                        string key = device.Id + "|" + group.Id + "|" + layer;
                        if (!seen.Add(key)) continue;
                        if (_open.TryGetValue(key, out LightSpan span)) span.To = now;
                        else _open[key] = new LightSpan { Device = device.Name, Group = group.Name, Layer = layer, Colour = ColourName(gamma ? Ungamma(c) : c), Context = context, Started = DateTime.Now, From = now, To = now };
                    }
            }
            List<string> ended = _open.Where(kv => !seen.Contains(kv.Key) && now - kv.Value.To >= GapSeconds).Select(kv => kv.Key).ToList();
            var result = ended.Select(k => _open[k]).ToList();
            foreach (string key in ended) _open.Remove(key);
            return result;
        }

        /// <summary>Ends every stretch still going.</summary>
        public List<LightSpan> Flush()
        {
            var result = _open.Values.ToList();
            _open.Clear();
            return result;
        }

        private static Color Ungamma(Color c)
        {
            int U(int v) => (int)Math.Round(255 * Math.Pow(v / 255.0, 1 / 2.2));
            return Color.FromArgb(U(c.R), U(c.G), U(c.B));
        }

        public static string ColourName(Color c)
        {
            int max = Math.Max(c.R, Math.Max(c.G, c.B));
            if (max == 0) return "off";
            double r = c.R / (double)max, g = c.G / (double)max, b = c.B / (double)max;
            if (r > 0.8 && g > 0.8 && b > 0.8) return "white";
            if (r > 0.8 && g > 0.55 && b < 0.4) return "yellow";
            if (r > 0.8 && g > 0.2 && b < 0.4) return "orange";
            if (r > 0.8 && b < 0.5) return "red";
            if (g > 0.8 && r < 0.5 && b < 0.5) return "green";
            if (b > 0.8 && r < 0.5) return g > 0.6 ? "cyan" : "blue";
            if (g > 0.8 && b > 0.6 && r < 0.5) return "cyan";
            return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
        }
    }
}
