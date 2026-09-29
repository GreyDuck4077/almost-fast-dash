using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using SimHub.BitmapDisplay.Core;
using SimHub.BitmapDisplay.Vocore;

namespace DV5Direct
{
    internal sealed class Dv5Screen : IDisposable
    {
        public const int Width = 854;
        public const int Height = 480;

        private const string MapName = "DV5Direct.FrameBuffer";
        private readonly byte[] _rgb565 = new byte[Width * Height * 2];
        private readonly byte[] _argb = new byte[Width * Height * 4];
        private readonly VOCOREService _service = new VOCOREService();
        private readonly MemoryMappedFile _map;
        private readonly MemoryMappedViewAccessor _view;
        private bool _connected;

        public Dv5Screen()
        {
            _map = MemoryMappedFile.CreateOrOpen(MapName, _rgb565.Length);
            _view = _map.CreateViewAccessor(0, _rgb565.Length, MemoryMappedFileAccess.ReadWrite);
        }

        public ConnectionResult Connect(int brightnessPercent = 100)
        {
            var request = new ConnectRequest
            {
                EnableTouch = false,
                // Keep the first proof on the simplest full-frame path.
                EnablePartialDraws = false,
                EnableCompression = false
            };

            ConnectionResult result = _service.ConnectToScreen(request);
            if (!result.Connected)
            {
                throw new InvalidOperationException("The M-PRO screen was not found.");
            }

            if (result.DisplayWidth != Width || result.DisplayHeight != Height)
            {
                throw new InvalidOperationException(
                    $"Unexpected screen size {result.DisplayWidth}x{result.DisplayHeight}; expected {Width}x{Height}.");
            }

            _connected = true;
            SetBrightness(brightnessPercent);
            return result;
        }

        public void SetBrightness(int percent)
        {
            if (!_connected) return;
            int clamped = Math.Max(10, Math.Min(100, percent));
            _service.SetBrightness((byte)(clamped * 255 / 100));
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

            ConvertToRgb565(bitmap, _argb, _rgb565);
            _view.WriteArray(0, _rgb565, 0, _rgb565.Length);

            if (!_service.Draw(new DrawRequest { MMFName = MapName }))
            {
                throw new InvalidOperationException("The screen rejected the frame.");
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

        public void Dispose()
        {
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
                _service.CloseDisplay();
                _connected = false;
            }

            _view.Dispose();
            _map.Dispose();
        }
    }
}
