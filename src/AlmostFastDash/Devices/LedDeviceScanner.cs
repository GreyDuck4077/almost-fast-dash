using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace AlmostFastDash.Devices
{
    /// <summary>
    /// Looks for LED boards on the computer's serial ports, only when asked. Each port is
    /// opened, given a moment to announce itself as Adalight ("Ada"), then asked its name
    /// on the common sim-racing protocol. Bluetooth serial ports are skipped: opening one
    /// can hang for many seconds.
    /// </summary>
    internal static class LedDeviceScanner
    {
        /// <summary>LED boards on serial ports other than <paramref name="skipPorts"/> (ports already in use by set-up devices).</summary>
        public static List<DeviceSettings> Scan(IEnumerable<string> skipPorts, Action<string> progress) => Scan(skipPorts, progress, out _);

        /// <param name="busy">Ports another program has open (other dashboard software), which can't be asked.</param>
        public static List<DeviceSettings> Scan(IEnumerable<string> skipPorts, Action<string> progress, out List<string> busy)
        {
            var skip = new HashSet<string>(skipPorts.Where(p => p != null), StringComparer.OrdinalIgnoreCase);
            var found = new List<DeviceSettings>();
            busy = new List<string>();
            foreach (string port in SerialPorts().Where(p => !skip.Contains(p)))
            {
                progress?.Invoke(port);
                if (InUse(port)) { busy.Add(port); continue; }
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

        /// <summary>True when another program has the port open.</summary>
        private static bool InUse(string port)
        {
            try { using (var serial = new SerialPort(port)) serial.Open(); return false; }
            catch (UnauthorizedAccessException) { return true; }
            catch { return false; }
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
