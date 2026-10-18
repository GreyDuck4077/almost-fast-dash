using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;

namespace AlmostFastDash.Checks
{
    /// <summary>
    /// Runs the end-to-end checks for <c>--designer-selftest</c>, which only the Checks build has.
    /// tests\AlmostFastDash.Tests starts that build in an empty folder and reads the report.
    /// </summary>
    internal static class CheckRunner
    {
        /// <summary>
        /// Opens a designer on a fresh Roady, runs every check through the real code and writes the report:
        /// a summary line, then one PASS, FAIL or INFO line per check. Saves into the settings next to the exe,
        /// so run it from an empty folder. Returns 0 when everything passed.
        /// </summary>
        internal static int Run(string reportPath)
        {
            string folder = AppDomain.CurrentDomain.BaseDirectory;
            if (!Path.IsPathRooted(reportPath)) reportPath = Path.Combine(folder, reportPath);
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/AlmostFastDash;component/UI/Theme.xaml") });
            DarkTitleBar.ForEveryWindow();
            AppSettings settings = SettingsStore.Load();
            DashboardSettings dashboard = settings.AddDesignedDashboard(DesignTemplates.Road, "Self test");
            var designer = new DesignerWindow(settings, dashboard, null) { Left = -4000, Top = 0, ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual };
            int exit = 1;
            designer.Loaded += (s, e) => designer.Dispatcher.BeginInvoke(new Action(() =>
            {
                List<string> report;
                try { report = designer.RunSelfTest(folder); }
                catch (Exception exception) { report = new List<string> { "CRASHED", exception.ToString() }; }
                File.WriteAllLines(reportPath, report);
                exit = report[0].StartsWith("ALL") ? 0 : 1;
                Environment.Exit(exit);
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            application.Run(designer);
            return exit;
        }
    }
}
