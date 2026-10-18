using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;

namespace AlmostFastDash.Rendering
{
    internal static partial class WidgetPainters
    {
        // ================================================================== Racing

        public static void Relative(WidgetContext c)
        {
            Graphics g = c.G;
            RectangleF b = c.B;
            int rows = c.Choice("rows") == "3" ? 3 : 5;
            RelativeEntry[] all = c.T.Relatives ?? new RelativeEntry[0];
            int player = Array.FindIndex(all, r => r.IsPlayer);
            float size = (float)c.Num("size");
            if (player < 0)
            {
                Paint.Text(g, c, "Waiting for cars", size * 0.85f, false, c.Theme("muted"), b, StringAlignment.Center, "Middle", true, 0,
                    Color.Empty, false);
                return;
            }
            float rowH = b.Height / rows;
            int half = rows / 2;
            for (int slot = 0; slot < rows; slot++)
            {
                int index = player + slot - half;
                if (index < 0 || index >= all.Length) continue;
                RelativeRow(c, all[index], new RectangleF(b.X, b.Y + slot * rowH + 2, b.Width, rowH - 4), size);
            }
        }

        private static void RelativeRow(WidgetContext c, RelativeEntry r, RectangleF b, float size)
        {
            Graphics g = c.G;
            Color accent = c.Color("highlight"), text = c.Theme("text"), track = c.Theme("track"), muted = c.Theme("muted");
            if (r.IsPlayer)
            {
                DashKit.Fill(g, b, Color.FromArgb(40, accent), 8);
                DashKit.Fill(g, new RectangleF(b.X, b.Y + b.Height * 0.2f, 3, b.Height * 0.6f), accent, 1.5f);
            }
            Color nameColor = r.IsPlayer ? text : r.InPits ? muted
                : r.LapRelation > 0 ? c.Color("lappingColor") : r.LapRelation < 0 ? c.Color("lappedColor") : text;
            float boxH = Math.Min(26, b.Height - 6), x = b.X + 8;
            float boxY = b.Y + (b.Height - boxH) / 2;
            Color background = c.Theme("background");
            if (c.Bool("showPosition"))
            {
                var pos = new RectangleF(x, boxY, boxH * 1.15f, boxH);
                DashKit.Fill(g, pos, r.IsPlayer ? accent : track, 6);
                Paint.Text(g, c, r.Position > 0 ? r.Position.ToString() : "-", boxH * 0.58f, true, r.IsPlayer ? background : text, pos,
                    StringAlignment.Center, "Middle", true, 0, Color.Empty, false);
                x = pos.Right + 6;
            }
            if (c.Bool("showNumber"))
            {
                var number = new RectangleF(x, boxY, boxH * 1.25f, boxH);
                DashKit.Fill(g, number, Color.FromArgb(226, 232, 240), 6);
                Paint.Text(g, c, r.CarNumber ?? "", Math.Max(c.MinDenseText, boxH * 0.5f), true,
                    DashboardAudit.InkOn(Color.FromArgb(226, 232, 240)), number, StringAlignment.Center, "Middle", true, 0, Color.Empty,
                    false);
                x = number.Right + 8;
            }
            float gapW = size * 3.4f;
            bool license = c.Bool("showLicense") && !string.IsNullOrEmpty(r.License);
            var nameBox = new RectangleF(x, b.Y, Math.Max(10, b.Right - x - gapW), license ? b.Height * 0.52f : b.Height);
            Paint.Text(g, c, r.IsPlayer ? "YOU" : r.Name, size, true, nameColor, nameBox, StringAlignment.Near,
                license ? "Bottom" : "Middle", true, 0, Color.Empty, false);
            if (license)
            {
                float lh = Math.Max(c.MinDenseText / 0.68f, size * 0.9f);
                var lic = new RectangleF(x, b.Y + b.Height * 0.54f, lh * 2.6f, lh);
                DashKit.Fill(g, lic, Color.FromArgb(170, r.LicenseColor), 4);
                Paint.Text(g, c, r.License + " " + DashKit.IRating(r.IRating), lh * 0.68f, true, text, Paint.Grow(lic, -3, 0),
                    StringAlignment.Center, "Middle", true, 0, Color.Empty, false);
                if (r.InPits)
                    Paint.Text(g, c, "PIT", lh * 0.75f, true, c.Theme("warn"), new RectangleF(lic.Right + 6, lic.Y, 40, lic.Height),
                        StringAlignment.Near, "Middle", false, 0, Color.Empty, false);
            }
            if (!r.IsPlayer)
                Paint.Text(g, c, Math.Abs(r.Gap).ToString("0.0"), size * 1.15f, true,
                    r.Gap < 0 ? c.Color("aheadColor") : c.Color("behindColor"),
                    new RectangleF(b.Right - gapW, b.Y, gapW - 8, b.Height), StringAlignment.Far, "Middle", true, 0, Color.Empty, false);
        }

