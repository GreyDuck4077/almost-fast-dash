using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;

namespace AlmostFastOverlays
{
    /// <summary>The kinds of LED device shape setup can describe.</summary>
    internal static class LedShapes
    {
        /// <summary>LEDs around a screen: a column on each side, a row across the top and/or bottom.</summary>
        public const string Screen = "Screen";
        /// <summary>One or more light bars side by side.</summary>
        public const string Bars = "Bars";
        /// <summary>A single straight strip.</summary>
        public const string Strip = "Strip";
    }

    /// <summary>One LED position on the drawn shape.</summary>
    internal sealed class LayoutSlot
    {
        public int Index;
        public string Piece;
        public double X, Y;
    }

    /// <summary>A part of the shape (a side, the top row, one bar) and its slots in reading order.</summary>
    internal sealed class LayoutPiece
    {
        public string Id, Name, DefaultRole;
        /// <summary>Slot indexes in the order a fill grows: left to right, or bottom to top for a side.</summary>
        public int[] Slots;
    }

    /// <summary>
    /// A device's physical LED shape, built from scratch in setup, and which LED (by its number on
    /// the wire) sits in each place. Everything the lights need comes from this: the groups, their
    /// roles and the direction fills grow, so nothing depends on how a board happens to be wired.
    /// </summary>
    internal sealed class LedLayout
    {
        public string Shape { get; set; } = LedShapes.Screen;
        /// <summary>LEDs on each edge of a screen (0 for none).</summary>
        public int Left { get; set; }
        public int Top { get; set; }
        public int Right { get; set; }
        public int Bottom { get; set; }
        /// <summary>Light bars side by side, each with the same number of LEDs.</summary>
        public int Bars { get; set; } = 1;
        public int PerBar { get; set; }
        /// <summary>Bars that join end to end into one long bar, rather than working as separate bars.</summary>
        public bool Connected { get; set; } = true;
        public int Length { get; set; }
        /// <summary>What each piece is for, by piece id, where it differs from the piece's usual role.</summary>
        public Dictionary<string, string> Roles { get; set; } = new Dictionary<string, string>();
        /// <summary>The LED (from 0, as numbered on the wire) at each slot, or -1; null until mapped.</summary>
        public List<int> Map { get; set; }

        [ScriptIgnore]
        public int Total
        {
            get
            {
                switch (Shape)
                {
                    case LedShapes.Bars: return Math.Max(0, Bars) * Math.Max(0, PerBar);
                    case LedShapes.Strip: return Math.Max(0, Length);
                    default: return Math.Max(0, Left) + Math.Max(0, Top) + Math.Max(0, Right) + Math.Max(0, Bottom);
                }
            }
        }

        [ScriptIgnore]
        public bool IsMapped => Map != null && Map.Count == Total && Total > 0 && Map.All(i => i >= 0) && Map.Distinct().Count() == Total;

        public string Describe()
        {
            switch (Shape)
            {
                case LedShapes.Bars:
                    return Bars <= 1 ? "A light bar of " + PerBar + " LEDs" : Bars + " light bars of " + PerBar + " LEDs, " + (Connected ? "joined into one" : "separate");
                case LedShapes.Strip: return "A strip of " + Length + " LEDs";
                default:
                    var edges = new List<string>();
                    if (Left > 0) edges.Add(Left + " left");
                    if (Top > 0) edges.Add(Top + " top");
                    if (Right > 0) edges.Add(Right + " right");
                    if (Bottom > 0) edges.Add(Bottom + " bottom");
                    return "LEDs around a screen: " + string.Join(", ", edges);
            }
        }

        // ------------------------------------------------------------------ geometry

        /// <summary>
        /// Every place an LED sits, in path order: around a screen clockwise from the bottom of the
        /// left side (up the left, across the top, down the right, back along the bottom); bars and
        /// strips left to right. Coordinates are in LED spacings, y growing downward.
        /// </summary>
        public List<LayoutSlot> Slots()
        {
            var slots = new List<LayoutSlot>();
            void Add(string piece, double x, double y) => slots.Add(new LayoutSlot { Index = slots.Count, Piece = piece, X = x, Y = y });
            switch (Shape)
            {
                case LedShapes.Bars:
                    double gap = Connected ? 0.6 : 1.6;
                    for (int b = 0; b < Bars; b++)
                        for (int j = 0; j < PerBar; j++) Add(Bars == 1 ? "bar" : "bar" + (b + 1), b * (PerBar + gap) + j, 0);
                    break;
                case LedShapes.Strip:
                    for (int j = 0; j < Length; j++) Add("strip", j, 0);
                    break;
                default:
                    int w = Math.Max(1, Math.Max(Top, Bottom)), h = Math.Max(1, Math.Max(Left, Right));
                    double offLeft = (h - Left) / 2.0, offRight = (h - Right) / 2.0, offTop = (w - Top) / 2.0, offBottom = (w - Bottom) / 2.0;
                    for (int j = 0; j < Left; j++) Add("left", 0, h - j - offLeft);
                    for (int j = 0; j < Top; j++) Add("top", 1 + j + offTop, 0);
                    for (int j = 0; j < Right; j++) Add("right", w + 1, 1 + j + offRight);
                    for (int j = 0; j < Bottom; j++) Add("bottom", w - j - offBottom, h + 1);
                    break;
            }
            return slots;
        }

