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
        private static readonly HashSet<string> CollapsedGroups = new HashSet<string> { "Effects", "Visibility" };
        private readonly List<FormulaBox> _liveFormulas = new List<FormulaBox>();
        private readonly Dictionary<string, TextBox> _positionBoxes = new Dictionary<string, TextBox>();
        private FrameworkElement _primaryEditor;
        private TelemetrySnapshot _lastSnapshot;
        private bool _lastFlash;
        private DateTime _formulaRefreshUtc;

        private EvalScope CurrentScope() => new EvalScope
        {
            T = _lastSnapshot ?? IRacingTelemetrySource.CreatePreviewSnapshot(),
            Units = _units,
            Theme = _layout.Theme,
            Accent = _dashboard.AccentColor,
            ShiftFlashOn = _lastFlash
        };

        private void UpdateFormulaResults(TelemetrySnapshot snapshot, bool flash)
        {
            _lastSnapshot = snapshot;
            _lastFlash = flash;
            if ((DateTime.UtcNow - _formulaRefreshUtc).TotalMilliseconds < 200) return;
            _formulaRefreshUtc = DateTime.UtcNow;
            foreach (FormulaBox box in _liveFormulas) box.ShowResult();
        }

        // ==================================================================
        // Inspector
        // ==================================================================

        private void RefreshInspector()
        {
            if (Inspector == null) return;
            Inspector.Children.Clear();
            _liveFormulas.Clear();
            _positionBoxes.Clear();
            _propRows.Clear();
            _groupHeaders.Clear();
            _primaryEditor = null;
            if (_selection.Count == 0) BuildScreenInspector();
            else if (_selection.Count > 1) BuildMultiInspector();
            else BuildWidgetInspector(_selection[0]);
        }

        private TextBlock Heading(string text) => new TextBlock { Text = text, Style = (Style)FindResource("T.Title"), Margin = new Thickness(0, 0, 0, 4) };
        private TextBlock Caption(string text, double top = 0) => new TextBlock { Text = text, Style = (Style)FindResource("T.Caption"), Margin = new Thickness(0, top, 0, 0) };

        private Grid Tip(string glyph, string title, string text)
        {
            var grid = new Grid { Margin = new Thickness(0, 4, 0, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            TextBlock icon = Glyph(glyph, 15, "Brush.Accent");
            icon.VerticalAlignment = VerticalAlignment.Top;
            icon.Margin = new Thickness(0, 2, 0, 0);
            grid.Children.Add(icon);
            var body = new StackPanel();
            body.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold });
            body.Children.Add(Caption(text));
            Grid.SetColumn(body, 1);
            grid.Children.Add(body);
            return grid;
        }

        /// <summary>
        /// For a widget drawn as a ghost: why it's faint, and a button to preview the moment it shows in, where there's one.
        /// </summary>
        private void AddGhostNotice(IEnumerable<DashWidget> widgets)
        {
            DashWidget ghost = widgets.FirstOrDefault(w => _ghosts.ContainsKey(w.Id));
            if (ghost == null) return;
            bool sample = ghost.Type == "flag" || ghost.Type == "blindspot";
            string condition = LayoutRenderer.Strip(ghost.Get("visible", "")).Trim();
            string moment = ghost.Type == "flag" ? "Yellow flag" : ghost.Type == "blindspot" ? "Cars alongside"
                : condition.Contains("OnPitRoad") || condition.Contains("PitLimiter") || condition.Contains("OverPitLimit") ? "Pit lane"
                : condition.Contains("Flag") ? "Yellow flag" : null;
            Grid tip = sample
                ? Tip("", "A sample", (ghost.Type == "flag" ? "No flag is out in this preview" : "No car is alongside in this preview") +
                    ", so it's drawn faintly. On the screen it " + (ghost.Type == "flag" ? "shows the flag that's out." : "lights while a car is alongside."))
                : Tip("", "Hidden right now", "It shows when " + (condition.Length > 80 ? condition.Substring(0, 78) + "…" : condition) +
                    ". It's drawn faintly here; Show when is under Visibility.");
            if (moment != null)
            {
                var see = new Button { Content = "Preview " + moment.ToLowerInvariant() + " ›", Style = (Style)FindResource("Btn.Ghost"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-8, 2, 0, 0) };
                see.Click += (s, e) => PreviewCombo.SelectedItem = moment;
                ((StackPanel)tip.Children[1]).Children.Add(see);
            }
            Inspector.Children.Add(new Border
            {
                Background = (Brush)FindResource("Brush.AccentSubtle"), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8, 10, 4), Margin = new Thickness(0, 10, 0, 0), Child = tip
            });
        }
    }
}
