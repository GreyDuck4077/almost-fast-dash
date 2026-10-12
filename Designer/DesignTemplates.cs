using System;
using System.Collections.Generic;
using System.Linq;

namespace AlmostFastOverlays
{
    internal sealed class DesignTemplate
    {
        public string Id, Name, Description, Accent;
        public Func<DashLayout> Create;
    }

    /// <summary>
    /// The dashboards, as designs. Halo and Oval share one Liquid Glass language:
    /// frosted panels over an aurora background, a header hung from angled
    /// hairlines, readouts on glass tiles and a delta badge at the bottom.
    /// </summary>
    internal static class DesignTemplates
    {
        public static readonly List<DesignTemplate> All = new List<DesignTemplate>
        {
            new DesignTemplate { Id = "halo", Name = "Halo", Accent = "#22D3EE", Description = "Road and GT: the gear inside a halo of rev lights, with timing and fuel on glass tiles around it.", Create = Halo },
            new DesignTemplate { Id = "oval", Name = "Oval", Accent = "#FFB020", Description = "Ovals: a glass spotter dial with cars alongside, gaps ahead and behind, lap speed and fuel to the end.", Create = Oval },
            new DesignTemplate { Id = "blank", Name = "Blank canvas", Accent = "#22D3EE", Description = "The Liquid Glass background and nothing else, to build your own.", Create = Blank },
        };

        public static DesignTemplate Find(string id) => All.FirstOrDefault(t => t.Id == id);

        public static DashLayout Blank()
        {
            var layout = new DashLayout();
            GlassTheme(layout.Theme);
            layout.Screens.Add(new DashScreen { Name = "Main" });
            return layout;
        }

        // ------------------------------------------------------------------ builder

        private sealed class Builder
        {
            public readonly DashLayout Layout = new DashLayout();
            private DashScreen _screen;

            public Builder() { Screen("Main"); GlassTheme(Layout.Theme); }

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

            public DashWidget Panel(string name, double x, double y, double w, double h, params string[] props) =>
                Add("panel", name, x, y, w, h, props);

            public DashWidget Metric(string name, double x, double y, double w, double h, string label, string value, params string[] props)
            {
                var all = new List<string> { "label", label, "value", value };
                all.AddRange(props);
                return Add("metric", name, x, y, w, h, all.ToArray());
            }

            public DashWidget Text(string name, double x, double y, double w, double h, string text, params string[] props)
            {
                var all = new List<string> { "text", text };
                all.AddRange(props);
                return Add("text", name, x, y, w, h, all.ToArray());
            }

            /// <summary>A small spaced-out caption, the label style used everywhere.</summary>
            public DashWidget Label(string name, double x, double y, double w, string text, string align = "Left", string color = "muted") =>
                Text(name, x, y, w, 12, text, "size", "10", "tracking", "2.5", "weight", "Regular", "color", color, "align", align);
        }

        private const string PositionText = "=Position > 0 ? \"P\" + Position : \"P--\"";
        private const string Racing = "!OnPitRoad", Pit = "OnPitRoad";
        private const string Hairline = "#263243";

        // ------------------------------------------------------------------ shared Liquid Glass pieces

        private static void GlassTheme(LayoutTheme theme)
        {
            theme.BackgroundStyle = "Aurora";
            theme.Background = "#020409";
            theme.Background2 = "#0A1220";
            theme.Font = "Bahnschrift";
            theme.Panel = "#A0141C28";
            theme.PanelTop = "#B01A2433";
            theme.Border = "#222D3B";
            theme.Track = "#151C25";
            theme.Text = "#F2F5F9";
            theme.Muted = "#8593A5";
            theme.Dim = "#3A4452";
            theme.Good = "#2EE07E";
            theme.Bad = "#FF3B4E";
            theme.Warn = "#FFC233";
            theme.Info = "#3FA9FF";
            theme.Purple = "#B57CFF";
        }

