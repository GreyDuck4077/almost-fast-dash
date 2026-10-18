using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;

namespace AlmostFastDash.UI
{
    public partial class MainWindow : Window
    {
        // ------------------------------------------------------------------
        // Device & app settings
        // ------------------------------------------------------------------

        private void LoadGeneralSettings()
        {
            GeneralSettings general = _settings.General;
            ScreenBrightness.Value = general.ScreenBrightness;
            ScreenBrightnessValue.Text = general.ScreenBrightness + "%";
            SendChangesToggle.IsChecked = general.SendChangesOnly;
            FrameRate30.IsChecked = general.FrameRate == 30;
            FrameRate60.IsChecked = general.FrameRate != 30;
            LoadPcOutput();
            SpeedKph.IsChecked = string.Equals(general.SpeedUnit, "KPH", StringComparison.OrdinalIgnoreCase);
            SpeedMph.IsChecked = SpeedKph.IsChecked != true;
            FuelGallons.IsChecked = string.Equals(general.FuelUnit, "Gallons", StringComparison.OrdinalIgnoreCase);
            FuelLiters.IsChecked = FuelGallons.IsChecked != true;
            FuelReserveHalf.IsChecked = Math.Abs(general.FuelReserveLaps - 0.5) < 0.01;
            FuelReserve1.IsChecked = Math.Abs(general.FuelReserveLaps - 1) < 0.01;
            FuelReserve2.IsChecked = Math.Abs(general.FuelReserveLaps - 2) < 0.01;
            FuelReserve0.IsChecked = FuelReserveHalf.IsChecked != true && FuelReserve1.IsChecked != true && FuelReserve2.IsChecked != true;
            TempC.IsChecked = new UnitSystem(general).Celsius;
            TempF.IsChecked = TempC.IsChecked != true;
            OpenDevices.IsChecked = general.StartPage == "Devices";
            OpenSettings.IsChecked = general.StartPage == "Settings";
            OpenDashboards.IsChecked = OpenDevices.IsChecked != true && OpenSettings.IsChecked != true;
            TrayToggle.IsChecked = general.MinimizeToTray;
            UpdatesToggle.IsChecked = general.CheckForUpdates;
            AutoSwitchToggle.IsChecked = general.AutoSwitchByDiscipline;
            DiagnosticsToggle.IsChecked = general.DiagnosticsEnabled;
            MoveOldStartupShortcut();
            StartupToggle.IsChecked = File.Exists(StartupShortcutPath);
        }

        private void Units_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _settings.General.SpeedUnit = SpeedKph.IsChecked == true ? "KPH" : "MPH";
            _settings.General.FuelUnit = FuelGallons.IsChecked == true ? "Gallons" : "Liters";
            _settings.General.FuelReserveLaps = FuelReserve2.IsChecked == true ? 2 : FuelReserve1.IsChecked == true ? 1 : FuelReserveHalf.IsChecked == true ? 0.5 : 0;
            if (TempC.IsChecked == true || TempF.IsChecked == true) _settings.General.TemperatureUnit = TempC.IsChecked == true ? "Celsius" : "Fahrenheit";
            Save();
        }

        private void StartPage_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _settings.General.StartPage = OpenDevices.IsChecked == true ? "Devices" : OpenSettings.IsChecked == true ? "Settings" : "Dashboards";
            Save();
        }

        private void Tray_Click(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _settings.General.MinimizeToTray = TrayToggle.IsChecked == true;
            Save();
        }

        private void AutoSwitch_Click(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _settings.General.AutoSwitchByDiscipline = AutoSwitchToggle.IsChecked == true;
            Save();
        }

        private string _updatePage;

        /// <summary>With the check turned on, shows a link in the sidebar when GitHub has a newer release.</summary>
        private async void CheckForUpdate()
        {
            if (!_settings.General.CheckForUpdates || Offline) return;
            Tuple<Version, string> newer = await UpdateCheck.NewerAsync(Assembly.GetExecutingAssembly().GetName().Version);
            if (newer == null) return;
            _updatePage = newer.Item2;
            UpdateRun.Text = "Version " + newer.Item1.Major + "." + newer.Item1.Minor + " is out. Download it";
            UpdateText.Visibility = Visibility.Visible;
            Motion.FadeIn(UpdateText, 300);
        }

        private void UpdateLink_Click(object sender, RoutedEventArgs e)
        {
            try { Process.Start(_updatePage ?? UpdateCheck.Releases); } catch { }
        }

        private void Updates_Click(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _settings.General.CheckForUpdates = UpdatesToggle.IsChecked == true;
            Save();
            if (_settings.General.CheckForUpdates) CheckForUpdate();
            else UpdateText.Visibility = Visibility.Collapsed;
        }

        private void Diagnostics_Click(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _settings.General.DiagnosticsEnabled = DiagnosticsToggle.IsChecked == true;
            Save(IsDashboardRunning() ? "Saved · applies next time the dashboard starts" : "Saved");
        }

        private static string StartupShortcutPath =>
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Almost Fast Dash.lnk");

        /// <summary>A start-with-Windows shortcut made before the rename points at the old exe; replace it with one for this one.</summary>
        private static void MoveOldStartupShortcut()
        {
            string old = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Almost Fast Dash.lnk");
            try
            {
                if (!File.Exists(old)) return;
                CreateStartupShortcut();
                File.Delete(old);
            }
            catch { }
        }

        private void Startup_Click(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            try
            {
                if (StartupToggle.IsChecked == true) CreateStartupShortcut();
                else if (File.Exists(StartupShortcutPath)) File.Delete(StartupShortcutPath);
                ShowSaved(StartupToggle.IsChecked == true ? "Will start with Windows" : "Won't start with Windows");
            }
            catch (Exception ex)
            {
                _loading = true;
                StartupToggle.IsChecked = File.Exists(StartupShortcutPath);
                _loading = false;
                ShowSaved("Couldn't update startup: " + ex.Message);
            }
        }

        private static void CreateStartupShortcut()
        {
            // WScript.Shell is the supported way to write .lnk files from .NET Framework.
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(shellType);
            try
            {
                object shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { StartupShortcutPath });
                Type t = shortcut.GetType();
                string exe = Assembly.GetEntryAssembly().Location;
                t.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { exe });
                t.InvokeMember("Arguments", BindingFlags.SetProperty, null, shortcut, new object[] { "--run" });
                t.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { System.IO.Path.GetDirectoryName(exe) });
                t.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { "Start the Almost Fast Dash iRacing dashboard automatically" });
                t.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
                Marshal.FinalReleaseComObject(shortcut);
            }
            finally
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }

        private void OpenLogs_Click(object sender, RoutedEventArgs e)
        {
            string logs = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            Directory.CreateDirectory(logs);
            Process.Start("explorer.exe", "\"" + logs + "\"");
        }

        /// <summary>About's links (the guide, what's new, reporting a problem, credits) open in the browser.</summary>
        private void AboutLink_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.Tag is string url)) return;
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }

        private void OpenSettingsFolder_Click(object sender, RoutedEventArgs e)
        {
            if (File.Exists(SettingsStore.FilePath)) Process.Start("explorer.exe", "/select,\"" + SettingsStore.FilePath + "\"");
            else Process.Start("explorer.exe", "\"" + AppDomain.CurrentDomain.BaseDirectory + "\"");
        }
    }
}