        public static void Standings(WidgetContext c)
        {
            Graphics g = c.G;
            RectangleF b = c.B;
            float size = (float)c.Num("size");
            StandingsEntry[] all = c.T.Standings ?? new StandingsEntry[0];
            bool classOnly = c.Bool("classOnly");
            StandingsEntry me = all.FirstOrDefault(r => r.IsPlayer);
            StandingsEntry[] field = classOnly && me != null ? all.Where(r => r.ClassName == me.ClassName).OrderBy(r => r.ClassPosition)
                .ToArray() : all;
            if (field.Length == 0)
            {
                Paint.Text(g, c, "Waiting for the leaderboard", size * 0.85f, false, c.Theme("muted"), b, StringAlignment.Center, "Middle",
                    true, 0, Color.Empty, false);
                return;
            }

            // Which cars to show: the top of the field, a window around you, or the top with your row kept in view.
            int rows = Math.Max(1, Math.Min(30, (int)c.Num("rows")));
            int mine = Array.IndexOf(field, me);
            List<StandingsEntry> shown;
            switch (c.Choice("focus"))
            {
                case "Around me":
                    int first = mine < 0 ? 0 : Math.Max(0, Math.Min(field.Length - rows, mine - rows / 2));
                    shown = field.Skip(first).Take(rows).ToList();
                    break;
                case "Top of the field":
                    shown = field.Take(rows).ToList();
                    break;
                default:
                    shown = field.Take(rows).ToList();
                    if (mine >= rows && rows >= 2) shown[rows - 1] = me;
                    break;
            }

            float gap = (float)c.Num("gap"), rowH = (b.Height - gap * (rows - 1)) / rows;
            for (int i = 0; i < shown.Count; i++)
                StandingsRow(c, shown[i], new RectangleF(b.X, b.Y + i * (rowH + gap), b.Width, rowH), size, i, classOnly);
        }

