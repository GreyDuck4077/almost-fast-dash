using System;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Drawing;

namespace AlmostFastOverlays
{
    /// <summary>
    /// LEDs on an Arduino-based board that speaks the common sim-racing LED serial
    /// protocol, the one SimHub's Arduino firmware uses: the SSG DV-5 and LB-10, and boards
    /// flashed with the same widely used firmware. Packets are 01 01 FF length payload CRC-8;
    /// payload 03 'N' asks the board its name, 03 '6' sets color runs. No SimHub code is
    /// used (see CREDITS.md).
    /// </summary>
    internal sealed class ArduinoLedController : IDisposable
    {
        /// <summary>The most LEDs one board can address (start and count are single bytes).</summary>
        public const int MaxLeds = 250;

        private const byte MessageHeader = 0x03;
        private readonly SerialPort _port;
        private readonly int _ledCount;
        private readonly string _expectedName;
        // How many LEDs the last frame addressed; clearing never reaches past it.
        private int _lastLength;

        /// <param name="expectedName">The name the board must report, or null to accept any board.</param>
        public ArduinoLedController(string portName, int baudRate, int ledCount, string expectedName = "SSG DV 5")
        {
            _ledCount = ledCount;
            _expectedName = expectedName;
            _port = new SerialPort(portName, baudRate)
            {
                DtrEnable = true,
                RtsEnable = true,
                ReadTimeout = 1000,
                WriteTimeout = 1000
            };
        }

        public void Connect()
        {
            _port.Open();
            Thread.Sleep(800);
            _port.DiscardInBuffer();
        }

        public string QueryDeviceName()
        {
            SendPacket(new[] { MessageHeader, (byte)'N' }, false);
            Thread.Sleep(150);
            byte[] response = ReadAvailable();
            string text = ExtractProtocolText(response);
            if (_expectedName == null)
            {
                if (text.Trim().Length == 0) throw new InvalidOperationException(_port.PortName + " did not answer as an LED board.");
                return text.Trim();
            }
            if (!text.Contains(_expectedName))
            {
                throw new InvalidOperationException(_port.PortName + " did not identify itself as " + _expectedName + ". Response: " + text);
            }

            return text.Trim();
        }

        /// <summary>
        /// Whether the board takes several color runs in one packet (fast updates): sends two runs that turn
        /// the first two LEDs off and checks the board acknowledges rather than rejects them (04 FF 02).
        /// </summary>
        public bool AcceptsBatches()
        {
            if (_ledCount < 2) return false;
            var runs = new System.Collections.Generic.List<Tuple<int, int, Color>> { Tuple.Create(0, 1, Color.Black), Tuple.Create(1, 1, Color.Black) };
            SendPacket(Payloads(runs, MaxRunsPerPacket)[0], false);
            var reply = new System.Collections.Generic.List<byte>();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (reply.Count < AckLength && watch.ElapsedMilliseconds < 250)
            {
                if (_port.BytesToRead > 0) reply.AddRange(ReadAvailable()); else Thread.Sleep(5);
            }
            if (_port.BytesToRead > 0) _port.DiscardInBuffer();
            return reply.Count >= AckLength && reply[0] == 0x03;
        }

        /// <summary>Turns off the LEDs the last frame addressed (none if no frame was sent).</summary>
        public void Clear()
        {
            if (_lastLength > 0) SetRange(0, _lastLength, 0, 0, 0);
            _shown = null;
        }

        /// <summary>
        /// Send a whole frame as one packet: several color runs before the terminating
        /// mode 0. Far fewer packets and pauses than one packet per run.
        /// </summary>
        public bool Batched { get; set; }

        // Only the LEDs that changed are sent, with the whole frame resent every second in case a packet
        // was lost, so a frame of many colors (a gradient, a sweep) doesn't cost a packet per LED each time.
        private const int FullFrameMilliseconds = 1000;
        private Color[] _shown;
        private int _fullFrameTick;

        /// <remarks>
        /// Only the LEDs in the frame are sent, up to the board's count: writing past a board's
        /// last LED can corrupt its memory and freeze it until it's power-cycled.
        /// </remarks>
        public void SetFrame(Color[] frame)
        {
            int count = Math.Min(frame.Length, _ledCount);
            if (count <= 0) return;
            _lastLength = count;
            bool full = _shown == null || _shown.Length != count || unchecked(Environment.TickCount - _fullFrameTick) >= FullFrameMilliseconds;
            if (full) _fullFrameTick = Environment.TickCount;
            foreach (byte[] payload in Payloads(Runs(frame, full ? null : _shown, count), Batched ? MaxRunsPerPacket : 1))
                SendPacket(payload);
            if (_shown == null || _shown.Length != count) _shown = new Color[count];
            Array.Copy(frame, _shown, count);
        }

        /// <summary>
        /// The color runs (start, count, color) to send for a frame: every LED, or with <paramref name="shown"> only
        /// the LEDs that differ from it. Neighboring LEDs of one color share a run.
        /// </summary>
        internal static System.Collections.Generic.List<Tuple<int, int, Color>> Runs(Color[] frame, Color[] shown, int count)
        {
            var runs = new System.Collections.Generic.List<Tuple<int, int, Color>>();
            int index = 0;
            while (index < count)
            {
                Color color = frame[index];
                if (shown != null && shown[index].ToArgb() == color.ToArgb()) { index++; continue; }
                int run = 1;
                while (index + run < count && frame[index + run].ToArgb() == color.ToArgb() &&
                       (shown == null || shown[index + run].ToArgb() != color.ToArgb())) run++;
                runs.Add(Tuple.Create(index, run, color));
                index += run;
            }
            return runs;
        }

