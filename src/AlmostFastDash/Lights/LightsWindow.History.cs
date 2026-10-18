using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using DrawingColor = System.Drawing.Color;

namespace AlmostFastDash.Lights
{
    public partial class LightsWindow : Window
    {
        // ==================================================================
        // Undo, redo, saving
        // ==================================================================

        private string Snapshot() => SettingsStore.ToJson(_profile);

        private void Commit(string label)
        {
            _pendingCommit = null;
            _commitTimer.Stop();
            string json = Snapshot();
            if (json != _committed)
            {
                bool merge = label == _lastCommitLabel && (DateTime.UtcNow - _lastCommitUtc).TotalMilliseconds < 1200 && _undo.Count > 0;
                if (!merge)
                {
                    _undo.Add(_committed);
                    if (_undo.Count > 300) _undo.RemoveAt(0);
                }
                _committed = json;
                _redo.Clear();
                _lastCommitLabel = label;
                _lastCommitUtc = DateTime.UtcNow;
                SaveSoon();
            }
            UpdateUndoButtons();
            UpdateSaveState();
        }

        private void CommitSoon(string label)
        {
            _pendingCommit = label;
            _commitTimer.Stop();
            _commitTimer.Start();
        }

        private void FlushCommit()
        {
            if (_pendingCommit != null) Commit(_pendingCommit);
        }

        private void Undo_Click(object sender, RoutedEventArgs e) => Undo();

        private void Redo_Click(object sender, RoutedEventArgs e) => Redo();

        private void Undo()
        {
            FlushCommit();
            if (_undo.Count == 0) return;
            _redo.Add(_committed);
            _committed = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            Load(_committed);
        }

        private void Redo()
        {
            if (_redo.Count == 0) return;
            _undo.Add(_committed);
            _committed = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            Load(_committed);
        }

        private void Load(string json)
        {
            string selectedId = _selected?.Id;
            _profile = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<LightProfile>(json);
            _profile.Normalize();
            _selected = _profile.Layers.FirstOrDefault(l => l.Id == selectedId) ?? _profile.Layers.FirstOrDefault();
            _renderer = null;
            _lastCommitLabel = null;
            LoadOutputControls();
            RefreshLayers();
            RefreshInspector();
            UpdateUndoButtons();
            UpdateSaveState();
            SaveSoon();
        }

        private void SaveSoon()
        {
            _liveSaveTimer.Stop();
            _liveSaveTimer.Start();
        }

        private void UpdateUndoButtons()
        {
            UndoButton.IsEnabled = _undo.Count > 0 || _pendingCommit != null;
            RedoButton.IsEnabled = _redo.Count > 0;
        }

        private bool Dirty => _committed != _savedJson;

        private void UpdateSaveState()
        {
            SaveState.Text = Dirty ? "Saving…" : "Saved";
            SaveStateIcon.Visibility = Dirty ? Visibility.Collapsed : Visibility.Visible;
        }

        private void SaveProfile(bool quiet)
        {
            FlushCommit();
            try
            {
                _dashboard.Lights = SettingsStore.Clone(_profile);
                SettingsStore.Save(_settings);
                _savedJson = _committed;
                _onSaved?.Invoke();
                if (!quiet) StatusText.Text = "Saved.";
            }
            catch (Exception exception)
            {
                StatusText.Text = "Could not save: " + exception.Message;
            }
            UpdateSaveState();
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            // Nothing is ever left unsaved: anything still waiting is written now.
            FlushCommit();
            _liveSaveTimer.Stop();
            if (Dirty) SaveProfile(true);
        }
    }
}
