using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;
using Brush = System.Windows.Media.Brush;
using DrawingColor = System.Drawing.Color;

namespace AlmostFastOverlays
{
    /// <summary>
    /// The Devices page: each LED device with its live connection status, a test, its settings and
    /// an on/off switch. Adding a device, or setting up its LEDs again, goes through the device wizard.
    /// </summary>
    public partial class SettingsWindow
    {
        private bool _deviceBusy;
        private readonly Dictionary<string, Tuple<Ellipse, TextBlock>> _deviceStatus = new Dictionary<string, Tuple<Ellipse, TextBlock>>();

        private void BuildDeviceList()
        {
            DevicesHost.Children.Clear();
            _deviceStatus.Clear();
            var devices = _settings.General.Devices;
            if (devices.Count == 0)
            {
                DevicesHost.Children.Add(new TextBlock
                {
                    Style = (Style)FindResource("T.Secondary"), TextWrapping = TextWrapping.Wrap,
                    Text = "No LED devices yet. Plug one in and click Add device. The app finds it, you describe its shape (LEDs around a screen, light bars, a strip), then show it where each LED is with a couple of clicks."
                });
                return;
            }
            foreach (DeviceSettings device in devices) DevicesHost.Children.Add(DeviceRow(device));
            RefreshDeviceStatus();
        }

