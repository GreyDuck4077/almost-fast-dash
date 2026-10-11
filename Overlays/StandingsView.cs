using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using DColor = System.Drawing.Color;

namespace AlmostFastOverlays
{
    /// <summary>
    /// The animated glass timing table behind both table overlays: the multiclass
    /// Standings and the on-track Relative (chosen by the overlay's type). Rows are
    /// keyed by car, so when the order changes each row slides to its new place, rows
    /// entering or leaving fade, and a car that gains or loses a place briefly flashes.
    /// </summary>
    internal sealed class StandingsView : Border
    {
        private const double RowH = 28, HeaderH = 36, LabelsH = 22, ClassH = 30, SpacerH = 8, Pad = 6;
        private static readonly FontFamily Ui = new FontFamily("Segoe UI Variable Display, Segoe UI");
        private static readonly FontFamily Digits = new FontFamily("Bahnschrift, Segoe UI");
        // Rows start moving at once and settle softly: quick enough to follow a pass, never jumpy.
        private static readonly Duration Slide = new Duration(TimeSpan.FromMilliseconds(300));
        private static readonly IEasingFunction Ease = Frozen(new CubicEase { EasingMode = EasingMode.EaseOut });
        private static readonly Duration FadeIn = new Duration(TimeSpan.FromMilliseconds(180)), FadeOut = new Duration(TimeSpan.FromMilliseconds(160));

        private readonly Canvas _canvas = new Canvas { ClipToBounds = false };
        private readonly Dictionary<string, FrameworkElement> _items = new Dictionary<string, FrameworkElement>();
        private readonly Dictionary<int, int> _lastPlace = new Dictionary<int, int>();
        private readonly ScaleTransform _scale = new ScaleTransform(1, 1);
        private OverlayConfig _config = new OverlayConfig();
        private List<Column> _columns = new List<Column>();
        private double _width;
        private string _signature;
        private int _alpha = -1;

