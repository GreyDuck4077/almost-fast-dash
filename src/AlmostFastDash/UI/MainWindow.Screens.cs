using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Brush = System.Windows.Media.Brush;

namespace AlmostFastDash.UI
{
    /// <summary>
    /// The screens the dashboard shows on, on the Devices page: the wheel screen's status and "Send only what
    /// changed", and the dashboard on the PC: in a window, over the game, or full screen on another display.
    /// </summary>
    public partial class MainWindow
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
                text = "Not found. Check it's plugged in. VoCore-family M-PRO screens are supported.";
                tone = "Brush.Warning";
            }
            else if (_settings.General.EffectivePcOutput() != "Off")
            {
                text = "Plugged in · " + ScreenSize + " · idle while the dashboard shows on your PC";
                tone = "Brush.TextTertiary";
            }
            else if (_dashboardRunning)
            {
                // The running dashboard keeps screen.link while it's connected to the screen.
                bool linked = File.Exists(ScreenSender.LinkPath);
                text = linked ? "Connected · " + ScreenSize + " · showing the dashboard"
                    : "Connecting… If this stays, close other dashboard software.";
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

        /// <summary>Shows where the dashboard shows (the header's Show on), and the settings for the PC.</summary>
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
            foreach (RadioButton choice in ShowOnChoices) choice.IsChecked = (string)choice.Tag == mode;
            OverlayOpacity.Value = Math.Max(30, Math.Min(100, general.OverlayOpacity));
            OverlayOpacityValue.Text = (int)OverlayOpacity.Value + "%";
            OverlayRacingToggle.IsChecked = general.OverlayOnlyWhileRacing;
            FillMonitors();
            ShowPcRows(mode);
            RefreshShowOn();
            WhenInCar.IsChecked = general.ShowInCar;
            WhenOutOfCar.IsChecked = general.ShowOutOfCar;
            WhenGarage.IsChecked = general.ShowInGarage;
            WhenSpectating.IsChecked = general.ShowSpectating;
        }

        /// <summary>Show when, at the top: the points in iRacing where the dashboard shows. It's hidden at the others.</summary>
        private void ShowWhen_Click(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            GeneralSettings general = _settings.General;
            general.ShowInCar = WhenInCar.IsChecked == true;
            general.ShowOutOfCar = WhenOutOfCar.IsChecked == true;
            general.ShowInGarage = WhenGarage.IsChecked == true;
            general.ShowSpectating = WhenSpectating.IsChecked == true;
            bool any = general.ShowInCar || general.ShowOutOfCar || general.ShowInGarage || general.ShowSpectating;
            Save(any ? null : "Saved · the dashboard is hidden all the time in iRacing now");
        }

        /// <summary>A point in iRacing, as the header says it: "out of the car".</summary>
        private static string AtPoint(CarState state)
        {
            switch (state)
            {
                case CarState.OutOfCar: return "out of the car";
                case CarState.Garage: return "in the garage";
                case CarState.Spectating: return "spectating";
                default: return "in the car";
            }
        }

        private RadioButton[] ShowOnChoices => new[] { ShowWheel, ShowWindow, ShowOverlay, ShowFullScreen };

        private void ShowPcRows(string mode)
        {
            PcWindowRows.Visibility = mode == MonitorOutput.Windowed ? Visibility.Visible : Visibility.Collapsed;
            PcOverlayRows.Visibility = mode == MonitorOutput.Overlay ? Visibility.Visible : Visibility.Collapsed;
            PcFullScreenRows.Visibility = mode == MonitorOutput.FullScreen ? Visibility.Visible : Visibility.Collapsed;
            MoveOverlayButton.Visibility = mode == MonitorOutput.Overlay ? Visibility.Visible : Visibility.Collapsed;
            if (mode != MonitorOutput.Overlay) StopPlacing();
        }

        /// <summary>
        /// The header's Show on: the dashboard shows in one place, the wheel screen or the PC. Choosing the PC lets
        /// the wheel screen go (blank); choosing the wheel screen closes the dashboard on the PC.
        /// </summary>
        private void ShowOn_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading || !(sender is RadioButton choice) || choice.IsChecked != true) return;
            string mode = (string)choice.Tag;
            if (mode == _settings.General.EffectivePcOutput()) return;
            _settings.General.PcOutput = mode;
            // Full screen goes on the first other display unless one was chosen before.
            if (mode == MonitorOutput.FullScreen && MonitorChoice.All().All(d => d.Device != _settings.General.MonitorDevice))
            {
                _settings.General.MonitorDevice = MonitorChoice.All().FirstOrDefault()?.Device;
                FillMonitors();
            }
            ShowPcRows(mode);
            Save();
        }

        /// <summary>
        /// Show on's choices that need something plugged in are greyed out without it: the wheel screen, and full
        /// screen (another display). Refreshed with the status, twice a second.
        /// </summary>
        private void RefreshShowOn()
        {
            bool wheel = ScreenPresent(), display = MonitorChoice.All().Count > 0;
            if (ShowWheel.IsEnabled != wheel) ShowWheel.IsEnabled = wheel;
            if (ShowFullScreen.IsEnabled != display) ShowFullScreen.IsEnabled = display;
            ShowWheel.ToolTip = wheel ? "The screen on your wheel" : "No wheel screen plugged in";
            ShowFullScreen.ToolTip = display ? "Full screen on another display" : "Plug in a second display to use this";
        }

        private void OverlayRacing_Click(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _settings.General.OverlayOnlyWhileRacing = OverlayRacingToggle.IsChecked == true;
            Save();
        }

        /// <summary>
        /// Unlocks the dashboard over the game to be moved and resized, playing the animated lap on it so there's
        /// something to place, until Done (or this window closes, or another choice is made). The header's move
        /// button and the Devices page's both do this.
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
                const string start = "Start the dashboard first.";
                PlaceOverlayNote.Text = start;
                ShowSaved(start);
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
            PlaceOverlayNote.Text = "Drag it to move it, or its corner to resize it, then click Done.";
            MoveOverlayIcon.Text = "\uE73E";
            MoveOverlayIcon.Foreground = (Brush)FindResource("Brush.Accent");
            MoveOverlayButton.ToolTip = "Done: lock it where it is";
        }

        private void StopPlacing()
        {
            if (_placing == null) return;
            _placing.Stop();
            _placing = null;
            OverlayUnlock.Clear();
            PreviewRequest.Clear();
            PlaceOverlayButton.Content = "Move and resize";
            PlaceOverlayNote.Text = "Drag it to move it, or its corner to resize it.";
            MoveOverlayIcon.Text = "\uE7C2";
            MoveOverlayIcon.ClearValue(TextBlock.ForegroundProperty);
            MoveOverlayButton.ToolTip = "Move and resize it over the game";
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
            foreach (MonitorChoice display in displays) MonitorPicker.Items.Add(new ComboBoxItem { Content = display.Label,
                Tag = display.Device });
            if (chosen.Length > 0 && displays.All(d => d.Device != chosen))
                MonitorPicker.Items.Add(new ComboBoxItem { Content = "Display " + new string(chosen.Where(char.IsDigit).ToArray()) +
                    " (not plugged in)",
                    Tag = chosen });
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
