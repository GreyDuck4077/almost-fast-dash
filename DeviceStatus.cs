using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AlmostFastOverlays
{
    /// <summary>How an LED device's connection is going, as its row on the Devices page shows it.</summary>
    internal static class LinkStates
    {
        public const string Off = "off", Connecting = "connecting", Connected = "connected", Missing = "missing", Dropping = "dropping";
    }

    /// <summary>
    /// Shares each LED device's connection state between processes: whichever is driving the devices
    /// (the dashboard, or the settings window's preview) writes it about once a second, and the
    /// Devices page reads it. A file that hasn't been written for a few seconds means nobody is.
    /// </summary>
    internal static class DeviceStatus
    {
        public sealed class Entry
        {
            public string State, Detail;
        }

        private static readonly string FilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "devices.status");
        private const double FreshSeconds = 3;

        public static void Write(IEnumerable<KeyValuePair<string, LedDeviceLink>> links)
        {
            try
            {
                File.WriteAllLines(FilePath, links.Select(l => l.Key + "\t" + l.Value.State + "\t" + (l.Value.Detail ?? "").Replace("\t", " ").Replace("\n", " ").Replace("\r", "")));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        /// <summary>Each device's state by id, or null when nothing is driving the devices right now.</summary>
        public static Dictionary<string, Entry> Read()
        {
            try
            {
                var file = new FileInfo(FilePath);
                if (!file.Exists || (DateTime.UtcNow - file.LastWriteTimeUtc).TotalSeconds > FreshSeconds) return null;
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