        public StandingsView()
        {
            // The same Liquid Glass as the dashboards: a frosted navy body, a sheen across
            // the top and a rim that catches the light at the top edge.
            CornerRadius = new CornerRadius(16);
            BorderThickness = new Thickness(1);
            BorderBrush = GlassRim();
            var sheen = new Border
            {
                Height = 46, VerticalAlignment = VerticalAlignment.Top, CornerRadius = new CornerRadius(15, 15, 0, 0), IsHitTestVisible = false,
                Background = new LinearGradientBrush(Color.FromArgb(22, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90)
            };
            var body = new Grid();
            body.Children.Add(sheen);
            body.Children.Add(_canvas);
            Child = body;
            LayoutTransform = _scale;
            ClipToBounds = true;
            // No drop shadow: blurring a panel whose rows change would redraw the whole
            // overlay on every animation frame. The bright rim outlines it instead.
        }

        private bool IsRelative => _config.Type == "relative";

        /// <summary>The overlays' frosted navy body; alpha is the overlay's background opacity.</summary>
        internal static Brush GlassFill(byte alpha) => new LinearGradientBrush(new GradientStopCollection
        {
            new GradientStop(Color.FromArgb(alpha, 30, 40, 56), 0),
            new GradientStop(Color.FromArgb(alpha, 20, 28, 40), 0.25),
            new GradientStop(Color.FromArgb(alpha, 10, 14, 22), 1)
        }, 90);

        /// <summary>The glass rim: bright where light catches the top edge, a faint blue at the bottom.</summary>
        internal static Brush GlassRim() => new LinearGradientBrush(new GradientStopCollection
        {
            new GradientStop(Color.FromArgb(110, 255, 255, 255), 0),
            new GradientStop(Color.FromArgb(22, 255, 255, 255), 0.35),
            new GradientStop(Color.FromArgb(14, 255, 255, 255), 0.8),
            new GradientStop(Color.FromArgb(40, 120, 200, 255), 1)
        }, 90);

        // ------------------------------------------------------------------ public entry

        public void Update(StandingsSnapshot snapshot, OverlayConfig config)
        {
            _config = config ?? new OverlayConfig();
            _scale.ScaleX = _scale.ScaleY = _config.Scale;
            // Brushes are only replaced when something actually changes, so unchanged
            // rows keep their cached image and nothing is redrawn needlessly.
            byte alpha = (byte)(_config.Opacity * 2.55);
            if (alpha != _alpha) { _alpha = alpha; Background = GlassFill(alpha); }
            _columns = Columns().ToList();
            _width = _columns.Sum(c => c.Width) + Pad * 2;
            _canvas.Width = _width;
            // Rows are built for a set of columns; when the columns change, rebuild them all.
            string signature = _config.Type + "|" + _config.Scale + "|" + string.Join(",", _columns.Select(c => c.Key + c.Width + (c.ShowLabel ? "h" : ""))) + _config.Bool("header") + _config.Bool("classHeaders");
            if (signature != _signature)
            {
                _signature = signature;
                _canvas.Children.Clear();
                _items.Clear();
            }

            var layout = BuildLayout(snapshot ?? StandingsSnapshot.Empty);
            var seen = new HashSet<string>();
            double y = Pad;
            foreach (var item in layout)
            {
                seen.Add(item.Key);
                bool isNew = !_items.TryGetValue(item.Key, out FrameworkElement element);
                if (isNew)
                {
                    element = item.Create();
                    element.Opacity = 0;
                    Canvas.SetLeft(element, Pad);
                    Canvas.SetTop(element, y);
                    _canvas.Children.Add(element);
                    _items[item.Key] = element;
                    element.BeginAnimation(OpacityProperty, new DoubleAnimation(item.Opacity, FadeIn));
                }
                else
                {
                    item.Refresh(element);
                    double from = Canvas.GetTop(element);
                    if (Math.Abs(from - y) > 0.5)
                        element.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(from, y, Slide) { EasingFunction = Ease, FillBehavior = FillBehavior.HoldEnd });
                    if (Math.Abs(element.Opacity - item.Opacity) > 0.01 && element.Tag as string != "leaving")
                        element.BeginAnimation(OpacityProperty, new DoubleAnimation(item.Opacity, FadeIn));
                }
                Canvas.SetTop(element, y);
                element.Tag = null;
                y += item.Height;
            }

            foreach (string key in _items.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                FrameworkElement gone = _items[key];
                _items.Remove(key);
                gone.Tag = "leaving";
                var fade = new DoubleAnimation(0, FadeOut);
                fade.Completed += (s, e) => _canvas.Children.Remove(gone);
                gone.BeginAnimation(OpacityProperty, fade);
            }

            double height = y + Pad;
            if (double.IsNaN(_canvas.Height) || Math.Abs(_canvas.Height - height) > 0.5)
                _canvas.BeginAnimation(HeightProperty, new DoubleAnimation(double.IsNaN(_canvas.Height) ? height : _canvas.Height, height, Slide) { EasingFunction = Ease });
            _canvas.Height = height;
        }

        // ------------------------------------------------------------------ choosing rows

        private sealed class Item
        {
            public string Key;
            public double Height;
            public double Opacity = 1;
            public Func<FrameworkElement> Create;
            public Action<FrameworkElement> Refresh;
        }

        /// <summary>How a car in the relative relates to the player.</summary>
        private sealed class Relation
        {
            /// <summary>Seconds along the track; positive ahead.</summary>
            public double Seconds;
            /// <summary>Laps ahead of the player overall (race only): positive is lapping you, negative is being lapped.</summary>
            public int LapsUp;
        }

        private List<Item> BuildLayout(StandingsSnapshot snapshot)
        {
            var items = new List<Item>();
            if (_config.Bool("header")) items.Add(new Item { Key = "header", Height = HeaderH, Create = () => Header(snapshot), Refresh = e => RefreshHeader((Grid)e, snapshot) });
            if (_columns.Any(c => c.ShowLabel)) items.Add(new Item { Key = "labels", Height = LabelsH, Create = Labels, Refresh = e => { } });
            if (!snapshot.Connected || snapshot.Classes.Count == 0)
            {
                items.Add(new Item { Key = "waiting", Height = 44, Create = () => Waiting("Waiting for iRacing…"), Refresh = e => { } });
                return items;
            }
            if (IsRelative) AddRelativeRows(snapshot, items);
            else AddStandingsRows(snapshot, items);
            return items;
        }

