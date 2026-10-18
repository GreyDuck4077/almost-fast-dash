using System.Windows;
using System.Windows.Controls;

namespace AlmostFastDash.UI
{
    /// <summary>Pieces of device settings shared by the device wizard and a device's settings window.</summary>
    internal static class DeviceUi
    {
        /// <summary>How the device is powered: a USB port from the PC (with the power limit) or power of its own (no limit).</summary>
        public static FrameworkElement PowerPicker(FrameworkElement owner, DeviceSettings device)
        {
            var panel = new StackPanel();
            var power = new ComboBox
            {
                ItemsSource = new[] { "From the PC's USB port", "A powered hub or its own power supply" }, Width = 320, HorizontalAlignment = HorizontalAlignment.Left,
                SelectedIndex = device.MaxMilliamps > 0 ? 0 : 1
            };
            var help = new TextBlock { Style = (Style)owner.FindResource("T.Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left };
            void Describe()
            {
                help.Text = device.MaxMilliamps > 0
                    ? "Keeps the LEDs to about " + device.MaxMilliamps + " mA by dimming bright frames, so the USB port isn't overloaded and the device stays connected."
                    : "Full brightness. Only for devices with their own power; if it keeps disconnecting, switch back.";
            }
            power.SelectionChanged += (s, e) => { device.MaxMilliamps = power.SelectedIndex == 0 ? LedPower.DefaultMilliamps : 0; Describe(); };
            Describe();
            panel.Children.Add(power);
            panel.Children.Add(help);
            return panel;
        }
    }
}
