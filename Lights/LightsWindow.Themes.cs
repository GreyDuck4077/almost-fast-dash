using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AlmostFastOverlays
{
    /// <summary>The light designer's colour theme picker: one card that opens into a grid of themes.</summary>
    public partial class LightsWindow
    {
        private bool _themesOpen;

        /// <summary>For --ui-snapshot: the theme grid open and in view.</summary>
        internal void ShowThemes()
        {
            _themesOpen = true;
            BuildThemePicker();
            Dispatcher.BeginInvoke(new Action(() => ThemeHost.BringIntoView()), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void BuildThemePicker()
        {
            ThemeHost.Children.Clear();
            _colorContext.Theme = _profile.LedTheme();
            LayoutTheme current = _colorContext.Theme;
            bool custom = _profile.ThemeId == LightThemes.CustomId;
            string name = custom ? "Custom" : (LightThemes.Find(_profile.ThemeId) ?? LightThemes.Find(LightThemes.DefaultId)).Name;

            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(new Border { Width = 120, Height = 28, CornerRadius = new CornerRadius(7), Background = Ramp(current.Good, current.Warn, current.Bad), VerticalAlignment = VerticalAlignment.Center });
            var titles = new StackPanel { Margin = new Thickness(16, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            titles.Children.Add(new TextBlock { Text = "COLOUR THEME", Style = (Style)FindResource("T.Overline") });
            titles.Children.Add(new TextBlock { Text = name, Style = (Style)FindResource("T.Title"), Margin = new Thickness(0, 2, 0, 0) });
            Grid.SetColumn(titles, 1);
            header.Children.Add(titles);
            var toggle = new Button { Content = _themesOpen ? "Done" : "Change theme", VerticalAlignment = VerticalAlignment.Center };
            toggle.Click += (s, e) => { _themesOpen = !_themesOpen; BuildThemePicker(); };
            Grid.SetColumn(toggle, 2);
            header.Children.Add(toggle);
            ThemeHost.Children.Add(header);
            ThemeHost.Children.Add(new TextBlock
            {
                Text = "The colours layers call good, warn and bad: the low, middle and high of rev lights and meters, the shift flash and blind-spot warnings. Layers given a colour of their own keep it.",
                Style = (Style)FindResource("T.Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0)
            });
            if (!_themesOpen) return;

            var tiles = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
            tiles.Children.Add(Tile(LightThemes.CustomId, "Custom", _profile.CustomLow, _profile.CustomMid, _profile.CustomHigh));
            foreach (LightTheme theme in LightThemes.All) tiles.Children.Add(Tile(theme.Id, theme.Name, theme.Low, theme.Mid, theme.High));
            ThemeHost.Children.Add(tiles);

            if (custom)
            {
                var colours = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
                colours.Children.Add(CustomColour("Low (good)", _profile.CustomLow, v => _profile.CustomLow = v));
                colours.Children.Add(CustomColour("Middle (warn)", _profile.CustomMid, v => _profile.CustomMid = v));
                colours.Children.Add(CustomColour("High (bad)", _profile.CustomHigh, v => _profile.CustomHigh = v));
                ThemeHost.Children.Add(colours);
            }
        }

        private Border Tile(string id, string name, string low, string mid, string high)
        {
            bool selected = _profile.ThemeId == id || (id == LightThemes.DefaultId && LightThemes.Find(_profile.ThemeId) == null && _profile.ThemeId != LightThemes.CustomId);
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 8) });
            stack.Children.Add(new Border { Height = 16, CornerRadius = new CornerRadius(4), Background = Ramp(low, mid, high) });
            var tile = new Border
            {
                Width = 132, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(10, 9, 10, 10), CornerRadius = new CornerRadius(8), Cursor = Cursors.Hand,
                Background = (Brush)FindResource(selected ? "Brush.AccentSubtle" : "Brush.SurfaceRaised"),
                BorderBrush = (Brush)FindResource(selected ? "Brush.Accent" : "Brush.Border"), BorderThickness = new Thickness(1), Child = stack, ToolTip = name
            };
            tile.MouseLeftButtonUp += (s, e) =>
            {
                if (_profile.ThemeId == id) return;
                _profile.ThemeId = id;
                ThemeChanged("Theme " + name);
            };
            return tile;
        }

        private FrameworkElement CustomColour(string label, string value, Action<string> set)
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            var field = new ColorField(this, value, null, () => _dashboard.AccentColor, false);
            field.Changed += v => { set(v); ThemeChanged("Custom colours", false); };
            Grid.SetColumn(field, 1);
            row.Children.Add(field);
            return row;
        }

        /// <summary>A new theme: the preview, layer swatches and colour fields all follow it.</summary>
        private void ThemeChanged(string label, bool rebuild = true)
        {
            _renderer = null;
            _colorContext.Theme = _profile.LedTheme();
            if (rebuild) BuildThemePicker();
            else if (ThemeHost.Children.Count > 0 && ThemeHost.Children[0] is Grid header && header.Children[0] is Border swatch)
                swatch.Background = Ramp(_colorContext.Theme.Good, _colorContext.Theme.Warn, _colorContext.Theme.Bad);
            CommitSoon(label);
            RefreshLayers();
            if (rebuild) RefreshInspector();
        }

        private static Brush Ramp(string low, string mid, string high)
        {
            Color C(string hex)
            {
                try { return (Color)ColorConverter.ConvertFromString(hex); } catch { return Colors.Black; }
            }
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
            brush.GradientStops.Add(new GradientStop(C(low), 0));
            brush.GradientStops.Add(new GradientStop(C(mid), 0.5));
            brush.GradientStops.Add(new GradientStop(C(high), 1));
            brush.Freeze();
            return brush;
        }
    }
}
