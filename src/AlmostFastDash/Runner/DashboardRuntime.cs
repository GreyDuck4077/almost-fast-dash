using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AlmostFastDash.Runner
{
    /// <summary>
    /// Owns the active dashboard and its renderer, reloading settings.json when
    /// it changes and following the session's track type when auto-switch is on.
    /// </summary>
    internal sealed class DashboardRuntime : IDisposable
    {
        private DateTime _settingsWriteUtc;
        private string _discipline;
        private string _previewId;

        public DashboardRuntime()
        {
            _settingsWriteUtc = SettingsStore.LastWriteUtc();
            Settings = SettingsStore.Load();
            Dashboard = Select();
            Renderer = CreateRenderer();
            WriteActive();
        }

        public AppSettings Settings { get; private set; }
        public DashboardSettings Dashboard { get; private set; }
        public LayoutRenderer Renderer { get; private set; }

        /// <summary>Returns true when the renderer or LED settings were replaced.</summary>
        /// <param name="previewId">A dashboard the main window is previewing, shown instead while set.</param>
        public bool Refresh(string discipline, string previewId = null)
        {
            DateTime writeUtc = SettingsStore.LastWriteUtc();
            bool settingsChanged = writeUtc != _settingsWriteUtc;
            bool disciplineChanged = discipline != null && discipline != _discipline;
            bool previewChanged = previewId != _previewId;
            if (!settingsChanged && !disciplineChanged && !previewChanged) return false;
            _previewId = previewId;

            if (discipline != null) _discipline = discipline;
            if (settingsChanged)
            {
                // A copy that can't be read right now is tried again next time; the current settings stay.
                if (!SettingsStore.TryLoad(out AppSettings reloaded)) return false;
                _settingsWriteUtc = writeUtc;
                Settings = reloaded;
            }

            DashboardSettings next = Select();
            if (!settingsChanged && next.Id == Dashboard.Id)
            {
                WriteActive();
                return false;
            }
            Dashboard = next;
            Renderer.Dispose();
            Renderer = CreateRenderer();
            WriteActive();
            return true;
        }

        private DashboardSettings Select() =>
            _previewId != null && Settings.Dashboards.Any(d => d.Id == _previewId)
                ? Settings.Dashboards.First(d => d.Id == _previewId)
                : Settings.ResolveDashboard(_discipline);

        private LayoutRenderer CreateRenderer()
        {
            System.Drawing.Size size = Dashboard.ScreenSize();
            // Tap views stay put while the same dashboard is redrawn after a settings change, and start
            // again from the first view on another dashboard.
            if (_tapsFor != Dashboard.Id) { _taps.Clear(); _tapsFor = Dashboard.Id; }
            return new LayoutRenderer(size.Width, size.Height, Dashboard, new UnitSystem(Settings.General)) { Taps = _taps, SharedOutput = true, ReuseFingerprints = true };
        }

        private readonly Dictionary<string, int> _taps = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private string _tapsFor;

        private void WriteActive()
        {
            try { File.WriteAllText(DashboardRunner.ActiveDashboardPath, Dashboard.Id + "\n" + (_discipline ?? "")); }
            catch { }
        }

        public void Dispose() => Renderer?.Dispose();
    }
}
