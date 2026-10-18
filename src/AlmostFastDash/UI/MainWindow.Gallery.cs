using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using DrawingColor = System.Drawing.Color;

namespace AlmostFastDash.UI
{
    public partial class MainWindow : Window
    {
        // ------------------------------------------------------------------
        // Gallery
        // ------------------------------------------------------------------

        private const double CardGap = 20, CardMinWidth = 340;

        private void BuildGalleryCards()
        {
            StopCardMotion();
            DashboardCards.Children.Clear();
            _galleryCards.Clear();
            var units = new UnitSystem(_settings.General);
            foreach (DashboardSettings dashboard in _settings.Dashboards)
            {
                DashboardSettings d = dashboard;
                bool isDefault = d.Id == _settings.DefaultDashboardId;
                var card = new Border
                {
                    Style = (Style)FindResource("Card"), Padding = new Thickness(0), Margin = new Thickness(0, 0, CardGap, CardGap),
                    Cursor = Cursors.Hand, Background = (Brush)FindResource("Brush.Surface")
                };
                // Pointed at, a card lifts (below) and glows faintly in its dashboard's own accent.
                Color glow = Colors.Teal;
                try { glow = (Color)ColorConverter.ConvertFromString(string.IsNullOrWhiteSpace(d.AccentColor) ? "#2DD4BF" : d.AccentColor); } catch { }
                var halo = new System.Windows.Media.Effects.DropShadowEffect { Color = glow, BlurRadius = 34, ShadowDepth = 0, Opacity = 0.32 };
                card.MouseEnter += (s, e) => { card.BorderBrush = (Brush)FindResource("Brush.BorderStrong"); card.Effect = halo; };
                card.MouseLeave += (s, e) => { card.BorderBrush = (Brush)FindResource("Brush.Border"); card.Effect = null; };
                card.MouseLeftButtonUp += (s, e) => { if (!(e.OriginalSource is DependencyObject o && FindParent<Button>(o) != null)) ShowEditor(d); };

                var stack = new StackPanel();
                var thumbnail = new Border
                {
                    // Dashboards for other screen shapes show whole, centred in the card's picture.
                    CornerRadius = new CornerRadius(11, 11, 0, 0), Background = new ImageBrush(RenderPreview(d, units, null)) { Stretch = Stretch.Uniform },
                    BorderBrush = (Brush)FindResource("Brush.Border"), BorderThickness = new Thickness(0, 0, 0, 1)
                };
                stack.Children.Add(thumbnail);
                // Under the pointer, the picture plays the animated lap.
                AnimateCardOnHover(card, thumbnail, d, units);

                var body = new StackPanel { Margin = new Thickness(20, 16, 20, 10) };
                var title = new StackPanel { Orientation = Orientation.Horizontal };
                title.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = BrushFor(d.AccentColor), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
                title.Children.Add(new TextBlock { Text = d.Name, Style = (Style)FindResource("T.Title"), VerticalAlignment = VerticalAlignment.Center });
                if (isDefault) title.Children.Add(Pill("Default", "Brush.SuccessSubtle", "Brush.Success", new Thickness(10, 0, 0, 0)));
                body.Children.Add(title);
                body.Children.Add(new TextBlock { Text = Describe(d), TextWrapping = TextWrapping.Wrap, Style = (Style)FindResource("T.Caption"), Margin = new Thickness(0, 6, 0, 12) });

                var tags = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
                if (d.RaceTypes.Count == 0)
                    tags.Children.Add(Pill(isDefault ? "Used whenever no other match" : "Not assigned to a track type", "Brush.SurfaceRaised", "Brush.TextTertiary", new Thickness(0, 0, 6, 6)));
                foreach (string type in Disciplines.All.Where(d.RaceTypes.Contains))
                    tags.Children.Add(Pill(type, "Brush.SurfaceRaised", "Brush.TextSecondary", new Thickness(0, 0, 6, 6)));
                System.Drawing.Size built = d.ScreenSize();
                tags.Children.Add(Pill(built.Width + " × " + built.Height, "Brush.SurfaceRaised", "Brush.TextTertiary", new Thickness(0, 0, 6, 6)));
                body.Children.Add(tags);

                // Wraps rather than clips when a card is narrow.
                var actions = new WrapPanel();
                var customize = new Button { Content = "Customize", Margin = new Thickness(0, 0, 8, 8) };
                customize.Click += (s, e) => ShowEditor(d);
                actions.Children.Add(customize);
                var design = new Button { Content = "Design", Style = (Style)FindResource("Btn.Primary"), Margin = new Thickness(0, 0, 8, 8) };
                design.Click += (s, e) => OpenDesigner(d);
                actions.Children.Add(design);
                if (!isDefault)
                {
                    var makeDefault = new Button { Content = "Set as default", Style = (Style)FindResource("Btn.Ghost"), Margin = new Thickness(0, 0, 0, 8) };
                    makeDefault.Click += (s, e) => MakeDefault(d);
                    actions.Children.Add(makeDefault);
                }
                body.Children.Add(actions);
                stack.Children.Add(body);
                card.Child = stack;
                card.Tag = thumbnail;
                _galleryCards.Add(card);
                Motion.Lift(card);
                DashboardCards.Children.Add(card);
            }
            LayoutCards(DashboardCards, _galleryCards);
        }

