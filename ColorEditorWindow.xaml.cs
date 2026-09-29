using System.Windows;
using System.Windows.Media;

namespace DV5Direct
{
    public partial class ColorEditorWindow : Window
    {
        private readonly bool _single;
        public string LowColor { get; private set; }
        public string MiddleColor { get; private set; }
        public string HighColor { get; private set; }

        public ColorEditorWindow(string title, string low, string middle = null, string high = null)
        {
            InitializeComponent();
            Title = title;
            Heading.Text = title;
            LowColor = low;
            MiddleColor = middle ?? low;
            HighColor = high ?? low;
            _single = middle == null && high == null;
            if (_single) { MiddleRow.Visibility = Visibility.Collapsed; HighRow.Visibility = Visibility.Collapsed; Height = 225; }
            RefreshColors();
        }

        private void Low_Click(object sender, RoutedEventArgs e) { if (WindowsColorPicker.TryChoose(LowColor, out string value)) { LowColor = value; RefreshColors(); } }
        private void Middle_Click(object sender, RoutedEventArgs e) { if (WindowsColorPicker.TryChoose(MiddleColor, out string value)) { MiddleColor = value; RefreshColors(); } }
        private void High_Click(object sender, RoutedEventArgs e) { if (WindowsColorPicker.TryChoose(HighColor, out string value)) { HighColor = value; RefreshColors(); } }

        private void RefreshColors()
        {
            LowValue.Text = LowColor; MiddleValue.Text = MiddleColor; HighValue.Text = HighColor;
            LowSwatch.Background = BrushFor(LowColor); MiddleSwatch.Background = BrushFor(MiddleColor); HighSwatch.Background = BrushFor(HighColor);
        }

        private void Save_Click(object sender, RoutedEventArgs e) => DialogResult = true;
        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
        private static Brush BrushFor(string value) { try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)); } catch { return new SolidColorBrush(Color.FromRgb(40, 50, 65)); } }
    }
}
