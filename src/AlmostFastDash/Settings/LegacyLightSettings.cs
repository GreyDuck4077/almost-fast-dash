namespace AlmostFastDash.Settings
{
    // ------------------------------------------------------------------ before schema 8
    // The light settings from before light layers, kept only so old settings can be read and converted.

    internal static class LedFunctions
    {
        public const string BlindSpot = "Blind spot", Rpm = "RPM", Brake = "Brake pressure", Throttle = "Throttle", Disabled = "Disabled";
    }

    internal static class BrowFunctions
    {
        public const string Shift = "Shift lights", BlindSpot = "Blind spot", Flags = "Flags only", Pedals = "Brake and throttle",
            Brake = "Brake pressure", Throttle = "Throttle", Delta = "Lap delta", Fuel = "Fuel level", Off = "Off";
    }

    internal static class LedProfiles
    {
        public const string GreenYellowRed = "Green / Yellow / Red", BluePurpleRed = "Blue / Purple / Red", Single = "Single color";
    }

    internal class LedBankSettings
    {
        public string Function { get; set; } = "RPM";
        public string Pattern { get; set; } = "Edges to center";
        public int Brightness { get; set; } = 55;
        public string ColorProfile { get; set; } = LedProfiles.GreenYellowRed;
        public string LowColor { get; set; } = "#00B85A";
        public string MidColor { get; set; } = "#FFB800";
        public string HighColor { get; set; } = "#FF202F";
    }

    internal sealed class SideLedSettings : LedBankSettings { }

    internal sealed class BrowLedSettings : LedBankSettings
    {
        /// <summary>When on, any racing flag took over the light bar.</summary>
        public bool FlagOverride { get; set; } = true;

        public static BrowLedSettings CreateDefault(string function) => new BrowLedSettings
        {
            Function = function, Pattern = "Edges to center", Brightness = 60, ColorProfile = LedProfiles.GreenYellowRed,
                FlagOverride = true
        };
    }
}
