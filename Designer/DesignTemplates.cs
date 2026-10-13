using System;
using System.Collections.Generic;
using System.Linq;

namespace AlmostFastOverlays
{
    internal sealed class DesignTemplate
    {
        public string Id, Name, Description, Accent;
        /// <summary>The lights that suit it: a road dashboard's rev lights, or an oval one's spotter-first lights.</summary>
        public bool OvalLights;
        public Func<DashLayout> Create;
    }

    /// <summary>
    /// The starting dashboards. Every one is an ordinary design: only widgets and settings the
    /// designer offers, so anything here can be built, changed or taken apart by hand. Each has a
    /// pit-lane screen, and some have extra pages for a wheel button.
    /// </summary>
    internal static class DesignTemplates
    {
        public static readonly List<DesignTemplate> All = new List<DesignTemplate>
        {
            new DesignTemplate { Id = "apex", Name = "Apex", Accent = "#2F8CFF", Create = Apex,
                Description = "Road and GT: an arched LED rev bar, speed inside a rev ring, the track map and a live graph either side." },
            new DesignTemplate { Id = "twindial", Name = "Twin Dial", Accent = "#5AB4FF", Create = TwinDial,
                Description = "Two needle dials, rev counter and speedometer, with the map and your lap between them." },
            new DesignTemplate { Id = "neon", Name = "Neon", Accent = "#A855F7", Create = Neon,
                Description = "A modern digital cluster: tapered neon rings, a huge speed readout and glowing tiles." },
            new DesignTemplate { Id = "ribbon", Name = "Ribbon", Accent = "#38BDF8", Create = Ribbon,
                Description = "A wide three-panel cluster: timing and pedals, mirrored half-dials around the gear, the cars around you." },
            new DesignTemplate { Id = "spotter", Name = "Spotter", Accent = "#FFB020", OvalLights = true, Create = Spotter,
                Description = "Ovals: full-height blind-spot bars, rev bars meeting in the middle, the relative and the race order." },
            new DesignTemplate { Id = "racedisplay", Name = "Race Display", Accent = "#00E08A", Create = RaceDisplay,
                Description = "A pro race display: LED row, huge gear, timing columns; a wheel button pages to standings and engine." },
            new DesignTemplate { Id = "blank", Name = "Blank canvas", Accent = "#22D3EE", Create = Blank,
                Description = "A dark background and nothing else, to build your own." },
        };

        public static DesignTemplate Find(string id) => All.FirstOrDefault(t => t.Id == id);

        /// <summary>The road and oval dashboards a new install starts with.</summary>
        public static DesignTemplate Road => Find("apex");
        public static DesignTemplate Oval => Find("spotter");

        public static DashLayout Blank()
        {
            var layout = new DashLayout();
            BaseTheme(layout.Theme);
            layout.Screens.Add(new DashScreen { Name = "Main" });
            return layout;
        }

        // ------------------------------------------------------------------ builder

        private sealed class Builder
        {
            public readonly DashLayout Layout = new DashLayout();
            private DashScreen _screen;

            public Builder(Action<LayoutTheme> theme)
            {
                BaseTheme(Layout.Theme);
                theme?.Invoke(Layout.Theme);
            }

            public Builder Screen(string name, string condition = "")
            {
                _screen = new DashScreen { Name = name, Condition = condition };
                Layout.Screens.Add(_screen);
                return this;
            }

            public DashWidget Add(string type, string name, double x, double y, double w, double h, params string[] props)
            {
                var widget = new DashWidget { Type = type, Name = name, X = x, Y = y, W = w, H = h };
                for (int i = 0; i + 1 < props.Length; i += 2) widget.Set(props[i], props[i + 1]);
                _screen.Widgets.Add(widget);
                return widget;
            }

            public DashWidget Panel(string name, double x, double y, double w, double h, params string[] props) => Add("panel", name, x, y, w, h, props);

            public DashWidget Metric(string name, double x, double y, double w, double h, string label, string value, params string[] props) =>
                Add("metric", name, x, y, w, h, new[] { "label", label, "value", value }.Concat(props).ToArray());

            public DashWidget Text(string name, double x, double y, double w, double h, string text, params string[] props) =>
                Add("text", name, x, y, w, h, new[] { "text", text }.Concat(props).ToArray());

            /// <summary>A small spaced-out caption.</summary>
            public DashWidget Label(string name, double x, double y, double w, string text, string align = "Left", string color = "muted") =>
                Text(name, x, y, w, 12, text, "size", "10", "tracking", "2.5", "weight", "Regular", "color", color, "align", align);

            /// <summary>A panel with a labelled value on it.</summary>
            public DashWidget Tile(string name, double x, double y, double w, double h, string label, string value, string panelStyle, params string[] metric)
            {
                Panel(name + " tile", x, y, w, h, "style", panelStyle, "radius", "16");
                return Metric(name, x, y + 4, w, h - 8, label, value, new[] { "inset", "16", "labelSize", "11", "labelTracking", "2", "valueSize", "30" }.Concat(metric).ToArray());
            }

            /// <summary>Puts widgets into one numbered group (see <see cref="DashWidget.Slot"/>).</summary>
            public void Group(string name, double slot, params DashWidget[] widgets)
            {
                string id = DashScreen.NewId("g");
                foreach (DashWidget w in widgets) { w.Group = id; w.GroupName = name; w.Slot = slot; }
            }

            public void Visible(string condition, params DashWidget[] widgets)
            {
                foreach (DashWidget w in widgets) w.Set("visible", condition);
            }
        }

        private const string PositionText = "=Position > 0 ? \"P\" + Position : \"P--\"";
        private const string FuelLaps = "=FuelLapsLeft < 0 ? \"--.-\" : format(FuelLapsLeft, \"0.0\")";
        private const string FuelTone = "=steps(FuelLapsLeft, $text, 0, $bad, 2, $warn, 5, $text)";
        private const string RpmTop = "=Redline > 0 ? ceil(Redline / 1000) : 8";
        private const string SpeedTop = "=SpeedUnit == \"KM/H\" ? 320 : 200";