        /// <summary>The runs an LED chain can follow without jumping: the whole way around a screen, or each bar or strip.</summary>
        public List<int[]> Paths(out bool closed)
        {
            List<LayoutSlot> slots = Slots();
            closed = Shape == LedShapes.Screen && Left > 0 && Top > 0 && Right > 0 && Bottom > 0;
            if (Shape == LedShapes.Screen || Shape == LedShapes.Strip) return new List<int[]> { slots.Select(s => s.Index).ToArray() };
            return slots.GroupBy(s => s.Piece).Select(g => g.Select(s => s.Index).ToArray()).ToList();
        }

        /// <summary>The shape's parts, each with its slots in fill order (left to right, bottom to top).</summary>
        public List<LayoutPiece> Pieces()
        {
            List<LayoutSlot> slots = Slots();
            int[] Of(string piece) => slots.Where(s => s.Piece == piece).Select(s => s.Index).ToArray();
            var pieces = new List<LayoutPiece>();
            switch (Shape)
            {
                case LedShapes.Bars:
                    if (Bars <= 1 || Connected)
                        pieces.Add(new LayoutPiece { Id = "bar", Name = "Light bar", DefaultRole = LedPurposes.Bar, Slots = slots.Select(s => s.Index).ToArray() });
                    else
                        for (int b = 1; b <= Bars; b++) pieces.Add(new LayoutPiece { Id = "bar" + b, Name = "Bar " + b, DefaultRole = LedPurposes.Bar, Slots = Of("bar" + b) });
                    break;
                case LedShapes.Strip:
                    pieces.Add(new LayoutPiece { Id = "strip", Name = "Strip", DefaultRole = LedPurposes.Bar, Slots = Of("strip") });
                    break;
                default:
                    // Sides read bottom to top and rows left to right, the way a gauge fills.
                    if (Left > 0) pieces.Add(new LayoutPiece { Id = "left", Name = "Left side", DefaultRole = LedPurposes.Left, Slots = Of("left") });
                    if (Top > 0) pieces.Add(new LayoutPiece { Id = "top", Name = "Top row", DefaultRole = LedPurposes.Shift, Slots = Of("top") });
                    if (Right > 0) pieces.Add(new LayoutPiece { Id = "right", Name = "Right side", DefaultRole = LedPurposes.Right, Slots = Of("right").Reverse().ToArray() });
                    if (Bottom > 0) pieces.Add(new LayoutPiece { Id = "bottom", Name = "Bottom row", DefaultRole = LedPurposes.Other, Slots = Of("bottom").Reverse().ToArray() });
                    break;
            }
            return pieces;
        }

        public string RoleOf(LayoutPiece piece) =>
            Roles != null && Roles.TryGetValue(piece.Id, out string role) && LedPurposes.All.Contains(role) ? role : piece.DefaultRole;

        /// <summary>One group per piece, its LEDs in fill order, from the mapping. Ids stay the same when set up again, so light layers aimed at a group keep working.</summary>
        public List<LedGroup> Groups()
        {
            var groups = new List<LedGroup>();
            if (!IsMapped) return groups;
            foreach (LayoutPiece piece in Pieces())
            {
                List<int> leds = piece.Slots.Select(s => Map[s] + 1).ToList();
                groups.Add(new LedGroup { Id = "layout-" + piece.Id, Name = piece.Name, Purpose = RoleOf(piece), Leds = leds, From = leds.Min(), To = leds.Max() });
            }
            return groups;
        }

        /// <summary>Where each LED (from 0) sits on the drawing, or null for one that isn't mapped.</summary>
        public LayoutSlot[] SlotOfLed()
        {
            var result = new LayoutSlot[Total];
            if (Map == null) return result;
            List<LayoutSlot> slots = Slots();
            for (int s = 0; s < slots.Count && s < Map.Count; s++)
                if (Map[s] >= 0 && Map[s] < result.Length) result[Map[s]] = slots[s];
            return result;
        }
    }