        private void AddStandingsRows(StandingsSnapshot snapshot, List<Item> items)
        {
            StandingsClass playerClass = snapshot.PlayerClass ?? snapshot.Classes[0];
            int top = Math.Max(0, _config.Int("driversAtTop")), around = Math.Max(0, _config.Int("aheadBehind")), others = Math.Max(0, _config.Int("otherClassTop"));
            foreach (StandingsClass group in snapshot.Classes)
            {
                var show = new SortedSet<int>();
                if (group == playerClass)
                {
                    int me = group.Entries.FindIndex(e => e.IsPlayer);
                    for (int i = 0; i < Math.Min(top, group.Entries.Count); i++) show.Add(i);
                    if (me >= 0) for (int i = Math.Max(0, me - around); i <= Math.Min(group.Entries.Count - 1, me + around); i++) show.Add(i);
                    else for (int i = 0; i < Math.Min(group.Entries.Count, top + around * 2 + 1); i++) show.Add(i);
                }
                else for (int i = 0; i < Math.Min(others, group.Entries.Count); i++) show.Add(i);
                if (show.Count == 0) continue;

                if (_config.Bool("classHeaders") && snapshot.Classes.Count > 0)
                {
                    StandingsClass g = group;
                    items.Add(new Item { Key = "class-" + g.Id, Height = ClassH, Create = () => ClassHeader(g), Refresh = e => RefreshClassHeader((Grid)e, g) });
                }
                int previous = -1, row = 0;
                foreach (int index in show)
                {
                    if (previous >= 0 && index != previous + 1)
                        items.Add(new Item { Key = "gap-" + group.Id + "-" + index, Height = SpacerH, Create = Spacer, Refresh = e => { } });
                    items.Add(CarRow(group.Entries[index], snapshot, row++ % 2 == 1, null));
                    previous = index;
                }
            }
        }

        /// <summary>
        /// The relative: cars nearest to the player by track position, ahead at the top.
        /// Distance round the lap becomes seconds using the class's lap time.
        /// </summary>
        private void AddRelativeRows(StandingsSnapshot snapshot, List<Item> items)
        {
            var cars = snapshot.Classes.SelectMany(c => c.Entries).ToList();
            StandingsEntry me = cars.FirstOrDefault(e => e.IsPlayer);
            if (me == null || me.TrackPct < 0)
            {
                items.Add(new Item { Key = "waiting", Height = 44, Create = () => Waiting("Waiting for your car on track…"), Refresh = e => { } });
                return;
            }
            double lap = snapshot.PlayerLapTime > 0 ? snapshot.PlayerLapTime : me.BestLap > 0 ? me.BestLap : me.LastLap > 0 ? me.LastLap : 100;
            var near = cars.Where(e => e != me && e.TrackPct >= 0).Select(e =>
            {
                double d = e.TrackPct - me.TrackPct;
                if (d > 0.5) d -= 1;
                if (d <= -0.5) d += 1;
                return new { Entry = e, Distance = d };
            }).ToList();
            int ahead = Math.Max(0, _config.Int("carsAhead")), behind = Math.Max(0, _config.Int("carsBehind"));
            var rows = near.Where(n => n.Distance > 0).OrderBy(n => n.Distance).Take(ahead).OrderByDescending(n => n.Distance).ToList();
            rows.Add(new { Entry = me, Distance = 0.0 });
            rows.AddRange(near.Where(n => n.Distance <= 0).OrderByDescending(n => n.Distance).Take(behind));
            int row = 0;
            foreach (var n in rows)
            {
                var relation = new Relation
                {
                    Seconds = n.Distance * lap,
                    LapsUp = snapshot.IsRace && n.Entry != me ? (int)Math.Round(n.Entry.Progress - me.Progress - n.Distance) : 0
                };
                items.Add(CarRow(n.Entry, snapshot, row++ % 2 == 1, relation));
            }
        }

        private Item CarRow(StandingsEntry entry, StandingsSnapshot snapshot, bool odd, Relation relation) => new Item
        {
            Key = "car-" + entry.CarIdx, Height = RowH, Opacity = entry.InWorld ? 1 : 0.42,
            Create = () => { var r = new RowView(this); r.Set(entry, snapshot, odd, relation); return r; },
            Refresh = e => ((RowView)e).Set(entry, snapshot, odd, relation)
        };

        // ------------------------------------------------------------------ columns

        private sealed class Column
        {
            public string Key, Label;
            public double Width;
            public TextAlignment Align = TextAlignment.Left;
            /// <summary>Show this column's label in the header row.</summary>
            public bool ShowLabel;
        }

