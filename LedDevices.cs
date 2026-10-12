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
    /// <summary>How an LED device is connected and spoken to.</summary>
    internal static class LedDeviceKinds
    {
        /// <summary>An Arduino board speaking the common sim-racing LED serial protocol (the SSG DV-5 and LB-10 among them).</summary>
        public const string Arduino = "Arduino";
        /// <summary>An Arduino running an Adalight sketch: the open "Ada" serial protocol for LED strips.</summary>
        public const string Adalight = "Adalight";
        /// <summary>A WLED controller (usually an ESP32) on the network, driven over UDP.</summary>
        public const string Wled = "WLED";
        public static readonly string[] All = { Arduino, Adalight, Wled };
    }

    /// <summary>
    /// What a group of LEDs is for. Light layers aimed at "Shift lights", "Left side" and
    /// so on light every group with that purpose, on whichever devices are plugged in.
    /// </summary>
    internal static class LedPurposes
    {
        public const string Shift = "Shift lights", Left = "Left side", Right = "Right side", Bar = "Light bar", Other = "Other";
        public static readonly string[] All = { Shift, Left, Right, Bar, Other };

        public static string Describe(string purpose)
        {
            switch (purpose)
            {
                case Shift: return "Rev and shift lights: layers aimed at Shift lights use it.";
                case Left: return "Lights down the left: blind spot and other side layers use it.";
                case Right: return "Lights down the right: blind spot and other side layers use it.";
                case Bar: return "A strip like a light bar: layers aimed at the Light bar use it.";
                default: return "Only layers aimed at this group directly use it.";
            }
        }
    }

    /// <summary>
    /// A named run of LEDs on a device, in the order fills grow: LEDs From to To
    /// (counting from 1), or To down to From when reversed.
    /// </summary>
    internal sealed class LedGroup
    {
        public string Id { get; set; } = "group-" + Guid.NewGuid().ToString("N").Substring(0, 6);
        public string Name { get; set; } = "LEDs";
        public string Purpose { get; set; } = LedPurposes.Bar;
        public int From { get; set; } = 1;
        public int To { get; set; } = 1;
        public bool Reverse { get; set; }

        /// <summary>The LED indexes (from 0) in fill order.</summary>
        public int[] Indexes()
        {
            int low = Math.Min(From, To), high = Math.Max(From, To);
            IEnumerable<int> range = Enumerable.Range(low - 1, high - low + 1);
            return (Reverse ? range.Reverse() : range).ToArray();
        }

        public void Normalize(int ledCount)
        {
            if (string.IsNullOrWhiteSpace(Id)) Id = "group-" + Guid.NewGuid().ToString("N").Substring(0, 6);
            if (string.IsNullOrWhiteSpace(Name)) Name = "LEDs";
            if (!LedPurposes.All.Contains(Purpose)) Purpose = LedPurposes.Other;
            From = Math.Max(1, Math.Min(Math.Max(1, ledCount), From));
            To = Math.Max(1, Math.Min(Math.Max(1, ledCount), To));
            if (To < From) { int swap = From; From = To; To = swap; }
        }

        public static LedGroup Of(string name, string purpose, int from, int to, bool reverse = false) =>
            new LedGroup { Id = "group-" + name.ToLowerInvariant().Replace(" ", "-"), Name = name, Purpose = purpose, From = from, To = to, Reverse = reverse };
    }

    /// <summary>One LED device, as stored in settings and set up in the device wizard.</summary>
    internal sealed class DeviceSettings
    {
        public string Id { get; set; } = "device-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        /// <summary>The name the user gave it.</summary>
        public string Name { get; set; } = "LED device";
        /// <summary>What the device called itself when it was found.</summary>
        public string DetectedName { get; set; }
        public string Kind { get; set; } = LedDeviceKinds.Arduino;
        /// <summary>COM port, for Arduino and Adalight devices.</summary>
        public string Port { get; set; }
        /// <summary>USB hardware ID (such as VID_9999&amp;PID_09C7&amp;MI_00), so the device is found again when Windows renumbers ports.</summary>
        public string UsbId { get; set; }
        /// <summary>IP address or host name, for WLED.</summary>
        public string Address { get; set; }
        public int Baud { get; set; } = 115200;
        public int LedCount { get; set; } = 8;
        public List<LedGroup> Groups { get; set; } = new List<LedGroup>();
        /// <summary>Send each frame in one message (Arduino boards that support it).</summary>
        public bool FastUpdates { get; set; }
        public bool Enabled { get; set; } = true;
        /// <summary>
        /// The most current the LEDs may draw, in milliamps (0 for no limit). Frames that would draw more
        /// are dimmed to fit. A board powered over USB can brown out its hub when many LEDs light white
        /// at once, which drops it (and anything sharing the hub) off USB with its LEDs stuck on.
        /// </summary>
        public int MaxMilliamps { get; set; } = LedPower.DefaultMilliamps;

        public void Normalize()
        {
            if (string.IsNullOrWhiteSpace(Id)) Id = "device-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            if (!LedDeviceKinds.All.Contains(Kind)) Kind = LedDeviceKinds.Arduino;
            if (string.IsNullOrWhiteSpace(Name)) Name = DetectedName ?? Kind + " LEDs";
            LedCount = Math.Max(1, Math.Min(Kind == LedDeviceKinds.Wled ? 1500 : ArduinoLedController.MaxLeds, LedCount));
            if (Baud <= 0) Baud = 115200;
            if (Groups == null) Groups = new List<LedGroup>();
            Groups.RemoveAll(g => g == null);
            foreach (LedGroup group in Groups) group.Normalize(LedCount);
        }

        [ScriptIgnore]
        public string Connection => Kind == LedDeviceKinds.Wled ? Address : Port;

        /// <summary>The port to open: where the device's USB ID is now, else the saved port.</summary>
        public string ResolvePort() => string.IsNullOrEmpty(UsbId) ? Port : DevicePorts.Resolve(Port, UsbId);
    }

    /// <summary>
    /// Devices the app knows by USB ID or by the name they report, so setup can fill in
    /// the LED count and groups. Anything else is set up from scratch.
    /// </summary>
    internal static class KnownDevices
    {
        internal sealed class Profile
        {
            public string Name, Match, UsbId;
            public int LedCount;
            public Func<int, List<LedGroup>> Groups;
        }

        public static readonly Profile Dv5 = new Profile
        {
            Name = "DV-5", Match = "SSG DV 5", UsbId = DevicePorts.Dv5, LedCount = 16,
            // The DV-5's chain runs up the left side, across the top, then down the right.
            Groups = n => new List<LedGroup>
            {
                LedGroup.Of("Left side", LedPurposes.Left, 1, 4),
                LedGroup.Of("Shift lights", LedPurposes.Shift, 5, 12),
                LedGroup.Of("Right side", LedPurposes.Right, 13, 16, true)
            }
        };

        public static readonly Profile Lb10 = new Profile
        {
            Name = "LB-10", Match = "SSG LB-10", UsbId = DevicePorts.LightBar, LedCount = 10,
            Groups = n => new List<LedGroup> { LedGroup.Of("Light bar", LedPurposes.Bar, 1, n) }
        };

        public static readonly Profile[] All = { Dv5, Lb10 };

        public static Profile Find(string usbId, string detectedName) =>
            All.FirstOrDefault(p => (!string.IsNullOrEmpty(usbId) && string.Equals(p.UsbId, usbId, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrEmpty(detectedName) && detectedName.IndexOf(p.Match, StringComparison.OrdinalIgnoreCase) >= 0));

        /// <summary>A device built from a profile, as the old fixed DV-5 and LB-10 settings become.</summary>
        public static DeviceSettings Create(Profile profile, string port, int ledCount, bool enabled, bool fast)
        {
            var device = new DeviceSettings
            {
                Id = "device-" + profile.Name.ToLowerInvariant().Replace(" ", "-"), Name = profile.Name, DetectedName = profile.Match,
                Kind = LedDeviceKinds.Arduino, Port = port, UsbId = profile.UsbId, LedCount = ledCount, Enabled = enabled, FastUpdates = fast,
                Groups = profile.Groups(ledCount)
            };
            device.Normalize();
            return device;
        }
    }

    /// <summary>
    /// Estimates the current addressable LEDs draw and dims frames that would draw too much.
    /// A typical RGB LED draws about 20 mA per colour channel at full level, 60 mA white.
    /// </summary>
    internal static class LedPower
    {
        /// <summary>The default limit: plenty for rev lights and coloured alerts, while keeping a
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
        public void Dispose() => _inner.Dispose();
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

        public ArduinoOutput(DeviceSettings device)
        {
            _controller = new ArduinoLedController(device.ResolvePort(), device.Baud, device.LedCount, null) { Batched = device.FastUpdates };
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

        public AdalightOutput(DeviceSettings device)
        {
            _port = new SerialPort(device.ResolvePort(), device.Baud) { DtrEnable = true, WriteTimeout = 500, ReadTimeout = 500 };
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

        public WledOutput(DeviceSettings device)
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
        private DateTime _nextAttemptUtc = DateTime.MinValue, _lastSendUtc = DateTime.MinValue;
        private Color[] _lastFrame;

        public DeviceSettings Device { get; private set; }

        /// <summary>Forgets the last frame so the next one is sent even if it's the same.</summary>
        public void Invalidate() => _lastFrame = null;

        public void Apply(DeviceSettings device)
        {
            string signature = device.Kind + "|" + device.Connection + "|" + device.UsbId + "|" + device.Baud + "|" + device.LedCount + "|" + device.Enabled + "|" + device.FastUpdates + "|" + device.MaxMilliamps;
            Device = device;
            if (signature == _signature) return;
            _signature = signature;
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
                    DeviceSettings device = Device;
                    _opening = System.Threading.Tasks.Task.Run(() => Open(device));
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

    /// <summary>
    /// Looks for LED boards on the computer's serial ports, only when asked. Each port is
    /// opened, given a moment to announce itself as Adalight ("Ada"), then asked its name
    /// on the common sim-racing protocol. Bluetooth serial ports are skipped: opening one
    /// can hang for many seconds.
    /// </summary>
    internal static class LedDeviceScanner
    {
        /// <summary>LED boards on serial ports other than <paramref name="skipPorts"/> (ports already in use by set-up devices).</summary>
        public static List<DeviceSettings> Scan(IEnumerable<string> skipPorts, Action<string> progress)
        {
            var skip = new HashSet<string>(skipPorts.Where(p => p != null), StringComparer.OrdinalIgnoreCase);
            var found = new List<DeviceSettings>();
            foreach (string port in SerialPorts().Where(p => !skip.Contains(p)))
            {
                progress?.Invoke(port);
                string usbId = DevicePorts.HardwareIdForPort(port);
                // Known devices are asked first by name, which is quick and doesn't wait for an Adalight greeting.
                KnownDevices.Profile known = KnownDevices.Find(usbId, null);
                if (known == null && IsAdalight(port))
                {
                    found.Add(new DeviceSettings { Kind = LedDeviceKinds.Adalight, Port = port, UsbId = usbId, DetectedName = "Adalight LED strip", Name = "Adalight LED strip" });
                    continue;
                }
                string name = ArduinoName(port);
                if (name != null) found.Add(new DeviceSettings { Kind = LedDeviceKinds.Arduino, Port = port, UsbId = usbId, DetectedName = name, Name = name });
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
