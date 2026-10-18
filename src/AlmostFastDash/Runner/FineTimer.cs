using System;
using System.Runtime.InteropServices;

namespace AlmostFastDash.Runner
{
    /// <summary>
    /// Windows wakes a waiting thread only on its timer tick, every 15.6 ms unless a program asks for finer, so
    /// a 16 ms frame wait often lasts 31 ms and the dashboard tops out near 32 frames a second. While the
    /// dashboard runs this asks for 1 ms ticks (for this process alone since Windows 10 2004), and asks Windows
    /// 11 to keep honouring that although the dashboard process has no window on screen.
    /// </summary>
    internal sealed class FineTimer : IDisposable
    {
        [DllImport("winmm.dll")]
        private static extern uint timeBeginPeriod(uint milliseconds);

        [DllImport("winmm.dll")]
        private static extern uint timeEndPeriod(uint milliseconds);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessInformation(IntPtr process, int informationClass, ref PowerThrottlingState information, int size);

        [StructLayout(LayoutKind.Sequential)]
        private struct PowerThrottlingState
        {
            public uint Version, ControlMask, StateMask;
        }

        private const int ProcessPowerThrottling = 4;
        private const uint IgnoreTimerResolution = 0x4;
        private bool _on;

        public FineTimer()
        {
            try
            {
                // Taking charge of "ignore timer resolution" and leaving it off: the request below always counts.
                var state = new PowerThrottlingState { Version = 1, ControlMask = IgnoreTimerResolution, StateMask = 0 };
                SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling, ref state, Marshal.SizeOf(typeof(PowerThrottlingState)));
            }
            catch { }
            try { _on = timeBeginPeriod(1) == 0; }
            catch { }
        }

        public void Dispose()
        {
            if (!_on) return;
            _on = false;
            try { timeEndPeriod(1); }
            catch { }
        }
    }
}
