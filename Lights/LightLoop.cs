using System;
using System.Drawing;
using System.Threading;

namespace AlmostFastOverlays
{
    /// <summary>
    /// Drives the DV-5 and LB-10 LEDs on their own thread at up to 60 frames a
    /// second, independent of the screen. Dashboards with a light profile use the
    /// layer renderer; the rest keep their classic light settings.
    /// </summary>
    internal sealed class LightLoop : IDisposable
    {
        private const int FrameMilliseconds = 16, IdleFrameMilliseconds = 100;
        private readonly Func<TelemetrySnapshot> _telemetry;
        private readonly Func<AppSettings> _settings;
        private readonly Func<DashboardSettings> _dashboard;
        private readonly string _ledPortOverride;
        private readonly ManualResetEventSlim _stop = new ManualResetEventSlim(false);
        private readonly Thread _thread;

        public LightLoop(Func<TelemetrySnapshot> telemetry, Func<AppSettings> settings, Func<DashboardSettings> dashboard, string ledPortOverride)
        {
            _telemetry = telemetry;
            _settings = settings;
            _dashboard = dashboard;
            _ledPortOverride = ledPortOverride;
            _thread = new Thread(Run) { IsBackground = true, Name = "LED loop", Priority = ThreadPriority.AboveNormal };
            _thread.Start();
        }

        private void Run()
        {
            GeneralSettings general = _settings().General;
            var dv5 = new LedLink(_ledPortOverride ?? general.LedPort ?? "COM7", LedFrame.Count, "SSG DV 5", DevicePorts.Dv5);
            var brow = new LedLink(general.BrowPort, general.BrowLedCount, "SSG LB-10", DevicePorts.LightBar) { Enabled = general.BrowEnabled };
            LightRenderer renderer = null;
            DashboardSettings lastDashboard = null;
            UnitSystem units = new UnitSystem(general);
            try
            {
                while (!_stop.IsSet)
                {
                    int started = Environment.TickCount;
                    try
                    {
                        general = _settings().General;
                        dv5.Apply(_ledPortOverride ?? general.LedPort ?? "COM7", LedFrame.Count, true, general.FastLedUpdates);
                        brow.Apply(general.BrowPort, general.BrowLedCount, general.BrowEnabled, general.FastLedUpdates);

                        DashboardSettings dashboard = _dashboard();
                        TelemetrySnapshot snapshot = _telemetry() ?? TelemetrySnapshot.Disconnected;
                        if (dashboard != lastDashboard)
                        {
                            // A new dashboard or reloaded settings: rebuild and resend everything.
                            lastDashboard = dashboard;
                            units = new UnitSystem(general);
                            renderer = null;
                            dv5.Invalidate();
                            brow.Invalidate();
                        }

                        if (dashboard?.Lights != null)
                        {
                            if (renderer == null || renderer.BrowCount != Math.Max(1, Math.Min(64, brow.Count)))
                                renderer = new LightRenderer(dashboard.Lights, dashboard.AccentColor, units, brow.Count);
                            LightFrame frame = renderer.Compose(snapshot, Environment.TickCount);
                            dv5.Send(frame.Dv5);
                            if (brow.Enabled) brow.Send(frame.Brow);
                        }
                        else
                        {
                            bool shiftFlashOn = snapshot.ShiftAlert && ((Environment.TickCount / 140) & 1) == 0;
                            LedInputs inputs = LedInputs.From(snapshot, shiftFlashOn);
                            dv5.Send(LedFrame.Compose(dashboard, inputs));
                            if (brow.Enabled && dashboard != null)
                                brow.Send(LedFrame.ComposeBrow(dashboard.BrowLeds, brow.Count, inputs));
                        }
                    }
                    catch (Exception exception)
                    {
                        Console.Error.WriteLine("LED loop: " + exception.Message);
                        _stop.Wait(500);
                    }
                    int elapsed = Environment.TickCount - started;
                    // Without iRacing there's little to animate, so idle at 10 frames a second.
                    bool connected = _telemetry()?.Connected == true;
                    _stop.Wait(Math.Max(1, (connected ? FrameMilliseconds : IdleFrameMilliseconds) - elapsed));
                }
            }
            finally
            {
                dv5.Dispose();
                brow.Dispose();
            }
        }

        public void Dispose()
        {
            _stop.Set();
            _thread.Join(3000);
            _stop.Dispose();
        }
    }

    /// <summary>
    /// One serial LED device (DV-5 or LB-10): connects lazily, retries every five
    /// seconds while unplugged, follows the device if Windows renumbers its port,
    /// and only sends frames that changed.
    /// </summary>
    internal sealed class LedLink : IDisposable
    {
        private readonly string _expectedName;
        private readonly string _hardwareId;
        private Dv5LedController _controller;
        private DateTime _nextAttemptUtc = DateTime.MinValue;
        private Color[] _lastFrame;
        private bool _batched;

        public LedLink(string port, int count, string expectedName, string hardwareId)
        {
            Port = port;
            Count = Math.Max(1, Math.Min(64, count));
            _expectedName = expectedName;
            _hardwareId = hardwareId;
        }

        public string Port { get; private set; }
        public int Count { get; private set; }
        public bool Enabled { get; set; } = true;

        public void Invalidate() => _lastFrame = null;

        /// <summary>Applies settings, reconnecting only when the port, size or on/off state changed.</summary>
        public void Apply(string port, int count, bool enabled, bool batched)
        {
            count = Math.Max(1, Math.Min(64, count));
            if (port != Port || count != Count || enabled != Enabled)
            {
                Close();
                Port = port;
                Count = count;
                Enabled = enabled;
                _nextAttemptUtc = DateTime.MinValue;
            }
            if (batched != _batched)
            {
                _batched = batched;
                if (_controller != null) _controller.Batched = batched;
                _lastFrame = null;
            }
        }

        public void Send(Color[] frame)
        {
            if (!Enabled || string.IsNullOrWhiteSpace(Port)) return;
            if (_controller == null)
            {
                if (DateTime.UtcNow < _nextAttemptUtc) return;
                _nextAttemptUtc = DateTime.UtcNow.AddSeconds(5);
                string port = DevicePorts.Resolve(Port, _hardwareId);
                var controller = new Dv5LedController(port, 115200, Count, _expectedName) { Batched = _batched };
                try
                {
                    controller.Connect();
                    Console.WriteLine("LED device on " + port + ": " + controller.QueryDeviceName());
                    _controller = controller;
                    _lastFrame = null;
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine(_expectedName + " unavailable on " + port + ": " + exception.Message);
                    controller.Dispose();
                    return;
                }
            }
            if (LedFrame.SameFrame(frame, _lastFrame)) return;
            try
            {
                _controller.SetFrame(frame);
                _lastFrame = frame;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(_expectedName + " lost: " + exception.Message);
                Close();
            }
        }

        private void Close()
        {
            _controller?.Dispose();
            _controller = null;
            _lastFrame = null;
        }

        public void Dispose() => Close();
    }
}