        private static void StandingsRow(WidgetContext c, StandingsEntry r, RectangleF b, float size, int index, bool classOnly)
        {
            Graphics g = c.G;
            float radius = (float)c.Num("radius");
            Color text = c.Color("textColor"), accent = c.Color("highlight");
            Color background = index % 2 == 1 ? c.OptionalColor("altRowColor") ?? c.Color("rowColor") : c.Color("rowColor");
            if (r.IsPlayer)
            {
                DashKit.Fill(g, b, Color.FromArgb(55, accent), radius);
                DashKit.Fill(g, new RectangleF(b.X, b.Y + b.Height * 0.2f, 3, b.Height * 0.6f), accent, 1.5f);
            }
            else if (background.A > 0) DashKit.Fill(g, b, background, radius);
            // Behind a row: its own fill, or the highlight over the panel for yours.
            Color under = r.IsPlayer ? Color.FromArgb(255, Mix(c.Theme("background"), accent, 0.22f)) : background.A > 0
                ? Color.FromArgb(255, background) : c.Theme("background");
            Color nameColor = DashboardAudit.Readable(r.InPits ? c.Color("pitColor") : text, under, 6);
            float boxH = Math.Min(size * 1.6f, b.Height - 4), boxY = b.Y + (b.Height - boxH) / 2, x = b.X + 8;

            if (c.Bool("showPosition"))
            {
                var pos = new RectangleF(x, boxY, boxH * 1.2f, boxH);
                Paint.Text(c.G, c, (classOnly ? r.ClassPosition : r.Position).ToString(), size, true,
                    DashboardAudit.Readable(r.IsPlayer ? accent : text, under, 6), pos, StringAlignment.Center, "Middle", true, 0,
                    Color.Empty, false);
                x = pos.Right + 4;
            }
            if (c.Bool("showClass"))
            {
                DashKit.Fill(g, new RectangleF(x, boxY + 2, 4, boxH - 4), r.ClassColor, 2);
                x += 10;
            }
            if (c.Bool("showNumber"))
            {
                var number = new RectangleF(x, boxY, boxH * 1.4f, boxH);
                DashKit.Fill(g, number, Color.FromArgb(226, 232, 240), 5);
                Paint.Text(g, c, r.CarNumber ?? "", Math.Max(c.MinDenseText, boxH * 0.55f), true,
                    DashboardAudit.InkOn(Color.FromArgb(226, 232, 240)), number, StringAlignment.Center, "Middle", true, 0, Color.Empty,
                    false);
                x = number.Right + 8;
            }

            // The right-hand value, then iRating, then the name takes what is left.
            string column = c.Choice("column");
            string value;
            Color valueColor = text;
            switch (column)
            {
                case "Interval": value = r.Position == 1 ? "LEADER" : r.LapsBehind > 0 && r.Interval <= 0 ? RaceFields.GapText(r)
                    : "+" + r.Interval.ToString("0.00"); break;
                case "Delta to me": value = r.IsPlayer ? ""
                    : (r.DeltaToPlayer > 0 ? "+" : "") + r.DeltaToPlayer.ToString("0.00"); valueColor = r.IsPlayer ? text
                    : r.DeltaToPlayer < 0 ? c.Theme("good") : c.Theme("warn"); break;
                case "Last lap": value = Fmt.LapTime(r.LastLap); break;
                case "Best lap":
                    value = Fmt.LapTime(r.BestLap);
                    if (r.BestLap > 0 && Math.Abs(r.BestLap - c.T.SessionBestLapSeconds) < 0.0005) valueColor = c.Color("bestColor");
                    break;
                case "Laps": value = r.Laps.ToString(); break;
                default: value = RaceFields.GapText(r); break;
            }
            float valueW = size * 4.6f, right = b.Right - 8;
            Paint.Text(g, c, value, size, true, valueColor, new RectangleF(right - valueW, b.Y, valueW, b.Height), StringAlignment.Far,
                "Middle", true, 0, Color.Empty, false);
            right -= valueW + 6;
            if (c.Bool("showIRating"))
            {
                float w = size * 2.6f;
                Paint.Text(g, c, DashKit.IRating(r.IRating), Math.Max(c.MinDenseText, size * 0.8f), false, c.Theme("muted"),
                    new RectangleF(right - w, b.Y, w, b.Height), StringAlignment.Far, "Middle", true, 0, Color.Empty, false);
                right -= w + 6;
            }
            bool car = c.Bool("showCar") && !string.IsNullOrEmpty(r.CarName);
            var nameBox = new RectangleF(x, b.Y, Math.Max(10, right - x), car ? b.Height * 0.6f : b.Height);
            Paint.Text(g, c, r.IsPlayer ? (r.Name ?? "YOU") : r.Name, size, true, nameColor, nameBox, StringAlignment.Near,
                car ? "Bottom" : "Middle", true, 0, Color.Empty, false);
            if (car)
                Paint.Text(g, c, r.CarName, Math.Max(c.MinDenseText, size * 0.65f), false, c.Theme("muted"),
                    new RectangleF(x, b.Y + b.Height * 0.6f, nameBox.Width, b.Height * 0.38f), StringAlignment.Near, "Top", true, 0,
                    Color.Empty, false);
        }

