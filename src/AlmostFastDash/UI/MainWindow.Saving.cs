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
        // Saving
        // ------------------------------------------------------------------

        private string _pendingSaveMessage;

        private void Changed()
        {
            RefreshPreview();
            Save();
        }

        /// <summary>Debounces writes so dragging a slider doesn't rewrite the file every tick.</summary>
        private void Save(string message = null)
        {
            if (_loading) return;
            _savePending = true;
            _pendingSaveMessage = message;
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private void FlushSave()
        {
            _saveTimer.Stop();
            if (!_savePending) return;
            _savePending = false;
            try
            {
                SettingsStore.Save(_settings);
                ShowSaved(_pendingSaveMessage ?? (IsDashboardRunning() ? "Saved · applied to the running dashboard" : "Saved"));
                // Names and the default show in the sidebar too.
                if (DashboardSubNav.Visibility == Visibility.Visible) BuildDashboardSubNav();
            }
            catch (Exception ex)
            {
                ShowSaved("Couldn't save: " + ex.Message);
            }
        }

        private void ShowSaved(string text)
        {
            SavedText.Text = text;
            var fade = new DoubleAnimationUsingKeyFrames();
            fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.4))));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3))));
            SavedIndicator.BeginAnimation(OpacityProperty, fade);
        }
    }
}
