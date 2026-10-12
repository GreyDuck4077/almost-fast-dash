using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace AlmostFastOverlays
{
    /// <summary>
    /// Drives every set-up LED device on its own thread at up to 60 frames a second,
    /// independent of the screen. The active dashboard's light layers are composed for
    /// the current devices and each device gets its own LEDs. Out of the car
    /// or in a replay the LEDs follow nothing, as if iRacing weren't running.
    /// </summary>
    internal sealed class LightLoop : IDisposable
    {
        private const int FrameMilliseconds = 16, IdleFrameMilliseconds = 100;
        private readonly Func<TelemetrySnapshot> _telemetry;
        private readonly Func<AppSettings> _settings;
        private readonly Func<DashboardSettings> _dashboard;
        private readonly Action<LightSpan> _log;
        private readonly ManualResetEventSlim _stop = new ManualResetEventSlim(false);
        private readonly Thread _thread;

        /// <param name="log">Given each stretch of a layer lighting a group, for the light log; may be null.</param>
        public LightLoop(Func<TelemetrySnapshot> telemetry, Func<AppSettings> settings, Func<DashboardSettings> dashboard, Action<LightSpan> log = null)
        {
            _log = log;
            _telemetry = telemetry;
            _settings = settings;
            _dashboard = dashboard;
            _thread = new Thread(Run) { IsBackground = true, Name = "LED loop", Priority = ThreadPriority.AboveNormal };
            _thread.Start();
        }

        private void Run()
        {
            var links = new Dictionary<string, LedDeviceLink>();
            LightRenderer renderer = null;
            DashboardSettings lastDashboard = null;
            LightProfile profile = null;
            List<DeviceSettings> lastDevices = null;
            var activity = new LightActivity();
            try
            {
                while (!_stop.IsSet)
                {
                    int started = Environment.TickCount;
                    TelemetrySnapshot snapshot = TelemetrySnapshot.Disconnected;
                    try
                    {
                        GeneralSettings general = _settings().General;
                        List<DeviceSettings> devices = general.Devices ?? new List<DeviceSettings>();
                        DashboardSettings dashboard = _dashboard();
                        TelemetrySnapshot live = _telemetry() ?? TelemetrySnapshot.Disconnected;
                        snapshot = live.ForLights();

                        if (dashboard != lastDashboard)
                        {
                            // A new dashboard or reloaded settings: rebuild and resend everything.
                            lastDashboard = dashboard;
                            profile = dashboard?.Lights;
                            renderer = null;
                            foreach (LedDeviceLink link in links.Values) link.Invalidate();
                        }
                        if (profile != null)
                        {
                            // Devices only change with a settings reload (a new list), so that's when to check them.
                            if (renderer == null || (devices != lastDevices && renderer.Signature != LightRenderer.DeviceSignature(devices)))
                                renderer = new LightRenderer(profile, dashboard.AccentColor, new UnitSystem(general), devices);
                            lastDevices = devices;
                            LightFrame frame = renderer.Compose(snapshot, Environment.TickCount);
                            Send(links, devices, frame);
                            if (_log != null)
                                foreach (LightSpan span in activity.Observe(frame, devices, Environment.TickCount / 1000.0, Context(live), profile.Gamma))
                                    // Rev lights come and go all lap; the log is for everything else.
                                    if (span.Seconds >= 1 && !profile.Layers.Any(l => l.Name == span.Layer && l.Effect == "revs")) _log(span);
                        }
                    }
                    catch (Exception exception)
                    {
                        Console.Error.WriteLine("LED loop: " + exception.Message);
                        _stop.Wait(500);
                    }
                    int elapsed = Environment.TickCount - started;
                    // Without iRacing there's little to animate, so idle at 10 frames a second.
                    _stop.Wait(Math.Max(1, (snapshot.Connected ? FrameMilliseconds : IdleFrameMilliseconds) - elapsed));
                }
            }
            finally
            {
                foreach (LedDeviceLink link in links.Values) link.Dispose();
            }
        }

        private static string Context(TelemetrySnapshot t) =>
            !t.Connected ? "not connected" : t.OutOfCar ? "out of car" : t.Flag != RacingFlag.None ? "flag " + t.Flag : t.OnPitRoad ? "pit road" : "driving";

        private static int _statusTick;

        /// <summary>Gives each device its LEDs, following devices being added, removed or changed as it happens.</summary>
        private static void Send(Dictionary<string, LedDeviceLink> links, List<DeviceSettings> devices, LightFrame frame)
        {
            foreach (string gone in links.Keys.Where(id => devices.All(d => d.Id != id)).ToList())
            {
                links[gone].Dispose();
                links.Remove(gone);
            }
            for (int i = 0; i < devices.Count; i++)
            {
                DeviceSettings device = devices[i];
                if (!links.TryGetValue(device.Id, out LedDeviceLink link)) links[device.Id] = link = new LedDeviceLink();
                link.Apply(device);
                if (i < frame.Leds.Length) link.Send(frame.Leds[i]);
            }
            // The Devices page shows each device's connection from this.
            if (unchecked(Environment.TickCount - _statusTick) >= 1000)
            {
                _statusTick = Environment.TickCount;
                DeviceStatus.Write(links);
            }
        }

        public void Dispose()
        {
            _stop.Set();
            _thread.Join(3000);
            _stop.Dispose();
        }
    }
}