        public static void Tyres(WidgetContext c)
        {
            Graphics g = c.G;
            RectangleF b = c.B;
            TyreCorner[] tyres = c.T.Tyres ?? new TyreCorner[0];
            float gap = Math.Max(4, Math.Min(b.Width, b.Height) * 0.04f);
            float w = (b.Width - gap) / 2, h = (b.Height - gap) / 2;
            string[] corners = { "LF", "RF", "LR", "RR" };
            for (int i = 0; i < 4; i++)
            {
                var cell = new RectangleF(b.X + (i % 2) * (w + gap), b.Y + (i / 2) * (h + gap), w, h);
                TyreCell(c, tyres.FirstOrDefault(t => t.Name == corners[i]), corners[i], cell);
            }
        }

        private static void TyreCell(WidgetContext c, TyreCorner tyre, string name, RectangleF b)
        {
            Graphics g = c.G;
            float radius = (float)c.Num("radius"), size = (float)c.Num("size");
            Color label = c.Color("labelColor");
            DashKit.Fill(g, b, c.Color("panelColor"), radius);
            double cool = c.Num("coolBelow"), hot = c.Num("hotAbove");
            // The limits are in the temperature unit you chose.
            Color Heat(double celsius) => double.IsNaN(celsius) ? c.Theme("track") : c.Scope.Units.Temperature(celsius) < cool
                ? c.Color("coolColor") : c.Scope.Units.Temperature(celsius) > hot ? c.Color("hotColor") : c.Color("okColor");
            string Temp(double celsius) => double.IsNaN(celsius) ? "--" : c.Scope.Units.Temperature(celsius).ToString("0") + "°";

            var lines = new List<string>();
            if (c.Bool("showWear")) lines.Add(tyre == null || double.IsNaN(tyre.Wear) ? "--%" : (tyre.Wear * 100).ToString("0") + "%");
            if (c.Bool("showPressure"))
            {
                double p = tyre == null ? double.NaN : RaceFields.Pressure(c.Scope.Units, tyre.PressureKpa / 100);
                lines.Add(double.IsNaN(p) ? "--" : p.ToString(c.Scope.Units.Metric ? "0.00" : "0.0") + " " +
                    RaceFields.PressureLabel(c.Scope.Units)
                    .ToLowerInvariant());
            }
            bool temps = c.Bool("showTemps");
            float pad = Math.Max(6, b.Height * 0.06f), y = b.Y + pad;
            float detailSize = Math.Max(c.MinDenseText, size * 0.55f), detailHeight = detailSize * 1.25f;
            // The detail line only where it fits whole below the temperature and tread (never cut off at the
            // cell's edge); the tread strips take the room that's left.
            float below = b.Bottom - pad - (temps ? y + size * 1.4f + 4 + 4 : y);
            bool details = lines.Count > 0 && below >= detailHeight;

            Paint.Text(g, c, name, Math.Max(c.MinText, size * 0.5f), false, label,
                new RectangleF(b.X + pad, y, b.Width - pad * 2, Math.Max(c.MinText, size * 0.5f) * 1.2f), StringAlignment.Near, "Top",
                true, 0, Color.Empty, false);
            if (temps)
            {
                double avg = tyre?.Temp ?? double.NaN;
                Paint.Text(g, c, Temp(avg), size, true, double.IsNaN(avg) ? label : Heat(avg),
                    new RectangleF(b.X + pad, y, b.Width - pad * 2, size * 1.3f), StringAlignment.Far, "Top", true, 0, Color.Empty, false);
                y += size * 1.4f;
                // The tread from left to right as seen from behind the car: outer edge first on the left-hand tyres.
                bool left = name[0] == 'L';
                double[] across = tyre == null ? new[] { double.NaN, double.NaN, double.NaN }
                    : left ? new[] { tyre.TempOuter, tyre.TempMiddle, tyre.TempInner }
                        : new[] { tyre.TempInner, tyre.TempMiddle, tyre.TempOuter };
                float room = b.Bottom - pad - y - (details ? detailHeight + 4 : 0);
                float stripH = Math.Max(4, Math.Min(size * 0.45f, room)), seg = (b.Width - pad * 2 - 8) / 3;
                for (int k = 0; k < 3; k++)
                    DashKit.Fill(g, new RectangleF(b.X + pad + k * (seg + 4), y, seg, stripH), Heat(across[k]), Math.Min(3, stripH / 2));
                y += stripH + 4;
            }
            if (details)
                Paint.Text(g, c, string.Join("  ", lines), detailSize, false, label,
                    new RectangleF(b.X + pad, y, b.Width - pad * 2, Math.Max(detailHeight, b.Bottom - pad - y)), StringAlignment.Near,
                    "Middle", true, 0, Color.Empty, false);
        }

