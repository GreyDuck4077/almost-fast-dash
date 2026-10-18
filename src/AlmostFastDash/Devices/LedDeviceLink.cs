using System;
using System.Drawing;

namespace AlmostFastDash.Devices
{
    /// <summary>
    /// Keeps one extra device connected: opens it in the background (an Arduino takes a
    /// second or two to start, and the light loop must never wait for that), retries every
    /// five seconds while it's missing, sends only frames that changed (plus a keep-alive
    /// where the device needs one), and reopens when its settings change. A device that keeps
    /// dropping soon after it connects (a USB supply that can't hold it) is retried less and less
    /// often, up to a minute apart, so the app doesn't keep knocking it over.
    /// </summary>
    internal sealed class LedDeviceLink : IDisposable
    {
        private ILedOutput _output;
        private System.Threading.Tasks.Task<ILedOutput> _opening;
        private string _signature;
        private DateTime _nextAttemptUtc = DateTime.MinValue, _lastSendUtc = DateTime.MinValue;
        private Color[] _lastFrame;
        private DateTime _connectedUtc;
        private int _drops;

        public DeviceSettings Device { get; private set; }

        /// <summary>How the connection is going (<see cref="LinkStates"/>), and a few words on it, for the device's row.</summary>
        public string State { get; private set; } = LinkStates.Connecting;
        public string Detail { get; private set; }

        /// <summary>Forgets the last frame so the next one is sent even if it's the same.</summary>
        public void Invalidate() => _lastFrame = null;

        public void Apply(DeviceSettings device)
        {
            // The same settings object as last frame: nothing to check.
            if (ReferenceEquals(device, Device)) return;
            string signature = device.Kind + "|" + device.Connection + "|" + device.UsbId + "|" + device.Baud + "|" + device.LedCount + "|" + device.Enabled + "|" + device.MaxMilliamps;
            Device = device;
            if (signature == _signature) return;
            _signature = signature;
            Close();
            _nextAttemptUtc = DateTime.MinValue;
            _drops = 0;
        }

        public void Send(Color[] frame)
        {
            if (Device == null || !Device.Enabled || string.IsNullOrWhiteSpace(Device.Connection)) { State = LinkStates.Off; Detail = null; return; }
            if (_output == null)
            {
                if (_opening != null)
                {
                    if (!_opening.IsCompleted) return;
                    // Each device sends on its own thread, so a slow one never holds up the others.
                    if (_opening.Status == System.Threading.Tasks.TaskStatus.RanToCompletion) _output = new BackgroundOutput(_opening.Result, Device.Name);
                    else
                    {
                        Exception cause = _opening.Exception?.GetBaseException();
                        string why = cause?.Message;
                        Console.Error.WriteLine(Device.Name + " unavailable on " + Device.Connection + ": " + why);
                        // Access denied: another program (other dashboard software) has the port open.
                        if (cause is UnauthorizedAccessException) State = LinkStates.Busy;
                        else if (State != LinkStates.Dropping) State = LinkStates.Missing;
                        Detail = why;
                    }
                    _opening = null;
                    if (_output == null) return;
                    _connectedUtc = DateTime.UtcNow;
                    State = LinkStates.Connected;
                    Detail = _output.Detail;
                }
                else
                {
                    if (DateTime.UtcNow < _nextAttemptUtc) return;
                    _nextAttemptUtc = DateTime.UtcNow.AddSeconds(5);
                    if (State == LinkStates.Off || State == LinkStates.Connected) State = LinkStates.Connecting;
                    DeviceSettings device = Device;
                    _opening = System.Threading.Tasks.Task.Run(() => Open(device));
                    return;
                }
            }
            bool keepAlive = _output.KeepAliveMilliseconds > 0 && (DateTime.UtcNow - _lastSendUtc).TotalMilliseconds >= _output.KeepAliveMilliseconds;
            if (!keepAlive && SameFrame(frame, _lastFrame)) return;
            try
            {
                _output.Send(frame);
                _lastFrame = frame;
                _lastSendUtc = DateTime.UtcNow;
                if (_drops > 0 && (_lastSendUtc - _connectedUtc).TotalSeconds > 30) _drops = 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(Device.Name + " lost: " + exception.Message);
                Close();
                // Lost again soon after connecting: back off 5, 10, 20, 40 then 60 seconds.
                _drops = (DateTime.UtcNow - _connectedUtc).TotalSeconds < 30 ? _drops + 1 : 1;
                _nextAttemptUtc = DateTime.UtcNow.AddSeconds(Math.Min(60, 5 * Math.Pow(2, _drops - 1)));
                State = _drops >= 2 ? LinkStates.Dropping : LinkStates.Connecting;
                Detail = exception.Message;
            }
        }

        private static bool SameFrame(Color[] a, Color[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i].ToArgb() != b[i].ToArgb()) return false;
            return true;
        }

        public static ILedOutput Open(DeviceSettings device)
        {
            switch (device.Kind)
            {
                case LedDeviceKinds.Adalight: return new LimitedOutput(new AdalightOutput(device), device.MaxMilliamps);
                // WLED limits its own power, from its power supply settings.
                case LedDeviceKinds.Wled: return new WledOutput(device);
                default: return new LimitedOutput(new ArduinoOutput(device), device.MaxMilliamps);
            }
        }

        private void Close()
        {
            _output?.Dispose();
            _output = null;
            _lastFrame = null;
            // A connection still being opened is closed as soon as it finishes.
            System.Threading.Tasks.Task<ILedOutput> opening = _opening;
            _opening = null;
            opening?.ContinueWith(t => { if (t.Status == System.Threading.Tasks.TaskStatus.RanToCompletion) t.Result.Dispose(); });
        }

        /// <summary>Closes the device, waiting for a connection still being opened so the port is free on return.</summary>
        public void Dispose()
        {
            System.Threading.Tasks.Task<ILedOutput> opening = _opening;
            _opening = null;
            Close();
            if (opening == null) return;
            try { if (opening.Wait(3000)) opening.Result.Dispose(); } catch { }
        }
    }
}
