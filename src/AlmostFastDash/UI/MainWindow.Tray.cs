using System;
using System.Windows;
using Forms = System.Windows.Forms;

namespace AlmostFastDash.UI
{
    /// <summary>
    /// The notification-area icon: with Settings › Minimize to the system tray on, minimizing hides the
    /// window there. The icon reopens it, and its menu starts or stops the dashboard or quits.
    /// </summary>
    public partial class MainWindow
    {
        private Forms.NotifyIcon _tray;

        private void SetUpTray()
        {
            StateChanged += (s, e) =>
            {
                // Nothing to show while minimized: the preview pauses (and lets go of the devices) until the window is back.
                if (WindowState == WindowState.Minimized)
                {
                    _ledTimer.Stop();
                    ReleaseDevices();
                    if (_settings.General.MinimizeToTray) HideToTray();
                }
                else if (EditorPage.Visibility == Visibility.Visible) _ledTimer.Start();
            };
            Closed += (s, e) => { if (_tray != null) { _tray.Visible = false; _tray.Dispose(); _tray = null; } };
        }

        private void HideToTray()
        {
            if (_tray == null)
            {
                _tray = new Forms.NotifyIcon { Text = "Almost Fast Dash" };
                try
                {
                    var stream = Application.GetResourceStream(new Uri("pack://application:,,,/AlmostFastDash;component/Assets/AppIcon.ico"))?.Stream;
                    if (stream != null) using (stream) _tray.Icon = new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
                }
                catch { }
                if (_tray.Icon == null) _tray.Icon = System.Drawing.SystemIcons.Application;
                _tray.MouseClick += (s, e) => { if (e.Button == Forms.MouseButtons.Left) RestoreFromTray(); };
                var menu = new Forms.ContextMenuStrip();
                menu.Items.Add("Open Almost Fast Dash", null, (s, e) => RestoreFromTray());
                var dashboard = new Forms.ToolStripMenuItem();
                dashboard.Click += async (s,
                    e) => { if (IsDashboardRunning()) await StopDashboardAsync(); else await StartDashboardAsync(); };
                menu.Items.Add(dashboard);
                menu.Items.Add(new Forms.ToolStripSeparator());
                menu.Items.Add("Quit", null, (s, e) => Close());
                // The dashboard item says what it will do when the menu opens.
                menu.Opening += (s, e) => dashboard.Text = IsDashboardRunning() ? "Stop the dashboard" : "Start the dashboard";
                _tray.ContextMenuStrip = menu;
            }
            _tray.Visible = true;
            Hide();
        }

        private void RestoreFromTray()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            if (_tray != null) _tray.Visible = false;
        }
    }
}
