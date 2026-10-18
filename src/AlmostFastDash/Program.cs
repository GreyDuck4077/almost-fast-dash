using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.Windows;

namespace AlmostFastDash
{
    internal static class Program
    {
        /// <summary>How often the screen is checked while iRacing isn't running; the waiting screen barely changes.</summary>
        private const int IdleRefreshMilliseconds = 250;
        private const int GearTransitionMilliseconds = 420;
        private const int GearTransitionFrames = 14;
        private static readonly ManualResetEventSlim StopRequested = new ManualResetEventSlim(false);
        // These names predate the rename to Almost Fast Dash and stay, so a new exe can still stop an old one.
        private static readonly EventWaitHandle StopSignal = new EventWaitHandle(
            false, EventResetMode.AutoReset, @"Local\AlmostFastOverlays.StopRequested");
        private static readonly string StopRequestPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "stop.request");
        internal static readonly string DashboardStatusPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "dashboard.running");
        private static Mutex _instanceMutex;

        internal static readonly string ActiveDashboardPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "dashboard.active");

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Contains("--stop"))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StopRequestPath));
                File.WriteAllText(StopRequestPath, DateTime.UtcNow.ToString("O"));
                StopSignal.Set();
                return 0;
            }


            string libraryDirectory = ParseOption(args, "--render-library");
            if (libraryDirectory != null) return LibraryAudit.Run(libraryDirectory);
            string templateDirectory = ParseOption(args, "--render-templates");
            if (templateDirectory != null) return RenderTemplates(templateDirectory);
            string dashboardDirectory = ParseOption(args, "--render-dashboards");
            if (dashboardDirectory != null) return RenderDashboards(dashboardDirectory);
            string flagDirectory = ParseOption(args, "--render-flags");
            if (flagDirectory != null) return RenderFlags(flagDirectory);
            string auditReport = ParseOption(args, "--audit-dashboards");
            if (auditReport != null) return AuditDashboards(auditReport);
#if CHECKS
            string selfTestReport = ParseOption(args, "--designer-selftest");
            if (selfTestReport != null) return Checks.CheckRunner.Run(selfTestReport);