        private static void BaseTheme(LayoutTheme theme)
        {
            theme.BackgroundStyle = "Bloom";
            theme.Background = "#04070C";
            theme.Background2 = "#0C1626";
            theme.Font = "Bahnschrift";
            theme.Panel = "#B00D141F";
            theme.PanelTop = "#C0131C2B";
            theme.Border = "#1E2A3A";
            theme.Track = "#141C27";
            theme.Text = "#EEF3FA";
            theme.Muted = "#8190A4";
            theme.Dim = "#3A4656";
            theme.Good = "#26D980";
            theme.Bad = "#FF3D55";
            theme.Warn = "#FFB627";
            theme.Info = "#3BA9FF";
            theme.Purple = "#B98CFF";
        }

        // ------------------------------------------------------------------ shared screens

        /// <summary>
        /// The pit-lane screen every dashboard has: speed against the limit, the limiter, fuel to add,
        /// the tyres the crew read and the stint so far. It takes over whenever the car is on pit road.
        /// </summary>
        private static void PitLane(Builder b, string panelStyle)
        {
            const string tone = "=OverPitLimit ? $bad : PitLimiter ? $good : $warn";
            b.Screen("Pit lane", "OnPitRoad");
            b.Panel("Limiter banner", 227, 16, 400, 52, "style", "Tinted", "tint", tone, "radius", "26");
            b.Text("Limiter", 227, 16, 400, 52, "=OverPitLimit ? \"SLOW DOWN\" : PitLimiter ? \"PIT LIMITER ON\" : \"PIT LIMITER OFF\"", "size", "24", "tracking", "2", "color", tone);
            b.Add("arc", "Pit speed ring", 287, 84, 280, 280, "value", "=PitSpeedLimit > 0 ? Speed / (PitSpeedLimit * 1.25) : 0", "style", "Smooth",
                "start", "135", "sweep", "270", "thickness", "14", "colorMode", "Shift colors", "color", "good", "warnColor", "bad", "dangerColor", "bad",
                "warnAt", "0.8", "dangerAt", "0.8", "flash", "=OverPitLimit", "glow", "12");
            b.Text("Pit speed", 307, 150, 240, 120, "=format(Speed, \"0\")", "size", "104", "color", "=OverPitLimit ? $bad : $text", "fxShadow", "8");
            b.Text("Pit limit", 307, 270, 240, 18, "=PitSpeedLimit > 0 ? \"LIMIT \" + format(PitSpeedLimit, \"0\") + \" \" + SpeedUnit : \"PIT LANE\"",
                "size", "12", "tracking", "3", "weight", "Regular", "color", "muted");
            b.Tile("Refuel", 20, 96, 240, 110, "FUEL TO ADD", "=FuelToFinish < 0 ? \"--.-\" : FuelToFinish <= 0.05 ? \"NONE\" : \"+\" + fuel(FuelToFinish)", panelStyle,
                "unit", "=FuelUnit", "valueColor", "=FuelToFinish < 0 ? $text : FuelToFinish <= 0.05 ? $good : $warn", "valueSize", "40");
            b.Tile("In tank", 20, 216, 240, 90, "IN THE TANK", "=fuel(Fuel)", panelStyle, "unit", "=FuelUnit");
            b.Tile("Stint", 20, 316, 240, 90, "STINT", "=StintLaps + \" LAPS\"", panelStyle);
            b.Panel("Tyres panel", 594, 96, 240, 310, "style", panelStyle, "radius", "16");
            b.Label("Tyres label", 610, 108, 200, "TYRES · LAST STOP");
            b.Add("tyres", "Tyres", 606, 128, 216, 266, "size", "22");
            b.Metric("Pit position", 307, 384, 240, 80, "POSITION", PositionText, "align", "Center", "valueSize", "40", "labelSize", "11", "labelTracking", "2",
                "unit", "=Starters > 0 ? \"/\" + Starters : \"\"", "unitColor", "dim");
        }

