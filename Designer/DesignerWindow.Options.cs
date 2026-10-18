using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AlmostFastOverlays
{
    public partial class DesignerWindow
    {
        // ==================================================================
        // Module options: settings made changeable in one place for a whole group
        // ==================================================================

        private List<DashWidget> GroupMembers(DashWidget widget) =>
            string.IsNullOrEmpty(widget.Group) ? new List<DashWidget> { widget } : _screen.Widgets.Where(w => w.Group == widget.Group).ToList();

        /// <summary>
        /// The group's options at the top of Properties: each edited the way the settings linked to it are (a color,
        /// a number, a switch, a list), and every linked setting follows.
        /// </summary>
        private void AddModuleOptions(DashWidget member)
        {
            if (string.IsNullOrEmpty(member.Group)) return;
            List<string> names = ModuleOptions.Names(member);
            if (names.Count == 0) return;
            List<DashWidget> group = GroupMembers(member);
            Inspector.Children.Add(SectionTitle("Options"));
            foreach (string name in names)
            {
                string option = name;
                List<(DashWidget Widget, PropDef Def)> uses = ModuleOptions.Uses(group, option).ToList();
                PropDef shape = uses.Select(u => u.Def).FirstOrDefault() ?? new PropDef { Key = "option", Label = option, Kind = PropKind.Text, Default = "" };
                FrameworkElement editor = LiteralEditor(shape, member.Get(ModuleOptions.Prefix + option, ""), (value, structural) =>
                {
                    ModuleOptions.Set(group, option, value);
                    if (structural) _rendererDirty = true;
                    RequestRender();
                    CommitSoon("Option " + option);
                });
                var remove = new Button
                {
                    Style = (Style)FindResource("Btn.Ghost"), Width = 26, Height = 26, Padding = new Thickness(0), Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                    Content = new TextBlock { Text = "×", FontSize = 14 }, ToolTip = "Stop being an option: the settings linked to it keep its value"
                };
                remove.Click += (s, e) =>
                {
                    ModuleOptions.Remove(group, option);
                    Commit("Remove option");
                    _rendererDirty = true;
                    RefreshInspector();
                    RequestRender();
                    StatusText.Text = "\"" + option + "\" is no longer an option; its settings keep the value it had.";
                };
                var line = new Grid();
                line.ColumnDefinitions.Add(new ColumnDefinition());
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                line.Children.Add(editor);
                Grid.SetColumn(remove, 1);
                line.Children.Add(remove);
                string linked = uses.Count == 0 ? "Not linked to any setting yet."
                    : "Linked to " + string.Join(", ", uses.Select(u => (string.IsNullOrWhiteSpace(u.Widget.Name) ? u.Widget.Type : u.Widget.Name) + " › " + u.Def.Label).Distinct().Take(6)) + (uses.Count > 6 ? "…" : "") + ".";
                Inspector.Children.Add(LabeledRow(option, line, linked));
            }
            Inspector.Children.Add(Caption("Right-click a setting's name to add or link an option.", 2));
        }

        /// <summary>The menu on a setting's name: make it an option of its group, link it to one, or unlink it.</summary>
        private ContextMenu OptionMenu(DashWidget widget, PropDef def)
        {
            if (def.Kind == PropKind.Formula || def.Kind == PropKind.Image || def.Key == "visible" || def.Key == "clip") return null;
            var menu = new ContextMenu();
            string raw = widget.Get(def.Key, def.Default) ?? "";
            string bound = ModuleOptions.Names(widget).FirstOrDefault(n => raw == ModuleOptions.Formula(n));
            if (bound != null)
            {
                menu.Items.Add(MenuItem("Unlink from \"" + bound + "\"", null, () =>
                {
                    widget.Set(def.Key, widget.Get(ModuleOptions.Prefix + bound, def.Default));
                    Commit("Unlink option");
                    _rendererDirty = true;
                    RefreshInspector();
                    RequestRender();
                }));
                return menu;
            }
            if (raw.StartsWith("=")) return null;
            menu.Items.Add(MenuItem("Make this an option…", null, () => MakeOption(widget, def)));
            foreach (string name in ModuleOptions.Names(widget))
            {
                string option = name;
                menu.Items.Add(MenuItem("Link to option \"" + option + "\"", null, () => LinkOption(widget, def, option)));
            }
            return menu;
        }

        /// <summary>
        /// Makes a setting an option of its group (a widget on its own becomes a group of one), starting from the value
        /// it has now. An option of the same name already there is linked to instead.
        /// </summary>
        private void MakeOption(DashWidget widget, PropDef def)
        {
            string suggested = Regex.Replace(def.Label ?? def.Key, @"[^\p{L}\p{N} ]", "").Trim();
            string name = ModuleOptions.Clean(Prompt("Make an option", "Name it. It's set once for the whole group, " +
                "and it's what people change when they use the module.", suggested.Length == 0 ? "Option" : suggested));
            if (name.Length == 0) return;
            if (string.IsNullOrEmpty(widget.Group))
            {
                widget.Group = DashScreen.NewId("g");
                widget.GroupName = string.IsNullOrWhiteSpace(widget.Name) ? def.Label : widget.Name;
                widget.Slot = 0;
            }
            List<DashWidget> group = GroupMembers(widget);
            if (widget.Get(ModuleOptions.Prefix + name) == null) ModuleOptions.Set(group, name, widget.Get(def.Key, def.Default) ?? "");
            LinkOption(widget, def, name);
            StatusText.Text = "\"" + name + "\" is now an option of " + widget.GroupName + ". Select the group to set it.";
        }

        private void LinkOption(DashWidget widget, PropDef def, string name)
        {
            widget.Set(def.Key, ModuleOptions.Formula(name));
            Commit("Option " + name);
            _rendererDirty = true;
            RefreshInspector();
            RefreshLayers();
            RequestRender();
        }
    }
}
