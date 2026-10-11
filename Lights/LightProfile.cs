using System;
using System.Collections.Generic;
using System.Linq;

namespace AlmostFastOverlays
{
    /// <summary>
    /// The lights for a dashboard, as data: a stack of layers where the top of the
    /// list wins. Stored in <see cref="DashboardSettings.Lights"/>; when that is null
    /// the dashboard keeps the classic light settings.
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
            if (LedTargets.Find(Target) == null) Target = "shift";
            if (LightEffects.Find(Effect) == null) Effect = "solid";
            Brightness = Math.Max(0, Math.Min(100, Brightness));
        }
    }

    /// <summary>One physical LED: device 0 is the DV-5 (16 LEDs), device 1 the LB-10 brow bar.</summary>
    internal struct LedRef
    {
        public readonly int Device, Index;
        public LedRef(int device, int index) { Device = device; Index = index; }
        public static LedRef Dv5(int index) => new LedRef(0, index);
        public static LedRef Brow(int index) => new LedRef(1, index);
    }

    internal sealed class LedTarget
    {
        public string Id, Name, Description;
        /// <summary>Where "front" sits along each strip, for blind-spot effects: true when it is the end.</summary>
        public bool FrontAtEnd = true;
        /// <summary>The strips the target covers; mirrored targets have two strips filled alike.</summary>
        public Func<int, List<LedRef[]>> Strips;
    }

    /// <summary>
    /// Named groups of LEDs. DV-5 chain: 0-3 run up the left side, 4-11 the shift
    /// row left to right, 12-15 down the right side. The brow bar runs left to right.
    /// Strips are ordered the way fills grow: sides bottom to top, the brow halves
    /// from the middle outward.
    /// </summary>
    internal static class LedTargets
    {
        private static LedRef[] Dv5(params int[] indexes) => indexes.Select(LedRef.Dv5).ToArray();

        public static readonly List<LedTarget> All = new List<LedTarget>
        {
            new LedTarget { Id = "shift", Name = "Shift lights", Description = "The 8 LEDs across the top of the DV-5, left to right.", Strips = n => new List<LedRef[]> { Dv5(4, 5, 6, 7, 8, 9, 10, 11) } },
            new LedTarget { Id = "left", Name = "Left side", Description = "The 4 LEDs down the DV-5's left side, bottom to top.", Strips = n => new List<LedRef[]> { Dv5(0, 1, 2, 3) } },
            new LedTarget { Id = "right", Name = "Right side", Description = "The 4 LEDs down the DV-5's right side, bottom to top.", Strips = n => new List<LedRef[]> { Dv5(15, 14, 13, 12) } },
            new LedTarget { Id = "sides", Name = "Both sides", Description = "Both DV-5 side columns, filled alike, bottom to top.", Strips = n => new List<LedRef[]> { Dv5(0, 1, 2, 3), Dv5(15, 14, 13, 12) } },
            new LedTarget { Id = "dv5", Name = "Whole DV-5", Description = "All 16 DV-5 LEDs as one horseshoe: up the left, across, down the right.", Strips = n => new List<LedRef[]> { Dv5(Enumerable.Range(0, 16).ToArray()) } },
            new LedTarget { Id = "brow", Name = "Light bar", Description = "The whole LB-10 brow bar, left to right.", Strips = n => new List<LedRef[]> { Enumerable.Range(0, n).Select(LedRef.Brow).ToArray() } },
            new LedTarget { Id = "brow-mirror", Name = "Light bar, mirrored", Description = "Both halves of the brow bar, each from the middle outward.", FrontAtEnd = false,
                Strips = n => new List<LedRef[]> { BrowHalf(n, true), BrowHalf(n, false) } },
            new LedTarget { Id = "brow-left", Name = "Light bar, left half", Description = "The left half of the brow bar, from the middle outward.", FrontAtEnd = false, Strips = n => new List<LedRef[]> { BrowHalf(n, true) } },
            new LedTarget { Id = "brow-right", Name = "Light bar, right half", Description = "The right half of the brow bar, from the middle outward.", FrontAtEnd = false, Strips = n => new List<LedRef[]> { BrowHalf(n, false) } },
            new LedTarget { Id = "all", Name = "Everything", Description = "Every LED on the DV-5 and the brow bar.",
                Strips = n => new List<LedRef[]> { Dv5(Enumerable.Range(0, 16).ToArray()), Enumerable.Range(0, n).Select(LedRef.Brow).ToArray() } },
        };

        private static LedRef[] BrowHalf(int count, bool left)
        {
            int half = count / 2;
            return left
                ? Enumerable.Range(0, half).Reverse().Select(LedRef.Brow).ToArray()
                : Enumerable.Range(count - half, half).Select(LedRef.Brow).ToArray();
        }

        public static LedTarget Find(string id) => All.FirstOrDefault(t => t.Id == id);
    }
}
