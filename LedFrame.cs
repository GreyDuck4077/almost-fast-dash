using System;
using System.Drawing;

namespace DV5Direct
{
    internal static class LedFunctions
    {
        public const string BlindSpot = "Blind spot";
        public const string Rpm = "RPM";
        public const string Brake = "Brake pressure";
        public const string Throttle = "Throttle";
        public const string Disabled = "Disabled";
        public static readonly string[] Side = { BlindSpot, Rpm, Brake, Throttle, Disabled };
        public static readonly string[] Center = { Rpm, Disabled };
    }

    internal static class LedPatterns
    {
        public static readonly string[] Center = { "Edges to center", "Center outward", "Left to right", "Right to left" };
    }

    /// <summary>
    /// Inputs that drive the 16 DV-5 LEDs for one frame.
    /// </summary>
    internal struct LedInputs
    {
        public BlindSpotZone Left;
        public BlindSpotZone Right;
        public double ShiftPct;
        public bool ShiftFlashOn;
        public double Brake;
        public double Throttle;

        public static LedInputs From(TelemetrySnapshot t, bool shiftFlashOn) => new LedInputs
        {
            Left = t.LeftBlindSpot,
            Right = t.RightBlindSpot,
            ShiftPct = t.ShiftIndicatorPct,
            ShiftFlashOn = shiftFlashOn,
            Brake = t.Brake,
            Throttle = t.Throttle
        };
    }

    /// <summary>
    /// Pure LED layout logic shared by the hardware controller and the settings preview.
    /// Physical chain: 0-3 run up the left side, 4-11 span the upper shift-light
    /// row from left to right, and 12-15 run down the right side.
    /// </summary>
    internal static class LedFrame
    {
        public const int Count = 16;

        public static Color[] Compose(DashboardSettings settings, LedInputs input)
        {
            var frame = new Color[Count];
            for (int i = 0; i < Count; i++) frame[i] = Color.Black;
            settings = settings ?? DashboardSettings.CreateDefault();
            int rpmPairs = (int)Math.Ceiling(Clamp01(input.ShiftPct) * 4);
            ComposeSide(frame, true, input.Left, rpmPairs, input, settings.LeftLeds);
            ComposeSide(frame, false, input.Right, rpmPairs, input, settings.RightLeds);
            ComposeCenter(frame, rpmPairs, input.ShiftFlashOn, settings.CenterLeds);
            return frame;
        }

        private static void ComposeCenter(Color[] frame, int pairs, bool shiftFlashOn, LedBankSettings settings)
        {
            if (settings == null || settings.Function == LedFunctions.Disabled) return;
            int brightness = settings.Brightness;
            if (shiftFlashOn)
            {
                for (int i = 4; i < 12; i++) frame[i] = Scale(Color.Red, brightness);
                return;
            }

            int count = Math.Max(0, Math.Min(4, pairs));
            Color[] colors = GetProfile(settings);
            for (int pair = 0; pair < count; pair++)
            {
                Color color = Scale(colors[Math.Min(2, pair * 3 / 4)], brightness);
                switch (settings.Pattern)
                {
                    case "Left to right": Fill(frame, 4 + pair * 2, 2, color); break;
                    case "Right to left": Fill(frame, 10 - pair * 2, 2, color); break;
                    case "Center outward": frame[7 - pair] = color; frame[8 + pair] = color; break;
                    default: frame[4 + pair] = color; frame[11 - pair] = color; break;
                }
            }
        }

        private static void ComposeSide(
            Color[] frame, bool left, BlindSpotZone zone, int rpmPairs, LedInputs input, LedBankSettings settings)
        {
            if (settings == null || settings.Function == LedFunctions.Disabled) return;
            // Positions counted from the top of the side column (0 = top, 3 = bottom).
            Func<int, int> at = fromTop => left ? 3 - fromTop : 12 + fromTop;
            Color[] colors = GetProfile(settings);
            int brightness = settings.Brightness;
            Color alert = Scale(colors[2], brightness);

            switch (settings.Function)
            {
                case LedFunctions.BlindSpot:
                    if (zone == BlindSpotZone.Front) frame[at(0)] = alert;
                    else if (zone == BlindSpotZone.Beside) { frame[at(1)] = alert; frame[at(2)] = alert; }
                    else if (zone == BlindSpotZone.Rear) { frame[at(2)] = alert; frame[at(3)] = alert; }
                    break;
                case LedFunctions.Rpm:
                    for (int i = 0; i < Math.Min(4, rpmPairs); i++)
                        frame[at(i)] = Scale(colors[Math.Min(2, i * 3 / 4)], brightness);
                    break;
                case LedFunctions.Brake:
                case LedFunctions.Throttle:
                    double value = settings.Function == LedFunctions.Brake ? input.Brake : input.Throttle;
                    int lit = (int)Math.Ceiling(Clamp01(value) * 4 - 0.05);
                    for (int i = 0; i < lit; i++)
                        frame[at(3 - i)] = Scale(colors[Math.Min(2, i * 3 / 4)], brightness);
                    break;
            }
        }

        public static Color[] GetProfile(LedBankSettings settings)
        {
            switch (settings.ColorProfile)
            {
                case LedProfiles.BluePurpleRed:
                    return new[] { Color.DeepSkyBlue, Color.MediumPurple, Color.Red };
                case LedProfiles.Single:
                case "Red":
                    Color single = SettingsStore.ParseColor(settings.HighColor, Color.Red);
                    return new[] { single, single, single };
                default:
                    return new[]
                    {
                        SettingsStore.ParseColor(settings.LowColor, Color.LimeGreen),
                        SettingsStore.ParseColor(settings.MidColor, Color.Gold),
                        SettingsStore.ParseColor(settings.HighColor, Color.Red)
                    };
            }
        }

        public static bool SameFrame(Color[] a, Color[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i].ToArgb() != b[i].ToArgb()) return false;
            return true;
        }

        private static void Fill(Color[] frame, int start, int count, Color color)
        {
            for (int i = start; i < start + count && i < Count; i++) if (i >= 0) frame[i] = color;
        }

        private static Color Scale(Color color, int brightness)
        {
            brightness = Math.Max(0, Math.Min(100, brightness));
            return Color.FromArgb(color.R * brightness / 100, color.G * brightness / 100, color.B * brightness / 100);
        }

        private static double Clamp01(double value) => double.IsNaN(value) ? 0 : Math.Max(0, Math.Min(1, value));
    }
}
