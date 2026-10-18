using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.Windows;

namespace AlmostFastDash.CommandLine
{
    internal static class RenderCommands
    {
        /// <summary>Saves a picture of every saved dashboard (in settings.json next to the exe), in each state and on each page.</summary>
        internal static int RenderDashboards(string directory)
        {
            if (!Path.IsPathRooted(directory))
                directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, directory);
            Directory.CreateDirectory(directory);
            AppSettings settings = SettingsStore.Load();
            var units = new UnitSystem(settings.General);
            var states = new[]
            {
                Tuple.Create("", IRacingTelemetrySource.CreatePreviewSnapshot()),
                Tuple.Create(" - Pit lane", IRacingTelemetrySource.CreatePitLanePreviewSnapshot()),
                Tuple.Create(" - Cars alongside", IRacingTelemetrySource.CreateSpotterPreviewSnapshot())
            };
            foreach (DashboardSettings dashboard in settings.Dashboards)
            {
                string name = string.Join("_", dashboard.Name.Split(Path.GetInvalidFileNameChars()));
                foreach (var state in states)
                    using (var frame = DashboardRendererFactory.RenderPreview(dashboard, units, state.Item2))
                        frame.Save(Path.Combine(directory, name + state.Item1 + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                var pages = dashboard.Design?.Screens.Where(s => string.IsNullOrWhiteSpace(s.Condition)).ToList() ?? new List<DashScreen>();
                for (int page = 1; page < pages.Count; page++)
                    using (var renderer = new LayoutRenderer(dashboard.ScreenSize().Width, dashboard.ScreenSize().Height, dashboard, units))
                    {
                        renderer.StepPage(page);
                        using (var frame = renderer.Render(IRacingTelemetrySource.CreatePreviewSnapshot(), false, 4, 1))
                            frame.Save(Path.Combine(directory, name + " - " + pages[page].Name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                    }
            }
            return 0;
        }

        /// <summary>
        /// Saves every flag as the default dashboard shows it a second after it came out (the cloth at a fixed
        /// moment), flags that stay out stepping aside and then as a frame, and every saved dashboard framed
        /// for a caution and the final lap.
        /// </summary>
        /// <summary>
        /// Audits every template and every saved dashboard for text too small or too faint to read at a glance
        /// (see <see cref="DashboardAudit"/>) and writes what it finds. Exits 1 when anything is found.
        /// </summary>
        internal static int AuditDashboards(string reportPath)
        {
            if (!Path.IsPathRooted(reportPath))
                reportPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, reportPath);
            AppSettings settings = SettingsStore.Load();
            var units = new UnitSystem(settings.General);
            var report = new List<string>();
            int total = 0;
            void Audit(string title, DashboardSettings dashboard)
            {
                List<DashboardAudit.Finding> findings = DashboardAudit.Run(dashboard, units);
                total += findings.Count;
                report.Add(title + ": " + (findings.Count == 0 ? "readable" : findings.Count + " to fix"));
                report.AddRange(findings.Select(f => "    " + f));
            }
            foreach (DesignTemplate template in DesignTemplates.All)
                Audit("Template " + template.Name, DashboardSettings.CreateDesigned(template));
            foreach (DashboardSettings dashboard in settings.Dashboards.Where(d => d.Design != null))
                Audit("Saved " + dashboard.Name, dashboard);
            report.Insert(0, total == 0 ? "Every dashboard is readable." : total + " pieces of text to fix.");
            File.WriteAllLines(reportPath, report);
            return total == 0 ? 0 : 1;
        }

        internal static int RenderFlags(string directory)
        {
            if (!Path.IsPathRooted(directory))
                directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, directory);
            Directory.CreateDirectory(directory);
            AppSettings settings = SettingsStore.Load();
            var units = new UnitSystem(settings.General);
            void Save(DashboardSettings dashboard, TelemetrySnapshot snapshot, int moment, string file)
            {
                using (var renderer = new LayoutRenderer(dashboard.ScreenSize().Width, dashboard.ScreenSize().Height, dashboard, units))
                {
                    renderer.FlagClock = 0;
                    renderer.StateHash(snapshot, false, -1);
                    renderer.FlagClock = moment;
                    using (var frame = renderer.Render(snapshot, false, snapshot.Gear, 1))
                        frame.Save(Path.Combine(directory, string.Join("_", file.Split(Path.GetInvalidFileNameChars())) + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            DashboardSettings main = settings.GetDefaultDashboard();
            foreach (RacingFlag flag in Enum.GetValues(typeof(RacingFlag)).Cast<RacingFlag>().Where(f => f != RacingFlag.None))
            {
                TelemetrySnapshot snapshot = IRacingTelemetrySource.CreatePreviewSnapshot(flag);
                Save(main, snapshot, 1000, flag.ToString());
                if (!FlagTimeline.StaysOut(flag)) continue;
                Save(main, snapshot, FlagTimeline.TakeoverMilliseconds + FlagTimeline.StepAside / 2, flag + " stepping aside");
                Save(main, snapshot, FlagTimeline.TakeoverMilliseconds + FlagTimeline.StepAside + 1000, flag + " frame");
            }
            foreach (DashboardSettings dashboard in settings.Dashboards)
            {
                TelemetrySnapshot caution = IRacingTelemetrySource.CreatePreviewSnapshot();
                caution.CautionActive = true;
                Save(dashboard, caution, 1000, dashboard.Name + " - Caution frame");
                TelemetrySnapshot last = IRacingTelemetrySource.CreatePreviewSnapshot();
                last.WhiteFlagActive = true;
                Save(dashboard, last, 1000, dashboard.Name + " - Final lap frame");
            }
            return 0;
        }

        /// <summary>Writes a PNG of every designer template in racing, pit-lane and spotter states.</summary>
        internal static int RenderTemplates(string directory)
        {
            if (!Path.IsPathRooted(directory))
                directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, directory);
            Directory.CreateDirectory(directory);
            var units = new UnitSystem(SettingsStore.Load().General);
            var states = new[]
            {
                Tuple.Create("", IRacingTelemetrySource.CreatePreviewSnapshot()),
                Tuple.Create(" - Pit lane", IRacingTelemetrySource.CreatePitLanePreviewSnapshot()),
                Tuple.Create(" - Cars alongside", IRacingTelemetrySource.CreateSpotterPreviewSnapshot())
            };
            foreach (DesignTemplate template in DesignTemplates.All)
            {
                DashboardSettings dashboard = DashboardSettings.CreateDesigned(template);
                foreach (var state in states)
                    using (var frame = DashboardRendererFactory.RenderPreview(dashboard, units, state.Item2))
                        frame.Save(Path.Combine(directory, template.Name + state.Item1 + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                // Pages beyond the first, as a wheel button shows them.
                var pages = dashboard.Design.Screens.Where(s => string.IsNullOrWhiteSpace(s.Condition)).ToList();
                for (int page = 1; page < pages.Count; page++)
                    using (var renderer = new LayoutRenderer(dashboard.ScreenSize().Width, dashboard.ScreenSize().Height, dashboard, units))
                    {
                        renderer.StepPage(page);
                        using (var frame = renderer.Render(IRacingTelemetrySource.CreatePreviewSnapshot(), false, 4, 1))
                            frame.Save(Path.Combine(directory, template.Name + " - " + pages[page].Name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                    }
            }

            // The flagships' second views, as a tap on each glass panel shows them.
            foreach (DesignTemplate template in new[] { DesignTemplates.Road, DesignTemplates.Oval })
            {
                DashboardSettings dashboard = DashboardSettings.CreateDesigned(template);
                using (var renderer = new LayoutRenderer(854, 480, dashboard, units))
                {
                    foreach (DashWidget zone in dashboard.Design.Screens[0].Widgets.Where(w => !string.IsNullOrWhiteSpace(w.Get("tapName", ""))))
                        renderer.Tap(zone.X + zone.W / 2, zone.Y + zone.H / 2);
                    using (var frame = renderer.Render(IRacingTelemetrySource.CreatePreviewSnapshot(), false, 4, 1))
                        frame.Save(Path.Combine(directory, template.Name + " - Tapped.png"), System.Drawing.Imaging.ImageFormat.Png);
                }
            }

            // The flagships made for other screen sizes.
            foreach (DesignTemplate template in new[] { DesignTemplates.Road, DesignTemplates.Oval })
                foreach (ScreenSizes.Preset size in ScreenSizes.Presets.Where(p => p.Width != ScreenSizes.DefaultWidth || p.Height != ScreenSizes.DefaultHeight))
                {
                    DashboardSettings sized = DashboardSettings.CreateDesigned(template, size.Width, size.Height);
                    using (var frame = DashboardRendererFactory.RenderPreview(sized, units))
                        frame.Save(Path.Combine(directory, "Size " + size.Width + "x" + size.Height + " - " + template.Name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                }

            // A sheet of the shaped gauges, at cruising revs and at the shift point.
            DashLayout sheet = DesignTemplates.Blank();
            void Place(string block, double x, double y, Action<DashWidget> adjust = null)
            {
                List<DashWidget> widgets = DesignBlocks.BuiltIn().First(b => b.Name == block).Create();
                foreach (DashWidget w in widgets) { w.X += x; w.Y += y; adjust?.Invoke(w); sheet.Screens[0].Widgets.Add(w); }
            }
            Place("Swoosh rev bar", 10, 10, w => { w.W = 400; w.H = 110; });
            Place("Slanted rev bar", 440, 46, w => w.W = 400);
            Place("Arched rev bar", 10, 128, w => { w.W = 830; w.H = 50; });
            Place("Tach dial", 10, 186, w => { if (w.Type == "arc") w.Set("taper", "60"); });
            Place("Tapered rev ring", 320, 186, w => { if (w.Type == "arc") w.Set("oval", "true"); });
            sheet.Screens[0].Widgets.Add(new DashWidget
            {
                Type = "bar", Name = "Bent vertical", X = 650, Y = 190, W = 80, H = 280,
                Props = { ["value"] = "=RevPct", ["orientation"] = "Vertical", ["bend"] = "-60", ["taper"] = "70", ["thickness"] = "40", ["colorMode"] = "Gradient", ["mode"] = "Segments", ["segments"] = "16", ["radius"] = "0" }
            });
            sheet.Screens[0].Widgets.Add(new DashWidget
            {
                Type = "bar", Name = "Pinched", X = 750, Y = 190, W = 90, H = 280,
                Props = { ["value"] = "=RevPct", ["orientation"] = "Vertical", ["bulge"] = "-70", ["skew"] = "-10", ["colorMode"] = "Shift colors" }
            });
            // And one of the race-data quick values: leaderboard, tires, sectors, an opponent card and a notice.
            DashLayout race = DesignTemplates.Blank();
            void Quick(string name, double x, double y, Action<DashWidget> adjust = null)
            {
                foreach (DashWidget w in QuickValues.All().First(b => b.Name == name).Create()) { w.X += x; w.Y += y; adjust?.Invoke(w); race.Screens[0].Widgets.Add(w); }
            }
            Quick("Leaderboard", 10, 10, w => { w.H = 330; w.Set("showIRating", "true"); });
            Quick("Tires: all readings", 340, 10);
            Quick("Car ahead in the race", 340, 240);
            Quick("Best sector times", 10, 350, w => { if (w.Name == "Ideal lap") { w.X = 10 + 360; } });
            Quick("Oil temperature", 610, 10, w => w.W = 230);
            Quick("Oil pressure", 610, 90, w => w.W = 230);
            Quick("CPU load", 610, 170, w => w.W = 230);
            Quick("Race notices", 420, 410, w => { w.Set("visible", ""); if (w.Type == "text") w.Set("text", "PERSONAL BEST 1:27.521"); else w.Set("tint", "good"); if (w.Type == "text") w.Set("color", "good"); });
            using (var frame = DashboardRendererFactory.RenderPreview(new DashboardSettings { Layout = Layouts.Custom, Design = race, AccentColor = "#22D3EE" }, units, IRacingTelemetrySource.CreatePreviewSnapshot()))
                frame.Save(Path.Combine(directory, "Race data.png"), System.Drawing.Imaging.ImageFormat.Png);

            // The Style effects on a few widgets.
            DashLayout effects = DesignTemplates.Blank();
            void Add(string type, double x, double y, double w, double h, params string[] props)
            {
                DashWidget widget = WidgetCatalog.Create(type, x, y);
                widget.W = w;
                widget.H = h;
                for (int i = 0; i + 1 < props.Length; i += 2) widget.Set(props[i], props[i + 1]);
                effects.Screens[0].Widgets.Add(widget);
            }
            Add("panel", 30, 30, 360, 200, "fxShadow", "14", "fxShadowY", "10", "fxFill", "Gradient", "fxFillColor", "#1E293B", "fxFillColor2", "#0EA5E9", "fxFillAngle", "60", "fxFillAmount", "55");
            Add("gear", 120, 40, 180, 170, "fxFill", "Gradient", "fxFillColor", "#FFFFFF", "fxFillColor2", "accent", "fxShadow", "6", "fxShadowY", "6", "neighbors", "false");
            Add("text", 430, 40, 390, 80, "text", "OUTLINE", "size", "56", "color", "#FACC15", "fxOutline", "4", "fxOutlineColor", "#000000", "fxGlow", "14", "fxGlowColor", "#FACC15");
            Add("metric", 430, 140, 390, 90, "label", "DELTA", "value", "=signed(Delta, 2)", "valueSize", "44", "valueColor", "good", "fxGlow", "10", "fxGlowColor", "good", "rotate", "-4");
            Add("bar", 30, 280, 790, 40, "value", "0.72", "mode", "Segments", "segments", "30", "colorMode", "Gradient", "fxOutline", "1.5", "fxOutlineColor", "#FFFFFF", "fxShadow", "8", "fxShadowY", "5");
            Add("arc", 300, 330, 260, 150, "style", "Smooth", "start", "180", "sweep", "180", "oval", "true", "thickness", "16", "fxGlow", "16", "fxGlowColor", "#A855F7", "fxFill", "Color", "fxFillColor", "#A855F7", "fxFillAmount", "60");
            using (var frame = DashboardRendererFactory.RenderPreview(new DashboardSettings { Layout = Layouts.Custom, Design = effects, AccentColor = "#22D3EE" }, units, IRacingTelemetrySource.CreatePreviewSnapshot()))
                frame.Save(Path.Combine(directory, "Style effects.png"), System.Drawing.Imaging.ImageFormat.Png);

            var gauges = new DashboardSettings { Layout = Layouts.Custom, Design = sheet, AccentColor = "#22D3EE" };
            foreach (var state in new[] { Tuple.Create("", IRacingTelemetrySource.CreatePreviewSnapshot()), Tuple.Create(" - Shift point", LightsWindow.Scenario("Shift point")) })
                using (var frame = DashboardRendererFactory.RenderPreview(gauges, units, state.Item2))
                    frame.Save(Path.Combine(directory, "Gauge shapes" + state.Item1 + ".png"), System.Drawing.Imaging.ImageFormat.Png);
            return 0;
        }
    }
}
