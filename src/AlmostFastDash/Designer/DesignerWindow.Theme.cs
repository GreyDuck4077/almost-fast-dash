using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AlmostFastDash.Designer
{
    public partial class DesignerWindow
    {
        // ==================================================================
        // Theme
        // ==================================================================

        private static readonly (string Name, string Accent, string[] Colors)[] ThemePresets =
        {
            // background, background2, style, panel, paneltop, border, track, text, muted, dim
            ("Midnight", null,
                new[] { "#05070A", "#0E1622", "Bloom", "#0E131A", "#141B24", "#202935", "#1B232E", "#ECF1F7", "#808EA0", "#4E5A6A" }),
            ("Carbon", "#FF3B30",
            new[] { "#0A0A0A", "#1C1C1C", "Gradient", "#141414", "#1E1E1E", "#2C2C2C", "#262626", "#F2F2F2", "#8C8C8C", "#555555" }),
            ("Deep ocean", "#38BDF8",
            new[] { "#020812", "#06243F", "Vignette", "#0A1828", "#0F2236", "#173452", "#12263C", "#E6F4FF", "#7FA3C2", "#47647F" }),
            ("Neon", "#FF2BD6",
            new[] { "#020205", "#140A24", "Vignette", "#B0100A1C", "#C0180F2A", "#40FF2BD6", "#1E1530", "#FFFFFF", "#9D8FB8", "#5C5070" }),
            ("Le Mans", "#C8F04B",
            new[] { "#071008", "#10200F", "Gradient", "#0D1A0F", "#132415", "#22361F", "#1A2B19", "#F0F7E8", "#8AA084", "#566A50" }),
            ("Daylight", "#0A84FF",
            new[] { "#E9EDF2", "#FFFFFF", "Gradient", "#FFFFFF", "#F6F8FA", "#D3DAE3", "#DCE2EA", "#0E141B", "#5B6878", "#9AA5B3" }),
        };

        private void ApplyThemePreset((string Name, string Accent, string[] Colors) preset)
        {
            LayoutTheme theme = _layout.Theme;
            string[] c = preset.Colors;
            theme.Background = c[0]; theme.Background2 = c[1]; theme.BackgroundStyle = c[2]; theme.Panel = c[3]; theme.PanelTop = c[4];
            theme.Border = c[5]; theme.Track = c[6]; theme.Text = c[7]; theme.Muted = c[8]; theme.Dim = c[9];
            if (preset.Accent != null) _dashboard.AccentColor = preset.Accent;
            _rendererDirty = true;
            Commit("Theme " + preset.Name);
            BuildThemeEditor();
            RequestRender();
        }

        private void BuildThemeEditor()
        {
            if (ThemeEditor == null) return;
            ThemeEditor.Children.Clear();
            LayoutTheme theme = _layout.Theme;

            ThemeEditor.Children.Add(SectionTitle("Presets"));
            var presets = new WrapPanel();
            foreach (var preset in ThemePresets)
            {
                var swatch = new Border
                {
                    Width = 72, Height = 44, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 6, 6), Cursor = Cursors.Hand,
                    BorderBrush = (Brush)FindResource("Brush.BorderStrong"), BorderThickness = new Thickness(1),
                    ToolTip = "Apply the " + preset.Name + " theme",
                    Background = new LinearGradientBrush(ToMedia(preset.Colors[1]), ToMedia(preset.Colors[0]), 90)
                };
                var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center };
                label.Children.Add(new Border { Width = 26, Height = 4, CornerRadius = new CornerRadius(2),
                    Background = new SolidColorBrush(ToMedia(preset.Accent ?? _dashboard.AccentColor)),
                    Margin = new Thickness(0, 0, 0, 4) });
                label.Children.Add(new TextBlock { Text = preset.Name, FontSize = 12,
                    Foreground = new SolidColorBrush(ToMedia(preset.Colors[7])) });
                swatch.Child = label;
                var p = preset;
                swatch.MouseLeftButtonUp += (s, e) => ApplyThemePreset(p);
                presets.Children.Add(swatch);
            }
            ThemeEditor.Children.Add(presets);

            ThemeEditor.Children.Add(SectionTitle("Brand"));
            ThemeEditor.Children.Add(ThemeColorRow("Accent", () => _dashboard.AccentColor,
                v => { _dashboard.AccentColor = v; _rendererDirty = true; },
                "Used by $accent and the LEDs' accent everywhere in this dashboard."));
            var fonts = FontCombo(theme.Font, false);
            fonts.SelectionChanged += (s,
                e) => { if (fonts.SelectedItem is string f) { theme.Font = f; _rendererDirty = true; Commit("Font"); RequestRender(); } };
            ThemeEditor.Children.Add(LabeledRow("Font", fonts, "The default font for every widget set to \"theme\"."));

            ThemeEditor.Children.Add(SectionTitle("Background"));
            var style = new ComboBox { ItemsSource = LayoutTheme.BackgroundStyles, SelectedItem = theme.BackgroundStyle };
            style.SelectionChanged += (s,
                e) => { theme.BackgroundStyle = (string)style.SelectedItem; Commit("Background"); RequestRender(); };
            ThemeEditor.Children.Add(LabeledRow("Style", style,
                "Bloom glows with the accent; Gradient and Vignette blend the two colors."));
            ThemeEditor.Children.Add(ThemeColorRow("Base", () => theme.Background, v => theme.Background = v));
            ThemeEditor.Children.Add(ThemeColorRow("Second", () => theme.Background2, v => theme.Background2 = v));

            ThemeEditor.Children.Add(SectionTitle("Surfaces"));
            ThemeEditor.Children.Add(ThemeColorRow("Panel", () => theme.Panel, v => theme.Panel = v, "$panel"));
            ThemeEditor.Children.Add(ThemeColorRow("Panel top", () => theme.PanelTop, v => theme.PanelTop = v,
                "$paneltop: the top of Soft panels."));
            ThemeEditor.Children.Add(ThemeColorRow("Border", () => theme.Border, v => theme.Border = v, "$border"));
            ThemeEditor.Children.Add(ThemeColorRow("Track", () => theme.Track, v => theme.Track = v,
                "$track: the unlit part of bars and gauges."));

            ThemeEditor.Children.Add(SectionTitle("Text"));
            ThemeEditor.Children.Add(ThemeColorRow("Text", () => theme.Text, v => theme.Text = v, "$text"));
            ThemeEditor.Children.Add(ThemeColorRow("Muted", () => theme.Muted, v => theme.Muted = v, "$muted: labels."));
            ThemeEditor.Children.Add(ThemeColorRow("Dim", () => theme.Dim, v => theme.Dim = v, "$dim"));

            ThemeEditor.Children.Add(SectionTitle("Status colors"));
            ThemeEditor.Children.Add(ThemeColorRow("Good", () => theme.Good, v => theme.Good = v, "$good: gaining, throttle, OK."));
            ThemeEditor.Children.Add(ThemeColorRow("Bad", () => theme.Bad, v => theme.Bad = v, "$bad: losing, brake, danger."));
            ThemeEditor.Children.Add(ThemeColorRow("Warning", () => theme.Warn, v => theme.Warn = v, "$warn"));
            ThemeEditor.Children.Add(ThemeColorRow("Info", () => theme.Info, v => theme.Info = v, "$info"));
            ThemeEditor.Children.Add(ThemeColorRow("Best lap", () => theme.Purple, v => theme.Purple = v, "$purple"));

            ThemeEditor.Children.Add(SectionTitle("Flags"));
            var flags = new CheckBox { Style = (Style)FindResource("Switch"), Content = "Full-screen flag alerts",
                IsChecked = _layout.FullScreenFlags };
            flags.Click += (s, e) => { _layout.FullScreenFlags = flags.IsChecked == true; Commit("Flags"); RequestRender(); };
            ThemeEditor.Children.Add(flags);
            ThemeEditor.Children.Add(new TextBlock
            {
                Style = (Style)FindResource("T.Caption"), Margin = new Thickness(0, 6, 0, 0),
                Text = "A new flag fills the screen for a few seconds; red and checkered stay until they end. Off: use a Flag widget instead."
            });
            var waving = new CheckBox { Style = (Style)FindResource("Switch"), Content = "Wave in a soft breeze",
                IsChecked = _layout.WavingFlags, Margin = new Thickness(0, 10, 0, 0) };
            waving.Click += (s, e) => { _layout.WavingFlags = waving.IsChecked == true; Commit("Flags"); RequestRender(); };
            ThemeEditor.Children.Add(waving);
            var frame = new CheckBox { Style = (Style)FindResource("Switch"), Content = "Frame the dashboard while a flag is out",
                IsChecked = _layout.FlagFrame, Margin = new Thickness(0, 10, 0, 0) };
            frame.Click += (s, e) => { _layout.FlagFrame = frame.IsChecked == true; Commit("Flags"); RequestRender(); };
            ThemeEditor.Children.Add(frame);
            ThemeEditor.Children.Add(new TextBlock
            {
                Style = (Style)FindResource("T.Caption"), Margin = new Thickness(0, 6, 0, 0),
                Text = "During a caution, the final lap or a black, repair or blue flag, a glow in its color runs around the edge."
            });
        }

        private Grid ThemeColorRow(string label, Func<string> get, Action<string> set, string help = null)
        {
            var editor = new ColorField(this, get(), _layout, () => _dashboard.AccentColor, allowTokens: false);
            editor.Changed += value =>
            {
                set(value);
                _rendererDirty = true;
                CommitSoon("Theme color");
                RequestRender();
            };
            return LabeledRow(label, editor, help);
        }

        private Grid LabeledRow(string label, FrameworkElement editor, string help = null)
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("Brush.TextSecondary"), ToolTip = help, TextTrimming = TextTrimming.CharacterEllipsis };
            row.Children.Add(text);
            Grid.SetColumn(editor, 1);
            row.Children.Add(editor);
            return row;
        }

        private static List<string> _fontNames;

        internal static List<string> FontNames()
        {
            if (_fontNames != null) return _fontNames;
            var preferred = new[] { "Bahnschrift", "Bahnschrift SemiBold", "Bahnschrift Condensed", "Bahnschrift SemiBold Condensed",
                "Bahnschrift Light", "Segoe UI", "Segoe UI Black", "Segoe UI Semibold", "Consolas", "Arial Black", "Impact" };
            using (var installed = new System.Drawing.Text.InstalledFontCollection())
            {
                var names = installed.Families.Select(f => f.Name).ToList();
                _fontNames = preferred.Where(names.Contains).Concat(names.Where(n => !preferred.Contains(n)).OrderBy(n => n)).ToList();
            }
            return _fontNames;
        }

        internal ComboBox FontCombo(string current, bool includeTheme)
        {
            var items = new List<string>();
            if (includeTheme) items.Add("theme");
            items.AddRange(FontNames());
            var combo = new ComboBox { ItemsSource = items, IsTextSearchEnabled = true, MaxDropDownHeight = 420 };
            combo.SelectedItem = items.FirstOrDefault(i => string.Equals(i, current, StringComparison.OrdinalIgnoreCase)) ?? (includeTheme
                ? "theme" : items.FirstOrDefault());
            // Each font named in its own typeface, as design tools list them; "theme" says which font that is. The list
            // only builds the rows in view, so hundreds of installed fonts open instantly.
            var face = new FrameworkElementFactory(typeof(TextBlock));
            face.SetBinding(TextBlock.TextProperty,
                new System.Windows.Data.Binding { Converter = new FontLabel(() => _layout?.Theme?.Font) });
            face.SetBinding(TextBlock.FontFamilyProperty,
                new System.Windows.Data.Binding { Converter = new FontFace(() => _layout?.Theme?.Font) });
            combo.ItemTemplate = new DataTemplate { VisualTree = face };
            combo.ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));
            VirtualizingPanel.SetIsVirtualizing(combo, true);
            TextSearch.SetTextPath(combo, ".");
            return combo;
        }

        /// <summary>A font's name in the list; "theme" names the theme's font.</summary>
        private sealed class FontLabel : System.Windows.Data.IValueConverter
        {
            private readonly Func<string> _theme;
            public FontLabel(Func<string> theme) { _theme = theme; }
            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
                value as string == "theme" ? "Theme font (" + (_theme() ?? "Bahnschrift") + ")" : value;
            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => value;
        }

        /// <summary>The typeface a font's name is shown in.</summary>
        private sealed class FontFace : System.Windows.Data.IValueConverter
        {
            private readonly Func<string> _theme;
            public FontFace(Func<string> theme) { _theme = theme; }
            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            {
                string name = value as string == "theme" ? _theme() ?? "Bahnschrift" : value as string;
                try { return new FontFamily(name ?? "Segoe UI"); } catch { return new FontFamily("Segoe UI"); }
            }
            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => value;
        }
    }
}
