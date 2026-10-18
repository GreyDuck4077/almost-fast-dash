using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace AlmostFastDash.Settings
{
    internal static class SettingsStore
    {
        public static readonly string FilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

        /// <summary>Earlier copies of settings.json, newest first: the save before last, then the timed backups.</summary>
        public static readonly string BackupFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SettingsBackups");
        private const int BackupsKept = 12;
        private static readonly TimeSpan BackupEvery = TimeSpan.FromMinutes(10);

        public static AppSettings Load()
        {
            if (TryLoad(out AppSettings loaded)) return loaded;
            // Unreadable even after retrying: keep the file (it may be fixable by hand) and go back to the newest
            // backup that reads, so a damaged file costs the last few minutes of changes rather than everything.
            try
            {
                if (File.Exists(FilePath)) File.Copy(FilePath, FilePath + ".unreadable-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), true);
            }
            catch { }
            foreach (string backup in Backups())
            {
                try
                {
                    var restored = Json.Deserialize<AppSettings>(File.ReadAllText(backup));
                    if (restored == null) continue;
                    restored.EnsureBuiltInDashboards();
                    try { Save(restored); } catch { }
                    return restored;
                }
                catch { }
            }
            var defaults = AppSettings.CreateDefault();
            try { if (!File.Exists(FilePath)) Save(defaults); } catch { }
            return defaults;
        }

        /// <summary>
        /// Reads settings.json, retrying for a moment: another process may be swapping in a new copy at that
        /// instant. True with defaults when there's no file yet; false when it exists but couldn't be read.
        /// </summary>
        public static bool TryLoad(out AppSettings settings)
        {
            settings = null;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    if (!File.Exists(FilePath))
                    {
                        settings = AppSettings.CreateDefault();
                        try { Save(settings); } catch { }
                        return true;
                    }
                    var loaded = Json.Deserialize<AppSettings>(File.ReadAllText(FilePath));
                    if (loaded == null) return false;
                    int schema = loaded.SchemaVersion;
                    loaded.EnsureBuiltInDashboards();
                    // Persist one-time upgrades so every process sees the same lineup.
                    if (loaded.SchemaVersion != schema) try { Save(loaded); } catch { }
                    settings = loaded;
                    return true;
                }
                catch (IOException) { System.Threading.Thread.Sleep(40); }
                catch (UnauthorizedAccessException) { System.Threading.Thread.Sleep(40); }
                catch (Exception) { return false; }
            }
            return false;
        }

        /// <summary>
        /// Writes settings.json whole or not at all: a new copy is written beside it and swapped in, the old
        /// one becoming settings.json.bak. Every ten minutes of saving, a dated copy also goes into
        /// SettingsBackups (the last twelve are kept). Each process writes its own temporary file, so the
        /// main window and the dashboard can never write into the same one.
        /// </summary>
        public static void Save(AppSettings settings)
        {
            string temporary = FilePath + "." + System.Diagnostics.Process.GetCurrentProcess().Id + ".tmp";
            File.WriteAllText(temporary, Indent(Json.Serialize(settings)));
            if (File.Exists(FilePath))
            {
                TimedBackup();
                File.Replace(temporary, FilePath, FilePath + ".bak");
            }
            else File.Move(temporary, FilePath);
        }

        private static void TimedBackup()
        {
            try
            {
                Directory.CreateDirectory(BackupFolder);
                string[] dated = Directory.GetFiles(BackupFolder, "settings-*.json").OrderByDescending(f => f, StringComparer.Ordinal).ToArray();
                if (dated.Length > 0 && DateTime.UtcNow - File.GetLastWriteTimeUtc(dated[0]) < BackupEvery) return;
                File.Copy(FilePath, Path.Combine(BackupFolder, "settings-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json"), true);
                foreach (string old in dated.Skip(BackupsKept - 1)) File.Delete(old);
            }
            catch { }
        }

        /// <summary>The copies to fall back on, newest first.</summary>
        public static IEnumerable<string> Backups()
        {
            if (File.Exists(FilePath + ".bak")) yield return FilePath + ".bak";
            string[] dated;
            try { dated = Directory.Exists(BackupFolder) ? Directory.GetFiles(BackupFolder, "settings-*.json") : new string[0]; }
            catch { dated = new string[0]; }
            foreach (string file in dated.OrderByDescending(f => f, StringComparer.Ordinal)) yield return file;
        }

        public static DateTime LastWriteUtc()
        {
            try { return File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : DateTime.MinValue; }
            catch { return DateTime.MinValue; }
        }

        public static T Clone<T>(T value) => Json.Deserialize<T>(Json.Serialize(value));

        public static string ToJson(object value) => Json.Serialize(value);

        // Designs can embed images, so allow large settings files.
        private static JavaScriptSerializer Json => new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 256 };

        /// <summary>Lays out compact JSON with two-space indents so settings.json stays readable.</summary>
        private static string Indent(string json)
        {
            var output = new System.Text.StringBuilder(json.Length * 2);
            int depth = 0;
            bool quoted = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (quoted)
                {
                    output.Append(c);
                    if (c == '\\' && i + 1 < json.Length) output.Append(json[++i]);
                    else if (c == '"') quoted = false;
                    continue;
                }
                switch (c)
                {
                    case '"': quoted = true; output.Append(c); break;
                    case '{':
                    case '[':
                        output.Append(c);
                        if (i + 1 < json.Length && (json[i + 1] == '}' || json[i + 1] == ']')) { output.Append(json[++i]); break; }
                        output.AppendLine().Append(' ', ++depth * 2);
                        break;
                    case '}':
                    case ']': output.AppendLine().Append(' ', --depth * 2).Append(c); break;
                    case ',': output.Append(c).AppendLine().Append(' ', depth * 2); break;
                    case ':': output.Append(": "); break;
                    default: output.Append(c); break;
                }
            }
            return output.ToString();
        }

        public static Color ParseColor(string value, Color fallback)
        {
            try { return ColorTranslator.FromHtml(value); }
            catch { return fallback; }
        }

        public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }
}
