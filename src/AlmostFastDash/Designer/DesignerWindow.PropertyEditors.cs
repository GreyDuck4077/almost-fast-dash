using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AlmostFastDash.Designer
{
    public partial class DesignerWindow
    {
        // ------------------------------------------------------------------ property rows

        /// <summary>
        /// One setting's row. With <paramref name="together"/> (several selected widgets) it shows their
        /// shared value, or "mixed" when they differ, and an edit applies to all of them.
        /// </summary>
        private FrameworkElement PropertyRow(DashWidget widget, PropDef def, IList<DashWidget> together = null)
        {
            IList<DashWidget> targets = together ?? new[] { widget };
            string raw = widget.Get(def.Key, def.Default) ?? "";
            bool mixed = targets.Any(t => (t.Get(def.Key, def.Default) ?? "") != raw);
            if (mixed && !raw.StartsWith("=")) raw = def.Default ?? "";
            bool alwaysFormula = def.Kind == PropKind.Formula;
            bool isFormula = alwaysFormula || raw.StartsWith("=");

            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            var label = new TextBlock
            {
                Text = mixed ? def.Label + " (mixed)" : def.Label, VerticalAlignment = isFormula ? VerticalAlignment.Top : VerticalAlignment.Center, Margin = new Thickness(0, isFormula ? 7 : 0, 8, 0),
                Foreground = (Brush)FindResource(raw == def.Default ? "Brush.TextSecondary" : "Brush.Text"), TextWrapping = TextWrapping.Wrap,
                ToolTip = def.Help
            };
            // Right-click a setting's name to make it a module option, or to link it to one.
            if (together == null && (label.ContextMenu = OptionMenu(widget, def)) != null) label.Background = Brushes.Transparent;
            row.Children.Add(label);

            void Apply(string value, bool structural = false)
            {
                foreach (DashWidget target in targets) target.Set(def.Key, value);
                if (structural) _rendererDirty = true;
                WidgetType owner = WidgetCatalog.Find(widget.Type);
                if (together == null && owner != null && owner.Props.Any(p => p.DependsOn == def.Key)) UpdateApplicableRows(widget, owner);
                label.Text = def.Label;
                label.Foreground = (Brush)FindResource(value == def.Default ? "Brush.TextSecondary" : "Brush.Text");
                RequestRender();
                CommitSoon("Edit " + def.Label.ToLowerInvariant());
            }

            FrameworkElement editor;
            if (isFormula)
            {
                // Formula results shown in the box use the widget's own Slot when it's in a numbered group.
                var box = new FormulaBox(this, LayoutRenderer.Strip(raw), () => LayoutRenderer.ForWidget(CurrentScope(), widget), def.Help);
                box.Changed += text => Apply(text.Trim().Length == 0 ? (alwaysFormula ? "" : def.Default) : "=" + text.Trim());
                _liveFormulas.Add(box);
                editor = box;
                if (_primaryEditor == null && (def.Key == "text" || def.Key == "value" || def.Key == "label")) _primaryEditor = box.Text;
            }
            else
            {
                editor = LiteralEditor(def, raw, Apply);
            }
            if (isFormula)
            {
                // Formulas get the full width on their own line under the label.
                row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                label.Margin = new Thickness(0, 4, 8, 4);
                Grid.SetColumnSpan(label, 2);
                Grid.SetRow(editor, 1);
                Grid.SetColumnSpan(editor, 3);
            }
            else Grid.SetColumn(editor, 1);
            row.Children.Add(editor);

            if (!alwaysFormula && def.Kind != PropKind.Image)
            {
                var fx = new ToggleButton
                {
                    Content = new TextBlock { Text = "ƒx", FontStyle = FontStyles.Italic, FontSize = 12, FontWeight = FontWeights.SemiBold }, IsChecked = isFormula, Width = 28, Height = 26,
                    Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Style = (Style)FindResource("Fx"),
                    ToolTip = isFormula ? "Back to a fixed value" : "Drive this with live data (a formula)"
                };
                fx.Click += (s, e) =>
                {
                    string current = widget.Get(def.Key, def.Default) ?? "";
                    Apply(fx.IsChecked == true ? "=" + ToFormula(def, current) : FromFormula(def, current));
                    Commit("Formula " + def.Label.ToLowerInvariant());
                    RefreshInspector();
                };
                Grid.SetColumn(fx, 2);
                row.Children.Add(fx);
            }
            return row;
        }

        /// <summary>Turns a fixed value into the equivalent formula.</summary>
        private static string ToFormula(PropDef def, string literal)
        {
            switch (def.Kind)
            {
                case PropKind.Number: return string.IsNullOrWhiteSpace(literal) ? "0" : literal;
                case PropKind.Bool: return Expr.Bool(literal) ? "true" : "false";
                case PropKind.Color:
                    if (string.IsNullOrWhiteSpace(literal)) return "$accent";
                    return literal.StartsWith("#") ? literal : "$" + literal;
                default:
                    return "\"" + (literal ?? "").Replace("\"", "'") + "\"";
            }
        }

        /// <summary>Turns a formula back into a fixed value: its current result where that makes sense.</summary>
        private string FromFormula(PropDef def, string formula)
        {
            object value = Expr.Evaluate(LayoutRenderer.Strip(formula), CurrentScope());
            switch (def.Kind)
            {
                case PropKind.Color:
                    System.Drawing.Color? c = Expr.Color(value, CurrentScope());
                    return c.HasValue ? (c.Value.A < 255 ? "#" + c.Value.A.ToString("X2") + SettingsStore.ToHex(c.Value).Substring(1) : SettingsStore.ToHex(c.Value)) : def.Default;
                case PropKind.Choice:
                    string choice = Expr.Text(value);
                    return def.Choices.Contains(choice) ? choice : def.Default;
                case PropKind.Bool: return Expr.Bool(value) ? "true" : "false";
                default:
                    string text = Expr.Text(value);
                    return text.Length == 0 ? def.Default : text;
            }
        }

        private FrameworkElement LiteralEditor(PropDef def, string raw, Action<string, bool> apply)
        {
            switch (def.Kind)
            {
                case PropKind.Number:
                {
                    var box = new TextBox { Text = raw, FontFamily = (FontFamily)FindResource("Font.Mono"), FontSize = 12, Width = 70, HorizontalAlignment = HorizontalAlignment.Left };
                    double step = def.Step > 0 ? def.Step : 1;
                    bool ranged = !double.IsNaN(def.Min) && !double.IsNaN(def.Max) && def.Max > def.Min;
                    Slider slider = null;
                    bool syncing = false;
                    box.TextChanged += (s, e) =>
                    {
                        if (!double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) return;
                        if (slider != null && !syncing) { syncing = true; slider.Value = Math.Max(def.Min, Math.Min(def.Max, v)); syncing = false; }
                        apply(box.Text.Trim(), false);
                    };
                    AttachWheel(box, step, v =>
                    {
                        if (ranged) v = Math.Max(def.Min, Math.Min(def.Max, v));
                        box.Text = v.ToString("0.###", CultureInfo.InvariantCulture);
                    });
                    if (!ranged) return box;
                    double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double initial);
                    slider = new Slider { Minimum = def.Min, Maximum = def.Max, Value = Math.Max(def.Min, Math.Min(def.Max, initial)), SmallChange = step, LargeChange = step * 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
                    slider.ValueChanged += (s, e) =>
                    {
                        if (syncing) return;
                        syncing = true;
                        double v = Math.Round(slider.Value / step) * step;
                        box.Text = v.ToString("0.###", CultureInfo.InvariantCulture);
                        syncing = false;
                    };
                    var grid = new Grid();
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    grid.ColumnDefinitions.Add(new ColumnDefinition());
                    grid.Children.Add(box);
                    Grid.SetColumn(slider, 1);
                    grid.Children.Add(slider);
                    return grid;
                }
                case PropKind.Color:
                {
                    bool optional = string.IsNullOrEmpty(def.Default);
                    var field = new ColorField(this, raw, _layout, () => _dashboard.AccentColor, true, optional);
                    field.Changed += v => apply(v, false);
                    return field;
                }
                case PropKind.Bool:
                {
                    var toggle = new CheckBox { Style = (Style)FindResource("Switch"), IsChecked = Expr.Bool(raw), VerticalAlignment = VerticalAlignment.Center };
                    toggle.Click += (s, e) => { apply(toggle.IsChecked == true ? "true" : "false", false); Commit("Edit " + def.Label.ToLowerInvariant()); };
                    return toggle;
                }
                case PropKind.Choice:
                {
                    var combo = new ComboBox { ItemsSource = def.Choices, SelectedItem = def.Choices.Contains(raw) ? raw : def.Default };
                    combo.SelectionChanged += (s, e) =>
                    {
                        apply((string)combo.SelectedItem, false);
                        Commit("Edit " + def.Label.ToLowerInvariant());
                    };
                    return combo;
                }
                case PropKind.Font:
                {
                    ComboBox combo = FontCombo(raw, true);
                    combo.SelectionChanged += (s, e) => { if (combo.SelectedItem is string f) { apply(f, false); Commit("Font"); } };
                    return combo;
                }
                case PropKind.Image:
                    return ImageEditor(raw, apply);
                default:
                {
                    var box = new TextBox { Text = raw };
                    box.TextChanged += (s, e) => apply(box.Text, false);
                    if (_primaryEditor == null && (def.Key == "text" || def.Key == "label" || def.Key == "value")) _primaryEditor = box;
                    return box;
                }
            }
        }

        private FrameworkElement ImageEditor(string raw, Action<string, bool> apply)
        {
            var panel = new StackPanel();
            if (!string.IsNullOrEmpty(raw) && _layout.Images.TryGetValue(raw, out string base64))
            {
                try
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.StreamSource = new MemoryStream(Convert.FromBase64String(base64));
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.DecodePixelWidth = 180;
                    image.EndInit();
                    panel.Children.Add(new Border
                    {
                        Background = ColorField.Checker(Colors.Transparent), CornerRadius = new CornerRadius(8), Padding = new Thickness(6), Margin = new Thickness(0, 0, 0, 6),
                        Child = new Image { Source = image, MaxHeight = 110, Stretch = Stretch.Uniform }
                    });
                }
                catch { }
            }
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            var choose = new Button { Content = string.IsNullOrEmpty(raw) ? "Choose image…" : "Replace…", Margin = new Thickness(0, 0, 6, 0) };
            choose.Click += (s, e) =>
            {
                var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Choose an image", Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif" };
                if (dialog.ShowDialog(this) != true) return;
                try
                {
                    string id = EmbedImage(dialog.FileName);
                    apply(id, true);
                    Commit("Image");
                    RefreshInspector();
                }
                catch (Exception exception)
                {
                    StatusText.Text = "Could not use that image: " + exception.Message;
                }
            };
            buttons.Children.Add(choose);
            if (!string.IsNullOrEmpty(raw))
            {
                var clear = new Button { Content = "Remove", Style = (Style)FindResource("Btn.Ghost") };
                clear.Click += (s, e) => { apply("", true); Commit("Image"); RefreshInspector(); };
                buttons.Children.Add(clear);
            }
            panel.Children.Add(buttons);
            panel.Children.Add(Caption("Stored in the design, so exports include it.", 6));
            return panel;
        }

        /// <summary>Embeds an image as PNG, scaled down to at most the screen size so designs stay small.</summary>
        private string EmbedImage(string path)
        {
            using (var source = new System.Drawing.Bitmap(path))
            {
                double scale = Math.Min(1, Math.Min(CanvasW * 1.5 / source.Width, CanvasH * 1.5 / source.Height));
                int w = Math.Max(1, (int)(source.Width * scale)), h = Math.Max(1, (int)(source.Height * scale));
                using (var resized = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    using (var g = System.Drawing.Graphics.FromImage(resized))
                    {
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.DrawImage(source, 0, 0, w, h);
                    }
                    using (var stream = new MemoryStream())
                    {
                        resized.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                        string id = "img-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                        _layout.Images[id] = Convert.ToBase64String(stream.ToArray());
                        return id;
                    }
                }
            }
        }

        /// <summary>Mouse wheel over a number box steps its value (Shift for ten steps).</summary>
        private static void AttachWheel(TextBox box, double step, Action<double> set)
        {
            box.PreviewMouseWheel += (s, e) =>
            {
                if (!box.IsKeyboardFocusWithin) return;
                if (!double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) v = 0;
                double amount = step * ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 10 : 1);
                set(Math.Round((v + (e.Delta > 0 ? amount : -amount)) / step) * step);
                e.Handled = true;
            };
        }
    }
}
