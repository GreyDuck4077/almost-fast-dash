using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AlmostFastDash.Designer
{
    /// <summary>
    /// A formula editor: monospace text, an Insert button that opens the data and
    /// function picker, and a live line showing the current result or the error.
    /// </summary>
    internal sealed class FormulaBox : StackPanel
    {
        private readonly FrameworkElement _owner;
        private readonly Func<EvalScope> _scope;
        public readonly TextBox Text;
        public readonly TextBlock Result;
        public event Action<string> Changed;

        public FormulaBox(FrameworkElement owner, string formula, Func<EvalScope> scope, string placeholder = null)
        {
            _owner = owner;
            _scope = scope;
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Text = new TextBox
            {
                Text = formula ?? "", FontFamily = (FontFamily)owner.FindResource("Font.Mono"), FontSize = 12, TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = false, MinHeight = 30, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = placeholder
            };
            Text.TextChanged += (s, e) => { Validate(); Changed?.Invoke(Text.Text); };
            Text.GotKeyboardFocus += (s, e) => _last = new WeakReference<FormulaBox>(this);
            row.Children.Add(Text);
            var insert = new Button
            {
                Style = (Style)owner.FindResource("Btn.Ghost"), Width = 30, Height = 30, Padding = new Thickness(0), Margin = new Thickness(4, 0, 0, 0),
                ToolTip = "Insert data, a function or a recipe",
                Content = new TextBlock { Text = "", Style = (Style)owner.FindResource("T.Icon"), FontSize = 12 }
            };
            insert.Click += (s, e) => DataPicker.Open(owner, insert, _scope, InsertText);
            Grid.SetColumn(insert, 1);
            row.Children.Add(insert);
            Children.Add(row);
            Result = new TextBlock { FontFamily = (FontFamily)owner.FindResource("Font.Mono"), FontSize = 12, Margin = new Thickness(2, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            Children.Add(Result);
            Validate();
        }

        /// <summary>The formula last clicked into, for the formula reference's Insert.</summary>
        private static WeakReference<FormulaBox> _last;

        /// <summary>
        /// Inserts at the cursor of the formula last clicked into, when it's still on screen (the inspector rebuilds
        /// its rows as the selection changes). False when there's none to insert into.
        /// </summary>
        public static bool InsertIntoLast(string snippet, out string where)
        {
            where = "the formula";
            if (_last == null || !_last.TryGetTarget(out FormulaBox box) || !box.IsLoaded || !box.IsVisible || PresentationSource.FromVisual(box) == null) return false;
            Window.GetWindow(box)?.Activate();
            box.InsertText(snippet, false);
            return true;
        }

        private void InsertText(string snippet, bool replace)
        {
            if (replace || Text.Text.Trim().Length == 0) Text.Text = snippet;
            else
            {
                int at = Text.CaretIndex;
                Text.Text = Text.Text.Insert(at, snippet);
                Text.CaretIndex = at + snippet.Length;
            }
            Text.Focus();
        }

        private void Validate()
        {
            Expr.Compiled compiled = Expr.Compile(Text.Text);
            if (compiled.Error != null)
            {
                Result.Text = "⚠ " + compiled.Error;
                Result.Foreground = (Brush)_owner.FindResource("Brush.Danger");
            }
            else ShowResult();
        }

        public void ShowResult()
        {
            Expr.Compiled compiled = Expr.Compile(Text.Text);
            if (compiled.Error != null || Text.Text.Trim().Length == 0)
            {
                if (Text.Text.Trim().Length == 0) Result.Text = "";
                return;
            }
            object value = Expr.Evaluate(Text.Text, _scope());
            string shown = value is System.Drawing.Color c ? SettingsStore.ToHex(c) + (c.A < 255 ? " @" + Math.Round(c.A / 2.55) + "%" : "") : Expr.Text(value);
            Result.Text = "→ " + (shown.Length == 0 ? "(empty)" : shown);
            Result.Foreground = (Brush)_owner.FindResource("Brush.TextTertiary");
        }
    }
}
