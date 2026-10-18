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

namespace AlmostFastDash.Devices
{
    /// <summary>Sends frames to one kind of device.</summary>
    internal interface ILedOutput : IDisposable
    {
        void Send(Color[] frame);
        /// <summary>How often to resend an unchanged frame so the device doesn't time out; 0 for never.</summary>
        int KeepAliveMilliseconds { get; }
        /// <summary>A few words on how it's being driven, for the device's status line.</summary>
        string Detail { get; }
    }

    /// <summary>A board on the common sim-racing LED protocol.</summary>
    internal sealed class ArduinoOutput : ILedOutput
    {
        private readonly ArduinoLedController _controller;

        public ArduinoOutput(DeviceSettings device)
        {
            _controller = new ArduinoLedController(device.ResolvePort(), device.Baud, device.LedCount, null);
            try
            {
                _controller.Connect();
                Name = _controller.QueryDeviceName();
                // Fast updates (several color runs a packet) only for a board that accepts them.
                _controller.Batched = _controller.AcceptsBatches();
            }
            catch
            {
                _controller.Dispose();
                throw;
            }
        }

        public string Name { get; }
        public int KeepAliveMilliseconds => 0;
        public string Detail => _controller.Batched ? "fast updates" : "standard updates";
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
        public string Detail => "Adalight";

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
        public string Detail => "WLED over the network";

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
    /// Sends a device's frames on its own thread, always the newest one. A board that takes a while
    /// per frame (many color runs without fast updates) then shows the latest frame as soon as it
    /// can, instead of slowing the light loop and every other device with it. A failed send is
    /// reported on the next Send, so the caller can reconnect.
    /// </summary>
    internal sealed class BackgroundOutput : ILedOutput
    {
        private readonly ILedOutput _inner;
        private readonly Thread _thread;
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private Color[] _pending;
        private volatile Exception _error;
        private volatile bool _stop;

        public BackgroundOutput(ILedOutput inner, string name)
        {
            _inner = inner;
            _thread = new Thread(Run) { IsBackground = true, Name = "LEDs: " + name };
            _thread.Start();
        }

        public int KeepAliveMilliseconds => _inner.KeepAliveMilliseconds;
        public string Detail => _inner.Detail;

        public void Send(Color[] frame)
        {
            Exception error = _error;
            if (error != null) throw error;
            Volatile.Write(ref _pending, frame);
            _wake.Set();
        }

        private void Run()
        {
            while (!_stop)
            {
                _wake.WaitOne(500);
                Color[] frame = Interlocked.Exchange(ref _pending, null);
                if (frame == null || _stop) continue;
                try { _inner.Send(frame); }
                catch (Exception exception) { _error = exception; return; }
            }
        }

        public void Dispose()
        {
            _stop = true;
            _wake.Set();
            _thread.Join(3000);
            _inner.Dispose();
            _wake.Dispose();
        }
    }
}
