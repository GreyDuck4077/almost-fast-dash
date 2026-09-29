using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.Windows;

namespace DV5Direct
{
    internal static class Program
    {
        private const int RefreshIntervalMilliseconds = 50;
        private const int ReconnectDelayMilliseconds = 2000;
        private const int GearTransitionMilliseconds = 420;
        private const int GearTransitionFrames = 14;
        private const string DefaultLedPort = "COM7";
        private static readonly ManualResetEventSlim StopRequested = new ManualResetEventSlim(false);
        private static readonly EventWaitHandle StopSignal = new EventWaitHandle(
            false, EventResetMode.AutoReset, @"Local\DV5Direct.StopRequested");
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

            if (args.Contains("--brow-test")) return BrowTest(ParseRunSeconds(args) ?? 60);

            string previewDirectory = ParseOption(args, "--render-previews");
            if (previewDirectory != null) return RenderPreviews(previewDirectory);

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
                    MessageBox.Show(e.Exception.Message, "DV5 Direct", MessageBoxButton.OK, MessageBoxImage.Warning);
                    e.Handled = true;
                };
                application.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/DV5Direct;component/Theme.xaml")
                });
                application.Run(new SettingsWindow());
                return 0;
            }

            if (File.Exists(StopRequestPath)) File.Delete(StopRequestPath);

            _instanceMutex = new Mutex(true, @"Local\DV5Direct.SingleInstance", out bool ownsMutex);
            if (!ownsMutex)
            {
                Console.Error.WriteLine("DV5Direct is already running.");
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
            Console.WriteLine("DV-5 Direct");
            Console.WriteLine("Close SimHub before running so it does not compete for the device.");

            Console.CancelKeyPress += OnCancelKeyPress;

            string captureDashboardId = ParseOption(args, "--capture-dashboard");
            var runtime = new DashboardRuntime(captureDashboardId);
            string ledPort = ParseOption(args, "--led-port") ?? runtime.Settings.General.LedPort ?? DefaultLedPort;
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

                    RunDashboard(telemetry, runtime, finishAt, ledPort);

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
        /// Holds blocks of ten colors on the brow light bars (red, green, blue,
        /// white) so the number of chained bars and their order can be read by eye.
        /// </summary>
        private static int BrowTest(int seconds)
        {
            GeneralSettings general = SettingsStore.Load().General;
            string port = DevicePorts.Resolve(general.BrowPort, DevicePorts.LightBar);
            System.Drawing.Color[] blocks = { System.Drawing.Color.Red, System.Drawing.Color.Lime, System.Drawing.Color.Blue, System.Drawing.Color.White };
            var frame = new System.Drawing.Color[40];
            for (int i = 0; i < frame.Length; i++)
                frame[i] = System.Drawing.Color.FromArgb(blocks[i / 10].R * 6 / 10, blocks[i / 10].G * 6 / 10, blocks[i / 10].B * 6 / 10);
            try
            {
                using (var leds = new Dv5LedController(port, 115200, frame.Length, "SSG LB-10"))
                {
                    leds.Connect();
                    Console.WriteLine("Connected: " + leds.QueryDeviceName() + " on " + port);
                    DateTime until = DateTime.UtcNow.AddSeconds(seconds);
                    // Resend periodically in case the bar resets or drops a packet.
                    while (DateTime.UtcNow < until)
                    {
                        leds.SetFrame(frame);
                        Thread.Sleep(2000);
                    }
                    leds.Clear();
                }
                Console.WriteLine("Done.");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("Light bar test failed on " + port + ": " + exception.Message);
                return 1;
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
                if (dashboard.Layout == Layouts.Oval)
                    using (var spotter = DashboardRendererFactory.RenderPreview(dashboard, units, IRacingTelemetrySource.CreateSpotterPreviewSnapshot()))
                        spotter.Save(Path.Combine(directory, dashboard.Name + " - Cars alongside.png"), System.Drawing.Imaging.ImageFormat.Png);
                if (dashboard.Layout == Layouts.Summit)
                    using (var pit = DashboardRendererFactory.RenderPreview(dashboard, units, IRacingTelemetrySource.CreatePitLanePreviewSnapshot()))
                        pit.Save(Path.Combine(directory, dashboard.Name + " - Pit lane.png"), System.Drawing.Imaging.ImageFormat.Png);
            }
            using (var flag = DashboardRendererFactory.RenderPreview(
                settings.GetDefaultDashboard(), units, IRacingTelemetrySource.CreatePreviewSnapshot(RacingFlag.Checkered)))
                flag.Save(Path.Combine(directory, "Flag - Checkered.png"), System.Drawing.Imaging.ImageFormat.Png);
            return 0;
        }

        private static void RunDashboard(
            IRacingTelemetrySource telemetry,
            DashboardRuntime runtime,
            DateTime? finishAt,
            string ledPort)
        {
            while (!ShouldStop(finishAt))
            {
                try
                {
                    using (var screen = new Dv5Screen())
                    {
                        var connection = screen.Connect(runtime.Settings.General.ScreenBrightness);
                        Console.WriteLine("Screen connected: " + connection.ModelName);
                        Console.WriteLine("Display ID: " + connection.DisplayId);
                        var leds = new LedLink(ledPort, LedFrame.Count, "SSG DV 5", DevicePorts.Dv5);
                        GeneralSettings general = runtime.Settings.General;
                        var brow = new LedLink(general.BrowPort, general.BrowLedCount, "SSG LB-10", DevicePorts.LightBar) { Enabled = general.BrowEnabled };
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

                                TelemetrySnapshot snapshot = telemetry.GetSnapshot();

                                // Pick up saved settings and switch dashboards when the
                                // track type changes, without restarting the process.
                                if (DateTime.UtcNow >= nextSettingsCheckUtc)
                                {
                                    nextSettingsCheckUtc = DateTime.UtcNow.AddSeconds(1);
                                    if (runtime.Refresh(snapshot.Discipline))
                                    {
                                        telemetry.ApplyAlertSettings(runtime.Dashboard.Alerts);
                                        screen.SetBrightness(runtime.Settings.General.ScreenBrightness);
                                        lastDrawn = null;
                                        leds.Invalidate();
                                        general = runtime.Settings.General;
                                        brow.Reconfigure(general.BrowPort, general.BrowLedCount, general.BrowEnabled);
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

                                DisplayState current = DisplayState.From(
                                    snapshot, shiftFlashOn, gearAnimationFrame, runtime.Renderer is ILiveTraceRenderer);
                                if (!current.Equals(lastDrawn))
                                {
                                    int previousGear = gearProgress < 1 ? transitionFromGear : snapshot.Gear;
                                    using (var bitmap = runtime.Renderer.Render(
                                        snapshot, shiftFlashOn, previousGear, gearProgress))
                                        screen.Draw(bitmap);
                                    lastDrawn = current;
                                }

                                LedInputs ledInputs = LedInputs.From(snapshot, shiftFlashOn);
                                leds.Send(LedFrame.Compose(runtime.Dashboard, ledInputs));
                                if (brow.Enabled)
                                    brow.Send(LedFrame.ComposeBrow(runtime.Dashboard.BrowLeds, brow.Count, ledInputs));

                                StopRequested.Wait(gearProgress < 1 ? 25 : RefreshIntervalMilliseconds);
                            }
                        }
                        finally
                        {
                            leds.Dispose();
                            brow.Dispose();
                        }
                    }
                }
                catch (Exception exception)
                {
                    if (ShouldStop(finishAt)) return;
                    Console.Error.WriteLine("DV-5 connection lost: " + exception.Message);
                    Console.Error.WriteLine("Retrying in 2 seconds...");
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
            public bool Refresh(string discipline)
            {
                DateTime writeUtc = SettingsStore.LastWriteUtc();
                bool settingsChanged = writeUtc != _settingsWriteUtc;
                bool disciplineChanged = discipline != null && discipline != _discipline;
                if (!settingsChanged && !disciplineChanged) return false;

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
                !string.IsNullOrWhiteSpace(_pinnedDashboardId)
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
        /// One SimHub-protocol LED device: connects lazily, retries every five
        /// seconds while unplugged, and only sends frames that changed.
        /// </summary>
        private sealed class LedLink : IDisposable
        {
            private readonly string _expectedName;
            private readonly string _hardwareId;
            private Dv5LedController _controller;
            private DateTime _nextAttemptUtc = DateTime.MinValue;
            private System.Drawing.Color[] _lastFrame;

            public LedLink(string port, int count, string expectedName, string hardwareId)
            {
                Port = port;
                Count = Math.Max(1, Math.Min(64, count));
                _expectedName = expectedName;
                _hardwareId = hardwareId;
            }

            public string Port { get; private set; }
            public int Count { get; private set; }
            public bool Enabled { get; set; } = true;

            public void Invalidate() => _lastFrame = null;

            public void Reconfigure(string port, int count, bool enabled)
            {
                count = Math.Max(1, Math.Min(64, count));
                if (port != Port || count != Count || enabled != Enabled)
                {
                    Close();
                    Port = port;
                    Count = count;
                    Enabled = enabled;
                    _nextAttemptUtc = DateTime.MinValue;
                }
                _lastFrame = null;
            }

            public void Send(System.Drawing.Color[] frame)
            {
                if (!Enabled || string.IsNullOrWhiteSpace(Port)) return;
                if (_controller == null)
                {
                    if (DateTime.UtcNow < _nextAttemptUtc) return;
                    _nextAttemptUtc = DateTime.UtcNow.AddSeconds(5);
                    // Follow the device if Windows gave it a new port number.
                    string port = DevicePorts.Resolve(Port, _hardwareId);
                    var controller = new Dv5LedController(port, 115200, Count, _expectedName);
                    try
                    {
                        controller.Connect();
                        Console.WriteLine("LED device on " + port + ": " + controller.QueryDeviceName());
                        _controller = controller;
                        _lastFrame = null;
                    }
                    catch (Exception exception)
                    {
                        Console.Error.WriteLine(_expectedName + " unavailable on " + port + ": " + exception.Message);
                        controller.Dispose();
                        return;
                    }
                }
                if (LedFrame.SameFrame(frame, _lastFrame)) return;
                try
                {
                    _controller.SetFrame(frame);
                    _lastFrame = frame;
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine(_expectedName + " lost: " + exception.Message);
                    Close();
                }
            }

            private void Close()
            {
                _controller?.Dispose();
                _controller = null;
                _lastFrame = null;
            }

            public void Dispose() => Close();
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
                TelemetrySnapshot snapshot, bool shiftFlashOn, int gearAnimationFrame, bool liveTrace = false) => new DisplayState(
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
                StrategyHashOf(snapshot) ^ (liveTrace ? snapshot.TraceVersion * 7919 : 0));

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
