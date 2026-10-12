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

namespace AlmostFastOverlays
{
    /// <summary>The kinds of LED device besides the DV-5 and LB-10.</summary>
    internal static class LedDeviceKinds
    {
        /// <summary>An Arduino board speaking the common sim-racing LED serial protocol (as the DV-5 and LB-10 do).</summary>
        public const string Arduino = "Arduino";
        /// <summary>An Arduino running an Adalight sketch: the open "Ada" serial protocol for LED strips.</summary>
        public const string Adalight = "Adalight";
        /// <summary>A WLED controller (usually an ESP32) on the network, driven over UDP.</summary>
        public const string Wled = "WLED";
        public static readonly string[] All = { Arduino, Adalight, Wled };
    }

    /// <summary>What a device's LEDs show, taken from the same lights the DV-5 and LB-10 use.</summary>
    internal static class LedRoles
    {
        public const string Shift = "Shift lights", Bar = "Light bar", Flags = "Flags";
        public static readonly string[] All = { Shift, Bar, Flags };

        public static string Describe(string role)
        {
            switch (role)
            {
                case Shift: return "The DV-5's shift lights, stretched across this device.";
                case Flags: return "The current flag in its colours; dark when there is no flag.";
                default: return "What the LB-10 light bar shows, stretched across this device.";
            }
        }
    }

    /// <summary>One extra LED device, as stored in settings.</summary>
    internal sealed class LedDeviceSettings
    {
        public string Id { get; set; } = "led-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        public string Name { get; set; } = "LED device";
        public string Kind { get; set; } = LedDeviceKinds.Arduino;
        /// <summary>COM port, for Arduino and Adalight devices.</summary>
        public string Port { get; set; }
        /// <summary>IP address or host name, for WLED.</summary>
        public string Address { get; set; }
        public int Baud { get; set; } = 115200;
        public int LedCount { get; set; } = 8;
        public string Role { get; set; } = LedRoles.Bar;
        /// <summary>Runs the LEDs right to left, for strips mounted the other way round.</summary>
        public bool Reverse { get; set; }
        /// <summary>0-100.</summary>
        public int Brightness { get; set; } = 100;
        public bool Enabled { get; set; } = true;

        public void Normalize()
        {
            if (string.IsNullOrWhiteSpace(Id)) Id = "led-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            if (!LedDeviceKinds.All.Contains(Kind)) Kind = LedDeviceKinds.Arduino;
            if (!LedRoles.All.Contains(Role)) Role = LedRoles.Bar;
            if (string.IsNullOrWhiteSpace(Name)) Name = Kind + " LEDs";
            LedCount = Math.Max(1, Math.Min(Kind == LedDeviceKinds.Wled ? 1500 : ArduinoLedController.MaxLeds, LedCount));
            Brightness = Math.Max(0, Math.Min(100, Brightness));
            if (Baud <= 0) Baud = 115200;
        }

        [ScriptIgnore]
        public string Connection => Kind == LedDeviceKinds.Wled ? Address : Port;
    }

    /// <summary>Sends frames to one kind of device.</summary>
    internal interface ILedOutput : IDisposable
    {
        void Send(Color[] frame);
        /// <summary>How often to resend an unchanged frame so the device doesn't time out; 0 for never.</summary>
        int KeepAliveMilliseconds { get; }
    }

    /// <summary>A board on the common sim-racing LED protocol.</summary>
    internal sealed class ArduinoOutput : ILedOutput
    {
        private readonly ArduinoLedController _controller;

        public ArduinoOutput(LedDeviceSettings device, bool batched)
        {
            _controller = new ArduinoLedController(device.Port, device.Baud, device.LedCount, null) { Batched = batched };
            try
            {
                _controller.Connect();
                Name = _controller.QueryDeviceName();
            }
            catch
            {
                _controller.Dispose();
                throw;
            }
        }

        public string Name { get; }
        public int KeepAliveMilliseconds => 0;
        public void Send(Color[] frame) => _controller.SetFrame(frame);
        public void Dispose() => _controller.Dispose();
    }