        private Border DeviceRow(DeviceSettings device)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            for (int i = 0; i < 3; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var dot = new Ellipse { Width = 9, Height = 9, Margin = new Thickness(0, 6, 12, 0), VerticalAlignment = VerticalAlignment.Top, Fill = (Brush)FindResource("Brush.TextTertiary") };
            grid.Children.Add(dot);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
            text.Children.Add(new TextBlock { Text = device.Name, Style = (Style)FindResource("T.Label"), TextTrimming = TextTrimming.CharacterEllipsis });
            var status = new TextBlock { Style = (Style)FindResource("T.Caption"), Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            text.Children.Add(status);
            text.Children.Add(new TextBlock
            {
                Text = device.Mapped ? device.Layout.Describe() : "LED positions not set up yet: open Settings and set up its LEDs",
                Style = (Style)FindResource("T.Caption"), Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = (Brush)FindResource(device.Mapped ? "Brush.TextTertiary" : "Brush.Warning")
            });
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            _deviceStatus[device.Id] = Tuple.Create(dot, status);

            var test = new Button { Content = "Test", Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Lights each part in turn, the way it fills" };
            test.Click += (s, e) => TestDevice(device, test);
            Grid.SetColumn(test, 2);
            grid.Children.Add(test);

            var settings = new Button { Content = "Settings", Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Name, power, LED layout, or remove it" };
            settings.Click += (s, e) => OpenDeviceSettings(device);
            Grid.SetColumn(settings, 3);
            grid.Children.Add(settings);

            var enabled = new CheckBox { Style = (Style)FindResource("Switch"), IsChecked = device.Enabled, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Use this device" };
            enabled.Click += (s, e) => { device.Enabled = enabled.IsChecked == true; Save(device.Name + (device.Enabled ? " on" : " off")); RefreshDeviceStatus(); };
            Grid.SetColumn(enabled, 4);
            grid.Children.Add(enabled);

            var row = new Border
            {
                Background = (Brush)FindResource("Brush.SurfaceRaised"), CornerRadius = new CornerRadius(10), Cursor = Cursors.Hand,
                Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 0, 8), Child = grid
            };
            row.MouseLeftButtonUp += (s, e) => { if (!(e.OriginalSource is DependencyObject o && (FindParent<Button>(o) != null || FindParent<CheckBox>(o) != null))) OpenDeviceSettings(device); };
            return row;
        }

        /// <summary>
        /// Each device's status: live from whatever is driving the devices (the dashboard or this window's
        /// preview), or otherwise whether it's plugged in at all.
        /// </summary>
        private void RefreshDeviceStatus()
        {
            string performance = _dashboardRunning ? ScreenStats.Read() : null;
            ScreenPerformance.Text = performance != null ? char.ToUpper(performance[0]) + performance.Substring(1) + "."
                : _dashboardRunning ? "Measuring… figures appear a few seconds after the screen starts drawing." : "Start the dashboard to measure how it keeps up.";
            if (_deviceStatus.Count == 0) return;
            // Live states only while something is driving the devices; otherwise just whether each is plugged in.
            Dictionary<string, DeviceStatus.Entry> live = _dashboardRunning || _mirror.Active ? DeviceStatus.Read() : null;
            HashSet<string> ports = null;
            foreach (DeviceSettings device in _settings.General.Devices)
            {
                if (!_deviceStatus.TryGetValue(device.Id, out Tuple<Ellipse, TextBlock> row)) continue;
                string text, tone;
                if (!device.Enabled) { text = "Off"; tone = "Brush.TextTertiary"; }
                else if (live != null && live.TryGetValue(device.Id, out DeviceStatus.Entry entry))
                {
                    switch (entry.State)
                    {
                        case LinkStates.Connected: text = "Connected · " + device.Connection + (string.IsNullOrEmpty(entry.Detail) ? "" : " · " + entry.Detail); tone = "Brush.Success"; break;
                        case LinkStates.Missing: text = "Not found on " + device.Connection + ". Check it's plugged in and powered."; tone = "Brush.Danger"; break;
                        case LinkStates.Busy: text = "In use by another program, such as SimHub. Close it to use " + device.Connection + " here."; tone = "Brush.Warning"; break;
                        case LinkStates.Dropping: text = "Keeps disconnecting. Check its cable and power, or set Power to the PC's USB port."; tone = "Brush.Danger"; break;
                        default: text = "Connecting…"; tone = "Brush.Warning"; break;
                    }
                }
                else if (device.Kind == LedDeviceKinds.Wled) { text = "WLED at " + device.Address + " · not in use right now"; tone = "Brush.TextTertiary"; }
                else
                {
                    ports = ports ?? new HashSet<string>(SerialPort.GetPortNames(), StringComparer.OrdinalIgnoreCase);
                    string port = device.ResolvePort();
                    bool present = port != null && ports.Contains(port);
                    text = present ? "Plugged in · " + port + " · not in use right now" : "Not plugged in";
                    tone = present ? "Brush.TextSecondary" : "Brush.Danger";
                }
                row.Item2.Text = text;
                row.Item2.ToolTip = text;
                row.Item1.Fill = (Brush)FindResource(tone == "Brush.TextSecondary" ? "Brush.TextTertiary" : tone);
            }
        }

        private void ShowDevicesStatus(string text)
        {
            DevicesStatus.Text = text;
            DevicesStatus.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        private void AddDevice_Click(object sender, RoutedEventArgs e) => OpenDeviceSetup(null);

        /// <summary>A device's settings window; from it, setting up the LEDs again, a test or removing it.</summary>
        private void OpenDeviceSettings(DeviceSettings device)
        {
            string status = _deviceStatus.TryGetValue(device.Id, out Tuple<Ellipse, TextBlock> row) ? row.Item2.Text : null;
            var window = new DeviceSettingsWindow(device, status) { Owner = this };
            if (window.ShowDialog() != true) return;
            int index = _settings.General.Devices.IndexOf(device);
            if (index < 0) return;
            if (window.Choice == DeviceSettingsWindow.Outcome.Removed)
            {
                _settings.General.Devices.RemoveAt(index);
                Save("Removed " + device.Name);
                BuildDeviceList();
                _stripsSignature = null;
                return;
            }
            DeviceSettings edited = window.Result;
            _settings.General.Devices[index] = edited;
            Save(edited.Name + " saved");
            FlushSave();
            BuildDeviceList();
            _stripsSignature = null;
            if (window.Choice == DeviceSettingsWindow.Outcome.SetUpLeds) OpenDeviceSetup(edited);
            else if (window.Choice == DeviceSettingsWindow.Outcome.Test) TestDevice(edited, null);
        }

        /// <summary>Runs the device wizard for a new device (null), or to set up an existing one's LEDs again; saves the result.</summary>
        private async void OpenDeviceSetup(DeviceSettings existing)
        {
            FlushSave();
            ReleaseDevices();
            var wizard = new DeviceSetupWindow(_settings, existing, IsDashboardRunning, StopDashboardAsync) { Owner = this };
            bool done = wizard.ShowDialog() == true;
            if (done && wizard.Result != null)
            {
                if (existing == null) _settings.General.Devices.Add(wizard.Result);
                else
                {
                    int index = _settings.General.Devices.IndexOf(existing);
                    if (index >= 0) _settings.General.Devices[index] = wizard.Result;
                }
                Save(wizard.Result.Name + (existing == null ? " added" : " saved"));
                FlushSave();
                ShowDevicesStatus("");
            }
            BuildDeviceList();
            _stripsSignature = null;
            if (wizard.StoppedDashboard)
            {
                ShowDevicesStatus("Starting the dashboard again…");
                await StartDashboardAsync();
                ShowDevicesStatus("");
            }
        }

        /// <summary>Lights each part in turn along its fill order, then the whole device, then off.</summary>
        private async void TestDevice(DeviceSettings device, Button button)
        {
            if (_deviceBusy) return;
            if (device.Kind != LedDeviceKinds.Wled && IsDashboardRunning())
            {
                ShowDevicesStatus("The running dashboard is driving " + device.Name + ". Stop it to test the device on its own.");
                return;
            }
            _deviceBusy = true;
            ReleaseDevices();
            if (button != null) button.IsEnabled = false;
            ShowDevicesStatus("Testing " + device.Name + ": each part lights in turn, the way it fills.");
            DeviceSettings copy = SettingsStore.Clone(device);
            string error = await Task.Run(() =>
            {
                try
                {
                    using (ILedOutput output = LedDeviceLink.Open(copy))
                    {
                        int n = copy.LedCount;
                        foreach (LedGroup group in copy.Groups.DefaultIfEmpty(LedGroup.Of("All LEDs", LedPurposes.Bar, 1, n)))
                        {
                            int[] leds = group.Indexes();
                            int delay = Math.Max(30, Math.Min(140, 1600 / Math.Max(1, leds.Length)));
                            var frame = new DrawingColor[n];
                            foreach (int led in leds)
                            {
                                if (led < n) frame[led] = DrawingColor.FromArgb(0, 200, 180);
                                output.Send((DrawingColor[])frame.Clone());
                                Thread.Sleep(delay);
                            }
                            Thread.Sleep(250);
                        }
                        // Everything at once, in a soft colour that stays within a USB port's power.
                        output.Send(Enumerable.Repeat(DrawingColor.FromArgb(0, 70, 60), n).ToArray());
                        Thread.Sleep(600);
                        output.Send(new DrawingColor[n]);
                    }
                    return null;
                }
                catch (UnauthorizedAccessException) { return "its port is in use by another program, such as SimHub. Close it and try again."; }
                catch (Exception exception) { return exception.Message; }
            });
            ShowDevicesStatus(error == null ? device.Name + " test finished." : device.Name + " didn't respond: " + error);
            if (button != null) button.IsEnabled = true;
            _deviceBusy = false;
        }
    }
}
