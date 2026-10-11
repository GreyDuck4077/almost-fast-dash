using System;
using System.IO.Ports;
using System.Linq;
using Microsoft.Win32;

namespace AlmostFastOverlays
{
    /// <summary>
    /// Finds SSG devices by USB hardware ID. Windows can renumber COM ports
    /// after a restart or a replug, so the ID is more reliable than a saved name.
    /// </summary>
    internal static class DevicePorts
    {
        public const string Dv5 = @"VID_9999&PID_09C7&MI_00";
        public const string LightBar = @"VID_9999&PID_0B5C&MI_00";

        /// <summary>The COM port of a connected device, or null when it isn't plugged in.</summary>
        public static string Find(string hardwareId)
        {
            try
            {
                string[] present = SerialPort.GetPortNames();
                using (RegistryKey device = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB\" + hardwareId))
                {
                    if (device == null) return null;
                    // Windows keeps entries for every instance it has seen; only a
                    // port that exists right now belongs to the connected device.
                    foreach (string instance in device.GetSubKeyNames())
                        using (RegistryKey parameters = device.OpenSubKey(instance + @"\Device Parameters"))
                        {
                            string port = parameters?.GetValue("PortName") as string;
                            if (port != null && present.Contains(port, StringComparer.OrdinalIgnoreCase)) return port;
                        }
                }
            }
            catch { }
            return null;
        }

        /// <summary>Prefers the detected port and falls back to the saved one.</summary>
        public static string Resolve(string configured, string hardwareId) => Find(hardwareId) ?? configured;
    }
}
