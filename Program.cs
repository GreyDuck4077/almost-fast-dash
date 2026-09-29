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

            bool explicitDashboardMode = args.Contains("--run") || args.Contains("--demo") ||
                args.Contains("--preview") || args.Contains("--preview-timed") || args.Contains("--seconds") ||
                args.Contains("--capture-preview") || args.Contains("--capture-dashboard");
            if (!explicitDashboardMode)
            {
                var application = new Application
                {
                    ShutdownMode = ShutdownMode.OnMainWindowClose
                };
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
            string ledPort = ParseOption(args, "--led-port") ?? DefaultLedPort;
            Console.WriteLine("DV-5 Direct");
            Console.WriteLine("Close SimHub before running so it does not compete for the device.");

            Console.CancelKeyPress += OnCancelKeyPress;

            AppSettings allSettings = SettingsStore.Load();
            string captureDashboardId = ParseOption(args, "--capture-dashboard");
            DashboardSettings dashboardSettings = string.IsNullOrWhiteSpace(captureDashboardId)
                ? allSettings.GetDefaultDashboard()
                : allSettings.Dashboards.FirstOrDefault(d => d.Id == captureDashboardId) ?? allSettings.GetDefaultDashboard();
            using (var renderer = DashboardRendererFactory.Create(Dv5Screen.Width, Dv5Screen.Height, dashboardSettings))
            using (var diagnostics = new DiagnosticLogger(!args.Contains("--no-logging")))
            using (var telemetry = new IRacingTelemetrySource(diagnostics, dashboardSettings))
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
                        using (var capture = renderer.Render(
                            captureSnapshot, false, captureSnapshot.Gear, 1f))
                            capture.Save(capturePath, System.Drawing.Imaging.ImageFormat.Png);
                    }

                    DateTime? finishAt = runSeconds.HasValue
                        ? DateTime.UtcNow.AddSeconds(runSeconds.Value)
                        : (DateTime?)null;

                    RunDashboard(telemetry, renderer, finishAt, ledPort, dashboardSettings);

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
                            File.Delete(DashboardStatusPath);
                    }
                    catch { }
                    _instanceMutex.ReleaseMutex();
                    _instanceMutex.Dispose();
                }
            }
        }

        private static void RunDashboard(
            IRacingTelemetrySource telemetry,
            IDashboardRenderer renderer,
            DateTime? finishAt,
            string ledPort,
            DashboardSettings dashboardSettings)
        {
            while (!ShouldStop(finishAt))
            {
                try
                {
                    using (var screen = new Dv5Screen())
                    {
                        var connection = screen.Connect();
                        Console.WriteLine("Screen connected: " + connection.ModelName);
                        Console.WriteLine("Display ID: " + connection.DisplayId);
                        Dv5LedController leds = null;
                        DateTime nextLedReconnectUtc = DateTime.MinValue;

                        DisplayState lastDrawn = null;
                        bool? lastTelemetryConnection = null;
                        BlindSpotZone lastLeft = (BlindSpotZone)(-1);
                        BlindSpotZone lastRight = (BlindSpotZone)(-1);
                        int lastRpmPairs = -1;
                        bool lastShiftFlash = false;
                        int lastObservedGear = int.MinValue;
                        int transitionFromGear = 0;
                        DateTime gearTransitionStartedUtc = DateTime.MinValue;

                        try
                        {
                            while (!ShouldStop(finishAt))
                            {
                                if (leds == null && DateTime.UtcNow >= nextLedReconnectUtc)
                                {
                                    leds = TryConnectLeds(ledPort);
                                    nextLedReconnectUtc = DateTime.UtcNow.AddSeconds(5);
                                    if (leds != null)
                                    {
                                        lastLeft = (BlindSpotZone)(-1);
                                        lastRight = (BlindSpotZone)(-1);
                                        lastRpmPairs = -1;
                                        lastShiftFlash = false;
                                    }
                                }

                                TelemetrySnapshot snapshot = telemetry.GetSnapshot();
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

                            DisplayState current = DisplayState.From(snapshot, shiftFlashOn, gearAnimationFrame);
                            if (!current.Equals(lastDrawn))
                            {
                                int previousGear = gearProgress < 1 ? transitionFromGear : snapshot.Gear;
                                using (var bitmap = renderer.Render(
                                    snapshot, shiftFlashOn, previousGear, gearProgress))
                                    screen.Draw(bitmap);
                                lastDrawn = current;
                            }

                                int rpmPairs = (int)Math.Ceiling(snapshot.ShiftIndicatorPct * 4);
                                if (leds != null &&
                                    (snapshot.LeftBlindSpot != lastLeft || snapshot.RightBlindSpot != lastRight ||
                                     rpmPairs != lastRpmPairs || shiftFlashOn != lastShiftFlash))
                                {
                                    try
                                    {
                                        leds.SetIndicators(
                                            snapshot.LeftBlindSpot, snapshot.RightBlindSpot, rpmPairs, shiftFlashOn,
                                            dashboardSettings);
                                        lastLeft = snapshot.LeftBlindSpot;
                                        lastRight = snapshot.RightBlindSpot;
                                        lastRpmPairs = rpmPairs;
                                        lastShiftFlash = shiftFlashOn;
                                    }
                                    catch (Exception ledException)
                                    {
                                        Console.Error.WriteLine("LED controller lost: " + ledException.Message);
                                        leds.Dispose();
                                        leds = null;
                                        nextLedReconnectUtc = DateTime.UtcNow.AddSeconds(5);
                                    }
                                }

                                StopRequested.Wait(gearProgress < 1 ? 25 : RefreshIntervalMilliseconds);
                            }
                        }
                        finally
                        {
                            leds?.Dispose();
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

        private static Dv5LedController TryConnectLeds(string ledPort)
        {
            var leds = new Dv5LedController(ledPort, 115200, 16);
            try
            {
                leds.Connect();
                Console.WriteLine("LED controller: " + leds.QueryDeviceName());
                return leds;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("LED controller unavailable: " + exception.Message);
                leds.Dispose();
                return null;
            }
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
                int maxForwardGear, int gearAnimationFrame)
            {
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

            public static DisplayState From(
                TelemetrySnapshot snapshot, bool shiftFlashOn, int gearAnimationFrame) => new DisplayState(
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
                gearAnimationFrame);

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
                    GearAnimationFrame == other.GearAnimationFrame;
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
                    return (hash * 397) ^ GearAnimationFrame;
                }
            }
        }
    }
}
