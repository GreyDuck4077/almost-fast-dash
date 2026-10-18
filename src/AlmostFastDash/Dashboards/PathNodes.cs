using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Text;

namespace AlmostFastDash.Dashboards
{
    /// <summary>A point on a path, with the handles that bend the lines into it and out of it (none for a corner).</summary>
    internal sealed class PathNode
    {
        public PointF P;
        public PointF? In, Out;

        public PathNode Clone() => new PathNode { P = P, In = In, Out = Out };
    }

    /// <summary>
    /// A path widget's points, kept in its "points" setting as fractions of its box (0 to 1 across and down), so
    /// resizing the widget stretches the path with it. Each point is "x,y", or "x,y|inX,inY|outX,outY" with the
    /// handles of a curve.
    /// </summary>
    internal static class PathNodes
    {
        public static List<PathNode> Parse(string text)
        {
            var nodes = new List<PathNode>();
            foreach (string part in (text ?? "").Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] pieces = part.Split('|');
                if (!TryPoint(pieces[0], out PointF p)) continue;
                var node = new PathNode { P = p };
                if (pieces.Length >= 3 && TryPoint(pieces[1], out PointF into) && TryPoint(pieces[2], out PointF outOf))
                {
                    node.In = into;
                    node.Out = outOf;
                }
                nodes.Add(node);
            }
            return nodes;
        }

