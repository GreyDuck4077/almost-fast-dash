using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AlmostFastOverlays
{
    /// <summary>
    /// One device's settings: its name, whether it's used, how it's powered, its LED layout (with a way to
    /// set the LEDs up again), a test and removal. Changes are made to a copy and kept on Done; the LED
    /// wizard is only needed to describe or map the LEDs again.
    /// </summary>
    internal sealed class DeviceSettingsWindow : Window
    {
        public enum Outcome { Cancelled, Saved, SetUpLeds, Test, Removed }

        private readonly DeviceSettings _device;

        /// <summary>The edited device (on Saved, SetUpLeds or Test).</summary>
        public DeviceSettings Result => _device;
        public Outcome Choice { get; private set; } = Outcome.Cancelled;

        public DeviceSettingsWindow(DeviceSettings device, string status)
        {
            _device = SettingsStore.Clone(device);
            Title = device.Name;
            Width = 640;
            SizeToContent = SizeToContent.Height;
            MaxHeight = 820;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            FontFamily = (FontFamily)FindResource("Font.Ui");
            FontSize = 13;
            Foreground = (Brush)FindResource("Brush.Text");
            Background = (Brush)FindResource("Brush.Bg");
            GlassBackdrop.Apply(this, 0xD8);
            Content = new Border { Padding = new Thickness(30, 24, 30, 22), Child = Body(status) };
            KeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };
        }

        private UIElement Body(string status)
        {
            var root = new StackPanel();
            root.Children.Add(new TextBlock { Text = "DEVICE", Style = (Style)FindResource("T.Overline") });
            root.Children.Add(new TextBlock { Text = _device.Name, Style = (Style)FindResource("T.PageTitle"), Margin = new Thickness(0, 6, 0, 0) });
            root.Children.Add(new TextBlock
            {
                Text = (_device.DetectedName ?? _device.Kind) + " · " + (_device.Connection ?? "no connection") + (string.IsNullOrEmpty(status) ? "" : " · " + status),
                Style = (Style)FindResource("T.Secondary"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 18)
            });

            root.Children.Add(Section("Name"));
            var name = new TextBox { Text = _device.Name, MaxLength = 40, Width = 340, HorizontalAlignment = HorizontalAlignment.Left };
            name.TextChanged += (s, e) => { if (name.Text.Trim().Length > 0) _device.Name = name.Text.Trim(); };
            root.Children.Add(name);

            var use = new CheckBox { Style = (Style)FindResource("Switch"), Content = "Use this device", IsChecked = _device.Enabled, Margin = new Thickness(0, 14, 0, 0) };
            use.Click += (s, e) => _device.Enabled = use.IsChecked == true;
            root.Children.Add(use);

            root.Children.Add(Section("LEDs"));
            if (_device.Mapped)
            {
                root.Children.Add(new TextBlock { Text = _device.Layout.Describe() + ".", Style = (Style)FindResource("T.Secondary"), TextWrapping = TextWrapping.Wrap });
                root.Children.Add(Drawing());
                foreach (LayoutPiece piece in _device.Layout.Pieces())
                    root.Children.Add(new TextBlock { Text = piece.Name + " · " + piece.Slots.Length + " LEDs · " + _device.Layout.RoleOf(piece), Style = (Style)FindResource("T.Caption"), Margin = new Thickness(0, 0, 0, 3) });
            }
            else root.Children.Add(new TextBlock { Text = "Where its LEDs are hasn't been set up yet, so its lights may run in the wrong order.", Style = (Style)FindResource("T.Secondary"), TextWrapping = TextWrapping.Wrap });
            var setUp = new Button { Content = _device.Mapped ? "Set up LEDs again" : "Set up its LEDs", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0), ToolTip = "Describe its shape and show the app where each LED is" };
            setUp.Click += (s, e) => Finish(Outcome.SetUpLeds);
            root.Children.Add(setUp);

            if (_device.Kind != LedDeviceKinds.Wled)
            {
                root.Children.Add(Section("Power"));
                root.Children.Add(DeviceUi.PowerPicker(this, _device));
            }
            if (_device.Kind == LedDeviceKinds.Arduino)
                root.Children.Add(new TextBlock
                {
                    Text = "Fast updates (several colour changes per message) are used automatically when the board accepts them.",
                    Style = (Style)FindResource("T.Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0)
                });

            // The buttons stay in view below the scrolling settings.
            var footer = new Grid { Margin = new Thickness(0, 18, 0, 0) };
            footer.ColumnDefinitions.Add(new ColumnDefinition());
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var remove = new Button { Content = "Remove device", Style = (Style)FindResource("Btn.Ghost"), Foreground = (Brush)FindResource("Brush.Danger"), HorizontalAlignment = HorizontalAlignment.Left };
            remove.Click += (s, e) =>
            {
                if (MessageBox.Show(this, "Remove " + _device.Name + "? Light layers aimed at its groups stop lighting anything until a device with those groups is set up.",
                        "Remove device", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK)
                    Finish(Outcome.Removed);
            };
            footer.Children.Add(remove);
            var right = new StackPanel { Orientation = Orientation.Horizontal };
            var test = new Button { Content = "Test", Margin = new Thickness(0, 0, 8, 0), ToolTip = "Saves, then lights each part in turn the way it fills" };
            test.Click += (s, e) => Finish(Outcome.Test);
            var done = new Button { Content = "Done", Style = (Style)FindResource("Btn.Primary"), MinWidth = 96, IsDefault = true };
            done.Click += (s, e) => Finish(Outcome.Saved);
            right.Children.Add(test);
            right.Children.Add(done);
            Grid.SetColumn(right, 1);
            footer.Children.Add(right);
            var frame = new DockPanel();
            DockPanel.SetDock(footer, Dock.Bottom);
            frame.Children.Add(footer);
            frame.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root, Padding = new Thickness(0, 0, 8, 0) });
            return frame;
        }

        private FrameworkElement Drawing()
        {
            LayoutSlot[] placed = _device.Layout.SlotOfLed();
            double size = LedDrawing.FitSize(_device, 520, 18);
            var dots = new List<FrameworkElement>();
            for (int led = 0; led < placed.Length; led++)
            {
                var cell = new Grid { Width = size, Height = size, ToolTip = "LED " + (led + 1) };
                cell.Children.Add(new Ellipse { Fill = (Brush)FindResource("Brush.AccentSubtle"), Stroke = (Brush)FindResource("Brush.Accent"), StrokeThickness = 1 });
                if (size >= 16) cell.Children.Add(new TextBlock { Text = (led + 1).ToString(), FontSize = size * 0.42, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                dots.Add(cell);
            }
            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(5, 7, 10)), BorderBrush = (Brush)FindResource("Brush.Border"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10), Padding = new Thickness(16), Margin = new Thickness(0, 10, 0, 10),
                Child = LedDrawing.Arrange(_device, dots, size)
            };
        }

        private TextBlock Section(string text) => new TextBlock { Text = text.ToUpperInvariant(), Style = (Style)FindResource("T.Overline"), Margin = new Thickness(0, 22, 0, 8) };

        private void Finish(Outcome outcome)
        {
            Choice = outcome;
            DialogResult = outcome != Outcome.Cancelled;
        }
    }
}
