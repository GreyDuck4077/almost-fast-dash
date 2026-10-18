using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Diagnostics;

namespace AlmostFastDash.Runner
{
    /// <summary>
    /// The dashboard itself (--run): reads iRacing, draws the screen, lights the LEDs and sends both out, until
    /// it's asked to stop. One runs at a time; the app's window starts and stops it, and talks to it through small
    /// files next to the exe.
    /// </summary>
    internal static class DashboardRunner
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

        /// <summary>Asks a running dashboard to stop (--stop), by file and by named event.</summary>
        internal static void RequestStop()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StopRequestPath));
            File.WriteAllText(StopRequestPath, DateTime.UtcNow.ToString("O"));
            StopSignal.Set();
        }

        /// <summary>Runs the dashboard until it's stopped. Returns 2 when another one is already running.</summary>
        internal static int Run()
        {
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

        private static void RunDashboard(
            IRacingTelemetrySource telemetry,
            DashboardRuntime runtime,
            DiagnosticLogger diagnostics)
        {
            // While iRacing isn't connected, the main window's live preview can show on the wheel.
            var preview = new PreviewRequest();
            var stats = new ScreenStats();
            MonitorOutput monitor = null;
            // The LEDs and the wheel screen each have a thread of their own: the screen connects, reconnects and
            // sends by itself, so this loop only decides what to draw and when, and draws the next frame while
            // the last one is still being sent. Their frame waits need Windows' 1 ms timer to keep 60 a second.
            using (new FineTimer())
            using (new LightLoop(() => preview.Apply(telemetry.GetSnapshot()), () => runtime.Settings, () => runtime.Dashboard,
                diagnostics.RecordLight))
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
                            monitor = MonitorOutput.Follow(monitor, runtime.Settings.General, runtime.Dashboard.Name,
                                runtime.Dashboard.ScreenSize());
                        }
                        // Show when (at the top of the main window): the dashboard shows only at the chosen points in
                        // iRacing. Before iRacing is running, and for a preview, it shows as it always has.
                        GeneralSettings general = runtime.Settings.General;
                        bool shown = Shown(snapshot, general);
                        NoteHidden(shown ? (CarState?)null : snapshot.CarState);
                        // The dashboard on the PC follows it. Over the game it also waits for iRacing (or a preview)
                        // unless it's wanted always, and it's unlocked while the main window is placing it.
                        if (monitor != null)
                        {
                            bool overlay = monitor.Mode == MonitorOutput.Overlay;
                            monitor.Update(general.OverlayOpacity,
                                shown && (!overlay || !general.OverlayOnlyWhileRacing || snapshot.Connected),
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
                            bool next = WheelButtons.IsDown(buttons, design.PageButton),
                                back = WheelButtons.IsDown(buttons, design.PageBackButton);
                            if (next && !pageButtonHeld) runtime.Renderer.StepPage(1);
                            if (back && !backButtonHeld) runtime.Renderer.StepPage(-1);
                            pageButtonHeld = next;
                            backButtonHeld = back;
                        }

                        // Taps on a touch wheel screen, or clicks and touches on the dashboard on the PC, move tap zones on to their next
                        // view.
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
                            Tap((tap.X - (screen.Width - built.Width * scale) / 2) / scale,
                                (tap.Y - (screen.Height - built.Height * scale) / 2) / scale);
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
                                blank = new System.Drawing.Bitmap(size.Width, size.Height,
                                    System.Drawing.Imaging.PixelFormat.Format32bppArgb);
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
                        if (wheel.Generation != wheelGeneration || monitor != lastMonitor ||
                            (monitor != null && monitor.Generation != monitorGeneration))
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
                            long now = System.Diagnostics.Stopwatch.GetTimestamp(),
                                step = (long)(period * System.Diagnostics.Stopwatch.Frequency / 1000);
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
        /// Tells the main window when the running dashboard is hidden by Show when, and at which point in iRacing
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

        /// <summary>Appends an unexpected error to Logs\crash.log, so a dashboard that stopped can be looked into.</summary>
        private static void WriteCrash(Exception exception)
        {
            if (exception == null) return;
            try
            {
                string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
                Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, "crash.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + exception + Environment.NewLine + Environment.NewLine);
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
    }
}
