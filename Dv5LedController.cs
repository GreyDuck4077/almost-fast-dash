using System;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Drawing;

namespace DV5Direct
{
    internal sealed class Dv5LedController : IDisposable
    {
        private const byte MessageHeader = 0x03;
        private readonly SerialPort _port;
        private readonly int _ledCount;

        public Dv5LedController(string portName, int baudRate, int ledCount)
        {
            _ledCount = ledCount;
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
            if (!text.Contains("SSG DV 5"))
            {
                throw new InvalidOperationException("COM7 did not identify itself as SSG DV 5. Response: " + text);
            }

            return text.Trim();
        }

        public void RunStartupAnimation(CancellationToken cancellationToken)
        {
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

        public void Clear() => SetAll(0, 0, 0);

        public void SetIndicators(
            BlindSpotZone left,
            BlindSpotZone right,
            int rpmPairs,
            bool shiftFlashOn,
            DashboardSettings settings)
        {
            Clear();
            settings = settings ?? DashboardSettings.CreateDefault();
            SetSide(left, true, rpmPairs, settings.LeftLeds);
            SetSide(right, false, rpmPairs, settings.RightLeds);
            SetRpm(rpmPairs, shiftFlashOn, settings.CenterLeds);
        }

        private void SetRpm(int pairs, bool shiftFlashOn, LedBankSettings settings)
        {
            int brightness = Clamp(settings.Brightness, 0, 100);
            if (shiftFlashOn)
            {
                SetRange(4, 8, Scale(255, brightness), 0, 0);
                return;
            }

            int count = Math.Max(0, Math.Min(4, pairs));
            Color[] colors = GetProfile(settings);
            for (int pair = 0; pair < count; pair++)
            {
                Color color = colors[Math.Min(2, pair * 3 / 4)];
                byte red = Scale(color.R, brightness), green = Scale(color.G, brightness), blue = Scale(color.B, brightness);
                switch (settings.Pattern)
                {
                    case "Left to right": SetRange(4 + pair * 2, Math.Min(2, 8 - pair * 2), red, green, blue); break;
                    case "Right to left": SetRange(10 - pair * 2, 2, red, green, blue); break;
                    case "Center outward":
                        SetRange(7 - pair, 1, red, green, blue);
                        SetRange(8 + pair, 1, red, green, blue);
                        break;
                    default:
                        SetRange(4 + pair, 1, red, green, blue);
                        SetRange(11 - pair, 1, red, green, blue);
                        break;
                }
            }
        }

        private void SetSide(BlindSpotZone zone, bool left, int rpmPairs, SideLedSettings settings)
        {
            // Confirmed physical chain: indices 0-3 run up the left side,
            // 4-11 span the upper shift-light row, and 12-15 run down the
            // right side.
            int top = left ? 3 : 12;
            int upperMiddle = left ? 2 : 13;
            int lowerMiddle = left ? 1 : 14;
            int bottom = left ? 0 : 15;
            if (settings == null || settings.Function == "Disabled") return;
            Color color = GetProfile(settings)[2];
            int brightness = Clamp(settings.Brightness, 0, 100);
            byte red = Scale(color.R, brightness), green = Scale(color.G, brightness), blue = Scale(color.B, brightness);

            if (settings.Function == "RPM")
            {
                int count = Math.Max(0, Math.Min(4, rpmPairs));
                if (count > 0) SetRange(left ? 4 - count : 12, count, red, green, blue);
                return;
            }
            if (settings.Function != "Blind spot") return;

            switch (zone)
            {
                case BlindSpotZone.Front:
                    SetRange(top, 1, red, green, blue);
                    break;
                case BlindSpotZone.Beside:
                    SetRange(Math.Min(upperMiddle, lowerMiddle), 2, red, green, blue);
                    break;
                case BlindSpotZone.Rear:
                    SetRange(Math.Min(lowerMiddle, bottom), 2, red, green, blue);
                    break;
            }
        }

        private static Color[] GetProfile(LedBankSettings settings)
        {
            switch (settings.ColorProfile)
            {
                case "Blue / Purple / Red":
                    return new[] { Color.DeepSkyBlue, Color.MediumPurple, Color.Red };
                case "Single color":
                case "Red":
                    Color single = SettingsStore.ParseColor(settings.HighColor, Color.Red);
                    return new[] { single, single, single };
                default:
                    return new[]
                    {
                        SettingsStore.ParseColor(settings.LowColor, Color.LimeGreen),
                        SettingsStore.ParseColor(settings.MidColor, Color.Gold),
                        SettingsStore.ParseColor(settings.HighColor, Color.Red)
                    };
            }
        }

        private static byte Scale(int channel, int brightness) =>
            (byte)Math.Max(0, Math.Min(255, channel * brightness / 100));

        private static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));

        private void SetAll(byte red, byte green, byte blue) =>
            SetRange(0, _ledCount, red, green, blue);

        private void SetRange(int start, int count, byte red, byte green, byte blue)
        {
            // SimHub firmware command 6, repeated-color mode 3, terminated by mode 0.
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

        public void Dispose()
        {
            if (_port.IsOpen)
            {
                try { Clear(); } catch { }
                _port.Close();
            }
            _port.Dispose();
        }
    }
}
