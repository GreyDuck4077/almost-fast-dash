using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingRectangle = System.Drawing.Rectangle;

namespace AlmostFastOverlays
{
    /// <summary>A display the dashboard can be shown on: any but the main one.</summary>
    internal sealed class MonitorChoice
    {
        /// <summary>Windows' name for it, such as \\.\DISPLAY2: what Settings keep.</summary>
        public string Device;
        public string Label;
        public DrawingRectangle Bounds;

        /// <summary>The displays other than the main one, as Windows numbers them.</summary>
        public static List<MonitorChoice> All() => System.Windows.Forms.Screen.AllScreens
            .Where(s => !s.Primary)
            .Select(s => new MonitorChoice
            {
                Device = s.DeviceName,
                Label = "Display " + new string(s.DeviceName.Where(char.IsDigit).ToArray()) + " · " + s.Bounds.Width + " × " + s.Bounds.Height,
                Bounds = s.Bounds
            })
            .OrderBy(c => c.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Shows the running dashboard full screen on another display, such as an HDMI dash screen or a spare monitor.
    /// The window has its own thread, never takes the keyboard or mouse from the game, and is scaled by the
    /// graphics card, so the dashboard is still drawn once at its own size whatever the display's. Clicks and
    /// touches on it work like taps on a touch wheel screen.
    /// </summary>
    internal sealed class MonitorOutput : IDisposable
    {
        private readonly Thread _thread;
        private readonly object _gate = new object();
        private readonly ConcurrentQueue<System.Drawing.PointF> _taps = new ConcurrentQueue<System.Drawing.PointF>();
        private readonly ManualResetEventSlim _started = new ManualResetEventSlim(false);
        private Dispatcher _dispatcher;
        private Image _image;
        private WriteableBitmap _bitmap;
        private int[] _pixels = new int[0];
        private int _width, _height;
        private volatile bool _queued;

        private MonitorOutput(MonitorChoice display)
        {
            Device = display.Device;
            _thread = new Thread(() => Run(display.Bounds)) { IsBackground = true, Name = "Monitor" };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            _started.Wait(5000);
        }

        /// <summary>The display it's on.</summary>
        public string Device { get; }

        /// <summary>Not still showing the last frame: the moment to give it the next.</summary>
        public bool Ready => !_queued;

        /// <summary>
        /// The output that should be showing now: the one there is when it's still on the chosen display, a new
        /// one on it, or none (with the old one closed) when nothing's chosen or that display is unplugged.
        /// </summary>
        public static MonitorOutput Follow(MonitorOutput current, string device)
        {
            MonitorChoice display = string.IsNullOrEmpty(device) ? null : MonitorChoice.All().FirstOrDefault(c => c.Device == device);
            if (current != null && display != null && current.Device == display.Device) return current;
            current?.Dispose();
            if (display == null) return null;
            Console.WriteLine("Showing the dashboard on " + display.Label);
            return new MonitorOutput(display);
        }

        /// <summary>Takes the next frame; the bitmap can be reused straight away.</summary>
        public void Submit(DrawingBitmap frame)
        {
            if (_dispatcher == null) return;
            lock (_gate)
            {
                int count = frame.Width * frame.Height;
                if (_pixels.Length != count) _pixels = new int[count];
                FrameEncoder.Copy(frame, _pixels);
                _width = frame.Width;
                _height = frame.Height;
            }
            if (_queued) return;
            _queued = true;
            _dispatcher.BeginInvoke(new Action(Present), DispatcherPriority.Render);
        }

        /// <summary>The next tap, as a share of the frame's width and height (0 to 1).</summary>
        public bool TryTakeTap(out System.Drawing.PointF tap) => _taps.TryDequeue(out tap);

        private void Run(DrawingRectangle bounds)
        {
            _image = new Image { Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
            _image.MouseLeftButtonUp += (s, e) =>
            {
                Point at = e.GetPosition(_image);
                if (_image.ActualWidth > 0 && _image.ActualHeight > 0)
                    _taps.Enqueue(new System.Drawing.PointF((float)(at.X / _image.ActualWidth), (float)(at.Y / _image.ActualHeight)));
            };
            var window = new Window
            {
                Title = "Almost Fast Dash",
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                ShowActivated = false,
                Topmost = true,
                Background = Brushes.Black,
                Cursor = Cursors.None,
                Content = _image
            };
            window.SourceInitialized += (s, e) =>
            {
                IntPtr handle = new WindowInteropHelper(window).Handle;
                // Never the active window, never in Alt+Tab: the game keeps the keyboard and mouse.
                SetWindowLong(handle, ExStyle, GetWindowLong(handle, ExStyle) | NoActivate | ToolWindow);
                // Placed in pixels over the whole display, whatever its scaling.
                SetWindowPos(handle, TopMost, bounds.X, bounds.Y, bounds.Width, bounds.Height, NoActivateMove | ShowWindow);
            };
            window.Show();
            _dispatcher = Dispatcher.CurrentDispatcher;
            _started.Set();
            Dispatcher.Run();
            window.Close();
        }

        private void Present()
        {
            lock (_gate)
            {
                if (_bitmap == null || _bitmap.PixelWidth != _width || _bitmap.PixelHeight != _height)
                {
                    _bitmap = new WriteableBitmap(_width, _height, 96, 96, PixelFormats.Bgra32, null);
                    _image.Source = _bitmap;
                }
                _bitmap.WritePixels(new Int32Rect(0, 0, _width, _height), _pixels, _width * 4, 0);
            }
            _queued = false;
        }

        public void Dispose()
        {
            _dispatcher?.InvokeShutdown();
            _thread.Join(2000);
            _started.Dispose();
        }

        private const int ExStyle = -20, NoActivate = 0x08000000, ToolWindow = 0x00000080;
        private const uint NoActivateMove = 0x0010, ShowWindow = 0x0040;
        private static readonly IntPtr TopMost = new IntPtr(-1);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong(IntPtr window, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong(IntPtr window, int index, int value);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    }
}