    /// <summary>
    /// An Adalight sketch: "Ada", the LED count less one (high and low byte), a checksum
    /// of those two XOR 0x55, then red, green and blue for every LED.
    /// </summary>
    internal sealed class AdalightOutput : ILedOutput
    {
        private readonly SerialPort _port;

        public AdalightOutput(LedDeviceSettings device)
        {
            _port = new SerialPort(device.Port, device.Baud) { DtrEnable = true, WriteTimeout = 500, ReadTimeout = 500 };
            _port.Open();
            // Opening the port resets most Arduinos; give the sketch time to start.
            Thread.Sleep(1600);
            _port.DiscardInBuffer();
        }

        public int KeepAliveMilliseconds => 1000;

        public void Send(Color[] frame)
        {
            byte[] packet = Packet(frame);
            _port.Write(packet, 0, packet.Length);
        }

        internal static byte[] Packet(Color[] frame)
        {
            int n = Math.Max(1, frame.Length);
            var packet = new byte[6 + n * 3];
            packet[0] = (byte)'A';
            packet[1] = (byte)'d';
            packet[2] = (byte)'a';
            packet[3] = (byte)((n - 1) >> 8);
            packet[4] = (byte)((n - 1) & 0xFF);
            packet[5] = (byte)(packet[3] ^ packet[4] ^ 0x55);
            for (int i = 0; i < frame.Length; i++)
            {
                packet[6 + i * 3] = frame[i].R;
                packet[7 + i * 3] = frame[i].G;
                packet[8 + i * 3] = frame[i].B;
            }
            return packet;
        }

        public void Dispose()
        {
            try
            {
                if (_port.IsOpen)
                {
                    try { Send(new Color[1]); } catch { }
                    _port.Close();
                }
            }
            catch { }
            _port.Dispose();
        }
    }

    /// <summary>
    /// A WLED controller, through its real-time UDP protocol on port 21324: DRGB for up
    /// to 490 LEDs, DNRGB (with a start index) in chunks beyond that. WLED goes back to
    /// its own effect a couple of seconds after frames stop.
    /// </summary>
    internal sealed class WledOutput : ILedOutput
    {
        public const int Port = 21324;
        private const byte TimeoutSeconds = 2;
        private readonly UdpClient _udp;

        public WledOutput(LedDeviceSettings device)
        {
            _udp = new UdpClient();
            _udp.Connect(device.Address, Port);
        }

        public int KeepAliveMilliseconds => 1000;

        public void Send(Color[] frame)
        {
            foreach (byte[] packet in Packets(frame, TimeoutSeconds)) _udp.Send(packet, packet.Length);
        }

        internal static List<byte[]> Packets(Color[] frame, byte timeout)
        {
            var packets = new List<byte[]>();
            if (frame.Length <= 490)
            {
                var packet = new byte[2 + frame.Length * 3];
                packet[0] = 2; // DRGB
                packet[1] = timeout;
                for (int i = 0; i < frame.Length; i++) { packet[2 + i * 3] = frame[i].R; packet[3 + i * 3] = frame[i].G; packet[4 + i * 3] = frame[i].B; }
                packets.Add(packet);
                return packets;
            }
            for (int start = 0; start < frame.Length; start += 489)
            {
                int count = Math.Min(489, frame.Length - start);
                var packet = new byte[4 + count * 3];
                packet[0] = 4; // DNRGB
                packet[1] = timeout;
                packet[2] = (byte)(start >> 8);
                packet[3] = (byte)(start & 0xFF);
                for (int i = 0; i < count; i++) { Color c = frame[start + i]; packet[4 + i * 3] = c.R; packet[5 + i * 3] = c.G; packet[6 + i * 3] = c.B; }
                packets.Add(packet);
            }
            return packets;
        }

        /// <summary>Asks a WLED controller its name and LED count (its /json/info page). Null when it doesn't answer.</summary>
        public static Tuple<string, int> Info(string address, int timeoutMilliseconds = 2500)
        {
            try
            {
                var request = (HttpWebRequest)WebRequest.Create("http://" + address.Trim() + "/json/info");
                request.Timeout = timeoutMilliseconds;
                request.ReadWriteTimeout = timeoutMilliseconds;
                using (var response = request.GetResponse())
                using (var reader = new System.IO.StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    var info = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(reader.ReadToEnd());
                    string name = info.TryGetValue("name", out object n) ? n as string : null;
                    int count = 0;
                    if (info.TryGetValue("leds", out object leds) && leds is Dictionary<string, object> l && l.TryGetValue("count", out object c))
                        count = Convert.ToInt32(c);
                    return Tuple.Create(string.IsNullOrWhiteSpace(name) ? "WLED" : name, count);
                }
            }
            catch { return null; }
        }

