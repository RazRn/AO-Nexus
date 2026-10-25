using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace AOLauncher
{
    internal static class LogRetention
    {
        internal static int NormalizeMb(int value) => value < 1 || value > 100 ? 5 : value;
        internal static string Tail(string text, int bytes)
        {
            var encoded = Encoding.UTF8.GetBytes(text ?? "");
            if (encoded.Length <= bytes) return text ?? "";
            int start = encoded.Length - bytes;
            while (start < encoded.Length && (encoded[start] & 0xC0) == 0x80) start++;
            // Prefer complete entries; retain a bounded tail even for one huge line.
            int newline = Array.IndexOf(encoded, (byte)'\n', start);
            if (newline >= start && newline < encoded.Length - 1) start = newline + 1;
            return Encoding.UTF8.GetString(encoded, start, encoded.Length - start);
        }
        internal static void AppendFile(string path, string entry, int limit)
        {
            string addition = Tail(entry, limit);
            using (var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read))
            {
                var bytes = Encoding.UTF8.GetBytes(addition);
                if (stream.Length + bytes.Length > limit)
                {
                    int keep = (int)Math.Min(stream.Length, Math.Max(0, limit - bytes.Length));
                    var buffer = new byte[keep]; stream.Position = stream.Length - keep;
                    int read = 0, count;
                    while (read < keep && (count = stream.Read(buffer, read, keep - read)) > 0) read += count;
                    int start = 0;
                    while (start < read && (buffer[start] & 0xC0) == 0x80) start++;
                    int newline = Array.IndexOf(buffer, (byte)'\n', start);
                    if (newline >= start && newline < read - 1) start = newline + 1;
                    stream.Position = 0;
                    stream.Write(buffer, start, read - start); stream.SetLength(read - start);
                }
                stream.Position = stream.Length; stream.Write(bytes, 0, bytes.Length);
            }
        }
    }

    internal static class UpdateCleanup
    {
        private static readonly string[] StageFiles = { "release.zip", "AONexus.exe", "request.json", "update-result.txt", "complete", ".active" };
        internal static bool PlainPath(string path)
        {
            for (string current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
            return true;
        }
        // Only our direct GUID staging directories and their known files. Never recursive deletion.
        internal static bool RemoveStage(string root, string folder)
        {
            try
            {
                Guid id;
                root = Path.GetFullPath(root); folder = Path.GetFullPath(folder);
                if (!string.Equals(Path.GetDirectoryName(folder), root, StringComparison.OrdinalIgnoreCase)
                    || !Guid.TryParseExact(Path.GetFileName(folder), "N", out id) || !Directory.Exists(folder) || !PlainPath(folder)
                    || Directory.GetDirectories(folder).Length != 0) return false;
                string[] files = Directory.GetFiles(folder);
                if (files.Any(file => !StageFiles.Contains(Path.GetFileName(file), StringComparer.OrdinalIgnoreCase) || !PlainPath(file))) return false;
                using (var gate = new FileStream(Path.Combine(folder, ".active"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                {
                    // The running helper locks its EXE too; delete it first so failures leave metadata intact.
                    string executable = Path.Combine(folder, "AONexus.exe");
                    if (File.Exists(executable)) File.Delete(executable);
                    foreach (string file in files.Where(file => Path.GetFileName(file) != ".active" && Path.GetFileName(file) != "AONexus.exe")) File.Delete(file);
                }
                File.Delete(Path.Combine(folder, ".active")); Directory.Delete(folder, false); return true;
            }
            catch (IOException) { return false; } catch (UnauthorizedAccessException) { return false; }
        }
        internal static void Sweep(string root, string target, DateTime now)
        {
            try
            {
                target = Path.GetFullPath(target);
                if (Directory.Exists(root) && PlainPath(root))
                    foreach (string folder in Directory.GetDirectories(root))
                        if (PlainPath(folder) && (File.Exists(Path.Combine(folder, "complete")) || Directory.GetLastWriteTimeUtc(folder) < now.AddDays(-1)))
                            RemoveStage(root, folder);
                string parent = Path.GetDirectoryName(Path.GetFullPath(target));
                if (!Directory.Exists(parent) || !PlainPath(parent)) return;
                var backups = Directory.GetFiles(parent, Path.GetFileName(target) + ".previous-*")
                    .Where(file => IsBackup(file, target)).OrderByDescending(File.GetLastWriteTimeUtc).ToArray();
                foreach (string backup in backups.Skip(1)) File.Delete(backup);
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        private static bool IsBackup(string file, string target)
        {
            Guid id;
            if (!PlainPath(file) || !Guid.TryParseExact(file.Substring(target.Length + ".previous-".Length), "N", out id)) return false;
            try { return AssemblyName.GetAssemblyName(file).Name == "AONexus"; }
            catch (BadImageFormatException) { return false; } catch (IOException) { return false; } catch (UnauthorizedAccessException) { return false; }
        }
        internal static async Task AfterStartup(string target)
        {
            // Give the helper time to exit and release its executable before cleaning its completed stage.
            await Task.Delay(3000).ConfigureAwait(false);
            await Task.Run(() => Sweep(AppUpdates.UpdateRoot, target, DateTime.UtcNow)).ConfigureAwait(false);
        }
    }
}
