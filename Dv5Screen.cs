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
    /// Drives the DV-5's M-PRO screen (a VoCore-family panel, USB C872:1004)
    /// directly through Windows' built-in WinUSB driver.
    /// </summary>
    internal sealed class Dv5Screen : IDisposable
    {
        public const int Width = 854;
        public const int Height = 480;

        private const ushort VendorId = 0xC872;
        private const ushort ProductId = 0x1004;
        private const byte VendorOut = 0x40;
        private const byte VendorIn = 0xC0;
        private const uint TransferTimeoutMilliseconds = 2000;
        private const int FrameBytes = Width * Height * 2;

        // Generic USB device interface, used when the WinUSB install did not record its own GUID.
        private static readonly Guid UsbDeviceInterface = new Guid("A5DCBF10-6530-11D2-901F-00C04FB951ED");

        private readonly byte[] _argb = new byte[Width * Height * 4];
        private byte[] _transfer = new byte[FrameBytes];
        private SafeFileHandle _file;
        private IntPtr _usb = IntPtr.Zero;
        private byte _bulkOut;
        private bool _connected;

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
            StartTouch();

            uint screen = Query(new byte[] { 0x51, 0x02, 0x04, 0x1F, 0xFC });
            uint code = Query(new byte[] { 0x51, 0x02, 0x04, 0x1F, 0xF8 });
            // The D500FPC931A-A panel (code 3) takes exactly one frame; the other
            // 854x480 panels expect 320 bytes of padding after it.
            int padding = (code & 0xFF) == 3 ? 0 : 320;
            _transfer = new byte[FrameBytes + padding];

            Control(VendorOut, 0xB0, new byte[] { 0x00, 0x29 }); // leave sleep mode
            _connected = true;
            SetBrightness(brightnessPercent);
            return new ConnectionResult
            {
                ModelName = "M-PRO 854x480",
                DisplayId = $"screen 0x{screen:X}, code 0x{code & 0xFF:X2}"
            };
        }

        public void SetBrightness(int percent)
        {
            if (!_connected) return;
            int clamped = Math.Max(10, Math.Min(100, percent));
            Control(VendorOut, 0xB0, new byte[] { 0x00, 0x51, 0x02, 0x00, 0x00, 0x00, (byte)(clamped * 255 / 100), 0x00 });
        }

        public void Draw(Bitmap bitmap)
        {
            if (!_connected)
            {
                throw new InvalidOperationException("The screen is not connected.");
            }

            if (bitmap.Width != Width || bitmap.Height != Height)
            {
                throw new ArgumentException($"Frame must be {Width}x{Height}.", nameof(bitmap));
            }

            ConvertToRgb565(bitmap, _argb, _transfer);
            Control(VendorOut, 0xB0, new byte[]
            {
                0x00, 0x2C, FrameBytes & 0xFF, (FrameBytes >> 8) & 0xFF, (FrameBytes >> 16) & 0xFF, 0x00
            });
            if (!Native.WinUsb_WritePipe(_usb, _bulkOut, _transfer, (uint)_transfer.Length, out uint written, IntPtr.Zero) ||
                written != _transfer.Length)
            {
                throw new InvalidOperationException("The screen rejected the frame.", new Win32Exception());
            }
        }

        private static void ConvertToRgb565(Bitmap bitmap, byte[] source, byte[] destination)
        {
            var rectangle = new Rectangle(0, 0, Width, Height);
            BitmapData data = bitmap.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int sourceLength = Math.Abs(data.Stride) * Height;
                if (sourceLength > source.Length)
                    throw new InvalidOperationException("Unexpected bitmap stride.");
                Marshal.Copy(data.Scan0, source, 0, sourceLength);

                for (int y = 0; y < Height; y++)
                {
                    int sourceRow = y * data.Stride;
                    for (int x = 0; x < Width; x++)
                    {
                        int sourceIndex = sourceRow + (x * 4);
                        int blue = source[sourceIndex];
                        int green = source[sourceIndex + 1];
                        int red = source[sourceIndex + 2];
                        ushort pixel = (ushort)(((red >> 3) << 11) | ((green >> 2) << 5) | (blue >> 3));
                        // The DV-5 exposes an 854x480 logical surface, but its M-PRO
                        // controller scans a 480x854 portrait panel. Rotate the logical
                        // frame counterclockwise into that physical transport order.
                        int physicalX = y;
                        int physicalY = Width - 1 - x;
                        int destinationIndex = ((physicalY * Height) + physicalX) * 2;
                        destination[destinationIndex] = (byte)(pixel & 0xff);
                        destination[destinationIndex + 1] = (byte)(pixel >> 8);
                    }
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
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

        /// <summary>The next tap since the last call, in the 854×480 picture's pixels.</summary>
        public bool TryTakeTap(out Point tap) => _taps.TryDequeue(out tap);

        /// <summary>
        /// Reads the touch panel on its own thread. While a finger is down the screen sends a report every
        /// 8 ms or so: a byte with the number of touches, then the first touch's position on the portrait
        /// panel (X and Y as 12-bit numbers, the top bits of X sharing a byte with the event flags). A short
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
            _touchThread = new System.Threading.Thread(() => ReadTouches(usb, touchIn)) { IsBackground = true, Name = "Touch", Priority = System.Threading.ThreadPriority.BelowNormal };
            _touchThread.Start();
        }

        private void ReadTouches(IntPtr usb, byte pipe)
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
                if (!ReadTouch(report, (int)got, out bool touching, out Point at)) continue;
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

        /// <summary>One touch report: whether a finger is down, and where on the 854×480 picture.</summary>
        internal static bool ReadTouch(byte[] report, int length, out bool touching, out Point at)
        {
            touching = false;
            at = Point.Empty;
            if (length < 7) return false;
            // The touch count, then event flags (top two bits: 01 is a lift) sharing a byte with X's top bits.
            touching = report[2] > 0 && (report[3] & 0xC0) != 0x40;
            if (!touching) return true;
            int panelX = ((report[3] & 0x0F) << 8) | report[4], panelY = ((report[5] & 0x0F) << 8) | report[6];
            // The panel is portrait; the picture is turned a quarter onto it (see ConvertToRgb565).
            at = new Point(Math.Max(0, Math.Min(Width - 1, Width - 1 - panelY)), Math.Max(0, Math.Min(Height - 1, panelX)));
            return true;
        }

        private void StopTouch()
        {
            _touchRunning = false;
            _touchThread?.Join(800);
            _touchThread = null;
        }

        /// <summary>Whether the screen is plugged in, without opening it.</summary>
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
                    // The M-PRO controller retains its last frame after the USB
                    // connection closes, so explicitly blank it on shutdown.
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
