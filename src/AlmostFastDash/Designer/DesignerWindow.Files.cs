using System;
using System.Windows;

namespace AlmostFastDash.Designer
{
    public partial class DesignerWindow : Window
    {
        // ==================================================================
        // Import and export
        // ==================================================================

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            FlushPendingCommit();
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export dashboard design",
                Filter = "Almost Fast Dash dashboard (*.afodash)|*.afodash",
                FileName = SafeFileName(_dashboard.Name) + ".afodash"
            };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                DesignPackage.Write(dialog.FileName, DesignPackage.From(_dashboard, _layout));
                StatusText.Text = "Exported to " + dialog.FileName;
            }
            catch (Exception exception)
            {
                StatusText.Text = "Export failed: " + exception.Message;
            }
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Import dashboard design",
                Filter = "Almost Fast Dash dashboard (*.afodash)|*.afodash|All files|*.*" };
            if (dialog.ShowDialog(this) != true) return;
            DesignPackage package;
            try { package = DesignPackage.Read(dialog.FileName); }
            catch (Exception exception) { StatusText.Text = "Import failed: " + exception.Message; return; }
            if (MessageBox.Show(this, "Replace this design with \"" + package.Name + "\"? You can undo this.", "Import design",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
                return;
            _layout = package.Design;
            _layout.Normalize();
            _screen = _layout.Screens[0];
            if (!string.IsNullOrEmpty(package.AccentColor)) _dashboard.AccentColor = package.AccentColor;
            _selection.Clear();
            _rendererDirty = true;
            Commit("Import");
            BuildScreensCombo();
            BuildThemeEditor();
            SelectionChanged();
            RequestRender();
        }

        internal static string SafeFileName(string name)
        {
            foreach (char c in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(c, '-');
            return name;
        }
    }
}
