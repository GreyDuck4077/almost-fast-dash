using System;
using System.Collections.Generic;
using System.Globalization;

namespace AlmostFastDash.IRacing
{
    /// <summary>
    /// Just enough YAML for iRacing's session string: indented maps, "- " lists
    /// and plain scalars. Values are kept as text.
    /// </summary>
    internal static class Yaml
    {
        public static Dictionary<string, object> Parse(string text)
        {
            var root = new Dictionary<string, object>();
            // Each open container with the indent of its keys; lists track the indent of their dashes.
            var stack = new List<KeyValuePair<int, object>> { new KeyValuePair<int, object>(-1, root) };
            string pendingKey = null;
            int pendingIndent = 0;
            Dictionary<string, object> pendingParent = null;

            foreach (string rawLine in text.Replace("\r", "").Split('\n'))
            {
                string line = rawLine.TrimEnd();
                string trimmed = line.TrimStart();
                if (trimmed.Length == 0 || trimmed == "---" || trimmed == "..." || trimmed.StartsWith("#")) continue;
                int indent = line.Length - trimmed.Length;
                bool item = trimmed.StartsWith("- ") || trimmed == "-";

                if (pendingKey != null)
                {
                    // A "key:" line opens a list or map, depending on the next line.
                    object child = item ? (object)new List<object>() : new Dictionary<string, object>();
                    pendingParent[pendingKey] = child;
                    stack.Add(new KeyValuePair<int, object>(item ? indent : Math.Max(indent, pendingIndent + 1), child));
                    pendingKey = null;
                }

                while (stack.Count > 1 && (indent < stack[stack.Count - 1].Key ||
                    (indent == stack[stack.Count - 1].Key && !item && stack[stack.Count - 1].Value is List<object>)))
                    stack.RemoveAt(stack.Count - 1);

                Dictionary<string, object> target;
                if (item)
                {
                    var list = stack[stack.Count - 1].Value as List<object>;
                    if (list == null) continue;
                    target = new Dictionary<string, object>();
                    list.Add(target);
                    trimmed = trimmed.Length > 2 ? trimmed.Substring(2).TrimStart() : "";
                    indent += 2;
                    stack.Add(new KeyValuePair<int, object>(indent, target));
                    if (trimmed.Length == 0) continue;
                }
                else
                {
                    target = stack[stack.Count - 1].Value as Dictionary<string, object>;
                    if (target == null) continue;
                }

                int colon = trimmed.IndexOf(':');
                if (colon <= 0) continue;
                string key = trimmed.Substring(0, colon).Trim();
                string value = trimmed.Substring(colon + 1).Trim();
                if (value.Length == 0)
                {
                    pendingKey = key;
                    pendingIndent = indent;
                    pendingParent = target;
                    target[key] = null;
                }
                else
                {
                    target[key] = Unquote(value);
                }
            }
            return root;
        }

        private static string Unquote(string value)
        {
            if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[value.Length - 1] == value[0])
                return value.Substring(1, value.Length - 2);
            return value;
        }

        public static Dictionary<string, object> Map(Dictionary<string, object> map, string key) =>
            map != null && map.TryGetValue(key, out object value) ? value as Dictionary<string, object> : null;

        public static IEnumerable<Dictionary<string, object>> Maps(Dictionary<string, object> map, string key)
        {
            if (map != null && map.TryGetValue(key, out object value) && value is List<object> list)
                foreach (object entry in list)
                    if (entry is Dictionary<string, object> child)
                        yield return child;
        }

        public static string Text(Dictionary<string, object> map, string key) =>
            map != null && map.TryGetValue(key, out object value) ? value as string : null;

        public static int Int(Dictionary<string, object> map, string key) => (int)Number(Text(map, key));

        /// <summary>The leading number of a value such as "2.50 km" or "1800.0000 sec"; 0 when there is none.</summary>
        public static double Number(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            string first = text.Trim().Split(' ')[0];
            return double.TryParse(first, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : 0;
        }
    }
}
