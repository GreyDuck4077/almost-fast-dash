using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Brush = System.Windows.Media.Brush;
using DrawingColor = System.Drawing.Color;

namespace AlmostFastOverlays
{
    /// <summary>The Devices page: every LED device, set up and changed through the device wizard.</summary>
    public partial class SettingsWindow
    {
        private bool _deviceBusy;

        /// <summary>One row per device: name and details, then Set up, Test, Remove and on/off.</summary>
        private void BuildDeviceList()
        {
            DevicesHost.Children.Clear();
            var devices = _settings.General.Devices;
            if (devices.Count == 0)
            {
                DevicesHost.Children.Add(new TextBlock
                {
                    Style = (Style)FindResource("T.Secondary"), TextWrapping = TextWrapping.Wrap,
                    Text = "No LED devices yet. Plug one in and click Add device: the app finds it, then helps you name it, count its LEDs and say which are which."
                });
                return;
            }
            foreach (DeviceSettings device in devices) DevicesHost.Children.Add(DeviceRow(device));
        }

        private Border DeviceRow(DeviceSettings device)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            for (int i = 0; i < 4; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
            text.Children.Add(new TextBlock { Text = device.Name, Style = (Style)FindResource("T.Label"), TextTrimming = TextTrimming.CharacterEllipsis });
            string groups = device.Mapped ? device.Layout.Describe() : "LED positions not mapped yet: click Set up";
            text.Children.Add(new TextBlock
            {
                Text = device.Kind + " · " + (device.Connection ?? "not connected") + " · " + groups,
                Style = (Style)FindResource("T.Caption"), Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis
            });
            grid.Children.Add(text);

            var setup = new Button { Content = "Set up", Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Rename it, recount its LEDs or change its groups" };
            setup.Click += (s, e) => OpenDeviceSetup(device);
            Grid.SetColumn(setup, 1);
            grid.Children.Add(setup);

            var test = new Button { Content = "Test", Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Lights each group in turn, in its order" };
            test.Click += (s, e) => TestDevice(device, test);
            Grid.SetColumn(test, 2);
            grid.Children.Add(test);

            var remove = new Button { Style = (Style)FindResource("Btn.Icon"), Content = "", VerticalAlignment = VerticalAlignment.Center, ToolTip = "Remove this device", Margin = new Thickness(0, 0, 12, 0) };
            remove.Click += (s, e) =>
            {
                if (MessageBox.Show(this, "Remove " + device.Name + "? Light layers aimed at its groups stop lighting anything until it's set up again.",
                        "Remove device", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
                _settings.General.Devices.Remove(device);
                Save("Removed " + device.Name);
                BuildDeviceList();
            };
            Grid.SetColumn(remove, 3);
            grid.Children.Add(remove);

            var enabled = new CheckBox { Style = (Style)FindResource("Switch"), IsChecked = device.Enabled, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Use this device" };
            enabled.Click += (s, e) => { device.Enabled = enabled.IsChecked == true; Save(device.Name + (device.Enabled ? " on" : " off")); };
            Grid.SetColumn(enabled, 4);
            grid.Children.Add(enabled);

            return new Border
            {
                Background = (Brush)FindResource("Brush.SurfaceRaised"), CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 0, 8), Child = grid
            };
        }

        private void ShowDevicesStatus(string text)
        {
            DevicesStatus.Text = text;
            DevicesStatus.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        private void AddDevice_Click(object sender, RoutedEventArgs e) => OpenDeviceSetup(null);

        /// <summary>Runs the device wizard for a new device (null) or one that's set up; saves the result.</summary>
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

        /// <summary>Lights each group in turn along its fill order, then the whole device, then off.</summary>
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
            button.IsEnabled = false;
            ShowDevicesStatus("Testing " + device.Name + ": each group lights in turn, in the direction it fills.");
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
                        output.Send(Enumerable.Repeat(DrawingColor.FromArgb(150, 150, 150), n).ToArray());
                        Thread.Sleep(600);
                        output.Send(new DrawingColor[n]);
                    }
                    return null;
                }
                catch (Exception exception) { return exception.Message; }
            });
            ShowDevicesStatus(error == null ? device.Name + " test finished." : device.Name + " didn't respond: " + error);
            button.IsEnabled = true;
            _deviceBusy = false;
        }
    }
}
