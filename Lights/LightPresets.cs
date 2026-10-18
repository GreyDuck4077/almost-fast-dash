using System;
using System.Collections.Generic;
using System.Linq;

namespace AlmostFastOverlays
{
    internal sealed class LightPreset
    {
        public string Id, Name, Description;
        public Func<DashboardSettings, LightProfile> Create;
    }

    /// <summary>Ready-made light profiles, and the one-time conversion of a dashboard's pre-layer light settings.</summary>
    internal static class LightPresets
    {
        public static readonly List<LightPreset> All = new List<LightPreset>
        {
            new LightPreset { Id = "refined", Name = "Refined", Description = "Rev lights, blind spot on the sides, pit limiter, low fuel, ABS and flags.", Create = d => Refined() },
            new LightPreset { Id = "spotter", Name = "Spotter focus", Description = "For ovals: the bar and both sides call cars alongside; revs stay on the shift row.", Create = d => SpotterFocus() },
            new LightPreset { Id = "endurance", Name = "Endurance", Description = "Revs on the shift row, fuel level across the bar, lap delta on the sides.", Create = d => Endurance() },
            new LightPreset { Id = "minimal", Name = "Minimal", Description = "Just rev lights and flags. Nothing else lights up.", Create = d => Minimal() },
        };

        public static LightPreset Find(string id) => All.FirstOrDefault(p => p.Id == id);

        private static LightLayer L(string name, string target, string effect, string condition = "", string blend = "Over", int brightness = 100, params string[] props)
        {
            var layer = new LightLayer { Name = name, Target = target, Effect = effect, Condition = condition, Blend = blend, Brightness = brightness };
            for (int i = 0; i + 1 < props.Length; i += 2) layer.Set(props[i], props[i + 1]);
            return layer;
        }

        private static LightProfile Profile(params LightLayer[] layers)
        {
            var profile = new LightProfile { Layers = layers.ToList() };
            profile.Normalize();
            return profile;
        }

        private static LightLayer Flags() =>
            L("Flags", "brow", "flag", "FlagActive", "Replace");

        private static LightLayer PitScanner() =>
            L("Pit limiter", "shift", "sweep", "OnPitRoad && PitLimiter && !OverPitLimit", "Replace", 80, "color", "info", "periodMs", "650", "tail", "2");

        private static LightLayer OverPitLimit() =>
            L("Over the pit limit", "shift", "blink", "OnPitRoad && OverPitLimit", "Replace", 100, "color", "bad", "rateMs", "90");

        public static LightProfile Refined() => Profile(
            Flags(),
            OverPitLimit(),
            PitScanner(),
            L("Blind spot", "sides", "spotter", "", "Over", 100),
            L("Light bar blind spot", "brow-mirror", "spotter", "", "Over", 100, "style", "Whole strip"),
            L("Low fuel", "sides", "blink", "FuelLapsLeft >= 0 && FuelLapsLeft < 2", "Over", 70, "color", "warn", "rateMs", "400"),
            L("ABS working", "sides", "solid", "AbsActive", "Over", 45, "color", "warn"),
            L("Rev lights", "shift", "revs", "", "Over", 60),
            L("Light bar rev lights", "brow", "revs", "", "Over", 55));

        public static LightProfile SpotterFocus() => Profile(
            Flags(),
            OverPitLimit(),
            PitScanner(),
            L("Light bar blind spot", "brow-mirror", "spotter", "", "Replace", 100, "style", "Whole strip"),
            L("Blind spot", "sides", "spotter", "", "Over", 100),
            L("Rev lights", "shift", "revs", "", "Over", 60));

        public static LightProfile Endurance() => Profile(
            Flags(),
            OverPitLimit(),
            PitScanner(),
            L("Blind spot", "sides", "spotter", "", "Over", 100),
            L("Lap delta", "sides", "center", "", "Over", 50),
            L("Fuel level", "brow", "meter", "", "Over", 45, "value", "=FuelPct", "direction", "Start to end", "colorMode", "By level", "low", "bad", "mid", "warn", "high", "good", "midAt", "0.1", "highAt", "0.25"),
            L("Rev lights", "shift", "revs", "", "Over", 60));

        public static LightProfile Minimal() => Profile(
            Flags(),
            L("Rev lights", "shift", "revs", "", "Over", 60));

        // ------------------------------------------------------------------ classic settings

        private static readonly Dictionary<string, string> PatternDirections = new Dictionary<string, string>
        {
            ["Edges to center"] = "Edges to center", ["Center outward"] = "Center out", ["Left to right"] = "Start to end", ["Right to left"] = "End to start"
        };

        private static string[] BankColors(LedBankSettings bank)
        {
            System.Drawing.Color[] colors;
            switch (bank.ColorProfile)
            {
                case LedProfiles.BluePurpleRed: colors = new[] { System.Drawing.Color.DeepSkyBlue, System.Drawing.Color.MediumPurple, System.Drawing.Color.Red }; break;
                case LedProfiles.Single:
                case "Red":
                    System.Drawing.Color single = SettingsStore.ParseColor(bank.HighColor, System.Drawing.Color.Red);
                    colors = new[] { single, single, single };
                    break;
                default:
                    colors = new[] { SettingsStore.ParseColor(bank.LowColor, System.Drawing.Color.LimeGreen), SettingsStore.ParseColor(bank.MidColor, System.Drawing.Color.Gold), SettingsStore.ParseColor(bank.HighColor, System.Drawing.Color.Red) };
                    break;
            }
            return colors.Select(SettingsStore.ToHex).ToArray();
        }

