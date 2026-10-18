using System;
using System.Collections.Generic;

namespace AlmostFastDash.Rendering
{
    /// <summary>What the flag system shows at one moment: a flag over the screen, a frame round the dashboard, or both while one hands over to the other.</summary>
    internal struct FlagView
    {
        /// <summary>The flag covering the screen, or None.</summary>
        public RacingFlag Takeover;
        /// <summary>How opaque that flag is: 0 gone, 1 the whole screen.</summary>
        public float Alpha;
        /// <summary>The flag whose colored frame runs round the dashboard, or None.</summary>
        public RacingFlag Frame;
        public float FrameAlpha;

        public bool Full => Takeover != RacingFlag.None && Alpha >= 0.999f;
    }

    /// <summary>
    /// Turns the telemetry's flag into what the screen shows. A new flag fades in over the whole screen and
    /// holds steady: the telemetry's on-off flashing is for the lights, so its short gaps are bridged here.
    /// Flags that stay out (black, repairs, blue, surface) step aside after a few seconds to a frame in their
    /// color round the dashboard; so do a caution period and the final lap once their alert is over. Red
    /// and checkered keep the screen.
    /// </summary>
    internal sealed class FlagTimeline
    {
        public const int FadeIn = 180, FadeOut = 280, TakeoverMilliseconds = 3000, StepAside = 450;
        /// <summary>A flag that stays out and comes back within this long (a blue flag for each faster car) goes straight to its frame.</summary>
        public const int Remember = 20000;

        private readonly int _grace;
        private RacingFlag _flag;
        private int _since, _lastSeen;
        private readonly Dictionary<RacingFlag, int> _seen = new Dictionary<RacingFlag, int>();

        /// <param name="flashMilliseconds">How long each flash of the lights lasts: gaps that short don't end a flag.</param>
        public FlagTimeline(int flashMilliseconds) => _grace = Math.Max(400, flashMilliseconds + 200);

        /// <summary>Flags that can stay out for a long time, so they hand the screen back after their alert.</summary>
        public static bool StaysOut(RacingFlag flag) =>
            flag == RacingFlag.Black || flag == RacingFlag.Meatball || flag == RacingFlag.BlueYellowStripe ||
            flag == RacingFlag.Surface || flag == RacingFlag.SafetyCar;

        /// <summary>Forgets every flag, so the next one alerts in full (the designer's preview loops this way).</summary>
        public void Reset()
        {
            _flag = RacingFlag.None;
            _seen.Clear();
        }

        /// <param name="takeovers">Flags may cover the screen.</param>
        /// <param name="frames">Flags that are out may frame the dashboard.</param>
        public FlagView At(TelemetrySnapshot t, int now, bool takeovers, bool frames)
        {
            RacingFlag flag = t?.Flag ?? RacingFlag.None;
            if (flag != RacingFlag.None)
            {
                if (flag != _flag)
                {
                    // Straight from one flag to another: the new one doesn't fade in again from nothing.
                    _since = _flag != RacingFlag.None && Alpha(now) > 0.5f ? now - FadeIn : now;
                    if (StaysOut(flag) && _seen.TryGetValue(flag, out int last) && now - last < Remember)
                        _since = now - TakeoverMilliseconds - StepAside;
                    _flag = flag;
                }
                _lastSeen = now;
                _seen[flag] = now;
            }
            else if (_flag != RacingFlag.None && now - _lastSeen > _grace + FadeOut)
                _flag = RacingFlag.None;

            var view = new FlagView();
            if (takeovers && _flag != RacingFlag.None)
            {
                view.Alpha = Alpha(now);
                if (view.Alpha > 0) view.Takeover = _flag;
            }
            if (frames)
            {
                if (_flag != RacingFlag.None && (StaysOut(_flag) || !takeovers))
                {
                    view.Frame = _flag;
                    view.FrameAlpha = Ease(Clamp((now - _since) / (float)FadeIn) * Presence(now));
                }
                else if (t != null && t.CautionActive)
                {
                    view.Frame = RacingFlag.Yellow;
                    view.FrameAlpha = 1;
                }
                else if (t != null && t.WhiteFlagActive)
                {
                    view.Frame = RacingFlag.White;
                    view.FrameAlpha = 1;
                }
                if (view.FrameAlpha <= 0) view.Frame = RacingFlag.None;
            }
            return view;
        }

        private float Alpha(int now)
        {
            if (_flag == RacingFlag.None) return 0;
            int age = now - _since;
            float alpha = Clamp(age / (float)FadeIn);
            if (StaysOut(_flag)) alpha = Math.Min(alpha, 1 - Clamp((age - TakeoverMilliseconds) / (float)StepAside));
            return Ease(alpha * Presence(now));
        }

        /// <summary>1 while the flag is out (or only between two flashes), falling to 0 once it has been withdrawn.</summary>
        private float Presence(int now)
        {
            int gone = now - _lastSeen;
            return gone <= _grace ? 1 : 1 - Clamp((gone - _grace) / (float)FadeOut);
        }

        private static float Clamp(float v) => v < 0 ? 0 : v > 1 ? 1 : v;

        private static float Ease(float v) => v * v * (3 - 2 * v);
    }
}
