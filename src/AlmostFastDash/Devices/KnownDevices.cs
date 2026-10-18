using System;
using System.Collections.Generic;
using System.Linq;

namespace AlmostFastDash.Devices
{
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
        public static DeviceSettings Create(Profile profile, string port, int ledCount, bool enabled)
        {
            var device = new DeviceSettings
            {
                Id = "device-" + profile.Name.ToLowerInvariant().Replace(" ", "-"), Name = profile.Name, DetectedName = profile.Match,
                Kind = LedDeviceKinds.Arduino, Port = port, UsbId = profile.UsbId, LedCount = ledCount, Enabled = enabled,
                Groups = profile.Groups(ledCount)
            };
            device.Normalize();
            return device;
        }
    }
}
