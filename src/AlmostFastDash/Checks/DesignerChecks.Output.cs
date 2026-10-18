using System;
using System.Collections.Generic;
using System.Linq;

namespace AlmostFastDash.Designer
{
    public partial class DesignerWindow
    {
        /// <summary>Checks: the dashboard on the PC.</summary>
        private void CheckDashboardOnPc()
        {
            {
                // Where the window and the overlay sit: saved per mode, kept on a display, shaped to the dashboard.
                var work = new System.Drawing.Rectangle(0, 0, 3440, 1392);
                var displays = new[] { new System.Drawing.Rectangle(0, 0, 3440, 1440) };
                var dash = new System.Drawing.Size(854, 480);
                string places = WindowPlacement.Merge(WindowPlacement.Merge("", "Window", new System.Drawing.Rectangle(10, 20, 854, 480)), "Overlay", new System.Drawing.Rectangle(1398, 961, 644, 362));
                places = WindowPlacement.Merge(places, "Window", new System.Drawing.Rectangle(30, 40, 1114, 626));
                Check("window places: each mode saved on its own line, the newest kept",
                    WindowPlacement.Parse(places, "Window") == new System.Drawing.Rectangle(30, 40, 1114, 626) &&
                    WindowPlacement.Parse(places, "Overlay") == new System.Drawing.Rectangle(1398, 961, 644, 362) &&
                    WindowPlacement.Parse(places, "FullScreen") == null && places.Split('\n').Count(l => l.Length > 0) == 2, places.Replace("\n", " | "));
                System.Drawing.Rectangle overlay = WindowPlacement.Resolve(null, "Overlay", dash, displays, work);
                System.Drawing.Rectangle offScreen = WindowPlacement.Resolve(new System.Drawing.Rectangle(5000, 200, 800, 450), "Window", dash, displays, work);
                System.Drawing.Rectangle reshaped = WindowPlacement.Resolve(new System.Drawing.Rectangle(100, 100, 800, 800), "Window", dash, displays, work);
                Check("window places: over the game starts low in the middle, a place off every display starts over, and another dashboard keeps the width",
                    overlay == new System.Drawing.Rectangle(1398, 961, 644, 362) && offScreen == WindowPlacement.Default("Window", 854 / 480.0, work) &&
                    reshaped == new System.Drawing.Rectangle(100, 100, 800, 450), overlay + " " + offScreen + " " + reshaped);
                DateTime now = DateTime.UtcNow;
                Check("over the game: unlocked only while the settings window keeps asking", OverlayUnlock.IsFresh(now.AddSeconds(-1), now) && !OverlayUnlock.IsFresh(now.AddSeconds(-4), now));
                Check("dashboard on the PC: settings from before keep their full-screen display",
                    new GeneralSettings { MonitorDevice = @"\\.\DISPLAY2" }.EffectivePcOutput() == "FullScreen" && new GeneralSettings().EffectivePcOutput() == "Off" &&
                    new GeneralSettings { PcOutput = "Overlay", MonitorDevice = @"\\.\DISPLAY2" }.EffectivePcOutput() == "Overlay");
                // Show when: where the driver is, from iRacing's own flags, and whether the dashboard shows there.
                Check("show when: in the car, out of it, in the garage, or watching another driver",
                    IRacingTelemetrySource.CarStateOf(true, false, false, 3, 3) == CarState.InCar &&
                    IRacingTelemetrySource.CarStateOf(false, false, false, 3, 3) == CarState.OutOfCar &&
                    IRacingTelemetrySource.CarStateOf(false, false, false, -1, 3) == CarState.OutOfCar &&
                    IRacingTelemetrySource.CarStateOf(false, true, false, 3, 3) == CarState.Garage &&
                    IRacingTelemetrySource.CarStateOf(false, false, false, 7, 3) == CarState.Spectating &&
                    IRacingTelemetrySource.CarStateOf(true, false, true, 7, 3) == CarState.Spectating &&
                    IRacingTelemetrySource.CarStateOf(true, false, true, 3, 3) == CarState.OutOfCar);
                var onlyInCar = new GeneralSettings { ShowOutOfCar = false, ShowInGarage = false, ShowSpectating = false };
                TelemetrySnapshot live = IRacingTelemetrySource.CreatePreviewSnapshot();
                live.IsPreview = false;
                live.CarState = CarState.Garage;
                TelemetrySnapshot driving = IRacingTelemetrySource.CreatePreviewSnapshot();
                driving.IsPreview = false;
                Check("show when: hidden only where it's turned off, never before iRacing runs or in a preview",
                    !DashboardRunner.Shown(live, onlyInCar) && DashboardRunner.Shown(driving, onlyInCar) && DashboardRunner.Shown(live, new GeneralSettings()) &&
                    DashboardRunner.Shown(TelemetrySnapshot.Disconnected, onlyInCar) && DashboardRunner.Shown(IRacingTelemetrySource.CreatePreviewSnapshot(), onlyInCar));
                Check("wheel screen: see-through pixels show over black", FrameEncoder.Rgb565(0) == 0 && FrameEncoder.Rgb565(unchecked((int)0x80FFFFFF)) == FrameEncoder.Rgb565(unchecked((int)0xFF808080)) &&
                    FrameEncoder.Rgb565(unchecked((int)0xFF3366CC)) == (ushort)((0x33 >> 3) << 11 | (0x66 >> 2) << 5 | (0xCC >> 3)));

                // A see-through background: nothing drawn where no widget is.
                DashboardSettings clear = DashboardSettings.CreateDesigned(DesignTemplates.All.First());
                clear.Design.Theme.BackgroundStyle = "None";
                using (var renderer = new LayoutRenderer(854, 480, clear, _units) { FlagClock = 0, FixedNow = TestNow })
                using (System.Drawing.Bitmap frame = renderer.Render(IRacingTelemetrySource.CreatePreviewSnapshot(), false, 4, 1))
                {
                    int clearPixels = 0, solid = 0;
                    for (int y = 0; y < frame.Height; y += 8)
                        for (int px = 0; px < frame.Width; px += 8)
                        {
                            int alpha = frame.GetPixel(px, y).A;
                            if (alpha == 0) clearPixels++;
                            else if (alpha == 255) solid++;
                        }
                    Check("a None background is see-through between the widgets", clearPixels > 100 && solid > 100, clearPixels + " clear, " + solid + " solid");
                }

                // The one shared output, copied into only where things changed, must match a whole frame every time,
                // also after a flag was drawn over it and has gone.
                DashboardSettings roady = DashboardSettings.CreateDesigned(DesignTemplates.All.First());
                double clock = 0;
                var lap = new SyntheticTelemetry { Clock = () => clock };
                TelemetrySnapshot yellow = IRacingTelemetrySource.CreatePreviewSnapshot(RacingFlag.Yellow);
                int differ = 0, quiet = 0, frames = 0;
                bool changedInside = true;
                using (var shared = new LayoutRenderer(854, 480, roady, _units) { SharedOutput = true, ReuseFingerprints = true, FlagClock = 0, FixedNow = TestNow })
                using (var whole = new LayoutRenderer(854, 480, roady, _units) { FlagClock = 0, FixedNow = TestNow })
                    for (int i = 0; i < 40; i++)
                    {
                        clock = i / 30.0;
                        TelemetrySnapshot snapshot = i >= 15 && i < 20 ? yellow : lap.Next(out _, out _, out _);
                        shared.StateHash(snapshot, false, -1);
                        System.Drawing.Bitmap a = shared.Render(snapshot, false, snapshot.Gear, 1);
                        using (System.Drawing.Bitmap b = whole.Render(snapshot, false, snapshot.Gear, 1))
                            if (Pixels(a) != Pixels(b)) differ++;
                        System.Drawing.Rectangle changed = shared.LastChanged;
                        changedInside &= changed.IsEmpty || new System.Drawing.Rectangle(0, 0, 854, 480).Contains(changed);
                        frames++;
                        // The same state again: nothing changed.
                        if (i == 25)
                        {
                            shared.StateHash(snapshot, false, -1);
                            shared.Render(snapshot, false, snapshot.Gear, 1);
                            if (shared.LastChanged.IsEmpty) quiet++;
                        }
                    }
                Check("shared output copied only where things changed matches a whole frame, flags and all", differ == 0 && quiet == 1 && changedInside,
                    differ + " of " + frames + " frames differ; unchanged frame reported empty: " + (quiet == 1));
            }
        }

