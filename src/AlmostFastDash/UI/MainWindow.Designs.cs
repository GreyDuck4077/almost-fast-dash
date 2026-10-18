using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using DrawingColor = System.Drawing.Color;

namespace AlmostFastDash.UI
{
    public partial class MainWindow : Window
    {
        // ------------------------------------------------------------------
        // Designer
        // ------------------------------------------------------------------

        private readonly Dictionary<string, DesignerWindow> _designers = new Dictionary<string, DesignerWindow>();

        private void OpenDesigner(DashboardSettings dashboard)
        {
            FlushSave();
            if (_designers.TryGetValue(dashboard.Id, out DesignerWindow open))
            {
                if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
                open.Activate();
                return;
            }
            var designer = new DesignerWindow(_settings, dashboard, () =>
            {
                if (GalleryPage.Visibility == Visibility.Visible) BuildGalleryCards();
                else if (EditorPage.Visibility == Visibility.Visible && _dashboard == dashboard) LoadDashboardValues();
                ShowSaved("Design saved");
            }) { Owner = this };
            _designers[dashboard.Id] = designer;
            designer.Closed += (s, e) => _designers.Remove(dashboard.Id);
            designer.Show();
        }

        private void Design_Click(object sender, RoutedEventArgs e) => OpenDesigner(_dashboard);

        private void ImportDesign()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Import dashboard design", Filter = "Almost Fast Dash dashboard (*.afodash)|*.afodash|All files|*.*" };
            if (dialog.ShowDialog(this) != true) return;
            DesignPackage package;
            try { package = DesignPackage.Read(dialog.FileName); }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "Import design", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DashboardSettings created = _settings.AddDesignedDashboard(DesignTemplates.Find("blank"), package.Name);
            created.Design = package.Design;
            if (!string.IsNullOrEmpty(package.AccentColor)) created.AccentColor = package.AccentColor;
            if (package.Lights != null)
            {
                created.Lights = package.Lights;
                created.Lights.Normalize();
            }
            Save("Imported " + created.Name);
            ShowEditor(created);
        }

        /// <summary>Saves a dashboard (its screens, images, accent and LED lights) to one file to share or back up.</summary>
        private void ExportDashboard(DashboardSettings dashboard)
        {
            FlushSave();
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export dashboard",
                Filter = "Almost Fast Dash dashboard (*.afodash)|*.afodash",
                FileName = DesignerWindow.SafeFileName(dashboard.Name) + ".afodash"
            };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                DesignPackage.Write(dialog.FileName, DesignPackage.From(dashboard));
                ShowSaved("Exported " + dashboard.Name);
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "Export dashboard", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
