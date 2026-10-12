using System;
using System.IO;

namespace AlmostFastOverlays
{
    /// <summary>
    /// Lets the dashboard page's live preview show on the wheel while the dashboard is running.
    /// The settings window keeps a small file fresh while its preview is open (which dashboard,
    /// which state); the running dashboard, which owns the screen and LEDs, shows that state
    /// whenever iRacing isn't connected. Live telemetry always wins, and a request that hasn't
    /// been refreshed for a few seconds (the window closed or moved on) is ignored.
    /// </summary>
    internal sealed class PreviewRequest
    {
        public static readonly string[] States =
            { "Racing", "Shift point", "Pit lane", "Cars alongside", "Green flag", "Yellow flag", "White flag", "Blue flag", "Black flag", "Checkered flag" };

        private static readonly string FilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview.request");
        private const double FreshSeconds = 3;

        private readonly object _gate = new object();
        private readonly SyntheticTelemetry _synthetic = new SyntheticTelemetry();
        private DateTime _nextReadUtc;

        public string DashboardId { get; private set; }
        public int State { get; private set; }
        public bool Animate { get; private set; }

        /// <summary>True while a preview has been asked for recently.</summary>
        public bool Active => DashboardId != null;

        // ------------------------------------------------------------------ settings window side

        /// <summary>Asks a running dashboard to show this preview (call at least every second while it's open).</summary>
        public static void Write(string dashboardId, int state, bool animate)
        {
            try { File.WriteAllText(FilePath, dashboardId + "\n" + state + "\n" + (animate ? "1" : "0")); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public static void Clear()
        {
            try { if (File.Exists(FilePath)) File.Delete(FilePath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        /// <summary>The telemetry a preview state stands for; the racing state animates when asked.</summary>
        public static TelemetrySnapshot ForState(int state, bool animate, SyntheticTelemetry synthetic)
        {
            switch (state)
            {
                case 1: return LightsWindow.Scenario("Shift point");
                case 2: return IRacingTelemetrySource.CreatePitLanePreviewSnapshot();
                case 3: return IRacingTelemetrySource.CreateSpotterPreviewSnapshot();
                case 4: return IRacingTelemetrySource.CreatePreviewSnapshot(RacingFlag.Green);
                case 5: return IRacingTelemetrySource.CreatePreviewSnapshot(RacingFlag.Yellow);
                case 6: return IRacingTelemetrySource.CreatePreviewSnapshot(RacingFlag.White);
                case 7: return IRacingTelemetrySource.CreatePreviewSnapshot(RacingFlag.BlueYellowStripe);
                case 8: return IRacingTelemetrySource.CreatePreviewSnapshot(RacingFlag.Black);
                case 9: return IRacingTelemetrySource.CreatePreviewSnapshot(RacingFlag.Checkered);
                default: return animate ? synthetic.Next(out _, out _, out _) : IRacingTelemetrySource.CreatePreviewSnapshot();
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
                DashboardId = null;
                try
                {
                    var file = new FileInfo(FilePath);
                    if (!file.Exists || (DateTime.UtcNow - file.LastWriteTimeUtc).TotalSeconds > FreshSeconds) return;
                    string[] lines = File.ReadAllLines(FilePath);
                    if (lines.Length < 3 || string.IsNullOrWhiteSpace(lines[0])) return;
                    State = int.TryParse(lines[1], out int state) ? state : 0;
                    Animate = lines[2].Trim() == "1";
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
                return Active ? ForState(State, Animate, _synthetic) : live;
        }
    }
}