        public static void BlindSpot(WidgetContext c)
        {
            Graphics g = c.G;
            RectangleF b = c.B;
            BlindSpotZone zone = c.Sample ? BlindSpotZone.Beside : c.Choice("side") == "Right" ? c.T.RightBlindSpot : c.T.LeftBlindSpot;
            Color color = c.Color("color"), off = c.Color("offColor");
            float glow = (float)c.Num("glow");
            float radius = Math.Min(b.Width, b.Height) / 2;
            var tool = new ShapeTool(c, b.Height >= b.Width);
            if (c.Choice("shape") == "Zones")
            {
                float gap = 4, h = (b.Height - gap * 2) / 3;
                for (int i = 0; i < 3; i++)
                {
                    var seg = new RectangleF(b.X, b.Y + i * (h + gap), b.Width, h);
                    // Beside lights all three; front and rear light their end.
                    bool on = zone == BlindSpotZone.Beside || (zone == BlindSpotZone.Front && i == 0) ||
                        (zone == BlindSpotZone.Rear && i == 2);
                    float r = Math.Min(seg.Width, seg.Height) / 2;
                    if (on && glow > 0) using (GraphicsPath p = tool.Rect(seg, r)) Paint.Glow(g, p, color, glow);
                    tool.Fill(g, seg, on ? color : off, r);
                }
                return;
            }
            tool.Fill(g, b, off, radius);
            if (zone == BlindSpotZone.None) return;
            float focus = zone == BlindSpotZone.Front ? 0.15f : zone == BlindSpotZone.Rear ? 0.85f : 0.5f;
            using (GraphicsPath path = tool.Rect(b, radius))
            {
                if (glow > 0) Paint.Glow(g, path, color, glow);
                using (var brush = new LinearGradientBrush(Paint.Grow(path.GetBounds(), 0, 1), color, color, 90f))
                {
                    var blend = new ColorBlend
                    {
                        Positions = new[] { 0f, Math.Max(0.001f, focus - 0.35f), focus, Math.Min(0.999f, focus + 0.35f), 1f },
                        Colors = new[] { Color.FromArgb(60, color), Color.FromArgb(120, color), color, Color.FromArgb(120, color),
                        Color.FromArgb(60, color) }
                    };
                    brush.InterpolationColors = blend;
                    g.FillPath(brush, path);
                }
            }
        }