        /// <summary>
        /// The most color runs in one packet with fast updates. Boards on this protocol have a small receive
        /// buffer: a DV-5 takes packets up to 26 bytes (3 runs), cuts off a 32-byte one and rejects anything
        /// bigger, and a steady stream of oversized packets crashes its LED controller until it's power-cycled.
        /// </summary>
        public const int MaxRunsPerPacket = 3;

        /// <summary>
        /// The command payloads for some color runs: each "6" command carries up to <paramref name="runsPerPacket"/>
        /// mode-3 entries (start, count, color) and ends with mode 0.
        /// </summary>
        internal static System.Collections.Generic.List<byte[]> Payloads(System.Collections.Generic.IList<Tuple<int, int, Color>> runs, int runsPerPacket)
        {
            var payloads = new System.Collections.Generic.List<byte[]>();
            for (int first = 0; first < runs.Count; first += runsPerPacket)
            {
                var payload = new System.Collections.Generic.List<byte> { MessageHeader, (byte)'6' };
                foreach (Tuple<int, int, Color> run in runs.Skip(first).Take(runsPerPacket))
                    payload.AddRange(new[] { (byte)3, (byte)run.Item1, (byte)run.Item2, run.Item3.R, run.Item3.G, run.Item3.B });
                payload.Add(0);
                payloads.Add(payload.ToArray());
            }
            return payloads;
        }

        private void SetRange(int start, int count, byte red, byte green, byte blue)
        {
            // Firmware command 6, repeated-color mode 3, terminated by mode 0.
            SendPacket(new[]
            {
                MessageHeader, (byte)'6', (byte)3, (byte)start, (byte)count,
                red, green, blue, (byte)0
            });
        }

        // Boards on this protocol acknowledge each packet (4 bytes) once they've handled it. Sending the next
        // packet before that, at a fixed pace, overruns a busy board: the DV-5's LED controller crashes
        // under a steady stream of fast-changing frames. So each packet waits for its acknowledgement
        // (up to AckMilliseconds); a board that never acknowledges is paced by a short pause instead.
        private const int AckMilliseconds = 120, AckLength = 4;
        private bool? _acknowledges;
        private readonly byte[] _ack = new byte[64];

        /// <summary>Packets sent, and how many went unacknowledged; and the slowest acknowledgement, for testing boards.</summary>
        public int Sent, Unacknowledged;
        public double SlowestAckMilliseconds;

        private void SendPacket(byte[] payload, bool waitForAck = true)
        {
            // The board answers every packet (4 bytes). Left unread, the answers fill the port's
            // buffer within seconds of fast-changing lights, the board stalls trying to send more
            // and its LEDs freeze until it's power-cycled. So they're thrown away before each packet;
            // a query reads its answer right after sending, before the next packet.
            if (_port.BytesToRead > 0) _port.DiscardInBuffer();
            var core = new byte[payload.Length + 2];
            core[0] = 0xff; // Reset/always-accepted packet ID.
            core[1] = (byte)payload.Length;
            Buffer.BlockCopy(payload, 0, core, 2, payload.Length);

            var packet = new byte[core.Length + 3];
            packet[0] = 0x01;
            packet[1] = 0x01;
            Buffer.BlockCopy(core, 0, packet, 2, core.Length);
            packet[packet.Length - 1] = ComputeCrc8(core);
            _port.Write(packet, 0, packet.Length);
            Sent++;
            if (!waitForAck) return;
            if (_acknowledges == false) { Thread.Sleep(10); return; }
            bool acked = WaitForAck();
            if (!acked) Unacknowledged++;
            // The first packets decide: a board that answers none of them doesn't acknowledge at all.
            if (acked) _acknowledges = true;
            else if (_acknowledges == null && Sent >= 3 && Unacknowledged >= 3) _acknowledges = false;
        }

        private bool WaitForAck()
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            int got = 0;
            _port.ReadTimeout = AckMilliseconds;
            try
            {
                while (got < AckLength)
                {
                    int read = _port.Read(_ack, 0, Math.Min(_ack.Length, Math.Max(AckLength - got, _port.BytesToRead)));
                    got += read;
                    if (watch.ElapsedMilliseconds > AckMilliseconds) break;
                }
            }
            catch (TimeoutException) { }
            finally { _port.ReadTimeout = 1000; }
            SlowestAckMilliseconds = Math.Max(SlowestAckMilliseconds, watch.Elapsed.TotalMilliseconds);
            return got >= AckLength;
        }

        private byte[] ReadAvailable()
        {
            var result = new byte[_port.BytesToRead];
            int offset = 0;
            while (offset < result.Length)
            {
                offset += _port.Read(result, offset, result.Length - offset);
            }
            return result;
        }

        private static string ExtractProtocolText(byte[] response)
        {
            var builder = new StringBuilder();
            for (int i = 0; i + 1 < response.Length; i++)
            {
                if (response[i] == 0x06)
                {
                    int length = response[i + 1];
                    if (i + 2 + length <= response.Length)
                    {
                        builder.Append(Encoding.ASCII.GetString(response, i + 2, length));
                        i += length + 1;
                    }
                }
            }
            return builder.ToString();
        }

        private static byte ComputeCrc8(byte[] bytes)
        {
            byte crc = 0;
            foreach (byte value in bytes)
            {
                crc ^= value;
                for (int bit = 0; bit < 8; bit++)
                {
                    crc = (byte)((crc & 0x80) != 0 ? (crc << 1) ^ 0xd5 : crc << 1);
                }
            }
            return crc;
        }

        /// <summary>Turn the LEDs off when closing. Off while probing a port that may not be an LED board.</summary>
        public bool ClearOnClose { get; set; } = true;

        public void Dispose()
        {
            if (_port.IsOpen)
            {
                if (ClearOnClose) try { Clear(); } catch { }
                _port.Close();
            }
            _port.Dispose();
        }
    }
}