        /// <summary>The top: a glass notch of four readouts hung from angled hairlines, session on the left, position on the right.</summary>
        private static void Header(Builder b, string[] labels, string[] tones, string[] values, bool conditions)
        {
            b.Panel("Header notch", 282, 8, 290, 54, "corners", "Cut diagonal", "radius", "12");
            for (int i = 0; i < 4; i++)
                b.Metric(labels[i], 286 + i * 70.5, 9, 70.5, 52, labels[i], values[i], "align", "Center", "valueSize", "22", "labelSize", "10", "labelTracking", "2", "labelColor", tones[i]);
            b.Add("progress", "Race progress", 304, 68, 246, 3, "gap", "1");

            b.Add("shape", "Hairline left", 196, 34, 86, 2, "shape", "Line", "stroke", Hairline, "strokeWidth", "1.5");
            b.Add("shape", "Hairline left angle", 176, 35, 20, 20, "shape", "Diagonal up", "stroke", Hairline, "strokeWidth", "1.5");
            b.Add("shape", "Hairline left base", 18, 54, 158, 2, "shape", "Line", "stroke", Hairline, "strokeWidth", "1.5");
            b.Add("shape", "Hairline right", 572, 34, 86, 2, "shape", "Line", "stroke", Hairline, "strokeWidth", "1.5");
            b.Add("shape", "Hairline right angle", 658, 35, 20, 20, "shape", "Diagonal down", "stroke", Hairline, "strokeWidth", "1.5");
            b.Add("shape", "Hairline right base", 678, 54, 158, 2, "shape", "Line", "stroke", Hairline, "strokeWidth", "1.5");

            b.Text("Session", 20, 8, 170, 14, "=SessionType + \"  ·  \" + RemainingText", "size", "11", "tracking", "1.5", "weight", "Regular", "color", "muted", "align", "Left");
            b.Text("Session clock", 20, 22, 170, 32, "=IsRace && TotalLaps > 0 ? \"LAP \" + LapText : clock(TimeRemaining)", "size", "28", "align", "Left");
            if (conditions)
                b.Text("Conditions", 20, 60, 200, 14, "=\"AIR \" + format(AirTemp, \"0\") + \"°     TRACK \" + format(TrackTemp, \"0\") + \"°\"",
                    "size", "11", "tracking", "1", "weight", "Regular", "color", "muted", "align", "Left");

            b.Text("Position label", 684, 8, 150, 14, "POSITION", "size", "11", "tracking", "2", "weight", "Regular", "color", "muted", "align", "Right");
            b.Text("Places gained", 684, 8, 70, 14, "=(PositionsGained > 0 ? \"▲ \" : \"▼ \") + abs(PositionsGained)", "size", "11", "align", "Left",
                "color", "=PositionsGained > 0 ? $good : $bad", "visible", "PositionsGained != 0");
            b.Metric("Position", 664, 20, 172, 36, "", PositionText, "layout", "Value only", "align", "Right", "inset", "0", "valueSize", "30",
                "unit", "=Starters > 0 ? \"/\" + Starters : \"\"", "unitSize", "17", "unitColor", "dim");
            b.Text("Clock", 684, 60, 150, 14, "=\"LOCAL \" + time(\"HH:mm\")", "size", "11", "tracking", "1", "weight", "Regular", "color", "muted", "align", "Right");
        }

        /// <summary>Caution and last-lap chips just below the header, centred.</summary>
        private static void StateChips(Builder b, double cx, double y)
        {
            b.Panel("Caution chip", cx - 58, y, 116, 24, "corners", "Cut diagonal", "radius", "7", "style", "Tinted", "tint", "warn", "visible", "Caution");
            b.Text("Caution", cx - 58, y, 116, 24, "CAUTION", "size", "12", "tracking", "2", "color", "warn", "visible", "Caution");
            b.Panel("Last lap chip", cx - 58, y, 116, 24, "corners", "Cut diagonal", "radius", "7", "style", "Tinted", "tint", "text", "visible", "!Caution && WhiteFlag");
            b.Text("Last lap", cx - 58, y, 116, 24, "LAST LAP", "size", "12", "tracking", "2", "visible", "!Caution && WhiteFlag");
        }