        public static void TrackMapWidget(WidgetContext c)
        {
            Graphics g = c.G;
            RectangleF b = c.B;
            TelemetrySnapshot t = c.T;
            TrackMap map = t.TrackMap;
            float carSize = (float)c.Num("carSize"), width = (float)c.Num("lineWidth");
            if (map == null || !map.Valid)
            {
                string note = !t.Connected ? "Track map" : t.MapProgress >= 0 ? "Drawing the map… " + (t.MapProgress * 100)
                    .ToString("0") + "%" : "Drive a clean lap to draw the map";
                using (var pen = new Pen(c.Color("trackColor"), width) { DashStyle = DashStyle.Dash })
                    g.DrawEllipse(pen, RectangleF.Inflate(b, -width - carSize, -width - carSize));
                Paint.Text(g, c, note, Math.Max(10, Math.Min(18, b.Height / 10)), false, c.Theme("muted"), b, StringAlignment.Center,
                    "Middle", true, 0, Color.Empty, false);
                return;
            }

            // Fit the loop into the box, keeping its proportions, with room for the car dots.
            float minX = map.X.Min(), maxX = map.X.Max(), minY = map.Y.Min(), maxY = map.Y.Max();
            float room = carSize + width / 2 + 2;
            float scale = Math.Min((b.Width - room * 2) / Math.Max(1, maxX - minX), (b.Height - room * 2) / Math.Max(1, maxY - minY));
            float ox = b.X + (b.Width - (maxX - minX) * scale) / 2, oy = b.Y + (b.Height - (maxY - minY) * scale) / 2;
            // North up: the trace's y grows northward, the screen's downward.
            PointF Place(PointF p) => new PointF(ox + (p.X - minX) * scale, oy + (maxY - p.Y) * scale);

            var loop = new PointF[TrackMap.Points];
            for (int i = 0; i < loop.Length; i++) loop[i] = Place(new PointF(map.X[i], map.Y[i]));
            using (var path = new GraphicsPath())
            {
                path.AddPolygon(loop);
                Color edge = c.Color("edgeColor");
                if (edge.A > 0) using (var pen = new Pen(edge, width + 3) { LineJoin = LineJoin.Round }) g.DrawPath(pen, path);
                using (var pen = new Pen(c.Color("trackColor"), width) { LineJoin = LineJoin.Round }) g.DrawPath(pen, path);
            }
            if (c.Bool("showStart"))
            {
                PointF a = loop[0], next = loop[2];
                float dx = next.X - a.X, dy = next.Y - a.Y, len = (float)Math.Sqrt(dx * dx + dy * dy);
                if (len > 0)
                    using (var pen = new Pen(c.Theme("text"), 2.5f))
                        g.DrawLine(pen, a.X - dy / len * width * 1.3f, a.Y + dx / len * width * 1.3f, a.X + dy / len * width * 1.3f,
                            a.Y - dx / len * width * 1.3f);
            }

            float[] dist = t.CarLapDist ?? new float[0];
            var byCar = (t.Standings ?? new StandingsEntry[0]).ToDictionary(r => r.CarIdx);
            byCar.TryGetValue(t.PlayerCarIdx, out StandingsEntry me);
            string show = c.Choice("cars");
            bool classColors = c.Bool("classColors"), numbers = c.Bool("numbers");
            Color others = c.Color("carColor"), player = c.Color("playerColor"), pits = c.Color("pitColor");
            void Car(int index, Color color, float size, bool glow)
            {
                PointF p = Place(map.At(dist[index]));
                var dot = new RectangleF(p.X - size / 2, p.Y - size / 2, size, size);
                if (glow) using (GraphicsPath ring = Paint.Rounded(dot, size / 2)) Paint.Glow(g, ring, color, size * 0.8f);
                using (var brush = new SolidBrush(color)) g.FillEllipse(brush, dot);
                using (var pen = new Pen(Color.FromArgb(200, c.Theme("background")), 1.5f)) g.DrawEllipse(pen, dot);
                if (numbers && size * 0.5f >= c.MinDenseText && byCar.TryGetValue(index, out StandingsEntry r))
                    Paint.Text(g, c, r.CarNumber ?? "", size * 0.5f, true, c.Theme("background"), dot, StringAlignment.Center, "Middle",
                        true, 0, Color.Empty, false);
            }
            if (show != "Only me")
                for (int i = 0; i < dist.Length; i++)
                {
                    if (i == t.PlayerCarIdx || dist[i] < 0) continue;
                    byCar.TryGetValue(i, out StandingsEntry r);
                    if (r == null && t.Standings != null && t.Standings.Length > 0) continue;
                    if (show == "My class" && me != null && r != null && r.ClassName != me.ClassName) continue;
                    Color color = r != null && r.InPits ? pits : classColors && r != null ? r.ClassColor : others;
                    Car(i, color, carSize, false);
                }
            if (t.PlayerCarIdx >= 0 && t.PlayerCarIdx < dist.Length && dist[t.PlayerCarIdx] >= 0) Car(t.PlayerCarIdx, player,
                carSize * 1.3f, true);
        }

