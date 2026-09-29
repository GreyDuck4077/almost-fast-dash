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
        public RacingFlag Flag;
        public double Delta;
        public double FuelPct;
        /// <summary>Milliseconds clock used for blinking; Environment.TickCount at runtime.</summary>
        public int Clock;

        public static LedInputs From(TelemetrySnapshot t, bool shiftFlashOn) => new LedInputs
        {
            Left = t.LeftBlindSpot,
            Right = t.RightBlindSpot,
            ShiftPct = t.ShiftIndicatorPct,
            ShiftFlashOn = shiftFlashOn,
            Brake = t.Brake,
            Throttle = t.Throttle,
            Flag = t.Flag,
            Delta = t.LapDeltaSeconds,
            FuelPct = t.FuelLevelPct,
            Clock = Environment.TickCount
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
            settings = settings ?? DashboardSettings.CreateSummit();
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

        /// <summary>
        /// Composes the LB-10 brow light bar, a single horizontal row of
        /// <paramref name="count"/> LEDs numbered left to right.
        /// </summary>
        public static Color[] ComposeBrow(BrowLedSettings settings, int count, LedInputs input)
        {
            count = Math.Max(1, count);
            var frame = new Color[count];
            for (int i = 0; i < count; i++) frame[i] = Color.Black;
            if (settings == null || settings.Function == BrowFunctions.Off) return frame;
            int brightness = settings.Brightness;
            Color[] colors = GetProfile(settings);
            bool blink = ((input.Clock / 250) & 1) == 0;

            if (settings.FlagOverride && input.Flag != RacingFlag.None || settings.Function == BrowFunctions.Flags)
            {
                if (input.Flag != RacingFlag.None) ComposeFlag(frame, input.Flag, blink, brightness);
                return frame;
            }

            int half = count / 2;
            bool odd = count % 2 == 1;
            switch (settings.Function)
            {
                case BrowFunctions.Shift:
                    if (input.ShiftFlashOn) { for (int i = 0; i < count; i++) frame[i] = Scale(Color.Red, brightness); break; }
                    int lit = (int)Math.Ceiling(Clamp01(input.ShiftPct) * (half + (odd ? 1 : 0)));
                    for (int i = 0; i < lit; i++)
                    {
                        Color c = Scale(colors[Math.Min(2, i * 3 / Math.Max(1, half))], brightness);
                        frame[i] = c;
                        frame[count - 1 - i] = c;
                    }
                    break;
                case BrowFunctions.BlindSpot:
                    Color alert = Scale(colors[2], brightness);
                    FillZone(frame, 0, half, input.Left, alert, true);
                    FillZone(frame, count - half, half, input.Right, alert, false);
                    break;
                case BrowFunctions.Pedals:
                    int brake = (int)Math.Ceiling(Clamp01(input.Brake) * half - 0.05);
                    int throttle = (int)Math.Ceiling(Clamp01(input.Throttle) * half - 0.05);
                    for (int i = 0; i < brake; i++) frame[half - 1 - i] = Scale(Color.Red, brightness);
                    for (int i = 0; i < throttle; i++) frame[count - half + i] = Scale(Color.Lime, brightness);
                    break;
                case BrowFunctions.Brake:
                case BrowFunctions.Throttle:
                    double value = settings.Function == BrowFunctions.Brake ? input.Brake : input.Throttle;
                    int outward = (int)Math.Ceiling(Clamp01(value) * (half + (odd ? 1 : 0)) - 0.05);
                    for (int i = 0; i < outward; i++)
                    {
                        Color c = Scale(colors[Math.Min(2, i * 3 / Math.Max(1, half))], brightness);
                        frame[(count - 1) / 2 - i] = c;
                        frame[count / 2 + i] = c;
                    }
                    break;
                case BrowFunctions.Delta:
                    int length = (int)Math.Round(Math.Min(1, Math.Abs(input.Delta)) * half);
                    Color tone = Scale(input.Delta >= 0 ? Color.Lime : Color.Red, brightness);
                    for (int i = 0; i < length; i++)
                        frame[input.Delta >= 0 ? count - half + i : half - 1 - i] = tone;
                    if (odd) frame[half] = Scale(Color.White, brightness / 3);
                    break;
                case BrowFunctions.Fuel:
                    double pct = Clamp01(input.FuelPct);
                    int fuel = (int)Math.Ceiling(pct * count);
                    Color level = pct < 0.1 ? colors[2] : pct < 0.25 ? colors[1] : colors[0];
                    if (pct < 0.1 && !blink) break;
                    for (int i = 0; i < fuel; i++) frame[i] = Scale(level, brightness);
                    break;
            }
            return frame;
        }

        private static void FillZone(Color[] frame, int start, int length, BlindSpotZone zone, Color color, bool leftSide)
        {
            if (zone == BlindSpotZone.None || length <= 0) return;
            // Alongside lights the whole half; ahead lights the inner part, behind the outer part.
            int part = Math.Max(1, (length + 1) / 2);
            int from = start, to = start + length;
            if (zone == BlindSpotZone.Front) { if (leftSide) from = to - part; else to = from + part; }
            else if (zone == BlindSpotZone.Rear) { if (leftSide) to = from + part; else from = to - part; }
            for (int i = from; i < to; i++) frame[i] = color;
        }

        private static void ComposeFlag(Color[] frame, RacingFlag flag, bool blink, int brightness)
        {
            for (int i = 0; i < frame.Length; i++)
            {
                Color c;
                switch (flag)
                {
                    case RacingFlag.Green: c = Color.Lime; break;
                    case RacingFlag.Yellow: case RacingFlag.SafetyCar: c = Color.FromArgb(255, 190, 0); break;
                    case RacingFlag.White: c = Color.White; break;
                    case RacingFlag.Red: c = Color.Red; break;
                    case RacingFlag.BlueYellowStripe: c = blink ? Color.FromArgb(0, 90, 255) : Color.FromArgb(255, 190, 0); break;
                    case RacingFlag.Black: c = blink ? Color.White : Color.Black; break;
                    case RacingFlag.Meatball: c = blink ? Color.FromArgb(255, 90, 0) : Color.Black; break;
                    case RacingFlag.Surface: c = (i % 2 == 0) == blink ? Color.FromArgb(255, 190, 0) : Color.Red; break;
                    case RacingFlag.Checkered: c = (i % 2 == 0) == blink ? Color.White : Color.Black; break;
                    default: c = Color.Black; break;
                }
                frame[i] = Scale(c, brightness);
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
