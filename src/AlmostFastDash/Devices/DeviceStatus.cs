using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AlmostFastDash.Devices
{
    /// <summary>How an LED device's connection is going, as its row on the Devices page shows it.</summary>
    internal static class LinkStates
    {
        public const string Off = "off", Connecting = "connecting", Connected = "connected", Missing = "missing", Dropping = "dropping", Busy = "busy";
    }

    /// <summary>
    /// Shares each LED device's connection state between processes: whichever is driving the devices
    /// (the dashboard, or the settings window's preview) writes it when a state changes, and the Devices
    /// page reads it while one of them is running. Nothing is written while states hold steady.
    /// </summary>
    internal static class DeviceStatus
    {
        public sealed class Entry
        {
            public string State, Detail;
        }

        private static readonly string FilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "devices.status");
        private static string _written;

        public static void Write(IEnumerable<KeyValuePair<string, LedDeviceLink>> links)
        {
            string text = string.Join(Environment.NewLine, links.Select(l =>
                l.Key + "\t" + l.Value.State + "\t" + (l.Value.Detail ?? "").Replace("\t", " ").Replace("\n", " ").Replace("\r", "")));
            if (text == _written) return;
            try
            {
                File.WriteAllText(FilePath, text);
                _written = text;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        /// <summary>Each device's state by id, as last written; only meaningful while something is driving the devices.</summary>
        public static Dictionary<string, Entry> Read()
        {
            try
            {
                if (!File.Exists(FilePath)) return null;
                var result = new Dictionary<string, Entry>();
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    string[] parts = line.Split('\t');
                    if (parts.Length >= 2) result[parts[0]] = new Entry { State = parts[1], Detail = parts.Length > 2 ? parts[2] : "" };
                }
                return result;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }
    }
}
