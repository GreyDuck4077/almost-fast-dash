using System;
using System.IO;
using System.Linq;
using System.Windows;

namespace AlmostFastDash
{
    /// <summary>
    /// The one exe is three programs, chosen by the command line: the app's window (no options), the dashboard
    /// itself running in the background (--run, see <see cref="DashboardRunner"/>), and the developer and support
    /// tools (see <see cref="CommandLineTools"/>).
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Contains("--stop"))
            {
                DashboardRunner.RequestStop();
                return 0;
            }
            int? tool = CommandLineTools.Run(args);
            if (tool != null) return tool.Value;
            return args.Contains("--run") ? DashboardRunner.Run() : OpenWindow();
        }

        /// <summary>The app's main window, with the app's look loaded and a safety net for anything unexpected.</summary>
        private static int OpenWindow()
        {
            var application = new Application
            {
                ShutdownMode = ShutdownMode.OnMainWindowClose
            };
            application.DispatcherUnhandledException += (sender, e) =>
            {
                // Keep the main window alive and leave a trace instead of vanishing.
                try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings-error.txt"), DateTime.Now + Environment.NewLine + e.Exception + Environment.NewLine); }
                catch { }
                MessageBox.Show(e.Exception.Message, "Almost Fast Dash", MessageBoxButton.OK, MessageBoxImage.Warning);
                e.Handled = true;
            };
            application.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/AlmostFastDash;component/UI/Theme.xaml")
            });
            DarkTitleBar.ForEveryWindow();
            application.Run(new MainWindow());
            return 0;
        }
    }
}