        /// <summary>On pit road the centre becomes a speed-versus-limit gauge.</summary>
        private static void PitCentre(Builder b, double cx, double cy, double size)
        {
            const string tone = "=OverPitLimit ? $bad : PitLimiter ? $good : $warn";
            b.Add("arc", "Pit speed", cx - size / 2, cy - size / 2, size, size, "value", "=PitSpeedLimit > 0 ? Speed / (PitSpeedLimit * 1.25) : 0", "style", "Smooth",
                "start", "135", "sweep", "270", "thickness", "12", "color", "good", "warnColor", "bad", "dangerColor", "bad", "warnAt", "0.8", "dangerAt", "0.8",
                "flash", "=OverPitLimit", "glow", "10", "visible", Pit);
            b.Panel("Limiter chip", cx - 70, cy - 110, 140, 30, "corners", "Cut diagonal", "radius", "9", "style", "Tinted", "tint", tone, "visible", Pit);
            b.Text("Limiter", cx - 70, cy - 110, 140, 30, "=OverPitLimit ? \"SLOW DOWN\" : PitLimiter ? \"LIMITER ON\" : \"LIMITER OFF\"", "size", "14", "tracking", "1.5",
                "color", tone, "visible", Pit);
            b.Text("Pit speed value", cx - 100, cy - 72, 200, 124, "=format(Speed, \"0\")", "size", "112", "color", "=OverPitLimit ? $bad : $text", "visible", Pit);
            b.Text("Pit limit", cx - 90, cy + 60, 180, 16, "=PitSpeedLimit > 0 ? \"LIMIT \" + format(PitSpeedLimit, \"0\") + \" \" + SpeedUnit : \"PIT LANE\"",
                "size", "11", "tracking", "2.5", "weight", "Regular", "color", "muted", "visible", Pit);
        }

        /// <summary>The delta badge at the bottom centre, glowing green or red, with bars growing out of it.</summary>
        private static void DeltaBadge(Builder b, double wing)
        {
            const string tone = "Delta > 0.005 ? $good : Delta < -0.005 ? $bad : $accent";
            b.Add("bar", "Delta losing", 342 - wing, 429, wing, 4, "value", "=Delta < 0 ? -Delta : 0", "fill", "End to start", "color", "bad", "glow", "6");
            b.Add("bar", "Delta gaining", 512, 429, wing, 4, "value", "=Delta > 0 ? Delta : 0", "color", "good", "glow", "6");
            b.Panel("Delta badge", 342, 398, 170, 66, "corners", "Cut diagonal", "radius", "14", "glow", "12", "glowColor", "=alpha(" + tone + ", 0.45)");
            b.Text("Delta label", 342, 404, 170, 12, "DELTA", "size", "10", "tracking", "3", "weight", "Regular", "color", "text", "shadow", "true");
            b.Text("Delta", 342, 416, 170, 42, "=signed(Delta, 2)", "size", "34", "color", "=deltacolor(Delta)", "glow", "5");
        }

        /// <summary>A car-ahead or car-behind readout: number plate, name, iRating and gap.</summary>
        private static void CarCard(Builder b, bool ahead, double x, double y, double w, double h, double gapSize)
        {
            string side = ahead ? "Ahead" : "Behind";
            string present = side + "Name != \"---\"";
            b.Panel("Car " + side.ToLowerInvariant(), x, y, w, h, "corners", "Cut diagonal", "radius", "12");
            b.Label(side + " label", x + 12, y + 7, 90, ahead ? "AHEAD" : "BEHIND");
            b.Add("shape", side + " plate", x + 12, y + 25, 32, 24, "shape", "Rectangle", "radius", "4", "strokeWidth", "0", "fill", "=" + present + " ? #E6EBF1 : $track");
            b.Text(side + " number", x + 12, y + 25, 32, 24, "=" + side + "Number", "size", "14", "color", "background");
            b.Text(side + " name", x + 52, y + 22, w - 116, 26, "=" + present + " ? " + side + "Name : \"CLEAR\"", "size", "16", "align", "Left", "color", "=" + present + " ? $text : $dim");
            b.Text(side + " rating", x + 52, y + 48, w - 116, 12, "=" + side + "IRating > 0 ? format(" + side + "IRating / 1000, \"0.0\") + \"k iR\" : \"\"",
                "size", "10", "tracking", "1", "weight", "Regular", "color", "muted", "align", "Left");
            b.Text(side + " gap", x + w - 66, y + 4, 56, h - 8, "=" + present + " ? format(" + side + "Gap, \"0.0\") : \"--\"", "size", gapSize.ToString(), "align", "Right",
                "color", ahead ? "good" : "warn");
        }

