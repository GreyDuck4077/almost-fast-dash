using System;
using System.Collections.Generic;
using System.Linq;
using DrawingRectangle = System.Drawing.Rectangle;

namespace AlmostFastDash.Output
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
}
