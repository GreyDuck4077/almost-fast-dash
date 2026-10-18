using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using DrawingColor = System.Drawing.Color;

namespace AlmostFastDash.UI
{
    internal sealed partial class DeviceSetupWindow : Window
    {
        // ------------------------------------------------------------------ 3. shape

        private StackPanel _shapeFields;

        private ContentControl _shapeDrawing;

        private TextBlock _shapeTotal;

        private int Limit => _device.Kind == LedDeviceKinds.Wled ? 1500 : _device.Kind == LedDeviceKinds.Adalight ? 300 : ArduinoLedController.MaxLeds;

        private bool ShapeReady() => !string.IsNullOrEmpty(_layout.Shape) && _layout.Total >= 1 && _layout.Total <= Limit;

        private void ShowShape()
        {
            _title.Text = "What does it look like?";
            _subtitle.Text = "Pick the kind of device and count the LEDs in each part exactly: a board sent colors for LEDs it doesn't have can freeze.";
            var body = new StackPanel();
            var choices = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 18) };
            choices.Children.Add(ShapeChoice(LedShapes.Screen, "Screen with LEDs", "LEDs down the sides of a wheel screen, across the top, or both."));
            choices.Children.Add(ShapeChoice(LedShapes.Bars, "Light bars", "One bar or several side by side, like brow lights."));
            choices.Children.Add(ShapeChoice(LedShapes.Strip, "LED strip", "A single straight strip of any length."));
            body.Children.Add(choices);
            _shapeFields = new StackPanel();
            body.Children.Add(_shapeFields);
            _shapeTotal = new TextBlock { Style = (Style)FindResource("T.Label"), Margin = new Thickness(0, 18, 0, 8) };
            body.Children.Add(_shapeTotal);
            _shapeDrawing = new ContentControl { HorizontalAlignment = HorizontalAlignment.Left };
            body.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(5, 7, 10)), BorderBrush = (Brush)FindResource("Brush.Border"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12), Padding = new Thickness(20), Child = _shapeDrawing, MinHeight = 80
            });
            _body.Content = body;
            RenderShapeFields();
        }

        private Border ShapeChoice(string shape, string title, string text)
        {
            var card = Card();
            card.Margin = new Thickness(0, 0, 10, 0);
            card.Cursor = Cursors.Hand;
            card.BorderThickness = new Thickness(1.5);
            card.BorderBrush = (Brush)FindResource(_layout.Shape == shape ? "Brush.Accent" : "Brush.Border");
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = title, Style = (Style)FindResource("T.Label") });
            stack.Children.Add(new TextBlock { Text = text, Style = (Style)FindResource("T.Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
            card.Child = stack;
            card.MouseLeftButtonUp += (s, e) =>
            {
                if (_layout.Shape == shape) return;
                _layout = new LedLayout { Shape = shape };
                _mapper = null;
                ShowShape();
            };
            return card;
        }

        private void RenderShapeFields()
        {
            _shapeFields.Children.Clear();
            var row = new WrapPanel();
            switch (_layout.Shape)
            {
                case LedShapes.Screen:
                    row.Children.Add(CountField("Left side", _layout.Left, v => _layout.Left = v));
                    row.Children.Add(CountField("Across the top", _layout.Top, v => _layout.Top = v));
                    row.Children.Add(CountField("Right side", _layout.Right, v => _layout.Right = v));
                    row.Children.Add(CountField("Along the bottom", _layout.Bottom, v => _layout.Bottom = v));
                    break;
                case LedShapes.Bars:
                    // Only the joined switch follows the count, so the box being typed in keeps its focus.
                    row.Children.Add(CountField("How many bars", _layout.Bars, v => { _layout.Bars = v; RenderJoined(); }, 1, 8));
                    row.Children.Add(CountField("LEDs on each bar", _layout.PerBar, v => _layout.PerBar = v));
                    break;
                case LedShapes.Strip:
                    row.Children.Add(CountField("LEDs on the strip", _layout.Length, v => _layout.Length = v));
                    break;
                default:
                    _shapeFields.Children.Add(new TextBlock { Text = "Pick one of the three above.", Style = (Style)FindResource("T.Secondary") });
                    UpdateShape();
                    return;
            }
            _shapeFields.Children.Add(row);
            _shapeFields.Children.Add(_joined = new StackPanel());
            RenderJoined();
            _shapeFields.Children.Add(_roles = new StackPanel { Margin = new Thickness(0, 14, 0, 0) });
            RenderRoles();
            UpdateShape();
        }

        private StackPanel _roles, _joined;

        /// <summary>For two or more bars: whether they're joined into one long bar.</summary>
        private void RenderJoined()
        {
            if (_joined == null) return;
            _joined.Children.Clear();
            if (_layout.Shape != LedShapes.Bars || _layout.Bars < 2) return;
            var joined = new CheckBox { Style = (Style)FindResource("Switch"), Content = "Joined end to end into one long bar", IsChecked = _layout.Connected, Margin = new Thickness(0, 14, 0, 2) };
            joined.Click += (s, e) => { _layout.Connected = joined.IsChecked == true; _layout.Map = null; _mapper = null; UpdateShape(); RenderRoles(); };
            _joined.Children.Add(joined);
            _joined.Children.Add(new TextBlock
            {
                Text = "Joined bars work as one long bar; separate bars each work on their own.",
                Style = (Style)FindResource("T.Caption"), TextWrapping = TextWrapping.Wrap, MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left
            });
        }

        /// <summary>What each part of the shape is for, which decides which light layers use it.</summary>
        private void RenderRoles()
        {
            if (_roles == null) return;
            _roles.Children.Clear();
            List<LayoutPiece> pieces = _layout.Total > 0 ? _layout.Pieces() : new List<LayoutPiece>();
            if (pieces.Count == 0) return;
            _roles.Children.Add(new TextBlock { Text = "WHAT EACH PART IS FOR", Style = (Style)FindResource("T.Overline"), Margin = new Thickness(0, 0, 0, 8) });
            foreach (LayoutPiece piece in pieces)
            {
                LayoutPiece p = piece;
                var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                grid.Children.Add(new TextBlock { Text = p.Name + " · " + p.Slots.Length, VerticalAlignment = VerticalAlignment.Center });
                var role = new ComboBox { ItemsSource = LedPurposes.All, SelectedItem = _layout.RoleOf(p), MinWidth = 0 };
                var help = new TextBlock { Text = LedPurposes.Describe(_layout.RoleOf(p)), Style = (Style)FindResource("T.Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), TextWrapping = TextWrapping.Wrap };
                role.SelectionChanged += (s, e) =>
                {
                    string chosen = (string)role.SelectedItem;
                    if (chosen == p.DefaultRole) _layout.Roles.Remove(p.Id); else _layout.Roles[p.Id] = chosen;
                    help.Text = LedPurposes.Describe(chosen);
                };
                Grid.SetColumn(role, 1);
                Grid.SetColumn(help, 2);
                grid.Children.Add(role);
                grid.Children.Add(help);
                _roles.Children.Add(grid);
            }
        }

        private FrameworkElement CountField(string label, int value, Action<int> set, int min = 0, int max = 250)
        {
            var stack = new StackPanel { Margin = new Thickness(0, 0, 18, 0) };
            stack.Children.Add(new TextBlock { Text = label, Style = (Style)FindResource("T.Caption"), Margin = new Thickness(0, 0, 0, 6) });
            var box = new TextBox { Text = value > 0 ? value.ToString() : "", Width = 96, HorizontalAlignment = HorizontalAlignment.Left };
            box.TextChanged += (s, e) =>
            {
                int n = int.TryParse(box.Text.Trim(), out int parsed) ? Math.Max(min, Math.Min(max, parsed)) : min;
                set(n);
                _layout.Map = null;
                _mapper = null;
                UpdateShape();
                RenderRoles();
            };
            stack.Children.Add(box);
            return stack;
        }

        private void UpdateShape()
        {
            int total = _layout.Total;
            _shapeTotal.Text = total == 0 ? "" : total + (total == 1 ? " LED" : " LEDs") + " in all";
            _status.Text = total > Limit ? "That's more LEDs than this kind of board can drive (" + Limit + ")." : "";
            List<LayoutSlot> slots = total > 0 && total <= Limit ? _layout.Slots() : new List<LayoutSlot>();
            var dots = slots.Select(s => (FrameworkElement)new Ellipse
            {
                Width = 14, Height = 14, Fill = (Brush)FindResource("Brush.LedOff"), Stroke = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), StrokeThickness = 1
            }).ToList();
            _shapeDrawing.Content = slots.Count == 0
                ? (object)new TextBlock { Text = "The shape appears here as you fill it in.", Style = (Style)FindResource("T.Caption") }
                : LedDrawing.Arrange(slots.Select(s => (Point?)new Point(s.X, s.Y)).ToList(), dots, 14);
            _next.IsEnabled = ShapeReady();
        }
    }
}
