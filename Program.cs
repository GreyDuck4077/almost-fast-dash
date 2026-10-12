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


            string previewDirectory = ParseOption(args, "--render-previews");
            if (previewDirectory != null) return RenderPreviews(previewDirectory);
            string templateDirectory = ParseOption(args, "--render-templates");
            if (templateDirectory != null) return RenderTemplates(templateDirectory);
            string flagDirectory = ParseOption(args, "--render-flags");
            if (flagDirectory != null) return RenderFlags(flagDirectory);
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

            bool explicitDashboardMode = args.Contains("--run") || args.Contains("--demo") ||
                args.Contains("--preview") || args.Contains("--preview-timed") || args.Contains("--seconds") ||
                args.Contains("--capture-preview") || args.Contains("--capture-dashboard");
            if (!explicitDashboardMode)
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

            int? runSeconds = ParseRunSeconds(args);
            bool demoMode = args.Contains("--demo");
            bool previewMode = args.Contains("--preview");
            bool timedPreviewMode = args.Contains("--preview-timed");
            Console.WriteLine("Almost Fast Overlays");
            Console.WriteLine("Close other dashboard software before running so it does not compete for the device.");

            Console.CancelKeyPress += OnCancelKeyPress;

            string captureDashboardId = ParseOption(args, "--capture-dashboard");
            var runtime = new DashboardRuntime(captureDashboardId);
            bool logging = runtime.Settings.General.DiagnosticsEnabled && !args.Contains("--no-logging");
            using (runtime)
            using (var diagnostics = new DiagnosticLogger(logging))
            using (var telemetry = new IRacingTelemetrySource(diagnostics, runtime.Dashboard))
            {
                try
                {
                    if (timedPreviewMode)
                    {
                        telemetry.StartTimedDashboardPreview();
                        Console.WriteLine("Preview mode: showing a representative timed race.");
                    }
                    else if (previewMode || !string.IsNullOrWhiteSpace(captureDashboardId))
                    {
                        telemetry.StartDashboardPreview();
                        Console.WriteLine("Preview mode: holding representative premium-dashboard values.");
                    }
                    else if (demoMode)
                    {
                        telemetry.StartDemoSequence();
                        Console.WriteLine("Demo mode: cycling gears, speed, and all programmed racing flags.");
                    }
                    else
                    {
                        telemetry.Start();
                        Console.WriteLine("Live mode: waiting for iRacing telemetry.");
                    }
                    if (diagnostics.DirectoryPath != null)
                        Console.WriteLine("Radar diagnostics: " + diagnostics.DirectoryPath);

                    string capturePath = ParseOption(args, "--capture-preview");
                    if (!string.IsNullOrWhiteSpace(capturePath))
                    {
                        if (!Path.IsPathRooted(capturePath))
                            capturePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, capturePath);
                        Directory.CreateDirectory(Path.GetDirectoryName(capturePath));
                        TelemetrySnapshot captureSnapshot = telemetry.GetSnapshot();
                        using (var capture = runtime.Renderer.Render(
                            captureSnapshot, false, captureSnapshot.Gear, 1f))
                            capture.Save(capturePath, System.Drawing.Imaging.ImageFormat.Png);
                    }

                    DateTime? finishAt = runSeconds.HasValue
                        ? DateTime.UtcNow.AddSeconds(runSeconds.Value)
                        : (DateTime?)null;

                    RunDashboard(telemetry, runtime, finishAt, diagnostics);

                    Console.WriteLine("Dashboard stopped cleanly.");
                    return 0;
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine("DV-5 test failed: " + exception.Message);
                    Console.Error.WriteLine(exception);
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
        /// Writes a PNG of every configured dashboard plus a flag sample without
        /// opening the screen or LEDs, so layouts can be reviewed while the hardware is in use.
        /// </summary>
        private static int RenderPreviews(string directory)
        {
            if (!Path.IsPathRooted(directory))
                directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, directory);
            Directory.CreateDirectory(directory);
            AppSettings settings = SettingsStore.Load();
            var units = new UnitSystem(settings.General);
            foreach (DashboardSettings dashboard in settings.Dashboards)
            {
                using (var frame = DashboardRendererFactory.RenderPreview(dashboard, units))
                    frame.Save(Path.Combine(directory, dashboard.Name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                using (var spotter = DashboardRendererFactory.RenderPreview(dashboard, units, IRacingTelemetrySource.CreateSpotterPreviewSnapshot()))
                    spotter.Save(Path.Combine(directory, dashboard.Name + " - Cars alongside.png"), System.Drawing.Imaging.ImageFormat.Png);
                using (var pit = DashboardRendererFactory.RenderPreview(dashboard, units, IRacingTelemetrySource.CreatePitLanePreviewSnapshot()))
                        pit.Save(Path.Combine(directory, dashboard.Name + " - Pit lane.png"), System.Drawing.Imaging.ImageFormat.Png);
            }
            using (var flag = DashboardRendererFactory.RenderPreview(
                settings.GetDefaultDashboard(), units, IRacingTelemetrySource.CreatePreviewSnapshot(RacingFlag.Checkered)))
                flag.Save(Path.Combine(directory, "Flag - Checkered.png"), System.Drawing.Imaging.ImageFormat.Png);
            return 0;
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
                window = new LightsWindow(settings, dashboard, null) { Width = 1440, Height = 880 };
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
        /// <summary>Writes every full-screen flag at a few moments of its breeze, and times a frame.</summary>
        private static int RenderFlags(string directory)
        {
            if (!Path.IsPathRooted(directory)) directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, directory);
            Directory.CreateDirectory(directory);
            using (var renderer = new LayoutRenderer(Dv5Screen.Width, Dv5Screen.Height, DashboardSettings.CreateHalo(), UnitSystem.Default))
            {
                foreach (RacingFlag flag in Enum.GetValues(typeof(RacingFlag)))
                {
                    if (flag == RacingFlag.None) continue;
                    TelemetrySnapshot snapshot = IRacingTelemetrySource.CreatePreviewSnapshot(flag);
                    foreach (int ms in new[] { 0, 900 })
                    {
                        renderer.FlagClock = ms;
                        using (System.Drawing.Bitmap frame = renderer.Render(snapshot, false, snapshot.Gear, 1))
                            frame.Save(Path.Combine(directory, flag + " " + ms + "ms.png"), System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                TelemetrySnapshot green = IRacingTelemetrySource.CreatePreviewSnapshot(RacingFlag.Green);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < 60; i++)
                {
                    renderer.FlagClock = i * 33;
                    using (renderer.Render(green, false, green.Gear, 1)) { }
                }
                File.WriteAllText(Path.Combine(directory, "timing.txt"), (watch.Elapsed.TotalMilliseconds / 60).ToString("0.0") + " ms per waving flag frame");
            }
            return 0;
        }

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
            return 0;
        }

        private static void RunDashboard(
            IRacingTelemetrySource telemetry,
            DashboardRuntime runtime,
            DateTime? finishAt,
            DiagnosticLogger diagnostics)
        {
            // While iRacing isn't connected, the settings window's live preview can show on the wheel.
            var preview = new PreviewRequest();
            // The LEDs run on their own loop, so they keep working while the screen reconnects.
            using (new LightLoop(() => preview.Apply(telemetry.GetSnapshot()), () => runtime.Settings, () => runtime.Dashboard, diagnostics.RecordLight))
            while (!ShouldStop(finishAt))
            {
                try
                {
                    using (var screen = new Dv5Screen())
                    {
                        var connection = screen.Connect(runtime.Settings.General.ScreenBrightness);
                        Console.WriteLine("Screen connected: " + connection.ModelName);
                        Console.WriteLine("Display ID: " + connection.DisplayId);
                        DateTime nextSettingsCheckUtc = DateTime.MinValue;

                        DisplayState lastDrawn = null;
                        bool? lastTelemetryConnection = null;
                        int lastObservedGear = int.MinValue;
                        int transitionFromGear = 0;
                        DateTime gearTransitionStartedUtc = DateTime.MinValue;

                        try
                        {
                            while (!ShouldStop(finishAt))
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

                                int designHash = runtime.Renderer is IStateRenderer designed
                                    ? designed.StateHash(snapshot, shiftFlashOn, gearAnimationFrame) : 0;
                                DisplayState current = DisplayState.From(
                                    snapshot, shiftFlashOn, gearAnimationFrame, runtime.Renderer is ILiveTraceRenderer, designHash);
                                if (!current.Equals(lastDrawn))
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
                        finally { }
                    }
                }
                catch (Exception exception)
                {
                    if (ShouldStop(finishAt)) return;
                    Console.Error.WriteLine("DV-5 connection lost: " + exception.Message);
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
            private readonly string _pinnedDashboardId;
            private DateTime _settingsWriteUtc;
            private string _discipline;
            private string _previewId;

            public DashboardRuntime(string pinnedDashboardId)
            {
                _pinnedDashboardId = pinnedDashboardId;
                _settingsWriteUtc = SettingsStore.LastWriteUtc();
                Settings = SettingsStore.Load();
                Dashboard = Select();
                Renderer = CreateRenderer();
                WriteActive();
            }

            public AppSettings Settings { get; private set; }
            public DashboardSettings Dashboard { get; private set; }
            public IDashboardRenderer Renderer { get; private set; }

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
                    _settingsWriteUtc = writeUtc;
                    Settings = SettingsStore.Load();
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
                    : !string.IsNullOrWhiteSpace(_pinnedDashboardId)
                    ? Settings.Dashboards.FirstOrDefault(d => d.Id == _pinnedDashboardId) ?? Settings.GetDefaultDashboard()
                    : Settings.ResolveDashboard(_discipline);

            private IDashboardRenderer CreateRenderer() =>
                DashboardRendererFactory.Create(Dv5Screen.Width, Dv5Screen.Height, Dashboard, new UnitSystem(Settings.General));

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

        private static bool ShouldStop(DateTime? finishAt)
        {
            if (File.Exists(StopRequestPath))
            {
                try { File.Delete(StopRequestPath); } catch { }
                return true;
            }
            return StopRequested.IsSet || StopSignal.WaitOne(0) ||
                (finishAt.HasValue && DateTime.UtcNow >= finishAt.Value);
        }

        private static void OnCancelKeyPress(object sender, ConsoleCancelEventArgs eventArgs)
        {
            eventArgs.Cancel = true;
            StopRequested.Set();
        }

        private static int? ParseRunSeconds(string[] args)
        {
            int optionIndex = Array.IndexOf(args, "--seconds");
            if (optionIndex >= 0 && optionIndex + 1 < args.Length &&
                int.TryParse(args[optionIndex + 1], out int seconds) && seconds > 0)
            {
                return seconds;
            }

            return null;
        }

        private static string ParseOption(string[] args, string option)
        {
            int optionIndex = Array.IndexOf(args, option);
            return optionIndex >= 0 && optionIndex + 1 < args.Length
                ? args[optionIndex + 1]
                : null;
        }

        private sealed class DisplayState
        {
            private DisplayState(
                bool connected, int gear, int speedMph, RacingFlag flag, bool shiftFlashOn,
                SessionProgressMode progressMode, int secondsRemaining, int lapsCompleted, int totalLaps,
                int position, int incidents, int fuelTenths, int currentLapTenths, int deltaHundredths,
                string driverAhead, int aheadHundredths, string driverBehind, int behindHundredths,
                int totalStarters, bool showLapSummary, int lastLapHundredths,
                int bestLapHundredths, int averageLapHundredths, int lapColorVersion,
                int maxForwardGear, int gearAnimationFrame, int strategyHash)
            {
                StrategyHash = strategyHash;
                Connected = connected;
                Gear = gear;
                SpeedMph = speedMph;
                Flag = flag;
                ShiftFlashOn = shiftFlashOn;
                ProgressMode = progressMode;
                SecondsRemaining = secondsRemaining;
                LapsCompleted = lapsCompleted;
                TotalLaps = totalLaps;
                Position = position;
                Incidents = incidents;
                FuelTenths = fuelTenths;
                CurrentLapTenths = currentLapTenths;
                DeltaHundredths = deltaHundredths;
                DriverAhead = driverAhead;
                AheadHundredths = aheadHundredths;
                DriverBehind = driverBehind;
                BehindHundredths = behindHundredths;
                TotalStarters = totalStarters;
                ShowLapSummary = showLapSummary;
                LastLapHundredths = lastLapHundredths;
                BestLapHundredths = bestLapHundredths;
                AverageLapHundredths = averageLapHundredths;
                LapColorVersion = lapColorVersion;
                MaxForwardGear = maxForwardGear;
                GearAnimationFrame = gearAnimationFrame;
            }

            private bool Connected { get; }
            private int Gear { get; }
            private int SpeedMph { get; }
            private RacingFlag Flag { get; }
            private bool ShiftFlashOn { get; }
            private SessionProgressMode ProgressMode { get; }
            private int SecondsRemaining { get; }
            private int LapsCompleted { get; }
            private int TotalLaps { get; }
            private int Position { get; }
            private int Incidents { get; }
            private int FuelTenths { get; }
            private int CurrentLapTenths { get; }
            private int DeltaHundredths { get; }
            private string DriverAhead { get; }
            private int AheadHundredths { get; }
            private string DriverBehind { get; }
            private int BehindHundredths { get; }
            private int TotalStarters { get; }
            private bool ShowLapSummary { get; }
            private int LastLapHundredths { get; }
            private int BestLapHundredths { get; }
            private int AverageLapHundredths { get; }
            private int LapColorVersion { get; }
            private int MaxForwardGear { get; }
            private int GearAnimationFrame { get; }
            private int StrategyHash { get; }

            public static DisplayState From(
                TelemetrySnapshot snapshot, bool shiftFlashOn, int gearAnimationFrame, bool liveTrace = false, int designHash = 0) => new DisplayState(
                snapshot.Connected,
                snapshot.Gear,
                (int)Math.Round(snapshot.SpeedMph, MidpointRounding.AwayFromZero),
                snapshot.Flag,
                shiftFlashOn,
                snapshot.ProgressMode,
                (int)Math.Ceiling(snapshot.SessionSecondsRemaining),
                snapshot.LapsCompleted,
                snapshot.TotalLaps,
                snapshot.Position,
                snapshot.IncidentPoints,
                (int)Math.Round(snapshot.FuelLiters * 10),
                (int)Math.Floor(snapshot.CurrentLapSeconds * 10),
                (int)Math.Round(snapshot.LapDeltaSeconds * 100),
                snapshot.DriverAhead,
                (int)Math.Round(snapshot.RelativeAheadSeconds * 100),
                snapshot.DriverBehind,
                (int)Math.Round(snapshot.RelativeBehindSeconds * 100),
                snapshot.TotalStarters,
                snapshot.ShowLapSummary,
                (int)Math.Round(snapshot.LastLapSeconds * 100),
                (int)Math.Round(snapshot.BestLapSeconds * 100),
                (int)Math.Round(snapshot.AverageLapSeconds * 100),
                snapshot.LapColorVersion,
                snapshot.MaxForwardGear,
                gearAnimationFrame,
                StrategyHashOf(snapshot) ^ (liveTrace ? snapshot.TraceVersion * 7919 : 0) ^ designHash);

            // Values only some layouts show (fuel strategy, brake bias, stint).
            private static int StrategyHashOf(TelemetrySnapshot s)
            {
                unchecked
                {
                    int hash = (int)Math.Round(s.BrakeBias * 10);
                    hash = (hash * 397) ^ (int)Math.Round(s.FuelLastLap * 100);
                    hash = (hash * 397) ^ (int)Math.Round(s.FuelPerLapAverage * 100);
                    hash = (hash * 397) ^ (int)Math.Round(s.FuelLevelPct * 100);
                    hash = (hash * 397) ^ s.StintLaps;
                    hash = (hash * 397) ^ (s.RecentLapSeconds?.Length ?? 0);
                    return hash;
                }
            }

            public override bool Equals(object obj)
            {
                var other = obj as DisplayState;
                return other != null &&
                    Connected == other.Connected &&
                    Gear == other.Gear &&
                    SpeedMph == other.SpeedMph &&
                    Flag == other.Flag &&
                    ShiftFlashOn == other.ShiftFlashOn &&
                    ProgressMode == other.ProgressMode &&
                    SecondsRemaining == other.SecondsRemaining &&
                    LapsCompleted == other.LapsCompleted &&
                    TotalLaps == other.TotalLaps &&
                    Position == other.Position &&
                    Incidents == other.Incidents &&
                    FuelTenths == other.FuelTenths &&
                    CurrentLapTenths == other.CurrentLapTenths &&
                    DeltaHundredths == other.DeltaHundredths &&
                    DriverAhead == other.DriverAhead &&
                    AheadHundredths == other.AheadHundredths &&
                    DriverBehind == other.DriverBehind &&
                    BehindHundredths == other.BehindHundredths &&
                    TotalStarters == other.TotalStarters &&
                    ShowLapSummary == other.ShowLapSummary &&
                    LastLapHundredths == other.LastLapHundredths &&
                    BestLapHundredths == other.BestLapHundredths &&
                    AverageLapHundredths == other.AverageLapHundredths &&
                    LapColorVersion == other.LapColorVersion &&
                    MaxForwardGear == other.MaxForwardGear &&
                    GearAnimationFrame == other.GearAnimationFrame &&
                    StrategyHash == other.StrategyHash;
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = Connected ? 1 : 0;
                    hash = (hash * 397) ^ Gear;
                    hash = (hash * 397) ^ SpeedMph;
                    hash = (hash * 397) ^ (int)Flag;
                    hash = (hash * 397) ^ (ShiftFlashOn ? 1 : 0);
                    hash = (hash * 397) ^ (int)ProgressMode;
                    hash = (hash * 397) ^ SecondsRemaining;
                    hash = (hash * 397) ^ LapsCompleted;
                    hash = (hash * 397) ^ TotalLaps;
                    hash = (hash * 397) ^ Position;
                    hash = (hash * 397) ^ Incidents;
                    hash = (hash * 397) ^ FuelTenths;
                    hash = (hash * 397) ^ CurrentLapTenths;
                    hash = (hash * 397) ^ DeltaHundredths;
                    hash = (hash * 397) ^ (DriverAhead?.GetHashCode() ?? 0);
                    hash = (hash * 397) ^ AheadHundredths;
                    hash = (hash * 397) ^ (DriverBehind?.GetHashCode() ?? 0);
                    hash = (hash * 397) ^ BehindHundredths;
                    hash = (hash * 397) ^ TotalStarters;
                    hash = (hash * 397) ^ (ShowLapSummary ? 1 : 0);
                    hash = (hash * 397) ^ LastLapHundredths;
                    hash = (hash * 397) ^ BestLapHundredths;
                    hash = (hash * 397) ^ AverageLapHundredths;
                    hash = (hash * 397) ^ LapColorVersion;
                    hash = (hash * 397) ^ MaxForwardGear;
                    hash = (hash * 397) ^ StrategyHash;
                    return (hash * 397) ^ GearAnimationFrame;
                }
            }
        }
    }
}
