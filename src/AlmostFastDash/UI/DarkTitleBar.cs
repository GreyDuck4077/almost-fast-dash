using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AlmostFastDash.UI
{
    /// <summary>
    /// Every window the app opens, its dialogs included, gets a dark title bar in the app's own background color,
    /// so nothing flashes a light Windows frame in the middle of a dark app.
    /// </summary>
    internal static class DarkTitleBar
    {
        private static bool _registered;

        public static void ForEveryWindow()
        {
            if (_registered) return;
            _registered = true;
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
                new RoutedEventHandler((s, e) => Apply(s as Window)));
        }

        public static void Apply(Window window)
        {
            if (window == null) return;
            try
            {
                IntPtr handle = new WindowInteropHelper(window).Handle;
                if (handle == IntPtr.Zero) return;
                int dark = 1;
                DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
                int caption = 0x00130E0B; // COLORREF (BGR) of Brush.Bg #0B0E13
                DwmSetWindowAttribute(handle, 35, ref caption, sizeof(int));
            }
            catch { }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
