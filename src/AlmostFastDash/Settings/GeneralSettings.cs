using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace AlmostFastDash.Settings
{
    internal sealed class GeneralSettings
    {
        /// <summary>Every LED device that has been set up: the LEDs on a wheel screen, light bars, strips.</summary>
        public List<DeviceSettings> Devices { get; set; } = new List<DeviceSettings>();

        public int ScreenBrightness { get; set; } = 100;

        /// <summary>The wheel screen is sent only the parts of each frame that changed (much less to send). Off sends every frame whole.</summary>
        public bool SendChangesOnly { get; set; } = true;

        /// <summary>Frames a second while racing, at most (drawn only when something changed): 60, or 30 to use less of the PC.</summary>
        public int FrameRate { get; set; } = 60;

        /// <summary>The display (Windows' name, such as \\.\DISPLAY2) the running dashboard is shown on full screen, when PcOutput is FullScreen.</summary>
        public string MonitorDevice { get; set; }

        /// <summary>
        /// Where on the PC the running dashboard is also shown: Off, Window, Overlay (over the game) or FullScreen (on
        /// MonitorDevice). Empty in settings from before it existed, where a chosen display meant full screen.
        /// </summary>
        public string PcOutput { get; set; }

        /// <summary>PcOutput as it applies: what was chosen, or for older settings full screen when a display was.</summary>
        public string EffectivePcOutput() =>
            PcOutputs.Contains(PcOutput) ? PcOutput : string.IsNullOrEmpty(MonitorDevice) ? "Off" : "FullScreen";

        public static readonly string[] PcOutputs = { "Off", "Window", "Overlay", "FullScreen" };

        /// <summary>How opaque the dashboard is over the game, 30 to 100 percent.</summary>
        public int OverlayOpacity { get; set; } = 100;

        /// <summary>Over the game, the dashboard shows only while iRacing is running (and while it's being placed or previewed).</summary>
        public bool OverlayOnlyWhileRacing { get; set; } = true;

        /// <summary>Where in iRacing the dashboard shows (Show when, at the top). Hidden elsewhere: the wheel screen black, the PC window away.</summary>
        public bool ShowInCar { get; set; } = true;
        public bool ShowOutOfCar { get; set; } = true;
        public bool ShowInGarage { get; set; } = true;
        public bool ShowSpectating { get; set; } = true;

        /// <summary>Whether the dashboard shows at this point in iRacing.</summary>
        public bool ShowsAt(CarState state)
        {
            switch (state)
            {
                case CarState.OutOfCar: return ShowOutOfCar;
                case CarState.Garage: return ShowInGarage;
                case CarState.Spectating: return ShowSpectating;
                default: return ShowInCar;
            }
        }
        public string SpeedUnit { get; set; } = "MPH";
        public string FuelUnit { get; set; } = "Liters";
        /// <summary>"Celsius" or "Fahrenheit"; empty follows the speed unit (km/h gives Celsius), as before it could be chosen.</summary>
        public string TemperatureUnit { get; set; }
        /// <summary>The sidebar page the window opens on: Dashboards, Devices or Settings.</summary>
        public string StartPage { get; set; } = "Dashboards";
        /// <summary>Minimizing the window hides it to the notification area instead of the taskbar.</summary>
        public bool MinimizeToTray { get; set; }
        /// <summary>Asks GitHub for the latest release when the window opens. Off unless the user turns it on.</summary>
        public bool CheckForUpdates { get; set; }

        /// <summary>Extra laps of fuel counted into fuel to add (0, 0.5, 1 or 2): a margin for a slower or thirstier finish.</summary>
        public double FuelReserveLaps { get; set; }

        /// <summary>The screen size last chosen for a new dashboard when no wheel screen was found (0 until then).</summary>
        public int LastScreenWidth { get; set; }
        public int LastScreenHeight { get; set; }
        public bool AutoSwitchByDiscipline { get; set; } = true;
        public bool DiagnosticsEnabled { get; set; } = true;

        /// <summary>
        /// The Dashboards page shows the getting-started steps. Only a new install turns this on, so an upgrade never
        /// shows them; it goes off when the dashboard is first started or the card is dismissed.
        /// </summary>
        public bool ShowWelcome { get; set; }

        /// <summary>
        /// The version whose "What's new" card was last seen (major.minor). After an update the Dashboards page shows
        /// the card once; a new install starts at its own version, so it sees the welcome card instead.
        /// </summary>
        public string SeenVersion { get; set; } = "";

        /// <summary>
        /// Units for the region Windows is set to: mph, gallons and °F where those are the norm (the US), mph with
        /// liters and °C in the UK, and km/h, liters and °C everywhere else.
        /// </summary>
        public void UseRegionUnits(System.Globalization.RegionInfo region)
        {
            bool british = region.TwoLetterISORegionName == "GB";
            bool imperial = !region.IsMetric && !british;
            SpeedUnit = imperial || british ? "MPH" : "KPH";
            FuelUnit = imperial ? "Gallons" : "Liters";
            TemperatureUnit = imperial ? "Fahrenheit" : "Celsius";
        }

        // Before schema 6 the DV-5 and LB-10 were fixed settings. They are read once to
        // build Devices and then cleared; nothing else uses them.
        public string LedPort { get; set; }
        public bool BrowEnabled { get; set; } = true;
        public string BrowPort { get; set; }
        public int BrowLedCount { get; set; } = 20;
    }
}
