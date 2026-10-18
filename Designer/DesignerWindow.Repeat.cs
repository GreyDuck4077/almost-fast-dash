using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace AlmostFastOverlays
{
    public partial class DesignerWindow
    {
        // ==================================================================
        // Repeat: copies in rows and columns or around a circle, shown on the canvas while they're set up
        // ==================================================================

        /// <summary>The last settings used, for the next repeat in this session.</summary>
        private static RepeatSpec _lastRepeat = new RepeatSpec();

        private void RepeatSelection()
        {
            if (_selection.Count == 0) { StatusText.Text = "Select what to repeat first."; return; }
            List<DashWidget> originals = _screen.Widgets.Where(_selection.Contains).ToList();
            var saved = originals.Select(w => new { Widget = w, w.Group, w.GroupName, w.Slot }).ToList();
            RepeatSpec spec = _lastRepeat.Copy();
            string name = originals.Count == 1 && !string.IsNullOrWhiteSpace(originals[0].Name) ? originals[0].Name.Trim() : "Repeat";
            int insertAt = originals.Max(w => _screen.Widgets.IndexOf(w)) + 1;
            var preview = new List<DashWidget>();

            var summary = new TextBlock { Style = (Style)FindResource("T.Caption"), Margin = new Thickness(0, 14, 0, 0), TextWrapping = TextWrapping.Wrap };

            void Restore()
            {
                foreach (DashWidget w in preview) _screen.Widgets.Remove(w);
                preview.Clear();
                foreach (var s in saved) { s.Widget.Group = s.Group; s.Widget.GroupName = s.GroupName; s.Widget.Slot = s.Slot; }
            }
            void Show()
            {
                Restore();
                List<List<DashWidget>> sets = Repeater.Build(originals, spec);
                Repeater.Group(originals, sets, spec.Number, name);
                preview.AddRange(sets.SelectMany(s => s));
                _screen.Widgets.InsertRange(Math.Min(insertAt, _screen.Widgets.Count), preview);
                _rendererDirty = true;
                RequestRender();
                UpdateOverlay();
                int total = sets.Count + 1;
                summary.Text = total + " in all: the original and " + sets.Count + (sets.Count == 1 ? " copy" : " copies") +
                    (spec.Number ? ", numbered " + originals[0].Slot.ToString("0.##", CultureInfo.InvariantCulture) + " to " +
                        (sets.Count > 0 ? sets[sets.Count - 1][0].Slot : originals[0].Slot).ToString("0.##", CultureInfo.InvariantCulture) : "") + "." +
                    (total >= Repeater.MostCopies ? " That's the most at once." : "");
            }

            TextBox Number(double value, double step, double min, double max, Action<double> set, bool whole = false)
            {
                var box = new TextBox
                {
                    Text = value.ToString("0.##", CultureInfo.InvariantCulture), FontFamily = (FontFamily)FindResource("Font.Mono"), FontSize = 12, Width = 76,
                    HorizontalAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Center
                };
                box.TextChanged += (s, e) =>
                {
                    if (!double.TryParse(box.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) || v < min || v > max) return;
                    set(whole ? Math.Round(v) : v);
                    Show();
                };
                AttachWheel(box, step, v => box.Text = Math.Max(min, Math.Min(max, v)).ToString("0.##", CultureInfo.InvariantCulture));
                return box;
            }
            (Border Bar, RadioButton First, RadioButton Second) Choice(string first, string second, bool firstOn, string group)
            {
                var a = new RadioButton { Style = (Style)FindResource("Segment"), MinWidth = 0, Content = first, GroupName = group, IsChecked = firstOn };
                var b = new RadioButton { Style = (Style)FindResource("Segment"), MinWidth = 0, Content = second, GroupName = group, IsChecked = !firstOn };
                var grid = new UniformGrid { Columns = 2 };
                grid.Children.Add(a);
                grid.Children.Add(b);
                return (new Border { Style = (Style)FindResource("Segmented"), Child = grid, Margin = new Thickness(0, 0, 0, 10) }, a, b);
            }

            var body = new StackPanel { Margin = new Thickness(20, 18, 20, 18) };
            body.Children.Add(new TextBlock { Text = originals.Count == 1 ? "Repeat " + name : "Repeat " + originals.Count + " widgets", Style = (Style)FindResource("T.Title"), Margin = new Thickness(0, 0, 0, 12) });
            var mode = Choice("Rows and columns", "Around a circle", !spec.Circle, "RepeatMode");
            body.Children.Add(mode.Bar);

            var grid = new StackPanel();
            grid.Children.Add(LabeledRow("Across", Number(spec.Across, 1, 1, 100, v => spec.Across = (int)v, true), "How many in each row, the original included."));
            grid.Children.Add(LabeledRow("Down", Number(spec.Down, 1, 1, 100, v => spec.Down = (int)v, true), "How many rows."));
            grid.Children.Add(LabeledRow("Gap across", Number(spec.GapX, 1, -2000, 2000, v => spec.GapX = v), "Space between each one and the next, in pixels. Negative overlaps them."));
            grid.Children.Add(LabeledRow("Gap down", Number(spec.GapY, 1, -2000, 2000, v => spec.GapY = v), "Space between rows, in pixels."));
            body.Children.Add(grid);

            var circle = new StackPanel();
            circle.Children.Add(LabeledRow("How many", Number(spec.Count, 1, 2, Repeater.MostCopies, v => spec.Count = (int)v, true), "How many in all, the original included."));
            TextBox sweep = Number(spec.Sweep, 15, -360, 360, v => spec.Sweep = v);
            var sweepRow = new StackPanel();
            sweepRow.Children.Add(sweep);
            var presets = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-6, 2, 0, 0) };
            foreach (int preset in new[] { 360, 270, 180, 90 })
            {
                var chip = new Button { Content = preset + "°", Style = (Style)FindResource("Btn.Ghost"), Height = 26, FontSize = 12, Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(0, 0, 2, 0) };
                chip.Click += (s, e) => sweep.Text = preset.ToString(CultureInfo.InvariantCulture);
                presets.Children.Add(chip);
            }
            sweepRow.Children.Add(presets);
            circle.Children.Add(LabeledRow("Angle °", sweepRow, "360 is a full circle, 180 a half. Negative goes counterclockwise."));
            circle.Children.Add(LabeledRow("Radius", Number(spec.Radius, 5, 1, 5000, v => spec.Radius = v), "From the middle of the circle to the middle of each one, in pixels."));
            circle.Children.Add(LabeledRow("Start at °", Number(spec.Start, 15, -360, 360, v => spec.Start = v),
                "Where the original sits: -90 is the top, 0 the right, 90 the bottom, 180 the left. The original stays put."));
            circle.Children.Add(new TextBlock { Text = "ROTATION", Style = (Style)FindResource("T.Overline"), Margin = new Thickness(2, 12, 0, 6) });
            var turn = Choice("Follow the circle", "Keep upright", spec.Follow, "RepeatTurn");
            turn.First.ToolTip = "Each copy turns with the circle, like a dial's ticks.";
            turn.Second.ToolTip = "Each copy stays the way the original is, like a dial's numbers.";
            turn.First.Checked += (s, e) => { spec.Follow = true; Show(); };
            turn.Second.Checked += (s, e) => { spec.Follow = false; Show(); };
            circle.Children.Add(turn.Bar);
            circle.Children.Add(LabeledRow("Extra turn °", Number(spec.TurnEach, 5, -360, 360, v => spec.TurnEach = v), "Turns each copy this much more than the one before, on top of the above."));
            body.Children.Add(circle);

            var numberSwitch = new CheckBox { Style = (Style)FindResource("Switch"), IsChecked = spec.Number, VerticalAlignment = VerticalAlignment.Center };
            numberSwitch.Click += (s, e) => { spec.Number = numberSwitch.IsChecked == true; Show(); };
            var numberRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
            numberRow.Children.Add(numberSwitch);
            numberRow.Children.Add(new TextBlock { Text = "Number them as Slot 1, 2, 3…", Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            body.Children.Add(numberRow);
            body.Children.Add(Caption("Each copy's formulas read its number as Slot, so =RevPct * 10 >= Slot lights a row one by one.", 6));
            body.Children.Add(summary);

            var ok = new Button { Content = "Repeat", Style = (Style)FindResource("Btn.Primary"), IsDefault = true, MinWidth = 100, Margin = new Thickness(8, 0, 0, 0) };
            var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90 };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            body.Children.Add(buttons);

            void ModeChanged()
            {
                spec.Circle = mode.Second.IsChecked == true;
                grid.Visibility = spec.Circle ? Visibility.Collapsed : Visibility.Visible;
                circle.Visibility = spec.Circle ? Visibility.Visible : Visibility.Collapsed;
                Show();
            }
            mode.First.Checked += (s, e) => ModeChanged();
            mode.Second.Checked += (s, e) => ModeChanged();
            grid.Visibility = spec.Circle ? Visibility.Collapsed : Visibility.Visible;
            circle.Visibility = spec.Circle ? Visibility.Visible : Visibility.Collapsed;

            var dialog = new Window
            {
                Title = "Repeat", Owner = this, Width = 372, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = (Brush)FindResource("Brush.Surface"),
                Foreground = (Brush)FindResource("Brush.Text"), FontFamily = (FontFamily)FindResource("Font.Ui"), Content = body
            };
            // Over the Properties panel, so the canvas stays in view while it's set up.
            PresentationSource source = PresentationSource.FromVisual(InspectorScroller);
            if (source?.CompositionTarget != null)
            {
                Point corner = source.CompositionTarget.TransformFromDevice.Transform(InspectorScroller.PointToScreen(new Point(0, 0)));
                dialog.WindowStartupLocation = WindowStartupLocation.Manual;
                dialog.Left = corner.X + Math.Max(0, InspectorScroller.ActualWidth - 372) / 2;
                dialog.Top = corner.Y;
            }
            ok.Click += (s, e) => dialog.DialogResult = true;
            Show();
            if (dialog.ShowDialog() != true)
            {
                Restore();
                _rendererDirty = true;
                RequestRender();
                UpdateOverlay();
                return;
            }
            _lastRepeat = spec.Copy();
            _selection.Clear();
            _selection.AddRange(originals.Concat(preview));
            Commit("Repeat");
            SelectionChanged();
            RequestRender();
            StatusText.Text = summary.Text + " Ctrl+Z undoes it.";
        }

        // ------------------------------------------------------------------ zoom to the selection

        /// <summary>Zooms and scrolls so the selection fills the view (the whole screen when nothing's selected).</summary>
        private void ZoomToSelection()
        {
            if (_selection.Count == 0) { ZoomFit(); return; }
            Rect r = Union(_selection);
            double w = CanvasScroller.ActualWidth - 160, h = CanvasScroller.ActualHeight - 200;
            if (w <= 0 || h <= 0) return;
            SetZoom(Math.Min(w / Math.Max(8, r.Width), h / Math.Max(8, r.Height)));
            CanvasScroller.UpdateLayout();
            Point middle = Overlay.TranslatePoint(new Point(r.X + r.Width / 2, r.Y + r.Height / 2), CanvasScroller);
            CanvasScroller.ScrollToHorizontalOffset(CanvasScroller.HorizontalOffset + middle.X - CanvasScroller.ViewportWidth / 2);
            CanvasScroller.ScrollToVerticalOffset(CanvasScroller.VerticalOffset + middle.Y - CanvasScroller.ViewportHeight / 2);
        }
    }
}
