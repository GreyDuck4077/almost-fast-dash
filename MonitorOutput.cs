using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingRectangle = System.Drawing.Rectangle;
using DrawingSize = System.Drawing.Size;

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
    /// Shows the running dashboard on the PC, so it needs no wheel screen: full screen on another display (such as
    /// an HDMI dash screen), in a window of its own, or over the game. Each has its own thread and is scaled and
    /// composed by the graphics card, so the dashboard is still drawn once at its own size; the only work per
    /// frame is one copy of its pixels. None of them takes the keyboard from the game. Clicks on the full-screen
    /// and windowed dashboard work like taps on a touch wheel screen; over the game they go through to the game.
    /// </summary>
    internal sealed class MonitorOutput : IDisposable
    {
        public const string FullScreen = "FullScreen", Windowed = "Window", Overlay = "Overlay";

        private readonly Thread _thread;
        private readonly object _gate = new object();
        private readonly ConcurrentQueue<System.Drawing.PointF> _taps = new ConcurrentQueue<System.Drawing.PointF>();
        private readonly ManualResetEventSlim _started = new ManualResetEventSlim(false);
        private readonly double _aspect;
        private Dispatcher _dispatcher;
        private Window _window;
        private IntPtr _handle;
        private Image _image;
        private FrameworkElement _placing;
        private WriteableBitmap _bitmap;
        private int[] _pixels = new int[0];
        private int _width, _height;
        // What changed since the window last showed a frame, and whether it needs the whole frame next.
        private DrawingRectangle _dirty;
        private bool _whole = true;
        private volatile bool _queued, _closing;
        // Wanted on screen (Show when, and over the game while racing), and minimised (a window only).
        private volatile bool _wanted, _minimised;
        private bool _unlocked;
        private int _opacity = 100;
        private string _title;
        private DrawingRectangle _place;

        private MonitorOutput(string mode, MonitorChoice display, DrawingSize dashboard, string title)
        {
            Mode = mode;
            Device = display?.Device;
            DashboardSize = dashboard;
            _aspect = dashboard.Width > 0 && dashboard.Height > 0 ? (double)dashboard.Width / dashboard.Height : 16 / 9.0;
            _title = title;
            // Over the game it waits to be shown, so it never flashes up over the desktop.
            _wanted = mode != Overlay;
            DrawingRectangle place = mode == FullScreen ? display.Bounds : WindowPlacement.Resolve(WindowPlacement.Load(mode), mode, dashboard,
                System.Windows.Forms.Screen.AllScreens.Select(s => s.Bounds).ToArray(), System.Windows.Forms.Screen.PrimaryScreen.WorkingArea);
            _place = place;
            _thread = new Thread(Run) { IsBackground = true, Name = "Dashboard window" };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            _started.Wait(5000);
        }

        /// <summary>FullScreen, Window or Overlay.</summary>
        public string Mode { get; }

        /// <summary>The display it's on, full screen.</summary>
        public string Device { get; }

        /// <summary>The size of the dashboard it was made for; another shape needs another window.</summary>
        public DrawingSize DashboardSize { get; }

        /// <summary>
        /// On screen now: not hidden by Show when (or, over the game, while iRacing isn't running), and not a
        /// minimised window. Nothing is drawn for it otherwise.
        /// </summary>
        public bool Showing => _wanted && !_minimised;

        /// <summary>Goes up each time it shows again after hiding: the moment it needs a whole frame.</summary>
        public int Generation { get; private set; }

        /// <summary>Not still showing the last frame: the moment to give it the next.</summary>
        public bool Ready => !_queued;

        /// <summary>
        /// The output that should be showing now for the settings: the one there is when it still fits, a new one,
        /// or none (with the old one closed) when it's off or its display is unplugged.
        /// </summary>
        public static MonitorOutput Follow(MonitorOutput current, GeneralSettings general, string dashboardName, DrawingSize dashboard)
        {
            string mode = general.EffectivePcOutput();
            MonitorChoice display = mode == FullScreen && !string.IsNullOrEmpty(general.MonitorDevice)
                ? MonitorChoice.All().FirstOrDefault(c => c.Device == general.MonitorDevice) : null;
            bool wanted = mode == Windowed || mode == Overlay || display != null;
            if (current != null && wanted && current.Mode == mode && current.Device == display?.Device &&
                (mode == FullScreen || current.DashboardSize == dashboard))
            {
                current.Retitle(dashboardName);
                return current;
            }
            current?.Dispose();
            if (!wanted) return null;
            Console.WriteLine("Showing the dashboard " + (mode == FullScreen ? "on " + display.Label : mode == Windowed ? "in a window" : "over the game"));
            return new MonitorOutput(mode, display, dashboard, dashboardName);
        }

        /// <summary>
        /// Whether it's on screen (a window or over the game goes away while hidden; full screen goes black), and over
        /// the game how opaque (30 to 100) and whether it's unlocked to be moved and resized (then it takes the
        /// mouse, and shows a frame and a corner to drag). Cheap when nothing changed.
        /// </summary>
        public void Update(int opacity, bool show, bool unlocked)
        {
            if (_dispatcher == null) return;
            if (Mode != Overlay)
            {
                opacity = 100;
                unlocked = false;
            }
            opacity = Math.Max(30, Math.Min(100, opacity));
            show |= unlocked;
            if (opacity == _opacity && show == _wanted && unlocked == _unlocked) return;
            bool was = Showing;
            _opacity = opacity;
            _unlocked = unlocked;
            _wanted = show;
            if (!was && Showing) Appear();
            _dispatcher.BeginInvoke(new Action(() => Apply(opacity, show, unlocked)));
        }

        /// <summary>Shown again (or restored): it gets the whole of the next frame, as nothing was copied while it was away.</summary>
        private void Appear()
        {
            lock (_gate) _whole = true;
            Generation++;
        }

        /// <summary>
        /// Takes the next frame, of which only <paramref name="changed"/> differs from the one before: just that is
        /// copied, and just that goes to the graphics card. The bitmap can be reused straight away.
        /// </summary>
        public void Submit(DrawingBitmap frame, DrawingRectangle changed)
        {
            if (_dispatcher == null || !Showing) return;
            lock (_gate)
            {
                int count = frame.Width * frame.Height;
                if (_pixels.Length != count || _width != frame.Width)
                {
                    _pixels = new int[count];
                    _whole = true;
                }
                _width = frame.Width;
                _height = frame.Height;
                var full = new DrawingRectangle(0, 0, _width, _height);
                DrawingRectangle area = _whole ? full : DrawingRectangle.Intersect(changed, full);
                if (area.IsEmpty) return;
                FrameEncoder.CopyArea(frame, _pixels, area);
                _dirty = _dirty.IsEmpty ? area : DrawingRectangle.Union(_dirty, area);
                _whole = false;
            }
            if (_queued) return;
            _queued = true;
            _dispatcher.BeginInvoke(new Action(Present), DispatcherPriority.Render);
        }

        /// <summary>The next tap, as a share of the frame's width and height (0 to 1).</summary>
        public bool TryTakeTap(out System.Drawing.PointF tap) => _taps.TryDequeue(out tap);

        private void Retitle(string dashboardName)
        {
            if (Mode != Windowed || dashboardName == _title || _dispatcher == null) return;
            _title = dashboardName;
            _dispatcher.BeginInvoke(new Action(() => { if (_window != null) _window.Title = WindowTitle(dashboardName); }));
        }

        private static string WindowTitle(string dashboardName) => "Almost Fast Dash · " + dashboardName;

        private void Run()
        {
            _image = new Image { Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
            var content = new Grid();
            content.Children.Add(_image);
            if (Mode != Overlay)
                _image.MouseLeftButtonUp += (s, e) =>
                {
                    Point at = e.GetPosition(_image);
                    if (_image.ActualWidth > 0 && _image.ActualHeight > 0)
                        _taps.Enqueue(new System.Drawing.PointF((float)(at.X / _image.ActualWidth), (float)(at.Y / _image.ActualHeight)));
                };

            _window = new Window
            {
                Title = Mode == Windowed ? WindowTitle(_title) : "Almost Fast Dash",
                ShowInTaskbar = Mode == Windowed,
                ShowActivated = false,
                Content = content
            };
            switch (Mode)
            {
                case FullScreen:
                    _window.WindowStyle = WindowStyle.None;
                    _window.ResizeMode = ResizeMode.NoResize;
                    _window.Topmost = true;
                    _window.Background = Brushes.Black;
                    _window.Cursor = Cursors.None;
                    break;
                case Windowed:
                    _window.WindowStyle = WindowStyle.SingleBorderWindow;
                    _window.ResizeMode = ResizeMode.CanResize;
                    _window.Background = Brushes.Black;
                    _window.Icon = AppIcon();
                    // It stays while the dashboard runs; Settings turn it off.
                    _window.Closing += (s, e) => e.Cancel = !_closing;
                    // Minimised, nobody sees it: no need to draw for it.
                    _window.StateChanged += (s, e) =>
                    {
                        bool was = Showing;
                        _minimised = _window.WindowState == WindowState.Minimized;
                        if (!was && Showing) Appear();
                    };
                    break;
                default:
                    _window.WindowStyle = WindowStyle.None;
                    _window.ResizeMode = ResizeMode.NoResize;
                    _window.Topmost = true;
                    _window.Background = Brushes.Transparent;
                    _placing = PlacingFrame();
                    content.Children.Add(_placing);
                    content.MouseLeftButtonDown += (s, e) =>
                    {
                        if (!_unlocked || e.OriginalSource is Thumb) return;
                        try { _window.DragMove(); } catch (InvalidOperationException) { }
                        SavePlace();
                    };
                    break;
            }
            _window.SourceInitialized += (s, e) => Prepare();
            if (Mode == Overlay) new WindowInteropHelper(_window).EnsureHandle();
            else
            {
                _window.Show();
                // Showing can size the window its own way: put it back where it belongs.
                Place();
            }
            _dispatcher = Dispatcher.CurrentDispatcher;
            _started.Set();
            Dispatcher.Run();
            _closing = true;
            _window.Close();
        }

        /// <summary>Styles and places the window in pixels, whatever the display's scaling.</summary>
        private void Prepare()
        {
            _handle = new WindowInteropHelper(_window).Handle;
            HwndSource source = HwndSource.FromHwnd(_handle);
            int style = GetWindowLong(_handle, ExStyle);
            switch (Mode)
            {
                case FullScreen:
                    // Never the active window, never in Alt+Tab: the game keeps the keyboard and mouse.
                    SetWindowLong(_handle, ExStyle, style | NoActivate | ToolWindow);
                    break;
                case Windowed:
                {
                    // A normal window to move, size and find on the taskbar; only its close button is off.
                    IntPtr menu = GetSystemMenu(_handle, false);
                    if (menu != IntPtr.Zero) EnableMenuItem(menu, CloseCommand, ByCommand | Greyed);
                    source.AddHook(WindowHook);
                    break;
                }
                default:
                    // Over the game: the dashboard's own see-through parts show the game, composed by Windows on the
                    // graphics card (no layered drawing on the processor). Layered and transparent, so clicks go
                    // through to the game; never active, never in Alt+Tab.
                    source.CompositionTarget.BackgroundColor = Colors.Transparent;
                    var all = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                    DwmExtendFrameIntoClientArea(_handle, ref all);
                    // WPF takes "layered" off any window it didn't make see-through itself (that would make it draw on
                    // the processor). A window procedure in front of WPF's puts the overlay's styles back after it.
                    _overlayProc = OverlayProc;
                    _wpfProc = SetWindowProc(_handle, WindowProcIndex, Marshal.GetFunctionPointerForDelegate(_overlayProc));
                    SetWindowLong(_handle, ExStyle, OverlayStyle(style));
                    SetLayeredWindowAttributes(_handle, 0, 255, LayeredAlpha);
                    break;
            }
            Place();
        }

        /// <summary>Puts the window where it belongs, in pixels: over the display, or with its dashboard area on the saved place.</summary>
        private void Place()
        {
            if (_handle == IntPtr.Zero) return;
            DrawingRectangle place = _place;
            if (Mode == Windowed)
            {
                var outer = new Rect32 { Left = place.Left, Top = place.Top, Right = place.Right, Bottom = place.Bottom };
                AdjustWindowRectEx(ref outer, GetWindowLong(_handle, Style), false, GetWindowLong(_handle, ExStyle));
                SetWindowPos(_handle, IntPtr.Zero, outer.Left, outer.Top, outer.Right - outer.Left, outer.Bottom - outer.Top, NoActivateMove | NoZOrder);
            }
            else SetWindowPos(_handle, TopMost, place.X, place.Y, place.Width, place.Height, NoActivateMove | (_window.IsVisible ? ShowWindow : 0));
        }

        /// <summary>The extended styles over the game: never active, not in Alt+Tab, layered, and clicks through unless unlocked.</summary>
        private int OverlayStyle(int style)
        {
            style |= NoActivate | ToolWindow | Layered;
            return _unlocked ? style & ~ClickThrough : style | ClickThrough;
        }

        private delegate IntPtr WindowProc(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);
        private WindowProc _overlayProc;
        private IntPtr _wpfProc;

        /// <summary>Over the game: WPF handles each message, then whatever extended styles it set, the overlay's own stay.</summary>
        private IntPtr OverlayProc(IntPtr handle, int message, IntPtr wParam, IntPtr lParam)
        {
            IntPtr result = CallWindowProc(_wpfProc, handle, message, wParam, lParam);
            if (message == StyleChanging && wParam.ToInt32() == ExStyle)
            {
                var change = (StyleChange)Marshal.PtrToStructure(lParam, typeof(StyleChange));
                int wanted = OverlayStyle(change.New);
                if (wanted != change.New)
                {
                    change.New = wanted;
                    Marshal.StructureToPtr(change, lParam, false);
                }
            }
            return result;
        }

        /// <summary>The window's messages: taps without taking the game's focus, and sizing that keeps the dashboard's shape.</summary>
        private IntPtr WindowHook(IntPtr handle, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            switch (message)
            {
                case MouseActivate:
                    // A tap on the dashboard leaves the game active; the title bar still brings the window forward.
                    if ((lParam.ToInt64() & 0xFFFF) == ClientArea)
                    {
                        handled = true;
                        return new IntPtr(NoActivateOnClick);
                    }
                    break;
                case Sizing:
                    KeepShape(handle, wParam.ToInt32(), lParam);
                    handled = true;
                    return new IntPtr(1);
                case ExitSizeMove:
                    SavePlace();
                    break;
            }
            return IntPtr.Zero;
        }

        /// <summary>While the window is being sized, follows the edge being dragged and fits the other to the dashboard's shape.</summary>
        private void KeepShape(IntPtr handle, int edge, IntPtr rectangle)
        {
            var r = (Rect32)Marshal.PtrToStructure(rectangle, typeof(Rect32));
            GetWindowRect(handle, out Rect32 outer);
            GetClientRect(handle, out Rect32 client);
            int frameX = (outer.Right - outer.Left) - (client.Right - client.Left), frameY = (outer.Bottom - outer.Top) - (client.Bottom - client.Top);
            bool byHeight = edge == SizeTop || edge == SizeBottom;
            if (byHeight)
            {
                int height = Math.Max(1, r.Bottom - r.Top - frameY);
                r.Right = r.Left + (int)Math.Round(height * _aspect) + frameX;
            }
            else
            {
                int width = Math.Max(WindowPlacement.MinimumWidth, r.Right - r.Left - frameX);
                if (edge == SizeLeft || edge == SizeTopLeft || edge == SizeBottomLeft) r.Left = r.Right - width - frameX;
                else r.Right = r.Left + width + frameX;
                int height = (int)Math.Round(width / _aspect) + frameY;
                if (edge == SizeTopLeft || edge == SizeTopRight) r.Top = r.Bottom - height;
                else r.Bottom = r.Top + height;
            }
            Marshal.StructureToPtr(r, rectangle, false);
        }

        /// <summary>Keeps where the dashboard is now (its area, without the title bar) for next time.</summary>
        private void SavePlace()
        {
            if (_handle == IntPtr.Zero || Mode == FullScreen) return;
            if (Mode == Windowed && (_window.WindowState != WindowState.Normal)) return;
            GetClientRect(_handle, out Rect32 client);
            var corner = new Point32 { X = 0, Y = 0 };
            ClientToScreen(_handle, ref corner);
            if (client.Right <= 0 || client.Bottom <= 0) return;
            _place = new DrawingRectangle(corner.X, corner.Y, client.Right, client.Bottom);
            WindowPlacement.Save(Mode, _place);
        }

        /// <summary>What shows while the dashboard over the game is unlocked: a frame, a note, and a corner to drag.</summary>
        private FrameworkElement PlacingFrame()
        {
            var accent = new SolidColorBrush(Color.FromRgb(45, 212, 191));
            var grid = new Grid { Visibility = Visibility.Collapsed };
            // Faint enough to see the dashboard through, there to grab even where the dashboard is see-through.
            grid.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)), BorderBrush = accent, BorderThickness = new Thickness(2) });
            grid.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(220, 16, 20, 26)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 5, 10, 6),
                Margin = new Thickness(10),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                IsHitTestVisible = false,
                Child = new TextBlock { Text = "Drag to move · drag the corner to resize", Foreground = Brushes.White, FontSize = 13 }
            });
            var grip = new Thumb { Width = 26, Height = 26, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Cursor = Cursors.SizeNWSE };
            var corner = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
            corner.SetValue(System.Windows.Shapes.Path.DataProperty, Geometry.Parse("M 26,0 L 26,26 L 0,26 Z"));
            corner.SetValue(System.Windows.Shapes.Shape.FillProperty, accent);
            grip.Template = new ControlTemplate(typeof(Thumb)) { VisualTree = corner };
            grip.DragDelta += (s, e) => Resize(e.HorizontalChange, e.VerticalChange);
            grip.DragCompleted += (s, e) => SavePlace();
            grid.Children.Add(grip);
            return grid;
        }

        /// <summary>Resizes the dashboard over the game from its corner, keeping its shape.</summary>
        private void Resize(double dx, double dy)
        {
            Matrix toPixels = PresentationSource.FromVisual(_window)?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
            GetWindowRect(_handle, out Rect32 r);
            int width = r.Right - r.Left, height = r.Bottom - r.Top;
            // Whichever way the corner moved further decides the size.
            double grow = Math.Abs(dx * toPixels.M11) >= Math.Abs(dy * toPixels.M22 * _aspect) ? dx * toPixels.M11 : dy * toPixels.M22 * _aspect;
            int newWidth = Math.Max(WindowPlacement.MinimumWidth, (int)Math.Round(width + grow));
            int newHeight = (int)Math.Round(newWidth / _aspect);
            if (newWidth != width || newHeight != height)
                SetWindowPos(_handle, IntPtr.Zero, 0, 0, newWidth, newHeight, NoActivateMove | NoMove | NoZOrder);
        }

        private void Apply(int opacity, bool show, bool unlocked)
        {
            if (_window == null || _handle == IntPtr.Zero) return;
            switch (Mode)
            {
                case FullScreen:
                    // A display of its own (a dash screen): black while hidden, rather than the desktop.
                    _image.Visibility = show ? Visibility.Visible : Visibility.Hidden;
                    return;
                case Windowed:
                    if (show && !_window.IsVisible)
                    {
                        _window.Show();
                        Place();
                    }
                    else if (!show && _window.IsVisible) HideInPlace();
                    return;
            }
            _image.Opacity = opacity / 100.0;
            _placing.Visibility = unlocked ? Visibility.Visible : Visibility.Collapsed;
            int style = GetWindowLong(_handle, ExStyle);
            // Locked, clicks go through to the game; unlocked, it takes them so it can be dragged.
            int wanted = OverlayStyle(style);
            if (wanted != style)
            {
                SetWindowLong(_handle, ExStyle, wanted);
                if ((style & Layered) == 0) SetLayeredWindowAttributes(_handle, 0, 255, LayeredAlpha);
            }
            if (show && !_window.IsVisible)
            {
                _window.Show();
                Place();
                // In case showing dropped it while the hook wasn't yet in place.
                if ((GetWindowLong(_handle, ExStyle) & Layered) == 0)
                {
                    SetWindowLong(_handle, ExStyle, OverlayStyle(GetWindowLong(_handle, ExStyle)));
                    SetLayeredWindowAttributes(_handle, 0, 255, LayeredAlpha);
                }
            }
            else if (!show && _window.IsVisible) HideInPlace();
            if (show) SetWindowPos(_handle, TopMost, 0, 0, 0, 0, NoActivateMove | NoMove | NoSize | ShowWindow);
        }

        /// <summary>Hidden where it is: it comes back to the same place.</summary>
        private void HideInPlace()
        {
            if (_window.WindowState == WindowState.Normal)
            {
                GetClientRect(_handle, out Rect32 client);
                var corner = new Point32();
                ClientToScreen(_handle, ref corner);
                if (client.Right > 0) _place = new DrawingRectangle(corner.X, corner.Y, client.Right, client.Bottom);
            }
            _window.Hide();
        }

        private void Present()
        {
            lock (_gate)
            {
                DrawingRectangle area = _dirty;
                if (_bitmap == null || _bitmap.PixelWidth != _width || _bitmap.PixelHeight != _height)
                {
                    _bitmap = new WriteableBitmap(_width, _height, 96, 96, PixelFormats.Bgra32, null);
                    _image.Source = _bitmap;
                    area = new DrawingRectangle(0, 0, _width, _height);
                }
                if (!area.IsEmpty)
                    _bitmap.WritePixels(new Int32Rect(area.X, area.Y, area.Width, area.Height), _pixels, _width * 4, area.X, area.Y);
                _dirty = DrawingRectangle.Empty;
            }
            _queued = false;
        }

        private static ImageSource AppIcon()
        {
            try
            {
                using (System.Drawing.Icon icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetEntryAssembly().Location))
                    return Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            }
            catch { return null; }
        }

        public void Dispose()
        {
            _closing = true;
            _dispatcher?.InvokeShutdown();
            _thread.Join(2000);
            _started.Dispose();
        }

        private const int WindowProcIndex = -4, ExStyle = -20, Style = -16, NoActivate = 0x08000000, ToolWindow = 0x00000080, Layered = 0x00080000, ClickThrough = 0x00000020;
        private const uint NoSize = 0x0001, NoMove = 0x0002, NoZOrder = 0x0004, NoActivateMove = 0x0010, ShowWindow = 0x0040, LayeredAlpha = 0x2;
        private const int StyleChanging = 0x007C, MouseActivate = 0x0021, Sizing = 0x0214, ExitSizeMove = 0x0232, ClientArea = 1, NoActivateOnClick = 3;
        private const int SizeLeft = 1, SizeRight = 2, SizeTop = 3, SizeTopLeft = 4, SizeTopRight = 5, SizeBottom = 6, SizeBottomLeft = 7;
        private const uint CloseCommand = 0xF060, ByCommand = 0x0, Greyed = 0x1;
        private static readonly IntPtr TopMost = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect32 { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct Point32 { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct StyleChange { public int Old, New; }

        [StructLayout(LayoutKind.Sequential)]
        private struct Margins { public int Left, Right, Top, Bottom; }

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong(IntPtr window, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong(IntPtr window, int index, int value);

        // The app is 32-bit only, where a window procedure fits SetWindowLong.
        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern IntPtr SetWindowProc(IntPtr window, int index, IntPtr procedure);

        [DllImport("user32.dll")]
        private static extern IntPtr CallWindowProc(IntPtr procedure, IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll")]
        private static extern bool SetLayeredWindowAttributes(IntPtr window, uint key, byte alpha, uint flags);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr window, out Rect32 rect);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr window, out Rect32 rect);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr window, ref Point32 point);

        [DllImport("user32.dll")]
        private static extern bool AdjustWindowRectEx(ref Rect32 rect, int style, bool menu, int exStyle);

        [DllImport("user32.dll")]
        private static extern IntPtr GetSystemMenu(IntPtr window, bool revert);

        [DllImport("user32.dll")]
        private static extern bool EnableMenuItem(IntPtr menu, uint item, uint flags);

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr window, ref Margins margins);
    }
}
