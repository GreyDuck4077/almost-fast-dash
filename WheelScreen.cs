using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace AlmostFastOverlays
{
    internal sealed class ConnectionResult
    {
        public string ModelName { get; set; }
        public string DisplayId { get; set; }
    }

    /// <summary>
    /// One of VoCore's M-PRO screens: its panel's own size, and whether a landscape picture goes onto it turned a
    /// quarter (most panels are portrait). Every model answers to the same USB ID and says which it is when asked;
    /// the models and sizes are those in VoCore's open driver (mpro.c).
    /// </summary>
    internal sealed class ScreenModel
    {
        public string Name { get; private set; }
        public int PanelWidth { get; private set; }
        public int PanelHeight { get; private set; }

        /// <summary>A portrait panel: the landscape picture goes onto it turned a quarter.</summary>
        public bool Turned => PanelWidth < PanelHeight;

        /// <summary>The picture's size, landscape on a portrait panel.</summary>
        public int Width => Turned ? PanelHeight : PanelWidth;
        public int Height => Turned ? PanelWidth : PanelHeight;

        /// <summary>
        /// Whole frames go with the original draw command, which the 5" LCD's panels expect 320 bytes of padding
        /// after (all but the D500FPC931A-A, version 3). Every other model, and any part of a frame, goes with the
        /// draw command that names its area.
        /// </summary>
        public bool WholeFrameCommand { get; private set; }
        public int FramePadding { get; private set; }

        /// <summary>The 5" LCD at 854×480, the screen this app was built on.</summary>
        public static readonly ScreenModel FiveInch = new ScreenModel { Name = "5\" M-PRO", PanelWidth = 480, PanelHeight = 854, WholeFrameCommand = true, FramePadding = 320 };

        /// <summary>The model a screen reports (its screen and version answers).</summary>
        public static ScreenModel For(uint screen, uint version)
        {
            switch (screen)
            {
                case 0x00000005: return new ScreenModel { Name = "5\" M-PRO", PanelWidth = 480, PanelHeight = 854, WholeFrameCommand = true, FramePadding = (version & 0xFF) == 3 ? 0 : 320 };
                case 0x00001005: return new ScreenModel { Name = "5\" OLED M-PRO", PanelWidth = 720, PanelHeight = 1280 };
                case 0x00000304: return new ScreenModel { Name = "4.3\" M-PRO", PanelWidth = 480, PanelHeight = 800 };
                case 0x00000004:
                case 0x00000104:
                case 0x00000B04: return new ScreenModel { Name = "4\" M-PRO", PanelWidth = 480, PanelHeight = 800 };
                case 0x00000007: return new ScreenModel { Name = "6.8\" M-PRO", PanelWidth = 800, PanelHeight = 480 };
                case 0x00000403: return new ScreenModel { Name = "3.4\" M-PRO", PanelWidth = 800, PanelHeight = 800 };
                case 0x0000000A: return new ScreenModel { Name = "10\" M-PRO", PanelWidth = 1024, PanelHeight = 600 };
                // No answer at all: treat it as the 5", as the app always has.
                case 0xFFFFFFFF: return FiveInch;
                // A model newer than this list: VoCore's driver assumes a 480×800 panel.
                default: return new ScreenModel { Name = "M-PRO", PanelWidth = 480, PanelHeight = 800 };
            }
        }
    }

    /// <summary>
    /// Drives VoCore's M-PRO screens (USB C872:1004) directly through Windows' built-in WinUSB driver: a whole
    /// frame, or just an area that changed. The commands match VoCore's own open-source M-PRO driver (mpro.c in
    /// github.com/Vonger/mpro_drm, GPL-2.0-or-later); CREDITS.md explains how they were first worked out.
    /// FrameEncoder turns pictures into the panel's pixels.
    /// </summary>
    internal sealed class WheelScreen : IDisposable
    {
        private const ushort VendorId = 0xC872;
        private const ushort ProductId = 0x1004;
        private const byte VendorOut = 0x40;
        private const byte VendorIn = 0xC0;
        private const uint TransferTimeoutMilliseconds = 2000;

        // Generic USB device interface, used when the WinUSB install did not record its own GUID.
        private static readonly Guid UsbDeviceInterface = new Guid("A5DCBF10-6530-11D2-901F-00C04FB951ED");

        private readonly byte[] _drawWhole = new byte[6], _drawArea = new byte[12];
        private SafeFileHandle _file;
        private IntPtr _usb = IntPtr.Zero;
        private byte _bulkOut;
        private bool _connected;

        /// <summary>The model connected (the 5" until one is).</summary>
        public ScreenModel Model { get; private set; } = ScreenModel.FiveInch;

        /// <summary>The picture's size on this screen.</summary>
        public int Width => Model.Width;
        public int Height => Model.Height;

        /// <summary>Bytes a whole frame needs: the panel's pixels (two bytes each) and any padding after them.</summary>
        public int FrameLength => Model.PanelWidth * Model.PanelHeight * 2 + Model.FramePadding;

        public ConnectionResult Connect(int brightnessPercent = 100)
        {
            string path = FindDevicePath() ?? throw new InvalidOperationException("The M-PRO screen was not found.");
            _file = Native.CreateFile(path, Native.GenericRead | Native.GenericWrite,
                Native.FileShareRead | Native.FileShareWrite, IntPtr.Zero, Native.OpenExisting,
                Native.FileAttributeNormal | Native.FileFlagOverlapped, IntPtr.Zero);
            if (_file.IsInvalid)
                throw new InvalidOperationException("The M-PRO screen is in use by another program.", new Win32Exception());
            if (!Native.WinUsb_Initialize(_file, out _usb))
                throw new InvalidOperationException("The M-PRO screen could not be opened.", new Win32Exception());

            _bulkOut = FindBulkOutPipe();
            uint timeout = TransferTimeoutMilliseconds;
            Native.WinUsb_SetPipePolicy(_usb, _bulkOut, Native.PipeTransferTimeout, 4, ref timeout);

            uint screen = Query(new byte[] { 0x51, 0x02, 0x04, 0x1F, 0xFC });
            uint version = Query(new byte[] { 0x51, 0x02, 0x04, 0x1F, 0xF8 });
            Model = ScreenModel.For(screen, version);
            StartTouch();

            Control(VendorOut, 0xB0, new byte[] { 0x00, 0x29 }); // leave sleep mode
            _connected = true;
            SetBrightness(brightnessPercent);
            return new ConnectionResult
            {
                ModelName = Model.Name + ", " + Width + " × " + Height,
                DisplayId = $"screen 0x{screen:X}, version 0x{version & 0xFF:X2}"
            };
        }

        public void SetBrightness(int percent)
        {
            if (!_connected) return;
            int clamped = Math.Max(10, Math.Min(100, percent));
            Control(VendorOut, 0xB0, new byte[] { 0x00, 0x51, 0x02, 0x00, 0x00, 0x00, (byte)(clamped * 255 / 100), 0x00 });
        }

        /// <summary>A whole frame: the panel's pixels from FrameEncoder, in a buffer at least FrameLength long.</summary>
        public void SendFrame(byte[] panel)
        {
            if (!Model.WholeFrameCommand)
            {
                SendArea(new Rectangle(0, 0, Model.PanelWidth, Model.PanelHeight), panel);
                return;
            }
            int pixels = Model.PanelWidth * Model.PanelHeight * 2;
            _drawWhole[0] = 0x00;
            _drawWhole[1] = 0x2C;
            _drawWhole[2] = (byte)pixels;
            _drawWhole[3] = (byte)(pixels >> 8);
            _drawWhole[4] = (byte)(pixels >> 16);
            Control(VendorOut, 0xB0, _drawWhole);
            Write(panel, FrameLength);
        }

        /// <summary>
        /// Part of a frame: an area of the panel (in the panel's own pixels) and its pixels row by row. This is
        /// VoCore's draw command that names the area: its length, then its left, top and width.
        /// </summary>
        public void SendArea(Rectangle area, byte[] pixels)
        {
            int length = area.Width * area.Height * 2;
            _drawArea[0] = 0x00;
            _drawArea[1] = 0x2C;
            _drawArea[2] = (byte)length;
            _drawArea[3] = (byte)(length >> 8);
            _drawArea[4] = (byte)(length >> 16);
            _drawArea[5] = 0x00;
            _drawArea[6] = (byte)area.X;
            _drawArea[7] = (byte)(area.X >> 8);
            _drawArea[8] = (byte)area.Y;
            _drawArea[9] = (byte)(area.Y >> 8);
            _drawArea[10] = (byte)area.Width;
            _drawArea[11] = (byte)(area.Width >> 8);
            Control(VendorOut, 0xB0, _drawArea);
            Write(pixels, length);
        }

        /// <summary>A whole picture at this screen's size, encoded and sent (for blanking and tests; the dashboard uses ScreenSender).</summary>
        public void Draw(Bitmap bitmap)
        {
            if (bitmap.Width != Width || bitmap.Height != Height)
                throw new ArgumentException($"Frame must be {Width}x{Height}.", nameof(bitmap));
            var pixels = new int[Width * Height];
            FrameEncoder.Copy(bitmap, pixels);
            var panel = new byte[FrameLength];
            FrameEncoder.Encode(pixels, Width, new Rectangle(0, 0, Width, Height), Model, panel);
            SendFrame(panel);
        }

        private void Write(byte[] buffer, int length)
        {
            if (!_connected) throw new InvalidOperationException("The screen is not connected.");
            if (!Native.WinUsb_WritePipe(_usb, _bulkOut, buffer, (uint)length, out uint written, IntPtr.Zero) || written != length)
                throw new InvalidOperationException("The screen rejected the frame.", new Win32Exception());
        }

        /// <summary>Sends a vendor query and reads back the 32-bit answer that follows its status byte.</summary>
        private uint Query(byte[] command)
        {
            try
            {
                Control(VendorOut, 0xB5, command);
                Control(VendorIn, 0xB6, new byte[1]);
                var answer = new byte[5];
                Control(VendorIn, 0xB7, answer);
                return BitConverter.ToUInt32(answer, 1);
            }
            catch
            {
                return 0xFFFFFFFF;
            }
        }

        private void Control(byte requestType, byte request, byte[] buffer)
        {
            var setup = new Native.WinUsbSetupPacket
            {
                RequestType = requestType,
                Request = request,
                Length = (ushort)buffer.Length
            };
            if (!Native.WinUsb_ControlTransfer(_usb, setup, buffer, (uint)buffer.Length, out _, IntPtr.Zero))
                throw new InvalidOperationException("The screen did not accept a command.", new Win32Exception());
        }

        private byte FindBulkOutPipe()
        {
            if (Native.WinUsb_QueryInterfaceSettings(_usb, 0, out Native.UsbInterfaceDescriptor descriptor))
            {
                for (byte i = 0; i < descriptor.NumEndpoints; i++)
                {
                    if (Native.WinUsb_QueryPipe(_usb, 0, i, out Native.WinUsbPipeInformation pipe) &&
                        pipe.PipeType == Native.UsbdPipeTypeBulk && (pipe.PipeId & 0x80) == 0)
                        return pipe.PipeId;
                }
            }
            return 0x02;
        }

        // ------------------------------------------------------------------ touch

        private System.Threading.Thread _touchThread;
        private volatile bool _touchRunning;
        private readonly System.Collections.Concurrent.ConcurrentQueue<Point> _taps = new System.Collections.Concurrent.ConcurrentQueue<Point>();

        /// <summary>Whether the screen reports touches (it has an interrupt channel for them).</summary>
        public bool HasTouch { get; private set; }

        /// <summary>The next tap since the last call, in the picture's pixels.</summary>
        public bool TryTakeTap(out Point tap) => _taps.TryDequeue(out tap);

        /// <summary>
        /// Reads the touch panel on its own thread. While a finger is down the screen sends a report every
        /// 8 ms or so: a byte with the number of touches, then the first touch's position on the panel
        /// (X and Y as 12-bit numbers, the top bits of X sharing a byte with the event flags). A short
        /// touch that hardly moves counts as a tap, so a hand brushing or resting on the glass doesn't.
        /// </summary>
        private void StartTouch()
        {
            byte touchIn = 0;
            if (Native.WinUsb_QueryInterfaceSettings(_usb, 0, out Native.UsbInterfaceDescriptor descriptor))
                for (byte i = 0; i < descriptor.NumEndpoints; i++)
                    if (Native.WinUsb_QueryPipe(_usb, 0, i, out Native.WinUsbPipeInformation pipe) && pipe.PipeType == Native.UsbdPipeTypeInterrupt && (pipe.PipeId & 0x80) != 0)
                        touchIn = pipe.PipeId;
            if (touchIn == 0) return;
            HasTouch = true;
            uint timeout = 250;
            Native.WinUsb_SetPipePolicy(_usb, touchIn, Native.PipeTransferTimeout, 4, ref timeout);
            _touchRunning = true;
            IntPtr usb = _usb;
            ScreenModel model = Model;
            _touchThread = new System.Threading.Thread(() => ReadTouches(usb, touchIn, model)) { IsBackground = true, Name = "Touch", Priority = System.Threading.ThreadPriority.BelowNormal };
            _touchThread.Start();
        }

        private void ReadTouches(IntPtr usb, byte pipe, ScreenModel model)
        {
            var report = new byte[64];
            bool down = false;
            Point start = Point.Empty, last = Point.Empty;
            int startTick = 0;
            while (_touchRunning)
            {
                if (!Native.WinUsb_ReadPipe(usb, pipe, report, (uint)report.Length, out uint got, IntPtr.Zero))
                {
                    // Nothing within the timeout is normal: nobody is touching the screen.
                    if (Marshal.GetLastWin32Error() != 121) System.Threading.Thread.Sleep(100);
                    continue;
                }
                if (!ReadTouch(report, (int)got, model, out bool touching, out Point at)) continue;
                if (touching)
                {
                    last = at;
                    if (!down) { down = true; start = last; startTick = Environment.TickCount; }
                }
                else if (down)
                {
                    down = false;
                    int dx = last.X - start.X, dy = last.Y - start.Y;
                    if (unchecked(Environment.TickCount - startTick) <= 700 && dx * dx + dy * dy <= 40 * 40) _taps.Enqueue(start);
                }
            }
        }

        /// <summary>One touch report: whether a finger is down, and where on the picture.</summary>
        internal static bool ReadTouch(byte[] report, int length, ScreenModel model, out bool touching, out Point at)
        {
            touching = false;
            at = Point.Empty;
            if (length < 7) return false;
            // The touch count, then event flags (top two bits: 01 is a lift) sharing a byte with X's top bits.
            touching = report[2] > 0 && (report[3] & 0xC0) != 0x40;
            if (!touching) return true;
            int panelX = ((report[3] & 0x0F) << 8) | report[4], panelY = ((report[5] & 0x0F) << 8) | report[6];
            // On a portrait panel the picture is turned a quarter (see FrameEncoder): the panel's height runs
            // right to left across the picture.
            int x = model.Turned ? model.Width - 1 - panelY : panelX, y = model.Turned ? panelX : panelY;
            at = new Point(Math.Max(0, Math.Min(model.Width - 1, x)), Math.Max(0, Math.Min(model.Height - 1, y)));
            return true;
        }

        private void StopTouch()
        {
            _touchRunning = false;
            _touchThread?.Join(800);
            _touchThread = null;
        }

        /// <summary>Whether a screen is plugged in, without opening it.</summary>
        public static bool IsPresent() => FindDevicePath() != null;

        private static string FindDevicePath()
        {
            string match = $"vid_{VendorId:x4}&pid_{ProductId:x4}";
            foreach (Guid guid in InterfaceGuids())
                foreach (string path in Native.DeviceInterfacePaths(guid))
                    if (path.ToLowerInvariant().Contains(match))
                        return path;
            return null;
        }

        /// <summary>The interface GUID(s) the WinUSB driver install registered for this screen, then the generic USB one.</summary>
        private static IEnumerable<Guid> InterfaceGuids()
        {
            var guids = new List<Guid>();
            try
            {
                using (RegistryKey device = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Enum\USB\VID_{VendorId:X4}&PID_{ProductId:X4}"))
                {
                    if (device != null)
                        foreach (string instance in device.GetSubKeyNames())
                            using (RegistryKey parameters = device.OpenSubKey(instance + @"\Device Parameters"))
                            {
                                object value = parameters?.GetValue("DeviceInterfaceGUIDs") ?? parameters?.GetValue("DeviceInterfaceGUID");
                                foreach (string text in value as string[] ?? new[] { value as string })
                                    if (Guid.TryParse(text, out Guid guid) && !guids.Contains(guid))
                                        guids.Add(guid);
                            }
                }
            }
            catch { }
            guids.Add(UsbDeviceInterface);
            return guids;
        }

        public void Dispose()
        {
            StopTouch();
            if (_connected)
            {
                try
                {
                    // The M-PRO controller keeps its last frame after the USB connection closes, so blank it.
                    using (var blank = new Bitmap(Width, Height, PixelFormat.Format32bppArgb))
                    using (Graphics graphics = Graphics.FromImage(blank))
                    {
                        graphics.Clear(Color.Black);
                        Draw(blank);
                    }
                }
                catch
                {
                    // The device may already be disconnected; shutdown must continue.
                }
                _connected = false;
            }

            if (_usb != IntPtr.Zero)
            {
                Native.WinUsb_Free(_usb);
                _usb = IntPtr.Zero;
            }
            _file?.Dispose();
            _file = null;
        }

        private static class Native
        {
            public const uint GenericRead = 0x80000000;
            public const uint GenericWrite = 0x40000000;
            public const uint FileShareRead = 0x1;
            public const uint FileShareWrite = 0x2;
            public const uint OpenExisting = 3;
            public const uint FileAttributeNormal = 0x80;
            public const uint FileFlagOverlapped = 0x40000000;
            public const uint PipeTransferTimeout = 0x03;
            public const int UsbdPipeTypeBulk = 2;
            public const int UsbdPipeTypeInterrupt = 3;
            private const int DigcfPresent = 0x2;
            private const int DigcfDeviceInterface = 0x10;

            [StructLayout(LayoutKind.Sequential, Pack = 1)]
            public struct WinUsbSetupPacket
            {
                public byte RequestType;
                public byte Request;
                public ushort Value;
                public ushort Index;
                public ushort Length;
            }

            [StructLayout(LayoutKind.Sequential, Pack = 1)]
            public struct UsbInterfaceDescriptor
            {
                public byte Length;
                public byte DescriptorType;
                public byte InterfaceNumber;
                public byte AlternateSetting;
                public byte NumEndpoints;
                public byte InterfaceClass;
                public byte InterfaceSubClass;
                public byte InterfaceProtocol;
                public byte Interface;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct WinUsbPipeInformation
            {
                public int PipeType;
                public byte PipeId;
                public ushort MaximumPacketSize;
                public byte Interval;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct DeviceInterfaceData
            {
                public int Size;
                public Guid InterfaceClassGuid;
                public int Flags;
                public IntPtr Reserved;
            }

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            public static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode,
                IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

            [DllImport("winusb.dll", SetLastError = true)]
            public static extern bool WinUsb_Initialize(SafeFileHandle deviceHandle, out IntPtr interfaceHandle);

            [DllImport("winusb.dll", SetLastError = true)]
            public static extern bool WinUsb_Free(IntPtr interfaceHandle);

            [DllImport("winusb.dll", SetLastError = true)]
            public static extern bool WinUsb_QueryInterfaceSettings(IntPtr interfaceHandle, byte alternateSetting, out UsbInterfaceDescriptor descriptor);

            [DllImport("winusb.dll", SetLastError = true)]
            public static extern bool WinUsb_QueryPipe(IntPtr interfaceHandle, byte alternateSetting, byte pipeIndex, out WinUsbPipeInformation pipe);

            [DllImport("winusb.dll", SetLastError = true)]
            public static extern bool WinUsb_SetPipePolicy(IntPtr interfaceHandle, byte pipeId, uint policyType, uint valueLength, ref uint value);

            [DllImport("winusb.dll", SetLastError = true)]
            public static extern bool WinUsb_ControlTransfer(IntPtr interfaceHandle, WinUsbSetupPacket setupPacket,
                byte[] buffer, uint bufferLength, out uint lengthTransferred, IntPtr overlapped);

            [DllImport("winusb.dll", SetLastError = true)]
            public static extern bool WinUsb_ReadPipe(IntPtr interfaceHandle, byte pipeId, byte[] buffer,
                uint bufferLength, out uint lengthTransferred, IntPtr overlapped);

            [DllImport("winusb.dll", SetLastError = true)]
            public static extern bool WinUsb_WritePipe(IntPtr interfaceHandle, byte pipeId, byte[] buffer,
                uint bufferLength, out uint lengthTransferred, IntPtr overlapped);

            [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr parent, int flags);

            [DllImport("setupapi.dll", SetLastError = true)]
            private static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData,
                ref Guid interfaceClassGuid, int memberIndex, ref DeviceInterfaceData deviceInterfaceData);

            [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref DeviceInterfaceData deviceInterfaceData,
                IntPtr detailData, int detailDataSize, out int requiredSize, IntPtr deviceInfoData);

            [DllImport("setupapi.dll", SetLastError = true)]
            private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

            public static IEnumerable<string> DeviceInterfacePaths(Guid interfaceGuid)
            {
                var paths = new List<string>();
                IntPtr set = SetupDiGetClassDevs(ref interfaceGuid, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
                if (set == new IntPtr(-1)) return paths;
                try
                {
                    var data = new DeviceInterfaceData { Size = Marshal.SizeOf(typeof(DeviceInterfaceData)) };
                    for (int index = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref interfaceGuid, index, ref data); index++)
                    {
                        SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out int size, IntPtr.Zero);
                        if (size <= 0) continue;
                        IntPtr detail = Marshal.AllocHGlobal(size);
                        try
                        {
                            // SP_DEVICE_INTERFACE_DETAIL_DATA_W: cbSize is 8 on 64-bit, 6 on 32-bit.
                            Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                            if (SetupDiGetDeviceInterfaceDetail(set, ref data, detail, size, out _, IntPtr.Zero))
                                paths.Add(Marshal.PtrToStringUni(detail + 4));
                        }
                        finally
                        {
                            Marshal.FreeHGlobal(detail);
                        }
                    }
                }
                finally
                {
                    SetupDiDestroyDeviceInfoList(set);
                }
                return paths;
            }
        }
    }
}
