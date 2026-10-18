using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AlmostFastDash.Designer
{
    public partial class DesignerWindow : Window
    {
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0, shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (ctrl && key == Key.S) { SaveDesign(false); e.Handled = true; return; }
            if (key == Key.F1) { FormulaReferenceWindow.Open(this, CurrentScope); e.Handled = true; return; }
            if (ctrl && (key == Key.Oem2 || key == Key.Divide)) { ShowShortcuts(); e.Handled = true; return; }

            // Text fields keep their own keys.
            if (Keyboard.FocusedElement is TextBox || Keyboard.FocusedElement is ComboBox) return;

            // The tool rail's keys: V, U, T, H, Z, Space held for the Hand, and Esc back to Move.
            if (PenKey(key) || EditKey(key)) { e.Handled = true; return; }
            if (!ctrl && (Keyboard.Modifiers & ModifierKeys.Alt) == 0 && ToolKey(key, shift, e)) { e.Handled = true; return; }

            double step = shift ? 10 : 1;
            switch (key)
            {
                case Key.Delete:
                case Key.Back: DeleteSelection(); break;
                case Key.Left: Nudge(-step, 0); break;
                case Key.Right: Nudge(step, 0); break;
                case Key.Up: Nudge(0, -step); break;
                case Key.Down: Nudge(0, step); break;
                case Key.Escape: _selection.Clear(); SelectionChanged(); break;
                case Key.OemCloseBrackets: Reorder(ctrl ? int.MaxValue : 1); break;
                case Key.OemOpenBrackets: Reorder(ctrl ? int.MinValue : -1); break;
                case Key.Z when ctrl: if (shift) Redo(); else Undo(); break;
                case Key.Y when ctrl: Redo(); break;
                case Key.D when ctrl && shift: RepeatSelection(); break;
                case Key.D when ctrl: DuplicateSelection(); break;
                case Key.D1 when shift && !ctrl: ZoomFit(); break;
                case Key.D2 when shift && !ctrl: ZoomToSelection(); break;
                case Key.G when ctrl && (Keyboard.Modifiers & ModifierKeys.Alt) != 0: ToggleClip(_selection); break;
                case Key.G when ctrl && shift: Ungroup(); break;
                case Key.G when ctrl: GroupSelection(); break;
                case Key.C when ctrl && shift: CopyStyle(); break;
                case Key.V when ctrl && shift: PasteStyle(); break;
                case Key.C when ctrl: CopySelection(); break;
                case Key.X when ctrl: CopySelection(); DeleteSelection(); break;
                case Key.V when ctrl: Paste(); break;
                case Key.A when ctrl: SelectAll(); break;
                case Key.L when ctrl: ToggleLock(); break;
                case Key.M when ctrl: SaveSelectionAsModule(); break;
                case Key.R when ctrl: ShowRulers(!_rulersOn); break;
                case Key.D0 when ctrl: ZoomFit(); break;
                case Key.OemPlus when ctrl: SetZoom(_zoom * 1.25); break;
                case Key.OemMinus when ctrl: SetZoom(_zoom / 1.25); break;
                default: return;
            }
            e.Handled = true;
        }
    }
}