        private Column Col(string key, string label, double width, TextAlignment align, string headerKey) =>
            new Column { Key = key, Label = label, Width = width, Align = align, ShowLabel = headerKey != null && _config.Bool(headerKey) };

        /// <summary>The visible columns in the order arranged in the settings; the class stripe sits just before the name.</summary>
        private IEnumerable<Column> Columns()
        {
            OverlayType type = OverlayTypes.Find(_config.Type) ?? OverlayTypes.Find("standings");
            foreach (OverlayOption option in type.OrderedColumns(_config))
            {
                if (!option.Fixed && !_config.Bool(option.Key)) continue;
                Column column = Make(option.Key);
                if (column == null) continue;
                if (column.Key == "name" && !(IsRelative && _config.Bool("colNumber"))) yield return Col("stripe", "", 12, TextAlignment.Left, null);
                yield return column;
            }
        }

        private Column Make(string optionKey)
        {
            const TextAlignment L = TextAlignment.Left, C = TextAlignment.Center, R = TextAlignment.Right;
            switch (optionKey)
            {
                case "fixed-hdrPos": case "colPosition": return Col("pos", "POS", 38, C, "hdrPos");
                case "colNumber": return Col("number", "#", 44, C, "hdrNumber");
                case "fixed-hdrName": return Col("name", "DRIVER", Math.Max(110, _config.Int("nameWidth")), L, "hdrName");
                case "colLicense": return Col("license", "LIC / iR", 62, L, "hdrLicense");
                case "colMake": return Col("make", "CAR", 44, L, "hdrMake");
                case "colGained": return Col("gained", "+/-", 40, C, "hdrGained");
                case "colGap": return IsRelative ? Col("relative", "GAP", 58, R, "hdrGap") : Col("gap", "GAP", 62, R, "hdrGap");
                case "colInterval": return Col("interval", "INT", 58, R, "hdrInterval");
                case "colBest": return Col("best", "BEST", 86, R, "hdrBest");
                case "colLast": return Col("last", "LAST", 86, R, "hdrLast");
                case "colStatus": return Col("status", "INFO", 40, C, "hdrStatus");
                default: return null;
            }
        }

        /// <summary>Text margins per column, shared by the rows and the header labels so they line up.</summary>
        private static Thickness CellMargin(string key)
        {
            switch (key)
            {
                case "name": return new Thickness(2, 0, 6, 0);
                case "gap": case "interval": case "best": case "last": case "relative": return new Thickness(0, 0, 8, 0);
                case "license": case "make": return new Thickness(0, 0, 4, 0);
                default: return new Thickness(0);
            }
        }

        // ------------------------------------------------------------------ pieces

        private static TextBlock Text(string text, double size, FontWeight weight, Brush brush, FontFamily family = null, TextAlignment align = TextAlignment.Left) =>
            new TextBlock
            {
                Text = text, FontSize = size, FontWeight = weight, Foreground = brush, FontFamily = family ?? Ui, TextAlignment = align,
                VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
            };

        private static SolidColorBrush Brush(DColor c, byte alpha = 255) => Brush(Color.FromArgb(alpha, c.R, c.G, c.B));

        private static readonly Dictionary<Color, SolidColorBrush> BrushCache = new Dictionary<Color, SolidColorBrush>();

        /// <summary>One shared, frozen brush per color: assigning the same brush again changes nothing on screen.</summary>
        private static SolidColorBrush Brush(Color color)
        {
            if (!BrushCache.TryGetValue(color, out SolidColorBrush brush))
                BrushCache[color] = brush = Frozen(new SolidColorBrush(color));
            return brush;
        }

        private static T Frozen<T>(T freezable) where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }

        private static readonly Brush PlayerRow = Frozen(new LinearGradientBrush(Color.FromArgb(235, 168, 132, 42), Color.FromArgb(200, 112, 88, 30), 0));
        private static readonly Brush OddRow = Brush(Color.FromArgb(14, 255, 255, 255));
        private static readonly Brush FastestCell = Brush(Color.FromArgb(70, 0, 0, 0));
        private static readonly Brush Clear = Brush(Colors.Transparent);
        private static readonly FontFamily Icons = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        private static readonly Brush White = Brushes.White;
        private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(150, 160, 175));
        private static readonly Brush Purple = new SolidColorBrush(Color.FromRgb(196, 120, 255));
        private static readonly Brush Green = new SolidColorBrush(Color.FromRgb(70, 225, 130));
        private static readonly Brush Red = new SolidColorBrush(Color.FromRgb(255, 92, 106));
        private static readonly Brush Lapping = new SolidColorBrush(Color.FromRgb(255, 128, 110));
        private static readonly Brush Lapped = new SolidColorBrush(Color.FromRgb(110, 178, 255));
        private static readonly Brush Dark = new SolidColorBrush(Color.FromRgb(16, 18, 24));

        private FrameworkElement Waiting(string message)
        {
            var grid = new Grid { Width = _width - Pad * 2, Height = 44 };
            grid.Children.Add(Text(message, 13, FontWeights.Normal, Muted, null, TextAlignment.Center));
            ((TextBlock)grid.Children[0]).HorizontalAlignment = HorizontalAlignment.Center;
            return grid;
        }

        private FrameworkElement Spacer()
        {
            var grid = new Grid { Width = _width - Pad * 2, Height = SpacerH };
            grid.Children.Add(new Border { Height = 1, Margin = new Thickness(40, 0, 40, 0), Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)), VerticalAlignment = VerticalAlignment.Center });
            return grid;
        }

        /// <summary>The column header row: small caps labels over the columns that have their header switched on.</summary>
        private FrameworkElement Labels()
        {
            var grid = new Grid { Width = _width - Pad * 2, Height = LabelsH };
            foreach (Column c in _columns) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(c.Width) });
            for (int i = 0; i < _columns.Count; i++)
            {
                Column c = _columns[i];
                if (!c.ShowLabel || c.Label.Length == 0) continue;
                TextBlock label = Text(c.Label, 10, FontWeights.SemiBold, Muted, Digits, c.Align);
                label.Margin = CellMargin(c.Key);
                Grid.SetColumn(label, i);
                grid.Children.Add(label);
            }
            var rule = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(4, 0, 4, 2), Background = new LinearGradientBrush(Color.FromArgb(40, 255, 255, 255), Color.FromArgb(6, 255, 255, 255), 0) };
            Grid.SetColumnSpan(rule, Math.Max(1, _columns.Count));
            grid.Children.Add(rule);
            return grid;
        }

        private Grid Header(StandingsSnapshot s)
        {
            var grid = new Grid { Width = _width - Pad * 2, Height = HeaderH };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var badge = new Border
            {
                Width = 24, Height = 24, CornerRadius = new CornerRadius(12), BorderBrush = Muted, BorderThickness = new Thickness(1.5), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(4, 0, 0, 0),
                Child = Text("", 12, FontWeights.Bold, White, Digits, TextAlignment.Center)
            };
            grid.Children.Add(badge);
            TextBlock clock = Text("", 17, FontWeights.SemiBold, White, Digits);
            clock.Margin = new Thickness(4, 0, 0, 0);
            Grid.SetColumn(clock, 1);
            grid.Children.Add(clock);
            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            right.Children.Add(Text(IsRelative ? "RELATIVE" : "", 11, FontWeights.SemiBold, Muted, Digits));
            right.Children.Add(new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 14, Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 6, 0) });
            right.Children.Add(Text("", 16, FontWeights.SemiBold, White, Digits));
            Grid.SetColumn(right, 2);
            grid.Children.Add(right);
            RefreshHeader(grid, s);
            return grid;
        }

        private static void RefreshHeader(Grid grid, StandingsSnapshot s)
        {
            ((TextBlock)((Border)grid.Children[0]).Child).Text = s.SessionLetter;
            string Clock(double seconds) { var t = TimeSpan.FromSeconds(Math.Max(0, seconds)); return ((int)t.TotalHours).ToString("00") + t.ToString(@"\:mm\:ss"); }
            string text = s.TotalLaps > 0 ? "LAP " + Math.Max(1, s.LeaderLap) + " / " + s.TotalLaps
                : s.SessionTotal > 0 ? Clock(s.SessionTotal - s.SessionRemaining) + " / " + Clock(s.SessionTotal)
                : Clock(s.SessionElapsed);
            ((TextBlock)grid.Children[1]).Text = s.Connected ? text : "--:--:--";
            ((TextBlock)((StackPanel)grid.Children[2]).Children[2]).Text = s.CarCount > 0 ? s.CarCount.ToString() : "-";
        }

        private Grid ClassHeader(StandingsClass g)
        {
            var grid = new Grid { Width = _width - Pad * 2, Height = ClassH };
            var badge = new StackPanel { Orientation = Orientation.Horizontal, Height = 24, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) };
            var count = new Border
            {
                Padding = new Thickness(10, 0, 12, 0), CornerRadius = new CornerRadius(5, 0, 0, 5), Background = new SolidColorBrush(Color.FromArgb(230, 14, 16, 20)),
                BorderThickness = new Thickness(1.5, 1.5, 0, 1.5),
                Child = Text("", 13, FontWeights.Bold, White, Digits)
            };
            var name = new Border
            {
                Padding = new Thickness(14, 0, 16, 0), CornerRadius = new CornerRadius(0, 5, 5, 0), RenderTransform = new SkewTransform(-14, 0), Margin = new Thickness(-4, 0, 0, 0),
                Child = Text("", 13, FontWeights.Bold, Dark, Ui)
            };
            badge.Children.Add(count);
            badge.Children.Add(name);
            grid.Children.Add(badge);
            TextBlock sof = Text("", 13, FontWeights.Bold, White, Digits, TextAlignment.Right);
            sof.HorizontalAlignment = HorizontalAlignment.Right;
            sof.Margin = new Thickness(0, 0, 8, 0);
            grid.Children.Add(sof);
            grid.Children.Add(new Border { Height = 2, VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(1) });
            RefreshClassHeader(grid, g);
            return grid;
        }

        private static void RefreshClassHeader(Grid grid, StandingsClass g)
        {
            var badge = (StackPanel)grid.Children[0];
            var count = (Border)badge.Children[0];
            var name = (Border)badge.Children[1];
            ((TextBlock)count.Child).Text = g.Entries.Count + (g.Entries.Count == 1 ? " CAR" : " CARS");
            ((TextBlock)name.Child).Text = g.Name.ToUpperInvariant();
            var sof = (TextBlock)grid.Children[1];
            sof.Text = g.StrengthOfField > 0 ? "SOF " + (g.StrengthOfField >= 1000 ? (g.StrengthOfField / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + "k" : g.StrengthOfField.ToString()) : "";
            // The class colour rarely changes; repaint only when it does.
            int argb = g.Color.ToArgb();
            if (grid.Tag is int painted && painted == argb) return;
            grid.Tag = argb;
            count.BorderBrush = Brush(g.Color);
            ((TextBlock)count.Child).Foreground = Brush(g.Color);
            name.Background = Frozen(new LinearGradientBrush(Color.FromRgb(g.Color.R, g.Color.G, g.Color.B), Color.FromArgb(255, (byte)(g.Color.R * 0.75), (byte)(g.Color.G * 0.75), (byte)(g.Color.B * 0.75)), 0));
            sof.Foreground = Brush(g.Color);
            ((Border)grid.Children[2]).Background = Frozen(new LinearGradientBrush(Color.FromArgb(220, g.Color.R, g.Color.G, g.Color.B), Color.FromArgb(0, g.Color.R, g.Color.G, g.Color.B), 0));
        }

        // ------------------------------------------------------------------ one driver row

        private sealed class RowView : Grid
        {
            private readonly StandingsView _owner;
            private readonly Border _back, _posCell, _stripe, _plate, _licBand, _licChip, _makeChip, _statusChip, _bestCell;
            private readonly TextBlock _pos, _number, _name, _lic, _rating, _make, _gained, _gap, _interval, _relative, _best, _last, _status;

            public RowView(StandingsView owner)
            {
                _owner = owner;
                Width = owner._width - Pad * 2;
                Height = RowH;
                // Each row is drawn once into a GPU texture; sliding it just moves the texture.
                // It's redrawn only when something in the row changes. Rendered at the overlay's
                // size and the screen's DPI so text stays sharp.
                CacheMode = new BitmapCache { RenderAtScale = owner._config.Scale * VisualTreeHelper.GetDpi(owner).DpiScaleX, SnapsToDevicePixels = true };
                List<Column> columns = owner._columns;
                foreach (Column c in columns) ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(c.Width) });
                int Col(string key) => columns.FindIndex(c => c.Key == key);
                TextBlock Cell(string key, double size, FontWeight weight, Brush brush, TextAlignment align)
                {
                    if (Col(key) < 0) return null;
                    TextBlock text = Text("", size, weight, brush, Digits, align);
                    text.Margin = CellMargin(key);
                    Place(text, Col(key));
                    return text;
                }

                _back = new Border { CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 1, 0, 1) };
                SetColumnSpan(_back, columns.Count);
                Children.Add(_back);

                if (Col("pos") >= 0)
                {
                    _pos = Text("", 14, FontWeights.Bold, White, Digits, TextAlignment.Center);
                    _posCell = new Border { CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 1, 0, 1), Child = _pos, Background = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)) };
                    Place(_posCell, Col("pos"));
                }
                if (Col("stripe") >= 0)
                {
                    _stripe = new Border { Width = 4, Margin = new Thickness(4, 5, 4, 5), CornerRadius = new CornerRadius(1.5), RenderTransform = new SkewTransform(-14, 0) };
                    Place(_stripe, Col("stripe"));
                }
                if (Col("number") >= 0)
                {
                    _number = Text("", 12, FontWeights.Bold, Dark, Digits, TextAlignment.Center);
                    _plate = new Border { Height = 20, Width = 34, CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = _number };
                    Place(_plate, Col("number"));
                }

                _name = Text("", 14, FontWeights.SemiBold, White);
                _name.Margin = CellMargin("name");
                Place(_name, Col("name"));

                if (Col("license") >= 0)
                {
                    _lic = Text("", 10, FontWeights.Bold, White, Digits, TextAlignment.Center);
                    _rating = Text("", 11.5, FontWeights.Bold, Dark, Digits, TextAlignment.Center);
                    _licBand = new Border { Width = 15, CornerRadius = new CornerRadius(3, 0, 0, 3), Child = _lic };
                    _licChip = new Border { CornerRadius = new CornerRadius(0, 3, 3, 0), Background = new SolidColorBrush(Color.FromArgb(235, 240, 243, 247)), Padding = new Thickness(4, 0, 4, 0), Child = _rating };
                    var chip = new StackPanel { Orientation = Orientation.Horizontal, Height = 19, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
                    chip.Children.Add(_licBand);
                    chip.Children.Add(_licChip);
                    Place(chip, Col("license"));
                }
                if (Col("make") >= 0)
                {
                    _make = Text("", 9.5, FontWeights.Bold, Muted, Digits, TextAlignment.Center);
                    _makeChip = new Border
                    {
                        Height = 18, Width = 36, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
                        BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), Child = _make
                    };
                    Place(_makeChip, Col("make"));
                }
                _gained = Cell("gained", 12, FontWeights.Bold, Muted, TextAlignment.Center);
                _gap = Cell("gap", 14, FontWeights.SemiBold, White, TextAlignment.Right);
                _interval = Cell("interval", 13, FontWeights.Normal, Muted, TextAlignment.Right);
                _relative = Cell("relative", 14, FontWeights.SemiBold, White, TextAlignment.Right);
                if (Col("best") >= 0)
                {
                    _best = Text("", 14, FontWeights.SemiBold, White, Digits, TextAlignment.Right);
                    _best.Margin = CellMargin("best");
                    _bestCell = new Border { Margin = new Thickness(2, 1, 2, 1), CornerRadius = new CornerRadius(4), Child = _best };
                    Place(_bestCell, Col("best"));
                }
                _last = Cell("last", 14, FontWeights.SemiBold, White, TextAlignment.Right);
                if (Col("status") >= 0)
                {
                    _status = Text("", 10, FontWeights.Bold, White, Digits, TextAlignment.Center);
                    _statusChip = new Border { Height = 20, MinWidth = 26, CornerRadius = new CornerRadius(5), Padding = new Thickness(3, 0, 3, 0), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = _status };
                    Place(_statusChip, Col("status"));
                }
            }

            private void Place(UIElement element, int column)
            {
                if (column < 0) return;
                SetColumn(element, column);
                Children.Add(element);
            }

            public void Set(StandingsEntry e, StandingsSnapshot s, bool odd, Relation relation)
            {
                // Flash the position when it changes.
                if (_posCell != null && _owner._lastPlace.TryGetValue(e.CarIdx, out int before) && before != e.ClassPosition && before > 0)
                {
                    Color flash = e.ClassPosition < before ? Color.FromArgb(200, 46, 210, 120) : Color.FromArgb(200, 240, 70, 85);
                    var brush = new SolidColorBrush(flash);
                    _posCell.Background = brush;
                    brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(flash, Color.FromArgb(28, 255, 255, 255), new Duration(TimeSpan.FromMilliseconds(1600))) { BeginTime = TimeSpan.FromMilliseconds(300) });
                }
                _owner._lastPlace[e.CarIdx] = e.ClassPosition;

                _back.Background = e.IsPlayer ? PlayerRow : odd ? OddRow : Clear;
                if (_pos != null) _pos.Text = e.ClassPosition > 0 ? e.ClassPosition.ToString() : "-";
                if (_stripe != null) _stripe.Background = Brush(e.ClassColor);
                if (_plate != null)
                {
                    _plate.Background = Brush(e.ClassColor);
                    _number.Text = string.IsNullOrEmpty(e.CarNumber) ? "-" : e.CarNumber;
                }
                _name.Text = FormatName(e.Name, _owner._config.Get("nameStyle"));
                // In the relative, cars a lap or more up on you are red and cars you're lapping are blue.
                _name.Foreground = relation == null || e.IsPlayer || relation.LapsUp == 0 ? White : relation.LapsUp > 0 ? Lapping : Lapped;
                if (_rating != null)
                {
                    _licBand.Background = Brush(e.LicenseColor);
                    _lic.Text = e.License;
                    _rating.Text = e.IRating >= 1000 ? (e.IRating / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + "k" : e.IRating > 0 ? e.IRating.ToString() : "-";
                }
                if (_make != null) _make.Text = e.Make;
                if (_gained != null)
                {
                    int gained = e.PositionsGained;
                    _gained.Text = gained > 0 ? "▲" + gained : gained < 0 ? "▼" + -gained : "";
                    _gained.Foreground = gained > 0 ? Green : Red;
                }
                bool leader = e.ClassPosition == 1;
                if (_gap != null) _gap.Text = leader ? "-" : e.LapsDown > 0 ? e.LapsDown + "L" : e.Gap < 0 ? "" : e.Gap >= 100 ? e.Gap.ToString("0", CultureInfo.InvariantCulture) : e.Gap.ToString("0.0", CultureInfo.InvariantCulture);
                if (_interval != null) _interval.Text = leader || e.Interval < 0 ? "" : "+" + e.Interval.ToString("0.0", CultureInfo.InvariantCulture);
                if (_relative != null)
                {
                    _relative.Text = e.IsPlayer || relation == null ? "" : Math.Abs(relation.Seconds).ToString("0.0", CultureInfo.InvariantCulture);
                    _relative.Foreground = _name.Foreground;
                }
                if (_best != null)
                {
                    _best.Text = Fmt.LapTime(e.BestLap).Replace("--:--.---", "");
                    _best.Foreground = e.IsClassFastest ? Purple : White;
                    _bestCell.Background = e.IsClassFastest ? FastestCell : Clear;
                }
                if (_last != null)
                {
                    _last.Text = Fmt.LapTime(e.LastLap).Replace("--:--.---", "");
                    _last.Foreground = e.LastIsClassBest ? Purple : e.LastIsPersonalBest ? Green : White;
                }
                if (_statusChip != null)
                {
                    if (e.InPits) Chip("PIT", Color.FromRgb(255, 190, 50), true);
                    else if (e.OutLap) Chip("OUT", Color.FromRgb(76, 158, 255), true);
                    else if (e.IsClassFastest && relation == null) Chip("", Color.FromRgb(160, 80, 255), false);
                    else { _statusChip.Background = Brushes.Transparent; _status.Text = ""; }
                }
            }

            private void Chip(string text, Color color, bool label)
            {
                _status.Text = text;
                _status.FontFamily = label ? Digits : Icons;
                _status.FontSize = label ? 10 : 12;
                _status.Foreground = label ? Dark : Brushes.White;
                _statusChip.Background = Brush(color);
            }
        }

        private static string FormatName(string name, string style)
        {
            name = (name ?? "").Trim();
            string[] parts = name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return name;
            switch (style)
            {
                case "First initial": return parts[0][0] + ". " + string.Join(" ", parts.Skip(1));
                case "Last name": return parts[parts.Length - 1];
                default: return name;
            }
        }
    }
}
