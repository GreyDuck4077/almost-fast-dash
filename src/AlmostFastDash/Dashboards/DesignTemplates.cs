using System;
using System.Collections.Generic;
using System.Linq;

namespace AlmostFastDash.Dashboards
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
    /// Each template is built in its own file under Templates; this file holds the list and what they share.
    /// </summary>
    internal static partial class DesignTemplates
    {
        public static readonly List<DesignTemplate> All = new List<DesignTemplate>
        {
            new DesignTemplate { Id = "roady", Name = "Roady", Accent = "#FF5A1F", Create = Readable(Roady),
                Description = "The road flagship: a sweep of revs over a huge gear, with delta, pedals, bias and fuel." },
            new DesignTemplate { Id = "roundy", Name = "Roundy", Accent = "#3D9BFF", OvalLights = true, Create = Readable(Roundy),
                Description = "The oval flagship: blind-spot arcs, a rev ring, the cars around you and your lap falloff." },
            new DesignTemplate { Id = "halo", Name = "Halo", Accent = "#30D158", Create = Readable(HaloRing),
                Description = "Endurance and multiclass: a rev ring over a live delta, your stint and fuel, and traffic in class colors." },
            new DesignTemplate { Id = "oval", Name = "Oval", Accent = "#FFB020", OvalLights = true, Create = Readable(OvalRadar),
                Description = "Pack racing: a live radar, gaps in car lengths and how fast they're closing, laps to go and fuel." },
            new DesignTemplate { Id = "apex", Name = "Apex", Accent = "#2F8CFF", Create = Readable(Apex),
                Description = "Road and GT: an arched LED rev bar, speed inside a rev ring, the track map and a live graph either side." },
            new DesignTemplate { Id = "twindial", Name = "Twin Dial", Accent = "#5AB4FF", Create = Readable(TwinDial),
                Description = "Two needle dials, rev counter and speedometer, with the map and your lap between them." },
            new DesignTemplate { Id = "neon", Name = "Neon", Accent = "#A855F7", Create = Readable(Neon),
                Description = "A modern digital cluster: tapered neon rings, a huge speed readout and glowing tiles." },
            new DesignTemplate { Id = "ribbon", Name = "Ribbon", Accent = "#38BDF8", Create = Readable(Ribbon),
                Description = "A wide three-panel cluster: timing and pedals, half-dials around the gear, the cars around you." },
            new DesignTemplate { Id = "spotter", Name = "Spotter", Accent = "#FFB020", OvalLights = true, Create = Readable(Spotter),
                Description = "Ovals: full-height blind-spot bars, rev bars meeting in the middle, the relative and the race order." },
            new DesignTemplate { Id = "racedisplay", Name = "Race Display", Accent = "#00E08A", Create = Readable(RaceDisplay),
                Description = "A pro race display: LED row, huge gear, timing columns; a wheel button pages to standings and engine." },
            new DesignTemplate { Id = "blank", Name = "Blank canvas", Accent = "#22D3EE", Create = Readable(Blank),
                Description = "A dark background and nothing else, to build your own." },
        };

        public static DesignTemplate Find(string id) => All.FirstOrDefault(t => t.Id == id);

        /// <summary>A template's build, brought up to the smallest text a wheel screen can show readably.</summary>
        private static Func<DashLayout> Readable(Func<DashLayout> build) => () =>
        {
            DashLayout layout = build();
            ReadableText.Apply(layout);
            return layout;
        };

        /// <summary>The road and oval dashboards a new install starts with.</summary>
        public static DesignTemplate Road => Find("roady");
        public static DesignTemplate Oval => Find("roundy");

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
                Text(name, x, y, w, 20, text, "size", "16", "tracking", "1.4", "weight", "Regular", "color", color, "align", align);

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
        /// <summary>Fuel to add to reach the flag: the amount with its unit, ENOUGH, or -- until it's known; EST while it rests on fewer than three laps.</summary>
        internal const string FuelToAdd = "=FuelToFinish < 0 ? \"--\" : FuelToFinish <= 0.05 ? \"ENOUGH\" : fuel(FuelToFinish)";
        internal const string FuelToAddUnit = "=FuelToFinish > 0.05 ? FuelUnit : \"\"";
        internal const string FuelToAddLabel = "=FuelEstimate && FuelToFinish >= 0 ? \"FUEL TO ADD · EST\" : \"FUEL TO ADD\"";
        internal const string FuelToAddTone = "=FuelToFinish < 0 ? $text : FuelToFinish <= 0.05 ? $good : $warn";
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
            theme.Muted = "#A9B6C8";
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
                "start", "135", "sweep", "270", "thickness", "12", "colorMode", "Shift colors", "color", "good", "warnColor", "bad", "dangerColor", "bad",
                // The red stripe on the rim marks the limit itself (the scale runs to 1.25 times it).
                "warnAt", "0.8", "dangerAt", "0.8", "zones", "true", "rim", "border", "rimWidth", "1",
                "needle", "Marker", "needleColor", "#FFFFFF", "needleLength", "130", "needleWidth", "3", "flash", "=OverPitLimit", "glow", "10");
            b.Text("Pit speed", 307, 150, 240, 120, "=format(Speed, \"0\")", "size", "104", "color", "=OverPitLimit ? $bad : $text", "fxShadow", "8");
            b.Text("Pit limit", 307, 270, 240, 18, "=PitSpeedLimit > 0 ? \"LIMIT \" + format(PitSpeedLimit, \"0\") + \" \" + SpeedUnit : \"PIT LANE\"",
                "size", "12", "tracking", "3", "weight", "Regular", "color", "muted");
            b.Tile("Refuel", 20, 96, 240, 110, FuelToAddLabel, FuelToAdd, panelStyle, "unit", FuelToAddUnit, "valueColor", FuelToAddTone, "valueSize", "40");
            b.Tile("In tank", 20, 216, 240, 90, "IN THE TANK", "=fuel(Fuel)", panelStyle, "unit", "=FuelUnit");
            b.Tile("Stint", 20, 316, 240, 90, "STINT", "=StintLaps + \" LAPS\"", panelStyle);
            b.Panel("Tires panel", 594, 96, 240, 310, "style", panelStyle, "radius", "16");
            b.Label("Tires label", 610, 108, 200, "TIRES · LAST STOP");
            b.Add("tyres", "Tires", 606, 128, 216, 266, "size", "22");
            b.Metric("Pit position", 307, 384, 240, 80, "POSITION", PositionText, "align", "Center", "valueSize", "40", "labelSize", "11", "labelTracking", "2",
                "unit", "=Starters > 0 ? \"/\" + Starters : \"\"", "unitColor", "muted");
        }

        /// <summary>A short race-notice banner (flags, penalties, a personal best), colored by urgency.</summary>
        private static void Notice(Builder b, double x, double y, double w, double h)
        {
            const string tone = "=NoticeKind == \"Danger\" ? $bad : NoticeKind == \"Warning\" ? $warn : NoticeKind == \"Good\" ? $good : $accent";
            b.Visible("Notice != \"\"",
                b.Panel("Notice", x, y, w, h, "style", "Tinted", "tint", tone, "radius", (h / 2).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                b.Text("Notice text", x, y, w, h, "=Notice", "size", (h * 0.42).ToString("0", System.Globalization.CultureInfo.InvariantCulture), "tracking", "1", "color", tone));
        }
    }
}
