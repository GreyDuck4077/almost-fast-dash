using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace AlmostFastOverlays
{
    /// <summary>End-to-end checks for the light layers, run as part of --designer-selftest.</summary>
    internal static class LightsSelfTest
    {
        private static TelemetrySnapshot Snap(double shift, double rpm, RacingFlag flag = RacingFlag.None,
            BlindSpotZone left = BlindSpotZone.None, BlindSpotZone right = BlindSpotZone.None) =>
            IRacingTelemetrySource.WithPreviewExtras(new TelemetrySnapshot(
                true, 4, 120, flag, left, right, shift, SessionProgressMode.LapRace, 0, 0, 8, 18, 4, 2, 24.3, 58.104, 0.46,
                "J. MILLER", -0.46, "M. CARTER", 0.82, 22, false, 87.932, 87.521, 88.204, new bool[18], 0, 6, rpm));

        public static List<string> Run(AppSettings settings)
        {
            var report = new List<string>();
            void Check(string name, bool ok, string detail = "") =>
                report.Add((ok ? "PASS  " : "FAIL  ") + name + (detail.Length > 0 ? "  (" + detail + ")" : ""));
            var units = new UnitSystem(settings.General);
            const int brow = 20;

            // ---------------- every preset in every scenario
            string[] scenarios = { "Racing", "Shift point", "Car left", "Car right", "Three wide", "Pit limiter", "Over pit limit", "Low fuel", "ABS working",
                "Green flag", "Yellow flag", "White flag", "Blue flag", "Black flag", "Checkered flag", "Not connected" };
            foreach (LightPreset preset in LightPresets.All)
            {
                LightProfile profile = preset.Create(DashboardSettings.CreateHalo());
                var renderer = new LightRenderer(profile, "#2DD4BF", units, brow);
                var errors = new List<string>();
                int now = 0;
                foreach (string scenario in scenarios)
                {
                    LightFrame frame = renderer.Compose(LightsWindow.Scenario(scenario), now += 33);
                    errors.AddRange(renderer.Errors.Values);
                    if (frame.Dv5.Length != 16 || frame.Brow.Length != brow) errors.Add("bad frame size");
                }
                Check("light preset runs in every scenario: " + preset.Name, errors.Count == 0, string.Join("; ", errors.Distinct().Take(3)));
            }

            // ---------------- converted lights match the classic lights exactly
            var states = new List<Tuple<string, TelemetrySnapshot>>
            {
                Tuple.Create("idle", Snap(0, 3000)),
                Tuple.Create("full revs", Snap(0.9, 7000)),
                Tuple.Create("shift point", Snap(1, 7960)),
            };
            foreach (BlindSpotZone zone in new[] { BlindSpotZone.Front, BlindSpotZone.Beside, BlindSpotZone.Rear })
            {
                states.Add(Tuple.Create("car left " + zone, Snap(0, 3000, left: zone)));
                states.Add(Tuple.Create("car right " + zone, Snap(0, 3000, right: zone)));
            }
            foreach (RacingFlag flag in new[] { RacingFlag.Green, RacingFlag.Yellow, RacingFlag.White, RacingFlag.BlueYellowStripe, RacingFlag.Black, RacingFlag.Checkered, RacingFlag.Meatball })
                states.Add(Tuple.Create("flag " + flag, Snap(0, 3000, flag)));

            var dashboards = settings.Dashboards.Select(SettingsStore.Clone).ToList();
            dashboards.AddRange(new[] { DashboardSettings.CreateHalo(), DashboardSettings.CreateOval() });
            foreach (DashboardSettings dashboard in dashboards)
            {
                LightProfile converted = LightPresets.FromClassic(dashboard);
                var renderer = new LightRenderer(converted, dashboard.AccentColor, units, brow);
                var mismatches = new List<string>();
                foreach (var state in states)
                    foreach (int now in new[] { 0, 140, 250 })
                    {
                        TelemetrySnapshot t = state.Item2;
                        bool flash = t.ShiftAlert && ((now / 140) & 1) == 0;
                        LedInputs inputs = LedInputs.From(t, flash);
                        inputs.Clock = now;
                        Color[] classicDv5 = LedFrame.Compose(dashboard, inputs);
                        Color[] classicBrow = LedFrame.ComposeBrow(dashboard.BrowLeds, brow, inputs);
                        LightFrame layered = renderer.Compose(t, now);
                        for (int i = 0; i < 16; i++)
                            if (classicDv5[i].ToArgb() != layered.Dv5[i].ToArgb()) mismatches.Add(state.Item1 + " @" + now + " DV-5 LED " + i + " " + Hex(classicDv5[i]) + "≠" + Hex(layered.Dv5[i]));
                        for (int i = 0; i < brow; i++)
                            if (classicBrow[i].ToArgb() != layered.Brow[i].ToArgb()) mismatches.Add(state.Item1 + " @" + now + " bar LED " + i + " " + Hex(classicBrow[i]) + "≠" + Hex(layered.Brow[i]));
                    }
                Check("converted lights match classic: " + dashboard.Name + " (" + dashboard.BrowLeds?.Function + " bar)", mismatches.Count == 0,
                    mismatches.Count + " differences" + (mismatches.Count > 0 ? ": " + string.Join("; ", mismatches.Take(3)) : ""));
            }

            // ---------------- priority and blending
            var refined = new LightRenderer(LightPresets.Refined(), "#2DD4BF", units, brow);
            TelemetrySnapshot leftLowFuel = Snap(0.3, 5000, left: BlindSpotZone.Beside);
            leftLowFuel.FuelPerLapAverage = 13;
            LightFrame f1 = refined.Compose(leftLowFuel, 1000);
            Check("higher layer wins: blind spot over low fuel", f1.Dv5Owner[1] == "Blind spot" && f1.Dv5Owner[13] == "Low fuel", f1.Dv5Owner[1] + " / " + f1.Dv5Owner[13]);
            LightFrame f2 = refined.Compose(LightsWindow.Scenario("Pit limiter"), 2000);
            Check("replace takes the whole target: pit limiter", Enumerable.Range(4, 8).All(i => f2.Dv5Owner[i] == "Pit limiter"));
            LightFrame f3 = refined.Compose(LightsWindow.Scenario("Yellow flag"), 3000);
            Check("flags take over the bar", f3.BrowOwner.All(o => o == "Flags") && f3.Brow.All(c => c.R > 0 && c.B == 0));

            // ---------------- output stage
            var gamma = new LightProfile { Gamma = true, FadeMs = 0, Layers = { new LightLayer { Target = "shift", Effect = "solid", Props = { ["color"] = "#808080" } } } };
            Color encoded = new LightRenderer(gamma, "#000000", units, brow).Compose(Snap(0, 3000), 0).Dv5[4];
            Check("gamma makes half level look half as bright", Math.Abs(encoded.R - 55) <= 1, "128 → " + encoded.R);
            var fade = new LightProfile { Gamma = false, FadeMs = 300, Layers = { new LightLayer { Target = "shift", Effect = "solid", Condition = "Gear = 4", Props = { ["color"] = "#FF0000" } } } };
            var fader = new LightRenderer(fade, "#000000", units, brow);
            fader.Compose(Snap(0, 3000), 0);
            TelemetrySnapshot neutral = IRacingTelemetrySource.WithPreviewExtras(new TelemetrySnapshot(true, 0, 0, RacingFlag.None, BlindSpotZone.None, BlindSpotZone.None, 0,
                SessionProgressMode.LapRace, 0, 0, 8, 18, 4, 2, 24.3, 58.104, 0.46, "", 0, "", 0, 22, false, 0, 0, 0, new bool[18], 0, 6, 900));
            int during = fader.Compose(neutral, 100).Dv5[4].R, after = fader.Compose(neutral, 1100).Dv5[4].R;
            Check("fade out eases off, then reaches dark", during > 10 && during < 255 && after == 0, "after 100 ms " + during + ", after 1.1 s " + after);

            TelemetrySnapshot car = Snap(0.2, 5800);
            Check("rev lights follow the car's shift-light RPMs", Math.Abs(DataCatalog.RevFraction(car) - 0.5) < 0.001, DataCatalog.RevFraction(car).ToString("0.000"));
            Check("shift point from the car's blink RPM", DataCatalog.AtShiftPoint(Snap(0.5, 7960)) && !DataCatalog.AtShiftPoint(Snap(0.99, 7900)));

            var rainbow = Enumerable.Range(0, 16).Select(i => Color.FromArgb(i * 10, 255 - i * 10, i)).ToArray();
            List<byte[]> packets = ArduinoLedController.BatchedPayloads(rainbow, 16);
            Check("fast updates send a 16-colour frame as one packet", packets.Count == 1 && packets[0].Length == 99 && packets[0][1] == (byte)'6' && packets[0][2] == 3 && packets[0][98] == 0,
                packets.Count + " packets, " + packets[0].Length + " bytes");
            List<byte[]> solid = ArduinoLedController.BatchedPayloads(Enumerable.Repeat(Color.Red, 16).ToArray(), 16);
            Check("fast updates merge a single colour into one run", solid.Count == 1 && solid[0].Length == 9 && solid[0][4] == 16);

            // ---------------- other LED devices
            byte[] ada = AdalightOutput.Packet(new[] { Color.Red, Color.Lime, Color.Blue });
            Check("Adalight packet: header, count and checksum", ada.Length == 15 && ada[0] == (byte)'A' && ada[1] == (byte)'d' && ada[2] == (byte)'a' && ada[3] == 0 && ada[4] == 2 && ada[5] == (0 ^ 2 ^ 0x55)
                && ada[6] == 255 && ada[7] == 0 && ada[10] == 255 && ada[14] == 255, string.Join(" ", ada.Take(6)));
            byte[] adaLong = AdalightOutput.Packet(new Color[300]);
            Check("Adalight packet over 255 LEDs", adaLong[3] == 1 && adaLong[4] == 43 && adaLong[5] == (1 ^ 43 ^ 0x55) && adaLong.Length == 6 + 900);
            List<byte[]> wled = WledOutput.Packets(Enumerable.Repeat(Color.FromArgb(1, 2, 3), 10).ToArray(), 2);
            Check("WLED packet: DRGB with timeout", wled.Count == 1 && wled[0].Length == 32 && wled[0][0] == 2 && wled[0][1] == 2 && wled[0][2] == 1 && wled[0][4] == 3);
            List<byte[]> wledLong = WledOutput.Packets(new Color[1000], 2);
            Check("WLED over 490 LEDs: DNRGB chunks with start index", wledLong.Count == 3 && wledLong.All(p => p[0] == 4)
                && wledLong[1][2] == (489 >> 8) && wledLong[1][3] == (489 & 0xFF) && wledLong[2][3] == (978 & 0xFF) && wledLong[2].Length == 4 + 22 * 3);
            Color[] stretched = LedRoleMap.Stretch(new[] { Color.Red, Color.Blue }, 4);
            Check("stretching a strip across more LEDs", stretched[0] == Color.Red && stretched[1] == Color.Red && stretched[2] == Color.Blue && stretched[3] == Color.Blue);
            Color[] squeezed = LedRoleMap.Stretch(Enumerable.Range(0, 8).Select(i => Color.FromArgb(i, 0, 0)).ToArray(), 4);
            Check("squeezing a strip onto fewer LEDs", squeezed.Select(c => (int)c.R).SequenceEqual(new[] { 1, 3, 5, 7 }), string.Join(",", squeezed.Select(c => c.R)));
            var dv5Sample = Enumerable.Range(0, 16).Select(i => Color.FromArgb(i * 10, 0, 0)).ToArray();
            var barSample = Enumerable.Range(0, 20).Select(i => Color.FromArgb(0, i * 10, 0)).ToArray();
            var shiftDevice = new LedDeviceSettings { Role = LedRoles.Shift, LedCount = 8 };
            Check("shift-lights role takes the DV-5 shift row", LedRoleMap.Frame(shiftDevice, dv5Sample, barSample, RacingFlag.None, 0).Select(c => (int)c.R).SequenceEqual(Enumerable.Range(4, 8).Select(i => i * 10)));
            var barDevice = new LedDeviceSettings { Role = LedRoles.Bar, LedCount = 20, Reverse = true, Brightness = 50 };
            Color[] bar = LedRoleMap.Frame(barDevice, dv5Sample, barSample, RacingFlag.None, 0);
            Check("light-bar role, reversed and at half brightness", bar[0].G == 95 && bar[19].G == 0 && bar[1].G == 90, bar[0].G + "/" + bar[19].G);
            var flagDevice = new LedDeviceSettings { Role = LedRoles.Flags, LedCount = 6 };
            Check("flags role shows the flag and goes dark without one",
                LedRoleMap.Frame(flagDevice, dv5Sample, barSample, RacingFlag.Green, 0).All(c => c.ToArgb() == Color.Lime.ToArgb())
                && LedRoleMap.Frame(flagDevice, dv5Sample, barSample, RacingFlag.None, 0).All(c => c.ToArgb() == Color.Black.ToArgb() || c.A == 0));
            var withDevices = new AppSettings();
            withDevices.General.LedDevices.Add(new LedDeviceSettings { Kind = LedDeviceKinds.Wled, Address = "192.168.1.40", Name = "Desk strip", LedCount = 60, Role = LedRoles.Flags, Reverse = true });
            AppSettings reloaded = SettingsStore.Clone(withDevices);
            reloaded.EnsureBuiltInDashboards();
            LedDeviceSettings again = reloaded.General.LedDevices.FirstOrDefault();
            Check("LED devices save and load", again != null && again.Kind == LedDeviceKinds.Wled && again.Address == "192.168.1.40" && again.LedCount == 60 && again.Role == LedRoles.Flags && again.Reverse);

            // ---------------- persistence
            DashboardSettings saved = DashboardSettings.CreateHalo();
            saved.Lights = LightPresets.Refined();
            DashboardSettings loaded = SettingsStore.Clone(saved);
            loaded.Normalize();
            Check("light profiles save and load", loaded.Lights != null && loaded.Lights.Layers.Count == saved.Lights.Layers.Count &&
                loaded.Lights.Layers[1].Condition == saved.Lights.Layers[1].Condition && loaded.Lights.Layers.Last().Effect == "revs");
            return report;
        }

        private static string Hex(Color c) => SettingsStore.ToHex(c);
    }
}
