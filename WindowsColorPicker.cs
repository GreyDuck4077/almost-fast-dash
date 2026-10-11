using System.Drawing;

namespace AlmostFastOverlays
{
    internal static class WindowsColorPicker
    {
        public static bool TryChoose(string currentValue, out string selectedValue)
        {
            Color initial = SettingsStore.ParseColor(currentValue, Color.FromArgb(0, 155, 210));
            using (var dialog = new System.Windows.Forms.ColorDialog
            {
                Color = initial,
                FullOpen = true,
                AnyColor = true,
                SolidColorOnly = false
            })
            {
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    selectedValue = SettingsStore.ToHex(dialog.Color);
                    return true;
                }
            }
            selectedValue = currentValue;
            return false;
        }
    }
}
