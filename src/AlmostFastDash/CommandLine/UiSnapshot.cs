using System;
using System.IO;
using System.Linq;
using System.Windows;

namespace AlmostFastDash.CommandLine
{
    internal static class UiSnapshot
    {
        /// <summary>
        /// Opens a window in a scripted state and saves a screenshot of it, for checking
        /// the UI without a person at the screen. Nothing is saved to settings.
        /// </summary>
        internal static int Run(string scenario, string output)
        {
            if (!Path.IsPathRooted(output)) output = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, output);
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/AlmostFastDash;component/UI/Theme.xaml") });
            DarkTitleBar.ForEveryWindow();
            Window window;
            if (scenario.StartsWith("wizard"))
            {
                AppSettings settings = SettingsStore.Load();
                var wizard = new DeviceSetupWindow(settings, null, () => false,
                    () => System.Threading.Tasks.Task.FromResult(true)) { Width = 820, Height = 680 };
                wizard.Offline = true;
                wizard.Loaded += (s, e) => wizard.JumpTo(scenario.Substring("wizard-".Length));
                window = wizard;
            }
            else if (scenario.StartsWith("device-settings"))
            {
                AppSettings settings = SettingsStore.Load();
                DeviceSettings device = settings.General.Devices.LastOrDefault() ?? new DeviceSettings { Name = "Wheel LEDs",
                    Port = "COM6", LedCount = 16 };
                window = new DeviceSettingsWindow(device, "Connected · fast updates");
            }
            else if (scenario.StartsWith("lights"))
            {
                AppSettings settings = SettingsStore.Load();
                DashboardSettings dashboard = SettingsStore.Clone(settings.GetDefaultDashboard());
                if (scenario.Contains("refined")) dashboard.Lights = LightPresets.Refined();
                var lights = new LightsWindow(settings, dashboard, null) { Width = 1440, Height = 880 };
                if (scenario.Contains("themes")) lights.Loaded += (s, e) => lights.ShowThemes();
                window = lights;
            }
            else if (scenario.StartsWith("template-picker"))
            {
                // "template-picker" with no wheel screen and no size chosen before; "template-picker-found" with a screen plugged in.
                var general = new GeneralSettings();
                window = TemplatePicker.Build(null, new UnitSystem(general), general,
                    scenario.EndsWith("found") ? new System.Drawing.Size(ScreenSizes.DefaultWidth, ScreenSizes.DefaultHeight)
                    : (System.Drawing.Size?)null,
                    (t, s) => { });
            }
            else if (scenario.StartsWith("formulas"))
            {
                // "formulas", or "formulas-fuel" already searched for fuel.
                AppSettings settings = SettingsStore.Load();
                var scope = new EvalScope { T = IRacingTelemetrySource.CreatePreviewSnapshot(), Units = new UnitSystem(settings.General),
                    Theme = settings.GetDefaultDashboard().Design?.Theme ?? new LayoutTheme() };
                FormulaReferenceWindow reference = FormulaReferenceWindow.Create(() => scope,
                    scenario.Contains("-") ? scenario.Substring(scenario.IndexOf('-') + 1) : null);
                reference.Width = 1060;
                reference.Height = 780;
                window = reference;
            }
            else if (scenario.StartsWith("designer"))
            {
                AppSettings settings = SettingsStore.Load();
                DashboardSettings dashboard = scenario.Contains("wide")
                    ? DashboardSettings.CreateDesigned(DesignTemplates.Road, 1280, 480)
                    : DashboardSettings.CreateDesigned(DesignTemplates.Find(scenario.Contains("blank") ? "blank" : scenario.Contains("oval") ? "spotter" : "apex"));
                var designer = new DesignerWindow(settings, dashboard, null) { Width = 1560, Height = 940 };
                designer.Loaded += (s, e) => designer.Dispatcher.BeginInvoke(new Action(() => designer.PrepareSnapshot(scenario)),
                    System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                window = designer;
            }
            else
            {
                var settingsWindow = new MainWindow { Width = 1360, Height = 900, Offline = true };
                settingsWindow.Loaded += (s,
                    e) => settingsWindow.Dispatcher.BeginInvoke(new Action(() => settingsWindow.PrepareSnapshot(scenario)),
                    System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                window = settingsWindow;
            }
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = 0;
            window.Top = 0;
            window.ShowActivated = false;
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                var content = (FrameworkElement)window.Content;
                var target = new System.Windows.Media.Imaging.RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96,
                    96, System.Windows.Media.PixelFormats.Pbgra32);
                // Glass windows are see-through; compose them over a dark desktop-like backdrop like the real blur.
                var backdrop = new System.Windows.Media.DrawingVisual();
                using (var dc = backdrop.RenderOpen())
                    dc.DrawRectangle(new System.Windows.Media.LinearGradientBrush(System.Windows.Media.Color.FromRgb(20, 30, 46),
                        System.Windows.Media.Color.FromRgb(8, 10, 16), 45),
                        null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
                target.Render(backdrop);
                if (window.Background is System.Windows.Media.SolidColorBrush tint && tint.Color.A < 255)
                {
                    var tintLayer = new System.Windows.Media.DrawingVisual();
                    using (var dc = tintLayer.RenderOpen()) dc.DrawRectangle(tint, null,
                        new Rect(0, 0, content.ActualWidth, content.ActualHeight));
                    target.Render(tintLayer);
                }
                target.Render(content);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(target));
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                using (FileStream file = File.Create(output)) encoder.Save(file);
                Environment.Exit(0);
            };
            // "...-hold" keeps the window open to try by hand instead of taking the picture.
            if (!scenario.Contains("hold")) window.Loaded += (s, e) => timer.Start();
            application.Run(window);
            return 0;
        }
    }
}
