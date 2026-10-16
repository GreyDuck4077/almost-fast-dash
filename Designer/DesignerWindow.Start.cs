using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace AlmostFastOverlays
{
    public partial class DesignerWindow
    {
        // ==================================================================
        // An empty screen's welcome: what to do first, with one-click starters
        // ==================================================================

        private Border _startCard;

        /// <summary>Shows the start card on an empty screen while the Move tool is out, and hides it otherwise.</summary>
        private void UpdateStartCard()
        {
            bool show = _screen != null && _screen.Widgets.Count == 0 && ActiveTool == Tool.Move && _editPath == null;
            if (!show)
            {
                if (_startCard != null) _startCard.Visibility = Visibility.Collapsed;
                return;
            }
            if (_startCard == null) BuildStartCard();
            if (_startCard.Visibility != Visibility.Visible)
            {
                _startCard.Visibility = Visibility.Visible;
                Motion.FadeIn(_startCard);
            }
        }

        private void BuildStartCard()
        {
            var body = new StackPanel();
            var badge = new Border
            {
                Width = 44, Height = 44, CornerRadius = new CornerRadius(12), Background = (Brush)FindResource("Brush.AccentSubtle"), HorizontalAlignment = HorizontalAlignment.Center,
                Child = new TextBlock { Text = "", Style = (Style)FindResource("T.Icon"), FontSize = 20, Foreground = (Brush)FindResource("Brush.Accent"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            body.Children.Add(badge);
            body.Children.Add(new TextBlock { Text = "Start your screen", Style = (Style)FindResource("T.Title"), FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 4) });
            body.Children.Add(new TextBlock
            {
                Text = "Drag widgets, live data and ready-made blocks in from the Library, or draw with the tools on the left. Or start with one of these:",
                Style = (Style)FindResource("T.Secondary"), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 14)
            });
            var starters = new UniformGrid { Columns = 3 };
            foreach ((string block, string glyph, string label) in new[] { ("Gear cluster", "", "Gear and revs"), ("LED strip", "", "Shift lights"), ("Lap times", "", "Lap times") })
            {
                DesignBlock starter = DesignBlocks.BuiltIn().FirstOrDefault(b => b.Name == block);
                if (starter == null) continue;
                var tile = new StackPanel();
                tile.Children.Add(new TextBlock { Text = glyph, Style = (Style)FindResource("T.Icon"), FontSize = 22, Foreground = (Brush)FindResource("Brush.Accent"), HorizontalAlignment = HorizontalAlignment.Center });
                tile.Children.Add(new TextBlock { Text = label, FontSize = 12.5, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) });
                var button = new Button { Content = tile, Height = 84, Margin = new Thickness(4), Padding = new Thickness(6), ToolTip = starter.Description };
                button.Click += (s, e) =>
                {
                    List<DashWidget> widgets = starter.Create();
                    Rect bounds = Union(widgets);
                    foreach (DashWidget w in widgets)
                    {
                        w.X += Math.Round((CanvasW - bounds.Width) / 2 - bounds.X);
                        w.Y += Math.Round((CanvasH - bounds.Height) / 2 - bounds.Y);
                    }
                    AddWidgets(widgets, "Add " + starter.Name);
                };
                starters.Children.Add(button);
            }
            body.Children.Add(starters);
            body.Children.Add(new TextBlock
            {
                Text = "Everything saves as you go · Ctrl+Z undoes · F1 lists every formula", Style = (Style)FindResource("T.Caption"),
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0)
            });
            _startCard = new Border
            {
                Width = 440, Padding = new Thickness(26, 24, 26, 20), CornerRadius = new CornerRadius(16), HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center, Background = (Brush)FindResource("Brush.SurfaceRaised"), BorderBrush = (Brush)FindResource("Brush.BorderStrong"),
                BorderThickness = new Thickness(1), Child = body, AllowDrop = true, Visibility = Visibility.Collapsed,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 40, ShadowDepth = 8, Opacity = 0.55, Color = Colors.Black }
            };
            // Things dragged in from the Library land on the screen beneath it.
            _startCard.DragOver += Overlay_DragOver;
            _startCard.Drop += Overlay_Drop;
            ((Grid)CanvasScroller.Parent).Children.Add(_startCard);
        }
    }
}
