using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;

namespace AlmostFastDash.Designer
{
    public partial class DesignerWindow : Window
    {
        // ==================================================================
        // Undo, redo and saving
        // ==================================================================

        private string Snapshot() => SettingsStore.ToJson(_layout);

        /// <summary>Records the design as one undo step. Rapid repeats of the same edit merge into one.</summary>
        internal void Commit(string label)
        {
            _pendingCommitLabel = null;
            _commitTimer.Stop();
            // Every finished edit passes through here, so the preview always catches up with it.
            RequestRender();
            string json = Snapshot();
            if (json == _committed)
            {
                // The accent lives on the dashboard, not the design, so it can change without a new snapshot.
                if (Dirty) SaveSoon();
                UpdateSaveState();
                return;
            }
            // Quick repeats of one edit (a slider dragged) are one step to undo; each widget added or drawn is its own.
            bool merge = label == _lastCommitLabel && (DateTime.UtcNow - _lastCommitUtc).TotalMilliseconds < 1200 && label != "Add" &&
                !label.StartsWith("Draw ") && _undo.Count > 0;
            if (!merge)
            {
                _undo.Add(_committed);
                if (_undo.Count > 300) _undo.RemoveAt(0);
            }
            _committed = json;
            _redo.Clear();
            _lastCommitLabel = label;
            _lastCommitUtc = DateTime.UtcNow;
            UpdateUndoButtons();
            UpdateSaveState();
            SaveSoon();
        }

        private void SaveSoon()
        {
            _liveSaveTimer.Stop();
            _liveSaveTimer.Start();
        }

        /// <summary>Commits after typing pauses, so each keystroke is not its own undo step.</summary>
        internal void CommitSoon(string label)
        {
            _pendingCommitLabel = label;
            _commitTimer.Stop();
            _commitTimer.Start();
        }

        private void FlushPendingCommit()
        {
            if (_pendingCommitLabel != null) Commit(_pendingCommitLabel);
        }

        private void Undo_Click(object sender, RoutedEventArgs e) => Undo();

        private void Redo_Click(object sender, RoutedEventArgs e) => Redo();

        private void Undo()
        {
            FlushPendingCommit();
            if (_undo.Count == 0) return;
            _redo.Add(_committed);
            _committed = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            LoadSnapshot(_committed);
            StatusText.Text = "Undid " + (_lastCommitLabel ?? "change").ToLowerInvariant() + ".";
            _lastCommitLabel = null;
        }

        private void Redo()
        {
            if (_redo.Count == 0) return;
            _undo.Add(_committed);
            _committed = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            LoadSnapshot(_committed);
            _lastCommitLabel = null;
        }

        private void LoadSnapshot(string json)
        {
            string screenId = _screen?.Id;
            var selected = new HashSet<string>(_selection.Select(w => w.Id));
            _layout = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue,
                RecursionLimit = 256 }.Deserialize<DashLayout>(json);
            _layout.Normalize();
            _screen = _layout.Screens.FirstOrDefault(s => s.Id == screenId) ?? _layout.Screens[0];
            _selection.Clear();
            _selection.AddRange(_screen.Widgets.Where(w => selected.Contains(w.Id)));
            _rendererDirty = true;
            BuildScreensCombo();
            BuildThemeEditor();
            SelectionChanged();
            UpdateUndoButtons();
            UpdateSaveState();
            RequestRender();
            SaveSoon();
        }

        private void UpdateUndoButtons()
        {
            UndoButton.IsEnabled = _undo.Count > 0 || _pendingCommitLabel != null;
            RedoButton.IsEnabled = _redo.Count > 0;
        }

        // The accent belongs to the dashboard rather than the design, so it is tracked separately.
        private string _savedAccent;

        private bool Dirty => _committed != _savedJson || _dashboard.AccentColor != _savedAccent;

        private void UpdateSaveState()
        {
            if (SaveState == null) return;
            SaveState.Text = Dirty ? "Saving…" : "Saved";
            SaveStateIcon.Visibility = Dirty ? Visibility.Collapsed : Visibility.Visible;
        }

        private void SaveDesign(bool quiet)
        {
            FlushPendingCommit();
            try
            {
                _dashboard.Design = SettingsStore.Clone(_layout);
                SettingsStore.Save(_settings);
                _savedJson = _committed;
                _savedAccent = _dashboard.AccentColor;
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
            FlushPendingCommit();
            _liveSaveTimer.Stop();
            if (Dirty) SaveDesign(true);
        }
    }
}
