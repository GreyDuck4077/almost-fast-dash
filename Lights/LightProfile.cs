using System;
using System.Collections.Generic;
using System.Linq;

namespace AlmostFastOverlays
{
    /// <summary>
    /// The lights for a dashboard, as data: a stack of layers where the top of the
    /// list wins, each aimed at LED groups on the set-up devices. Stored in
    /// <see cref="DashboardSettings.Lights"/>; when that is null the dashboard's older
    /// classic light settings are converted to layers as it runs.
    /// </summary>
    internal sealed class LightProfile
    {
        public int Version { get; set; } = 1;
        /// <summary>Master brightness for every LED, 0-100.</summary>
        public int Brightness { get; set; } = 100;
        /// <summary>Perceptual (gamma) brightness, so dim settings and colour mixes look even.</summary>
        public bool Gamma { get; set; } = true;
        /// <summary>How long an LED takes to fade out after it turns off, in milliseconds; 0 snaps off.</summary>
        public int FadeMs { get; set; } = 90;
        /// <summary>Highest priority first.</summary>
        public List<LightLayer> Layers { get; set; } = new List<LightLayer>();

        public void Normalize()
        {
            Brightness = Math.Max(0, Math.Min(100, Brightness));
            FadeMs = Math.Max(0, Math.Min(2000, FadeMs));
            if (Layers == null) Layers = new List<LightLayer>();
            Layers.RemoveAll(l => l == null);
            foreach (LightLayer layer in Layers) layer.Normalize();
        }
    }

    internal sealed class LightLayer
    {
        public string Id { get; set; } = DashScreen.NewId("layer");
        public string Name { get; set; } = "Layer";
        public bool Enabled { get; set; } = true;
        public string Target { get; set; } = "shift";
        public string Effect { get; set; } = "revs";
        /// <summary>A formula that must be true for the layer to show; empty means always.</summary>
        public string Condition { get; set; } = "";
        /// <summary>Over: lit LEDs cover the layers below. Replace: the whole target is taken, unlit LEDs go dark.</summary>
        public string Blend { get; set; } = "Over";
        public int Brightness { get; set; } = 100;
        public Dictionary<string, string> Props { get; set; } = new Dictionary<string, string>();

        public static readonly string[] Blends = { "Over", "Replace" };

        public string Get(string key, string fallback = null) =>
            Props != null && Props.TryGetValue(key, out string value) && value != null ? value : fallback;

        public void Set(string key, string value)
        {
            if (Props == null) Props = new Dictionary<string, string>();
            Props[key] = value;
        }

        public void Normalize()
        {
            if (string.IsNullOrWhiteSpace(Id)) Id = DashScreen.NewId("layer");
            if (string.IsNullOrWhiteSpace(Name)) Name = "Layer";
            if (Props == null) Props = new Dictionary<string, string>();
            if (Condition == null) Condition = "";
            if (!Blends.Contains(Blend)) Blend = "Over";
            if (!LedTargets.IsValidId(Target)) Target = "shift";
            if (LightEffects.Find(Effect) == null) Effect = "solid";
            Brightness = Math.Max(0, Math.Min(100, Brightness));
        }
    }

    /// <summary>One physical LED: the index of its device in the set-up device list, and its number on that device (from 0).</summary>
    internal struct LedRef
    {
        public readonly int Device, Index;
        public LedRef(int device, int index) { Device = device; Index = index; }
    }

    /// <summary>What a light layer can aim at, with the LEDs it covers on the current devices.</summary>
    internal sealed class LedTarget
    {
        public string Id, Name, Description;
        /// <summary>Where "front" sits along each strip, for blind-spot effects: true when it is the end.</summary>
        public bool FrontAtEnd = true;
        /// <summary>The strips the target covers; mirrored targets have two strips filled alike.</summary>
        public List<LedRef[]> Strips = new List<LedRef[]>();
        /// <summary>For each strip, whether it is on the right-hand side (for blind-spot effects set to follow the target).</summary>
        public List<bool> RightStrips = new List<bool>();

        public bool IsRight(int strip) => strip >= 0 && strip < RightStrips.Count && RightStrips[strip];
    }

    /// <summary>
    /// The targets a light layer can aim at, built from the devices that are set up.
    /// Purpose targets ("Shift lights", "Left side", "Light bar"...) light every group
    /// with that purpose on every device, so light profiles work whatever is plugged in;
    /// group and device targets aim at one device.
    /// </summary>
    internal static class LedTargets
    {
        /// <summary>A strip, and whether it is on the right-hand side.</summary>
        private sealed class Part
        {
            public LedRef[] Leds;
            public bool Right;
            public Part(LedRef[] leds, bool right) { Leds = leds; Right = right; }
        }

        private sealed class Purpose
        {
            public string Id, Name, Description;
            public bool FrontAtEnd = true;
            public Func<IReadOnlyList<DeviceSettings>, IEnumerable<Part>> Strips;
        }