        private static bool TryPoint(string text, out PointF point)
        {
            point = PointF.Empty;
            string[] xy = text.Split(',');
            if (xy.Length != 2 || !float.TryParse(xy[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
                !float.TryParse(xy[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)) return false;
            point = new PointF(x, y);
            return true;
        }

        public static string Format(IEnumerable<PathNode> nodes)
        {
            var text = new StringBuilder();
            foreach (PathNode node in nodes)
            {
                if (text.Length > 0) text.Append(' ');
                text.Append(Point(node.P));
                if (node.In.HasValue || node.Out.HasValue) text.Append('|').Append(Point(node.In ?? node.P)).Append('|')
                    .Append(Point(node.Out ?? node.P));
            }
            return text.ToString();
        }

        private static string Point(PointF p) =>
            Math.Round(p.X, 4).ToString(CultureInfo.InvariantCulture) + "," + Math.Round(p.Y, 4).ToString(CultureInfo.InvariantCulture);

        /// <summary>The path drawn over a box: straight lines between corners, curves where a point has handles.</summary>
        public static GraphicsPath Build(List<PathNode> nodes, bool closed, RectangleF box)
        {
            var path = new GraphicsPath();
            if (nodes.Count < 2) return path;
            PointF At(PointF p) => new PointF(box.X + p.X * box.Width, box.Y + p.Y * box.Height);
            int segments = closed ? nodes.Count : nodes.Count - 1;
            for (int i = 0; i < segments; i++)
            {
                PathNode from = nodes[i], to = nodes[(i + 1) % nodes.Count];
                if (from.Out.HasValue || to.In.HasValue)
                    path.AddBezier(At(from.P), At(from.Out ?? from.P), At(to.In ?? to.P), At(to.P));
                else
                    path.AddLine(At(from.P), At(to.P));
            }
            if (closed) path.CloseFigure();
            return path;
        }

        /// <summary>
        /// Points given on the screen as a path widget's box and its points in that box: the box takes in every point
        /// and handle, at least a couple of pixels each way.
        /// </summary>
        public static RectangleF Fit(List<PathNode> screen, out List<PathNode> normalised)
        {
            IEnumerable<PointF> all = screen.SelectMany(n => new[] { n.P, n.In ?? n.P, n.Out ?? n.P });
            float left = all.Min(p => p.X), top = all.Min(p => p.Y), right = all.Max(p => p.X), bottom = all.Max(p => p.Y);
            float w = Math.Max(2, right - left), h = Math.Max(2, bottom - top);
            var box = new RectangleF((float)Math.Floor(left), (float)Math.Floor(top), (float)Math.Ceiling(w), (float)Math.Ceiling(h));
            PointF Local(PointF p) => new PointF((p.X - box.X) / box.Width, (p.Y - box.Y) / box.Height);
            normalised = screen.Select(n => new PathNode { P = Local(n.P), In = n.In.HasValue ? Local(n.In.Value) : (PointF?)null,
                Out = n.Out.HasValue ? Local(n.Out.Value) : (PointF?)null })
                .ToList();
            return box;
        }

        /// <summary>A path widget's points on the screen, from its box.</summary>
        public static List<PathNode> OnScreen(List<PathNode> nodes, RectangleF box)
        {
            PointF At(PointF p) => new PointF(box.X + p.X * box.Width, box.Y + p.Y * box.Height);
            return nodes.Select(n => new PathNode { P = At(n.P), In = n.In.HasValue ? At(n.In.Value) : (PointF?)null,
                Out = n.Out.HasValue ? At(n.Out.Value) : (PointF?)null })
                .ToList();
        }

        /// <summary>
        /// The points of a shape as a path, so it can be reshaped point by point: corners for rectangles, polygons,
        /// stars, triangles, lines and chevrons; four curved points for an ellipse. Null for shapes that don't convert.
        /// </summary>
        public static List<PathNode> FromShape(string shape, int sides, float inner, out bool closed)
        {
            closed = true;
            switch (shape)
            {
                case "Rectangle": return Corners(0, 0, 1, 0, 1, 1, 0, 1);
                case "Triangle": return Corners(0.5f, 0, 1, 1, 0, 1);
                case "Line": closed = false; return Corners(0, 0.5f, 1, 0.5f);
                case "Diagonal down": closed = false; return Corners(0, 0, 1, 1);
                case "Diagonal up": closed = false; return Corners(0, 1, 1, 0);
                case "Chevron": closed = false; return Corners(0, 0, 1, 0.5f, 0, 1);
                case "Polygon":
                case "Star":
                    return Polygon(sides, shape == "Star" ? inner : 1).Select(p => new PathNode { P = p }).ToList();
                case "Ellipse":
                {
                    // Four points with handles 0.5523 of the radius long: the closest a curve comes to a quarter circle.
                    const float k = 0.5523f * 0.5f;
                    return new List<PathNode>
                    {
                        new PathNode { P = new PointF(0.5f, 0), In = new PointF(0.5f - k, 0), Out = new PointF(0.5f + k, 0) },
                        new PathNode { P = new PointF(1, 0.5f), In = new PointF(1, 0.5f - k), Out = new PointF(1, 0.5f + k) },
                        new PathNode { P = new PointF(0.5f, 1), In = new PointF(0.5f + k, 1), Out = new PointF(0.5f - k, 1) },
                        new PathNode { P = new PointF(0, 0.5f), In = new PointF(0, 0.5f + k), Out = new PointF(0, 0.5f - k) }
                    };
                }
                default: return null;
            }
        }

        private static List<PathNode> Corners(params float[] xy)
        {
            var nodes = new List<PathNode>();
            for (int i = 0; i + 1 < xy.Length; i += 2) nodes.Add(new PathNode { P = new PointF(xy[i], xy[i + 1]) });
            return nodes;
        }

        /// <summary>
        /// A regular polygon (or a star, when <paramref name="inner"/> is under 1) filling a 1×1 box, first point at the
        /// top. A star has twice the points, every other one at the inner size.
        /// </summary>
        public static List<PointF> Polygon(int sides, float inner)
        {
            sides = Math.Max(3, Math.Min(48, sides));
            bool star = inner < 1;
            int count = star ? sides * 2 : sides;
            var points = new List<PointF>();
            for (int i = 0; i < count; i++)
            {
                double angle = -Math.PI / 2 + i * Math.PI * 2 / count;
                float r = star && i % 2 == 1 ? Math.Max(0.05f, inner) : 1f;
                points.Add(new PointF(0.5f + 0.5f * r * (float)Math.Cos(angle), 0.5f + 0.5f * r * (float)Math.Sin(angle)));
            }
            return points;
        }
    }
}
