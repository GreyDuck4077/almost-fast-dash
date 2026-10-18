using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO.Ports;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace AlmostFastDash.Devices
{
    /// <summary>
    /// Estimates the current addressable LEDs draw and dims frames that would draw too much.
    /// A typical RGB LED draws about 20 mA per color channel at full level, 60 mA white.
    /// </summary>
    internal static class LedPower
    {
        /// <summary>The default limit: plenty for rev lights and colored alerts, while keeping a
        /// USB-powered wheel hub from browning out when a whole strip lights white.</summary>
        public const int DefaultMilliamps = 250;
        private const double MilliampsPerChannel = 20;

        public static double Milliamps(Color[] frame)
        {
            double total = 0;
            foreach (Color c in frame) total += (c.R + c.G + c.B) / 255.0 * MilliampsPerChannel;
            return total;
        }

        /// <summary>The frame itself when it's within <paramref name="maxMilliamps"/>, otherwise a dimmed copy.</summary>
        public static Color[] Limit(Color[] frame, int maxMilliamps)
        {
            if (maxMilliamps <= 0 || frame == null) return frame;
            double draw = Milliamps(frame);
            if (draw <= maxMilliamps) return frame;
            double scale = maxMilliamps / draw;
            var dimmed = new Color[frame.Length];
            for (int i = 0; i < frame.Length; i++)
                dimmed[i] = Color.FromArgb((int)(frame[i].R * scale), (int)(frame[i].G * scale), (int)(frame[i].B * scale));
            return dimmed;
        }
    }

    /// <summary>A device output with the power limit applied to every frame.</summary>
    internal sealed class LimitedOutput : ILedOutput
    {
        private readonly ILedOutput _inner;
        private readonly int _maxMilliamps;

        public LimitedOutput(ILedOutput inner, int maxMilliamps)
        {
            _inner = inner;
            _maxMilliamps = maxMilliamps;
        }

        public int KeepAliveMilliseconds => _inner.KeepAliveMilliseconds;
        public void Send(Color[] frame) => _inner.Send(LedPower.Limit(frame, _maxMilliamps));
        public string Detail => _inner.Detail + (_maxMilliamps > 0 ? ", USB power limit" : "");
        public void Dispose() => _inner.Dispose();
    }
}
