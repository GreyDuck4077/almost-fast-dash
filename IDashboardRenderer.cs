using System;
using System.Drawing;

namespace DV5Direct
{
    internal interface IDashboardRenderer : IDisposable
    {
        Bitmap Render(TelemetrySnapshot telemetry, bool shiftFlashOn, int previousGear, float gearTransitionProgress);
    }

    internal static class DashboardRendererFactory
    {
        public static IDashboardRenderer Create(int width, int height, DashboardSettings settings) =>
            string.Equals(settings?.Layout, "Apex", StringComparison.OrdinalIgnoreCase) || settings?.Id == "dashboard-3"
                ? (IDashboardRenderer)new ApexRenderer(width, height, settings)
                : string.Equals(settings?.Layout, "Endurance", StringComparison.OrdinalIgnoreCase) || settings?.Id == "dashboard-2"
                    ? new EnduranceRenderer(width, height, settings)
                    : new GearSpeedRenderer(width, height, settings);
    }
}
