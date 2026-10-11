using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace AlmostFastOverlays
{
    /// <summary>One race event shown as a toast: a coloured badge, a headline and a detail line.</summary>
    internal sealed class Toast
    {
        public string Badge, Title, Detail;
        public Color Tone;
    }

    /// <summary>
    /// Watches the session for things worth a toast: the player's class position
    /// changing (once it has settled, naming the car passed) and incident points
    /// (gathered for a moment, since iRacing can add them in steps, with the nearest
    /// car at the time named for contact).
    /// </summary>
    internal sealed class ToastTracker
    {
        public static readonly Color Gain = Color.FromRgb(46, 224, 126), Loss = Color.FromRgb(255, 59, 78), Incident = Color.FromRgb(255, 194, 51);
        private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(800), Gather = TimeSpan.FromMilliseconds(1200);

        private int _position = -1, _pending = -1;
        private DateTime _pendingSince;
        private Dictionary<int, int> _places = new Dictionary<int, int>();
        private Dictionary<int, string> _names = new Dictionary<int, string>();
        private int _incidents = -1, _gathering;
        private DateTime _gatherSince;
        private string _nearest;
        private double _elapsed = -1;

        public List<Toast> Update(TelemetrySnapshot t, StandingsSnapshot s, OverlayConfig config, DateTime now)
        {
            var toasts = new List<Toast>();
            if (t == null || !t.Connected || s == null || !s.Connected)
            {
                Reset();
                return toasts;
            }
            // A new session starts the clock again: forget everything.
            if (s.SessionElapsed + 5 < _elapsed) Reset();
            _elapsed = s.SessionElapsed;

            StandingsEntry me = s.Classes.SelectMany(c => c.Entries).FirstOrDefault(e => e.IsPlayer);
            if (me != null && config.Bool("positions")) Positions(s, me, now, config.Bool("practice") || s.IsRace, toasts);
            if (config.Bool("incidents")) Incidents(t, s, me, now, toasts);
            return toasts;
        }

        private void Reset()
        {
            _position = _pending = _incidents = -1;
            _gathering = 0;
            _places.Clear();
            _names.Clear();
            _elapsed = -1;
        }

        private void Positions(StandingsSnapshot s, StandingsEntry me, DateTime now, bool announce, List<Toast> toasts)
        {
            StandingsClass group = s.Classes.First(c => c.Entries.Contains(me));
            int position = me.ClassPosition;
            if (position <= 0) return;
            if (_position < 0 || position == _position)
            {
                _position = position;
                _pending = -1;
                // Remember the order while it's settled, to name who was passed.
                _places = group.Entries.ToDictionary(e => e.CarIdx, e => e.ClassPosition);
                _names = group.Entries.ToDictionary(e => e.CarIdx, e => e.Name);
                return;
            }
            if (position != _pending)
            {
                _pending = position;
                _pendingSince = now;
                return;
            }
            if (now - _pendingSince < Settle) return;

            int before = _position, gained = before - position;
            var passed = new List<string>();
            foreach (StandingsEntry e in group.Entries)
            {
                if (e.IsPlayer || !_places.TryGetValue(e.CarIdx, out int was)) continue;
                bool overtaken = gained > 0 ? was < before && e.ClassPosition > position : was > before && e.ClassPosition < position;
                if (overtaken) passed.Add(e.Name);
            }
            if (announce)
            {
                string who = passed.Count == 0 ? "" : passed[0] + (passed.Count > 1 ? " and " + (passed.Count - 1) + " more" : "");
                toasts.Add(new Toast
                {
                    Badge = (gained > 0 ? "▲ " : "▼ ") + Math.Abs(gained),
                    Title = "P" + position,
                    Detail = who.Length == 0 ? (gained > 0 ? "Places gained" : "Places lost") : (gained > 0 ? "Passed " : "Passed by ") + who,
                    Tone = gained > 0 ? Gain : Loss
                });
            }
            _position = position;
            _pending = -1;
            _places = group.Entries.ToDictionary(e => e.CarIdx, e => e.ClassPosition);
            _names = group.Entries.ToDictionary(e => e.CarIdx, e => e.Name);
        }

        private void Incidents(TelemetrySnapshot t, StandingsSnapshot s, StandingsEntry me, DateTime now, List<Toast> toasts)
        {
            int count = t.IncidentPoints;
            if (_incidents < 0 || count < _incidents)
            {
                _incidents = count;
                _gathering = 0;
                return;
            }
            if (count > _incidents)
            {
                if (_gathering == 0)
                {
                    _gatherSince = now;
                    _nearest = NearestCar(s, me);
                }
                _gathering += count - _incidents;
                _incidents = count;
            }
            if (_gathering == 0 || now - _gatherSince < Gather) return;
            // iRacing: 4x contact with another car, 2x loss of control, 1x off track.
            string what = _gathering >= 4 ? (_nearest != null ? "Contact w/ " + _nearest : "Contact")
                : _gathering >= 2 ? "Loss of control" : "Off track";
            toasts.Add(new Toast { Badge = _gathering + "x", Title = what, Detail = "Incidents: " + count + "x", Tone = Incident });
            _gathering = 0;
        }

        /// <summary>The driver closest on track, within about a second and a half; the likely other party in contact.</summary>
        private static string NearestCar(StandingsSnapshot s, StandingsEntry me)
        {
            if (me == null || me.TrackPct < 0) return null;
            double lap = s.PlayerLapTime > 0 ? s.PlayerLapTime : 100;
            StandingsEntry best = null;
            double bestGap = 1.5;
            foreach (StandingsEntry e in s.Classes.SelectMany(c => c.Entries))
            {
                if (e == me || e.TrackPct < 0 || e.InPits) continue;
                double d = Math.Abs(e.TrackPct - me.TrackPct);
                double seconds = Math.Min(d, 1 - d) * lap;
                if (seconds < bestGap) { bestGap = seconds; best = e; }
            }
            return best?.Name;
        }
    }

    /// <summary>The toast stack: glass cards that slide in, wait, and fade away, newest on top.</summary>
    internal sealed class ToastView : Grid
    {
        public const double CardWidth = 460;
        private static readonly FontFamily Ui = new FontFamily("Segoe UI Variable Display, Segoe UI");
        private static readonly FontFamily Digits = new FontFamily("Bahnschrift, Segoe UI");
        private readonly StackPanel _stack = new StackPanel();
        private readonly ScaleTransform _scale = new ScaleTransform(1, 1);
        private readonly ToastTracker _tracker = new ToastTracker();
        private OverlayConfig _config = new OverlayConfig { Type = "toasts" };
        private int _demo = -1;

        public ToastView()
        {
            Width = CardWidth + 24;
            Children.Add(_stack);
            LayoutTransform = _scale;
        }

        /// <summary>Feeds one telemetry sample; while arranging, sample toasts keep coming so there's something to place.</summary>
        public void Update(TelemetrySnapshot t, StandingsSnapshot standings, OverlayConfig config, bool editing)
        {
            _config = config ?? _config;
            _scale.ScaleX = _scale.ScaleY = _config.Scale;
            // Keep room for the full stack so the window doesn't jump as toasts come and go.
            MinHeight = Math.Max(1, _config.Int("stack")) * 84;
            if (editing && (t == null || !t.Connected))
            {
                int tick = (int)(DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond / 3);
                if (tick != _demo)
                {
                    _demo = tick;
                    Show(Samples[tick % Samples.Length]);
                }
                return;
            }
            foreach (Toast toast in _tracker.Update(t, standings, _config, DateTime.UtcNow)) Show(toast);
        }

        public static readonly Toast[] Samples =
        {
            new Toast { Badge = "▲ 1", Title = "P5", Detail = "Passed Jordan Mercer", Tone = ToastTracker.Gain },
            new Toast { Badge = "4x", Title = "Contact w/ Sam Cole", Detail = "Incidents: 6x", Tone = ToastTracker.Incident },
            new Toast { Badge = "▼ 1", Title = "P6", Detail = "Passed by Casey Rossi", Tone = ToastTracker.Loss },
        };

        /// <summary>Adds a toast on top of the stack. A toast that stays (for still previews) never times out.</summary>
        public void Show(Toast toast, bool stay = false)
        {
            Border card = Card(toast);
            _stack.Children.Insert(0, card);
            int most = Math.Max(1, _config.Int("stack"));
            while (_stack.Children.Count > most) _stack.Children.RemoveAt(_stack.Children.Count - 1);

            var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };
            var move = new TranslateTransform(0, -18);
            card.RenderTransform = move;
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-18, 0, new Duration(TimeSpan.FromMilliseconds(420))) { EasingFunction = ease });
            card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(260))));
            if (stay) return;

            var hold = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(1, _config.Int("duration"))) };
            hold.Tick += (s, e) =>
            {
                hold.Stop();
                var fade = new DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(380)));
                fade.Completed += (s2, e2) => _stack.Children.Remove(card);
                card.BeginAnimation(OpacityProperty, fade);
                move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, 10, new Duration(TimeSpan.FromMilliseconds(380))));
            };
            hold.Start();
        }

        private Border Card(Toast toast)
        {
            byte alpha = (byte)(_config.Opacity * 2.55);
            Color tone = toast.Tone;
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());

            var badge = new Border
            {
                MinWidth = 64, Height = 44, CornerRadius = new CornerRadius(11), Padding = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(Color.FromArgb(46, tone.R, tone.G, tone.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(150, tone.R, tone.G, tone.B)), BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect { Color = tone, BlurRadius = 18, ShadowDepth = 0, Opacity = 0.55 },
                Child = new TextBlock
                {
                    Text = toast.Badge, FontFamily = Digits, FontSize = 22, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(tone),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            };
            row.Children.Add(badge);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 4, 0) };
            text.Children.Add(new TextBlock { Text = toast.Title, FontFamily = Digits, FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis });
            if (!string.IsNullOrEmpty(toast.Detail))
                text.Children.Add(new TextBlock { Text = toast.Detail, FontFamily = Ui, FontSize = 12.5, Foreground = new SolidColorBrush(Color.FromRgb(160, 172, 188)), Margin = new Thickness(0, 1, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(text, 1);
            row.Children.Add(text);

            var body = new Grid();
            body.Children.Add(new Border
            {
                Height = 30, VerticalAlignment = VerticalAlignment.Top, CornerRadius = new CornerRadius(15, 15, 0, 0), IsHitTestVisible = false,
                Background = new LinearGradientBrush(Color.FromArgb(24, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90)
            });
            body.Children.Add(new Border { Padding = new Thickness(12, 10, 18, 10), Child = row });
            return new Border
            {
                Width = CardWidth, Margin = new Thickness(12, 6, 12, 6), CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1),
                Background = StandingsView.GlassFill(alpha), BorderBrush = StandingsView.GlassRim(),
                Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Opacity = 0.45, Color = Colors.Black },
                Child = body
            };
        }
    }
}
