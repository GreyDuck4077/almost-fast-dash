using System;
using System.Drawing;

namespace AlmostFastDash.Rendering
{
    internal interface IDashboardRenderer : IDisposable
    {
        Bitmap Render(TelemetrySnapshot telemetry, bool shiftFlashOn, int previousGear, float gearTransitionProgress);
    }

    internal static class DashboardRendererFactory
    {
        public static IDashboardRenderer Create(int width, int height, DashboardSettings settings, UnitSystem units = null) =>
            new LayoutRenderer(width, height, settings ?? DashboardSettings.CreateRoad(), units);

        /// <summary>Renders one representative frame without touching any hardware.</summary>
        public static Bitmap RenderPreview(DashboardSettings settings, UnitSystem units, TelemetrySnapshot snapshot = null)
        {
            snapshot = snapshot ?? IRacingTelemetrySource.CreatePreviewSnapshot();
            System.Drawing.Size size = settings?.ScreenSize() ?? new System.Drawing.Size(ScreenSizes.DefaultWidth,
                ScreenSizes.DefaultHeight);
            using (IDashboardRenderer renderer = Create(size.Width, size.Height, settings, units))
                return renderer.Render(snapshot, false, snapshot.Gear, 1f);
        }
    }

    /// <summary>Renderers that can tell cheaply whether their next frame would differ.</summary>
    internal interface IStateRenderer
    {
        int StateHash(TelemetrySnapshot telemetry, bool shiftFlashOn, int gearAnimationFrame);
    }
}
