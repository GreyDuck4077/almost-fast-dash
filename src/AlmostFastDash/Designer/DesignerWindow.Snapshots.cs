using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace AlmostFastDash.Designer
{
    public partial class DesignerWindow : Window
    {
        /// <summary>Sets up a state for the --ui-snapshot self-check.</summary>
        internal void PrepareSnapshot(string scenario)
        {
            PreviewCombo.SelectedItem = "Racing";
            DashWidget Find(string name) => _screen.Widgets.FirstOrDefault(w => w.Name == name);
            if (scenario.Contains("theme")) TabTheme.IsChecked = true;
            if (scenario.Contains("shortcuts"))
                Dispatcher.BeginInvoke(new Action(() => ShowShortcuts(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui",
                    "shortcuts-sheet.png"))),
                    System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            // A newcomer's first widget, as it comes in from the Library.
            if (scenario.Contains("first-"))
            {
                string type = scenario.Substring(scenario.IndexOf("first-") + 6).Split('-')[0];
                if (WidgetCatalog.Find(type) != null) AddWidgetAtCenter(type);
            }

            if (scenario.Contains("ghosts"))
            {
                // What only shows at certain moments: ghosts with a tag saying when, so they can be placed and styled.
                _screen.Widgets.Clear();
                void Place(List<DashWidget> widgets, double x, double y)
                {
                    double left = widgets.Min(w => w.X), top = widgets.Min(w => w.Y);
                    foreach (DashWidget w in widgets) { w.X += x - left; w.Y += y - top; _screen.Widgets.Add(w); }
                }
                foreach (DesignBlock block in DesignBlocks.BuiltIn().Where(b => b.Folder == "Warnings and spotter"))
                    Place(block.Create(), block.Name == "Spotter bars" ? 0 : block.Name == "Low fuel warning" ? 60 : 480,
                        block.Name == "Spotter bars" ? 90 : 60);
                Place(new List<DashWidget> { WidgetCatalog.Create("flag", 0, 0) }, 300, 200);
                Place(QuickValues.All().First(b => b.Folder == "Notification").Create(), 220, 360);
                _rendererDirty = true;
                Select(_screen.Widgets.First(w => w.Type == "flag"));
                _hover = _screen.Widgets.First(w => w.Name == "Pit banner");
                RequestRender();
            }
            else if (scenario.Contains("options"))
            {
                // Rulers and guides, and a tach whose color and redline are options set in one place.
                _screen.Widgets.Clear();
                List<DashWidget> tach = DesignBlocks.BuiltIn().First(b => b.Name == "Tach dial").Create();
                string id = DashScreen.NewId("g");
                foreach (DashWidget w in tach) { w.X += 277; w.Y += 90; w.Group = id; w.GroupName = "Tach"; _screen.Widgets.Add(w); }
                ModuleOptions.Set(tach, "Color", "#FF7A00");
                ModuleOptions.Set(tach, "Red from", "0.85");
                tach[0].Set("color", ModuleOptions.Formula("Color"));
                tach[0].Set("dangerAt", ModuleOptions.Formula("Red from"));
                tach[1].Set("color", ModuleOptions.Formula("Color"));
                _layout.GuidesX.Clear();
                _layout.GuidesY.Clear();
                _layout.GuidesX.AddRange(new double[] { 277, 427, 577 });
                _layout.GuidesY.AddRange(new double[] { 90, 390 });
                ShowRulers(true);
                _rendererDirty = true;
                _selection.Clear();
                _selection.AddRange(tach);
                SelectionChanged();
                RequestRender();
            }
            else if (scenario.Contains("repeat"))
            {
                // What Repeat makes: a dial's ticks and numbers, a numbered row of lights, and a ring turning as it goes.
                _screen.Widgets.Clear();
                DashWidget Piece(string type, string name, double cx, double cy, double w, double h, params string[] props)
                {
                    DashWidget piece = WidgetCatalog.Create(type, 0, 0);
                    piece.Name = name; piece.W = w; piece.H = h; piece.X = cx - w / 2; piece.Y = cy - h / 2;
                    if (type == "shape") { piece.Set("strokeWidth", "0"); piece.Set("radius", "0"); }
                    for (int i = 0; i + 1 < props.Length; i += 2) piece.Set(props[i], props[i + 1]);
                    _screen.Widgets.Add(piece);
                    return piece;
                }
                void Repeat(DashWidget original, RepeatSpec spec)
                {
                    List<List<DashWidget>> sets = Repeater.Build(new[] { original }, spec);
                    Repeater.Group(new[] { original }, sets, spec.Number, original.Name);
                    _screen.Widgets.AddRange(sets.SelectMany(s => s));
                }
                double cx = 230, cy = 262, a = 135 * Math.PI / 180;
                Point On(double radius) => new Point(cx + radius * Math.Cos(a), cy + radius * Math.Sin(a));
                Point p = On(150);
                Repeat(Piece("shape", "Minor tick", p.X, p.Y, 2, 10, "fill", "muted", "rotate", "-135"),
                    new RepeatSpec { Circle = true, Count = 41, Sweep = 270, Radius = 150, Start = 135, Follow = true });
                p = On(146);
                Repeat(Piece("shape", "Tick", p.X, p.Y, 4, 22, "fill", "#FFFFFF", "rotate", "-135"),
                    new RepeatSpec { Circle = true, Count = 9, Sweep = 270, Radius = 146, Start = 135, Follow = true });
                p = On(112);
                Repeat(Piece("text", "Number", p.X, p.Y, 40, 30, "text", "=Slot - 1", "size", "24"),
                    new RepeatSpec { Circle = true, Count = 9, Sweep = 270, Radius = 112, Start = 135, Follow = false, Number = true });
                Repeat(Piece("shape", "Light", 478, 64, 22, 22, "shape", "Ellipse",
                    "fill", "=RevPct * 12 >= Slot ? (Slot > 10 ? $bad : Slot > 7 ? $warn : $good) : $track", "glow", "6"),
                    new RepeatSpec { Across = 12, Down = 1, GapX = 6, Number = true });
                DashWidget petal = Piece("shape", "Petal", 650, 168, 26, 26, "radius", "6", "fill", "accent");
                // With the Repeat window open, it makes the ring itself, live.
                if (!scenario.Contains("dialog"))
                    Repeat(petal,
                        new RepeatSpec { Circle = true, Count = 16, Sweep = 360, Radius = 110, Start = -90, Follow = false,
                        TurnEach = 11.25 });
                _rendererDirty = true;
                if (scenario.Contains("dialog"))
                {
                    Select(_screen.Widgets.First(w => w.Name == "Petal"));
                    _lastRepeat = new RepeatSpec { Circle = true, Count = 16, Sweep = 360, Radius = 110, Start = -90, Follow = false,
                        TurnEach = 11.25 };
                    Dispatcher.BeginInvoke(new Action(RepeatSelection), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                }
                else Select(null);
                RequestRender();
            }
            else if (scenario.Contains("masks") || scenario.Contains("modules"))
            {
                // Clipping masks, fades and blending, for the snapshots and the guide.
                _screen.Widgets.Clear();
                DashWidget Layer(string name, string kind, double x, double y, double w, double h, params string[] props)
                {
                    DashWidget s = WidgetCatalog.Create(kind == "text" ? "text" : "shape", x, y);
                    s.Name = name; s.W = w; s.H = h;
                    if (kind != "text") { s.Set("shape", kind); s.Set("strokeWidth", "0"); s.Set("radius", "0"); }
                    for (int i = 0; i + 1 < props.Length; i += 2) s.Set(props[i], props[i + 1]);
                    _screen.Widgets.Add(s);
                    return s;
                }
                // A rectangle's outline with stripes clipped to it: a kerb.
                Layer("Outline", "Rectangle", 24, 24, 300, 150, "radius", "16", "fill", "", "stroke", "#FFFFFF", "strokeWidth", "12");
                Layer("Stripes", "Rectangle", 10, 10, 330, 180, "fillStyle", "Pattern", "pattern", "Stripes", "fill", "#FF3B30",
                    "fill2", "#FFFFFF",
                    "patternScale", "150", "patternAngle", "45", "clip", "true");
                // A gear digit filled with a gradient and a sheen, both clipped to the digit.
                Layer("Gear", "text", 350, 0, 200, 220, "text", "7", "size", "200", "color", "#FFFFFF");
                Layer("Gradient", "Rectangle", 350, 0, 200, 220, "fillStyle", "Linear", "fill", "#22D3EE", "fill2", "#A855F7",
                    "fillAngle", "90", "clip", "true");
                Layer("Sheen", "Ellipse", 330, -60, 200, 160, "fillStyle", "Radial", "fill", "#FFFFFF", "fill2", "", "clip", "true",
                    "fxBlend", "Screen", "opacity", "70");
                // A panel with light spilling over it, added and kept inside the panel.
                Layer("Panel", "Rectangle", 580, 24, 196, 150, "radius", "18", "fillStyle", "Pattern", "pattern", "Carbon",
                    "fill", "#3A3F48", "fill2", "#0B0C0F", "patternAngle", "0");
                Layer("Light", "Ellipse", 640, -20, 220, 170, "fillStyle", "Radial", "fill", "#FF7A00", "fill2", "", "clip", "true",
                    "fxBlend", "Add");
                // Fades.
                Layer("Fade right", "Rectangle", 24, 236, 360, 28, "fill", "#2DD4BF", "fxFade", "Right", "fxFadeLength", "70");
                Layer("Fade sides", "Rectangle", 24, 286, 360, 6, "fill", "#FFFFFF", "fxFade", "Sides", "fxFadeLength", "45");
                Layer("Fade bottom", "Rectangle", 24, 316, 360, 140, "radius", "14", "fillStyle", "Pattern", "pattern", "Honeycomb",
                    "fill", "#2DD4BF", "fill2", "#10202A",
                    "patternScale", "80", "patternAngle", "0", "fxFade", "Bottom", "fxFadeLength", "80");
                // Multiply tints what's under it; Screen lightens.
                Layer("Tiles", "Rectangle", 420, 236, 356, 220, "radius", "14", "fillStyle", "Pattern", "pattern", "Checks",
                    "fill", "#F8FAFC", "fill2", "#64748B", "patternScale", "120");
                Layer("Tint", "Ellipse", 440, 256, 180, 180, "fill", "#F43F5E", "fxBlend", "Multiply");
                Layer("Glow", "Ellipse", 576, 256, 180, 180, "fill", "#1D4ED8", "fxBlend", "Screen");
                _rendererDirty = true;
                Select(_screen.Widgets.First(w => w.Name == "Stripes"));
                if (scenario.Contains("modules"))
                {
                    // Some of them kept as modules, then placed on a fresh screen with one selected.
                    DesignModule Keep(string name, string category, string description, params string[] layers)
                    {
                        DesignModule module = DesignModule.From(name, _screen.Widgets.Where(w => layers.Contains(w.Name)), _layout,
                            _dashboard.AccentColor, _units);
                        module.Category = category;
                        module.Description = description;
                        DesignModule.Save(module);
                        return module;
                    }
                    DesignModule kerb = Keep("Kerb frame", "Frames", "A frame in red and white kerb stripes.", "Outline", "Stripes");
                    DesignModule gear = Keep("Neon gear", "Gauges", "A gear digit in a cyan to violet gradient with a sheen.", "Gear",
                        "Gradient", "Sheen");
                    DesignModule light = Keep("Carbon light panel", "Cards", "Carbon fibre with an orange light spilling over it.",
                        "Panel", "Light");
                    DesignModule honeycomb = Keep("Honeycomb fade", "Backgrounds", "A honeycomb panel fading away at the bottom.",
                        "Fade bottom");
                    Keep("Tinted tiles", "Backgrounds", "Checks tinted and lit by blended circles.", "Tiles", "Tint", "Glow");
                    _screen.Widgets.Clear();
                    ModulesChanged();
                    PlaceModule(honeycomb, new Point(190, 370), false);
                    PlaceModule(light, new Point(690, 120), false);
                    PlaceModule(gear, new Point(540, 330), false);
                    PlaceModule(kerb, new Point(190, 120), false);
                    // Properties scrolled to the placed module's colors.
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        TextBlock colors = Inspector.Children.OfType<TextBlock>().FirstOrDefault(t => t.Text == "COLORS");
                        if (colors != null) InspectorScroller.ScrollToVerticalOffset(colors.TranslatePoint(new Point(0, 0),
                            Inspector).Y - 120);
                    }), System.Windows.Threading.DispatcherPriority.ContextIdle);
                }
                RequestRender();
            }
            else if (scenario.Contains("drawing"))
            {
                // What the drawing tools make, for the snapshots and the guide.
                _screen.Widgets.Clear();
                DashWidget Shape(string name, string kind, double x, double y, double w, double h, params string[] props)
                {
                    DashWidget s = WidgetCatalog.Create(kind == "path" ? "path" : "shape", x, y);
                    s.Name = name; s.W = w; s.H = h;
                    if (kind != "path") s.Set("shape", kind);
                    for (int i = 0; i + 1 < props.Length; i += 2) s.Set(props[i], props[i + 1]);
                    _screen.Widgets.Add(s);
                    return s;
                }
                Shape("Carbon panel", "Rectangle", 24, 24, 380, 200, "radius", "18", "fillStyle", "Pattern", "pattern", "Carbon",
                    "fill", "#3A3F48", "fill2", "#0B0C0F",
                    "patternAngle", "0", "stroke", "#2DD4BF", "strokeWidth", "2");
                Shape("Kerb", "Rectangle", 56, 64, 316, 120, "radius", "0", "fill", "", "strokeWidth", "10", "stroke", "#FFFFFF",
                    "cap", "Flat", "join", "Sharp",
                    "fxFill", "Pattern", "fxFillColor", "#FF3B30", "fxPatternBack", "#FFFFFF", "fxPatternScale", "180",
                    "fxPatternAngle", "45");
                Shape("Star", "Star", 440, 24, 150, 150, "sides", "5", "inner", "45", "fillStyle", "Linear", "fill", "#FFD54A",
                    "fill2", "#FF7A00", "fillAngle", "90",
                    "strokeWidth", "0", "glow", "14");
                Shape("Hexagon", "Polygon", 620, 24, 200, 176, "sides", "6", "fillStyle", "Pattern", "pattern", "Honeycomb",
                    "fill", "#2DD4BF", "fill2", "#10202A",
                    "patternScale", "90", "patternAngle", "0", "stroke", "#2DD4BF", "strokeWidth", "2.5");
                Shape("Swoosh", "path", 40, 250, 520, 150, "closed", "false", "points",
                    "0,1|0,1|0.25,0.95 0.55,0.35|0.35,0.55|0.75,0.15 1,0|0.9,0.02|1,0",
                    "strokeWidth", "8", "strokeStyle", "Gradient", "stroke", "#22D3EE", "stroke2", "#A855F7", "strokeAngle", "0",
                    "glow", "10");
                Shape("Dashes", "Line", 40, 420, 360, 12, "strokeWidth", "4", "dash", "Dashed", "stroke", "#94A3B8");
                Shape("Dots", "Line", 40, 450, 360, 12, "strokeWidth", "5", "dash", "Dotted", "stroke", "#FBBF24");
                DashWidget metal = WidgetCatalog.Create("text", 600, 250);
                metal.Name = "Brushed metal"; metal.W = 230; metal.H = 120;
                metal.Set("text", "136"); metal.Set("size", "110"); metal.Set("color", "#FFFFFF");
                metal.Set("fxFill", "Pattern"); metal.Set("fxPattern", "Brushed"); metal.Set("fxFillColor",
                    "#FFFFFF"); metal.Set("fxPatternBack", "#7C8794");
                metal.Set("fxPatternAngle", "0"); metal.Set("fxPatternScale", "100");
                _screen.Widgets.Add(metal);
                Shape("Dots panel", "Rectangle", 600, 400, 230, 64, "radius", "10", "fillStyle", "Pattern", "pattern", "Dots",
                    "fill", "#334155", "fill2", "#0F172A",
                    "patternScale", "60", "patternAngle", "0", "strokeWidth", "0");
                _rendererDirty = true;
                Select(_screen.Widgets.First(w => w.Name == "Kerb"));
                RequestRender();
            }
            else if (scenario.Contains("multi"))
            {
                _selection.Clear();
                _selection.AddRange(_screen.Widgets.Where(w => w.Type == "metric").Take(4));
                SelectionChanged();
            }
            else if (scenario.Contains("empty")) Select(null);
            else if (scenario.Contains("shape"))
            {
                // A swoosh rev bar with the Shape controls in view.
                DashWidget bar = DesignBlocks.BuiltIn().First(b => b.Name == "Swoosh rev bar").Create()[0];
                bar.X = 220;
                bar.Y = 150;
                _screen.Widgets.Add(bar);
                foreach (DashWidget half in DesignBlocks.BuiltIn().First(b => b.Name == "Mirrored rev bars").Create())
                {
                    half.X += 15;
                    half.Y = 380;
                    _screen.Widgets.Add(half);
                }
                OpenFolders.Add("Ready-made blocks/Rev gauges");
                BuildToolbox();
                CollapsedGroups.Add("Value");
                CollapsedGroups.Add("Look");
                Select(bar);
            }
            else Select(Find(scenario.Contains("neon") ? "Gear" : "Fuel laps") ?? _screen.Widgets.LastOrDefault());
            RenderNow();
        }
    }
}
