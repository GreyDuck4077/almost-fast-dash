using System;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Drawing;

namespace AlmostFastOverlays
{
    /// <summary>
    /// LEDs on an Arduino-based board that speaks the common sim-racing LED serial
    /// protocol: the SSG DV-5 and LB-10, and boards flashed with the same widely used
    /// firmware. Packets are 01 01 FF length payload CRC-8; payload 03 'N' asks the
    /// board its name, 03 '6' sets colour runs.
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
            SendPacket(new[] { MessageHeader, (byte)'N' });
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

        public void RunStartupAnimation(CancellationToken cancellationToken)
        {
            _lastLength = _ledCount;
            for (int pass = 0; pass < 2 && !cancellationToken.IsCancellationRequested; pass++)
            {
                for (int index = 0; index < _ledCount && !cancellationToken.IsCancellationRequested; index++)
                {
                    byte red = pass == 0 ? (byte)0 : (byte)20;
                    byte green = pass == 0 ? (byte)18 : (byte)0;
                    byte blue = pass == 0 ? (byte)35 : (byte)22;
                    SetAll(0, 0, 0);
                    SetRange(index, 1, red, green, blue);
                    Thread.Sleep(45);
                }
            }

            Clear();
        }

        /// <summary>Turns off the LEDs the last frame addressed (none if no frame was sent).</summary>
        public void Clear()
        {
            if (_lastLength > 0) SetRange(0, _lastLength, 0, 0, 0);
            _shown = null;
        }

        /// <summary>
        /// Send a whole frame as one packet: several colour runs before the terminating
        /// mode 0. Far fewer packets and pauses than one packet per run.
        /// </summary>
        public bool Batched { get; set; }

        // One packet per colour run costs a pause each, so a frame of many colours (a gradient) can take
        // a fifth of a second and the LEDs visibly update one after another. Without batching, only the
        // LEDs that changed are sent, with the whole frame resent every second in case a packet was lost.
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
            if (Batched)
            {
                foreach (byte[] payload in BatchedPayloads(frame, count)) SendPacket(payload);
                return;
            }
            bool full = _shown == null || _shown.Length != count || unchecked(Environment.TickCount - _fullFrameTick) >= FullFrameMilliseconds;
            if (full) _fullFrameTick = Environment.TickCount;
            foreach (Tuple<int, int, Color> run in Runs(frame, full ? null : _shown, count))
                SetRange(run.Item1, run.Item2, run.Item3.R, run.Item3.G, run.Item3.B);
            if (_shown == null || _shown.Length != count) _shown = new Color[count];
            Array.Copy(frame, _shown, count);
        }

        /// <summary>
        /// The colour runs (start, count, colour) to send for a frame: every LED, or with <paramref name="shown"> only
        /// the LEDs that differ from it. Neighbouring LEDs of one colour share a run.
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
        /// The command payloads for one frame in batched mode: colour runs as repeated
        /// mode-3 entries ending in mode 0, split only if a packet would exceed its
        /// one-byte length.
        /// </summary>
        internal static System.Collections.Generic.List<byte[]> BatchedPayloads(Color[] frame, int ledCount)
        {
            var payloads = new System.Collections.Generic.List<byte[]>();
            var payload = new System.Collections.Generic.List<byte> { MessageHeader, (byte)'6' };
            int index = 0;
            while (index < ledCount)
            {
                Color color = index < frame.Length ? frame[index] : Color.Black;
                int run = 1;
                while (index + run < ledCount && index + run < frame.Length &&
                       frame[index + run].ToArgb() == color.ToArgb()) run++;
                if (payload.Count + 7 > 250)
                {
                    payload.Add(0);
                    payloads.Add(payload.ToArray());
                    payload = new System.Collections.Generic.List<byte> { MessageHeader, (byte)'6' };
                }
                payload.AddRange(new[] { (byte)3, (byte)index, (byte)run, color.R, color.G, color.B });
                index += run;
            }
            payload.Add(0);
            payloads.Add(payload.ToArray());
            return payloads;
        }

        private void SetAll(byte red, byte green, byte blue) =>
            SetRange(0, _ledCount, red, green, blue);

        private void SetRange(int start, int count, byte red, byte green, byte blue)
        {
            // Firmware command 6, repeated-color mode 3, terminated by mode 0.
            SendPacket(new[]
            {
                MessageHeader, (byte)'6', (byte)3, (byte)start, (byte)count,
                red, green, blue, (byte)0
            });
        }

        private void SendPacket(byte[] payload)
        {
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
            Thread.Sleep(10);
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
