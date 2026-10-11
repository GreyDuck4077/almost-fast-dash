using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace AlmostFastOverlays
{
    /// <summary>
    /// --overlay-fps: opens the real overlay windows (Standings, Relative, Toasts) locked
    /// and click-through as in a race, keeps the field reshuffling so rows slide and
    /// toasts arrive, and reports how smoothly frames arrive and what it costs.
    /// </summary>
    internal static class OverlayBench
    {
        public static int Run(string output)
        {
            OverlayHost.LimitAnimationRate();
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var report = new List<string> { "Render tier: " + (RenderCapability.Tier >> 16) + " (2 = full GPU), animations up to " + OverlayHost.AnimationFrameRate + " fps" };

            var windows = new List<OverlayWindow>();
            int index = 0;
            foreach (string type in new[] { "standings", "relative", "toasts" })
            {
                var config = new OverlayConfig { Id = type, Type = type, Name = type, Enabled = true };
                var window = new OverlayWindow(config) { Left = 40 + index * 620, Top = 60 + (type == "toasts" ? 560 : 0) };
                index++;
                window.Show(true);
                window.SetLocked(true);
                windows.Add(window);
            }

            // Data every 100 ms with the clock running; the running order changes every
            // "shuffle" ticks (worst case: constantly; typical: a pass every few seconds).
            int seed = 0, tick = 0, shuffle = 4, toastEvery = 15;
            var data = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(100) };
            data.Tick += (s, e) =>
            {
                if (++tick % shuffle == 0) seed++;
                StandingsSnapshot standings = StandingsSample.Create(seed, 1208 + tick * 0.1);
                foreach (OverlayWindow w in windows)
                {
                    if (w.View is ToastView toasts) { if (tick % toastEvery == 0) toasts.Show(ToastView.Samples[tick / toastEvery % ToastView.Samples.Length]); }
                    else w.Update(null, standings, false);
                }
            };
            data.Start();

            Wait(1.5);
            // Smoothness: every frame the display could show is counted (this keeps WPF drawing continuously).
            var frames = new List<double>();
            double last = -1;
            EventHandler rendering = (s, e) =>
            {
                double now = ((RenderingEventArgs)e).RenderingTime.TotalMilliseconds;
                if (now == last) return;
                if (last >= 0) frames.Add(now - last);
                last = now;
            };
            var clock = Stopwatch.StartNew();
            CompositionTarget.Rendering += rendering;
            Wait(6);
            CompositionTarget.Rendering -= rendering;
            double seconds = clock.Elapsed.TotalSeconds;
            frames.Sort();
            report.Add(frames.Count == 0 ? "No frames" : string.Format(
                "Smoothness: {0:0} fps average, 95% of frames within {1:0.0} ms, {2} frames over 25 ms in {3:0} s",
                frames.Count / seconds, frames[(int)(frames.Count * 0.95)], frames.Count(f => f > 25), seconds));

            // Cost: as in a race, WPF draws only when something changes.
            double Cost(double length)
            {
                double cpu0 = Process.GetCurrentProcess().TotalProcessorTime.TotalMilliseconds;
                clock.Restart();
                Wait(length);
                return (Process.GetCurrentProcess().TotalProcessorTime.TotalMilliseconds - cpu0) / clock.Elapsed.TotalMilliseconds * 100;
            }
            Wait(1);
            report.Add(string.Format("Cost, order changing every 0.4 s and a toast every 1.5 s (worst case): {0:0.0}% of one CPU core", Cost(8)));
            shuffle = 50;
            toastEvery = 80;
            Wait(1);
            report.Add(string.Format("Cost, gaps ticking, a pass every 5 s and a toast every 8 s (typical race): {0:0.0}% of one CPU core", Cost(10)));
            report.Add(string.Format("Memory: {0} MB", Process.GetCurrentProcess().PrivateMemorySize64 / (1024 * 1024)));

            data.Stop();
            foreach (OverlayWindow w in windows) w.Close();
            File.WriteAllLines(output, report);
            app.Shutdown();
            return 0;
        }

        private static void Wait(double seconds)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
            timer.Tick += (s, e) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }
    }
}
