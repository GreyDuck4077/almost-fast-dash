using System;

namespace AlmostFastOverlays
{
    /// <summary>Converts telemetry (MPH, liters) into the user's preferred display units.</summary>
    internal sealed class UnitSystem
    {
        private const double KphPerMph = 1.609344;
        private const double LitersPerGallon = 3.785411784;

        public static readonly UnitSystem Default = new UnitSystem(null);

        public UnitSystem(GeneralSettings general)
        {
            Metric = string.Equals(general?.SpeedUnit, "KPH", StringComparison.OrdinalIgnoreCase);
            Gallons = string.Equals(general?.FuelUnit, "Gallons", StringComparison.OrdinalIgnoreCase);
        }

        public bool Metric { get; }
        public bool Gallons { get; }
        public string SpeedLabel => Metric ? "KM/H" : "MPH";
        public string FuelSuffix => Gallons ? "GAL" : "L";

        public double Speed(double mph) => Metric ? mph * KphPerMph : mph;
        public double Fuel(double liters) => Gallons ? liters / LitersPerGallon : liters;

        public string FormatSpeed(TelemetrySnapshot t) => t.Connected ? Speed(t.SpeedMph).ToString("0") : "---";

        /// <summary>Formats a fuel quantity; negative values mean "not known yet".</summary>
        public string FormatFuel(double liters, bool withUnit = true, bool precise = false)
        {
            string suffix = withUnit ? " " + FuelSuffix : "";
            if (liters < 0) return "--.-" + suffix;
            string format = Gallons ? (precise ? "0.000" : "0.00") : (precise ? "0.00" : "0.0");
            return Fuel(liters).ToString(format) + suffix;
        }
    }
}
