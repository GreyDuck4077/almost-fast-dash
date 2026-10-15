using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Brush = System.Windows.Media.Brush;

namespace AlmostFastOverlays
{
    /// <summary>
    /// The screens the dashboard shows on, on the Devices page: the wheel screen's status and "Send only what
    /// changed", and the display chosen for the dashboard on a monitor.
    /// </summary>
    public partial class SettingsWindow
    {
        private bool _screenPresent, _fillingMonitors, _watchingDisplays;
        private DateTime _screenCheckedAt = DateTime.MinValue;

        /// <summary>Whether a wheel screen is plugged in, looked up at most every two seconds.</summary>
        private bool ScreenPresent()
        {
            if (DateTime.UtcNow - _screenCheckedAt < TimeSpan.FromSeconds(2)) return _screenPresent;
            _screenCheckedAt = DateTime.UtcNow;
            try { _screenPresent = WheelScreen.IsPresent(); }
            catch { _screenPresent = false; }
            return _screenPresent;
        }

        /// <summary>The screen's model and picture size, such as 5" M-PRO, 854 × 480.</summary>
        private static string ScreenSize
        {
            get
            {
                System.Drawing.Size size = ScreenSizes.LastScreen(out string model);
                return model + ", " + size.Width + " × " + size.Height;
            }
        }

        /// <summary>The wheel screen's status: showing the dashboard, plugged in, or not found.</summary>
        private void RefreshScreenStatus()
        {
            string text, tone;
            if (!ScreenPresent())
            {
                text = "Not found. Check it's plugged in by USB. VoCore-family M-PRO screens are supported.";
                tone = "Brush.Warning";
            }
            else if (_dashboardRunning)
            {
                // The running dashboard keeps screen.link while it's connected to the screen.
                bool linked = File.Exists(ScreenSender.LinkPath);
                text = linked ? "Connected · " + ScreenSize + " · showing the dashboard"
                    : "Connecting… If this stays, close other dashboard software that may be using the screen.";
                tone = linked ? "Brush.Success" : "Brush.Warning";
            }
            else
            {
                text = "Plugged in · " + ScreenSize + " · not in use right now";
                tone = "Brush.TextTertiary";
            }
            ScreenStatusText.Text = text;
            ScreenStatusDot.Fill = (Brush)FindResource(tone);
        }

        private void SendChanges_Click(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _settings.General.SendChangesOnly = SendChangesToggle.IsChecked == true;
            Save();
        }

        private void FrameRate_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _settings.General.FrameRate = FrameRate30.IsChecked == true ? 30 : 60;
            Save();
        }

        /// <summary>
        /// The displays the dashboard can go on (Off, then every display but the main one), with the chosen one
        /// kept in the list while it's unplugged. Filled again whenever Windows' displays change.
        /// </summary>
        private void FillMonitors()
        {
            if (!_watchingDisplays)
            {
                _watchingDisplays = true;
                SystemEvents.DisplaySettingsChanged += DisplaysChanged;
                Closed += (s, e) => SystemEvents.DisplaySettingsChanged -= DisplaysChanged;
            }
            _fillingMonitors = true;
            string chosen = _settings.General.MonitorDevice ?? "";
            List<MonitorChoice> displays = MonitorChoice.All();
            MonitorPicker.Items.Clear();
            MonitorPicker.Items.Add(new ComboBoxItem { Content = "Off", Tag = "" });
            foreach (MonitorChoice display in displays) MonitorPicker.Items.Add(new ComboBoxItem { Content = display.Label, Tag = display.Device });
            if (chosen.Length > 0 && displays.All(d => d.Device != chosen))
                MonitorPicker.Items.Add(new ComboBoxItem { Content = "Display " + new string(chosen.Where(char.IsDigit).ToArray()) + " (not plugged in)", Tag = chosen });
            MonitorPicker.SelectedItem = MonitorPicker.Items.Cast<ComboBoxItem>().First(i => (string)i.Tag == chosen || (chosen.Length == 0 && (string)i.Tag == ""));
            MonitorNote.Text = displays.Count == 0 ? "Plug in a second display to use this." : "";
            _fillingMonitors = false;
        }

        private void DisplaysChanged(object sender, EventArgs e) => Dispatcher.BeginInvoke(new Action(FillMonitors));

        private void MonitorPicker_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || _fillingMonitors) return;
            string device = (MonitorPicker.SelectedItem as ComboBoxItem)?.Tag as string;
            _settings.General.MonitorDevice = string.IsNullOrEmpty(device) ? null : device;
            Save();
        }
    }
}
