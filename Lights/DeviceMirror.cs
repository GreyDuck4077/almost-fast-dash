using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace AlmostFastOverlays
{
    /// <summary>
    /// Shows a preview's light frames on the real LED devices, so a dashboard's lights can be
    /// seen on the hardware while the dashboard isn't running. Sends on its own thread (a serial
    /// board takes a few milliseconds a frame) and only the latest frame. Release lets go of the
    /// devices, turning their LEDs off, so the dashboard or device setup can have them.
    /// </summary>
    internal sealed class DeviceMirror : IDisposable
    {
        private const int FrameMilliseconds = 33;
        private readonly object _gate = new object();
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private Thread _thread;
        private volatile bool _stop;
        private LightFrame _frame;
        private List<DeviceSettings> _devices;
        private string _signature;

        public bool Active => _thread != null;

        /// <summary>Queues a frame for the devices it was composed for, connecting to them first if needed.</summary>
        public void Show(LightFrame frame, IEnumerable<DeviceSettings> devices)
        {
            // The sending thread gets its own copy of the devices, made again only when they change.
            List<DeviceSettings> list = devices.ToList();
            string signature = LightRenderer.DeviceSignature(list) + "|" + string.Join("|", list.Select(d => d.Connection + d.MaxMilliamps));
            lock (_gate)
            {
                _frame = frame;
                if (signature != _signature)
                {
                    _devices = list.Select(SettingsStore.Clone).ToList();
                    _signature = signature;
                }
            }
            if (_thread == null)
            {
                _stop = false;
                _thread = new Thread(Run) { IsBackground = true, Name = "LED preview on devices" };
                _thread.Start();
            }
            _wake.Set();
        }

        /// <summary>Turns the LEDs off and lets go of the devices. Returns once they're free.</summary>
        public void Release()
        {
            Thread thread = _thread;
            if (thread == null) return;
            _stop = true;
            _wake.Set();
            thread.Join(3000);
            _thread = null;
        }

        private void Run()
        {
            var links = new Dictionary<string, LedDeviceLink>();
            int statusTick = 0;
            try
            {
                while (!_stop)
                {
                    _wake.WaitOne(FrameMilliseconds);
                    if (_stop) break;
                    LightFrame frame;
                    List<DeviceSettings> devices;
                    lock (_gate)
                    {
                        frame = _frame;
                        devices = _devices;
                    }
                    if (frame == null || devices == null) continue;
                    foreach (string gone in links.Keys.Where(id => devices.All(d => d.Id != id)).ToList())
                    {
                        links[gone].Dispose();
                        links.Remove(gone);
                    }
                    for (int i = 0; i < devices.Count && i < frame.Leds.Length; i++)
                    {
                        DeviceSettings device = devices[i];
                        if (!links.TryGetValue(device.Id, out LedDeviceLink link)) links[device.Id] = link = new LedDeviceLink();
                        link.Apply(device);
                        link.Send(frame.Leds[i]);
                    }
                    if (unchecked(Environment.TickCount - statusTick) >= 1000)
                    {
                        statusTick = Environment.TickCount;
                        DeviceStatus.Write(links);
                    }
                    Thread.Sleep(FrameMilliseconds);
                }
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("LED preview: " + exception.Message);
            }
            finally
            {
                // Closing each link turns its LEDs off.
                foreach (LedDeviceLink link in links.Values) link.Dispose();
            }
        }

        public void Dispose()
        {
            Release();
            _wake.Dispose();
        }
    }
}
