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
            string temperature = general?.TemperatureUnit;
            Celsius = string.IsNullOrEmpty(temperature) ? Metric : string.Equals(temperature, "Celsius", StringComparison.OrdinalIgnoreCase);
            FuelReserveLaps = Math.Max(0, Math.Min(5, general?.FuelReserveLaps ?? 0));
        }

        /// <summary>The fuel margin chosen under settings, in laps; it travels with the units because both shape the numbers shown.</summary>
        public double FuelReserveLaps { get; }

        public bool Metric { get; }
        public bool Gallons { get; }
        public bool Celsius { get; }
        public string TemperatureLabel => Celsius ? "°C" : "°F";

        /// <summary>A temperature in the user's unit; NaN stays NaN (not known).</summary>
        public double Temperature(double celsius) => double.IsNaN(celsius) ? double.NaN : Celsius ? celsius : celsius * 9 / 5 + 32;
        public string SpeedLabel => Metric ? "KM/H" : "MPH";
        public string FuelSuffix => Gallons ? "GAL" : "L";

        public double Speed(double mph) => Metric ? mph * KphPerMph : mph;
        public double Fuel(double liters) => Gallons ? liters / LitersPerGallon : liters;
    }
}
