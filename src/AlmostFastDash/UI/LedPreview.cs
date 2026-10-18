using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using DrawingColor = System.Drawing.Color;

namespace AlmostFastDash.UI
{
    /// <summary>
    /// Shows frames on a device while it's being set up. Frames are sent from a background
    /// thread, always the newest one, so a slider dragged quickly never queues up stale frames.
    /// </summary>
    internal sealed class LedPreview : IDisposable
    {
        private readonly ILedOutput _output;
        private readonly Thread _thread;
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private readonly string _signature;
        private DrawingColor[] _pending;
        private volatile bool _stop;

        private int _sent;

        /// <summary>The most LEDs a frame may hold. Frames are sent as long as they are, never padded,
        /// so a board is only ever sent LEDs the setup has asked for.</summary>
        public int Limit { get; }

        public LedPreview(DeviceSettings device, ILedOutput output = null)
        {
            var open = SettingsStore.Clone(device);
            Limit = device.Kind == LedDeviceKinds.Wled ? Math.Max(device.LedCount, 1) : device.Kind == LedDeviceKinds.Adalight ? 300 : ArduinoLedController.MaxLeds;
            open.LedCount = Limit;
            _output = output ?? LedDeviceLink.Open(open);
            _signature = device.Kind + "|" + device.Connection;
            _thread = new Thread(Run) { IsBackground = true, Name = "LED setup preview" };
            _thread.Start();
        }

        public bool Matches(DeviceSettings device) => _signature == device.Kind + "|" + device.Connection;

        /// <summary>Why the last frame couldn't be sent (the device was unplugged, say), or null while it's fine.</summary>
        public string LastError { get; private set; }

        public void Show(DrawingColor[] frame)
        {
            Volatile.Write(ref _pending, frame);
            _wake.Set();
        }

        private void Run()
        {
            DrawingColor[] last = null;
            while (!_stop)
            {
                _wake.WaitOne(_output.KeepAliveMilliseconds > 0 ? _output.KeepAliveMilliseconds : 1000);
                DrawingColor[] frame = Volatile.Read(ref _pending);
                if (frame == null || (frame == last && _output.KeepAliveMilliseconds == 0)) continue;
                try
                {
                    var sized = new DrawingColor[Math.Min(frame.Length, Limit)];
                    Array.Copy(frame, sized, sized.Length);
                    if (sized.Length == 0) continue;
                    _output.Send(sized);
                    _sent = sized.Length;
                    last = frame;
                    LastError = null;
                }
                catch (Exception exception) { LastError = exception.Message; }
            }
        }

        public void Dispose()
        {
            _stop = true;
            _wake.Set();
            _thread.Join(1500);
            // Off, for exactly the LEDs that were lit.
            if (_sent > 0) try { _output.Send(new DrawingColor[_sent]); } catch { }
            _output.Dispose();
            _wake.Dispose();
        }
    }

    /// <summary>LEDs that go nowhere: setup screenshots without touching hardware.</summary>
    internal sealed class NowhereOutput : ILedOutput
    {
        public int KeepAliveMilliseconds => 0;
        public string Detail => "nowhere";
        public void Send(DrawingColor[] frame) { }
        public void Dispose() { }
    }
}
