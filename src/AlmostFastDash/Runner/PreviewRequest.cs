using System;
using System.IO;

namespace AlmostFastDash.Runner
{
    /// <summary>
    /// The preview states shared by the dashboard page and the light designer, and the way the
    /// dashboard page's preview reaches the wheel while the dashboard is running. The settings
    /// window keeps a small file fresh while its preview is open (which dashboard, which state);
    /// the running dashboard, which owns the screen and LEDs, shows that state whenever iRacing
    /// isn't connected. Live telemetry always wins, and a request that hasn't been refreshed for
    /// a few seconds (the window closed or moved on) is ignored.
    /// </summary>
    internal sealed class PreviewRequest
    {
        public const string AnimatedLap = "Animated lap", LiveIRacing = "Live iRacing";

        /// <summary>Every preview state, in menu order. Each one is exactly the telemetry the dashboard and lights are shown with.</summary>
        public static readonly string[] States =
        {
            AnimatedLap, LiveIRacing, "Racing", "Shift point", "Car left", "Car right", "Three wide", "Pit limiter", "Over pit limit",
            "Low fuel", "ABS working", "Green flag", "Yellow flag", "White flag", "Blue flag", "Black flag", "Checkered flag", "Not connected"
        };

        private static readonly string FilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview.request");
        private const double FreshSeconds = 3;

        private readonly object _gate = new object();
        private readonly SyntheticTelemetry _synthetic = new SyntheticTelemetry();
        private DateTime _nextReadUtc;

        public string DashboardId { get; private set; }
        public string State { get; private set; }

        /// <summary>True while a preview other than live data has been asked for recently.</summary>
        public bool Active => DashboardId != null && State != LiveIRacing;

        // ------------------------------------------------------------------ settings window side

        /// <summary>Asks a running dashboard to show this preview (call at least every second while it's open).</summary>
        public static void Write(string dashboardId, string state)
        {
            try { File.WriteAllText(FilePath, dashboardId + "\n" + state); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public static void Clear()
        {
            try { if (File.Exists(FilePath)) File.Delete(FilePath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        /// <summary>
        /// The telemetry a preview state stands for. Animated lap plays <paramref name="synthetic"/>;
        /// Live iRacing is <paramref name="live"/> (or not connected without it).
        /// </summary>
        public static TelemetrySnapshot ForState(string state, SyntheticTelemetry synthetic, TelemetrySnapshot live = null)
        {
            switch (state)
            {
                case AnimatedLap: return synthetic.Next(out _, out _, out _);
                case LiveIRacing: return live ?? TelemetrySnapshot.Disconnected;
                default: return LightsWindow.Scenario(state);
            }
        }

        // ------------------------------------------------------------------ dashboard side

        /// <summary>Re-reads the request at most four times a second.</summary>
        public void Refresh()
        {
            lock (_gate)
            {
                if (DateTime.UtcNow < _nextReadUtc) return;
                _nextReadUtc = DateTime.UtcNow.AddMilliseconds(250);
                try
                {
                    var file = new FileInfo(FilePath);
                    if (!file.Exists || (DateTime.UtcNow - file.LastWriteTimeUtc).TotalSeconds > FreshSeconds)
                    {
                        DashboardId = null;
                        return;
                    }
                    // A file caught while the settings window rewrites it reads empty or locked: keep the last request
                    // rather than dropping to the default dashboard for a moment (two full redraws and a flash).
                    string[] lines = File.ReadAllLines(FilePath);
                    if (lines.Length < 2 || string.IsNullOrWhiteSpace(lines[0]) || Array.IndexOf(States, lines[1].Trim()) < 0) return;
                    State = lines[1].Trim();
                    DashboardId = lines[0].Trim();
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        /// <summary>Live telemetry when iRacing is connected, otherwise the requested preview (if any).</summary>
        public TelemetrySnapshot Apply(TelemetrySnapshot live)
        {
            if (live != null && live.Connected) return live;
            Refresh();
            lock (_gate)
                return Active ? ForState(State, _synthetic, live) : live;
        }
    }
}