        public void Dispose() => _udp.Dispose();
    }

    /// <summary>
    /// Keeps one extra device connected: opens it in the background (an Arduino takes a
    /// second or two to start, and the light loop must never wait for that), retries every
    /// five seconds while it's missing, sends only frames that changed (plus a keep-alive
    /// where the device needs one), and reopens when its settings change.
    /// </summary>
    internal sealed class LedDeviceLink : IDisposable
    {
        private ILedOutput _output;
        private System.Threading.Tasks.Task<ILedOutput> _opening;
        private string _signature;
        private bool _batched;
        private DateTime _nextAttemptUtc = DateTime.MinValue, _lastSendUtc = DateTime.MinValue;
        private Color[] _lastFrame;

        public LedDeviceSettings Device { get; private set; }

        public void Apply(LedDeviceSettings device, bool batched)
        {
            string signature = device.Kind + "|" + device.Connection + "|" + device.Baud + "|" + device.LedCount + "|" + device.Enabled + "|" + batched;
            Device = device;
            if (signature == _signature) return;
            _signature = signature;
            _batched = batched;
            Close();
            _nextAttemptUtc = DateTime.MinValue;
        }

        public void Send(Color[] frame)
        {
            if (Device == null || !Device.Enabled || string.IsNullOrWhiteSpace(Device.Connection)) return;
            if (_output == null)
            {
                if (_opening != null)
                {
                    if (!_opening.IsCompleted) return;
                    if (_opening.Status == System.Threading.Tasks.TaskStatus.RanToCompletion) _output = _opening.Result;
                    else Console.Error.WriteLine(Device.Name + " unavailable on " + Device.Connection + ": " + _opening.Exception?.GetBaseException().Message);
                    _opening = null;
                    if (_output == null) return;
                }
                else
                {
                    if (DateTime.UtcNow < _nextAttemptUtc) return;
                    _nextAttemptUtc = DateTime.UtcNow.AddSeconds(5);
                    LedDeviceSettings device = Device;
                    bool batched = _batched;
                    _opening = System.Threading.Tasks.Task.Run(() => Open(device, batched));
                    return;
                }
            }
            bool keepAlive = _output.KeepAliveMilliseconds > 0 && (DateTime.UtcNow - _lastSendUtc).TotalMilliseconds >= _output.KeepAliveMilliseconds;
            if (!keepAlive && LedFrame.SameFrame(frame, _lastFrame)) return;
            try
            {
                _output.Send(frame);
                _lastFrame = frame;
                _lastSendUtc = DateTime.UtcNow;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(Device.Name + " lost: " + exception.Message);
                Close();
            }
        }