        /// <summary>Checks: touch on the wheel screen, and sending frames to it.</summary>
        private void CheckTouch()
        {
            {
                // Reports the wheel screen sent while it was tapped at its top-left, top-right and bottom-right, then lifted.
                byte[] Report(params byte[] start) => start.Concat(new byte[] { 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF }).ToArray();
                WheelScreen.ReadTouch(Report(0, 0, 1, 0x80, 0x48, 0x03, 0x28), 14, ScreenModel.FiveInch, out bool downA, out System.Drawing.Point topLeft);
                WheelScreen.ReadTouch(Report(0, 0, 1, 0x80, 0x2E, 0x00, 0x2D), 14, ScreenModel.FiveInch, out bool downB, out System.Drawing.Point topRight);
                WheelScreen.ReadTouch(Report(0, 0, 1, 0x81, 0xB4, 0x00, 0x25), 14, ScreenModel.FiveInch, out bool downC, out System.Drawing.Point bottomRight);
                WheelScreen.ReadTouch(Report(0, 0, 0, 0x40, 0x48, 0x03, 0x28), 14, ScreenModel.FiveInch, out bool lifted, out _);
                Check("touch reports read as the corners that were tapped", downA && downB && downC && !lifted &&
                    topLeft == new System.Drawing.Point(45, 72) && topRight == new System.Drawing.Point(808, 46) && bottomRight == new System.Drawing.Point(816, 436),
                    topLeft + " " + topRight + " " + bottomRight);
                // A landscape panel's touches need no turning.
                WheelScreen.ReadTouch(Report(0, 0, 1, 0x80, 0x48, 0x00, 0x28), 14, ScreenModel.For(0x7, 0), out bool downWide, out System.Drawing.Point wide);
                Check("touches on a landscape panel read straight across", downWide && wide == new System.Drawing.Point(72, 40), wide.ToString());

                // ---------------- sending to the screen
                var random = new Random(5);
                int[] Picture(int w, int h)
                {
                    var pixels = new int[w * h];
                    for (int i = 0; i < pixels.Length; i++) pixels[i] = random.Next() | unchecked((int)0xFF000000);
                    return pixels;
                }
                ScreenModel five = ScreenModel.FiveInch;
                int[] picture = Picture(five.Width, five.Height);
                // The original whole-frame conversion, pixel by pixel: RGB565, turned a quarter onto the portrait panel.
                var reference = new byte[five.Width * five.Height * 2];
                for (int row = 0; row < five.Height; row++)
                    for (int column = 0; column < five.Width; column++)
                    {
                        int c = picture[row * five.Width + column];
                        int pixel = (((c >> 16) & 0xFF) >> 3 << 11) | (((c >> 8) & 0xFF) >> 2 << 5) | ((c & 0xFF) >> 3);
                        int index = ((five.Width - 1 - column) * five.Height + row) * 2;
                        reference[index] = (byte)pixel;
                        reference[index + 1] = (byte)(pixel >> 8);
                    }
                var encoded = new byte[five.Width * five.Height * 2 + five.FramePadding];
                FrameEncoder.Encode(picture, five.Width, new System.Drawing.Rectangle(0, 0, five.Width, five.Height), five, encoded);
                Check("a whole frame encodes exactly as the screen takes it", reference.SequenceEqual(encoded.Take(reference.Length)));

                // Any part, put where Encode says it goes on the panel, matches the whole frame there.
                bool PartsLand(ScreenModel model)
                {
                    int[] pixels = Picture(model.Width, model.Height);
                    var whole = new byte[model.PanelWidth * model.PanelHeight * 2];
                    FrameEncoder.Encode(pixels, model.Width, new System.Drawing.Rectangle(0, 0, model.Width, model.Height), model, whole);
                    var part = new byte[whole.Length];
                    foreach (System.Drawing.Rectangle area in new[] { new System.Drawing.Rectangle(0, 0, 16, 16), new System.Drawing.Rectangle(37, 5, 101, 64), new System.Drawing.Rectangle(model.Width - 48, model.Height - 32, 48, 32) })
                    {
                        System.Drawing.Rectangle panel = FrameEncoder.Encode(pixels, model.Width, area, model, part);
                        if (panel.Width * panel.Height != area.Width * area.Height) return false;
                        for (int row = 0; row < panel.Height; row++)
                            for (int column = 0; column < panel.Width * 2; column++)
                                if (part[row * panel.Width * 2 + column] != whole[((panel.Y + row) * model.PanelWidth + panel.X) * 2 + column]) return false;
                    }
                    return true;
                }
                Check("parts of a frame land where they belong on portrait and landscape panels",
                    PartsLand(five) && PartsLand(ScreenModel.For(0x7, 0)) && PartsLand(ScreenModel.For(0x1005, 0)) && PartsLand(ScreenModel.For(0x403, 0)));

                // Nothing changed; two small changes far apart; most of the screen.
                int[] shown = (int[])picture.Clone(), next = (int[])picture.Clone();
                bool nothing = FrameEncoder.Changes(next, shown, five.Width, five.Height).Count == 0;
                next[10 * five.Width + 20] ^= 1;
                next[400 * five.Width + 700] ^= 1;
                List<System.Drawing.Rectangle> two = FrameEncoder.Changes(next, shown, five.Width, five.Height);
                bool small = two.Count == 2 && two.Any(r => r.Contains(20, 10) && r.Width * r.Height <= 256) && two.Any(r => r.Contains(700, 400) && r.Width * r.Height <= 256);
                for (int i = 0; i < next.Length * 3 / 4; i++) next[i] ^= 1;
                List<System.Drawing.Rectangle> most = FrameEncoder.Changes(next, shown, five.Width, five.Height);
                Check("only what changed is sent, and most of the screen goes as a whole frame",
                    nothing && small && most.Count == 1 && most[0] == new System.Drawing.Rectangle(0, 0, five.Width, five.Height), string.Join(" ", two));

                // Every M-PRO model a screen can report.
                ScreenModel oled = ScreenModel.For(0x1005, 0), wide68 = ScreenModel.For(0x7, 0), ten = ScreenModel.For(0xA, 0), newer = ScreenModel.For(0x99, 0);
                Check("M-PRO models: their sizes, which way the picture goes, and the 5\"'s padding",
                    five.Width == 854 && five.Height == 480 && five.Turned && ScreenModel.For(0x5, 0).FramePadding == 320 && ScreenModel.For(0x5, 3).FramePadding == 0 &&
                    oled.Width == 1280 && oled.Height == 720 && oled.Turned && !oled.WholeFrameCommand &&
                    wide68.Width == 800 && wide68.Height == 480 && !wide68.Turned && ten.Width == 1024 && ten.Height == 600 &&
                    newer.Width == 800 && newer.Height == 480 && ScreenModel.For(0xFFFFFFFF, 0) == five);

                DashboardSettings roady = DashboardSettings.CreateDesigned(DesignTemplates.Road);
                using (var renderer = new LayoutRenderer(854, 480, roady, _units))
                {
                    TelemetrySnapshot snapshot = IRacingTelemetrySource.CreatePreviewSnapshot();
                    DashWidget fuelPanel = roady.Design.Screens[0].Widgets.First(w => w.Name == "Fuel panel");
                    int before = renderer.StateHash(snapshot, false, -1);
                    using (renderer.Render(snapshot, false, 4, 1)) { }
                    bool hit = renderer.Tap(fuelPanel.X + 20, fuelPanel.Y + 20, snapshot);
                    int after = renderer.StateHash(snapshot, false, -1);
                    bool missed = !renderer.Tap(427, 150, snapshot);
                    renderer.Tap(fuelPanel.X + 20, fuelPanel.Y + 20, snapshot);
                    Check("a tap on a glass panel moves it to its next view and back, and a tap elsewhere does nothing",
                        hit && missed && before != after && renderer.Taps.TryGetValue("fuel", out int view) && view == 0);
                }

                var saved = new AppSettings { SchemaVersion = 11 };
                DashboardSettings oldRoady = DashboardSettings.CreateRoad(), oldRoundy = DashboardSettings.CreateOval();
                foreach (DashboardSettings d in new[] { oldRoady, oldRoundy })
                    foreach (DashWidget w in d.Design.AllWidgets().ToList())
                    {
                        if (!string.IsNullOrEmpty(w.Get("tapName", ""))) { w.Props.Remove("tapName"); w.Props.Remove("tapViews"); }
                        if ((w.Get("visible", "") ?? "").Contains("tap(")) d.Design.Screens.ForEach(s => s.Widgets.Remove(w));
                    }
                foreach (DashScreen s in oldRoady.Design.Screens.Concat(oldRoundy.Design.Screens))
                    foreach (DashWidget w in s.Widgets) if ((w.Get("visible", "") ?? "").Contains("tap(")) w.Props.Remove("visible");
                saved.Dashboards.Add(oldRoady);
                saved.Dashboards.Add(oldRoundy);
                int widgets = oldRoady.Design.AllWidgets().Count();
                saved.EnsureBuiltInDashboards();
                saved.EnsureBuiltInDashboards();
                Check("saved flagships gain their tap views once", oldRoady.Design.AllWidgets().Count(w => !string.IsNullOrEmpty(w.Get("tapName", ""))) == 3 &&
                    oldRoundy.Design.AllWidgets().Count(w => !string.IsNullOrEmpty(w.Get("tapName", ""))) == 1 && oldRoady.Design.AllWidgets().Count() == widgets + 8,
                    widgets + " -> " + oldRoady.Design.AllWidgets().Count());
            }
        }