        /// <summary>A short race-notice banner (flags, penalties, a personal best), coloured by urgency.</summary>
        private static void Notice(Builder b, double x, double y, double w, double h)
        {
            const string tone = "=NoticeKind == \"Danger\" ? $bad : NoticeKind == \"Warning\" ? $warn : NoticeKind == \"Good\" ? $good : $accent";
            b.Visible("Notice != \"\"",
                b.Panel("Notice", x, y, w, h, "style", "Tinted", "tint", tone, "radius", (h / 2).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                b.Text("Notice text", x, y, w, h, "=Notice", "size", (h * 0.42).ToString("0", System.Globalization.CultureInfo.InvariantCulture), "tracking", "1", "color", tone));
        }

        // ------------------------------------------------------------------ Apex

        /// <summary>
        /// Road and GT, after the BMW digital cluster: an arched row of rev LEDs across the top filling
        /// from both ends, speed and gear inside a rev ring, the track map on the left and a live speed
        /// graph on the right, and five timing tiles along the bottom. A second page shows the race order.
        /// </summary>
        private static DashLayout Apex()
        {
            var b = new Builder(t =>
            {
                t.Background = "#03060B";
                t.Background2 = "#0B1A33";
                t.BackgroundStyle = "Bloom";
            });
            b.Screen("Race");
            b.Add("bar", "Rev LEDs", 70, 8, 714, 50, "value", "=RevPct", "mode", "Segments", "segments", "34", "bulge", "70", "grow", "Bottom edge", "radius", "3",
                "fill", FillOrder.EdgesToCenter, "colorMode", "Gradient", "warnAt", "0.55", "dangerAt", "0.85", "track", "#0F1724",
                "flash", "=ShiftFlash", "flashColor", "bad", "glow", "6");

            b.Panel("Map panel", 14, 70, 268, 254, "style", "Liquid", "radius", "20");
            b.Label("Map label", 32, 84, 200, "TRACK");
            b.Add("trackmap", "Track map", 26, 100, 244, 214, "lineWidth", "6", "carSize", "9");

            b.Panel("Graph panel", 572, 70, 268, 254, "style", "Liquid", "radius", "20");
            b.Label("Graph label", 590, 84, 200, "SPEED · LAST 30 S");
            b.Text("Top speed", 590, 98, 230, 26, "=\"TOP \" + format(LapMaxSpeed, \"0\") + \"  ·  LOW \" + format(LapMinSpeed, \"0\")", "size", "15", "align", "Left", "color", "muted");
            b.Add("graph", "Speed graph", 584, 130, 244, 182, "value", "=Speed", "min", "0", "max", SpeedTop, "seconds", "30", "style", "Area", "lineWidth", "2.5", "gridColor", "#1A2433");

            b.Add("arc", "Rev ring", 302, 70, 250, 250, "value", "=RevPct", "style", "Smooth", "start", "135", "sweep", "270", "thickness", "9",
                "colorMode", "Gradient", "warnAt", "0.55", "dangerAt", "0.85", "zones", "true", "track", "#121A26", "flash", "=ShiftFlash",
                "ticks", "9", "minorTicks", "1", "tickLength", "7", "tickColor", "dim", "glow", "12");
            b.Text("Speed", 327, 122, 200, 88, "=format(Speed, \"0\")", "size", "84", "fxShadow", "10", "fxShadowY", "4");
            b.Text("Speed unit", 327, 206, 200, 16, "=SpeedUnit", "size", "12", "tracking", "3", "weight", "Regular", "color", "muted");
            b.Panel("Gear pill", 384, 234, 86, 62, "style", "Tinted", "tint", "accent", "radius", "18");
            b.Add("gear", "Gear", 384, 234, 86, 62, "size", "48", "neighbors", "false", "glow", "8");

            const double tileY = 340, tileH = 128, tileW = 158;
            double X(int i) => 14 + i * (tileW + 9.5);
            b.Tile("Position", X(0), tileY, tileW, tileH, "POSITION", PositionText, "Liquid", "unit", "=Starters > 0 ? \"/\" + Starters : \"\"", "unitColor", "dim");
            b.Text("Lap", X(0) + 16, tileY + 96, tileW - 32, 18, "=\"LAP \" + LapText", "size", "13", "align", "Left", "color", "muted");
            b.Tile("Last lap", X(1), tileY, tileW, tileH, "LAST LAP", "=laptime(LastLap)", "Liquid", "valueSize", "27", "valueColor", "=LastLapIsBest ? $purple : $text");
            b.Text("Best lap", X(1) + 16, tileY + 96, tileW - 32, 18, "=\"BEST \" + laptime(BestLap)", "size", "13", "align", "Left", "color", "purple");
            b.Tile("Delta", X(2), tileY, tileW, tileH, "DELTA", "=signed(Delta, 2)", "Liquid", "valueColor", "=deltacolor(Delta)");
            b.Add("bar", "Delta bar", X(2) + 16, tileY + 102, tileW - 32, 6, "value", "=Delta", "min", "-1", "max", "1", "mode", "Center", "color", "good", "negColor", "bad", "glow", "4");
            b.Tile("Fuel", X(3), tileY, tileW, tileH, "FUEL", "=fuel(Fuel)", "Liquid", "unit", "=FuelUnit");
            b.Text("Fuel laps", X(3) + 16, tileY + 96, tileW - 32, 18, "=FuelLapsLeft < 0 ? \"-- LAPS\" : format(FuelLapsLeft, \"0.0\") + \" LAPS\"", "size", "13", "align", "Left", "color", FuelTone);
            b.Panel("Gaps tile", X(4), tileY, tileW, tileH, "style", "Liquid", "radius", "16");
            b.Metric("Ahead", X(4), tileY + 4, tileW, 60, "AHEAD", "=AheadName == \"---\" ? \"--\" : \"-\" + format(AheadGap, \"0.0\")", "inset", "16", "layout", "Label left",
                "valueSize", "24", "labelSize", "11", "labelTracking", "2", "valueColor", "good");
            b.Metric("Behind", X(4), tileY + 64, tileW, 60, "BEHIND", "=BehindName == \"---\" ? \"--\" : \"+\" + format(BehindGap, \"0.0\")", "inset", "16", "layout", "Label left",
                "valueSize", "24", "labelSize", "11", "labelTracking", "2", "valueColor", "warn");
            b.Add("blindspot", "Car left", 2, 120, 6, 200, "side", "Left", "glow", "12");
            b.Add("blindspot", "Car right", 846, 120, 6, 200, "side", "Right", "glow", "12");
            Notice(b, 287, 300, 280, 34);

            // Page 2 on a wheel button: the race order and the map.
            b.Screen("Standings");
            b.Panel("Standings panel", 14, 14, 528, 452, "style", "Liquid", "radius", "20");
            b.Label("Standings label", 32, 28, 300, "RACE ORDER");
            b.Add("standings", "Leaderboard", 24, 48, 508, 408, "rows", "11", "focus", "Top and me", "showIRating", "true", "size", "16", "rowColor", "#80101826", "radius", "8");
            b.Panel("Map panel 2", 552, 14, 288, 288, "style", "Liquid", "radius", "20");
            b.Add("trackmap", "Track map 2", 562, 24, 268, 268, "lineWidth", "7", "carSize", "11", "numbers", "true");
            b.Tile("Gap to leader", 552, 312, 288, 74, "TO THE LEADER", "=GapToLeader == \"\" ? \"--\" : GapToLeader", "Liquid", "layout", "Label left", "valueSize", "24");
            b.Tile("Session best", 552, 394, 288, 72, "SESSION BEST", "=laptime(SessionBestLap)", "Liquid", "layout", "Label left", "valueSize", "24", "valueColor", "purple");

            PitLane(b, "Liquid");
            return b.Layout;
        }

        // ------------------------------------------------------------------ Twin Dial

        /// <summary>
        /// After the Ford and Audi clusters: a rev counter and a speedometer as real needle dials with
        /// numbered scales and a red zone, the gear inside the rev counter, and the map, lap and delta
        /// between them; fuel and temperatures along the bottom.
        /// </summary>
        private static DashLayout TwinDial()
        {
            var b = new Builder(t =>
            {
                t.Background = "#02040A";
                t.Background2 = "#0A1630";
                t.BackgroundStyle = "Vignette";
                t.Track = "#1A2436";
            });
            b.Screen("Race");
            // Faint discs behind the dials give them a face.
            b.Add("shape", "Tach face", 26, 30, 312, 312, "shape", "Ellipse", "fill", "#60080F1C", "stroke", "#1B2840", "strokeWidth", "1.5");
            b.Add("shape", "Speedo face", 516, 30, 312, 312, "shape", "Ellipse", "fill", "#60080F1C", "stroke", "#1B2840", "strokeWidth", "1.5");
            b.Add("arc", "Rev counter", 16, 20, 332, 332, "value", "=Rpm / 1000", "max", RpmTop, "style", "Smooth", "thickness", "6", "start", "135", "sweep", "270",
                "colorMode", "Shift colors", "color", "accent", "warnAt", "0.8", "dangerAt", "0.9", "zones", "true", "ticks", RpmTop, "minorTicks", "4",
                "tickLength", "14", "tickColor", "#8EA2BE", "numbers", "true", "numbersTo", RpmTop, "numberSize", "24", "needle", "Needle", "needleColor", "#7DD3FC",
                "needleLength", "88", "needleWidth", "7", "glow", "10", "flash", "=ShiftFlash");
            b.Text("Rev unit", 107, 232, 150, 16, "x1000 RPM", "size", "11", "tracking", "2", "weight", "Regular", "color", "muted");
            b.Add("gear", "Gear", 132, 250, 100, 70, "size", "56", "neighbors", "false", "glow", "6");

            b.Add("arc", "Speedometer", 506, 20, 332, 332, "value", "=Speed", "max", SpeedTop, "style", "Smooth", "thickness", "6", "start", "135", "sweep", "270",
                "colorMode", "Solid", "color", "accent", "ticks", "=SpeedUnit == \"KM/H\" ? 8 : 10", "minorTicks", "3", "tickLength", "14", "tickColor", "#8EA2BE",
                "numbers", "true", "numbersTo", SpeedTop, "numberSize", "22", "needle", "Needle", "needleColor", "#7DD3FC", "needleLength", "88", "needleWidth", "7", "glow", "8");
            b.Text("Speed", 582, 226, 180, 60, "=format(Speed, \"0\")", "size", "52");
            b.Text("Speed unit", 582, 282, 180, 16, "=SpeedUnit", "size", "11", "tracking", "3", "weight", "Regular", "color", "muted");

            b.Panel("Centre panel", 352, 34, 150, 300, "style", "Glass", "radius", "18");
            b.Add("trackmap", "Track map", 358, 44, 138, 120, "lineWidth", "5", "carSize", "7");
            b.Metric("Lap", 352, 170, 150, 50, "LAP", "=LapText", "align", "Center", "valueSize", "22", "labelSize", "10", "labelTracking", "2");
            b.Metric("Delta", 352, 224, 150, 50, "DELTA", "=signed(Delta, 2)", "align", "Center", "valueSize", "22", "labelSize", "10", "labelTracking", "2", "valueColor", "=deltacolor(Delta)");
            b.Metric("Last lap", 352, 278, 150, 50, "LAST", "=laptime(LastLap)", "align", "Center", "valueSize", "20", "labelSize", "10", "labelTracking", "2", "valueColor", "=LastLapIsBest ? $purple : $text");

            b.Panel("Bottom strip", 16, 378, 822, 88, "style", "Glass", "radius", "18");
            b.Metric("Position", 20, 382, 150, 80, "POSITION", PositionText, "valueSize", "30", "unit", "=Starters > 0 ? \"/\" + Starters : \"\"", "unitColor", "dim");
            b.Metric("Fuel", 180, 382, 160, 80, "FUEL", "=fuel(Fuel)", "valueSize", "30", "unit", "=FuelUnit");
            b.Add("bar", "Fuel level", 196, 448, 130, 5, "value", "=FuelPct", "color", "=FuelLapsLeft >= 0 && FuelLapsLeft < 2 ? $bad : $accent");
            b.Metric("Fuel laps", 350, 382, 140, 80, "LAPS LEFT", FuelLaps, "valueSize", "30", "valueColor", FuelTone);
            b.Metric("Water", 500, 382, 110, 80, "WATER", "=format(WaterTemp, \"0\") + \"°\"", "valueSize", "26");
            b.Metric("Oil", 610, 382, 110, 80, "OIL", "=format(OilTemp, \"0\") + \"°\"", "valueSize", "26");
            b.Metric("Clock", 720, 382, 114, 80, "TIME", "=Clock", "valueSize", "26", "align", "Right");
            b.Add("blindspot", "Car left", 2, 80, 6, 240, "side", "Left", "glow", "12");
            b.Add("blindspot", "Car right", 846, 80, 6, 240, "side", "Right", "glow", "12");
            Notice(b, 287, 342, 280, 30);

            PitLane(b, "Glass");
            return b.Layout;
        }

        // ------------------------------------------------------------------ Neon

        /// <summary>
        /// A modern digital cluster in neon: tapered rings that grow toward the redline (revs on the left,
        /// speed on the right) with glowing gradients, a huge speed readout and gear in the middle, and
        /// glowing tiles for fuel, tyres and temperatures.
        /// </summary>
        private static DashLayout Neon()
        {
            var b = new Builder(t =>
            {
                t.Background = "#05020C";
                t.Background2 = "#1A0B36";
                t.BackgroundStyle = "Aurora";
                t.Panel = "#A0120A26";
                t.PanelTop = "#B0190E33";
                t.Border = "#3A1E66";
                t.Track = "#1C1230";
                t.Text = "#F4F0FF";
                t.Muted = "#9C8CC4";
                t.Dim = "#4A3A6E";
                t.Info = "#22D3EE";
            });
            b.Screen("Race");
            b.Text("Clock", 20, 12, 160, 22, "=Clock", "size", "18", "align", "Left", "color", "muted");
            b.Text("Lap", 327, 10, 200, 26, "=\"LAP \" + LapText", "size", "18", "tracking", "2", "color", "text");
            b.Text("Air", 674, 12, 160, 22, "=format(AirTemp, \"0\") + TempUnit", "size", "18", "align", "Right", "color", "muted");

            b.Add("arc", "Rev ring", 20, 50, 270, 270, "value", "=RevPct", "style", "Smooth", "thickness", "22", "taper", "75", "start", "135", "sweep", "270",
                "colorMode", "Gradient", "color", "info", "warnColor", "accent", "dangerColor", "bad", "warnAt", "0.5", "dangerAt", "0.88", "track", "#160E28",
                "glow", "16", "flash", "=ShiftFlash", "fxGlow", "10", "fxGlowColor", "=alpha($accent, 0.5)");
            b.Text("Rpm", 70, 150, 170, 50, "=format(Rpm, \"0\")", "size", "40", "color", "info", "fxGlow", "8", "fxGlowColor", "info");
            b.Text("Rpm unit", 70, 198, 170, 16, "RPM", "size", "11", "tracking", "3", "weight", "Regular", "color", "muted");

            b.Add("arc", "Fuel ring", 564, 50, 270, 270, "value", "=FuelPct", "style", "Smooth", "thickness", "22", "taper", "75",
                "start", "45", "sweep", "-270", "colorMode", "Solid", "color", "accent",
                "track", "#160E28", "glow", "16", "fxGlow", "10", "fxGlowColor", "=alpha($accent, 0.5)");
            b.Text("Fuel pct", 614, 150, 170, 50, "=format(FuelPct * 100, \"0\") + \"%\"", "size", "40", "color", "accent", "fxGlow", "8", "fxGlowColor", "accent");
            b.Text("Fuel pct unit", 614, 198, 170, 16, "FUEL", "size", "11", "tracking", "3", "weight", "Regular", "color", "muted");

            b.Text("Speed", 297, 80, 260, 140, "=format(Speed, \"0\")", "size", "132", "fxGlow", "14", "fxGlowColor", "=alpha($accent, 0.7)",
                "fxFill", "Gradient", "fxFillColor", "#FFFFFF", "fxFillColor2", "#D9C7FF");
            b.Text("Speed unit", 297, 218, 260, 20, "=SpeedUnit", "size", "16", "tracking", "4", "weight", "Regular", "color", "muted");
            b.Panel("Gear box", 382, 248, 90, 64, "style", "Outline", "border", "accent", "borderWidth", "2", "radius", "14", "glow", "12", "glowColor", "=alpha($accent, 0.6)");
            b.Add("gear", "Gear", 382, 248, 90, 64, "size", "48", "neighbors", "false", "color", "text");

            b.Add("shape", "Divider", 40, 334, 774, 2, "shape", "Line", "stroke", "accent", "strokeWidth", "1.5", "fxGlow", "8", "fxGlowColor", "accent", "opacity", "70");

            b.Tile("Last lap", 20, 350, 196, 116, "LAST LAP", "=laptime(LastLap)", "Glass", "valueSize", "28", "valueColor", "=LastLapIsBest ? $purple : $text");
            b.Text("Delta", 36, 432, 164, 24, "=signed(Delta, 2)", "size", "18", "align", "Left", "color", "=deltacolor(Delta)");
            b.Panel("Tyres tile", 226, 350, 196, 116, "style", "Glass", "radius", "16");
            b.Add("tyres", "Tyres", 236, 358, 176, 100, "size", "16", "showWear", "false", "showPressure", "false", "panelColor", "#60100A20");
            b.Tile("Position", 432, 350, 196, 116, "POSITION", PositionText, "Glass", "valueSize", "34", "unit", "=Starters > 0 ? \"/\" + Starters : \"\"", "unitColor", "dim");
            b.Tile("Fuel laps", 638, 350, 196, 116, "FUEL LAPS", FuelLaps, "Glass", "valueSize", "34", "valueColor", FuelTone);
            b.Add("blindspot", "Car left", 2, 80, 6, 240, "side", "Left", "color", "accent", "glow", "14");
            b.Add("blindspot", "Car right", 846, 80, 6, 240, "side", "Right", "color", "accent", "glow", "14");
            Notice(b, 287, 40, 280, 32);

            PitLane(b, "Glass");
            return b.Layout;
        }

        // ------------------------------------------------------------------ Ribbon

        /// <summary>
        /// A wide three-panel cluster: lap timing and a pedal graph on the left; in the middle two
        /// half-dials (one a mirrored copy of the other) meeting around the gear and speed; the cars
        /// around you and the gaps on the right.
        /// </summary>
        private static DashLayout Ribbon()
        {
            var b = new Builder(t =>
            {
                t.Background = "#03070D";
                t.Background2 = "#0B2236";
                t.BackgroundStyle = "Gradient";
            });
            b.Screen("Race");
            b.Panel("Left panel", 10, 24, 270, 432, "style", "Liquid", "radius", "24");
            b.Panel("Centre panel", 290, 24, 274, 432, "style", "Liquid", "radius", "24");
            b.Panel("Right panel", 574, 24, 270, 432, "style", "Liquid", "radius", "24");

            b.Metric("Last lap", 18, 36, 254, 70, "LAST LAP", "=laptime(LastLap)", "valueSize", "34", "valueColor", "=LastLapIsBest ? $purple : $text");
            b.Metric("Best lap", 18, 108, 126, 56, "BEST", "=laptime(BestLap)", "valueSize", "20", "valueColor", "purple");
            b.Metric("Predicted", 146, 108, 126, 56, "PREDICTED", "=laptime(PredictedLap)", "valueSize", "20", "align", "Right");
            b.Label("Delta label", 34, 176, 120, "DELTA");
            b.Text("Delta", 120, 168, 136, 28, "=signed(Delta, 2)", "size", "24", "align", "Right", "color", "=deltacolor(Delta)");
            b.Add("bar", "Delta bar", 34, 204, 222, 6, "value", "=Delta", "min", "-1", "max", "1", "mode", "Center", "color", "good", "negColor", "bad", "glow", "4");
            b.Label("Pedals label", 34, 232, 200, "THROTTLE · BRAKE");
            b.Add("graph", "Pedals", 26, 250, 238, 120, "value", "=Throttle", "value2", "=Brake", "min", "0", "max", "1", "seconds", "6", "style", "Line",
                "lineWidth", "2.5", "color", "good", "color2", "bad", "gridColor", "#18222F");
            b.Metric("Fuel", 18, 382, 126, 66, "FUEL", "=fuel(Fuel)", "valueSize", "24", "unit", "=FuelUnit");
            b.Metric("Fuel laps", 146, 382, 126, 66, "LAPS", FuelLaps, "valueSize", "24", "align", "Right", "valueColor", FuelTone);

            // Two half-dials: the rev counter on the left, and the same widget flipped to read the speed on the right.
            b.Add("arc", "Rev half", 296, 60, 262, 262, "value", "=RevPct", "style", "Segmented", "segments", "18", "start", "100", "sweep", "160", "thickness", "14",
                "colorMode", "Gradient", "warnAt", "0.6", "dangerAt", "0.88", "flash", "=ShiftFlash", "glow", "10");
            b.Add("arc", "Speed half", 296, 60, 262, 262, "value", "=Speed", "max", SpeedTop, "style", "Segmented", "segments", "18", "start", "100", "sweep", "160",
                "thickness", "14", "colorMode", "Solid", "color", "info", "glow", "8", "flipX", "true");
            b.Add("gear", "Gear", 377, 110, 100, 110, "size", "92", "neighbors", "false", "glow", "8", "fxShadow", "8");
            b.Text("Speed", 362, 220, 130, 46, "=format(Speed, \"0\")", "size", "40");
            b.Text("Speed unit", 362, 264, 130, 16, "=SpeedUnit", "size", "11", "tracking", "3", "weight", "Regular", "color", "muted");
            b.Metric("Position", 298, 342, 128, 100, "POSITION", PositionText, "align", "Center", "valueSize", "34");
            b.Metric("Lap", 428, 342, 128, 100, "LAP", "=LapText", "align", "Center", "valueSize", "30");
            b.Add("progress", "Race progress", 306, 440, 242, 4, "gap", "1");

            b.Label("Around label", 592, 40, 200, "AROUND YOU");
            b.Add("standings", "Around me", 582, 58, 254, 250, "rows", "5", "focus", "Around me", "column", "Interval", "showClass", "true", "size", "15", "rowColor", "#70101826");
            b.Metric("Ahead", 582, 324, 254, 56, "GAP AHEAD", "=AheadName == \"---\" ? \"--\" : format(AheadGap, \"0.0\")", "layout", "Label left", "valueSize", "26", "valueColor", "good");
            b.Metric("Behind", 582, 384, 254, 56, "GAP BEHIND", "=BehindName == \"---\" ? \"--\" : format(BehindGap, \"0.0\")", "layout", "Label left", "valueSize", "26", "valueColor", "warn");
            b.Add("blindspot", "Car left", 2, 80, 6, 320, "side", "Left", "glow", "12");
            b.Add("blindspot", "Car right", 846, 80, 6, 320, "side", "Right", "glow", "12");
            Notice(b, 300, 2, 254, 22);

            PitLane(b, "Liquid");
            return b.Layout;
        }

        // ------------------------------------------------------------------ Spotter

        /// <summary>
        /// Ovals: blind-spot bars down both edges, two rev bars meeting in the middle, the gear and speed,
        /// the relative on the left and the race order around you on the right, lap speed, fuel to the end
        /// and laps to go along the bottom.
        /// </summary>
        private static DashLayout Spotter()
        {
            var b = new Builder(t =>
            {
                t.Background = "#050506";
                t.Background2 = "#1C1408";
                t.BackgroundStyle = "Vignette";
                t.Track = "#1D1A14";
                t.Panel = "#B0141210";
                t.PanelTop = "#C01C1914";
                t.Border = "#2E2716";
            });
            b.Screen("Race");
            b.Add("blindspot", "Car left", 4, 16, 20, 448, "side", "Left", "shape", "Zones", "glow", "18");
            b.Add("blindspot", "Car right", 830, 16, 20, 448, "side", "Right", "shape", "Zones", "glow", "18");
            foreach (bool right in new[] { false, true })
                b.Add("bar", right ? "Revs right" : "Revs left", right ? 431 : 36, 14, 387, 34, "value", "=RevPct", "mode", "Segments", "segments", "16", "taper", "70",
                    "radius", "2", "colorMode", "Shift colors", "warnAt", "0.6", "dangerAt", "0.85", "flash", "=ShiftFlash", "flashColor", "bad", "glow", "5", "flipX", right ? "true" : "false");

            b.Panel("Relative panel", 36, 62, 252, 290, "style", "Soft", "radius", "16");
            b.Label("Relative label", 52, 74, 200, "ON TRACK");
            b.Add("relative", "Relative", 42, 92, 240, 254, "rows", "5", "size", "15");

            b.Add("gear", "Gear", 312, 56, 230, 210, "size", "190", "neighbors", "false", "glow", "10", "fxShadow", "12", "fxShadowY", "6");
            b.Text("Speed", 327, 262, 200, 54, "=format(Speed, \"0\")", "size", "46");
            b.Text("Speed unit", 327, 314, 200, 16, "=SpeedUnit", "size", "12", "tracking", "3", "weight", "Regular", "color", "muted");

            b.Panel("Order panel", 566, 62, 252, 290, "style", "Soft", "radius", "16");
            b.Label("Order label", 582, 74, 200, "RACE ORDER");
            b.Add("standings", "Race order", 572, 92, 240, 254, "rows", "5", "focus", "Around me", "column", "Interval", "showNumber", "false", "size", "15", "rowColor", "#70181510");

            const double y = 366, h = 100;
            b.Tile("Position", 36, y, 150, h, "POSITION", PositionText, "Soft", "valueSize", "34", "unit", "=Starters > 0 ? \"/\" + Starters : \"\"", "unitColor", "dim");
            b.Tile("To go", 194, y, 150, h, "=SessionType", "=RemainingText", "Soft", "valueSize", "24");
            b.Tile("Lap speed", 352, y, 150, h, "LAST LAP SPEED", "=format(LastLapSpeed, \"0.0\")", "Soft", "valueSize", "30", "unit", "=SpeedUnit");
            b.Tile("Refuel", 510, y, 150, h, "FUEL TO END", "=FuelToFinish < 0 ? \"--.-\" : FuelToFinish <= 0.05 ? \"NONE\" : \"+\" + fuel(FuelToFinish)", "Soft",
                "valueSize", "28", "valueColor", "=FuelToFinish < 0 ? $text : FuelToFinish <= 0.05 ? $good : $warn");
            b.Tile("Last lap", 668, y, 150, h, "LAST LAP", "=laptime(LastLap)", "Soft", "valueSize", "24", "valueColor", "=LastLapIsBest ? $purple : $text");
            b.Panel("Caution banner", 312, 322, 230, 36, "style", "Tinted", "tint", "warn", "radius", "18", "visible", "Caution");
            b.Text("Caution", 312, 322, 230, 36, "CAUTION", "size", "18", "tracking", "4", "color", "warn", "visible", "Caution");
            Notice(b, 312, 322, 230, 36);

            PitLane(b, "Soft");
            return b.Layout;
        }

        // ------------------------------------------------------------------ Race Display

        /// <summary>
        /// A pro race display: a row of LEDs, a huge gear, timing on the left, car and fuel on the right,
        /// tyres and sectors underneath. A wheel button pages to the standings (built from numbered
        /// driver-row groups) and to an engine page.
        /// </summary>
        private static DashLayout RaceDisplay()
        {
            var b = new Builder(t =>
            {
                t.Background = "#000000";
                t.Background2 = "#000000";
                t.BackgroundStyle = "Solid";
                t.Panel = "#FF0C0F12";
                t.PanelTop = "#FF12161B";
                t.Border = "#1E252D";
                t.Track = "#14191F";
            });
            b.Screen("Race");
            b.Add("shiftlights", "LEDs", 120, 8, 614, 30, "count", "15", "shape", "Dot", "fill", FillOrder.EdgesToCenter, "glow", "12");
            b.Add("gear", "Gear", 292, 44, 270, 256, "size", "236", "neighbors", "false", "color", "text", "flashColor", "info");
            b.Text("Speed", 327, 298, 200, 48, "=format(Speed, \"0\")", "size", "40");
            b.Text("Speed unit", 327, 342, 200, 16, "=SpeedUnit", "size", "11", "tracking", "3", "weight", "Regular", "color", "muted");

            string[] leftLabels = { "LAST", "BEST", "PREDICTED" };
            string[] leftValues = { "=laptime(LastLap)", "=laptime(BestLap)", "=laptime(PredictedLap)" };
            string[] leftTones = { "=LastLapIsBest ? $purple : $text", "purple", "=PredictedLap > 0 && BestLap > 0 && PredictedLap <= BestLap ? $good : $text" };
            for (int i = 0; i < 3; i++)
            {
                b.Panel(leftLabels[i] + " box", 10, 52 + i * 78, 272, 70, "style", "Solid", "radius", "6", "edge", "Left", "edgeColor", "accent");
                b.Metric(leftLabels[i], 10, 52 + i * 78, 272, 70, leftLabels[i], leftValues[i], "layout", "Label left", "valueSize", "30", "valueColor", leftTones[i]);
            }
            b.Panel("Delta box", 10, 286, 272, 70, "style", "Solid", "radius", "6", "edge", "Left", "edgeColor", "=deltacolor(Delta)");
            b.Metric("Delta", 10, 286, 272, 56, "DELTA", "=signed(Delta, 2)", "layout", "Label left", "valueSize", "30", "valueColor", "=deltacolor(Delta)");
            b.Add("bar", "Delta bar", 24, 340, 244, 6, "value", "=Delta", "min", "-1", "max", "1", "mode", "Center", "color", "good", "negColor", "bad");

            string[] rightLabels = { "FUEL", "LAPS LEFT", "POSITION", "TC · ABS · BIAS" };
            string[] rightValues = { "=fuel(Fuel)", FuelLaps, PositionText,
                "=(TractionControl < 0 ? \"-\" : format(TractionControl, \"0\")) + \" · \" + (Abs < 0 ? \"-\" : format(Abs, \"0\")) + \" · \" + format(BrakeBias, \"0.0\")" };
            string[] rightTones = { "text", FuelTone, "text", "info" };
            for (int i = 0; i < 4; i++)
            {
                b.Panel(rightLabels[i] + " box", 572, 52 + i * 78, 272, 70, "style", "Solid", "radius", "6", "edge", "Right", "edgeColor", "accent");
                b.Metric(rightLabels[i], 572, 52 + i * 78, 272, 70, rightLabels[i], rightValues[i], "layout", "Label left", "valueSize", i == 3 ? "22" : "30", "valueColor", rightTones[i]);
            }
            b.Panel("Sector box", 10, 364, 534, 106, "style", "Solid", "radius", "6");
            for (int s = 1; s <= 3; s++)
            {
                string time = "sectortime(" + s + ")";
                b.Metric("S" + s, 10 + (s - 1) * 178, 368, 178, 98, "S" + s + " · LAST LAP", "=seconds(lastsectortime(" + s + "))", "align", "Center", "valueSize", "26",
                    "valueColor", "=lastsectortime(" + s + ") > 0 && lastsectortime(" + s + ") <= bestsectortime(" + s + ") + 0.0005 ? $purple : $text", "visible", s == 3 ? "SectorCount >= 3" : "");
            }
            b.Panel("Tyre box", 552, 364, 292, 106, "style", "Solid", "radius", "6");
            b.Add("tyres", "Tyres", 560, 370, 276, 94, "size", "17", "showPressure", "false", "panelColor", "#FF101418");
            Notice(b, 292, 44, 270, 30);

            // Page 2: the standings as twelve numbered driver rows (one group, copied with Ctrl+D for each place).
            b.Screen("Standings");
            b.Label("Standings label", 16, 10, 300, "STANDINGS");
            b.Text("Session best", 438, 6, 400, 18, "=\"SESSION BEST \" + laptime(SessionBestLap)", "size", "13", "align", "Right", "color", "purple");
            for (int p = 1; p <= 12; p++)
            {
                double y = 30 + (p - 1) * 37;
                string S(string field) => "standing(Slot, \"" + field + "\")";
                DashWidget row = b.Panel("Row", 10, y, 834, 33, "style", "Solid", "radius", "5", "fill", "=" + S("IsPlayer") + " ? alpha($accent, 0.25) : $panel",
                    "edge", "Left", "edgeColor", "=" + S("ClassColor"));
                DashWidget place = b.Text("Place", 22, y, 44, 33, "=" + S("Position"), "size", "18", "color", "=" + S("IsPlayer") + " ? $accent : $text");
                DashWidget number = b.Text("Number", 70, y, 54, 33, "=\"#\" + " + S("Number"), "size", "15", "color", "muted");
                DashWidget name = b.Text("Name", 130, y, 330, 33, "=" + S("Name"), "size", "17", "align", "Left", "color", "=" + S("InPits") + " ? $dim : $text");
                DashWidget best = b.Text("Best", 470, y, 140, 33, "=laptime(" + S("BestLap") + ")", "size", "16", "color", "=" + S("BestLap") + " > 0 && abs(" + S("BestLap") + " - SessionBestLap) < 0.0005 ? $purple : $muted");
                DashWidget last = b.Text("Last", 616, y, 120, 33, "=laptime(" + S("LastLap") + ")", "size", "16", "color", "muted");
                DashWidget gap = b.Text("Gap", 740, y, 96, 33, "=" + S("GapText"), "size", "16", "align", "Right");
                b.Visible(S("Exists"), row, place, number, name, best, last, gap);
                b.Group("Driver row", p, row, place, number, name, best, last, gap);
            }

            // Page 3: the engine.
            b.Screen("Engine");
            b.Label("Engine label", 16, 10, 300, "ENGINE");
            b.Tile("Oil temp", 10, 34, 205, 110, "OIL TEMP", "=format(OilTemp, \"0\")", "Solid", "unit", "=TempUnit", "valueSize", "40");
            b.Tile("Water temp", 223, 34, 205, 110, "WATER", "=format(WaterTemp, \"0\")", "Solid", "unit", "=TempUnit", "valueSize", "40");
            b.Tile("Oil pressure", 436, 34, 205, 110, "OIL PRESSURE", "=format(OilPressure, \"0.0\")", "Solid", "unit", "=PressureUnit", "valueSize", "40");
            b.Tile("Voltage", 649, 34, 195, 110, "BATTERY", "=format(Voltage, \"0.0\")", "Solid", "unit", "V", "valueSize", "40");
            b.Panel("Temp graph box", 10, 154, 834, 200, "style", "Solid", "radius", "6");
            b.Label("Temp graph label", 26, 166, 400, "WATER AND OIL · LAST 2 MINUTES");
            b.Add("graph", "Temps", 22, 186, 810, 158, "value", "=WaterTemp", "value2", "=OilTemp", "seconds", "120", "style", "Line", "lineWidth", "2.5", "color", "info", "color2", "warn");
            b.Tile("Rpm", 10, 364, 274, 106, "RPM", "=format(Rpm, \"0\")", "Solid", "valueSize", "40");
            b.Tile("Fuel pressure", 292, 364, 274, 106, "FUEL PRESSURE", "=format(FuelPressure, \"0.0\")", "Solid", "unit", "=PressureUnit", "valueSize", "40");
            b.Tile("Warnings", 574, 364, 270, 106, "WARNINGS", "=EngineWarning ? \"CHECK\" : \"OK\"", "Solid", "valueSize", "40", "valueColor", "=EngineWarning ? $bad : $good");

            PitLane(b, "Solid");
            return b.Layout;
        }
    }
}
