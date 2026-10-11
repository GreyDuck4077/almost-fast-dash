using System;
using System.Drawing;

namespace AlmostFastOverlays
{
    internal interface IDashboardRenderer : IDisposable
    {
        Bitmap Render(TelemetrySnapshot telemetry, bool shiftFlashOn, int previousGear, float gearTransitionProgress);
    }

    /// <summary>Renderers that draw live pedal inputs need a redraw on every trace sample.</summary>
    internal interface ILiveTraceRenderer { }

    internal static class DashboardRendererFactory
    {
        public static IDashboardRenderer Create(int width, int height, DashboardSettings settings, UnitSystem units = null) =>
            new LayoutRenderer(width, height, settings ?? DashboardSettings.CreateHalo(), units);

        /// <summary>Renders one representative frame without touching any hardware.</summary>
        public static Bitmap RenderPreview(DashboardSettings settings, UnitSystem units, TelemetrySnapshot snapshot = null)
        {
            snapshot = snapshot ?? IRacingTelemetrySource.CreatePreviewSnapshot();
            using (IDashboardRenderer renderer = Create(Dv5Screen.Width, Dv5Screen.Height, settings, units))
                return renderer.Render(snapshot, false, snapshot.Gear, 1f);
        }
    }
}
