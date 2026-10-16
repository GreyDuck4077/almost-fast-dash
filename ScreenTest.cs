using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Threading;

namespace AlmostFastOverlays
{
    /// <summary>
    /// A test on the wheel screen through the dashboard's own sending code (--screen-test): a whole frame, then
    /// numbered squares in the corners and middle sent as small parts of frames, then a bar sliding across, sent
    /// first as whole frames and then as only what changed, timed both ways. Watching it shows the screen takes
    /// parts of frames in the right places; the figures show what sending only the changes gains. The dashboard
    /// must be stopped first, since only one program can drive the screen.
    /// </summary>
    internal static class ScreenTest
    {
        public static int Run(string reportPath)
        {
            var settings = new GeneralSettings { SendChangesOnly = true };
            var report = new System.Text.StringBuilder();
            void Say(string line)
            {
                Console.WriteLine(line);
                report.AppendLine(line);
            }
            using (var sender = new ScreenSender(() => settings, new ScreenStats()))
            {
                var waited = Stopwatch.StartNew();
                while (!sender.Connected && waited.ElapsedMilliseconds < 6000) Thread.Sleep(50);
                if (!sender.Connected)
                {
                    Say("No wheel screen connected. Plug it in and stop the dashboard (and any other dashboard software) first.");
                    if (reportPath != null) File.WriteAllText(reportPath, report.ToString());
                    return 1;
                }
                Size size = sender.Size;
                try { Say("Screen: " + File.ReadAllText(ScreenSender.InfoPath).Replace("\n", ", ")); } catch { }

                using (var frame = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb))
                using (Graphics g = Graphics.FromImage(frame))
                using (var title = new Font("Bahnschrift", Math.Max(16, size.Height / 14f), FontStyle.Bold, GraphicsUnit.Pixel))
                using (var label = new Font("Bahnschrift", Math.Max(14, size.Height / 20f), FontStyle.Regular, GraphicsUnit.Pixel))
                using (var number = new Font("Bahnschrift", Math.Max(16, size.Height / 9f), FontStyle.Bold, GraphicsUnit.Pixel))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    var centre = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    Color background = Color.FromArgb(14, 17, 22);
                    void Show()
                    {
                        sender.WaitReady(1000);
                        sender.Submit(frame);
                    }
                    void Caption(string text)
                    {
                        using (var brush = new SolidBrush(background)) g.FillRectangle(brush, 0, size.Height * 0.86f, size.Width, size.Height * 0.14f);
                        g.DrawString(text, label, Brushes.Gainsboro, new RectangleF(0, size.Height * 0.86f, size.Width, size.Height * 0.14f), centre);
                    }

                    // 1. A whole frame: a frame round the edge and the title.
                    g.Clear(background);
                    using (var edge = new Pen(Color.FromArgb(45, 212, 191), 6)) g.DrawRectangle(edge, 3, 3, size.Width - 6, size.Height - 6);
                    g.DrawString("Almost Fast Dash · screen test", title, Brushes.White, new RectangleF(0, size.Height * 0.06f, size.Width, size.Height * 0.16f), centre);
                    Caption("A whole frame");
                    Show();
                    Thread.Sleep(1500);

                    // 2. Numbered squares, each sent on its own as a small part: 1 top left, 2 top right, 3 bottom
                    //    right, 4 bottom left, 5 in the middle.
                    float side = size.Height * 0.2f;
                    PointF[] spots =
                    {
                        new PointF(size.Width * 0.04f, size.Height * 0.25f), new PointF(size.Width * 0.96f - side, size.Height * 0.25f),
                        new PointF(size.Width * 0.96f - side, size.Height * 0.84f - side), new PointF(size.Width * 0.04f, size.Height * 0.84f - side),
                        new PointF((size.Width - side) / 2, (size.Height - side) / 2)
                    };
                    Color[] colors = { Color.FromArgb(239, 68, 68), Color.FromArgb(34, 197, 94), Color.FromArgb(59, 130, 246), Color.FromArgb(234, 179, 8), Color.FromArgb(168, 85, 247) };
                    for (int i = 0; i < spots.Length; i++)
                    {
                        var square = new RectangleF(spots[i], new SizeF(side, side));
                        using (var brush = new SolidBrush(colors[i])) g.FillRectangle(brush, square);
                        g.DrawString((i + 1).ToString(), number, Brushes.White, square, centre);
                        Caption("Squares 1 to 5, clockwise from the top left, then the middle");
                        Show();
                        Thread.Sleep(600);
                    }
                    Thread.Sleep(1200);

                    // 3. A bar sliding across, as whole frames and then as only what changed. It's drawn on whole
                    //    pixels: rubbing out a smoothed bar between pixels would leave a faint edge behind every frame.
                    g.Clear(background);
                    g.SmoothingMode = SmoothingMode.None;
                    int track = (int)(size.Height * 0.45f), barWidth = size.Width / 12, barHeight = (int)(size.Height * 0.16f);
                    foreach (bool changesOnly in new[] { false, true })
                    {
                        settings.SendChangesOnly = changesOnly;
                        g.Clear(background);
                        g.DrawString(changesOnly ? "Sending only what changed" : "Sending whole frames", title, Brushes.White, new RectangleF(0, size.Height * 0.06f, size.Width, size.Height * 0.16f), centre);
                        Caption("A bar sliding smoothly across and back");
                        int frames = 0;
                        var clock = Stopwatch.StartNew();
                        Rectangle last = Rectangle.Empty;
                        while (clock.Elapsed.TotalSeconds < 3)
                        {
                            double phase = clock.Elapsed.TotalSeconds / 1.5 % 2;
                            int x = (int)Math.Round((phase < 1 ? phase : 2 - phase) * (size.Width - barWidth));
                            using (var clear = new SolidBrush(background)) g.FillRectangle(clear, last);
                            last = new Rectangle(x, track, barWidth, barHeight);
                            using (var bar = new SolidBrush(Color.FromArgb(45, 212, 191))) g.FillRectangle(bar, last);
                            Show();
                            frames++;
                        }
                        sender.WaitReady(1000);
                        Say(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}: {1:0} frames a second",
                            changesOnly ? "Only what changed" : "Whole frames", frames / clock.Elapsed.TotalSeconds));
                    }

                    // 4. Done.
                    g.Clear(background);
                    g.DrawString("Screen test finished", title, Brushes.White, new RectangleF(0, 0, size.Width, size.Height), centre);
                    Show();
                    Thread.Sleep(1500);
                }
            }
            if (reportPath != null) File.WriteAllText(reportPath, report.ToString());
            return 0;
        }
    }
}
