using System.Windows;
using System.Windows.Controls;

namespace AlmostFastOverlays
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
                    ? "Keeps the LEDs to about " + device.MaxMilliamps + " mA, dimming bright frames (a whole bar lit white, say) so a USB port isn't overloaded. An overloaded port can drop the device and leave its LEDs stuck on."
                    : "No limit: every LED can light at full brightness. Only choose this when the device really has power of its own: if it keeps disconnecting, switch back.";
            }
            power.SelectionChanged += (s, e) => { device.MaxMilliamps = power.SelectedIndex == 0 ? LedPower.DefaultMilliamps : 0; Describe(); };
            Describe();
            panel.Children.Add(power);
            panel.Children.Add(help);
            return panel;
        }
    }
}
