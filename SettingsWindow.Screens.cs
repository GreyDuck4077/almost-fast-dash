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
    /// changed", and the dashboard on the PC: in a window, over the game, or full screen on another display.
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

        // ------------------------------------------------------------------ the dashboard on the PC

        private bool _pcWired;
        private System.Windows.Threading.DispatcherTimer _placing;

        /// <summary>Shows the saved choice of where the dashboard appears on the PC, and its settings.</summary>
        private void LoadPcOutput()
        {
            GeneralSettings general = _settings.General;
            if (!_pcWired)
            {
                _pcWired = true;
                OverlayOpacity.ValueChanged += (s, e) =>
                {
                    OverlayOpacityValue.Text = (int)OverlayOpacity.Value + "%";
                    if (_loading) return;
                    _settings.General.OverlayOpacity = (int)OverlayOpacity.Value;
                    Save();
                };
                Closed += (s, e) => StopPlacing();
            }
            string mode = general.EffectivePcOutput();
            foreach (RadioButton choice in new[] { PcOff, PcWindow, PcOverlay, PcFullScreen }) choice.IsChecked = (string)choice.Tag == mode;
            OverlayOpacity.Value = Math.Max(30, Math.Min(100, general.OverlayOpacity));
            OverlayOpacityValue.Text = (int)OverlayOpacity.Value + "%";
            OverlayRacingToggle.IsChecked = general.OverlayOnlyWhileRacing;
            FillMonitors();
            ShowPcRows(mode);
        }

        private void ShowPcRows(string mode)
        {
            PcWindowRows.Visibility = mode == MonitorOutput.Windowed ? Visibility.Visible : Visibility.Collapsed;
            PcOverlayRows.Visibility = mode == MonitorOutput.Overlay ? Visibility.Visible : Visibility.Collapsed;
            PcFullScreenRows.Visibility = mode == MonitorOutput.FullScreen ? Visibility.Visible : Visibility.Collapsed;
            if (mode != MonitorOutput.Overlay) StopPlacing();
        }

        private void PcOutput_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading || !(sender is RadioButton choice) || choice.IsChecked != true) return;
            string mode = (string)choice.Tag;
            _settings.General.PcOutput = mode;
            // Full screen goes on the first other display unless one was chosen before.
            if (mode == MonitorOutput.FullScreen && string.IsNullOrEmpty(_settings.General.MonitorDevice))
            {
                _settings.General.MonitorDevice = MonitorChoice.All().FirstOrDefault()?.Device;
                FillMonitors();
            }
            ShowPcRows(mode);
            Save();
        }

        private void OverlayRacing_Click(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _settings.General.OverlayOnlyWhileRacing = OverlayRacingToggle.IsChecked == true;
            Save();
        }

        /// <summary>
        /// Unlocks the dashboard over the game to be moved and resized, playing the animated lap on it so there's
        /// something to place, until Done (or this window closes, or another choice is made).
        /// </summary>
        private void PlaceOverlay_Click(object sender, RoutedEventArgs e)
        {
            if (_placing != null)
            {
                StopPlacing();
                return;
            }
            if (!IsDashboardRunning())
            {
                PlaceOverlayNote.Text = "Start the dashboard first: it's the running dashboard that shows over the game.";
                return;
            }
            string dashboard = _settings.GetDefaultDashboard().Id;
            void Ask()
            {
                OverlayUnlock.Write();
                PreviewRequest.Write(dashboard, PreviewRequest.AnimatedLap);
            }
            Ask();
            _placing = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _placing.Tick += (s, a) => Ask();
            _placing.Start();
            PlaceOverlayButton.Content = "Done";
            PlaceOverlayNote.Text = "Drag the dashboard to move it, and its corner to resize it. Click Done when it's where you want it.";
        }

        private void StopPlacing()
        {
            if (_placing == null) return;
            _placing.Stop();
            _placing = null;
            OverlayUnlock.Clear();
            PreviewRequest.Clear();
            PlaceOverlayButton.Content = "Move and resize";
            PlaceOverlayNote.Text = "The dashboard shows a frame: drag it to move it, and its corner to resize it.";
        }

        /// <summary>
        /// The displays the dashboard can go on full screen (every display but the main one), with the chosen one
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
            foreach (MonitorChoice display in displays) MonitorPicker.Items.Add(new ComboBoxItem { Content = display.Label, Tag = display.Device });
            if (chosen.Length > 0 && displays.All(d => d.Device != chosen))
                MonitorPicker.Items.Add(new ComboBoxItem { Content = "Display " + new string(chosen.Where(char.IsDigit).ToArray()) + " (not plugged in)", Tag = chosen });
            MonitorPicker.SelectedItem = MonitorPicker.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == chosen);
            MonitorPicker.IsEnabled = MonitorPicker.Items.Count > 0;
            MonitorNote.Text = displays.Count == 0 ? "Plug in a second display to use this." : "";
            _fillingMonitors = false;
        }

        private void DisplaysChanged(object sender, EventArgs e) => Dispatcher.BeginInvoke(new Action(FillMonitors));

        private void MonitorPicker_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || _fillingMonitors) return;
            string device = (MonitorPicker.SelectedItem as ComboBoxItem)?.Tag as string;
            if (string.IsNullOrEmpty(device)) return;
            _settings.General.MonitorDevice = device;
            Save();
        }
    }
}
