using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace AlmostFastDash.Designer
{
    /// <summary>
    /// The formula reference: everything a ƒx formula can use, in a window of its own to keep open beside the designer
    /// (or on another display) while building. It's made from the same lists the formulas run on, every data field
    /// and every function, so it's complete and can't go out of date. Values are live from the designer's preview, and
    /// each entry can be copied or inserted into the formula being edited.
    /// </summary>
    internal sealed class FormulaReferenceWindow : Window
    {
        /// <summary>A worked example for every function (the self-test checks each one has one, and that it works).</summary>
        internal static readonly Dictionary<string, string> Examples = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "if", "if(OnPitRoad, \"PIT\", \"RACE\")" },
            { "format", "format(Speed, \"0\")" },
            { "laptime", "laptime(BestLap)" },
            { "clock", "clock(TimeRemaining)" },
            { "signed", "signed(Delta, 2)" },
            { "fuel", "fuel(Fuel)" },
            { "gear", "gear(Gear)" },
            { "time", "time(\"HH:mm\")" },
            { "round", "round(FuelPerLap, 2)" },
            { "floor", "floor(Speed / 10) * 10" },
            { "ceil", "ceil(FuelToFinish)" },
            { "abs", "abs(Delta)" },
            { "min", "min(FuelLapsLeft, LapsRemaining)" },
            { "max", "max(0, FuelToFinish)" },
            { "clamp", "clamp(Delta, -1, 1)" },
            { "norm", "norm(Rpm, 3000, Redline)" },
            { "nz", "nz(BestLap, LastLap)" },
            { "blink", "blink(250)" },
            { "upper", "upper(CarName)" },
            { "lower", "lower(SessionType)" },
            { "left", "left(AheadName, 8)" },
            { "pad", "pad(Position, 2)" },
            { "rgb", "rgb(255, 64, 48)" },
            { "rgba", "rgba(255, 64, 48, 0.5)" },
            { "alpha", "alpha($accent, 0.3)" },
            { "mix", "mix($good, $bad, ShiftPct)" },
            { "steps", "steps(FuelLapsLeft, $bad, 2, $warn, 5, $text)" },
            { "deltacolor", "deltacolor(Delta)" },
            { "temp", "temp(100)" },
            { "seconds", "seconds(LastSectorTime)" },
            { "standing", "standing(1, \"Name\")" },
            { "classstanding", "classstanding(1, \"GapText\")" },
            { "sectortime", "sectortime(1)" },
            { "lastsectortime", "lastsectortime(1)" },
            { "bestsectortime", "bestsectortime(1)" },
            { "tap", "tap(\"fuel\") == 1" },
            { "option", "option(\"Redline\") * 1000" },
            { "tire", "tire(\"LF\", \"Temp\")" },
            { "tyre", "tyre(\"LF\", \"Temp\")" }
        };

        /// <summary>The functions in groups, as the reference lists them. Any function not named here goes under "More".</summary>
        private static readonly (string Group, string[] Names)[] FunctionGroups =
        {
            ("Logic and numbers", new[] { "if", "round", "floor", "ceil", "abs", "min", "max", "clamp", "norm", "nz", "blink" }),
            ("Formatting", new[] { "format", "laptime", "clock", "seconds", "signed", "fuel", "gear", "temp", "time", "upper", "lower", "left", "pad" }),
            ("Color", new[] { "rgb", "rgba", "alpha", "mix", "steps", "deltacolor" }),
            ("Race data", new[] { "standing", "classstanding", "sectortime", "lastsectortime", "bestsectortime", "tire", "tyre", "tap" })
        };

        private static FormulaReferenceWindow _open;

        /// <summary>Opens the reference beside the designer, or brings it forward when it's already open.</summary>
        public static void Open(Window owner, Func<EvalScope> scope)
        {
            if (_open != null)
            {
                _open._scope = scope;
                if (_open.WindowState == WindowState.Minimized) _open.WindowState = WindowState.Normal;
                _open.Activate();
                return;
            }
            _open = new FormulaReferenceWindow(scope) { Owner = owner };
            _open.Closed += (s, e) => _open = null;
            _open.Show();
        }

        /// <summary>A reference on its own (no designer), for the UI snapshots; optionally already searched.</summary>
        internal static FormulaReferenceWindow Create(Func<EvalScope> scope, string search = null)
        {
            var window = new FormulaReferenceWindow(scope);
            if (!string.IsNullOrEmpty(search)) window._search.Text = search;
            return window;
        }

        private sealed class Entry
        {
            public FrameworkElement View;
            public string Search;
        }

        private sealed class Section
        {
            public string Title;
            public FrameworkElement Header;
            public readonly List<FrameworkElement> Prose = new List<FrameworkElement>();
            public readonly List<Entry> Entries = new List<Entry>();
            public Button Nav;
        }

        private Func<EvalScope> _scope;
        private readonly TextBox _search = new TextBox();
        private readonly StackPanel _nav = new StackPanel { Margin = new Thickness(12, 8, 12, 12) };
        private readonly StackPanel _content = new StackPanel { Margin = new Thickness(28, 20, 28, 40), MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Left };
        private readonly ScrollViewer _scroller;
        private readonly TextBlock _status = new TextBlock();
        private readonly List<Section> _sections = new List<Section>();
        private readonly List<(TextBlock Text, Func<EvalScope, string> Value)> _live = new List<(TextBlock, Func<EvalScope, string>)>();
        private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        private Section _current;

        private FormulaReferenceWindow(Func<EvalScope> scope)
        {
            _scope = scope;
            Title = "Formula reference · Almost Fast Dash";
            Width = 1060;
            Height = 780;
            MinWidth = 720;
            MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Res<Brush>("Brush.Bg");
            Foreground = Res<Brush>("Brush.Text");
            ShowInTaskbar = true;

            // Left: search and the sections. Right: the reference itself.
            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
            root.ColumnDefinitions.Add(new ColumnDefinition());
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var side = new DockPanel { Background = Res<Brush>("Brush.Sidebar") };
            var heading = new StackPanel { Margin = new Thickness(16, 18, 16, 6) };
            heading.Children.Add(new TextBlock { Text = "Formula reference", Style = Res<Style>("T.Title") });
            heading.Children.Add(new TextBlock { Text = "Everything ƒx can use. Keep it open while you build.", Style = Res<Style>("T.Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 10) });
            _search.ToolTip = "Search every field, function and example";
            _search.TextChanged += (s, e) => Filter();
            // What to search for, until something's typed (Ctrl+F comes back here).
            var hint = new TextBlock
            {
                Text = "Search: fuel, gap, color…  (Ctrl+F)", IsHitTestVisible = false, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("Brush.TextTertiary")
            };
            _search.TextChanged += (s, e) => hint.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            var searchHost = new Grid { Margin = _search.Margin };
            _search.Margin = new Thickness(0);
            searchHost.Children.Add(_search);
            searchHost.Children.Add(hint);
            heading.Children.Add(searchHost);
            DockPanel.SetDock(heading, Dock.Top);
            side.Children.Add(heading);
            side.Children.Add(new ScrollViewer { Content = _nav, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            Grid.SetRowSpan(side, 2);
            root.Children.Add(side);

            _scroller = new ScrollViewer { Content = _content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Grid.SetColumn(_scroller, 1);
            root.Children.Add(_scroller);

            var footer = new Border { BorderBrush = Res<Brush>("Brush.Border"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(28, 8, 28, 8), Child = _status };
            _status.Style = Res<Style>("T.Caption");
            _status.Text = "Insert puts an entry into the formula you last clicked in the designer. Copy puts it on the clipboard.";
            Grid.SetColumn(footer, 1);
            Grid.SetRow(footer, 1);
            root.Children.Add(footer);
            Content = root;

            Build();
            Filter();
            _timer.Tick += (s, e) => RefreshLive();
            _timer.Start();
            Closed += (s, e) => _timer.Stop();
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { _search.Focus(); _search.SelectAll(); e.Handled = true; }
                else if (e.Key == Key.Escape && _search.Text.Length > 0) { _search.Text = ""; e.Handled = true; }
            };
            Loaded += (s, e) => _search.Focus();
        }

        private T Res<T>(string key) where T : class => (T)FindResource(key);

        // ------------------------------------------------------------------ content

        private void Build()
        {
            NewSection("Start here");
            Prose("A formula makes a widget live. Click ƒx next to any property in the inspector and type a formula instead of a fixed value: " +
                  "a text that shows the speed, a color that turns red when fuel is low, a bar that fills with the revs.");
            Prose("The formula's answer has to suit the property: a number for sizes and values, text for labels, a color for colors, " +
                  "and true or false for switches. Text and numbers convert to each other where it makes sense.");
            Prose("Formulas also decide when things show:");
            Bullet("A widget's Show when shows it only while its formula is true, such as OnPitRoad.");
            Bullet("A screen's Show when makes that screen take over while it's true; the others are pages that a wheel button steps through.");
            Bullet("A group's widgets read the group's number as Slot, so one driver row becomes a whole leaderboard.");
            Prose("While you type, the line under the formula shows its answer from the preview, or what's wrong. A formula with a mistake never " +
                  "breaks the dashboard: the widget keeps its normal value until it's fixed.");

            NewSection("The language");
            Prose("Formulas read like a spreadsheet formula. Names don't care about capitals: Speed and speed are the same.");
            Item("1.5   -3   0.25", "Numbers. A dot for decimals.", null);
            Item("\"PIT\"   'PIT'", "Text, in double or single quotes. A backslash puts a quote inside: \"6\\\" screen\".", "\"P\" + Position");
            Item("true   false", "Yes and no. 0, empty text, \"0\" and \"false\" also count as false; anything else counts as true.", "OnPitRoad == false");
            Item("#FF3040   #F30   #80FF3040", "Colors: #RRGGBB, the short #RGB, or #AARRGGBB with transparency first (00 clear, FF solid).", "#FF3040");
            Item("$accent   $good   $bad ...", "Theme colors, which follow the dashboard's theme. All of them are under Colors.", "$accent");
            Item("+  -  *  /  %", "Add, subtract, multiply, divide and remainder. Dividing by 0 gives 0 rather than an error. + joins text when either side is text.", "\"Lap \" + Lap + \" of \" + TotalLaps");
            Item("==  !=  <  >  <=  >=", "Compare. Text compares without caring about capitals. A single = works like ==.", "Gear == 1");
            Item("and  or  not", "Combine conditions; && || and ! work too.", "OnPitRoad and not PitLimiter");
            Item("condition ? a : b", "Chooses a or b. Chain them for more than two choices.", "Delta < 0 ? $good : Delta > 0 ? $bad : $text");
            Item("( )", "Parentheses work first.", "(LapsRemaining + 1) * FuelPerLap");
            Prose("Order, first to last: not and minus signs, then * / %, then + -, then < > <= >=, then == !=, then and, then or, then ? :. " +
                  "Use brackets whenever it isn't obvious.");
            Prose("Common mistakes: text needs quotes (\"PIT\", not PIT); a function needs brackets even with nothing in them; " +
                  "a name that isn't a data field says Unknown data field, with the name, under the formula.");

            NewSection("Colors");
            Prose("Theme colors follow the dashboard's theme, so changing the theme recolors every formula that uses them. Each shows its color in this dashboard.");
            foreach (string token in LayoutTheme.Tokens)
            {
                string name = "$" + token;
                ColorItem(name, ThemeDescription(token));
            }
            Prose("To make colors from other colors or from numbers, see the Color functions: rgb, rgba, alpha, mix, steps and deltacolor.");

            foreach (string category in DataCatalog.Categories)
            {
                List<DataField> fields = DataCatalog.InCategory(category).ToList();
                if (fields.Count == 0) continue;
                NewSection("Data · " + category);
                if (_sections.Count(x => x.Title.StartsWith("Data · ")) == 1)
                    Prose("Live values from iRacing, already in your units (speed, fuel, temperature and pressure follow Settings). " +
                          "The value on the right is the preview's, as it plays.");
                foreach (DataField field in fields)
                {
                    DataField f = field;
                    Item(f.Name, f.Description, string.IsNullOrEmpty(f.Suggested) || f.Suggested == f.Name ? null : f.Suggested,
                        s => Show(f.Get(s)), insertName: f.Name);
                }
            }

            var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach ((string group, string[] names) in FunctionGroups)
            {
                NewSection("Functions · " + group);
                foreach (string name in names)
                {
                    FunctionInfo fn = Functions.Find(name);
                    if (fn == null) continue;
                    placed.Add(fn.Name);
                    FunctionItem(fn);
                }
            }
            List<FunctionInfo> more = Functions.All.Where(f => !placed.Contains(f.Name)).ToList();
            if (more.Count > 0)
            {
                NewSection("Functions · More");
                foreach (FunctionInfo fn in more) FunctionItem(fn);
            }

            NewSection("Leaderboard, tires and sectors");
            Prose("standing(position, \"field\") reads any car in the race order; classstanding() counts only your class. The field is one of:");
            foreach (string field in RaceFields.StandingFields)
            {
                string example = "standing(1, \"" + field + "\")";
                Item("\"" + field + "\"", StandingDescription(field), example, s => Show(Expr.Evaluate(example, s)), insertName: "\"" + field + "\"");
            }
            Prose("tire(\"corner\", \"reading\") reads a tire (tyre() works too). Corners: LF, RF, LR, RR. iRacing updates tire readings while you're in the pits. The reading is one of:");
            foreach (string reading in RaceFields.TyreFields)
            {
                string example = "tyre(\"LF\", \"" + reading + "\")";
                Item("\"" + reading + "\"", TyreDescription(reading), example, s => Show(Expr.Evaluate(example, s)), insertName: "\"" + reading + "\"");
            }
            Prose("Sectors are numbered from 1: sectortime(1) is your time in the first sector of this lap, lastsectortime(1) on your last lap, bestsectortime(1) your best.");

            NewSection("Groups, pages and taps");
            Item("Slot", "In a group, the group's number. Build one driver row with standing(Slot, \"Name\"), group it (Ctrl+G), then Ctrl+D copies it for the next place.", "standing(Slot, \"Name\")");
            Item("Page   PageCount", "Which page is showing (from 1) and how many there are. A wheel button steps through the pages.", "Page + \" / \" + PageCount");
            Item("tap(\"name\")", "Which view a tap zone is on: 0 at first, then 1, 2... each time it's tapped on a touch screen (or clicked in a window). Use it in Show when.", "tap(\"fuel\") == 1");

            NewSection("Recipes");
            Prose("Ready-made formulas for common jobs. Insert one and change it to suit.");
            foreach (var recipe in DataPicker.Recipes)
            {
                string formula = recipe.Formula;
                Item(recipe.Name, recipe.Description, formula, null, insertName: null, mono: false);
            }
        }

        private void NewSection(string title)
        {
            var header = new TextBlock { Text = title, Style = Res<Style>("T.Title"), FontSize = 20, Margin = new Thickness(0, _sections.Count == 0 ? 0 : 30, 0, 10) };
            _content.Children.Add(header);
            var section = new Section { Title = title, Header = header };
            var nav = new Button
            {
                Content = title, Style = Res<Style>("Btn.Ghost"), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 0, 1), FontSize = 13
            };
            nav.Click += (s, e) => Jump(section);
            section.Nav = nav;
            _nav.Children.Add(nav);
            _sections.Add(section);
            _current = section;
        }

        private void Jump(Section section)
        {
            if (_search.Text.Length > 0) _search.Text = "";
            _scroller.UpdateLayout();
            Point at = section.Header.TranslatePoint(new Point(0, 0), _content);
            _scroller.ScrollToVerticalOffset(Math.Max(0, at.Y + _content.Margin.Top - 12));
        }

        private void Prose(string text)
        {
            var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Res<Brush>("Brush.TextSecondary"), FontSize = 13, LineHeight = 20, Margin = new Thickness(0, 0, 0, 10) };
            _content.Children.Add(block);
            _current.Prose.Add(block);
        }

        private void Bullet(string text)
        {
            var row = new DockPanel { Margin = new Thickness(8, 0, 0, 6) };
            var dot = new TextBlock { Text = "•", Foreground = Res<Brush>("Brush.Accent"), Margin = new Thickness(0, 0, 8, 0) };
            DockPanel.SetDock(dot, Dock.Left);
            row.Children.Add(dot);
            row.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Res<Brush>("Brush.TextSecondary"), FontSize = 13, LineHeight = 20 });
            _content.Children.Add(row);
            _current.Prose.Add(row);
        }

        private void FunctionItem(FunctionInfo fn)
        {
            Examples.TryGetValue(fn.Name, out string example);
            string args = fn.MinArgs == fn.MaxArgs ? fn.MinArgs + (fn.MinArgs == 1 ? " value" : " values")
                : fn.MaxArgs >= 16 ? fn.MinArgs + " or more values" : fn.MinArgs + " to " + fn.MaxArgs + " values";
            Item(fn.Signature, fn.Description + " Takes " + args + ".", example, null, insertName: fn.Signature);
        }

        private void ColorItem(string name, string description)
        {
            var swatch = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(5), BorderBrush = Res<Brush>("Brush.BorderStrong"), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Top };
            Item(name, description, null, s => Show(Expr.Evaluate(name, s)), insertName: name, leading: swatch, color: s => Expr.Color(Expr.Evaluate(name, s), s));
        }

        /// <summary>
        /// One entry: its name, what it is, an example with its answer, a live value, and Copy and Insert. Live parts
        /// are updated four times a second from the designer's preview.
        /// </summary>
        private void Item(string name, string description, string example, Func<EvalScope, string> live = null, string insertName = null, bool mono = true,
            FrameworkElement leading = null, Func<EvalScope, System.Drawing.Color?> color = null)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (leading != null)
            {
                grid.Children.Add(leading);
                if (color != null && leading is Border swatch)
                    _live.Add((new TextBlock(), s =>
                    {
                        System.Drawing.Color? c = color(s);
                        swatch.Background = c.HasValue ? new SolidColorBrush(Color.FromArgb(c.Value.A, c.Value.R, c.Value.G, c.Value.B)) : null;
                        return "";
                    }));
            }

            var text = new StackPanel();
            Grid.SetColumn(text, 1);
            text.Children.Add(new TextBlock
            {
                Text = name, FontFamily = mono ? Res<FontFamily>("Font.Mono") : FontFamily, FontSize = 13, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap
            });
            text.Children.Add(new TextBlock { Text = description, Foreground = Res<Brush>("Brush.TextSecondary"), FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
            if (!string.IsNullOrEmpty(example))
            {
                var code = new Grid { Margin = new Thickness(0, 7, 0, 0) };
                code.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                code.ColumnDefinitions.Add(new ColumnDefinition());
                var box = new Border
                {
                    Background = Res<Brush>("Brush.SurfaceRaised"), BorderBrush = Res<Brush>("Brush.Border"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(9, 4, 9, 5), HorizontalAlignment = HorizontalAlignment.Left,
                    Child = new TextBlock { Text = example, FontFamily = Res<FontFamily>("Font.Mono"), FontSize = 12.5, TextWrapping = TextWrapping.Wrap }
                };
                code.Children.Add(box);
                var answer = new TextBlock { FontFamily = Res<FontFamily>("Font.Mono"), FontSize = 12.5, Foreground = Res<Brush>("Brush.TextTertiary"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
                Grid.SetColumn(answer, 1);
                code.Children.Add(answer);
                text.Children.Add(code);
                string formula = example;
                _live.Add((answer, s =>
                {
                    Expr.Compiled compiled = Expr.Compile(formula);
                    return compiled.Error != null ? "⚠ " + compiled.Error : "→ " + Show(Expr.Evaluate(formula, s));
                }));
            }
            grid.Children.Add(text);

            var side = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(16, 0, 0, 0) };
            Grid.SetColumn(side, 2);
            if (live != null)
            {
                var value = new TextBlock
                {
                    FontFamily = Res<FontFamily>("Font.Mono"), FontSize = 12.5, Foreground = Res<Brush>("Brush.Accent"), MinWidth = 90, MaxWidth = 170,
                    TextAlignment = TextAlignment.Right, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0),
                    ToolTip = "Live, from the designer's preview"
                };
                side.Children.Add(value);
                _live.Add((value, live));
            }
            string snippet = insertName ?? example ?? name;
            string copyText = example ?? insertName ?? name;
            side.Children.Add(SmallButton("Copy", "Copy " + copyText, () =>
            {
                try { Clipboard.SetText(copyText); _status.Text = "Copied " + copyText; }
                catch { _status.Text = "Couldn't reach the clipboard; try again."; }
            }));
            side.Children.Add(SmallButton("Insert", "Insert " + snippet + " into the formula you last clicked in the designer", () => Insert(snippet)));
            grid.Children.Add(side);

            var row = new Border { Child = grid, Padding = new Thickness(12, 10, 12, 10), CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 0, 4), Background = Brushes.Transparent };
            row.MouseEnter += (s, e) => row.Background = Res<Brush>("Brush.SurfaceHover");
            row.MouseLeave += (s, e) => row.Background = Brushes.Transparent;
            _content.Children.Add(row);
            _current.Entries.Add(new Entry { View = row, Search = name + " " + description + " " + example + " " + _current.Title });
        }

        private Button SmallButton(string text, string tooltip, Action click)
        {
            var button = new Button { Content = text, ToolTip = tooltip, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(4, 0, 0, 0), FontSize = 12, MinHeight = 0, VerticalAlignment = VerticalAlignment.Top };
            button.Click += (s, e) => click();
            return button;
        }

        private void Insert(string snippet)
        {
            if (FormulaBox.InsertIntoLast(snippet, out string where)) _status.Text = "Inserted " + snippet + " into " + where + ".";
            else _status.Text = "Click into a formula in the designer first (turn on ƒx beside a property), then Insert.";
        }

        // ------------------------------------------------------------------ search and live values

        private void Filter()
        {
            string[] words = _search.Text.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            bool searching = words.Length > 0;
            int found = 0;
            foreach (Section section in _sections)
            {
                int shown = 0;
                foreach (Entry entry in section.Entries)
                {
                    bool match = words.All(w => entry.Search.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);
                    entry.View.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
                    if (match) shown++;
                }
                foreach (FrameworkElement prose in section.Prose) prose.Visibility = searching ? Visibility.Collapsed : Visibility.Visible;
                bool any = !searching || shown > 0;
                section.Header.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
                section.Nav.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
                found += shown;
            }
            // The first heading showing sits at the top; the rest keep their gap.
            bool first = true;
            foreach (Section section in _sections.Where(x => x.Header.Visibility == Visibility.Visible))
            {
                section.Header.Margin = new Thickness(0, first ? 0 : 30, 0, 10);
                first = false;
            }
            if (searching) _status.Text = found == 0 ? "Nothing matches \"" + _search.Text.Trim() + "\"." : found + (found == 1 ? " match." : " matches.");
            _scroller.ScrollToTop();
            RefreshLive();
        }

        private void RefreshLive()
        {
            EvalScope scope;
            try { scope = _scope(); }
            catch { return; }
            foreach ((TextBlock text, Func<EvalScope, string> value) in _live)
            {
                if (!IsShown(text)) continue;
                string shown;
                try { shown = value(scope); }
                catch (Exception e) { shown = "⚠ " + e.Message; }
                if (text.Text != shown) text.Text = shown;
            }
        }

        /// <summary>On screen in the reference (not filtered out); live values off screen aren't worked out.</summary>
        private bool IsShown(FrameworkElement element)
        {
            if (element.Parent == null) return true;
            if (!element.IsVisible) return false;
            Point at = element.TranslatePoint(new Point(0, 0), _scroller);
            return at.Y > -60 && at.Y < _scroller.ActualHeight + 60;
        }

        private static string Show(object value)
        {
            if (value is System.Drawing.Color c) return SettingsStore.ToHex(c) + (c.A < 255 ? " @" + Math.Round(c.A / 2.55) + "%" : "");
            if (value is double d) return Math.Abs(d - Math.Round(d)) < 1e-9 ? Math.Round(d).ToString(System.Globalization.CultureInfo.InvariantCulture) : d.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            string text = Expr.Text(value);
            return text.Length == 0 ? "(empty)" : text;
        }

        // ------------------------------------------------------------------ words for the fixed lists

        private static string ThemeDescription(string token)
        {
            switch (token)
            {
                case "accent": return "The dashboard's accent color.";
                case "text": return "Main text.";
                case "muted": return "Labels and secondary text.";
                case "dim": return "Faint text and inactive marks.";
                case "panel": return "Panel fill.";
                case "paneltop": return "The lighter top of a panel.";
                case "border": return "Panel edges and dividers.";
                case "track": return "The empty part of bars and gauges.";
                case "good": return "Good news: gaining, enough fuel, green.";
                case "bad": return "Bad news: losing, warnings, red.";
                case "warn": return "Caution: yellow and amber.";
                case "info": return "Information: blue.";
                case "purple": return "Best times: purple.";
                case "background": return "The dashboard's background.";
                default: return "A theme color.";
            }
        }

        private static string StandingDescription(string field)
        {
            switch (field)
            {
                case "Name": return "Driver name.";
                case "Number": return "Car number.";
                case "Car": return "Car model.";
                case "Class": return "Class name.";
                case "ClassColor": return "The class's color.";
                case "Position": return "Overall position.";
                case "ClassPosition": return "Position in class.";
                case "LastLap": return "Last lap time, in seconds (use laptime() to show it).";
                case "BestLap": return "Best lap time, in seconds.";
                case "Laps": return "Laps completed.";
                case "LapsBehind": return "Laps behind the leader.";
                case "Gap": return "Seconds behind the leader.";
                case "GapText": return "The gap as shown on a leaderboard: LEADER, +5.48 or +1 LAP.";
                case "Interval": return "Seconds behind the car one place ahead.";
                case "Delta": return "Seconds from you: negative is ahead of you.";
                case "IRating": return "iRating.";
                case "License": return "License letter.";
                case "LicenseColor": return "The license's color.";
                case "InPits": return "True while on pit road.";
                case "IsPlayer": return "True for your car.";
                case "Exists": return "True when there's a car in that position.";
                default: return "A detail of the car.";
            }
        }

        private static string TyreDescription(string reading)
        {
            switch (reading)
            {
                case "Temp": return "Average temperature, in your unit.";
                case "TempInner": return "Inner edge temperature.";
                case "TempMiddle": return "Middle temperature.";
                case "TempOuter": return "Outer edge temperature.";
                case "Wear": return "Average tread left, 0 to 1.";
                case "WearInner": return "Inner tread left.";
                case "WearMiddle": return "Middle tread left.";
                case "WearOuter": return "Outer tread left.";
                case "Pressure": return "Pressure, in your unit.";
                default: return "A tire reading.";
            }
        }
    }
}
