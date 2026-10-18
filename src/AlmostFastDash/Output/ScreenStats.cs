using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace AlmostFastDash.Output
{
    /// <summary>
    /// How the wheel screen is really keeping up, measured while the dashboard runs: frames a second, the time to
    /// draw a frame, and the time to send it down the USB cable with how much of the screen that was. Drawing and
    /// sending happen on different threads, so each reports its own. Every five seconds the figures go to
    /// screen.status, where the main window and --live-check read them.
    /// </summary>
    internal sealed class ScreenStats
    {
        private static readonly string FilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "screen.status");
        private readonly object _gate = new object();
        private readonly Stopwatch _window = Stopwatch.StartNew();
        private int _drawn, _sent;
        private double _draw, _drawMax, _send, _sendMax, _share;

        /// <summary>A frame drawn, in milliseconds.</summary>
        public void Drawn(double milliseconds)
        {
            lock (_gate)
            {
                _drawn++;
                _draw += milliseconds;
                _drawMax = Math.Max(_drawMax, milliseconds);
                Publish();
            }
        }

        /// <summary>A frame sent to the wheel screen: how long it took, and what share of the screen it covered.</summary>
        public void Sent(double milliseconds, double share)
        {
            lock (_gate)
            {
                _sent++;
                _send += milliseconds;
                _sendMax = Math.Max(_sendMax, milliseconds);
                _share += share;
                Publish();
            }
        }

        /// <summary>Writes the figures if they're due, even when no frame has gone since (nothing on the dashboard moved).</summary>
        public void Tick()
        {
            lock (_gate) Publish();
        }

        private void Publish()
        {
            double seconds = _window.Elapsed.TotalSeconds;
            if (seconds < 5) return;
            if (_drawn == 0 && _sent == 0)
            {
                try { File.WriteAllText(FilePath,
                    "Nothing on the dashboard has moved for a few seconds, so nothing needed sending; measured " +
                    DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture)); } catch { }
                _window.Restart();
                return;
            }
            // Frames that reached the wheel screen; without one (a monitor only), frames drawn.
            int frames = _sent > 0 ? _sent : _drawn;
            string sending = _sent == 0 ? "" : string.Format(CultureInfo.InvariantCulture,
                "; sending {0:0.0} ms on average, {1:0.0} at most, {2:0}% of the screen a frame", _send / _sent, _sendMax,
                100 * _share / _sent);
            string text = string.Format(CultureInfo.InvariantCulture,
                "{0:0.0} frames a second (redrawn only when something changed); drawing {1:0.0} ms on average, {2:0.0} at most{3}; " +
                    "measured {4:HH:mm:ss}",
                frames / seconds, _drawn == 0 ? 0 : _draw / _drawn, _drawMax, sending, DateTime.Now);
            try { File.WriteAllText(FilePath, text); } catch { }
            _drawn = _sent = 0;
            _draw = _drawMax = _send = _sendMax = _share = 0;
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