#endif
            string soakPort = ParseOption(args, "--led-soak");
            if (soakPort != null) return LedSoak(soakPort, int.TryParse(ParseOption(args, soakPort), out int soakLeds) ? soakLeds : 16, args.Contains("--fast"));
            string lightReport = ParseOption(args, "--light-frames");
            if (lightReport != null) return LightFrames.Run(lightReport);
            string recording = ParseOption(args, "--replay-ibt");
            if (recording != null) return RecordingReplay.Run(recording, ParseOption(args, recording) ?? "replay.txt");
            string controllerReport = ParseOption(args, "--controllers");
            if (controllerReport != null)
            {
                // Lists game controllers Windows sees, and the buttons held on each, for checking page buttons.
                var lines = WheelButtons.Read().Select(d => d.Key + ": buttons held 0x" + d.Value.ToString("X8"));
                File.WriteAllLines(controllerReport, lines.DefaultIfEmpty("No game controllers found."));
                return 0;
            }
            string profileReport = ParseOption(args, "--profile-dashboards");
            if (profileReport != null) return DashboardProfiler.Run(profileReport);
            // A test pattern on the wheel screen through the dashboard's own sending code, with an optional report.
            if (args.Contains("--screen-test")) return ScreenTest.Run(ParseOption(args, "--screen-test"));
            string liveReport = ParseOption(args, "--live-check");
            if (liveReport != null) return LiveCheck.Run(liveReport, int.TryParse(ParseOption(args, liveReport), out int liveSeconds) ? liveSeconds : 20);
            string uiScenario = ParseOption(args, "--ui-snapshot");
            if (uiScenario != null) return UiSnapshot(uiScenario, ParseOption(args, uiScenario) ?? "ui.png");

            if (!args.Contains("--run"))
            {
                var application = new Application
                {
                    ShutdownMode = ShutdownMode.OnMainWindowClose
                };
                application.DispatcherUnhandledException += (sender, e) =>
                {
                    // Keep the settings window alive and leave a trace instead of vanishing.
                    try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings-error.txt"), DateTime.Now + Environment.NewLine + e.Exception + Environment.NewLine); }
                    catch { }
                    MessageBox.Show(e.Exception.Message, "Almost Fast Dash", MessageBoxButton.OK, MessageBoxImage.Warning);
                    e.Handled = true;
                };
                application.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/AlmostFastDash;component/UI/Theme.xaml")
                });
                DarkTitleBar.ForEveryWindow();
                application.Run(new SettingsWindow());
                return 0;
            }

            if (File.Exists(StopRequestPath)) File.Delete(StopRequestPath);

            _instanceMutex = new Mutex(true, @"Local\AlmostFastOverlays.SingleInstance", out bool ownsMutex);
            if (!ownsMutex)
            {
                Console.Error.WriteLine("Almost Fast Dash is already running.");
                _instanceMutex.Dispose();
                return 2;
            }

            // A stopping dashboard may consume stop.request before consuming the
            // named event. Once this process owns the single-instance mutex, any
            // remaining signal is stale and must not stop the replacement run.
            while (StopSignal.WaitOne(0)) { }
            File.WriteAllText(DashboardStatusPath, Process.GetCurrentProcess().Id.ToString());

            Console.WriteLine("Almost Fast Dash");
            Console.WriteLine("Close other dashboard software before running so it does not compete for the devices.");

            Console.CancelKeyPress += OnCancelKeyPress;
            // The dashboard runs unattended in the background: anything unexpected is written down before it ends.
            AppDomain.CurrentDomain.UnhandledException += (s, e) => WriteCrash(e.ExceptionObject as Exception);

            var runtime = new DashboardRuntime();
            bool logging = runtime.Settings.General.DiagnosticsEnabled;
            using (runtime)
            using (var diagnostics = new DiagnosticLogger(logging))
            using (var telemetry = new IRacingTelemetrySource(diagnostics, runtime.Dashboard))
            {
                try
                {
                    telemetry.Start();
                    Console.WriteLine("Waiting for iRacing telemetry.");
                    if (diagnostics.DirectoryPath != null)
                        Console.WriteLine("Diagnostic logs: " + diagnostics.DirectoryPath);
                    RunDashboard(telemetry, runtime, diagnostics);

                    Console.WriteLine("Dashboard stopped cleanly.");
                    return 0;
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine(exception);
                    WriteCrash(exception);
                    return 1;
                }
                finally
                {
                    Console.CancelKeyPress -= OnCancelKeyPress;
                    try
                    {
                        if (File.Exists(DashboardStatusPath) &&
                            File.ReadAllText(DashboardStatusPath).Trim() == Process.GetCurrentProcess().Id.ToString())
                        {
                            File.Delete(DashboardStatusPath);
                            if (File.Exists(ActiveDashboardPath)) File.Delete(ActiveDashboardPath);
                        }
                    }
                    catch { }
                    _instanceMutex.ReleaseMutex();
                    _instanceMutex.Dispose();
                }
            }
        }

        /// <summary>
        /// Opens a window in a scripted state and saves a screenshot of it, for checking
        /// the UI without a person at the screen. Nothing is saved to settings.
        /// </summary>
        private static int UiSnapshot(string scenario, string output)
        {
            if (!Path.IsPathRooted(output)) output = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, output);
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/AlmostFastDash;component/UI/Theme.xaml") });
            DarkTitleBar.ForEveryWindow();
            Window window;
            if (scenario.StartsWith("wizard"))
            {
                AppSettings settings = SettingsStore.Load();
                var wizard = new DeviceSetupWindow(settings, null, () => false, () => System.Threading.Tasks.Task.FromResult(true)) { Width = 820, Height = 680 };
                wizard.Offline = true;
                wizard.Loaded += (s, e) => wizard.JumpTo(scenario.Substring("wizard-".Length));
                window = wizard;
            }
            else if (scenario.StartsWith("device-settings"))
            {
                AppSettings settings = SettingsStore.Load();
                DeviceSettings device = settings.General.Devices.LastOrDefault() ?? new DeviceSettings { Name = "Wheel LEDs", Port = "COM6", LedCount = 16 };
                window = new DeviceSettingsWindow(device, "Connected · fast updates");
            }
            else if (scenario.StartsWith("lights"))
            {
                AppSettings settings = SettingsStore.Load();
                DashboardSettings dashboard = SettingsStore.Clone(settings.GetDefaultDashboard());
                if (scenario.Contains("refined")) dashboard.Lights = LightPresets.Refined();
                var lights = new LightsWindow(settings, dashboard, null) { Width = 1440, Height = 880 };
                if (scenario.Contains("themes")) lights.Loaded += (s, e) => lights.ShowThemes();
                window = lights;
            }
            else if (scenario.StartsWith("template-picker"))
            {
                // "template-picker" with no wheel screen and no size chosen before; "template-picker-found" with a screen plugged in.
                var general = new GeneralSettings();
                window = TemplatePicker.Build(null, new UnitSystem(general), general,
                    scenario.EndsWith("found") ? new System.Drawing.Size(ScreenSizes.DefaultWidth, ScreenSizes.DefaultHeight) : (System.Drawing.Size?)null, (t, s) => { });
            }
            else if (scenario.StartsWith("formulas"))
            {
                // "formulas", or "formulas-fuel" already searched for fuel.
                AppSettings settings = SettingsStore.Load();
                var scope = new EvalScope { T = IRacingTelemetrySource.CreatePreviewSnapshot(), Units = new UnitSystem(settings.General), Theme = settings.GetDefaultDashboard().Design?.Theme ?? new LayoutTheme() };
                FormulaReferenceWindow reference = FormulaReferenceWindow.Create(() => scope, scenario.Contains("-") ? scenario.Substring(scenario.IndexOf('-') + 1) : null);
                reference.Width = 1060;
                reference.Height = 780;
                window = reference;
            }
            else if (scenario.StartsWith("designer"))
            {
                AppSettings settings = SettingsStore.Load();
                DashboardSettings dashboard = scenario.Contains("wide")
                    ? DashboardSettings.CreateDesigned(DesignTemplates.Road, 1280, 480)
                    : DashboardSettings.CreateDesigned(DesignTemplates.Find(scenario.Contains("blank") ? "blank" : scenario.Contains("oval") ? "spotter" : "apex"));
                var designer = new DesignerWindow(settings, dashboard, null) { Width = 1560, Height = 940 };
                designer.Loaded += (s, e) => designer.Dispatcher.BeginInvoke(new Action(() => designer.PrepareSnapshot(scenario)), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                window = designer;
            }
            else
            {
                var settingsWindow = new SettingsWindow { Width = 1360, Height = 900, Offline = true };
                settingsWindow.Loaded += (s, e) => settingsWindow.Dispatcher.BeginInvoke(new Action(() => settingsWindow.PrepareSnapshot(scenario)), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                window = settingsWindow;
            }
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = 0;
            window.Top = 0;
            window.ShowActivated = false;
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                var content = (FrameworkElement)window.Content;
                var target = new System.Windows.Media.Imaging.RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                // Glass windows are see-through; compose them over a dark desktop-like backdrop like the real blur.
                var backdrop = new System.Windows.Media.DrawingVisual();
                using (var dc = backdrop.RenderOpen())
                    dc.DrawRectangle(new System.Windows.Media.LinearGradientBrush(System.Windows.Media.Color.FromRgb(20, 30, 46), System.Windows.Media.Color.FromRgb(8, 10, 16), 45), null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
                target.Render(backdrop);
                if (window.Background is System.Windows.Media.SolidColorBrush tint && tint.Color.A < 255)
                {
                    var tintLayer = new System.Windows.Media.DrawingVisual();
                    using (var dc = tintLayer.RenderOpen()) dc.DrawRectangle(tint, null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
                    target.Render(tintLayer);
                }
                target.Render(content);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(target));
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                using (FileStream file = File.Create(output)) encoder.Save(file);
                Environment.Exit(0);
            };
            // "...-hold" keeps the window open to try by hand instead of taking the picture.
            if (!scenario.Contains("hold")) window.Loaded += (s, e) => timer.Start();
            application.Run(window);
            return 0;
        }

        /// <summary>Saves a picture of every saved dashboard (in settings.json next to the exe), in each state and on each page.</summary>
        private static int RenderDashboards(string directory)
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
        private static int AuditDashboards(string reportPath)
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

        private static int RenderFlags(string directory)
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
        private static int RenderTemplates(string directory)
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

        private static void RunDashboard(
            IRacingTelemetrySource telemetry,
            DashboardRuntime runtime,
            DiagnosticLogger diagnostics)
        {
            // While iRacing isn't connected, the settings window's live preview can show on the wheel.
            var preview = new PreviewRequest();
            var stats = new ScreenStats();
            MonitorOutput monitor = null;
            // The LEDs and the wheel screen each have a thread of their own: the screen connects, reconnects and
            // sends by itself, so this loop only decides what to draw and when, and draws the next frame while
            // the last one is still being sent. Their frame waits need Windows' 1 ms timer to keep 60 a second.
            using (new FineTimer())
            using (new LightLoop(() => preview.Apply(telemetry.GetSnapshot()), () => runtime.Settings, () => runtime.Dashboard, diagnostics.RecordLight))
            using (var wheel = new ScreenSender(() => runtime.Settings.General, stats))
            {
                try
                {
                    DateTime nextSettingsCheckUtc = DateTime.MinValue;
                    // The dashboard's own fingerprint of everything it shows: it redraws only when that changes.
                    int? lastDrawn = null;
                    int wheelGeneration = 0, monitorGeneration = 0;
                    // The wheel screen's connection that was last blanked by Show when (-1: none), and the black frame.
                    int wheelBlanked = -1;
                    System.Drawing.Bitmap blank = null;
                    MonitorOutput lastMonitor = null;
                    bool? lastTelemetryConnection = null;
                    int lastObservedGear = int.MinValue;
                    int transitionFromGear = 0;
                    DateTime gearTransitionStartedUtc = DateTime.MinValue;
                    bool pageButtonHeld = false, backButtonHeld = false;
                    // When the next frame is due, on the Stopwatch's clock.
                    long due = System.Diagnostics.Stopwatch.GetTimestamp();

                    while (!ShouldStop())
                    {
                        TelemetrySnapshot snapshot = preview.Apply(telemetry.GetSnapshot());

                        // Pick up saved settings and switch dashboards when the track type changes, without
                        // restarting the process; and follow where on the PC it's shown (a window, over the game, or a
                        // display, which may be unplugged).
                        if (DateTime.UtcNow >= nextSettingsCheckUtc)
                        {
                            nextSettingsCheckUtc = DateTime.UtcNow.AddSeconds(1);
                            if (runtime.Refresh(preview.Active ? null : snapshot.Discipline, preview.Active ? preview.DashboardId : null))
                            {
                                telemetry.ApplyAlertSettings(runtime.Dashboard.Alerts);
                                lastDrawn = null;
                                Console.WriteLine("Dashboard: " + runtime.Dashboard.Name);
                            }
                            monitor = MonitorOutput.Follow(monitor, runtime.Settings.General, runtime.Dashboard.Name, runtime.Dashboard.ScreenSize());
                        }
                        // Show when (at the top of the settings window): the dashboard shows only at the chosen points in
                        // iRacing. Before iRacing is running, and for a preview, it shows as it always has.
                        GeneralSettings general = runtime.Settings.General;
                        bool shown = Shown(snapshot, general);
                        NoteHidden(shown ? (CarState?)null : snapshot.CarState);
                        // The dashboard on the PC follows it. Over the game it also waits for iRacing (or a preview)
                        // unless it's wanted always, and it's unlocked while the settings window is placing it.
                        if (monitor != null)
                        {
                            bool overlay = monitor.Mode == MonitorOutput.Overlay;
                            monitor.Update(general.OverlayOpacity, shown && (!overlay || !general.OverlayOnlyWhileRacing || snapshot.Connected),
                                overlay && OverlayUnlock.Requested());
                        }
                        wheel.SetBrightness(runtime.Settings.General.ScreenBrightness);

                        bool shiftFlashOn = snapshot.ShiftAlert && ((Environment.TickCount / 140) & 1) == 0;
                        if (snapshot.Connected)
                        {
                            if (lastObservedGear == int.MinValue)
                                lastObservedGear = snapshot.Gear;
                            else if (snapshot.Gear != lastObservedGear)
                            {
                                transitionFromGear = lastObservedGear;
                                lastObservedGear = snapshot.Gear;
                                gearTransitionStartedUtc = DateTime.UtcNow;
                            }
                        }
                        else
                        {
                            lastObservedGear = int.MinValue;
                            gearTransitionStartedUtc = DateTime.MinValue;
                        }

                        double transitionElapsed = gearTransitionStartedUtc == DateTime.MinValue
                            ? GearTransitionMilliseconds
                            : (DateTime.UtcNow - gearTransitionStartedUtc).TotalMilliseconds;
                        float gearProgress = (float)Math.Min(1, transitionElapsed / GearTransitionMilliseconds);
                        int gearAnimationFrame = gearProgress < 1 ? (int)(gearProgress * GearTransitionFrames) : -1;
                        if (lastTelemetryConnection != snapshot.Connected)
                        {
                            Console.WriteLine(snapshot.Connected ? "iRacing telemetry connected." : "Waiting for iRacing telemetry...");
                            lastTelemetryConnection = snapshot.Connected;
                        }

                        // A wheel button steps through the dashboard's pages.
                        DashLayout design = runtime.Dashboard.Design;
                        if (design != null && (!string.IsNullOrEmpty(design.PageButton) || !string.IsNullOrEmpty(design.PageBackButton)))
                        {
                            Dictionary<string, uint> buttons = WheelButtons.Read();
                            bool next = WheelButtons.IsDown(buttons, design.PageButton), back = WheelButtons.IsDown(buttons, design.PageBackButton);
                            if (next && !pageButtonHeld) runtime.Renderer.StepPage(1);
                            if (back && !backButtonHeld) runtime.Renderer.StepPage(-1);
                            pageButtonHeld = next;
                            backButtonHeld = back;
                        }

                        // Taps on a touch wheel screen, or clicks and touches on the dashboard on the PC, move tap zones on to their next view.
                        System.Drawing.Size built = runtime.Dashboard.ScreenSize();
                        void Tap(double x, double y)
                        {
                            if (runtime.Renderer.Tap(x, y, snapshot)) Console.WriteLine("Tap at " + Math.Round(x) + ", " + Math.Round(y));
                        }
                        while (wheel.TryTakeTap(out System.Drawing.Point tap))
                        {
                            // A dashboard of another size is fitted to the screen: undo that fit for the tap.
                            System.Drawing.Size screen = wheel.Size;
                            double scale = Math.Min(screen.Width / (double)built.Width, screen.Height / (double)built.Height);
                            Tap((tap.X - (screen.Width - built.Width * scale) / 2) / scale, (tap.Y - (screen.Height - built.Height * scale) / 2) / scale);
                        }
                        if (monitor != null)
                            while (monitor.TryTakeTap(out System.Drawing.PointF share)) Tap(share.X * built.Width, share.Y * built.Height);

                        // Hidden by Show when: the wheel screen goes black once, and stays so until it's shown again
                        // (then it gets a whole frame).
                        bool onWheel = wheel.Connected && shown;
                        if (wheel.Connected && !shown && wheelBlanked != wheel.Generation && wheel.Ready)
                        {
                            System.Drawing.Size size = wheel.Size;
                            if (blank == null || blank.Size != size)
                            {
                                blank?.Dispose();
                                blank = new System.Drawing.Bitmap(size.Width, size.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                                using (var g = System.Drawing.Graphics.FromImage(blank)) g.Clear(System.Drawing.Color.Black);
                            }
                            wheel.Submit(blank);
                            wheelBlanked = wheel.Generation;
                        }
                        if (shown && wheelBlanked != -1)
                        {
                            wheelBlanked = -1;
                            lastDrawn = null;
                        }

                        // A screen that has just connected, or a window just opened or shown again, needs a whole frame.
                        bool showing = monitor != null && monitor.Showing;
                        if (wheel.Generation != wheelGeneration || monitor != lastMonitor || (monitor != null && monitor.Generation != monitorGeneration))
                        {
                            wheelGeneration = wheel.Generation;
                            lastMonitor = monitor;
                            monitorGeneration = monitor?.Generation ?? 0;
                            lastDrawn = null;
                        }

                        bool waiting = false;
                        int current = runtime.Renderer.StateHash(snapshot, shiftFlashOn, gearAnimationFrame);
                        // Nothing to draw for when no screen shows it (no wheel screen, the window minimised, hidden by
                        // Show when, or hidden over the game while iRacing isn't running).
                        if ((onWheel || showing) && current != lastDrawn)
                        {
                            // Drawn only once every screen showing it can take it, so no frame is drawn just to be
                            // replaced; that also means it's the newest state when it goes.
                            if ((!onWheel || wheel.Ready) && (!showing || monitor.Ready))
                            {
                                int previousGear = gearProgress < 1 ? transitionFromGear : snapshot.Gear;
                                long drawStart = System.Diagnostics.Stopwatch.GetTimestamp();
                                // The renderer's own bitmap (SharedOutput): each screen copies it straight away.
                                System.Drawing.Bitmap frame = runtime.Renderer.Render(snapshot, shiftFlashOn, previousGear, gearProgress);
                                stats.Drawn((System.Diagnostics.Stopwatch.GetTimestamp() - drawStart) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
                                if (onWheel)
                                {
                                    // A dashboard built for another screen size is fitted to this one, never stretched.
                                    System.Drawing.Size size = wheel.Size;
                                    if (frame.Width == size.Width && frame.Height == size.Height) wheel.Submit(frame);
                                    else using (var fitted = ScreenSizes.Fit(frame, size.Width, size.Height)) wheel.Submit(fitted);
                                }
                                // The window fits any size itself, on the graphics card.
                                if (showing) monitor.Submit(frame, runtime.Renderer.LastChanged);
                                lastDrawn = current;
                            }
                            else waiting = true;
                        }

                        // At the frame rate chosen in Settings (60 a second unless 30 is chosen), 40 at least while the gear
                        // changes. A steady beat: each frame is due one period after the last one was due, so a wait that
                        // ran a little long is made up on the next frame instead of slowing every frame down.
                        double frameTime = 1000.0 / (runtime.Settings.General.FrameRate == 30 ? 30 : 60);
                        double period = gearProgress < 1 ? Math.Min(frameTime, 25)
                            : snapshot.Flag != RacingFlag.None || snapshot.Connected ? frameTime : IdleRefreshMilliseconds;
                        if (!waiting)
                        {
                            long now = System.Diagnostics.Stopwatch.GetTimestamp(), step = (long)(period * System.Diagnostics.Stopwatch.Frequency / 1000);
                            due += step;
                            // More than a frame behind (a slow frame, or a longer idle wait just ended): start the beat
                            // again from now rather than rushing frames out to catch up.
                            if (due < now - step) due = now;
                            double left = (due - now) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                            if (left >= 1) StopRequested.Wait((int)left);
                        }
                        // A frame is due but a screen is still sending the last: draw the moment it's taken.
                        else if (wheel.Connected && !wheel.Ready) wheel.WaitReady((int)period);
                        else StopRequested.Wait(2);
                    }
                }
                finally
                {
                    monitor?.Dispose();
                    NoteHidden(null);
                }
            }
        }

        /// <summary>Whether the dashboard shows now: always before iRacing runs and for previews, otherwise where Show when says.</summary>
        internal static bool Shown(TelemetrySnapshot snapshot, GeneralSettings general) =>
            snapshot == null || !snapshot.Connected || snapshot.IsPreview || general.ShowsAt(snapshot.CarState);

        internal static readonly string HiddenPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dashboard.hidden");
        private static CarState? _hiddenNoted;

        /// <summary>
        /// Tells the settings window when the running dashboard is hidden by Show when, and at which point in iRacing
        /// (dashboard.hidden); written only when that changes.
        /// </summary>
        private static void NoteHidden(CarState? hidden)
        {
            if (hidden == _hiddenNoted) return;
            _hiddenNoted = hidden;
            try
            {
                if (hidden.HasValue) File.WriteAllText(HiddenPath, hidden.Value.ToString());
                else if (File.Exists(HiddenPath)) File.Delete(HiddenPath);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        /// <summary>
        /// Owns the active dashboard and its renderer, reloading settings.json when
        /// it changes and following the session's track type when auto-switch is on.
        /// </summary>
        private sealed class DashboardRuntime : IDisposable
        {
            private DateTime _settingsWriteUtc;
            private string _discipline;
            private string _previewId;

            public DashboardRuntime()
            {
                _settingsWriteUtc = SettingsStore.LastWriteUtc();
                Settings = SettingsStore.Load();
                Dashboard = Select();
                Renderer = CreateRenderer();
                WriteActive();
            }

            public AppSettings Settings { get; private set; }
            public DashboardSettings Dashboard { get; private set; }
            public LayoutRenderer Renderer { get; private set; }

            /// <summary>Returns true when the renderer or LED settings were replaced.</summary>
            /// <param name="previewId">A dashboard the settings window is previewing, shown instead while set.</param>
            public bool Refresh(string discipline, string previewId = null)
            {
                DateTime writeUtc = SettingsStore.LastWriteUtc();
                bool settingsChanged = writeUtc != _settingsWriteUtc;
                bool disciplineChanged = discipline != null && discipline != _discipline;
                bool previewChanged = previewId != _previewId;
                if (!settingsChanged && !disciplineChanged && !previewChanged) return false;
                _previewId = previewId;

                if (discipline != null) _discipline = discipline;
                if (settingsChanged)
                {
                    // A copy that can't be read right now is tried again next time; the current settings stay.
                    if (!SettingsStore.TryLoad(out AppSettings reloaded)) return false;
                    _settingsWriteUtc = writeUtc;
                    Settings = reloaded;
                }

                DashboardSettings next = Select();
                if (!settingsChanged && next.Id == Dashboard.Id)
                {
                    WriteActive();
                    return false;
                }
                Dashboard = next;
                Renderer.Dispose();
                Renderer = CreateRenderer();
                WriteActive();
                return true;
            }

            private DashboardSettings Select() =>
                _previewId != null && Settings.Dashboards.Any(d => d.Id == _previewId)
                    ? Settings.Dashboards.First(d => d.Id == _previewId)
                    : Settings.ResolveDashboard(_discipline);

            private LayoutRenderer CreateRenderer()
            {
                System.Drawing.Size size = Dashboard.ScreenSize();
                // Tap views stay put while the same dashboard is redrawn after a settings change, and start
                // again from the first view on another dashboard.
                if (_tapsFor != Dashboard.Id) { _taps.Clear(); _tapsFor = Dashboard.Id; }
                return new LayoutRenderer(size.Width, size.Height, Dashboard, new UnitSystem(Settings.General)) { Taps = _taps, SharedOutput = true, ReuseFingerprints = true };
            }

            private readonly Dictionary<string, int> _taps = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            private string _tapsFor;

            private void WriteActive()
            {
                try { File.WriteAllText(ActiveDashboardPath, Dashboard.Id + "\n" + (_discipline ?? "")); }
                catch { }
            }

            public void Dispose() => Renderer?.Dispose();
        }

        /// <summary>
        /// Streams a dim sweep to an LED board at 60 frames a second for 90 seconds through the real
        /// LED code, then checks the board still answers: proves it keeps up with fast-changing lights.
        /// </summary>
        private static int LedSoak(string port, int leds, bool fast)
        {
            using (var controller = new ArduinoLedController(port, 115200, leds, null) { Batched = fast })
            {
                controller.Connect();
                Console.WriteLine("Board: " + controller.QueryDeviceName());
                var watch = System.Diagnostics.Stopwatch.StartNew();
                int frames = 0;
                try
                {
                while (watch.Elapsed.TotalSeconds < 90)
                {
                    var frame = new System.Drawing.Color[leds];
                    double head = (watch.ElapsedMilliseconds / 650.0 % 1) * leds;
                    for (int i = 0; i < leds; i++)
                    {
                        double level = Math.Max(0, 1 - Math.Abs(head - i) / 3);
                        frame[i] = System.Drawing.Color.FromArgb(0, (int)(25 * level), (int)(40 * level));
                    }
                    controller.SetFrame(frame);
                    frames++;
                    System.Threading.Thread.Sleep(16);
                }
                Console.WriteLine(frames + " frames in " + watch.Elapsed.TotalSeconds.ToString("0") + " s (" + (frames / watch.Elapsed.TotalSeconds).ToString("0") + " a second); board still answers: " + controller.QueryDeviceName());
                }
                catch (Exception exception)
                {
                    Console.WriteLine("FAILED after " + watch.Elapsed.TotalSeconds.ToString("0.0") + " s and " + frames + " frames: " + exception.Message);
                }
                Console.WriteLine("Packets " + controller.Sent + ", unacknowledged " + controller.Unacknowledged + ", slowest acknowledgement " + controller.SlowestAckMilliseconds.ToString("0.0") + " ms");
            }
            return 0;
        }

        /// <summary>Appends an unexpected error to Logs\crash.log, so a dashboard that stopped can be looked into.</summary>
        private static void WriteCrash(Exception exception)
        {
            if (exception == null) return;
            try
            {
                string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
                Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, "crash.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + exception + Environment.NewLine + Environment.NewLine);
            }
            catch { }
        }

        private static bool ShouldStop()
        {
            if (File.Exists(StopRequestPath))
            {
                try { File.Delete(StopRequestPath); } catch { }
                return true;
            }
            return StopRequested.IsSet || StopSignal.WaitOne(0);
        }

        private static void OnCancelKeyPress(object sender, ConsoleCancelEventArgs eventArgs)
        {
            eventArgs.Cancel = true;
            StopRequested.Set();
        }

        private static string ParseOption(string[] args, string option)
        {
            int optionIndex = Array.IndexOf(args, option);
            return optionIndex >= 0 && optionIndex + 1 < args.Length
                ? args[optionIndex + 1]
                : null;
        }
    }
}
