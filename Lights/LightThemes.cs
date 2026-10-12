using System.Collections.Generic;
using System.Linq;

namespace AlmostFastOverlays
{
    /// <summary>A named set of three LED colours: low, middle and high (the good, warn and bad colour names).</summary>
    internal sealed class LightTheme
    {
        public string Id, Name, Low, Mid, High;
    }

    /// <summary>
    /// Prebuilt LED colour themes. A light profile's theme sets the colours its layers call good, warn and
    /// bad: the low, middle and high of rev lights and meters, a shift flash, a blind-spot warning. Layers
    /// with a colour of their own (a hex value, or accent, info…) keep it.
    /// </summary>
    internal static class LightThemes
    {
        public const string CustomId = "custom", DefaultId = "racing";

        public static readonly List<LightTheme> All = new List<LightTheme>
        {
            T("racing", "Racing", "#008000", "#FFBE2E", "#890711"),
            T("classic", "Classic", "#00FF00", "#FFFF00", "#FF0000"),
            T("arctic", "Arctic", "#00FFFF", "#FFFFFF", "#0040FF"),
            T("sunset", "Sunset", "#FFD000", "#FF7A00", "#FF1A00"),
            T("octane", "Octane", "#FFB000", "#FFFFFF", "#C00000"),
            T("tricolore", "Tricolore", "#009246", "#FFFFFF", "#CE2B37"),
            T("gulf", "Gulf", "#6CC5E9", "#F47920", "#6CC5E9"),
            T("tokyo", "Tokyo", "#00E5FF", "#3A00FF", "#FF00C8"),
            T("neon", "Neon", "#39FF14", "#FF00FF", "#00FFFF"),
            T("cyber", "Cyber", "#FFFF00", "#00FFFF", "#FF00AA"),
            T("aurora", "Aurora", "#00FFA3", "#00B3FF", "#8A2BE2"),
            T("nebula", "Nebula", "#7B2FF7", "#F107A3", "#FFB400"),
            T("retro", "Retro", "#2EC4B6", "#FFBF69", "#FF6B6B"),
            T("citrus", "Citrus", "#28A000", "#9BE000", "#FFF000"),
            T("velvet", "Velvet", "#6A0D2B", "#8E3A8A", "#E8A0A0"),
            T("ice", "Ice", "#FFFFFF", "#80D8FF", "#0091EA"),
            T("red", "Red", "#FF0000", "#FF0000", "#FF0000"),
            T("blue", "Blue", "#0040FF", "#0040FF", "#0040FF"),
            T("green", "Green", "#00C000", "#00C000", "#00C000"),
            T("yellow", "Yellow", "#FFD000", "#FFD000", "#FFD000"),
            T("orange", "Orange", "#FF7000", "#FF7000", "#FF7000"),
            T("purple", "Purple", "#7A00FF", "#7A00FF", "#7A00FF"),
            T("pink", "Pink", "#FF1493", "#FF1493", "#FF1493"),
            T("white", "White", "#FFFFFF", "#FFFFFF", "#FFFFFF")
        };

        public static LightTheme Find(string id) => All.FirstOrDefault(t => t.Id == id);

        private static LightTheme T(string id, string name, string low, string mid, string high) =>
            new LightTheme { Id = id, Name = name, Low = low, Mid = mid, High = high };
    }
}
