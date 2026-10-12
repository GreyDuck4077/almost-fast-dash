using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace AlmostFastOverlays
{
    /// <summary>
    /// Bends, tapers, bulges and slants a bar the way a warp works in an image editor. Everything is
    /// drawn straight in a flat space (Length long, Thickness thick, starting at the left) and each
    /// point is then carried onto the warped shape, which is fitted back into the widget's box. A
    /// vertical bar is laid out the same way and turned so it starts at the bottom.
    /// </summary>
    internal sealed class BarWarp
    {
        public static readonly string[] Anchors = { "Middle", "Bottom edge", "Top edge" };

        private const int Samples = 64;
        private readonly RectangleF _box;
        private readonly bool _vertical;
        private readonly float _bend, _peak, _taper, _bulge, _tanSkew, _anchor;
        private float _profileMax = 1, _left, _top, _sx = 1, _sy = 1, _offsetY;

        /// <summary>The flat space: a straight bar this long and this thick at its thickest.</summary>
        public float Length { get; }
        public float Thickness { get; }
        /// <summary>No warp at all: the plain bar fills its box, so the classic drawing is used.</summary>
        public bool IsStraight { get; }

        /// <param name="bend">-1 to 1: how far the bar curves, in box heights (up for a horizontal bar).</param>
        /// <param name="peak">0 to 1: where along the bar the curve is highest (1 rises all the way to the end).</param>
        /// <param name="taper">-1 to 1: positive starts thin and ends full, negative the other way round.</param>
        /// <param name="bulge">-1 to 1: positive swells the middle, negative pinches it.</param>
        /// <param name="skewDegrees">Slants the bar sideways like italic type.</param>
        /// <param name="thickness">0 to 1: the thickest point as a share of the box.</param>
        /// <param name="anchor">Which edge stays put when the thickness changes: Middle, Bottom edge or Top edge.</param>
        public BarWarp(RectangleF box, bool vertical, double bend, double peak, double taper, double bulge, double skewDegrees, double thickness, string anchor)
        {
            _box = box;
            _vertical = vertical;
            float across = vertical ? box.Width : box.Height;
            Length = Math.Max(1, vertical ? box.Height : box.Width);
            _bend = (float)(Clamp(bend, -1, 1) * across);
            _peak = (float)Clamp(peak, 0, 1);
            _taper = (float)Clamp(taper, -1, 1);
            _bulge = (float)Clamp(bulge, -1, 1);
            _tanSkew = (float)Math.Tan(Clamp(skewDegrees, -60, 60) * Math.PI / 180);
            _anchor = anchor == "Bottom edge" ? 0 : anchor == "Top edge" ? 1 : 0.5f;
            float share = (float)Clamp(thickness, 0.02, 1);
            Thickness = Math.Max(1, across * share);
            IsStraight = Math.Abs(_bend) < 0.01f && Math.Abs(_taper) < 0.001f && Math.Abs(_bulge) < 0.001f && Math.Abs(_tanSkew) < 0.0001f && share >= 0.999f;
            if (IsStraight) return;

            _profileMax = 0;
            for (int i = 0; i <= Samples; i++) _profileMax = Math.Max(_profileMax, RawProfile(i / (float)Samples));

            // Fit: the warped outline always spans the box lengthwise; across, it shrinks only when it
            // would not fit, and otherwise sits against the edge it grows from.
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i <= Samples; i++)
                foreach (float v in new[] { 0f, 1f })
                {
                    PointF p = Raw(i / (float)Samples, v);
                    minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
                    minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
                }
            _left = minX;
            _top = minY;
            _sx = Length / Math.Max(1e-3f, maxX - minX);
            float height = maxY - minY;
            _sy = height > across ? across / height : 1;
            float spare = across - height * _sy;
            _offsetY = _anchor == 0 ? spare : _anchor == 1 ? 0 : spare / 2;
        }

        private static double Clamp(double v, double lo, double hi) => double.IsNaN(v) ? 0 : Math.Max(lo, Math.Min(hi, v));

        /// <summary>The centre line's height at a point along the bar, 0 at the ends and 1 at the peak.</summary>
        private float Curve(float u)
        {
            u = Math.Max(0, Math.Min(1, u));
            if (_peak <= 0.001f) return (float)Math.Cos(Math.PI / 2 * u);
            if (_peak >= 0.999f) return (float)Math.Sin(Math.PI / 2 * u);
            return u <= _peak
                ? (float)Math.Sin(Math.PI / 2 * u / _peak)
                : (float)Math.Cos(Math.PI / 2 * (u - _peak) / (1 - _peak));
        }

        private float RawProfile(float u)
        {
            float w = _taper >= 0 ? Lerp(1 - 0.95f * _taper, 1, u) : Lerp(1, 1 + 0.95f * _taper, u);
            return w * (1 + _bulge * (_bulge > 0 ? 1 : 0.9f) * (float)Math.Sin(Math.PI * u));
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        /// <summary>A point of the warped bar before fitting; y grows downward as on screen. v is 0 on the bottom edge, 1 on the top.</summary>
        private PointF Raw(float u, float v)
        {
            const float h = 0.002f;
            float cx = u * Length, cy = -_bend * Curve(u);
            float ua = Math.Max(0, u - h), ub = Math.Min(1, u + h);
            float dx = (ub - ua) * Length, dy = -_bend * (Curve(ub) - Curve(ua));
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            float nx = dy / len, ny = -dx / len;
            float off = Thickness * RawProfile(u) / _profileMax * (v - _anchor);
            float px = cx + nx * off, py = cy + ny * off;
            return new PointF(px - py * _tanSkew, py);
        }

        /// <summary>Carries a point of the flat bar onto the screen.</summary>
        public PointF Map(float x, float y)
        {
            float vx, vy;
            if (IsStraight) { vx = x; vy = y; }
            else
            {
                PointF p = Raw(x / Length, 1 - y / Thickness);
                vx = (p.X - _left) * _sx;
                vy = (p.Y - _top) * _sy + _offsetY;
            }
            return _vertical ? new PointF(_box.X + vy, _box.Bottom - vx) : new PointF(_box.X + vx, _box.Y + vy);
        }

        /// <summary>
        /// Carries a shape drawn in the flat space onto the screen. Curves are flattened and long edges
        /// split into short steps first, so straight edges follow the bend.
        /// </summary>
        public GraphicsPath Map(GraphicsPath flat)
        {
            var result = new GraphicsPath(flat.FillMode);
            if (flat.PointCount == 0) return result;
            using (var copy = (GraphicsPath)flat.Clone())
            {
                copy.Flatten(null, 0.25f);
                PointF[] points = copy.PathPoints;
                byte[] types = copy.PathTypes;
                var figure = new List<PointF>();
                void Flush(bool closed)
                {
                    if (closed && figure.Count > 1) Densify(figure, figure[figure.Count - 1], figure[0], false);
                    var mapped = new PointF[figure.Count];
                    for (int i = 0; i < figure.Count; i++) mapped[i] = Map(figure[i].X, figure[i].Y);
                    if (closed && mapped.Length >= 3) result.AddPolygon(mapped);
                    else if (mapped.Length >= 2) { result.StartFigure(); result.AddLines(mapped); }
                    figure.Clear();
                }
                for (int i = 0; i < points.Length; i++)
                {
                    bool start = (types[i] & (byte)PathPointType.PathTypeMask) == (byte)PathPointType.Start;
                    if (start && figure.Count > 0) Flush(false);
                    if (figure.Count > 0) Densify(figure, figure[figure.Count - 1], points[i], true);
                    else figure.Add(points[i]);
                    if ((types[i] & (byte)PathPointType.CloseSubpath) != 0) Flush(true);
                }
                if (figure.Count > 0) Flush(false);
            }
            return result;
        }

        /// <summary>Adds the points between a and b (and b itself when asked) at most a few flat pixels apart.</summary>
        private void Densify(List<PointF> figure, PointF a, PointF b, bool includeEnd)
        {
            if (!IsStraight)
            {
                float dx = b.X - a.X, dy = b.Y - a.Y;
                int steps = (int)Math.Min(400, Math.Ceiling(Math.Sqrt(dx * dx + dy * dy) / 3));
                for (int k = 1; k < steps; k++) figure.Add(new PointF(a.X + dx * k / steps, a.Y + dy * k / steps));
            }
            if (includeEnd) figure.Add(b);
        }
    }

    /// <summary>
    /// The ring of an arc gauge: a circle or an oval that fills its box, optionally growing thicker
    /// (or thinner) along its sweep. Angles are in degrees, clockwise from three o'clock.
    /// </summary>
    internal sealed class ArcRing
    {
        public PointF Center;
        public float RadiusX, RadiusY, Thickness, Taper;
        /// <summary>A plain circle of even thickness, which the classic pen drawing handles.</summary>
        public bool Classic => Math.Abs(Taper) < 0.001f && Math.Abs(RadiusX - RadiusY) < 0.01f;
        public float Start, Sweep;

        public RectangleF Rect => new RectangleF(Center.X - RadiusX, Center.Y - RadiusY, RadiusX * 2, RadiusY * 2);

        /// <summary>Thickness at an angle, following the taper from the start of the sweep to its end.</summary>
        public float ThicknessAt(float angle)
        {
            float t = Math.Abs(Sweep) < 1e-3f ? 0 : Math.Max(0, Math.Min(1, (angle - Start) / Sweep));
            float w = Taper >= 0 ? 1 - 0.9f * Taper * (1 - t) : 1 + 0.9f * Taper * t;
            return Thickness * w;
        }

        /// <summary>A point on the ring's centre line, pushed out (positive) or in (negative) by an offset.</summary>
        public PointF At(float angle, float offset = 0)
        {
            double a = angle * Math.PI / 180;
            return new PointF(Center.X + (RadiusX + offset) * (float)Math.Cos(a), Center.Y + (RadiusY + offset) * (float)Math.Sin(a));
        }

        /// <summary>The band from one angle to another, with flat or round ends.</summary>
        public GraphicsPath Band(float from, float sweep, bool round)
        {
            var path = new GraphicsPath();
            int steps = Math.Max(4, (int)Math.Ceiling(Math.Abs(sweep) / 1.5f));
            var outline = new List<PointF>(steps * 2 + 40);
            for (int i = 0; i <= steps; i++)
            {
                float a = from + sweep * i / steps;
                outline.Add(At(a, ThicknessAt(a) / 2));
            }
            if (round) Cap(outline, from + sweep, Math.Sign(sweep), true);
            for (int i = steps; i >= 0; i--)
            {
                float a = from + sweep * i / steps;
                outline.Add(At(a, -ThicknessAt(a) / 2));
            }
            if (round) Cap(outline, from, Math.Sign(sweep), false);
            path.AddPolygon(outline.ToArray());
            return path;
        }

        /// <summary>A half circle closing one end of the band: from the outer edge round to the inner at the end, the reverse at the start.</summary>
        private void Cap(List<PointF> outline, float angle, int direction, bool end)
        {
            double a = angle * Math.PI / 180;
            float r = ThicknessAt(angle) / 2;
            PointF c = At(angle);
            float rx = (float)Math.Cos(a), ry = (float)Math.Sin(a);
            float fx = -ry * direction, fy = rx * direction;
            for (int k = 1; k < 12; k++)
            {
                double phi = Math.PI * k / 12;
                float along = (float)Math.Sin(phi) * (end ? 1 : -1), outward = (float)Math.Cos(phi) * (end ? 1 : -1);
                outline.Add(new PointF(c.X + r * (rx * outward + fx * along), c.Y + r * (ry * outward + fy * along)));
            }
        }
    }
}
