using System;
using System.Collections;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AlmostFastOverlays
{
    /// <summary>
    /// The app's motion: short, eased entrances and hover lifts, so moving around feels deliberate.
    /// All of it follows Windows' own "Animation effects" setting and is skipped when that is off.
    /// </summary>
    internal static class Motion
    {
        private static readonly IEasingFunction Ease = new QuinticEase { EasingMode = EasingMode.EaseOut };

        public static bool On => SystemParameters.ClientAreaAnimation;

        /// <summary>Fades an element in while it slides the last few pixels into place, after an optional delay.</summary>
        public static void Enter(UIElement element, double fromX, double fromY, int delayMs = 0, int durationMs = 340)
        {
            if (element == null) return;
            if (!On)
            {
                element.BeginAnimation(UIElement.OpacityProperty, null);
                return;
            }
            var shift = element.RenderTransform as TranslateTransform;
            if (shift == null)
            {
                // Leave any other transform alone; fading is enough then.
                if (element.RenderTransform == null || element.RenderTransform == Transform.Identity) element.RenderTransform = shift = new TranslateTransform();
            }
            TimeSpan delay = TimeSpan.FromMilliseconds(delayMs), length = TimeSpan.FromMilliseconds(durationMs);
            // Key frames hold the element hidden through its delay, so nothing flickers before it moves in.
            var fade = new DoubleAnimationUsingKeyFrames();
            fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(delay)));
            fade.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(delay + TimeSpan.FromMilliseconds(durationMs * 0.75)), Ease));
            element.BeginAnimation(UIElement.OpacityProperty, fade);
            if (shift == null) return;
            shift.BeginAnimation(TranslateTransform.XProperty, Slide(fromX, delay, length));
            shift.BeginAnimation(TranslateTransform.YProperty, Slide(fromY, delay, length));
        }

        private static AnimationTimeline Slide(double from, TimeSpan delay, TimeSpan length)
        {
            var slide = new DoubleAnimationUsingKeyFrames();
            slide.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            slide.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(delay)));
            slide.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(delay + length), Ease));
            return slide;
        }

        /// <summary>Brings a list in one item after another, quickly, capped so long lists don't drag.</summary>
        public static void Stagger(IEnumerable elements, int stepMs = 35, int maxDelayMs = 280)
        {
            int i = 0;
            foreach (object item in elements)
                if (item is UIElement element && element.Visibility == Visibility.Visible)
                    Enter(element, 0, 10, Math.Min(maxDelayMs, i++ * stepMs), 360);
        }

        /// <summary>A gentle fade for something just shown, such as an opened folder or group.</summary>
        public static void FadeIn(UIElement element, int durationMs = 180)
        {
            if (element == null || !On) return;
            element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(durationMs)) { EasingFunction = Ease });
        }

        /// <summary>Lifts a card a couple of pixels under the mouse and settles it back.</summary>
        public static void Lift(FrameworkElement card)
        {
            if (card == null) return;
            var shift = card.RenderTransform as TranslateTransform ?? new TranslateTransform();
            card.RenderTransform = shift;
            card.MouseEnter += (s, e) => { if (On) shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-3, TimeSpan.FromMilliseconds(160)) { EasingFunction = Ease }); };
            card.MouseLeave += (s, e) => { if (On) shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(220)) { EasingFunction = Ease }); };
        }
    }
}
