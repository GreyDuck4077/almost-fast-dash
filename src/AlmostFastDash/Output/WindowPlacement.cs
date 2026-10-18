using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;

namespace AlmostFastDash.Output
{
    /// <summary>
    /// Where the dashboard's window and its place over the game sit: the dashboard area in screen pixels, kept in
    /// dashboard.window next to the settings. Only the running dashboard writes it, since it owns those windows,
    /// so settings.json keeps a single writer (the settings window).
    /// </summary>
    internal static class WindowPlacement
    {
        private static readonly string FilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dashboard.window");

        /// <summary>The place saved for a mode (Window or Overlay), if any.</summary>
        public static Rectangle? Load(string mode)
        {
            try { return File.Exists(FilePath) ? Parse(File.ReadAllText(FilePath), mode) : null; }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        public static void Save(string mode, Rectangle place)
        {
            try
            {
                string text = File.Exists(FilePath) ? File.ReadAllText(FilePath) : "";
                File.WriteAllText(FilePath, Merge(text, mode, place));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        /// <summary>A mode's place from the file's lines ("Window 100 80 854 480"), or null.</summary>
        internal static Rectangle? Parse(string text, string mode)
        {
            foreach (string line in (text ?? "").Split('\n'))
            {
                string[] parts = line.Trim().Split(' ');
                if (parts.Length != 5 || parts[0] != mode) continue;
                var n = new int[4];
                bool ok = true;
                for (int i = 0; i < 4; i++) ok &= int.TryParse(parts[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out n[i]);
                if (ok && n[2] > 0 && n[3] > 0) return new Rectangle(n[0], n[1], n[2], n[3]);
            }
            return null;
        }

        /// <summary>The file's text with one mode's place set, the other modes' kept.</summary>
        internal static string Merge(string text, string mode, Rectangle place)
        {
            IEnumerable<string> others = (text ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith(mode + " "));
            string mine = string.Format(CultureInfo.InvariantCulture, "{0} {1} {2} {3} {4}", mode, place.X, place.Y, place.Width, place.Height);
            return string.Join("\n", others.Concat(new[] { mine })) + "\n";
        }

        /// <summary>
        /// Where the dashboard goes: the saved place, shaped to the dashboard, when enough of it is on a display
        /// today; otherwise the default for the mode on the main display.
        /// </summary>
        internal static Rectangle Resolve(Rectangle? saved, string mode, Size dashboard, Rectangle[] displays, Rectangle mainWork)
        {
            double aspect = dashboard.Width > 0 && dashboard.Height > 0 ? (double)dashboard.Width / dashboard.Height : 16 / 9.0;
            if (saved.HasValue && saved.Value.Width >= MinimumWidth)
            {
                Rectangle s = saved.Value;
                // Another dashboard may have another shape: keep the width and where it was.
                var place = new Rectangle(s.X, s.Y, s.Width, Math.Max(1, (int)Math.Round(s.Width / aspect)));
                long area = (long)place.Width * place.Height, visible = 0;
                foreach (Rectangle d in displays)
                {
                    Rectangle overlap = Rectangle.Intersect(d, place);
                    if (!overlap.IsEmpty) visible += (long)overlap.Width * overlap.Height;
                }
                if (visible * 4 >= area) return place;
            }
            return Default(mode, aspect, mainWork);
        }

        /// <summary>
        /// A window of the dashboard: nearly half the main display's height, in the middle. Over the game: about a
        /// quarter of the height, low in the middle where a dash would be.
        /// </summary>
        internal static Rectangle Default(string mode, double aspect, Rectangle work)
        {
            bool overlay = mode == "Overlay";
            double height = work.Height * (overlay ? 0.26 : 0.45), width = height * aspect;
            double widest = work.Width * (overlay ? 0.6 : 0.7);
            if (width > widest) { width = widest; height = width / aspect; }
            int w = (int)Math.Round(width), h = (int)Math.Round(height);
            int x = work.X + (work.Width - w) / 2;
            int y = overlay ? work.Bottom - h - (int)(work.Height * 0.05) : work.Y + (work.Height - h) / 2;
            return new Rectangle(x, y, w, h);
        }

        /// <summary>Narrower than this and the dashboard can't be read, or grabbed to make it bigger again.</summary>
        internal const int MinimumWidth = 160;
    }

    /// <summary>
    /// The settings window keeps overlay.unlock fresh while the dashboard over the game is being moved and resized.
    /// The running dashboard unlocks it only while the file is fresh, so it locks again by itself when the
    /// settings window closes or moves on.
    /// </summary>
    internal static class OverlayUnlock
    {
        private static readonly string FilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "overlay.unlock");
        private const double FreshSeconds = 3;
        private static DateTime _nextReadUtc;
        private static bool _requested;

        /// <summary>Asks for the overlay to be unlocked (call at least every second while placing it).</summary>
        public static void Write()
        {
            try { File.WriteAllText(FilePath, "unlock"); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public static void Clear()
        {
            try { if (File.Exists(FilePath)) File.Delete(FilePath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        /// <summary>Whether the overlay should be unlocked now; the file is looked at four times a second at most.</summary>
        public static bool Requested()
        {
            DateTime now = DateTime.UtcNow;
            if (now < _nextReadUtc) return _requested;
            _nextReadUtc = now.AddMilliseconds(250);
            try
            {
                var file = new FileInfo(FilePath);
                _requested = file.Exists && IsFresh(file.LastWriteTimeUtc, now);
            }
            catch (IOException) { _requested = false; }
            catch (UnauthorizedAccessException) { _requested = false; }
            return _requested;
        }

        internal static bool IsFresh(DateTime writtenUtc, DateTime nowUtc) => (nowUtc - writtenUtc).TotalSeconds <= FreshSeconds;
    }
}
