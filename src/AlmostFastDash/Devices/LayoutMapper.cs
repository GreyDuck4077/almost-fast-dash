using System;
using System.Collections.Generic;
using System.Linq;

namespace AlmostFastDash.Devices
{
    /// <summary>
    /// Works out which LED is where from the user's clicks. LEDs are asked for in order: the user
    /// clicks where each lit one is. Once two neighbors are placed, the chain is followed along the
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
        private readonly bool _followChain;

        /// <param name="followChain">False to place every LED by its own click, for wiring the chain-following gets wrong.</param>
        public LayoutMapper(LedLayout layout, bool followChain = true)
        {
            _followChain = followChain;
            int total = layout.Total;
            _slotLed = Enumerable.Repeat(-1, total).ToArray();
            _ledSlot = Enumerable.Repeat(-1, total).ToArray();
            _paths = layout.Paths(out _closed);
        }

        public int Total => _ledSlot.Length;

        /// <summary>True when every LED is placed by its own click.</summary>
        public bool ByHand => !_followChain;

        /// <summary>The LED to find next (from 0), or -1 when all are placed.</summary>
        public int Next => Array.IndexOf(_ledSlot, -1);

        public bool Done => Next < 0;

        /// <summary>True while the last LED placed has a lone neighbor still to find, so the direction isn't known yet.</summary>
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
            if (!_followChain) { }
            else if (_pendingSlot >= 0 && led > 0 && _ledSlot[led - 1] == _pendingSlot)
            {
                // Two neighbors known: if they're next to each other on the shape, follow the chain.
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
            if (Done || !_followChain) _pendingSlot = -1;
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
