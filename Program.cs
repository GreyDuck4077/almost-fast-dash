using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.Windows;

namespace AlmostFastOverlays
{
    internal static class Program
    {
        private const int RefreshIntervalMilliseconds = 50;
        /// <summary>How often the screen is checked while iRacing isn't running; the waiting screen barely changes.</summary>
        private const int IdleRefreshMilliseconds = 250;
        private const int ReconnectDelayMilliseconds = 2000;
        private const int GearTransitionMilliseconds = 420;
        private const int GearTransitionFrames = 14;
        private static readonly ManualResetEventSlim StopRequested = new ManualResetEventSlim(false);
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


            string templateDirectory = ParseOption(args, "--render-templates");
            if (templateDirectory != null) return RenderTemplates(templateDirectory);
            string selfTestReport = ParseOption(args, "--designer-selftest");
            if (selfTestReport != null) return DesignerSelfTest(selfTestReport);
            string soakPort = ParseOption(args, "--led-soak");
            if (soakPort != null) return LedSoak(soakPort, int.TryParse(ParseOption(args, soakPort), out int soakLeds) ? soakLeds : 16, args.Contains("--fast"));
            string lightReport = ParseOption(args, "--light-frames");
            if (lightReport != null) return LightFrames.Run(lightReport);
            string recording = ParseOption(args, "--replay-ibt");
            if (recording != null) return RecordingReplay.Run(recording, ParseOption(args, recording) ?? "replay.txt");
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
                    MessageBox.Show(e.Exception.Message, "Almost Fast Overlays", MessageBoxButton.OK, MessageBoxImage.Warning);
                    e.Handled = true;
                };
                application.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/AlmostFastOverlays;component/Theme.xaml")
                });
                application.Run(new SettingsWindow());
                return 0;
            }

            if (File.Exists(StopRequestPath)) File.Delete(StopRequestPath);

            _instanceMutex = new Mutex(true, @"Local\AlmostFastOverlays.SingleInstance", out bool ownsMutex);
            if (!ownsMutex)
            {
                Console.Error.WriteLine("Almost Fast Overlays is already running.");
                _instanceMutex.Dispose();
                return 2;
            }

            // A stopping dashboard may consume stop.request before consuming the
            // named event. Once this process owns the single-instance mutex, any
            // remaining signal is stale and must not stop the replacement run.
            while (StopSignal.WaitOne(0)) { }
            File.WriteAllText(DashboardStatusPath, Process.GetCurrentProcess().Id.ToString());

            Console.WriteLine("Almost Fast Overlays");
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
            application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/AlmostFastOverlays;component/Theme.xaml") });
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
            else if (scenario.StartsWith("designer"))
            {
                AppSettings settings = SettingsStore.Load();
                DashboardSettings dashboard = DashboardSettings.CreateDesigned(DesignTemplates.Find(scenario.Contains("oval") ? "oval" : "halo"));
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
            window.Loaded += (s, e) => timer.Start();
            application.Run(window);
            return 0;
        }

        /// <summary>Runs the designer's end-to-end checks and writes the report. Saves into this folder's settings.</summary>
        private static int DesignerSelfTest(string reportPath)
        {
            string folder = AppDomain.CurrentDomain.BaseDirectory;
            if (!Path.IsPathRooted(reportPath)) reportPath = Path.Combine(folder, reportPath);
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/AlmostFastOverlays;component/Theme.xaml") });
            AppSettings settings = SettingsStore.Load();
            DashboardSettings dashboard = settings.AddDesignedDashboard(DesignTemplates.Find("halo"), "Self test");
            var designer = new DesignerWindow(settings, dashboard, null) { Left = -4000, Top = 0, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
            int exit = 1;
            designer.Loaded += (s, e) => designer.Dispatcher.BeginInvoke(new Action(() =>
            {
                List<string> report;
                try { report = designer.RunSelfTest(folder); }
                catch (Exception exception) { report = new List<string> { "CRASHED", exception.ToString() }; }
                File.WriteAllLines(reportPath, report);
                exit = report[0].StartsWith("ALL") ? 0 : 1;
                Environment.Exit(exit);
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            application.Run(designer);
            return exit;
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
            // And one of the race-data quick values: leaderboard, tyres, sectors, an opponent card and a notice.
            DashLayout race = DesignTemplates.Blank();
            void Quick(string name, double x, double y, Action<DashWidget> adjust = null)
            {
                foreach (DashWidget w in QuickValues.All().First(b => b.Name == name).Create()) { w.X += x; w.Y += y; adjust?.Invoke(w); race.Screens[0].Widgets.Add(w); }
            }
            Quick("Leaderboard", 10, 10, w => { w.H = 330; w.Set("showIRating", "true"); });
            Quick("Tyres: all readings", 340, 10);
            Quick("Car ahead in the race", 340, 240);
            Quick("Best sector times", 10, 350, w => { if (w.Name == "Ideal lap") { w.X = 10 + 360; } });
            Quick("Oil temperature", 610, 10, w => w.W = 230);
            Quick("Oil pressure", 610, 90, w => w.W = 230);
            Quick("CPU load", 610, 170, w => w.W = 230);
            Quick("Race notices", 420, 410, w => { w.Set("visible", ""); if (w.Type == "text") w.Set("text", "PERSONAL BEST 1:27.521"); else w.Set("tint", "good"); if (w.Type == "text") w.Set("color", "good"); });
            using (var frame = DashboardRendererFactory.RenderPreview(new DashboardSettings { Layout = Layouts.Custom, Design = race, AccentColor = "#22D3EE" }, units, IRacingTelemetrySource.CreatePreviewSnapshot()))
                frame.Save(Path.Combine(directory, "Race data.png"), System.Drawing.Imaging.ImageFormat.Png);

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
            // The LEDs run on their own loop, so they keep working while the screen reconnects.
            using (new LightLoop(() => preview.Apply(telemetry.GetSnapshot()), () => runtime.Settings, () => runtime.Dashboard, diagnostics.RecordLight))
            while (!ShouldStop())
            {
                try
                {
                    using (var screen = new Dv5Screen())
                    {
                        var connection = screen.Connect(runtime.Settings.General.ScreenBrightness);
                        Console.WriteLine("Screen connected: " + connection.ModelName);
                        Console.WriteLine("Display ID: " + connection.DisplayId);
                        DateTime nextSettingsCheckUtc = DateTime.MinValue;

                        // The dashboard's own fingerprint of everything it shows: it redraws only when that changes.
                        int? lastDrawn = null;
                        bool? lastTelemetryConnection = null;
                        int lastObservedGear = int.MinValue;
                        int transitionFromGear = 0;
                        DateTime gearTransitionStartedUtc = DateTime.MinValue;

                        while (!ShouldStop())
                        {
                            TelemetrySnapshot snapshot = preview.Apply(telemetry.GetSnapshot());

                            // Pick up saved settings and switch dashboards when the
                            // track type changes, without restarting the process.
                            if (DateTime.UtcNow >= nextSettingsCheckUtc)
                            {
                                nextSettingsCheckUtc = DateTime.UtcNow.AddSeconds(1);
                                if (runtime.Refresh(preview.Active ? null : snapshot.Discipline, preview.Active ? preview.DashboardId : null))
                                {
                                    telemetry.ApplyAlertSettings(runtime.Dashboard.Alerts);
                                    screen.SetBrightness(runtime.Settings.General.ScreenBrightness);
                                    lastDrawn = null;
                                    Console.WriteLine("Dashboard: " + runtime.Dashboard.Name);
                                }
                            }

                            bool shiftFlashOn = snapshot.ShiftAlert &&
                                ((Environment.TickCount / 140) & 1) == 0;
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
                            int gearAnimationFrame = gearProgress < 1
                                ? (int)(gearProgress * GearTransitionFrames)
                                : -1;
                            if (lastTelemetryConnection != snapshot.Connected)
                            {
                                Console.WriteLine(snapshot.Connected
                                    ? "iRacing telemetry connected."
                                    : "Waiting for iRacing telemetry...");
                                lastTelemetryConnection = snapshot.Connected;
                            }

                            int current = runtime.Renderer.StateHash(snapshot, shiftFlashOn, gearAnimationFrame);
                            if (current != lastDrawn)
                            {
                                int previousGear = gearProgress < 1 ? transitionFromGear : snapshot.Gear;
                                using (var bitmap = runtime.Renderer.Render(
                                    snapshot, shiftFlashOn, previousGear, gearProgress))
                                    screen.Draw(bitmap);
                                lastDrawn = current;
                            }

                            // Gear changes and waving flags animate, so they get a faster refresh.
                            StopRequested.Wait(gearProgress < 1 ? 25 : snapshot.Flag != RacingFlag.None ? 33
                                : snapshot.Connected ? RefreshIntervalMilliseconds : IdleRefreshMilliseconds);
                        }
                    }
                }
                catch (Exception exception)
                {
                    if (ShouldStop()) return;
                    Console.Error.WriteLine("Screen connection lost: " + exception.Message);
                    Console.Error.WriteLine("Retrying in 2 seconds...");
                    // Keep following settings and track type so the lights stay right meanwhile.
                    try
                    {
                        TelemetrySnapshot live = telemetry.GetSnapshot();
                        preview.Apply(live);
                        bool previewing = !live.Connected && preview.Active;
                        if (runtime.Refresh(previewing ? null : live.Discipline, previewing ? preview.DashboardId : null)) telemetry.ApplyAlertSettings(runtime.Dashboard.Alerts);
                    }
                    catch { }
                    StopRequested.Wait(ReconnectDelayMilliseconds);
                }
            }
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

            private LayoutRenderer CreateRenderer() =>
                new LayoutRenderer(Dv5Screen.Width, Dv5Screen.Height, Dashboard, new UnitSystem(Settings.General));

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
