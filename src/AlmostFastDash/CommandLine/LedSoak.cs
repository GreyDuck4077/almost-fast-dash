using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.Windows;

namespace AlmostFastDash.CommandLine
{
    internal static class LedSoak
    {
        /// <summary>
        /// Streams a dim sweep to an LED board at 60 frames a second for 90 seconds through the real
        /// LED code, then checks the board still answers: proves it keeps up with fast-changing lights.
        /// </summary>
        internal static int Run(string port, int leds, bool fast)
        {
            using (var controller = new ArduinoLedController(port, 115200, leds, null) { Batched = fast })
            {
                controller.Connect();
                Console.WriteLine("Board: " + controller.QueryDeviceName());
                var watch = System.Diagnostics.Stopwatch.StartNew();
                int frames = 0;
                try
                {
                while (watch.Elapsed.TotalSeconds < 90)
                {
                    var frame = new System.Drawing.Color[leds];
                    double head = (watch.ElapsedMilliseconds / 650.0 % 1) * leds;
                    for (int i = 0; i < leds; i++)
                    {
                        double level = Math.Max(0, 1 - Math.Abs(head - i) / 3);
                        frame[i] = System.Drawing.Color.FromArgb(0, (int)(25 * level), (int)(40 * level));
                    }
                    controller.SetFrame(frame);
                    frames++;
                    System.Threading.Thread.Sleep(16);
                }
                Console.WriteLine(frames + " frames in " + watch.Elapsed.TotalSeconds.ToString("0") + " s (" + (frames / watch.Elapsed.TotalSeconds).ToString("0") + " a second); board still answers: " + controller.QueryDeviceName());
                }
                catch (Exception exception)
                {
                    Console.WriteLine("FAILED after " + watch.Elapsed.TotalSeconds.ToString("0.0") + " s and " + frames + " frames: " + exception.Message);
                }
                Console.WriteLine("Packets " + controller.Sent + ", unacknowledged " + controller.Unacknowledged + ", slowest acknowledgement " + controller.SlowestAckMilliseconds.ToString("0.0") + " ms");
            }
            return 0;
        }
    }
}
