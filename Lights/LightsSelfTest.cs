using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace AlmostFastOverlays
{
    /// <summary>End-to-end checks for the light layers, run as part of --designer-selftest.</summary>
    internal static class LightsSelfTest
    {
        private sealed class RecordingOutput : ILedOutput
        {
            public readonly List<int> Lengths = new List<int>();
            public int KeepAliveMilliseconds => 0;
            public void Send(Color[] frame) { lock (Lengths) Lengths.Add(frame.Length); }
            public void Dispose() { }
        }

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
            // The DV-5 and a 20-LED LB-10 pair, as the schema-6 migration sets them up: device 0 and device 1.
            var devices = new List<DeviceSettings>
            {
                KnownDevices.Create(KnownDevices.Dv5, "COM4", 16, true, false),
                KnownDevices.Create(KnownDevices.Lb10, "COM3", brow, true, false)
            };

            // ---------------- every preset in every scenario
            string[] scenarios = { "Racing", "Shift point", "Car left", "Car right", "Three wide", "Pit limiter", "Over pit limit", "Low fuel", "ABS working",
                "Green flag", "Yellow flag", "White flag", "Blue flag", "Black flag", "Checkered flag", "Not connected" };
            foreach (LightPreset preset in LightPresets.All)
            {
                LightProfile profile = preset.Create(DashboardSettings.CreateHalo());
                var renderer = new LightRenderer(profile, "#2DD4BF", units, devices);
                var errors = new List<string>();
                int now = 0;
                foreach (string scenario in scenarios)
                {
                    LightFrame frame = renderer.Compose(LightsWindow.Scenario(scenario), now += 33);
                    errors.AddRange(renderer.Errors.Values);
                    if (frame.Leds.Length != 2 || frame.Leds[0].Length != 16 || frame.Leds[1].Length != brow) errors.Add("bad frame size");
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
                var renderer = new LightRenderer(converted, dashboard.AccentColor, units, devices);
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
                            if (classicDv5[i].ToArgb() != layered.Leds[0][i].ToArgb()) mismatches.Add(state.Item1 + " @" + now + " DV-5 LED " + i + " " + Hex(classicDv5[i]) + "≠" + Hex(layered.Leds[0][i]));
                        for (int i = 0; i < brow; i++)
                            if (classicBrow[i].ToArgb() != layered.Leds[1][i].ToArgb()) mismatches.Add(state.Item1 + " @" + now + " bar LED " + i + " " + Hex(classicBrow[i]) + "≠" + Hex(layered.Leds[1][i]));
                    }
                Check("converted lights match classic: " + dashboard.Name + " (" + dashboard.BrowLeds?.Function + " bar)", mismatches.Count == 0,
                    mismatches.Count + " differences" + (mismatches.Count > 0 ? ": " + string.Join("; ", mismatches.Take(3)) : ""));
            }

            // ---------------- priority and blending
            var refined = new LightRenderer(LightPresets.Refined(), "#2DD4BF", units, devices);
            TelemetrySnapshot leftLowFuel = Snap(0.3, 5000, left: BlindSpotZone.Beside);
            leftLowFuel.FuelPerLapAverage = 13;
            LightFrame f1 = refined.Compose(leftLowFuel, 1000);
            Check("higher layer wins: blind spot over low fuel", f1.Owners[0][1] == "Blind spot" && f1.Owners[0][13] == "Low fuel", f1.Owners[0][1] + " / " + f1.Owners[0][13]);
            LightFrame f2 = refined.Compose(LightsWindow.Scenario("Pit limiter"), 2000);
            Check("replace takes the whole target: pit limiter", Enumerable.Range(4, 8).All(i => f2.Owners[0][i] == "Pit limiter"));
            LightFrame f3 = refined.Compose(LightsWindow.Scenario("Yellow flag"), 3000);
            Check("flags take over the bar", f3.Owners[1].All(o => o == "Flags") && f3.Leds[1].All(c => c.R > 0 && c.B == 0));
            TelemetrySnapshot replay = LightsWindow.Scenario("Checkered flag");
            replay.OutOfCar = true;
            LightFrame dark = new LightRenderer(LightPresets.Refined(), "#2DD4BF", units, devices).Compose(replay.ForLights(), 4000);
            Check("out of the car or in a replay the LEDs go dark", dark.Leds.All(d => d.All(c => (c.R | c.G | c.B) == 0)));

            // ---------------- device setup never writes past a board's LEDs
            var recorder = new RecordingOutput();
            var setupPreview = new LedPreview(new DeviceSettings { Kind = LedDeviceKinds.Arduino, Port = "COM99", LedCount = 16 }, recorder);
            setupPreview.Show(Enumerable.Repeat(Color.White, 16).ToArray());
            System.Threading.Thread.Sleep(150);
            setupPreview.Show(Enumerable.Repeat(Color.White, 3).ToArray());
            System.Threading.Thread.Sleep(150);
            setupPreview.Dispose();
            Check("device setup sends only the LEDs it asks for, and clears only those", recorder.Lengths.SequenceEqual(new[] { 16, 3, 3 }), string.Join(", ", recorder.Lengths));

            // ---------------- a light bar made of two groups (two bars side by side)
            var twoBars = new List<DeviceSettings>
            {
                new DeviceSettings { Id = "bar", Name = "Bar", Kind = LedDeviceKinds.Arduino, LedCount = 20, Groups =
                {
                    new LedGroup { Id = "l", Name = "Left bar", Purpose = LedPurposes.Bar, From = 1, To = 10 },
                    new LedGroup { Id = "r", Name = "Right bar", Purpose = LedPurposes.Bar, From = 11, To = 20, Reverse = true }
                } }
            };
            LedTarget halves = LedTargets.Find("brow-mirror", twoBars);
            var carLeftBar = new LightRenderer(new LightProfile { FadeMs = 0, Layers = { new LightLayer { Name = "Spot", Target = "brow-mirror", Effect = "spotter", Props = { ["style"] = "Whole strip" } } } },
                "#000000", units, twoBars).Compose(Snap(0.3, 5000, left: BlindSpotZone.Beside), 0).Leds[0];
            Check("two bars side by side: a car on the left lights only the left bar", halves.Strips.Count == 2 && halves.Strips[0].Select(l => l.Index).OrderBy(i => i).SequenceEqual(Enumerable.Range(0, 10))
                && halves.Strips[1].Select(l => l.Index).OrderBy(i => i).SequenceEqual(Enumerable.Range(10, 10)) && halves.RightStrips.SequenceEqual(new[] { false, true })
                && carLeftBar.Take(10).All(c => c.R > 0) && carLeftBar.Skip(10).All(c => (c.R | c.G | c.B) == 0));

            // ---------------- without fast updates, only changed LEDs are sent
            Color[] gradient = Enumerable.Range(0, 20).Select(i => Color.FromArgb(i * 10, 200 - i * 10, 0)).ToArray();
            Color[] nextGradient = (Color[])gradient.Clone();
            nextGradient[9] = Color.Red;
            nextGradient[10] = Color.Red;
            var fullRuns = ArduinoLedController.Runs(gradient, null, 20);
            var changedRuns = ArduinoLedController.Runs(nextGradient, gradient, 20);
            Check("a frame of many colours sends only what changed", fullRuns.Count == 20 && changedRuns.Count == 1 && changedRuns[0].Item1 == 9 && changedRuns[0].Item2 == 2,
                fullRuns.Count + " runs for the first frame, " + changedRuns.Count + " for the next");

            // ---------------- device layouts: describe the shape, then click where LEDs are
            var screen = new LedLayout { Shape = LedShapes.Screen, Left = 4, Top = 8, Right = 4 };
            var screenMap = new LayoutMapper(screen);
            screenMap.Place(0);
            int followed = screenMap.Place(1);
            screen.Map = screenMap.Map();
            List<LedGroup> screenGroups = screen.Groups();
            Check("two clicks map a screen's LEDs all the way round", screenMap.Done && followed == 15 && screen.IsMapped
                && screenGroups.Single(g => g.Purpose == LedPurposes.Left).Indexes().SequenceEqual(new[] { 0, 1, 2, 3 })
                && screenGroups.Single(g => g.Purpose == LedPurposes.Shift).Indexes().SequenceEqual(Enumerable.Range(4, 8))
                && screenGroups.Single(g => g.Purpose == LedPurposes.Right).Indexes().SequenceEqual(new[] { 15, 14, 13, 12 }),
                string.Join(" | ", screenGroups.Select(g => g.Name + ": " + string.Join(",", g.Indexes()))));

            var bars = new LedLayout { Shape = LedShapes.Bars, Bars = 2, PerBar = 10, Connected = true };
            var barMap = new LayoutMapper(bars);
            barMap.Place(0); barMap.Place(1);
            bool askedForSecondBar = barMap.Next == 10;
            barMap.Place(10); barMap.Place(11);
            bars.Map = barMap.Map();
            List<LedGroup> barGroups = bars.Groups();
            Check("joined bars map in four clicks and work as one long bar", askedForSecondBar && barMap.Done && barGroups.Count == 1 && barGroups[0].Purpose == LedPurposes.Bar
                && barGroups[0].Indexes().SequenceEqual(Enumerable.Range(0, 20))
                && LedTargets.Find("brow-mirror", new List<DeviceSettings> { new DeviceSettings { Id = "b", LedCount = 20, Layout = bars, Groups = barGroups } }).Strips.Select(s => s.Length).SequenceEqual(new[] { 10, 10 }));
            bars.Connected = false;
            Check("separate bars become a group each", bars.Groups().Count == 2 && bars.Groups()[1].Indexes().SequenceEqual(Enumerable.Range(10, 10)));

            var odd = new LedLayout { Shape = LedShapes.Strip, Length = 3 };
            var oddMap = new LayoutMapper(odd);
            oddMap.Place(2); oddMap.Place(0);
            bool stillAsking = !oddMap.Done;
            oddMap.Place(1);
            var byHand = new LayoutMapper(new LedLayout { Shape = LedShapes.Screen, Left = 4, Top = 8, Right = 4 }, false);
            byHand.Place(0); byHand.Place(1);
            Check("placing by hand never follows the chain", byHand.Next == 2 && byHand.LedAt(1) == 1 && byHand.LedAt(2) == -1);
            Check("wiring that jumps is mapped one LED at a time", stillAsking && oddMap.Done && oddMap.Map().SequenceEqual(new[] { 1, 2, 0 }));

            var loop = new LedLayout { Shape = LedShapes.Screen, Left = 2, Top = 2, Right = 2, Bottom = 2 };
            var loopMap = new LayoutMapper(loop);
            loopMap.Place(7); loopMap.Place(0);
            var undoMap = new LayoutMapper(loop);
            undoMap.Place(7); undoMap.Place(0); undoMap.Undo();
            Check("a loop all the way round wraps, and Undo takes back a click", loopMap.Done && loopMap.LedAt(7) == 0 && loopMap.LedAt(6) == 7 && undoMap.Next == 1 && undoMap.LedAt(0) == -1);

            var stored = new DeviceSettings { Name = "Wheel", LedCount = 16, Layout = screen, Groups = screenGroups };
            stored.Layout.Roles["top"] = LedPurposes.Bar;
            DeviceSettings storedAgain = SettingsStore.Clone(stored);
            Check("a device's layout saves and loads", storedAgain.Mapped && storedAgain.Layout.Left == 4 && storedAgain.Layout.Map.SequenceEqual(screen.Map) && storedAgain.Layout.Roles["top"] == LedPurposes.Bar
                && storedAgain.Groups.All(g => g.Leds != null));
            stored.Layout.Roles.Clear();

            // ---------------- LED colour names
            var namedColours = new LightProfile { FadeMs = 0, Gamma = false, Layers = { new LightLayer { Target = "shift", Effect = "solid", Props = { ["color"] = "bad" } }, new LightLayer { Target = "left", Effect = "solid", Props = { ["color"] = "good" } } } };
            LightFrame namedFrame = new LightRenderer(namedColours, "#000000", units, devices).Compose(Snap(0, 3000), 0);
            Check("on LEDs bad is deep red and good is true green", namedFrame.Leds[0][4] == Color.FromArgb(0x89, 0x07, 0x11) && namedFrame.Leds[0][0] == Color.FromArgb(0, 0x80, 0), Hex(namedFrame.Leds[0][4]) + " / " + Hex(namedFrame.Leds[0][0]));

            // ---------------- power limit
            Color[] allWhite = Enumerable.Repeat(Color.White, 16).ToArray();
            Color[] limited = LedPower.Limit(allWhite, LedPower.DefaultMilliamps);
            Color[] revs = Enumerable.Repeat(Color.FromArgb(230, 10, 15), 8).ToArray();
            Check("a whole strip of white is dimmed to the power limit", LedPower.Milliamps(allWhite) > 900 && LedPower.Milliamps(limited) <= LedPower.DefaultMilliamps && limited[0].R > 0,
                Math.Round(LedPower.Milliamps(allWhite)) + " mA → " + Math.Round(LedPower.Milliamps(limited)) + " mA");
            Check("rev lights at full are left alone by the power limit", ReferenceEquals(LedPower.Limit(revs, LedPower.DefaultMilliamps), revs), Math.Round(LedPower.Milliamps(revs)) + " mA");

            // ---------------- preview on a running dashboard
            PreviewRequest.Write("dashboard-halo", "Shift point");
            var follower = new PreviewRequest();
            TelemetrySnapshot shown = follower.Apply(TelemetrySnapshot.Disconnected);
            TelemetrySnapshot live = Snap(0.2, 4000);
            bool liveWins = ReferenceEquals(follower.Apply(live), live);
            PreviewRequest.Clear();
            var cleared = new PreviewRequest();
            Check("a running dashboard shows the settings preview until iRacing connects", follower.DashboardId == "dashboard-halo" && shown.Connected && DataCatalog.AtShiftPoint(shown) && liveWins
                && !cleared.Apply(TelemetrySnapshot.Disconnected).Connected);

            // ---------------- light log
            var activity = new LightActivity();
            var flagRenderer = new LightRenderer(LightPresets.Refined(), "#2DD4BF", units, devices);
            var logged = new List<LightSpan>();
            for (int ms = 0; ms <= 3000; ms += 100)
                logged.AddRange(activity.Observe(flagRenderer.Compose(ms < 2000 ? LightsWindow.Scenario("Yellow flag") : TelemetrySnapshot.Disconnected, ms), devices, ms / 1000.0, "test"));
            LightSpan flagSpan = logged.FirstOrDefault(s => s.Layer == "Flags");
            Check("the light log records what lit a group and for how long", flagSpan != null && flagSpan.Device == devices[1].Name && Math.Abs(flagSpan.Seconds - 1.9) < 0.01 && flagSpan.Colour == "yellow",
                flagSpan == null ? "nothing logged" : flagSpan.Device + " " + flagSpan.Seconds + "s " + flagSpan.Colour);

            // ---------------- output stage
            var gamma = new LightProfile { Gamma = true, FadeMs = 0, Layers = { new LightLayer { Target = "shift", Effect = "solid", Props = { ["color"] = "#808080" } } } };
            Color encoded = new LightRenderer(gamma, "#000000", units, devices).Compose(Snap(0, 3000), 0).Leds[0][4];
            Check("gamma makes half level look half as bright", Math.Abs(encoded.R - 55) <= 1, "128 → " + encoded.R);
            var fade = new LightProfile { Gamma = false, FadeMs = 300, Layers = { new LightLayer { Target = "shift", Effect = "solid", Condition = "Gear = 4", Props = { ["color"] = "#FF0000" } } } };
            var fader = new LightRenderer(fade, "#000000", units, devices);
            fader.Compose(Snap(0, 3000), 0);
            TelemetrySnapshot neutral = IRacingTelemetrySource.WithPreviewExtras(new TelemetrySnapshot(true, 0, 0, RacingFlag.None, BlindSpotZone.None, BlindSpotZone.None, 0,
                SessionProgressMode.LapRace, 0, 0, 8, 18, 4, 2, 24.3, 58.104, 0.46, "", 0, "", 0, 22, false, 0, 0, 0, new bool[18], 0, 6, 900));
            int during = fader.Compose(neutral, 100).Leds[0][4].R, after = fader.Compose(neutral, 1100).Leds[0][4].R;
            Check("fade out eases off, then reaches dark", during > 10 && during < 255 && after == 0, "after 100 ms " + during + ", after 1.1 s " + after);

            TelemetrySnapshot car = Snap(0.2, 5800);
            Check("rev lights follow the car's shift-light RPMs", Math.Abs(DataCatalog.RevFraction(car) - 0.5) < 0.001, DataCatalog.RevFraction(car).ToString("0.000"));
            Check("shift point from the car's blink RPM", DataCatalog.AtShiftPoint(Snap(0.5, 7960)) && !DataCatalog.AtShiftPoint(Snap(0.99, 7900)));

            var rainbow = Enumerable.Range(0, 16).Select(i => Color.FromArgb(i * 10, 255 - i * 10, i)).ToArray();
            List<byte[]> packets = ArduinoLedController.Payloads(ArduinoLedController.Runs(rainbow, null, 16), ArduinoLedController.MaxRunsPerPacket);
            // A DV-5 takes packets up to 26 bytes: a 21-byte payload plus 5 bytes of framing.
            Check("fast updates keep every packet within what boards accept", packets.Count == 6 && packets.All(p => p.Length <= 21 && p[1] == (byte)'6' && p[p.Length - 1] == 0) && packets[0].Length == 21,
                packets.Count + " packets, largest " + packets.Max(p => p.Length) + " bytes");
            List<byte[]> solid = ArduinoLedController.Payloads(ArduinoLedController.Runs(Enumerable.Repeat(Color.Red, 16).ToArray(), null, 16), ArduinoLedController.MaxRunsPerPacket);
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
            // ---------------- devices and groups
            int[] Leds(LedTarget target, int strip) => target.Strips[strip].Select(l => l.Index).ToArray();
            LedTarget right = LedTargets.Find("right", devices), mirror = LedTargets.Find("brow-mirror", devices), shiftTarget = LedTargets.Find("shift", devices);
            Check("DV-5 profile keeps the old LED order", Leds(shiftTarget, 0).SequenceEqual(Enumerable.Range(4, 8)) && Leds(LedTargets.Find("left", devices), 0).SequenceEqual(new[] { 0, 1, 2, 3 })
                && Leds(right, 0).SequenceEqual(new[] { 15, 14, 13, 12 }) && right.IsRight(0));
            Check("light bar mirrors from the middle outward", mirror.Strips.Count == 2 && Leds(mirror, 0).SequenceEqual(Enumerable.Range(0, 10).Reverse()) && Leds(mirror, 1).SequenceEqual(Enumerable.Range(10, 10))
                && !mirror.IsRight(0) && mirror.IsRight(1) && mirror.Strips.All(s => s.All(l => l.Device == 1)));

            var withExtra = devices.Select(SettingsStore.Clone).ToList();
            withExtra.Add(new DeviceSettings
            {
                Id = "device-desk", Name = "Desk board", Kind = LedDeviceKinds.Arduino, Port = "COM9", LedCount = 12,
                Groups = { LedGroup.Of("Revs", LedPurposes.Shift, 1, 8), LedGroup.Of("Left", LedPurposes.Left, 9, 10), LedGroup.Of("Right", LedPurposes.Right, 11, 12, true) }
            });
            LedTarget sharedShift = LedTargets.Find("shift", withExtra), sides = LedTargets.Find("sides", withExtra);
            Check("a purpose covers every device with it", sharedShift.Strips.Count == 2 && sharedShift.Strips[1].All(l => l.Device == 2) && Leds(sharedShift, 1).SequenceEqual(Enumerable.Range(0, 8)));
            Check("each side strip knows its side across devices", sides.Strips.Count == 4 && sides.RightStrips.SequenceEqual(new[] { false, false, true, true }) && Leds(sides, 3).SequenceEqual(new[] { 11, 10 }));
            LedTarget deskRevs = LedTargets.Find("group:device-desk/group-revs", withExtra);
            Check("a group target lights only its own device", deskRevs != null && deskRevs.Strips.Count == 1 && deskRevs.Strips[0].All(l => l.Device == 2));
            LightFrame shared = new LightRenderer(LightPresets.Refined(), "#2DD4BF", units, withExtra).Compose(Snap(0.9, 7000), 500);
            Check("rev lights reach every device with shift lights", shared.Leds.Length == 3 && shared.Leds[0].Skip(4).Take(8).Any(c => c.ToArgb() != Color.Black.ToArgb())
                && shared.Leds[2].Take(8).Any(c => c.ToArgb() != Color.Black.ToArgb()));
            var disabled = withExtra.Select(SettingsStore.Clone).ToList();
            disabled[2].Enabled = false;
            Check("a device that's off gets nothing", new LightRenderer(LightPresets.Refined(), "#2DD4BF", units, disabled).Compose(Snap(0.9, 7000), 500).Leds[2].All(c => c.ToArgb() == Color.Black.ToArgb() || c.A == 0));

            var reversed = new LedGroup { From = 3, To = 6, Reverse = true };
            Check("groups count from 1 and reverse", reversed.Indexes().SequenceEqual(new[] { 5, 4, 3, 2 }) && new LedGroup { From = 1, To = 3 }.Indexes().SequenceEqual(new[] { 0, 1, 2 }));
            var clamped = new LedGroup { From = 0, To = 99 };
            clamped.Normalize(16);
            Check("groups stay inside the device", clamped.From == 1 && clamped.To == 16);

            var old = new AppSettings { SchemaVersion = 5 };
            old.General.LedPort = "COM4";
            old.General.BrowPort = "COM3";
            old.General.BrowLedCount = 20;
            old.General.BrowEnabled = false;
            old.General.FastLedUpdates = true;
            old.EnsureBuiltInDashboards();
            DeviceSettings dv5 = old.General.Devices.FirstOrDefault(), lb10 = old.General.Devices.Skip(1).FirstOrDefault();
            Check("old DV-5 and LB-10 settings become set-up devices", old.General.Devices.Count == 2 && dv5.Port == "COM4" && dv5.UsbId == DevicePorts.Dv5 && dv5.Groups.Count == 3 && dv5.FastUpdates
                && lb10.Port == "COM3" && lb10.LedCount == 20 && !lb10.Enabled && lb10.Groups.Single().To == 20 && old.General.LedPort == null && old.SchemaVersion == 7);
            var named = new AppSettings { SchemaVersion = 6 };
            named.Dashboards.Add(DashboardSettings.CreateHalo());
            named.Dashboards[0].Lights.Layers.Add(new LightLayer { Name = "Brow rev lights", Target = "brow", Effect = "revs" });
            named.EnsureBuiltInDashboards();
            Check("light layers aren't named after one device", named.Dashboards.SelectMany(d => d.Lights?.Layers ?? new List<LightLayer>()).All(l => !l.Name.StartsWith("Brow"))
                && LightPresets.Refined().Layers.Any(l => l.Name == "Light bar rev lights"));
            var fresh = new AppSettings();
            fresh.EnsureBuiltInDashboards();
            Check("a new install starts with no devices, for setup", fresh.General.Devices.Count == 0);

            var withDevices = new AppSettings { SchemaVersion = 6 };
            withDevices.General.Devices.Add(new DeviceSettings { Kind = LedDeviceKinds.Wled, Address = "192.168.1.40", Name = "Desk strip", LedCount = 60, Groups = { LedGroup.Of("Strip", LedPurposes.Bar, 1, 60, true) } });
            AppSettings reloaded = SettingsStore.Clone(withDevices);
            reloaded.EnsureBuiltInDashboards();
            DeviceSettings again = reloaded.General.Devices.FirstOrDefault();
            Check("LED devices save and load", again != null && again.Kind == LedDeviceKinds.Wled && again.Address == "192.168.1.40" && again.LedCount == 60
                && again.Groups.Count == 1 && again.Groups[0].Reverse && again.Groups[0].To == 60);
            Check("known devices are recognised by USB ID or name", KnownDevices.Find(DevicePorts.Dv5, null) == KnownDevices.Dv5 && KnownDevices.Find(null, "SSG LB-10 v2") == KnownDevices.Lb10
                && KnownDevices.Find("VID_2341&PID_0043", "My LEDs") == null);
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