    /// <summary>
    /// Works out which LED is where from the user's clicks. LEDs are asked for in order: the user
    /// clicks where each lit one is. Once two neighbours are placed, the chain is followed along the
    /// shape automatically until it runs off the end of a side, bar or strip, and the next LED is
    /// asked for. So an ordinary chain takes two clicks, and odd wiring still works, just with more.
    /// </summary>
    internal sealed class LayoutMapper
    {
        private readonly int[] _slotLed, _ledSlot;
        private readonly List<int[]> _paths;
        private readonly bool _closed;
        private readonly Stack<List<int>> _history = new Stack<List<int>>();
        private int _pendingSlot = -1;

        public LayoutMapper(LedLayout layout)
        {
            int total = layout.Total;
            _slotLed = Enumerable.Repeat(-1, total).ToArray();
            _ledSlot = Enumerable.Repeat(-1, total).ToArray();
            _paths = layout.Paths(out _closed);
        }

        public int Total => _ledSlot.Length;

        /// <summary>The LED to find next (from 0), or -1 when all are placed.</summary>
        public int Next => Array.IndexOf(_ledSlot, -1);

        public bool Done => Next < 0;

        /// <summary>True while the last LED placed has a lone neighbour still to find, so the direction isn't known yet.</summary>
        public bool Pending => _pendingSlot >= 0;

        public int LedAt(int slot) => slot >= 0 && slot < _slotLed.Length ? _slotLed[slot] : -1;

        public int SlotOf(int led) => led >= 0 && led < _ledSlot.Length ? _ledSlot[led] : -1;

        /// <summary>The user says the LED being asked for is at this slot. Returns how many LEDs that placed (0 if the slot is taken).</summary>
        public int Place(int slot)
        {
            int led = Next;
            if (led < 0 || slot < 0 || slot >= _slotLed.Length || _slotLed[slot] >= 0) return 0;
            var placed = new List<int>();
            Assign(led, slot, placed);
            if (_pendingSlot >= 0 && led > 0 && _ledSlot[led - 1] == _pendingSlot)
            {
                // Two neighbours known: if they're next to each other on the shape, follow the chain.
                int step = Step(_pendingSlot, slot, out int[] path);
                _pendingSlot = -1;
                if (step != 0)
                {
                    int at = Array.IndexOf(path, slot), next = led + 1;
                    while (next < Total)
                    {
                        at += step;
                        if (_closed) at = (at % path.Length + path.Length) % path.Length;
                        if (at < 0 || at >= path.Length || _slotLed[path[at]] >= 0 || _ledSlot[next] >= 0) break;
                        Assign(next, path[at], placed);
                        next++;
                    }
                }
                else _pendingSlot = slot;
            }
            else _pendingSlot = slot;
            if (Done) _pendingSlot = -1;
            _history.Push(placed);
            return placed.Count;
        }

        /// <summary>Takes back the last click and everything it placed.</summary>
        public void Undo()
        {
            if (_history.Count == 0) return;
            foreach (int led in _history.Pop())
            {
                _slotLed[_ledSlot[led]] = -1;
                _ledSlot[led] = -1;
            }
            int last = Next - 1;
            _pendingSlot = last >= 0 && _history.Count > 0 && _history.Peek().Count == 1 ? _ledSlot[last] : -1;
        }

        public bool CanUndo => _history.Count > 0;

        /// <summary>Starts from a finished map (setting up a device again), as one step that Undo or Start over can clear.</summary>
        public void Load(List<int> map)
        {
            var placed = new List<int>();
            for (int slot = 0; slot < map.Count && slot < _slotLed.Length; slot++)
                if (map[slot] >= 0 && map[slot] < Total && _ledSlot[map[slot]] < 0) Assign(map[slot], slot, placed);
            _pendingSlot = -1;
            _history.Push(placed);
        }

        /// <summary>The finished map, slot by slot.</summary>
        public List<int> Map() => _slotLed.ToList();

        private void Assign(int led, int slot, List<int> placed)
        {
            _slotLed[slot] = led;
            _ledSlot[led] = slot;
            placed.Add(led);
        }

        /// <summary>+1 or -1 when b follows a along one of the paths, else 0.</summary>
        private int Step(int a, int b, out int[] path)
        {
            foreach (int[] p in _paths)
            {
                int i = Array.IndexOf(p, a), j = Array.IndexOf(p, b);
                if (i < 0 || j < 0) continue;
                path = p;
                if (j == i + 1 || (_closed && i == p.Length - 1 && j == 0)) return 1;
                if (j == i - 1 || (_closed && i == 0 && j == p.Length - 1)) return -1;
                return 0;
            }
            path = null;
            return 0;
        }
    }
}
