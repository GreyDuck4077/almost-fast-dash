using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace AlmostFastOverlays
{
    /// <summary>One composed set of LED colours, plus which layer lit each LED.</summary>
    internal sealed class LightFrame
    {
        public Color[] Dv5 = new Color[LedFrame.Count];
        public Color[] Brow;
        public string[] Dv5Owner = new string[LedFrame.Count];
        public string[] BrowOwner;
    }

    /// <summary>
    /// Turns a <see cref="LightProfile"/> and live telemetry into LED colours.
    /// Layers are drawn bottom to top, so the top of the list wins; then master
    /// brightness, fade-out and gamma are applied. Keeps fade state, so use one
    /// instance per output (the device loop or one preview).
    /// </summary>
    internal sealed class LightRenderer
    {
        private const double Gamma = 2.2;
        private readonly LightProfile _profile;
        private readonly string _accent;
        private readonly UnitSystem _units;
        private readonly int _browCount;
        private readonly LayoutTheme _theme = new LayoutTheme();
        private readonly double[] _dv5State = new double[LedFrame.Count * 3];
        private readonly double[] _browState;
        private int _lastTick;
        private bool _started;

        public LightRenderer(LightProfile profile, string accent, UnitSystem units, int browCount)
        {
            _profile = profile ?? new LightProfile();
            _profile.Normalize();
            _accent = accent ?? "#2DD4BF";
            _units = units ?? UnitSystem.Default;
            _browCount = Math.Max(1, Math.Min(64, browCount));
            _browState = new double[_browCount * 3];
        }

        public LightProfile Profile => _profile;
        public int BrowCount => _browCount;

        /// <summary>Layer ids whose condition or properties failed on the last frame, with the reason.</summary>
        public Dictionary<string, string> Errors { get; } = new Dictionary<string, string>();

        public LightFrame Compose(TelemetrySnapshot t, int now)
        {
            t = t ?? TelemetrySnapshot.Disconnected;
            var scope = new EvalScope
            {
                T = t, Units = _units, Theme = _theme, Accent = _accent, Now = DateTime.Now,
                ShiftFlashOn = DataCatalog.AtShiftPoint(t) && ((now / 140) & 1) == 0
            };
            var dv5 = new Color?[LedFrame.Count];
            var brow = new Color?[_browCount];
            var frame = new LightFrame { Brow = new Color[_browCount], BrowOwner = new string[_browCount] };
            Errors.Clear();

            // Bottom of the list first, so higher layers paint over it.
            for (int l = _profile.Layers.Count - 1; l >= 0; l--)
            {
                LightLayer layer = _profile.Layers[l];
                if (!layer.Enabled) continue;
                try
                {
                    if (!string.IsNullOrWhiteSpace(layer.Condition) && !Expr.Bool(Expr.Evaluate(LayoutRenderer.Strip(layer.Condition), scope)))
                        continue;
                    LightEffect effect = LightEffects.Find(layer.Effect);
                    LedTarget target = LedTargets.Find(layer.Target);
                    if (effect == null || target == null) continue;
                    List<LedRef[]> strips = target.Strips(_browCount);
                    double level = layer.Brightness / 100.0;
                    bool replace = layer.Blend == "Replace";
                    for (int s = 0; s < strips.Count; s++)
                    {
                        LedRef[] strip = strips[s];
                        var context = new LightContext { Scope = scope, Layer = layer, Effect = effect, Target = target, Strip = s, StripCount = strips.Count, Length = strip.Length, Now = now };
                        Color?[] colors = effect.Render(context);
                        for (int i = 0; i < strip.Length && i < colors.Length; i++)
                        {
                            Color? c = colors[i];
                            bool lit = c.HasValue && (c.Value.R | c.Value.G | c.Value.B) != 0;
                            if (!lit && !replace) continue;
                            Color value = lit ? LightEffects.Scale(c.Value, level) : Color.Black;
                            LedRef led = strip[i];
                            if (led.Device == 0 && led.Index < dv5.Length) { dv5[led.Index] = value; frame.Dv5Owner[led.Index] = layer.Name; }
                            else if (led.Device == 1 && led.Index < brow.Length) { brow[led.Index] = value; frame.BrowOwner[led.Index] = layer.Name; }
                        }
                    }
                }
                catch (Exception exception)
                {
                    Errors[layer.Id] = exception.Message;
                }
            }

            double dt = _started ? Math.Max(0, Math.Min(5000, (now - _lastTick) & int.MaxValue)) : 0;
            _started = true;
            _lastTick = now;
            Output(dv5, _dv5State, frame.Dv5, dt);
            Output(brow, _browState, frame.Brow, dt);
            return frame;
        }

        /// <summary>Master brightness, fade-out smoothing (in linear light) and gamma.</summary>
        private void Output(Color?[] composed, double[] state, Color[] output, double dt)
        {
            double master = _profile.Brightness / 100.0;
            double tau = _profile.FadeMs / 3.0;
            double keep = tau <= 0 || dt <= 0 ? (dt <= 0 && tau > 0 ? 1 : 0) : Math.Exp(-dt / tau);
            for (int i = 0; i < composed.Length; i++)
            {
                Color c = composed[i] ?? Color.Black;
                double[] target = { c.R * master, c.G * master, c.B * master };
                for (int ch = 0; ch < 3; ch++)
                {
                    int k = i * 3 + ch;
                    // Rise at once, fall smoothly.
                    state[k] = target[ch] >= state[k] ? target[ch] : target[ch] + (state[k] - target[ch]) * keep;
                    if (state[k] < 0.5 && target[ch] == 0) state[k] = 0;
                }
                output[i] = Color.FromArgb(Encode(state[i * 3]), Encode(state[i * 3 + 1]), Encode(state[i * 3 + 2]));
            }
        }

        private int Encode(double value)
        {
            value = Math.Max(0, Math.Min(255, value));
            if (_profile.Gamma) value = 255 * Math.Pow(value / 255, Gamma);
            int v = (int)Math.Round(value);
            // Keep very dim but lit LEDs at the lowest visible step instead of switching them off.
            return v == 0 && value > 0.05 ? 1 : v;
        }
    }
}