        public static void Flag(WidgetContext c)
        {
            RacingFlag flag = c.Sample ? RacingFlag.Yellow : c.T.Flag;
            if (flag == RacingFlag.None) return;
            Graphics g = c.G;
            RectangleF b = c.B;
            float radius = (float)c.Num("radius");
            string name = flag == RacingFlag.BlueYellowStripe ? "BLUE" : flag == RacingFlag.SafetyCar ? "SAFETY CAR" : flag.ToString()
                .ToUpperInvariant();
            Color main, ink;
            switch (flag)
            {
                case RacingFlag.Green: main = Color.FromArgb(30, 205, 70); ink = Color.Black; break;
                case RacingFlag.White: main = Color.White; ink = Color.Black; break;
                case RacingFlag.Yellow: main = Color.FromArgb(255, 220, 0); ink = Color.Black; break;
                case RacingFlag.Red: main = Color.FromArgb(225, 20, 35); ink = Color.White; break;
                case RacingFlag.Meatball: main = Color.FromArgb(245, 95, 30); ink = Color.White; break;
                case RacingFlag.BlueYellowStripe: main = Color.FromArgb(35, 115, 210); ink = Color.White; break;
                case RacingFlag.Surface: main = Color.FromArgb(255, 220, 0); ink = Color.Black; break;
                case RacingFlag.SafetyCar: main = Color.White; ink = Color.Black; break;
                default: main = Color.Black; ink = Color.White; break;
            }

            if (c.Choice("style") == "Badge")
            {
                DashKit.Fill(g, b, Color.FromArgb(230, 10, 12, 16), radius);
                float d = b.Height * 0.5f;
                var dot = new RectangleF(b.X + b.Height * 0.25f, b.Y + (b.Height - d) / 2, d, d);
                using (GraphicsPath p = Paint.Rounded(dot, d / 2)) Paint.Glow(g, p, main, 10);
                DashKit.Fill(g, dot, flag == RacingFlag.Black ? Color.FromArgb(40, 40, 40) : main, d / 2);
                if (c.Bool("label"))
                    Paint.Text(g, c, name, b.Height * 0.42f, true, Color.White,
                        new RectangleF(dot.Right + 10, b.Y, b.Right - dot.Right - 18, b.Height), StringAlignment.Near, "Middle", true, 0,
                        Color.Empty, false);
                return;
            }

            GraphicsState state = g.Save();
            using (GraphicsPath clip = Paint.Rounded(b, radius))
            {
                g.SetClip(clip, CombineMode.Intersect);
                if (flag == RacingFlag.Checkered)
                {
                    float cell = Math.Max(6, b.Height / 4);
                    for (float y = b.Y; y < b.Bottom; y += cell)
                        for (float x = b.X; x < b.Right; x += cell)
                            g.FillRectangle((((int)((x - b.X) / cell) + (int)((y - b.Y) / cell)) & 1) == 0 ? Brushes.White : Brushes.Black,
                                x, y, cell + 1, cell + 1);
                }
                else if (flag == RacingFlag.Meatball)
                {
                    g.FillRectangle(Brushes.Black, b);
                    float d = b.Height * 0.7f;
                    using (var orange = new SolidBrush(main)) g.FillEllipse(orange, b.X + (b.Width - d) / 2, b.Y + (b.Height - d) / 2, d,
                        d);
                }
                else
                {
                    using (var brush = new SolidBrush(main)) g.FillRectangle(brush, b);
                    if (flag == RacingFlag.BlueYellowStripe)
                        using (var stripe = new Pen(Color.FromArgb(255, 220, 0), b.Height * 0.3f)) g.DrawLine(stripe, b.X, b.Bottom,
                            b.Right, b.Y);
                    if (flag == RacingFlag.Surface)
                        for (float x = b.X; x < b.Right; x += b.Height * 0.5f)
                            if (((int)((x - b.X) / (b.Height * 0.5f)) & 1) == 1)
                                using (var red = new SolidBrush(Color.FromArgb(220, 25, 35))) g.FillRectangle(red, x, b.Y, b.Height * 0.5f,
                                    b.Height);
                    if (flag == RacingFlag.Black)
                        using (var pen = new Pen(Color.White, 3)) g.DrawPath(pen, clip);
                }
            }
            g.Restore(state);
            if (c.Bool("label") && flag != RacingFlag.Meatball)
            {
                bool backdrop = flag == RacingFlag.Checkered || flag == RacingFlag.BlueYellowStripe || flag == RacingFlag.Surface;
                var box = new RectangleF(b.X + b.Width * 0.2f, b.Y + b.Height * 0.22f, b.Width * 0.6f, b.Height * 0.56f);
                if (backdrop) DashKit.Fill(g, box, Color.FromArgb(205, 0, 0, 0), 6);
                Paint.Text(g, c, name, b.Height * 0.36f, true, backdrop ? Color.White : ink, box, StringAlignment.Center, "Middle", true,
                    0, Color.Empty, false);
            }
        }

