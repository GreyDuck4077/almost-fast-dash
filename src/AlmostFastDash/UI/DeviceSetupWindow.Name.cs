using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace AlmostFastDash.UI
{
    internal sealed partial class DeviceSetupWindow : Window
    {
        // ------------------------------------------------------------------ 2. name

        private TextBox _nameBox;

        private void ShowName()
        {
            _title.Text = _existing == null ? "Name it" : "Name";
            _subtitle.Text = _existing == null
                ? "Found " + _device.DetectedName + " on " + _device.Connection + ". Give it a name you'll recognize."
                : _device.DetectedName + " on " + _device.Connection + ".";
            var body = new StackPanel();
            body.Children.Add(new TextBlock { Text = "Name", Style = (Style)FindResource("T.Label"), Margin = new Thickness(0, 0, 0, 8) });
            _nameBox = new TextBox { Text = _device.Name, Width = 360, HorizontalAlignment = HorizontalAlignment.Left, MaxLength = 40 };
            _nameBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) GoNext(); };
            body.Children.Add(_nameBox);
            _body.Content = body;
            Dispatcher.BeginInvoke(new Action(() => { _nameBox.Focus(); _nameBox.SelectAll(); }), DispatcherPriority.Input);
        }

        /// <summary>The name, or with a number added when another device already has it, so every device can be told apart.</summary>
        private string UniqueName(string name)
        {
            string candidate = name;
            for (int n = 2; _settings.General.Devices.Any(d => d.Id != _device.Id && string.Equals(d.Name, candidate, StringComparison.OrdinalIgnoreCase)); n++)
                candidate = name + " " + n;
            return candidate;
        }
    }
}