        public static ILedOutput Open(LedDeviceSettings device, bool batched)
        {
            switch (device.Kind)
            {
                case LedDeviceKinds.Adalight: return new AdalightOutput(device);
                case LedDeviceKinds.Wled: return new WledOutput(device);
                default: return new ArduinoOutput(device, batched);
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

        public void Dispose() => Close();
    }

    /// <summary>Turns the lights of the moment into a frame for a device's role.</summary>
    internal static class LedRoleMap
    {
        /// <param name="dv5">The 16 DV-5 LEDs: 0-3 up the left, 4-11 the shift row, 12-15 down the right.</param>
        /// <param name="bar">The LB-10 light bar, left to right.</param>
        public static Color[] Frame(LedDeviceSettings device, Color[] dv5, Color[] bar, RacingFlag flag, int now)
        {
            int n = device.LedCount;
            Color[] frame;
            switch (device.Role)
            {
                case LedRoles.Shift:
                    frame = Stretch(dv5 != null && dv5.Length >= 12 ? dv5.Skip(4).Take(8).ToArray() : null, n);
                    break;
                case LedRoles.Flags:
                    bool blink = ((now / 250) & 1) == 0;
                    frame = new Color[n];
                    if (flag != RacingFlag.None) for (int i = 0; i < n; i++) frame[i] = LightEffects.FlagColor(flag, i, blink, true);
                    break;
                default:
                    frame = Stretch(bar, n);
                    break;
            }
            if (device.Reverse) Array.Reverse(frame);
            if (device.Brightness < 100)
            {
                double scale = device.Brightness / 100.0;
                for (int i = 0; i < n; i++) frame[i] = Color.FromArgb((int)(frame[i].R * scale), (int)(frame[i].G * scale), (int)(frame[i].B * scale));
            }
            return frame;
        }

        /// <summary>Spreads a strip over n LEDs: each LED takes the colour of the source LED at the same position along the strip.</summary>
        internal static Color[] Stretch(Color[] source, int n)
        {
            var result = new Color[n];
            if (source == null || source.Length == 0) return result;
            for (int i = 0; i < n; i++)
            {
                int from = (int)((i + 0.5) * source.Length / n);
                result[i] = source[Math.Min(source.Length - 1, from)];
            }
            return result;
        }
    }

    /// <summary>
    /// Looks for LED boards on the computer's serial ports, only when asked. Each port is
    /// opened, given a moment to announce itself as Adalight ("Ada"), then asked its name
    /// on the common sim-racing protocol. Bluetooth serial ports are skipped: opening one
    /// can hang for many seconds.
    /// </summary>
    internal static class LedDeviceScanner
    {
        public static List<LedDeviceSettings> Scan(IEnumerable<string> skipPorts, Action<string> progress)
        {
            var skip = new HashSet<string>(skipPorts.Where(p => p != null), StringComparer.OrdinalIgnoreCase);
            var found = new List<LedDeviceSettings>();
            foreach (string port in SerialPorts().Where(p => !skip.Contains(p)))
            {
                progress?.Invoke("Checking " + port + "…");
                if (IsAdalight(port))
                {
                    found.Add(new LedDeviceSettings { Kind = LedDeviceKinds.Adalight, Port = port, Name = "Adalight on " + port, Role = LedRoles.Bar });
                    continue;
                }
                string name = ArduinoName(port);
                if (name != null) found.Add(new LedDeviceSettings { Kind = LedDeviceKinds.Arduino, Port = port, Name = name, Role = LedRoles.Bar });
            }
            return found;
        }

        /// <summary>Serial ports that exist right now, minus Bluetooth ones.</summary>
        public static List<string> SerialPorts()
        {
            var ports = SerialPort.GetPortNames().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            try
            {
                using (RegistryKey map = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM"))
                    if (map != null)
                        foreach (string device in map.GetValueNames())
                            if (device.IndexOf("Bth", StringComparison.OrdinalIgnoreCase) >= 0 || device.IndexOf("Bluetooth", StringComparison.OrdinalIgnoreCase) >= 0)
                                ports.RemoveAll(p => string.Equals(p, map.GetValue(device) as string, StringComparison.OrdinalIgnoreCase));
            }
            catch { }
            return ports.OrderBy(p => p.Length).ThenBy(p => p).ToList();
        }

        private static bool IsAdalight(string port)
        {
            try
            {
                using (var serial = new SerialPort(port, 115200) { DtrEnable = true, ReadTimeout = 200 })
                {
                    serial.Open();
                    var text = new StringBuilder();
                    DateTime until = DateTime.UtcNow.AddMilliseconds(2200);
                    while (DateTime.UtcNow < until)
                    {
                        if (serial.BytesToRead > 0) text.Append(serial.ReadExisting());
                        if (text.ToString().Contains("Ada")) return true;
                        Thread.Sleep(50);
                    }
                }
            }
            catch { }
            return false;
        }

        private static string ArduinoName(string port)
        {
            try
            {
                // Only the name question is sent; an unknown device gets nothing else.
                using (var controller = new ArduinoLedController(port, 115200, 1, null) { ClearOnClose = false })
                {
                    controller.Connect();
                    return controller.QueryDeviceName();
                }
            }
            catch { return null; }
        }
    }
}
