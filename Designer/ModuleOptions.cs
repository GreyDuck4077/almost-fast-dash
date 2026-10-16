using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AlmostFastOverlays
{
    /// <summary>
    /// Module options: settings a module's maker made changeable in one place, such as its accent color or its
    /// redline. Each is a value on every widget of the group (a property named "opt:" and the option's name, as the
    /// group's Slot is carried by each widget), and settings linked to it read it with the formula option("Name").
    /// </summary>
    internal static class ModuleOptions
    {
        public const string Prefix = "opt:";

        /// <summary>The option's value for a formula: a number when it is one, otherwise its text (a color, a word…).</summary>
        public static object Value(DashWidget widget, string name)
        {
            string raw = widget?.Get(Prefix + (name ?? "").Trim());
            if (raw == null) return null;
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)) return number;
            return raw;
        }

        public static string Formula(string name) => "=option(\"" + name + "\")";

        /// <summary>A name that's safe inside the formula's quotes.</summary>
        public static string Clean(string name) => (name ?? "").Replace("\"", "'").Trim();

        public static List<string> Names(DashWidget widget) =>
            widget?.Props == null ? new List<string>() : widget.Props.Keys.Where(k => k.StartsWith(Prefix)).Select(k => k.Substring(Prefix.Length)).ToList();

        /// <summary>The settings linked to an option, across a group.</summary>
        public static IEnumerable<(DashWidget Widget, PropDef Def)> Uses(IEnumerable<DashWidget> group, string name)
        {
            string formula = Formula(name);
            foreach (DashWidget w in group)
            {
                WidgetType type = WidgetCatalog.Find(w.Type);
                if (type == null) continue;
                foreach (PropDef def in type.Props)
                    if (w.Get(def.Key) == formula) yield return (w, def);
            }
        }

        /// <summary>Sets an option on every widget of the group.</summary>
        public static void Set(IEnumerable<DashWidget> group, string name, string value)
        {
            foreach (DashWidget w in group) w.Set(Prefix + name, value ?? "");
        }

        /// <summary>Ends an option: every setting linked to it keeps the option's value as its own, fixed.</summary>
        public static void Remove(IList<DashWidget> group, string name)
        {
            string value = group.Select(w => w.Get(Prefix + name)).FirstOrDefault(v => v != null) ?? "";
            foreach ((DashWidget widget, PropDef def) in Uses(group, name).ToList()) widget.Set(def.Key, value);
            foreach (DashWidget w in group) w.Props.Remove(Prefix + name);
        }
    }
}