        private static readonly List<Purpose> Purposes = new List<Purpose>
        {
            new Purpose { Id = "shift", Name = "Shift lights", Description = "Every group set up as shift lights, in its fill order.", Strips = d => Groups(d, LedPurposes.Shift) },
            new Purpose { Id = "left", Name = "Left side", Description = "Every group set up as a left side, usually bottom to top.", Strips = d => Groups(d, LedPurposes.Left) },
            new Purpose { Id = "right", Name = "Right side", Description = "Every group set up as a right side, usually bottom to top.", Strips = d => Groups(d, LedPurposes.Right) },
            new Purpose { Id = "sides", Name = "Both sides", Description = "The left and right side groups, filled alike.", Strips = d => Groups(d, LedPurposes.Left).Concat(Groups(d, LedPurposes.Right)) },
            new Purpose { Id = "dv5", Name = "Whole screen LEDs", Description = "Every LED on each device with shift lights, in the device's own order (on a DV-5: up the left, across, down the right).",
                Strips = d => Devices(d).Where(x => x.Item2.Groups.Any(g => g.Purpose == LedPurposes.Shift)).Select(x => new Part(All(x.Item1, x.Item2), false)) },
            new Purpose { Id = "brow", Name = "Light bar", Description = "Every group set up as a light bar, left to right.", Strips = d => Groups(d, LedPurposes.Bar) },
            new Purpose { Id = "brow-mirror", Name = "Light bar, mirrored", Description = "Both halves of every light bar, each from the middle outward.", FrontAtEnd = false,
                Strips = d => Groups(d, LedPurposes.Bar).SelectMany(p => new[] { new Part(Half(p.Leds, true), false), new Part(Half(p.Leds, false), true) }) },
            new Purpose { Id = "brow-left", Name = "Light bar, left half", Description = "The left half of every light bar, from the middle outward.", FrontAtEnd = false,
                Strips = d => Groups(d, LedPurposes.Bar).Select(p => new Part(Half(p.Leds, true), false)) },
            new Purpose { Id = "brow-right", Name = "Light bar, right half", Description = "The right half of every light bar, from the middle outward.", FrontAtEnd = false,
                Strips = d => Groups(d, LedPurposes.Bar).Select(p => new Part(Half(p.Leds, false), true)) },
            new Purpose { Id = "all", Name = "Everything", Description = "Every LED on every device.", Strips = d => Devices(d).Select(x => new Part(All(x.Item1, x.Item2), false)) },
        };

        /// <summary>Every target for these devices: the purpose targets, then each device and each of its groups.</summary>
        public static List<LedTarget> For(IReadOnlyList<DeviceSettings> devices)
        {
            devices = devices ?? new List<DeviceSettings>();
            var targets = Purposes.Select(p => Target(p.Id, p.Name, p.Description, p.FrontAtEnd, p.Strips(devices))).ToList();
            foreach (Tuple<int, DeviceSettings> device in Devices(devices))
            {
                DeviceSettings d = device.Item2;
                targets.Add(Target("device:" + d.Id, d.Name + " · all LEDs", "Every LED on " + d.Name + ", in its own order.", true, new[] { new Part(All(device.Item1, d), false) }));
                foreach (LedGroup group in d.Groups)
                    targets.Add(Target("group:" + d.Id + "/" + group.Id, d.Name + " · " + group.Name, "LEDs " + group.From + "-" + group.To + " on " + d.Name + ".", true,
                        new[] { new Part(Strip(device.Item1, group), group.Purpose == LedPurposes.Right) }));
            }
            return targets;
        }

        private static LedTarget Target(string id, string name, string description, bool frontAtEnd, IEnumerable<Part> parts)
        {
            var target = new LedTarget { Id = id, Name = name, Description = description, FrontAtEnd = frontAtEnd };
            foreach (Part part in parts.Where(p => p.Leds.Length > 0))
            {
                target.Strips.Add(part.Leds);
                target.RightStrips.Add(part.Right);
            }
            return target;
        }

        /// <summary>A target by id for these devices, or null when it names a device or group that isn't set up.</summary>
        public static LedTarget Find(string id, IReadOnlyList<DeviceSettings> devices) => For(devices).FirstOrDefault(t => t.Id == id);

        /// <summary>True for an id that names a purpose, or a device or group in the usual form.</summary>
        public static bool IsValidId(string id) =>
            !string.IsNullOrEmpty(id) && (Purposes.Any(p => p.Id == id) || id.StartsWith("device:") || id.StartsWith("group:"));

        /// <summary>Enabled devices with their index in the full list (which is what LedRef.Device means).</summary>
        private static IEnumerable<Tuple<int, DeviceSettings>> Devices(IReadOnlyList<DeviceSettings> devices) =>
            devices.Select((d, i) => Tuple.Create(i, d)).Where(x => x.Item2 != null && x.Item2.Enabled);

        private static IEnumerable<Part> Groups(IReadOnlyList<DeviceSettings> devices, string purpose) =>
            Devices(devices).SelectMany(x => x.Item2.Groups.Where(g => g.Purpose == purpose).Select(g => new Part(Strip(x.Item1, g), purpose == LedPurposes.Right)));

        private static LedRef[] Strip(int device, LedGroup group) => group.Indexes().Select(i => new LedRef(device, i)).ToArray();

        private static LedRef[] All(int device, DeviceSettings d) => Enumerable.Range(0, d.LedCount).Select(i => new LedRef(device, i)).ToArray();

        /// <summary>Half of a strip, from its middle outward.</summary>
        private static LedRef[] Half(LedRef[] strip, bool left)
        {
            int half = strip.Length / 2;
            return left ? strip.Take(half).Reverse().ToArray() : strip.Skip(strip.Length - half).ToArray();
        }
    }
}