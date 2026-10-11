using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace AlmostFastOverlays
{
    /// <summary>
    /// Gives a window the Windows 11 acrylic backdrop: the desktop behind it shows
    /// through, blurred, under a dark tint, like frosted glass. On systems without
    /// it the window keeps its solid background.
    /// </summary>
    internal static class GlassBackdrop
    {
        private const int DarkMode = 20, SystemBackdropType = 38, Acrylic = 3;

        public static void Apply(Window window, byte tintAlpha = 0xA6)
        {
            window.SourceInitialized += (s, e) => Enable(window, tintAlpha);
        }

        private static void Enable(Window window, byte tintAlpha)
        {
            try
            {
                IntPtr handle = new WindowInteropHelper(window).Handle;
                int on = 1;
                DwmSetWindowAttribute(handle, DarkMode, ref on, sizeof(int));
                int backdrop = Acrylic;
                if (DwmSetWindowAttribute(handle, SystemBackdropType, ref backdrop, sizeof(int)) != 0) return;
                HwndSource source = HwndSource.FromHwnd(handle);
                if (source?.CompositionTarget == null) return;
                source.CompositionTarget.BackgroundColor = Colors.Transparent;
                var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                DwmExtendFrameIntoClientArea(handle, ref margins);
                window.Background = new SolidColorBrush(Color.FromArgb(tintAlpha, 8, 11, 16));
            }
            catch
            {
                // Older Windows: keep the solid background.
            }
        }

        /// <summary>
        /// Makes a borderless window see-through where nothing is drawn, with the
        /// compositor (DWM) blending it on the GPU. Unlike WPF's AllowsTransparency,
        /// which copies the whole window back from the GPU on every frame, this keeps
        /// animations at the display's refresh rate for a fraction of the CPU.
        /// </summary>
        public static void MakeClear(Window window)
        {
            window.AllowsTransparency = false;
            window.WindowStyle = WindowStyle.None;
            window.ResizeMode = ResizeMode.NoResize;
            window.Background = Brushes.Transparent;
            window.SourceInitialized += (s, e) =>
            {
                IntPtr handle = new WindowInteropHelper(window).Handle;
                HwndSource source = HwndSource.FromHwnd(handle);
                if (source?.CompositionTarget != null) source.CompositionTarget.BackgroundColor = Colors.Transparent;
                var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                DwmExtendFrameIntoClientArea(handle, ref margins);
                // Windows 11: no rounded corners or accent border around an overlay.
                int square = DoNotRound;
                DwmSetWindowAttribute(handle, CornerPreference, ref square, sizeof(int));
                int noBorder = ColorNone;
                DwmSetWindowAttribute(handle, BorderColor, ref noBorder, sizeof(int));
            };
        }

        private const int CornerPreference = 33, DoNotRound = 1, BorderColor = 34;
        private const int ColorNone = unchecked((int)0xFFFFFFFE);

        [StructLayout(LayoutKind.Sequential)]
        private struct Margins { public int Left, Right, Top, Bottom; }

        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
    }
}
