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
            List<byte[]> packets = Dv5LedController.BatchedPayloads(rainbow, 16);
            Check("fast updates send a 16-colour frame as one packet", packets.Count == 1 && packets[0].Length == 99 && packets[0][1] == (byte)'6' && packets[0][2] == 3 && packets[0][98] == 0,
                packets.Count + " packets, " + packets[0].Length + " bytes");
            List<byte[]> solid = Dv5LedController.BatchedPayloads(Enumerable.Repeat(Color.Red, 16).ToArray(), 16);
            Check("fast updates merge a single colour into one run", solid.Count == 1 && solid[0].Length == 9 && solid[0][4] == 16);

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
