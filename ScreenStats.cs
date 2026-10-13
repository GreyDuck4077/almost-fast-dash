using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace AlmostFastOverlays
{
    /// <summary>
    /// How the wheel screen is really keeping up, measured while the dashboard runs: frames a second, the
    /// time to draw a frame and the time to send it down the USB cable. Every five seconds the figures go
    /// to screen.status, where the settings window and --live-check read them.
    /// </summary>
    internal sealed class ScreenStats
    {
        private static readonly string FilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "screen.status");
        private readonly Stopwatch _window = Stopwatch.StartNew();
        private int _frames;
        private double _render, _send, _renderMax, _sendMax;

        /// <summary>One frame: how long drawing it and sending it took, in milliseconds.</summary>
        public void Frame(double renderMs, double sendMs)
        {
            _frames++;
            _render += renderMs;
            _send += sendMs;
            _renderMax = Math.Max(_renderMax, renderMs);
            _sendMax = Math.Max(_sendMax, sendMs);
            double seconds = _window.Elapsed.TotalSeconds;
            if (seconds < 5) return;
            string text = string.Format(CultureInfo.InvariantCulture,
                "{0:0.0} frames a second (redrawn only when something changed); drawing {1:0.0} ms on average, {2:0.0} at most; sending {3:0.0} ms on average, {4:0.0} at most; measured {5:HH:mm:ss}",
                _frames / seconds, _render / _frames, _renderMax, _send / _frames, _sendMax, DateTime.Now);
            try { File.WriteAllText(FilePath, text); } catch { }
            _frames = 0;
            _render = _send = _renderMax = _sendMax = 0;
            _window.Restart();
        }

        /// <summary>The last figures, or null when none were written in the last minute.</summary>
        public static string Read()
        {
            try
            {
                if (!File.Exists(FilePath) || DateTime.UtcNow - File.GetLastWriteTimeUtc(FilePath) > TimeSpan.FromMinutes(1)) return null;
                return File.ReadAllText(FilePath);
            }
            catch { return null; }
        }
    }
}
