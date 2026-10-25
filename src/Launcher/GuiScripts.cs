using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace AOLauncher
{
    internal sealed class TextMacro
    {
        public uint Id { get; set; }
        public string Name { get; set; }
        public string Text { get; set; }
        public override string ToString() => Name + "  →  " + Text;
    }

    // AO's per-character TextMacro.bin: a big-endian count, then per macro its number, name and text
    // (each string length-prefixed). Hotbars refer to macros by number. Nexus only validates and
    // previews the file; it is copied byte for byte and never logged or sent anywhere.
    internal static class TextMacros
    {
        internal const int MaxBytes = 256 * 1024, MaxMacros = 1000, MaxName = 256, MaxText = 8192;
        private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

        internal static List<TextMacro> Parse(byte[] data)
        {
            if (data == null || data.Length < 4 || data.Length > MaxBytes) throw new InvalidDataException("Unsupported text macro file.");
            int offset = 0;
            uint count = UInt(data, ref offset);
            if (count > MaxMacros) throw new InvalidDataException("Unsupported text macro file.");
            var result = new List<TextMacro>((int)count);
            for (uint index = 0; index < count; index++)
            {
                uint id = UInt(data, ref offset);
                string name = Text(data, ref offset, MaxName), text = Text(data, ref offset, MaxText);
                result.Add(new TextMacro { Id = id, Name = name, Text = text });
            }
            if (offset != data.Length) throw new InvalidDataException("Unsupported text macro file.");
            return result;
        }

        internal static List<TextMacro> Read(string charPath)
        {
            string path = Path.Combine(charPath, GuiLayouts.MacroFile);
            if (!File.Exists(path)) return new List<TextMacro>();
            GuiScripts.SafePath(path);
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (input.Length > MaxBytes) throw new InvalidDataException("Unsupported text macro file.");
                var bytes = new byte[input.Length]; int read = 0, count;
                while (read < bytes.Length && (count = input.Read(bytes, read, bytes.Length - read)) > 0) read += count;
                if (read != bytes.Length) throw new IOException("Macro file changed while reading.");
                return Parse(bytes);
            }
        }

        private static uint UInt(byte[] data, ref int offset)
        {
            if (offset > data.Length - 4) throw new InvalidDataException("Unsupported text macro file.");
            uint value = ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) | ((uint)data[offset + 2] << 8) | data[offset + 3];
            offset += 4; return value;
        }
        private static string Text(byte[] data, ref int offset, int limit)
        {
            uint length = UInt(data, ref offset);
            if (length > limit || length > data.Length - offset) throw new InvalidDataException("Unsupported text macro file.");
            string value = Latin1.GetString(data, offset, (int)length); offset += (int)length; return value;
        }
    }

    internal sealed class GuiScriptInfo
    {
        public string Name { get; set; }
        public long Bytes { get; set; }
        public DateTime ModifiedUtc { get; set; }
        public string Hash { get; set; }
        public override string ToString() => Name;
    }
    internal sealed class GuiScriptBackup
    {
        public string Id { get; set; }
        public string ScriptsRoot { get; set; }
        public string Action { get; set; }
        public DateTime CreatedUtc { get; set; }
        public string State { get; set; }
        public List<GuiScriptChange> Changes { get; set; } = new List<GuiScriptChange>();
        public override string ToString() => CreatedUtc.ToLocalTime().ToString("g") + " · " + State;
    }
    internal sealed class GuiScriptChange
    {
        public string Name { get; set; }
        public bool Existed { get; set; }
        public string BeforeHash { get; set; }
        public string AfterHash { get; set; }
    }

    // Chat scripts (/name) live in the scripts folder beside an installation's Prefs and are
    // shared by every character using it. Only plain text files at the top level are managed:
    // no subfolders, no executables, no binary content. Every change is backed up first.
    // Scripts are read by the game when used, so running clients do not need to close.
    internal static class GuiScripts
    {
        internal const int MaxScriptBytes = 64 * 1024, MaxScripts = 500;
        private static readonly Regex NamePattern = new Regex(@"\A[A-Za-z0-9][A-Za-z0-9_.\-]{0,63}\z", RegexOptions.CultureInvariant);
        private static readonly Regex IdPattern = new Regex(@"\A[a-f0-9]{32}\z", RegexOptions.CultureInvariant);
        private static readonly HashSet<string> BlockedExtensions = new HashSet<string>(new[] { ".exe", ".dll", ".bat", ".cmd", ".com", ".scr", ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".msi", ".msp", ".lnk", ".url", ".hta", ".cpl", ".reg", ".sys", ".jar", ".pif" }, StringComparer.OrdinalIgnoreCase);
        private static readonly object Sync = new object();
        // Injected only by synthetic tests, never exposed as a user setting.
        internal static Action<int> BeforeWrite;

        internal static string ScriptsRoot(string prefsRoot) => Path.Combine(Path.GetDirectoryName(GuiSkins.ResolvePrefsRoot(prefsRoot)), "scripts");

        internal static bool ValidName(string name)
        {
            if (name == null || !NamePattern.IsMatch(name) || name.EndsWith(".", StringComparison.Ordinal) || name.Contains("..")) return false;
            if (BlockedExtensions.Contains(Path.GetExtension(name))) return false;
            string stem = name.Split('.')[0].ToUpperInvariant();
            return stem != "CON" && stem != "PRN" && stem != "AUX" && stem != "NUL"
                && !(stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && stem[3] >= '0' && stem[3] <= '9');
        }

        internal static void ValidateContent(byte[] data)
        {
            if (data == null || data.Length > MaxScriptBytes) throw new InvalidDataException("Scripts must be text files of at most 64 KB.");
            if (Array.IndexOf(data, (byte)0) >= 0 || (data.Length >= 2 && data[0] == (byte)'M' && data[1] == (byte)'Z')) throw new InvalidDataException("Only plain text scripts are supported.");
        }

        internal static List<GuiScriptInfo> List(string prefsRoot)
        {
            lock (Sync)
            {
                string root = ScriptsRoot(prefsRoot); var result = new List<GuiScriptInfo>();
                if (!Directory.Exists(root)) return result;
                SafePath(root);
                foreach (string path in Directory.EnumerateFiles(root).Take(MaxScripts * 2))
                {
                    string name = Path.GetFileName(path);
                    if (!ValidName(name) || name.IndexOf(".nexus-", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    var info = new FileInfo(path);
                    if ((info.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System)) != 0 || info.Length > MaxScriptBytes) continue;
                    byte[] data;
                    try { data = ReadFile(path); ValidateContent(data); } catch (Exception error) when (error is IOException || error is InvalidDataException || error is UnauthorizedAccessException) { continue; }
                    result.Add(new GuiScriptInfo { Name = name, Bytes = data.Length, ModifiedUtc = info.LastWriteTimeUtc, Hash = Hash(data) });
                    if (result.Count >= MaxScripts) break;
                }
                return result.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }

        internal static byte[] ReadScript(string prefsRoot, string name)
        {
            if (!ValidName(name)) throw new InvalidDataException("Invalid script name.");
            byte[] data = ReadFile(Child(ScriptsRoot(prefsRoot), name)); ValidateContent(data); return data;
        }

        internal static string Preview(byte[] data) => Encoding.Default.GetString(data ?? new byte[0]);

        internal static GuiScriptBackup Copy(string sourcePrefsRoot, IEnumerable<string> names, string targetPrefsRoot, string backupRoot)
        {
            lock (Sync)
            {
                var outputs = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
                foreach (string name in names ?? Enumerable.Empty<string>())
                {
                    if (outputs.ContainsKey(name)) continue;
                    outputs[name] = ReadScript(sourcePrefsRoot, name);
                }
                if (outputs.Count == 0) throw new ArgumentException("Choose at least one script.");
                return Commit(ScriptsRoot(targetPrefsRoot), backupRoot, "Copy", outputs);
            }
        }

        internal static GuiScriptBackup Import(string prefsRoot, string filePath, string backupRoot)
        {
            lock (Sync)
            {
                SafePath(filePath); string name = Path.GetFileName(filePath);
                if (!ValidName(name)) throw new InvalidDataException("Script names may use letters, digits, '-', '_' and '.', up to 64 characters, and cannot be programs.");
                var info = new FileInfo(filePath);
                if (!info.Exists || info.Length > MaxScriptBytes) throw new InvalidDataException("Scripts must be text files of at most 64 KB.");
                byte[] data = ReadFile(filePath); ValidateContent(data);
                return Commit(ScriptsRoot(prefsRoot), backupRoot, "Import", new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase) { { name, data } });
            }
        }

        internal static GuiScriptBackup Delete(string prefsRoot, IEnumerable<string> names, string backupRoot)
        {
            lock (Sync)
            {
                var outputs = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
                foreach (string name in names ?? Enumerable.Empty<string>()) { ReadScript(prefsRoot, name); outputs[name] = null; }
                if (outputs.Count == 0) throw new ArgumentException("Choose at least one script.");
                return Commit(ScriptsRoot(prefsRoot), backupRoot, "Delete", outputs);
            }
        }

        internal static List<GuiScriptBackup> ListBackups(string prefsRoot, string backupRoot)
        {
            lock (Sync)
            {
                string scripts = Path.GetFullPath(ScriptsRoot(prefsRoot)); var result = new List<GuiScriptBackup>();
                if (string.IsNullOrWhiteSpace(backupRoot) || !Directory.Exists(backupRoot)) return result;
                SafePath(backupRoot);
                foreach (string folder in Directory.EnumerateDirectories(backupRoot).Take(1024))
                {
                    if (!IdPattern.IsMatch(Path.GetFileName(folder))) continue;
                    try { var item = Load(folder); if (item != null && item.Id == Path.GetFileName(folder) && SamePath(item.ScriptsRoot, scripts)) result.Add(item); }
                    catch (Exception error) when (error is IOException || error is InvalidDataException || error is ArgumentException || error is UnauthorizedAccessException || error is InvalidOperationException) { }
                }
                return result.OrderByDescending(item => item.CreatedUtc).ToList();
            }
        }

        internal static GuiScriptBackup Restore(string prefsRoot, string backupId, string backupRoot)
        {
            lock (Sync)
            {
                string scripts = ScriptsRoot(prefsRoot), folder = Item(backupRoot, backupId);
                var backup = Load(folder);
                if (backup == null || backup.Id != backupId || !SamePath(backup.ScriptsRoot, scripts) || backup.Changes == null || backup.Changes.Count > MaxScripts) throw new InvalidDataException("This backup belongs to another installation.");
                var outputs = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
                foreach (var change in backup.Changes)
                {
                    if (change == null || !ValidName(change.Name)) throw new InvalidDataException("Invalid backup entry.");
                    byte[] before = null;
                    if (change.Existed)
                    {
                        before = ReadFile(Child(folder, "Files/" + change.Name));
                        if (Hash(before) != change.BeforeHash) throw new InvalidDataException("Backup integrity check failed.");
                    }
                    outputs[change.Name] = before;
                }
                // The restore itself is backed up, so newer edits remain recoverable.
                var undo = Commit(scripts, backupRoot, "Restore", outputs);
                backup.State = "Restored"; Save(folder, backup);
                return undo;
            }
        }

        // outputs: script name → new content, or null to remove it.
        private static GuiScriptBackup Commit(string scriptsRoot, string backupRoot, string action, Dictionary<string, byte[]> outputs)
        {
            scriptsRoot = Path.GetFullPath(scriptsRoot); SafePath(scriptsRoot);
            var originals = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            var changes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in outputs)
            {
                if (!ValidName(pair.Key)) throw new InvalidDataException("Invalid script name.");
                if (pair.Value != null) ValidateContent(pair.Value);
                string path = Child(scriptsRoot, pair.Key);
                byte[] before = File.Exists(path) ? ReadFile(path) : null;
                if (before == null && pair.Value == null) continue;
                if (before != null && pair.Value != null && Hash(before) == Hash(pair.Value)) continue;
                originals[pair.Key] = before; changes[pair.Key] = pair.Value;
            }
            if (changes.Count == 0) return new GuiScriptBackup { ScriptsRoot = scriptsRoot, Action = action, CreatedUtc = DateTime.UtcNow, State = "Unchanged" };
            if (string.IsNullOrWhiteSpace(backupRoot) || !Path.IsPathRooted(backupRoot)) throw new IOException("Use an absolute backup path.");
            var backup = new GuiScriptBackup { Id = Guid.NewGuid().ToString("N"), ScriptsRoot = scriptsRoot, Action = action, CreatedUtc = DateTime.UtcNow, State = "Applying" };
            string folder = Item(backupRoot, backup.Id); Directory.CreateDirectory(folder);
            foreach (var pair in changes)
            {
                byte[] before = originals[pair.Key];
                backup.Changes.Add(new GuiScriptChange { Name = pair.Key, Existed = before != null, BeforeHash = before == null ? null : Hash(before), AfterHash = pair.Value == null ? null : Hash(pair.Value) });
                if (before != null) WriteAtomic(Child(folder, "Files/" + pair.Key), before);
            }
            Save(folder, backup);
            var written = new List<string>();
            try
            {
                Directory.CreateDirectory(scriptsRoot); SafePath(scriptsRoot);
                foreach (var pair in changes)
                {
                    BeforeWrite?.Invoke(written.Count);
                    string path = Child(scriptsRoot, pair.Key);
                    VerifyCurrent(path, originals[pair.Key]);
                    if (pair.Value == null) File.Delete(path); else WriteAtomic(path, pair.Value);
                    written.Add(pair.Key);
                }
                backup.State = "Applied"; Save(folder, backup);
                try { Prune(backupRoot, scriptsRoot, backup.Id); } catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is InvalidDataException) { }
                return backup;
            }
            catch
            {
                bool complete = true;
                foreach (string name in written.AsEnumerable().Reverse())
                {
                    try { string path = Child(scriptsRoot, name); VerifyCurrent(path, changes[name]); if (originals[name] == null) File.Delete(path); else WriteAtomic(path, originals[name]); }
                    catch { complete = false; }
                }
                backup.State = complete ? "Rolled back" : "Recovery required"; Save(folder, backup); throw;
            }
        }

        // Ten complete snapshots per scripts folder; failed or incomplete recoveries are kept.
        private static void Prune(string backupRoot, string scriptsRoot, string keepId)
        {
            var old = new List<GuiScriptBackup>();
            foreach (string folder in Directory.EnumerateDirectories(backupRoot).Take(1024))
            {
                if (!IdPattern.IsMatch(Path.GetFileName(folder)) || Path.GetFileName(folder) == keepId) continue;
                try { var item = Load(folder); if (item != null && item.Id == Path.GetFileName(folder) && SamePath(item.ScriptsRoot, scriptsRoot) && (item.State == "Applied" || item.State == "Restored" || item.State == "Rolled back")) old.Add(item); }
                catch (Exception error) when (error is IOException || error is InvalidDataException || error is ArgumentException || error is UnauthorizedAccessException || error is InvalidOperationException) { }
            }
            foreach (var item in old.OrderByDescending(entry => entry.CreatedUtc).Skip(9))
            {
                string folder = Item(backupRoot, item.Id); DeleteBackupFolder(folder);
            }
        }
        private static void DeleteBackupFolder(string folder)
        {
            SafePath(folder);
            string files = Path.Combine(folder, "Files");
            if (Directory.Exists(files))
            {
                SafePath(files);
                if (Directory.EnumerateDirectories(files).Any()) throw new IOException("Unexpected backup layout.");
                foreach (string file in Directory.EnumerateFiles(files).Take(MaxScripts + 1)) { SafePath(file); File.Delete(file); }
                Directory.Delete(files, false);
            }
            if (Directory.EnumerateDirectories(folder).Any()) throw new IOException("Unexpected backup layout.");
            foreach (string file in Directory.EnumerateFiles(folder)) { SafePath(file); File.Delete(file); }
            Directory.Delete(folder, false);
        }

        private static JavaScriptSerializer Json() => new JavaScriptSerializer { MaxJsonLength = 1024 * 1024, RecursionLimit = 16 };
        private static GuiScriptBackup Load(string folder)
        {
            string path = Child(folder, "manifest.json"); var info = new FileInfo(path);
            if (!info.Exists || info.Length > 1024 * 1024) throw new InvalidDataException("Invalid script backup.");
            return Json().Deserialize<GuiScriptBackup>(new UTF8Encoding(false, true).GetString(ReadFile(path)));
        }
        private static void Save(string folder, GuiScriptBackup value) => WriteAtomic(Child(folder, "manifest.json"), new UTF8Encoding(false).GetBytes(Json().Serialize(value)));
        private static string Item(string root, string id)
        {
            if (id == null || !IdPattern.IsMatch(id)) throw new InvalidDataException("Invalid backup identifier.");
            if (string.IsNullOrWhiteSpace(root) || !Path.IsPathRooted(root)) throw new IOException("Use an absolute backup path.");
            return Child(Path.GetFullPath(root), id);
        }
        private static string Child(string root, string relative)
        {
            if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(":") || relative.Split('/', '\\').Any(part => part == ".." || part == "." || part.Length == 0)) throw new InvalidDataException("Invalid script path.");
            string full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Path leaves the selected folder.");
            SafePath(full); return full;
        }
        internal static void SafePath(string path)
        {
            for (string cursor = Path.GetFullPath(path); !string.IsNullOrEmpty(cursor); cursor = Path.GetDirectoryName(cursor))
                if ((File.Exists(cursor) || Directory.Exists(cursor)) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked folders are not supported for script operations.");
        }
        private static byte[] ReadFile(string path)
        {
            SafePath(path);
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (input.Length > MaxScriptBytes) throw new InvalidDataException("Scripts must be text files of at most 64 KB.");
                var bytes = new byte[input.Length]; int read = 0, count;
                while (read < bytes.Length && (count = input.Read(bytes, read, bytes.Length - read)) > 0) read += count;
                if (read != bytes.Length) throw new IOException("Script changed while reading.");
                return bytes;
            }
        }
        private static void VerifyCurrent(string path, byte[] expected)
        {
            byte[] current = File.Exists(path) ? ReadFile(path) : null;
            if ((current == null) != (expected == null) || (current != null && Hash(current) != Hash(expected))) throw new IOException("A script changed during this operation. Try again.");
        }
        private static void WriteAtomic(string path, byte[] data)
        {
            SafePath(path); Directory.CreateDirectory(Path.GetDirectoryName(path)); SafePath(path);
            string temporary = path + ".nexus-" + Guid.NewGuid().ToString("N") + ".tmp";
            try { using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { output.Write(data, 0, data.Length); output.Flush(true); } SafePath(path); if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        private static string Hash(byte[] data) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant(); }
        private static bool SamePath(string a, string b) { try { return string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); } catch { return false; } }
    }
}