        private static void EdgeSpotters(Builder b)
        {
            b.Add("blindspot", "Car left", 4, 116, 6, 248, "side", "Left", "glow", "14");
            b.Add("blindspot", "Car right", 844, 116, 6, 248, "side", "Right", "glow", "14");
        }

        // ------------------------------------------------------------------ Halo

        /// <summary>
        /// Road and GT. Everything is composed around one ring centred on the gear: rev
        /// lights curve up its sides and timing and fuel sit on glass tiles that follow
        /// its curve.
        /// </summary>
        private static DashLayout Halo()
        {
            var b = new Builder();
            const double cx = 427, cy = 236;

            b.Add("arc", "Tick ring", cx - 180, cy - 180, 360, 360, "value", "=1", "start", "-60", "sweep", "300", "segments", "75", "thickness", "6",
                "colorMode", "Solid", "color", "#1C2530", "track", "#1C2530", "flash", "", "glow", "0");
            foreach (int angle in new[] { 45, 135, 225, 315 })
                b.Add("arc", "Ring mark", cx - 180, cy - 180, 360, 360, "value", "=1", "style", "Smooth", "start", (angle - 3).ToString(), "sweep", "6",
                    "thickness", "6", "colorMode", "Solid", "color", "accent", "flash", "", "glow", "8");
            b.Add("gradient", "Shift glow", cx - 200, cy - 200, 400, 400, "style", "Radial", "strength", "15",
                "color", "=ShiftFlash ? $bad : mix($accent, $bad, round(clamp((ShiftPct - 0.6) / 0.4, 0, 1) * 8) / 8)");
            b.Add("shape", "Inner ring", cx - 128, cy - 128, 256, 256, "shape", "Ellipse", "stroke", "#1A2230", "strokeWidth", "1.5", "visible", Racing);

            b.Add("arc", "Rev lights left", cx - 166, cy - 166, 332, 332, "value", "=ShiftPct", "start", "125", "sweep", "110", "segments", "18", "thickness", "16",
                "color", "good", "warnAt", "0.6", "dangerAt", "0.85", "glow", "10", "visible", Racing);
            b.Add("arc", "Rev lights right", cx - 166, cy - 166, 332, 332, "value", "=ShiftPct", "start", "55", "sweep", "-110", "segments", "18", "thickness", "16",
                "color", "good", "warnAt", "0.6", "dangerAt", "0.85", "glow", "10", "visible", Racing);

            b.Text("Speed", cx - 60, 110, 120, 36, "=format(Speed, \"0\")", "size", "32", "visible", Racing);
            b.Text("Speed unit", cx - 60, 146, 120, 12, "=SpeedUnit", "size", "10", "tracking", "3", "weight", "Regular", "color", "muted", "visible", Racing);
            b.Add("gear", "Gear", cx - 100, cy - 86, 200, 172, "size", "190", "neighborSize", "28", "neighborColor", "#3A4452", "glow", "14",
                "glowColor", "=alpha($accent, 0.55)", "visible", Racing);
            b.Text("RPM", cx - 60, 320, 120, 28, "=Rpm > 0 ? format(Rpm, \"0\") : \"----\"", "size", "22", "color", "#C9D2DC", "visible", Racing);
            b.Text("RPM label", cx - 60, 348, 120, 12, "RPM", "size", "10", "tracking", "3", "weight", "Regular", "color", "muted", "visible", Racing);
            StateChips(b, cx, 80);
            PitCentre(b, cx, cy, 332);

            Header(b, new[] { "TC", "ABS", "BIAS", "INC" }, new[] { "info", "warn", "bad", "muted" },
                new[] { "=TractionControl < 0 ? \"--\" : format(TractionControl, \"0\")", "=Abs < 0 ? \"--\" : format(Abs, \"0\")", "=BrakeBias > 0 ? format(BrakeBias, \"0.0\") : \"--\"", "=Incidents + \"x\"" },
                true);

            // Readouts on glass tiles, their inner edges following the ring.
            void Orbit(string name, double x, double y, bool left, string label, string value, string color, string tick, string unit = null)
            {
                const double w = 196, h = 64;
                var props = new List<string> { "align", left ? "Right" : "Left", "inset", "18", "labelSize", "11", "labelTracking", "2.5", "valueSize", "30", "valueColor", color };
                if (unit != null) props.AddRange(new[] { "unit", unit, "unitSize", "14" });
                b.Panel(name + " tile", x, y + 2, w, h - 4, "corners", "Cut diagonal", "radius", "14");
                b.Metric(name, x, y, w, h, label, value, props.ToArray());
                b.Add("shape", name + " marker", left ? x + w - 3 : x, y + 14, 3, 40, "shape", "Rectangle", "radius", "1.5", "strokeWidth", "0", "fill", tick);
            }
            const string predicted = "=PredictedLap > 0 && BestLap > 0 ? (PredictedLap <= BestLap ? $good : $warn) : $text";
            Orbit("Last lap", 56, 114, true, "LAST LAP", "=laptime(LastLap)", "=LastLapIsBest ? $purple : $text", "=LastLapIsBest ? $purple : $dim");
            Orbit("Best lap", 32, 204, true, "BEST LAP", "=laptime(BestLap)", "purple", "purple");
            Orbit("Predicted lap", 56, 294, true, "PREDICTED", "=laptime(PredictedLap)", predicted, predicted);

            const string fuelLaps = "=FuelLapsLeft >= 0 && FuelLapsLeft < 2 ? (blink(400) ? $bad : $warn) : steps(FuelLapsLeft, $text, 0, $bad, 2, $warn, 5, $text)";
            Orbit("Fuel", 602, 114, false, "FUEL", "=fuel(Fuel)", "text", "accent", "=FuelUnit");
            Orbit("Fuel laps", 626, 204, false, "LAPS OF FUEL", "=FuelLapsLeft < 0 ? \"--.-\" : format(FuelLapsLeft, \"0.0\")", fuelLaps, fuelLaps);
            const string scale = "max(1, max(FuelLapsLeft, LapsRemaining) * 1.05)";
            b.Add("bar", "Fuel range", 644, 256, 160, 3, "value", "=FuelLapsLeft > 0 ? FuelLapsLeft / " + scale + " : FuelPct",
                "marker", "=LapsRemaining > 0 && FuelLapsLeft > 0 ? LapsRemaining / " + scale + " : 0",
                "color", "=FuelLapsLeft >= 0 && FuelLapsLeft < LapsRemaining ? $warn : $accent", "markerColor", "text");
            const string refuelTone = "=FuelToFinish < 0 ? $text : FuelToFinish <= 0.05 ? $good : $warn";
            Orbit("Refuel", 602, 294, false, "=\"REFUEL   \" + fuel(FuelPerLap, true) + \" / LAP\"",
                "=FuelToFinish < 0 ? \"--.-\" : FuelToFinish <= 0.05 ? \"NONE\" : \"+\" + fuel(FuelToFinish)", refuelTone, refuelTone);

            DeltaBadge(b, 112);
            CarCard(b, false, 18, 394, 204, 68, 26);
            CarCard(b, true, 632, 394, 204, 68, 26);
            EdgeSpotters(b);
            return b.Layout;
        }