        private static string[] ColorProps(LedBankSettings bank)
        {
            string[] c = BankColors(bank);
            return new[] { "low", c[0], "mid", c[1], "high", c[2] };
        }

        /// <summary>
        /// Rebuilds a dashboard's pre-layer light settings as layers, once, when settings from before
        /// schema 8 are loaded. The colors, patterns, functions and brightness carry over.
        /// </summary>
        public static LightProfile FromClassic(DashboardSettings d)
        {
            LedBankSettings centerDefault = new LedBankSettings();
            var layers = new List<LightLayer>();
            BrowLedSettings brow = d?.BrowLeds ?? BrowLedSettings.CreateDefault(BrowFunctions.Shift);
            if (brow.FlagOverride || brow.Function == BrowFunctions.Flags) layers.Add(L("Flags", "brow", "flag", "FlagActive", "Replace", brow.Brightness));

            void Side(LedBankSettings bank, string target, string name)
            {
                if (bank == null || bank.Function == LedFunctions.Disabled) return;
                var props = new List<string>(ColorProps(bank));
                switch (bank.Function)
                {
                    case LedFunctions.BlindSpot:
                        layers.Add(L(name + " blind spot", target, "spotter", "", "Over", bank.Brightness, "color", BankColors(bank)[2]));
                        break;
                    case LedFunctions.Rpm:
                        props.AddRange(new[] { "source", "iRacing shift %", "direction", "End to start", "smooth", "false", "midAt", "0.5", "highAt", "0.75", "flash", "false" });
                        layers.Add(L(name + " revs", target, "revs", "", "Over", bank.Brightness, props.ToArray()));
                        break;
                    case LedFunctions.Brake:
                    case LedFunctions.Throttle:
                        props.AddRange(new[] { "value", bank.Function == LedFunctions.Brake ? "=Brake" : "=Throttle", "direction", "Start to end", "smooth", "false", "midAt", "0.5", "highAt", "0.75" });
                        layers.Add(L(name + (bank.Function == LedFunctions.Brake ? " brake" : " throttle"), target, "meter", "", "Over", bank.Brightness, props.ToArray()));
                        break;
                }
            }
            Side(d?.LeftLeds, "left", "Left");
            Side(d?.RightLeds, "right", "Right");

            LedBankSettings center = d?.CenterLeds ?? centerDefault;
            if (center != null && center.Function != LedFunctions.Disabled)
            {
                var props = new List<string>(ColorProps(center));
                props.AddRange(new[]
                {
                    "source", "iRacing shift %", "direction", PatternDirections.TryGetValue(center.Pattern ?? "", out string dir) ? dir : "Edges to center",
                    "smooth", "false", "midAt", "0.5", "highAt", "0.75", "flashColor", "#FF0000", "flashStyle", "Over fill", "flashMs", "140"
                });
                layers.Add(L("Rev lights", "shift", "revs", "", "Over", center.Brightness, props.ToArray()));
            }

            var browColors = ColorProps(brow);
            switch (brow.Function)
            {
                case BrowFunctions.Shift:
                    layers.Add(L("Light bar rev lights", "brow", "revs", "", "Over", brow.Brightness,
                        browColors.Concat(new[] { "source", "iRacing shift %", "smooth", "false", "midAt", "0.4", "highAt", "0.7", "flashColor", "#FF0000", "flashStyle", "Over fill", "flashMs", "140" }).ToArray()));
                    break;
                case BrowFunctions.BlindSpot:
                    layers.Add(L("Light bar blind spot", "brow-mirror", "spotter", "", "Over", brow.Brightness, "color", BankColors(brow)[2], "style", "Halves"));
                    break;
                case BrowFunctions.Pedals:
                    layers.Add(L("Light bar brake", "brow-left", "meter", "", "Over", brow.Brightness, "value", "=Brake", "direction", "Start to end", "smooth", "false", "colorMode", "Bands", "low", "#FF0000", "mid", "#FF0000", "high", "#FF0000"));
                    layers.Add(L("Light bar throttle", "brow-right", "meter", "", "Over", brow.Brightness, "value", "=Throttle", "direction", "Start to end", "smooth", "false", "low", "#00FF00", "mid", "#00FF00", "high", "#00FF00"));
                    break;
                case BrowFunctions.Brake:
                case BrowFunctions.Throttle:
                    layers.Add(L(brow.Function == BrowFunctions.Brake ? "Light bar brake" : "Light bar throttle", "brow", "meter", "", "Over", brow.Brightness,
                        browColors.Concat(new[] { "value", brow.Function == BrowFunctions.Brake ? "=Brake" : "=Throttle", "direction", "Center out", "smooth", "false", "midAt", "0.4", "highAt", "0.7" }).ToArray()));
                    break;
                case BrowFunctions.Delta:
                    layers.Add(L("Light bar lap delta", "brow-mirror", "center", "", "Over", brow.Brightness, "positive", "#00FF00", "negative", "#FF0000", "smooth", "false"));
                    break;
                case BrowFunctions.Fuel:
                    layers.Add(L("Light bar fuel", "brow", "meter", "", "Over", brow.Brightness,
                        browColors.Concat(new[] { "value", "=FuelPct", "direction", "Start to end", "smooth", "false", "colorMode", "By level", "low", BankColors(brow)[2], "high", BankColors(brow)[0], "midAt", "0.1", "highAt", "0.25" }).ToArray()));
                    break;
            }
            var profile = new LightProfile { Gamma = false, FadeMs = 0, Layers = layers };
            profile.Normalize();
            return profile;
        }
    }
}