        public static void Progress(WidgetContext c)
        {
            Graphics g = c.G;
            RectangleF b = c.B;
            TelemetrySnapshot t = c.T;
            Color color = c.Color("color"), caution = c.Color("cautionColor"), track = c.Color("track");
            float radius = Math.Min(b.Height / 2, 3);
            var tool = new ShapeTool(c);
            if (t.ProgressMode == SessionProgressMode.LapRace && t.TotalLaps > 0)
            {
                int laps = Math.Min(t.TotalLaps, 400);
                float gap = laps > 120 ? 0 : (float)c.Num("gap");
                float w = (b.Width - gap * (laps - 1)) / laps;
                for (int i = 0; i < laps; i++)
                {
                    bool done = i < t.LapsCompleted;
                    bool yellow = done && t.YellowCompletedLaps != null && i < t.YellowCompletedLaps.Length && t.YellowCompletedLaps[i];
                    tool.Fill(g, new RectangleF(b.X + i * (w + gap), b.Y, Math.Max(0.5f, w), b.Height),
                        done ? (yellow ? caution : color) : track, Math.Min(radius, w / 2));
                }
                return;
            }
            tool.Fill(g, b, track, radius);
            double remaining = t.SessionTotalSeconds > 0 ? t.SessionSecondsRemaining / t.SessionTotalSeconds : 0;
            if (t.ProgressMode == SessionProgressMode.TimedRace)
            {
                Color tone = remaining > 0.6 ? color : remaining >= 0.25 ? c.Theme("warn") : c.Theme("bad");
                float w = (float)(Math.Max(0, Math.Min(1, remaining)) * b.Width);
                if (w >= 1) tool.Fill(g, new RectangleF(b.X, b.Y, w, b.Height), tone, radius);
            }
            else if (t.SessionTotalSeconds > 0)
            {
                float w = (float)(Math.Max(0, Math.Min(1, 1 - remaining)) * b.Width);
                if (w >= 1) tool.Fill(g, new RectangleF(b.X, b.Y, w, b.Height), color, radius);
            }
        }
    }
}
