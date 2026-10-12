using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Brush = System.Windows.Media.Brush;

namespace AlmostFastOverlays
{
    /// <summary>The Devices page's "Other LED devices": Arduino boards, Adalight strips and WLED controllers.</summary>
    public partial class SettingsWindow
    {
        private bool _ledDeviceBusy;

        private List<LedDeviceSettings> LedDevices => _settings.General.LedDevices;

        /// <summary>One row per device: name and connection, role, LED count, reverse, test, remove and on/off.</summary>
        private void BuildLedDevices()
        {
            LedDevicesHost.Children.Clear();
            if (LedDevices.Count == 0)
            {
                LedDevicesHost.Children.Add(new TextBlock
                {
                    Style = (Style)FindResource("T.Caption"), TextWrapping = TextWrapping.Wrap,
                    Text = "No other LED devices yet. Plug in an Arduino LED board and click Scan USB, or add a WLED controller by its IP address."
                });
                return;
            }
            foreach (LedDeviceSettings device in LedDevices) LedDevicesHost.Children.Add(LedDeviceRow(device));
        }

        private Border LedDeviceRow(LedDeviceSettings device)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            for (int i = 0; i < 6; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
            names.Children.Add(new TextBlock { Text = device.Name, Style = (Style)FindResource("T.Label"), TextTrimming = TextTrimming.CharacterEllipsis });
            names.Children.Add(new TextBlock { Text = device.Kind + " · " + (device.Connection ?? "not set"), Style = (Style)FindResource("T.Caption"), Margin = new Thickness(0, 2, 0, 0) });
            grid.Children.Add(names);

            var role = new ComboBox { ItemsSource = LedRoles.All, SelectedItem = device.Role, Width = 140, MinWidth = 0, VerticalAlignment = VerticalAlignment.Center, ToolTip = LedRoles.Describe(device.Role) };
            role.SelectionChanged += (s, e) =>
            {
                device.Role = (string)role.SelectedItem;
                role.ToolTip = LedRoles.Describe(device.Role);
                Save(device.Name + " shows " + device.Role.ToLowerInvariant());
            };
            Place(grid, role, 1, new Thickness(0, 0, 12, 0));

            var countBox = new TextBox { Text = device.LedCount.ToString(), Width = 52, VerticalAlignment = VerticalAlignment.Center, ToolTip = "How many LEDs this device has" };
            void CommitCount()
            {
                if (int.TryParse(countBox.Text.Trim(), out int count) && count != device.LedCount)
                {
                    device.LedCount = count;
                    device.Normalize();
                    Save(device.Name + ": " + device.LedCount + " LEDs");
                }
                countBox.Text = device.LedCount.ToString();
            }
            countBox.LostKeyboardFocus += (s, e) => CommitCount();
            countBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) { CommitCount(); Keyboard.ClearFocus(); } };
            var count = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            count.Children.Add(countBox);
            count.Children.Add(new TextBlock { Text = "LEDs", Style = (Style)FindResource("T.Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
            Place(grid, count, 2, new Thickness(0, 0, 16, 0));

            var reverse = new CheckBox { Style = (Style)FindResource("Switch"), Content = "Reverse", IsChecked = device.Reverse, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Run the LEDs right to left, for a strip mounted the other way round" };
            reverse.Click += (s, e) => { device.Reverse = reverse.IsChecked == true; Save(); };
            Place(grid, reverse, 3, new Thickness(0, 0, 16, 0));

            var test = new Button { Content = "Test", VerticalAlignment = VerticalAlignment.Center, ToolTip = "Lights each LED in turn, so you can check the count and direction" };
            test.Click += (s, e) => TestLedDevice(device, test);
            Place(grid, test, 4, new Thickness(0, 0, 4, 0));

            var remove = new Button { Style = (Style)FindResource("Btn.Icon"), Content = "", VerticalAlignment = VerticalAlignment.Center, ToolTip = "Remove this device" };
            remove.Click += (s, e) =>
            {
                LedDevices.Remove(device);
                Save("Removed " + device.Name);
                BuildLedDevices();
            };
            Place(grid, remove, 5, new Thickness(0, 0, 12, 0));

            var enabled = new CheckBox { Style = (Style)FindResource("Switch"), IsChecked = device.Enabled, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Use this device" };
            enabled.Click += (s, e) => { device.Enabled = enabled.IsChecked == true; Save(device.Name + (device.Enabled ? " on" : " off")); };
            Place(grid, enabled, 6, new Thickness(0));

            return new Border
            {
                Background = (Brush)FindResource("Brush.SurfaceRaised"), CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 8), Child = grid
            };
        }

        private static void Place(Grid grid, FrameworkElement element, int column, Thickness margin)
        {
            element.Margin = margin;
            Grid.SetColumn(element, column);
            grid.Children.Add(element);
        }

        private void ShowLedDevicesStatus(string text)
        {
            LedDevicesStatus.Text = text;
            LedDevicesStatus.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        // ------------------------------------------------------------------ scanning USB

        private async void ScanLedDevices_Click(object sender, RoutedEventArgs e)
        {
            if (_ledDeviceBusy) return;
            _ledDeviceBusy = true;
            ScanLedDevicesButton.IsEnabled = false;
            // Leave the DV-5, the LB-10 and devices already added alone.
            GeneralSettings general = _settings.General;
            var skip = new List<string>
            {
                DevicePorts.Resolve(general.LedPort, DevicePorts.Dv5),
                DevicePorts.Resolve(general.BrowPort, DevicePorts.LightBar)
            };
            skip.AddRange(LedDevices.Select(d => d.Port));
            bool running = IsDashboardRunning();
            ShowLedDevicesStatus("Looking for LED boards… each USB serial port takes a few seconds." +
                (running ? " Ports the running dashboard is using are skipped." : ""));
            List<LedDeviceSettings> found;
            try
            {
                found = await Task.Run(() => LedDeviceScanner.Scan(skip, port => Dispatcher.BeginInvoke(new Action(() => ShowLedDevicesStatus("Looking for LED boards… " + port)))));
            }
            catch (Exception exception)
            {
                found = new List<LedDeviceSettings>();
                ShowLedDevicesStatus("The scan stopped: " + exception.Message);
            }
            foreach (LedDeviceSettings device in found)
            {
                device.LedCount = device.Kind == LedDeviceKinds.Adalight ? 30 : 8;
                device.Normalize();
                LedDevices.Add(device);
            }
            if (found.Count > 0)
            {
                Save("Added " + found.Count + (found.Count == 1 ? " LED device" : " LED devices"));
                ShowLedDevicesStatus("Found " + string.Join(", ", found.Select(d => d.Name + (d.Kind == LedDeviceKinds.Adalight ? "" : " on " + d.Port))) +
                    ". Set each one's LED count (Test lights them one at a time to count) and what it shows.");
            }
            else if (LedDevicesStatus.Text.StartsWith("Looking"))
                ShowLedDevicesStatus("No LED boards answered. Boards need Adalight or the common sim-racing LED firmware; WLED controllers are added by IP address.");
            BuildLedDevices();
            ScanLedDevicesButton.IsEnabled = true;
            _ledDeviceBusy = false;
        }

        // ------------------------------------------------------------------ adding WLED

        private void AddWled_Click(object sender, RoutedEventArgs e)
        {
            WledEntry.Visibility = Visibility.Visible;
            WledAddress.Focus();
        }

        private void AddWledCancel_Click(object sender, RoutedEventArgs e)
        {
            WledEntry.Visibility = Visibility.Collapsed;
            WledAddress.Text = "";
        }

        private void WledAddress_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) AddWledConfirm_Click(sender, e);
            else if (e.Key == Key.Escape) AddWledCancel_Click(sender, e);
        }

        private async void AddWledConfirm_Click(object sender, RoutedEventArgs e)
        {
            string address = WledAddress.Text.Trim();
            if (address.Length == 0 || _ledDeviceBusy) return;
            _ledDeviceBusy = true;
            ShowLedDevicesStatus("Asking " + address + "…");
            Tuple<string, int> info = await Task.Run(() => WledOutput.Info(address));
            _ledDeviceBusy = false;
            if (info == null)
            {
                ShowLedDevicesStatus("No WLED controller answered at " + address + ". Check the address in the WLED app, and that this PC is on the same network.");
                return;
            }
            var device = new LedDeviceSettings
            {
                Kind = LedDeviceKinds.Wled, Address = address, Name = info.Item1,
                LedCount = info.Item2 > 0 ? info.Item2 : 30, Role = LedRoles.Bar
            };
            device.Normalize();
            LedDevices.Add(device);
            Save("Added " + device.Name);
            ShowLedDevicesStatus("Added " + device.Name + " with " + device.LedCount + " LEDs.");
            WledEntry.Visibility = Visibility.Collapsed;
            WledAddress.Text = "";
            BuildLedDevices();
        }

        // ------------------------------------------------------------------ testing

        /// <summary>Lights each LED in turn in white, then the whole strip in the accent colour, then off.</summary>
        private async void TestLedDevice(LedDeviceSettings device, Button button)
        {
            if (_ledDeviceBusy) return;
            if (device.Kind != LedDeviceKinds.Wled && IsDashboardRunning())
            {
                ShowLedDevicesStatus("The running dashboard is driving " + device.Name + ". Stop the dashboard to test it on its own.");
                return;
            }
            _ledDeviceBusy = true;
            button.IsEnabled = false;
            ShowLedDevicesStatus("Testing " + device.Name + ": watch for one LED moving along, then the whole strip.");
            bool batched = _settings.General.FastLedUpdates;
            string error = await Task.Run(() =>
            {
                try
                {
                    using (ILedOutput output = LedDeviceLink.Open(device, batched))
                    {
                        int n = device.LedCount, delay = Math.Max(15, Math.Min(120, 2400 / n));
                        for (int i = 0; i < n; i++)
                        {
                            var frame = new Color[n];
                            frame[device.Reverse ? n - 1 - i : i] = Color.White;
                            output.Send(frame);
                            Thread.Sleep(delay);
                        }
                        output.Send(Enumerable.Repeat(Color.FromArgb(0, 200, 180), n).ToArray());
                        Thread.Sleep(700);
                        output.Send(new Color[n]);
                    }
                    return null;
                }
                catch (Exception exception) { return exception.Message; }
            });
            ShowLedDevicesStatus(error == null ? device.Name + " test finished." : device.Name + " didn't respond: " + error);
            button.IsEnabled = true;
            _ledDeviceBusy = false;
        }
    }
}
