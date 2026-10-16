using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AlmostFastOverlays
{
    /// <summary>How to repeat a selection: in rows and columns, or around a circle.</summary>
    internal sealed class RepeatSpec
    {
        public bool Circle;
        /// <summary>A grid: how many across and down (the original counts as the first), and the gaps between them.</summary>
        public int Across = 5, Down = 1;
        public double GapX = 8, GapY = 8;
        /// <summary>
        /// A circle: how many in all, over how many degrees (360 all the way round, 180 half; negative goes
        /// anticlockwise), how far from the middle, and where the original sits (-90 the top, 0 the right, 90 the bottom).
        /// </summary>
        public int Count = 8;
        public double Sweep = 360, Radius = 120, Start = -90;
        /// <summary>Copies turn with the circle (like a dial's ticks) or stay upright (like its numbers), plus a turn of their own each.</summary>
        public bool Follow = true;
        public double TurnEach;
        /// <summary>Numbers them 1, 2, 3… in order, as their groups' Slot, for formulas such as RevPct * 10 >= Slot.</summary>
        public bool Number;

        public RepeatSpec Copy() => (RepeatSpec)MemberwiseClone();
    }

    /// <summary>The Repeat tool: copies of a selection laid out in a grid or around a circle.</summary>
    internal static class Repeater
    {
        public const int MostCopies = 400;

        /// <summary>
        /// The copies, one set per place after the original's (each set a copy of every original widget, in order).
        /// Nothing is added to the screen.
        /// </summary>
        public static List<List<DashWidget>> Build(IList<DashWidget> originals, RepeatSpec spec)
        {
            var sets = new List<List<DashWidget>>();
            if (originals.Count == 0) return sets;
            double left = originals.Min(w => w.X), top = originals.Min(w => w.Y);
            double width = originals.Max(w => w.X + w.W) - left, height = originals.Max(w => w.Y + w.H) - top;
            double cx = left + width / 2, cy = top + height / 2;
            if (!spec.Circle)
            {
                int across = Math.Max(1, spec.Across), down = Math.Max(1, spec.Down);
                double stepX = width + spec.GapX, stepY = height + spec.GapY;
                for (int row = 0; row < down; row++)
                    for (int column = 0; column < across; column++)
                    {
                        if (row == 0 && column == 0) continue;
                        if (sets.Count + 1 >= MostCopies) return sets;
                        sets.Add(originals.Select(w => Copy(w, w.X + column * stepX, w.Y + row * stepY, 0)).ToList());
                    }
                return sets;
            }

            int count = Math.Max(1, Math.Min(MostCopies, spec.Count));
            // All the way round, the last stops a step short of the first; part of the way, the last ends the arc.
            double step = Math.Abs(spec.Sweep) >= 360 ? spec.Sweep / count : count > 1 ? spec.Sweep / (count - 1) : 0;
            double start = spec.Start * Math.PI / 180;
            double middleX = cx - spec.Radius * Math.Cos(start), middleY = cy - spec.Radius * Math.Sin(start);
            for (int i = 1; i < count; i++)
            {
                double angle = (spec.Start + i * step) * Math.PI / 180;
                double placeX = middleX + spec.Radius * Math.Cos(angle), placeY = middleY + spec.Radius * Math.Sin(angle);
                double turn = (spec.Follow ? i * step : 0) + i * spec.TurnEach;
                double radians = turn * Math.PI / 180, cos = Math.Cos(radians), sin = Math.Sin(radians);
                sets.Add(originals.Select(w =>
                {
                    // Each widget keeps its place in the selection, turned with it.
                    double ox = w.X + w.W / 2 - cx, oy = w.Y + w.H / 2 - cy;
                    double x = placeX + ox * cos - oy * sin, y = placeY + ox * sin + oy * cos;
                    return Copy(w, x - w.W / 2, y - w.H / 2, turn);
                }).ToList());
            }
            return sets;
        }

        private static DashWidget Copy(DashWidget original, double x, double y, double turn)
        {
            DashWidget copy = SettingsStore.Clone(original);
            copy.Id = DashScreen.NewId("w");
            copy.Locked = false;
            copy.X = Math.Round(x, 1);
            copy.Y = Math.Round(y, 1);
            if (Math.Abs(turn % 360) > 0.001)
            {
                string rotate = copy.Get("rotate", "0") ?? "0";
                copy.Set("rotate", rotate.StartsWith("=")
                    ? "=(" + rotate.Substring(1) + ") + " + Turn(turn).ToString("0.###", CultureInfo.InvariantCulture)
                    : Turn(Expr.Num(rotate) + turn).ToString("0.###", CultureInfo.InvariantCulture));
            }
            return copy;
        }

        /// <summary>An angle from -180 to 180.</summary>
        public static double Turn(double degrees)
        {
            degrees %= 360;
            if (degrees > 180) degrees -= 360;
            if (degrees <= -180) degrees += 360;
            return Math.Round(degrees, 3);
        }

        /// <summary>
        /// Gives every set (the original first) its own group, so each copy moves as one and is told apart. Numbered,
        /// the groups' Slot counts up from the original's (from 1 when it had none); they share the original's name,
        /// as copies made with Duplicate do, and Layers shows each one's number beside it.
        /// </summary>
        public static void Group(IList<DashWidget> originals, List<List<DashWidget>> sets, bool number, string name)
        {
            bool oneGroup = originals.All(w => !string.IsNullOrEmpty(w.Group) && w.Group == originals[0].Group);
            bool loose = originals.All(w => string.IsNullOrEmpty(w.Group));
            // Numbering needs each set in a group of its own, and so do loose widgets repeated together; otherwise
            // the groups they're in are copied as they are.
            bool regroup = number || (loose && originals.Count > 1);
            double first = oneGroup && originals[0].Slot != 0 ? originals[0].Slot : 1;
            string groupName = oneGroup && !string.IsNullOrWhiteSpace(originals[0].GroupName) ? originals[0].GroupName : name;
            if (regroup)
            {
                string id = oneGroup ? originals[0].Group : DashScreen.NewId("g");
                foreach (DashWidget w in originals)
                {
                    w.Group = id;
                    w.GroupName = groupName;
                    if (number) w.Slot = first;
                }
            }
            for (int i = 0; i < sets.Count; i++)
            {
                if (regroup)
                {
                    string id = DashScreen.NewId("g");
                    foreach (DashWidget w in sets[i])
                    {
                        w.Group = id;
                        w.GroupName = groupName;
                        if (number) w.Slot = first + i + 1;
                    }
                    continue;
                }
                var ids = new Dictionary<string, string>();
                foreach (DashWidget w in sets[i].Where(w => !string.IsNullOrEmpty(w.Group)))
                {
                    if (!ids.TryGetValue(w.Group, out string id)) ids[w.Group] = id = DashScreen.NewId("g");
                    w.Group = id;
                }
            }
        }

    }
}
