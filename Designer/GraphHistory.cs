using System;

namespace AlmostFastOverlays
{
    /// <summary>
    /// The recent values behind one Graph widget: up to two lines, sampled on a steady clock so the
    /// line moves at an even pace whether or not the screen happened to redraw.
    /// </summary>
    internal sealed class GraphHistory
    {
        public const int Points = 160;
        private readonly double[] _first = new double[Points], _second = new double[Points];
        private int _head, _count;
        private DateTime _next = DateTime.MinValue;

        /// <summary>Changes whenever a sample is added, so the widget knows to redraw.</summary>
        public int Version { get; private set; }
        public int Count => _count;

        /// <summary>Adds a sample if it's time for one; the span is how many seconds the whole graph shows.</summary>
        public void Sample(DateTime now, double seconds, double first, double second)
        {
            if (now < _next && now > _next.AddSeconds(-seconds)) return;
            _next = now.AddSeconds(Math.Max(0.02, seconds / Points));
            _first[_head] = first;
            _second[_head] = second;
            _head = (_head + 1) % Points;
            _count = Math.Min(Points, _count + 1);
            Version++;
        }

        /// <summary>Sample i of the ones held, oldest first.</summary>
        public double First(int i) => _first[(_head - _count + i + Points * 2) % Points];
        public double Second(int i) => _second[(_head - _count + i + Points * 2) % Points];
    }
}
