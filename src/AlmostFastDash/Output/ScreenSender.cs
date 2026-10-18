using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;

namespace AlmostFastDash.Output
{
    /// <summary>
    /// Sends the dashboard to the wheel screen on its own thread, so the next frame is drawn while this one goes
    /// down the USB cable. It connects (and reconnects) by itself, sends only the parts of a frame that changed
    /// (Settings › Devices › Send only what changed), and always sends the newest frame: one that arrives while
    /// another is still being sent replaces any waiting. While the dashboard is shown on the PC instead, it leaves
    /// the screen alone: blanked, and free for other programs.
    /// </summary>
    internal sealed class ScreenSender : IDisposable
    {
        /// <summary>The connected screen's model and size, for the main window and the template picker.</summary>
        internal static readonly string InfoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "screen.info");

        /// <summary>There while the dashboard is connected to the screen, so the main window can say so.</summary>
        internal static readonly string LinkPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "screen.link");

        private readonly Func<GeneralSettings> _settings;
        private readonly ScreenStats _stats;
        private readonly Thread _thread;
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);
        /// <summary>Set while no frame is waiting to be sent.</summary>
        private readonly ManualResetEventSlim _taken = new ManualResetEventSlim(true);
        private readonly object _gate = new object();
        private volatile bool _running = true, _connected, _hasNext;
        private volatile int _width, _height, _generation, _brightness = 100;
        private volatile WheelScreen _screen;
        private int[] _next, _work, _shown;
        private bool _shownValid;
        private byte[] _panel, _area;
        private int _sentBrightness = -1;
        private string _lastProblem;

        public ScreenSender(Func<GeneralSettings> settings, ScreenStats stats)
        {
            _settings = settings;
            _stats = stats;
            _thread = new Thread(Run) { IsBackground = true, Name = "Wheel screen", Priority = ThreadPriority.AboveNormal };
            _thread.Start();
        }

        /// <summary>A screen is connected and taking frames.</summary>
        public bool Connected => _connected;

        /// <summary>The picture size of the connected screen.</summary>
        public Size Size => new Size(_width, _height);

        /// <summary>Goes up with every connection: the screen then needs a whole frame.</summary>
        public int Generation => _generation;

        /// <summary>Connected, and no frame is waiting to be sent: the moment to draw the next one.</summary>
        public bool Ready => _connected && !_hasNext;

        /// <summary>Takes a frame at the screen's size (see Size) to send; the bitmap can be reused straight away.</summary>
        public void Submit(Bitmap frame)
        {
            if (!_connected || frame.Width != _width || frame.Height != _height) return;
            lock (_gate)
            {
                if (_next == null || _next.Length != frame.Width * frame.Height) return;
                FrameEncoder.Copy(frame, _next);
                _hasNext = true;
                _taken.Reset();
            }
            _wake.Set();
        }

        /// <summary>Waits (up to the time given) until no frame is waiting to be sent.</summary>
        public void WaitReady(int milliseconds) => _taken.Wait(milliseconds);

        public void SetBrightness(int percent) => _brightness = percent;

        public bool TryTakeTap(out Point tap)
        {
            tap = Point.Empty;
            WheelScreen screen = _screen;
            return screen != null && screen.TryTakeTap(out tap);
        }

        private void Run()
        {
            while (_running)
            {
                // The dashboard shows in one place: here, or on the PC.
                if (!Wanted())
                {
                    if (_screen != null) Release();
                    _wake.WaitOne(500);
                    continue;
                }
                if (_screen == null && !Connect())
                {
                    _wake.WaitOne(2000);
                    continue;
                }
                _wake.WaitOne(250);
                if (!_running) break;
                // The figures stay current while nothing on the dashboard moves.
                _stats.Tick();
                try
                {
                    int brightness = _brightness;
                    if (brightness != _sentBrightness)
                    {
                        _screen.SetBrightness(brightness);
                        _sentBrightness = brightness;
                    }
                    bool have;
                    lock (_gate)
                    {
                        have = _hasNext;
                        if (have)
                        {
                            int[] swap = _work;
                            _work = _next;
                            _next = swap;
                            _hasNext = false;
                            _taken.Set();
                        }
                    }
                    if (have) Send();
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine("Screen connection lost: " + exception.Message);
                    Disconnect();
                    _wake.WaitOne(2000);
                }
            }
            Disconnect();
        }

        /// <summary>The dashboard is to show on the wheel screen, not on the PC.</summary>
        private bool Wanted()
        {
            try { return _settings().EffectivePcOutput() == "Off"; }
            catch { return true; }
        }

        /// <summary>Shown on the PC now: the screen goes black rather than keep a frozen frame, and is let go.</summary>
        private void Release()
        {
            try
            {
                WheelScreen screen = _screen;
                if (screen != null && _panel != null)
                {
                    Array.Clear(_panel, 0, _panel.Length);
                    screen.SendFrame(_panel);
                }
            }
            catch { }
            Console.WriteLine("Screen released: the dashboard shows on the PC");
            Disconnect();
        }

        private bool Connect()
        {
            if (!WheelScreen.IsPresent()) return false;
            var screen = new WheelScreen();
            try
            {
                ConnectionResult connection = screen.Connect(_brightness);
                int width = screen.Width, height = screen.Height;
                lock (_gate)
                {
                    _next = new int[width * height];
                    _work = new int[width * height];
                    _shown = new int[width * height];
                    _hasNext = false;
                    _taken.Set();
                }
                _panel = new byte[screen.FrameLength];
                _area = new byte[screen.FrameLength];
                _shownValid = false;
                _sentBrightness = _brightness;
                _width = width;
                _height = height;
                _screen = screen;
                _lastProblem = null;
                try
                {
                    File.WriteAllText(InfoPath, screen.Model.Name + "\n" + width + "x" + height + "\n" + connection.DisplayId);
                    File.WriteAllText(LinkPath, "");
                }
                catch { }
                Console.WriteLine("Screen connected: " + connection.ModelName + " (" + connection.DisplayId + ")");
                _generation++;
                _connected = true;
                return true;
            }
            catch (Exception exception)
            {
                screen.Dispose();
                // Said once, not every two seconds while it stays that way.
                if (exception.Message != _lastProblem) Console.Error.WriteLine(exception.Message);
                _lastProblem = exception.Message;
                return false;
            }
        }

        /// <summary>The frame in _work: only the areas that differ from what the screen shows, or all of it.</summary>
        private void Send()
        {
            long started = Stopwatch.GetTimestamp();
            int width = _width, height = _height;
            var whole = new Rectangle(0, 0, width, height);
            List<Rectangle> areas = _shownValid && _settings().SendChangesOnly
                ? FrameEncoder.Changes(_work, _shown, width, height)
                : new List<Rectangle> { whole };
            long pixels = 0;
            WheelScreen screen = _screen;
            if (areas.Count == 1 && areas[0] == whole)
            {
                FrameEncoder.Encode(_work, width, whole, screen.Model, _panel);
                screen.SendFrame(_panel);
                pixels = (long)width * height;
            }
            else
                foreach (Rectangle area in areas)
                {
                    screen.SendArea(FrameEncoder.Encode(_work, width, area, screen.Model, _area), _area);
                    pixels += (long)area.Width * area.Height;
                }
            // The screen now shows this frame; it's what the next one is compared with.
            int[] shown = _shown;
            _shown = _work;
            _work = shown;
            _shownValid = true;
            if (areas.Count > 0)
                _stats.Sent((Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency, pixels / (double)((long)width * height));
        }

        private void Disconnect()
        {
            _connected = false;
            lock (_gate)
            {
                _hasNext = false;
                _taken.Set();
            }
            WheelScreen screen = _screen;
            _screen = null;
            screen?.Dispose();
            try { File.Delete(LinkPath); } catch { }
        }

        public void Dispose()
        {
            _running = false;
            _wake.Set();
            _thread.Join(3000);
            _wake.Dispose();
            _taken.Dispose();
        }
    }
}
