using System;
using System.Collections.Generic;
using System.Linq;
using AlmostFastDash.Checks;

namespace AlmostFastDash.Designer
{
    public partial class DesignerWindow
    {
        /// <summary>
        /// Exercises the designer end to end through its real code paths and reports
        /// each check. Used by --designer-selftest; it saves into the settings next to
        /// the exe it runs from, so run it from a scratch copy.
        /// </summary>
        private static readonly DateTime TestNow = new DateTime(2026, 1, 1, 12, 0, 0);

        /// <summary>The first control of a type inside a (not yet shown) piece of UI.</summary>
        private static T FindChild<T>(System.Windows.DependencyObject root) where T : System.Windows.DependencyObject
        {
            foreach (object child in System.Windows.LogicalTreeHelper.GetChildren(root))
            {
                if (child is T found) return found;
                if (child is System.Windows.DependencyObject inner && FindChild<T>(inner) is T deeper) return deeper;
            }
            return null;
        }

        internal List<string> RunSelfTest(string scratchFolder)
        {
            _report = new List<string>();
            _failures = 0;
            _scratchFolder = scratchFolder;
            _scope = new EvalScope { T = IRacingTelemetrySource.CreatePreviewSnapshot(), Units = _units, Theme = _layout.Theme, Accent = "#2DD4BF" };
            _states = new[]
            {
                IRacingTelemetrySource.CreatePreviewSnapshot(), IRacingTelemetrySource.CreatePitLanePreviewSnapshot(), IRacingTelemetrySource.CreateSpotterPreviewSnapshot(),
                TelemetrySnapshot.Disconnected, IRacingTelemetrySource.CreatePreviewSnapshot(RacingFlag.Checkered), ShiftSnapshot()
            };
            CheckFormulas();
            CheckTemplatesRender();
            CheckFrameCost();
            CheckFlags();
            CheckGaugeShapes();
            CheckGlowingGaugeCost();
            CheckLiveDataAndRaceData();
            CheckFlipAndRotate();
            CheckFillDirections();
            CheckGraphAndTrackMap();
            CheckStyleEffects();
            CheckGroupsAndSlots();
            CheckTurnedWidgets();
            CheckLiquidGlass();
            CheckTrustworthyNumbers();
            CheckInstrumentFinish();
            CheckRadarAndTraffic();
            CheckDashboardOnPc();
            CheckToolRail();
            CheckDrawing();
            CheckMasks();
            CheckModules();
            CheckRepeat();
            CheckModuleOptionsAndGuides();
            CheckLibraryExplainsItself();
            CheckFormulaReference();
            CheckTouch();
            CheckDroppingOntoPanels();
            CheckScreenSizes();
            CheckLapFalloffAndFlagships();
            CheckResizingTogether();
            CheckShapeTools();
            CheckEditingOperations();
            CheckScreens();
            CheckSaveAndExport();
            CheckLightLayers();

            int checks = _report.Count(line => !line.StartsWith("INFO"));
            _report.Insert(0, _failures == 0 ? "ALL " + checks + " CHECKS PASSED" : _failures + " OF " + checks + " CHECKS FAILED");
            return _report;
        }

        private List<string> _report;
        private int _failures;
        private string _scratchFolder;
        private EvalScope _scope;
        private TelemetrySnapshot[] _states;

        /// <summary>Records one check: PASS or FAIL, its name, and what was found when it failed.</summary>
        private void Check(string name, bool ok, string detail = "")
        {
            if (!ok) _failures++;
            _report.Add((ok ? "PASS  " : "FAIL  ") + name + (detail.Length > 0 ? "  (" + detail + ")" : ""));
        }

        private static string Pixels(System.Drawing.Bitmap bitmap)
        {
            var data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                var bytes = new byte[data.Stride * bitmap.Height];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
                using (var sha = System.Security.Cryptography.SHA1.Create()) return Convert.ToBase64String(sha.ComputeHash(bytes));
            }
            finally { bitmap.UnlockBits(data); }
        }

        private System.Drawing.Color Sample(DashWidget w, int x, int y, bool cache = true)
        {
            var layout = DesignTemplates.Blank();
            layout.Theme.BackgroundStyle = "Solid";
            layout.Theme.Background = "#000000";
            layout.Screens[0].Widgets.Add(w);
            using (var renderer = new LayoutRenderer(854, 480, new DashboardSettings { Layout = Layouts.Custom, Design = layout }, _units) { CacheStatic = cache })
            using (System.Drawing.Bitmap frame = renderer.Render(IRacingTelemetrySource.CreatePreviewSnapshot(), false, 4, 1, layout.Screens[0]))
                return frame.GetPixel(x, y);
        }

        private static DashWidget FillBar(bool flip, string rotate = "0")
        {
            DashWidget w = WidgetCatalog.Create("bar", 100, 100);
            w.W = 400; w.H = 40;
            w.Set("value", "0.3"); w.Set("color", "#FF0000"); w.Set("track", "#00000000"); w.Set("flipX", flip ? "true" : "false"); w.Set("rotate", rotate);
            return w;
        }

        /// <summary>Checks: light layers.</summary>
        private void CheckLightLayers()
        {
            foreach (string line in LightChecks.Run(SettingsStore.Load()))
            {
                if (line.StartsWith("FAIL")) _failures++;
                _report.Add(line);
            }
        }
    }
}
