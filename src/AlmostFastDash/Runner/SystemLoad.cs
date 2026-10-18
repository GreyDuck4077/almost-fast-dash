using System;
using System.Runtime.InteropServices;

namespace AlmostFastDash.Runner
{
    /// <summary>
    /// The PC's CPU and memory use, for dashboards that show them. Read straight from Windows on
    /// demand and at most once a second, so nothing runs when no dashboard asks.
    /// </summary>
    internal static class SystemLoad
    {
        private static readonly object Gate = new object();
        private static long _idle, _kernel, _user;
        private static DateTime _nextUtc;
        private static double _cpu, _memory;

        /// <summary>CPU use across all cores, 0 to 100.</summary>
        public static double Cpu { get { Refresh(); return _cpu; } }

        /// <summary>Memory in use, 0 to 100.</summary>
        public static double Memory { get { Refresh(); return _memory; } }

        private static void Refresh()
        {
            lock (Gate)
            {
                if (DateTime.UtcNow < _nextUtc) return;
                _nextUtc = DateTime.UtcNow.AddSeconds(1);
                if (GetSystemTimes(out long idle, out long kernel, out long user))
                {
                    // Kernel time includes idle time.
                    // The first reading is the average since Windows started, so it is sensible straight away.
                    long total = kernel - _kernel + user - _user, rest = idle - _idle;
                    if (total > 0) _cpu = Math.Max(0, Math.Min(100, (total - rest) * 100.0 / total));
                    _idle = idle;
                    _kernel = kernel;
                    _user = user;
                }
                var status = new MemoryStatus { Length = (uint)Marshal.SizeOf(typeof(MemoryStatus)) };
                if (GlobalMemoryStatusEx(ref status)) _memory = status.MemoryLoad;
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatus
        {
            public uint Length, MemoryLoad;
            public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    }
}