        // ------------------------------------------------------------------ Oval

        /// <summary>
        /// Ovals. A glass spotter dial in the middle shows your car and any car
        /// alongside, with the spotter's call above it; the car behind is on the left
        /// and the car ahead on the right, with lap speed and fuel to the end below them.
        /// </summary>
        private static DashLayout Oval()
        {
            var b = new Builder();
            const double cx = 427, cy = 236;
            const string call = "CarLeft && CarRight ? \"THREE WIDE\" : CarLeft ? \"CAR LEFT\" : CarRight ? \"CAR RIGHT\" : \"CLEAR\"";

            // ---------------- the dial
            b.Add("arc", "Tick ring", cx - 162, cy - 162, 324, 324, "value", "=1", "start", "-60", "sweep", "300", "segments", "70", "thickness", "5",
                "colorMode", "Solid", "color", "#1C2530", "track", "#1C2530", "flash", "", "glow", "0");
            b.Add("gradient", "Spotter glow", cx - 190, cy - 190, 380, 380, "style", "Radial", "strength", "16",
                "color", "=CarLeft || CarRight ? $bad : $accent");
            b.Add("arc", "Rev lights", cx - 158, cy - 158, 316, 316, "value", "=ShiftPct", "start", "212", "sweep", "116", "segments", "20", "thickness", "8",
                "color", "good", "warnAt", "0.6", "dangerAt", "0.85", "glow", "8", "visible", Racing);
            b.Panel("Spotter dial", cx - 142, cy - 142, 284, 284, "radius", "142", "visible", Racing);
            b.Text("Spotter call", cx - 120, 112, 240, 34, "=" + call, "size", "24", "tracking", "1", "color", "=CarLeft || CarRight ? $bad : $good", "visible", Racing);
            b.Add("shape", "Lane left", cx - 46, 154, 2, 120, "shape", "Line", "stroke", "#26FFFFFF", "strokeWidth", "1.5", "visible", Racing);
            b.Add("shape", "Lane right", cx + 44, 154, 2, 120, "shape", "Line", "stroke", "#26FFFFFF", "strokeWidth", "1.5", "visible", Racing);
            b.Add("shape", "Your car", cx - 26, 158, 52, 112, "shape", "Rectangle", "radius", "20", "fill", "=alpha($accent, 0.28)", "stroke", "accent", "strokeWidth", "2.5", "glow", "8", "visible", Racing);
            b.Add("blindspot", "Car left zones", cx - 98, 154, 30, 120, "side", "Left", "shape", "Zones", "glow", "14", "visible", Racing);
            b.Add("blindspot", "Car right zones", cx + 68, 154, 30, 120, "side", "Right", "shape", "Zones", "glow", "14", "visible", Racing);
            b.Add("gear", "Gear", cx - 92, 282, 60, 56, "size", "40", "neighbors", "false", "animate", "false", "glow", "8", "glowColor", "=alpha($accent, 0.5)", "visible", Racing);
            b.Text("Speed", cx - 34, 282, 98, 56, "=format(Speed, \"0\")", "size", "40", "align", "Right", "visible", Racing);
            b.Text("Speed unit", cx + 68, 300, 40, 20, "=SpeedUnit", "size", "10", "tracking", "1.5", "weight", "Regular", "color", "muted", "align", "Left", "visible", Racing);
            StateChips(b, cx, 80);
            PitCentre(b, cx, cy, 316);

            Header(b, new[] { "INC", "AIR", "TRACK", "LAP SPD" }, new[] { "muted", "info", "warn", "accent" },
                new[] { "=Incidents + \"x\"", "=format(AirTemp, \"0\") + \"°\"", "=format(TrackTemp, \"0\") + \"°\"", "=LastLapSpeed > 0 ? format(LastLapSpeed, \"0.0\") : \"---\"" },
                false);

            // ---------------- left: the car behind, then lap times
            CarCard(b, false, 22, 92, 234, 112, 40);
            b.Panel("Laps tile", 22, 216, 234, 168, "corners", "Cut diagonal", "radius", "14");
            b.Label("Last label", 38, 228, 100, "LAST LAP");
            b.Text("Last lap", 36, 240, 140, 36, "=laptime(LastLap)", "size", "26", "align", "Left", "color", "=LastLapIsBest ? $purple : $text");
            b.Text("Last speed", 166, 240, 76, 36, "=LastLapSpeed > 0 ? format(LastLapSpeed, \"0.0\") : \"\"", "size", "18", "align", "Right", "color", "muted");
            b.Label("Best label", 38, 286, 100, "BEST LAP");
            b.Text("Best lap", 36, 298, 140, 36, "=laptime(BestLap)", "size", "26", "align", "Left", "color", "purple");
            b.Label("Current label", 38, 344, 100, "CURRENT");
            b.Text("Current lap", 36, 352, 206, 28, "=laptime(CurrentLap)", "size", "20", "align", "Left", "color", "warn");

            // ---------------- right: the car ahead, then fuel to the end
            CarCard(b, true, 598, 92, 234, 112, 40);
            const string plan = "FuelLapsLeft < 0 || LapsRemaining < 0 ? $muted : FuelToFinish <= 0.05 ? $good : $warn";
            b.Panel("Fuel tile", 598, 216, 234, 168, "corners", "Cut diagonal", "radius", "14");
            b.Label("Fuel laps label", 614, 228, 120, "LAPS OF FUEL");
            b.Text("Fuel laps", 612, 240, 120, 44, "=FuelLapsLeft < 0 ? \"--.-\" : format(FuelLapsLeft, \"0.0\")", "size", "34", "align", "Left",
                "color", "=FuelLapsLeft >= 0 && FuelLapsLeft < 2 ? (blink(400) ? $bad : $warn) : steps(FuelLapsLeft, $text, 0, $bad, 2, $warn, 5, $text)");
            b.Text("Fuel", 712, 246, 106, 36, "=fuel(Fuel) + \" \" + FuelUnit", "size", "18", "align", "Right", "color", "muted");
            const string scale = "max(1, max(FuelLapsLeft, LapsRemaining) * 1.05)";
            b.Add("bar", "Fuel range", 614, 296, 202, 4, "value", "=FuelLapsLeft > 0 ? FuelLapsLeft / " + scale + " : FuelPct",
                "marker", "=LapsRemaining > 0 && FuelLapsLeft > 0 ? LapsRemaining / " + scale + " : 0", "color", "=FuelToFinish > 0.05 ? $warn : $accent", "markerColor", "text");
            b.Add("shape", "Fuel plan chip", 614, 318, 202, 48, "shape", "Rectangle", "radius", "10", "strokeWidth", "0", "fill", "=alpha(" + plan + ", 0.14)");
            b.Text("Fuel plan", 614, 318, 202, 48, "=FuelLapsLeft < 0 || LapsRemaining < 0 ? \"NEEDS A CLEAN LAP\" : FuelToFinish <= 0.05 ? \"MAKES THE FINISH\" : \"PIT IN \" + floor(FuelLapsLeft) + \" · ADD \" + fuel(FuelToFinish)",
                "size", "13", "tracking", "1", "color", "=" + plan);

            // ---------------- bottom
            DeltaBadge(b, 76);
            b.Panel("Laps to go tile", 22, 396, 234, 66, "corners", "Cut diagonal", "radius", "12");
            b.Label("Laps to go label", 38, 404, 140, "LAPS TO GO");
            b.Text("Laps to go", 36, 418, 206, 38, "=LapsRemaining < 0 ? \"--\" : format(LapsRemaining, \"0\")", "size", "28", "align", "Left");
            b.Panel("Gained tile", 598, 396, 234, 66, "corners", "Cut diagonal", "radius", "12");
            b.Label("Gained label", 614, 404, 140, "FROM THE START");
            b.Text("Gained", 612, 418, 206, 38, "=StartPosition > 0 ? (PositionsGained > 0 ? \"▲ \" + PositionsGained : PositionsGained < 0 ? \"▼ \" + abs(PositionsGained) : \"±0\") + \"   from P\" + StartPosition : \"--\"",
                "size", "22", "align", "Left", "color", "=PositionsGained > 0 ? $good : PositionsGained < 0 ? $bad : $text");
            EdgeSpotters(b);
            return b.Layout;
        }
    }
}
