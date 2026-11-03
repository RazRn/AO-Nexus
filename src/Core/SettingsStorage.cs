using System;
using System.IO;

namespace Nexus.Core
{
    public static class SettingsStorage
    {
        public static bool Exists(string path)
        {
            try { File.GetAttributes(path); return true; }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
        }

        public static string ReadPath(string directory)
        {
            string current = Path.Combine(directory, "AONexus.json");
            string legacy = Path.Combine(directory, "RebirthLauncher.json");
            return Exists(current) || !Exists(legacy) ? current : legacy;
        }

        public static void Save(string directory, string json, Func<string, bool> needsMigrationBackup)
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "AONexus.json");
            string temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                if (File.Exists(path) && !File.Exists(path + ".before-account-targets.bak")
                    && needsMigrationBackup(File.ReadAllText(path)))
                    File.Copy(path, path + ".before-account-targets.bak");
                // Flush before atomic replacement; never truncate a user's working settings.
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(file, new System.Text.UTF8Encoding(false)))
                {
                    writer.Write(json);
                    writer.Flush();
                    file.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
