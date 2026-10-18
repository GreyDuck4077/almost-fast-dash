using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace AlmostFastDash.UI
{
    internal sealed partial class DeviceSetupWindow : Window
    {
        // ------------------------------------------------------------------ 1. find

        private readonly List<DeviceSettings> _found = new List<DeviceSettings>();

        private StackPanel _foundList;

        private void ShowFind()
        {
            _title.Text = "Find your device";
            _subtitle.Text = "Plug it in, then look for it. WLED controllers are found by their address.";
            var body = new StackPanel();
            _foundList = new StackPanel();
            body.Children.Add(_foundList);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
            var scan = _scanButton = new Button { Content = "Look again" };
            scan.Click += async (s, e) => await Scan();
            actions.Children.Add(scan);
            body.Children.Add(actions);

            body.Children.Add(new TextBlock { Text = "WLED ON YOUR NETWORK", Style = (Style)FindResource("T.Overline"),
                Margin = new Thickness(0, 26, 0, 8) });
            var wled = new StackPanel { Orientation = Orientation.Horizontal };
            var address = new TextBox { Width = 240, ToolTip = "The controller's IP address or name" };
            // What to type, until something's typed.
            var addressHint = new TextBlock
            {
                Text = "192.168.1.40 or wled.local", IsHitTestVisible = false, Margin = new Thickness(12, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("Brush.TextTertiary")
            };
            address.TextChanged += (s, e) => addressHint.Visibility = address.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            var lookUp = new Button { Content = "Look up", Margin = new Thickness(8, 0, 0, 0) };
            async Task LookUp()
            {
                string text = address.Text.Trim();
                if (text.Length == 0 || !lookUp.IsEnabled) return;
                DeviceSettings known = _found.FirstOrDefault(d => d.Kind == LedDeviceKinds.Wled &&
                    string.Equals(d.Address, text, StringComparison.OrdinalIgnoreCase));
                if (known != null) { Select(known); return; }
                _status.Text = "Asking " + text + "…";
                lookUp.IsEnabled = false;
                Tuple<string, int> info = await Task.Run(() => WledOutput.Info(text));
                lookUp.IsEnabled = true;
                if (info == null) { _status.Text = "No WLED controller answered at " + text +
                    ". Check the address in the WLED app, and that this PC is on the same network."; return; }
                var found = new DeviceSettings { Kind = LedDeviceKinds.Wled, Address = text, DetectedName = info.Item1, Name = info.Item1,
                    LedCount = info.Item2 > 0 ? info.Item2 : 30 };
                _found.Add(found);
                _status.Text = "";
                RenderFound();
                Select(found);
            }
            lookUp.Click += async (s, e) => await LookUp();
            address.KeyDown += async (s, e) => { if (e.Key == Key.Enter) await LookUp(); };
            var addressHost = new Grid();
            addressHost.Children.Add(address);
            addressHost.Children.Add(addressHint);
            wled.Children.Add(addressHost);
            wled.Children.Add(lookUp);
            body.Children.Add(wled);
            _body.Content = body;
            _next.IsEnabled = _device != null;
            if (Offline && _found.Count == 0)
            {
                _found.Add(new DeviceSettings { Kind = LedDeviceKinds.Arduino, Port = "COM4", DetectedName = "SSG DV 5",
                    Name = "SSG DV 5" });
                _found.Add(new DeviceSettings { Kind = LedDeviceKinds.Arduino, Port = "COM9", DetectedName = "Arduino LED board",
                    Name = "Arduino LED board" });
            }
            RenderFound();
            if (_found.Count == 0) Dispatcher.BeginInvoke(new Func<Task>(Scan), DispatcherPriority.Background);
        }

        private bool _scanning;

        private Button _scanButton;

        private async Task Scan()
        {
            if (_scanning || Offline) return;
            if (_dashboardRunning())
            {
                ShowNeedsStop(async () => await Scan());
                return;
            }
            _scanning = true;
            if (_scanButton != null) _scanButton.IsEnabled = false;
            _status.Text = "Looking for LED devices…";
            var skip = _settings.General.Devices.Select(d => d.ResolvePort()).Where(p => p != null).ToList();
            List<DeviceSettings> found;
            List<string> busy = new List<string>();
            try
            {
                found = await Task.Run(() =>
                {
                    List<DeviceSettings> result = LedDeviceScanner.Scan(skip,
                        port => Dispatcher.BeginInvoke(new Action(() => _status.Text = "Looking for LED devices… " + port)),
                        out List<string> inUse);
                    busy = inUse;
                    return result;
                });
            }
            catch (Exception exception) { found = new List<DeviceSettings>(); _status.Text = "The search stopped: " + exception.Message; }
            _scanning = false;
            if (_scanButton != null) _scanButton.IsEnabled = true;
            foreach (DeviceSettings device in found.Where(f => _found.All(x => x.Port != f.Port))) _found.Add(device);
            if (_step != Step.Find) return;
            _status.Text = busy.Count > 0
                ? string.Join(", ", busy) + (busy.Count == 1 ? " is" : " are") + " in use by another program, such as SimHub, so " +
                    (busy.Count == 1 ? "it" : "they") + " can't be asked. Close it, then look again."
                : _found.Count == 0
                    ? "Nothing answered. Check the cable, then look again. Boards need the common sim-racing LED firmware or Adalight."
                    : "";
            RenderFound();
            if (_device == null && _found.Count == 1) Select(_found[0]);
        }

        /// <summary>Setup needs the devices to itself, so a running dashboard has to pause first.</summary>
        private void ShowNeedsStop(Func<Task> then)
        {
            _foundList.Children.Clear();
            var card = Card();
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = "The dashboard is using your devices", Style = (Style)FindResource("T.Label") });
            stack.Children.Add(new TextBlock
            {
                Text = "The dashboard pauses during setup and starts again when you're done.",
                Style = (Style)FindResource("T.Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 12)
            });
            var stop = new Button { Content = "Pause the dashboard", Style = (Style)FindResource("Btn.Primary"),
                HorizontalAlignment = HorizontalAlignment.Left };
            stop.Click += async (s, e) =>
            {
                stop.IsEnabled = false;
                _status.Text = "Pausing the dashboard…";
                if (await _stopDashboard())
                {
                    StoppedDashboard = true;
                    await then();
                }
                else { _status.Text = "The dashboard didn't stop. Stop it from the main window, then look again."; stop.IsEnabled = true; }
            };
            stack.Children.Add(stop);
            card.Child = stack;
            _foundList.Children.Add(card);
        }

        private void RenderFound()
        {
            if (_foundList == null) return;
            _foundList.Children.Clear();
            foreach (DeviceSettings found in _found)
            {
                DeviceSettings f = found;
                var card = Card();
                card.Cursor = Cursors.Hand;
                card.BorderThickness = new Thickness(1.5);
                card.BorderBrush = (Brush)FindResource(_device != null && f.Connection == _device.Connection && f.Kind == _device.Kind
                    ? "Brush.Accent" : "Brush.Border");
                var text = new StackPanel();
                text.Children.Add(new TextBlock { Text = f.DetectedName, Style = (Style)FindResource("T.Label") });
                text.Children.Add(new TextBlock { Text = f.Kind + " · " + f.Connection, Style = (Style)FindResource("T.Caption"),
                    Margin = new Thickness(0, 3, 0, 0) });
                card.Child = text;
                card.MouseLeftButtonUp += (s, e) => Select(f);
                _foundList.Children.Add(card);
            }
        }

        private void Select(DeviceSettings found)
        {
            _device = SettingsStore.Clone(found);
            // Nothing is assumed from a recognized board: its shape is described from scratch, and fast updates are detected when it
            // connects.
            _layout = new LedLayout { Shape = "" };
            _mapper = null;
            _next.IsEnabled = true;
            RenderFound();
        }
    }
}
