using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AlmostFastDash.UI
{
    public partial class MainWindow : Window
    {
        // ------------------------------------------------------------------
        // Dashboard process control
        // ------------------------------------------------------------------

        private async void StartDashboard_Click(object sender, RoutedEventArgs e) => await StartDashboardAsync();

        /// <summary>Starts the dashboard (restarting it if it's running) and updates the header.</summary>
        private async Task StartDashboardAsync()
        {
            FlushSave();
            _dashboardOperationInProgress = true;
            StartButton.IsEnabled = false;
            bool wasRunning = IsDashboardRunning();
            if (wasRunning)
            {
                SetStatus("Stopping…", "Brush.Warning");
                Launch("--stop");
                await WaitForDashboardState(false, 10000);
            }
            SetStatus(wasRunning ? "Restarting…" : "Starting…", "Brush.Warning");
            ReleaseDevices();
            Launch("--run");
            bool running = await WaitForDashboardState(true, 10000);
            _dashboardOperationInProgress = false;
            StartButton.IsEnabled = true;
            if (running) RefreshDashboardStatus(); else SetStopped("Couldn't start");
        }

        private async void StopDashboard_Click(object sender, RoutedEventArgs e) => await StopDashboardAsync();

        /// <summary>Stops the dashboard and updates the header; true once it has stopped.</summary>
        private async Task<bool> StopDashboardAsync()
        {
            _dashboardOperationInProgress = true;
            SetStatus("Stopping…", "Brush.Warning");
            Launch("--stop");
            bool stopped = await WaitForDashboardState(false, 10000);
            _dashboardOperationInProgress = false;
            if (stopped) RefreshDashboardStatus(); else SetStatus("Didn't stop", "Brush.Danger");
            return stopped;
        }

        private static Process Launch(string arguments) => Process.Start(new ProcessStartInfo
        {
            FileName = Assembly.GetEntryAssembly().Location,
            Arguments = arguments,
            WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
            UseShellExecute = true
        });

        private async Task<bool> WaitForDashboardState(bool expectedRunning, int timeoutMilliseconds)
        {
            int waited = 0;
            while (waited < timeoutMilliseconds)
            {
                if (IsDashboardRunning() == expectedRunning) return true;
                await Task.Delay(200);
                waited += 200;
            }
            return IsDashboardRunning() == expectedRunning;
        }

        private static bool IsDashboardRunning()
        {
            try
            {
                if (!File.Exists(DashboardRunner.DashboardStatusPath)) return false;
                if (!int.TryParse(File.ReadAllText(DashboardRunner.DashboardStatusPath).Trim(), out int pid)) return false;
                Process process = Process.GetProcessById(pid);
                // The exe was called AlmostFastOverlays before the rename; either is ours.
                return !process.HasExited && (string.Equals(process.ProcessName, "AlmostFastDash", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(process.ProcessName, "AlmostFastOverlays", StringComparison.OrdinalIgnoreCase));
            }
            catch { return false; }
        }

        private void RefreshDashboardStatus()
        {
            _dashboardRunning = IsDashboardRunning();
            if (!_dashboardRunning)
            {
                SetStopped("Stopped");
                return;
            }
            SetStatus("Running", "Brush.Success");
            SetRunButton(StartButton, StartIcon, false, "Restart the dashboard");
            StopButton.IsEnabled = true;
            EndWelcome();
            string active = null, discipline = null;
            try
            {
                string[] lines = File.ReadAllLines(DashboardRunner.ActiveDashboardPath);
                active = lines.Length > 0 ? lines[0].Trim() : null;
                discipline = lines.Length > 1 ? lines[1].Trim() : null;
            }
            catch { }
            DashboardSettings shown = _settings.Dashboards.FirstOrDefault(d => d.Id == active);
            ActiveText.Text = shown == null ? "" : " · " + shown.Name;
            StatusPill.ToolTip = shown == null ? null
                : "Showing " + shown.Name + (string.IsNullOrEmpty(discipline) ? "" : " for a " + discipline.ToLowerInvariant() + " track");
            // Hidden by Show when at this point in iRacing (the running dashboard notes it in dashboard.hidden).
            string hidden = null;
            try { if (File.Exists(DashboardRunner.HiddenPath)) hidden = File.ReadAllText(DashboardRunner.HiddenPath).Trim(); }
            catch { }
            if (!string.IsNullOrEmpty(hidden) && Enum.TryParse(hidden, out CarState at))
            {
                SetStatus("Hidden", "Brush.TextTertiary");
                ActiveText.Text = " · " + AtPoint(at);
                StatusPill.ToolTip = "Running, hidden while " + AtPoint(at) + " (see Show when).";
            }
        }

        private void SetStopped(string text)
        {
            SetStatus(text, "Brush.TextTertiary");
            SetRunButton(StartButton, StartIcon, true, "Start the dashboard");
            StopButton.IsEnabled = false;
            ActiveText.Text = " · " + _settings.GetDefaultDashboard().Name;
            StatusPill.ToolTip = "Starts with " + _settings.GetDefaultDashboard().Name + ", the default dashboard";
        }

        private void SetStatus(string text, string brushKey)
        {
            StatusText.Text = text;
            StatusDot.Fill = (Brush)FindResource(brushKey);
        }

        /// <summary>
        /// The run controls in the header share one look: a filled play button when
        /// something is stopped, and a quiet secondary button (restart or stop) while it runs.
        /// </summary>
        private void SetRunButton(Button button, TextBlock icon, bool start, string tip, string runningGlyph = "\uE72C")
        {
            button.Style = (Style)FindResource(start ? "Btn.RunPrimary" : "Btn.RunIcon");
            icon.Text = start ? "\uE768" : runningGlyph;
            icon.Foreground = start ? (Brush)FindResource("Brush.OnAccent") : (Brush)FindResource("Brush.Text");
            button.ToolTip = tip;
        }
    }
}
