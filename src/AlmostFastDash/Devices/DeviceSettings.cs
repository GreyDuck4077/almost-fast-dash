using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;

namespace AlmostFastDash.Devices
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
    /// A named run of LEDs on a device, in the order fills grow: the LEDs listed in Leds
    /// (made from a device's mapped layout, in physical order), or else LEDs From to To
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
        /// <summary>The LEDs (counting from 1) in fill order, when the group comes from a mapped layout; From, To and Reverse are then only for display.</summary>
        public List<int> Leds { get; set; }

        /// <summary>The LED indexes (from 0) in fill order.</summary>
        public int[] Indexes()
        {
            if (Leds != null && Leds.Count > 0) return Leds.Select(n => n - 1).ToArray();
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
            if (Leds != null)
            {
                Leds = Leds.Where(n => n >= 1 && n <= ledCount).Distinct().ToList();
                if (Leds.Count == 0) Leds = null;
            }
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
        public bool Enabled { get; set; } = true;
        /// <summary>
        /// The most current the LEDs may draw, in milliamps (0 for no limit). Frames that would draw more
        /// are dimmed to fit. A board powered over USB can brown out its hub when many LEDs light white
        /// at once, which drops it (and anything sharing the hub) off USB with its LEDs stuck on.
        /// </summary>
        public int MaxMilliamps { get; set; } = LedPower.DefaultMilliamps;
        /// <summary>The device's physical shape and where each LED sits on it; null for a device set up before layouts.</summary>
        public LedLayout Layout { get; set; }

        /// <summary>True once every LED's place on the device is known.</summary>
        [ScriptIgnore]
        public bool Mapped => Layout != null && Layout.IsMapped;

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
}