        /// <summary>Checks: screen sizes.</summary>
        private void CheckScreenSizes()
        {
            {
                DashLayout sized = DesignTemplates.Blank();
                DashWidget label = WidgetCatalog.Create("text", 427, 240);
                label.W = 100; label.H = 50;
                label.Set("size", "20");
                label.Set("color", "=Speed > 100 ? $bad : $text");
                DashWidget bar = WidgetCatalog.Create("bar", 0, 0);
                bar.Set("radius", "-1");
                bar.Set("thickness", "60");
                sized.Screens[0].Widgets.Add(label);
                sized.Screens[0].Widgets.Add(bar);
                sized.ScaleTo(1280, 720);
                Check("a design moves to another screen size, scaling what's on it", sized.Width == 1280 && sized.Height == 720 &&
                    label.X == 640 && label.Y == 360 && label.W == 150 && label.H == 75 && label.Get("size") == "30" && label.Get("color").StartsWith("=") &&
                    bar.Get("radius") == "-1" && bar.Get("thickness") == "60", label.X + "," + label.Y + " " + label.W + "x" + label.H + " size " + label.Get("size"));

                DashboardSettings wide = DashboardSettings.CreateDesigned(DesignTemplates.Oval, 1024, 600);
                using (var renderer = new LayoutRenderer(wide.ScreenSize().Width, wide.ScreenSize().Height, wide, _units))
                using (System.Drawing.Bitmap frame = renderer.Render(IRacingTelemetrySource.CreatePreviewSnapshot(), false, 4, 1))
                using (System.Drawing.Bitmap fitted = ScreenSizes.Fit(frame, 854, 480))
                    Check("a dashboard is drawn at its own size and fitted to a screen of another", frame.Width == 1024 && frame.Height == 600 && fitted.Width == 854 && fitted.Height == 480
                        && renderer.Errors.Count == 0, string.Join("; ", renderer.Errors.Values));

                // The designer's canvas follows the design's size, and back again on undo.
                int before = _frame.PixelWidth;
                _layout.ScaleTo(1024, 600);
                Commit("Self-test resize");
                RenderNow();
                bool followed = _frame.PixelWidth == 1024 && _frame.PixelHeight == 600 && Math.Abs(Overlay.Width - 1024) < 0.1;
                Undo_Click(null, null);
                RenderNow();
                Check("the designer's canvas follows the screen size, and undo puts it back", followed && _frame.PixelWidth == before && _layout.Width == before, _frame.PixelWidth + "x" + _frame.PixelHeight);
            }
        }
    }
}
