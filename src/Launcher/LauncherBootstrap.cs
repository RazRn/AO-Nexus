using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace AOLauncher
{
    // Copy, never move: both applications retain independent settings and encrypted credentials.
    internal static class LauncherBootstrap
    {
        internal static bool ImportedThisSession { get; private set; }

        internal static void ImportExistingProfile()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            ImportedThisSession = ImportFromDirectory(Path.Combine(local, "AOQuickLauncher"), SettingsStore.DirectoryPath);
        }

        internal static bool ImportFromDirectory(string source, string destination)
        {
            source = Path.GetFullPath(source);
            destination = Path.GetFullPath(destination);
            if (string.Equals(source.TrimEnd('\\'), destination.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The source and destination profiles must differ.");
            string destinationFile = Path.Combine(destination, "AONexus.json");
            string lockKey;
            using (var hash = SHA256.Create())
                lockKey = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(destination.ToUpperInvariant()))).Replace("-", "");
            using (var mutex = new Mutex(false, "Local\\AONexus.Import." + lockKey))
            {
                bool owned = false;
                try
                {
                    try { owned = mutex.WaitOne(TimeSpan.FromSeconds(10)); }
                    catch (AbandonedMutexException) { owned = true; }
                    if (!owned) throw new IOException("Another AO Nexus window is importing your profile. Please try again.");
                    if (Exists(destinationFile)) return false;
                    string sourceFile = Path.Combine(source, "AOLauncher.json");
                    if (!Exists(sourceFile)) sourceFile = Path.Combine(source, "RebirthLauncher.json");
                    if (!Exists(sourceFile)) return false;
                    if (new FileInfo(sourceFile).Length > 8 * 1024 * 1024)
                        throw new InvalidDataException("The original launcher profile is too large to import.");
                    // Validate before copying anything. No password is decrypted during import.
                    var settings = SettingsStore.Deserialize(File.ReadAllText(sourceFile));
                    foreach (var account in settings.Accounts)
                    {
                        if (!account.RememberPassword) continue;
                        string name;
                        using (var sha = SHA256.Create())
                            name = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(account.Id))).Replace("-", "") + ".bin";
                        string encrypted = Path.Combine(source, "Credentials", name);
                        string copied = Path.Combine(destination, "Credentials", name);
                        if (!File.Exists(encrypted) || File.Exists(copied)) continue;
                        if (new FileInfo(encrypted).Length > 1024 * 1024)
                            throw new InvalidDataException("A saved credential is too large to import.");
                        Directory.CreateDirectory(Path.GetDirectoryName(copied));
                        File.Copy(encrypted, copied, false);
                    }
                    // The settings file is the completion marker, written atomically and last.
                    SettingsStore.SaveToDirectory(settings, destination);
                    return true;
                }
                finally { if (owned) mutex.ReleaseMutex(); }
            }
        }

        private static bool Exists(string path)
        {
            try { File.GetAttributes(path); return true; }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
        }
    }
}