        private void DashboardCards_SizeChanged(object sender, SizeChangedEventArgs e) => LayoutCards(DashboardCards, _galleryCards);

        /// <summary>
        /// Lays out a grid of dashboard cards in as many equal columns as fit.
        /// A card whose Tag is its thumbnail keeps that thumbnail in the DV-5's shape.
        /// </summary>
        private static void LayoutCards(Panel panel, List<Border> cards)
        {
            double available = panel.ActualWidth;
            if (available <= 0) return;
            int columns = Math.Max(1, (int)((available + CardGap) / (CardMinWidth + CardGap)));
            // No empty columns: two dashboards share the row instead of leaving room for a third.
            columns = Math.Max(1, Math.Min(columns, Math.Max(cards.Count, 2)));
            double width = Math.Floor((available - CardGap * columns) / columns);
            foreach (Border card in cards)
            {
                card.Width = width;
                if (card.Tag is Border thumbnail) thumbnail.Height = Math.Round((width - 2) * ScreenSizes.DefaultHeight / ScreenSizes.DefaultWidth);
            }
        }

        private void NewDashboard_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu { PlacementTarget = NewDashboardButton, Placement = PlacementMode.Bottom };

            var designHeader = new StackPanel { Margin = new Thickness(0, 1, 0, 1) };
            designHeader.Children.Add(new TextBlock { Text = "New dashboard…", FontWeight = FontWeights.SemiBold });
            designHeader.Children.Add(new TextBlock { Text = "Start from a template or a blank canvas", Style = (Style)FindResource("T.Caption"), MaxWidth = 260, TextWrapping = TextWrapping.Wrap });
            var designItem = new MenuItem { Header = designHeader, Icon = GlyphIcon("") };
            designItem.Click += (s, a) =>
            {
                DesignTemplate template = TemplatePicker.Choose(this, new UnitSystem(_settings.General), _settings.General, out System.Drawing.Size screen);
                if (template == null) return;
                DashboardSettings created = _settings.AddDesignedDashboard(template, null, screen);
                Save();
                ShowEditor(created);
                OpenDesigner(created);
            };
            menu.Items.Add(designItem);
            var importItem = new MenuItem { Header = "Import a design (.afodash)…", Icon = GlyphIcon("") };
            importItem.Click += (s, a) => ImportDesign();
            menu.Items.Add(importItem);
            menu.IsOpen = true;
        }

        /// <summary>One line under a dashboard's name: the template's description, or what's in a design of the user's own.</summary>
        private static string Describe(DashboardSettings d)
        {
            DesignTemplate template = DesignTemplates.All.FirstOrDefault(t => t.Id != "blank" && string.Equals(t.Name, d.Name, StringComparison.OrdinalIgnoreCase));
            if (template != null) return template.Description;
            int widgets = d.Design?.AllWidgets().Count() ?? 0, screens = d.Design?.Screens.Count ?? 1;
            return "Your own design: " + widgets + " widgets on " + screens + (screens == 1 ? " screen" : " screens") + ".";
        }
    }
}
