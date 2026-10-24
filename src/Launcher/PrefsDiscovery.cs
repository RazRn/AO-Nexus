using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace AOLauncher
{
    internal sealed class PrefsCandidate
    {
        // This is the account folder, ready for PrefsImporter.Scan.
        public string PrefsPath { get; internal set; }
        public string InstallationName { get; internal set; }
        public string AccountName { get; internal set; }
        public string GamePath { get; internal set; }
        public int CharacterCount { get; internal set; }
        // Directory metadata is a recency hint, not proof of the last game login.
        public DateTime LastUsedUtc { get; internal set; }
        public bool GameExists { get; internal set; }
        public bool MatchesGamePath { get; internal set; }
        public bool MatchesGameFolderName { get; internal set; }
        public bool MatchesUsername { get; internal set; }
    }

    internal static class PrefsDiscovery
    {
        private const int MaxDirectories = 20000;
        private const int MaxInstallations = 256;
        private const int MaxLogBytes = 1024 * 1024;
        private const int MaxTotalLogBytes = 16 * MaxLogBytes;
        private const int MaxRegistryEntriesPerView = 2048;
        private static readonly Regex HashFolder = new Regex(@"\A[0-9a-f]{8}\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex GameExecutablePath = new Regex(
            @"(?<path>[a-z]:[\\/](?:[^\\/\r\n:""<>|?*]+[\\/])*(?:AnarchyPatcher|AnarchyOnline|Anarchy)\.exe)(?=$|[\s""'(),;])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(200));

        public static string DefaultRoot => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Funcom", "Anarchy Online");

        public static IReadOnlyList<PrefsCandidate> Find(string prefsRoot, string selectedGamePath, string username)
        {
            return FindWithInstallations(prefsRoot, selectedGamePath, username, ReadRegisteredInstallations());
        }

        public static IReadOnlyList<PrefsCandidate> Find(string prefsRoot, string selectedGamePath,
            string username, IReadOnlyList<string> registeredGamePaths)
        {
            return FindWithInstallations(prefsRoot, selectedGamePath, username,
                (registeredGamePaths ?? new string[0]).Take(MaxInstallations)
                    .Select(path => new PrefsInstallation { GamePath = path }).ToArray());
        }

        // Test seam: injected installation metadata never accesses the registry.
        internal static IReadOnlyList<PrefsCandidate> FindWithInstallations(string prefsRoot, string selectedGamePath,
            string username, IReadOnlyList<PrefsInstallation> installationsMetadata)
        {
            var results = new List<PrefsCandidate>();
            string root = NormalizeAbsolutePath(prefsRoot);
            if (!SafeDirectory(root)) return results.AsReadOnly();

            string selectedPath = NormalizeAbsolutePath(selectedGamePath);
            string selectedName = selectedPath == null ? null : Path.GetFileName(selectedPath);
            string requestedUser = username == null ? null : username.Trim();
            var registered = (installationsMetadata ?? new PrefsInstallation[0]).Take(MaxInstallations)
                .Where(item => item != null).Select(item => new PrefsInstallation {
                    GamePath = NormalizeAbsolutePath(item.GamePath), DisplayName = item.DisplayName, IsSteam = item.IsSteam
                }).Where(item => item.GamePath != null).ToArray();
            var budget = new ScanBudget();
            int installations = 0;

            foreach (string hash in ChildDirectories(root, budget))
            {
                if (!HashFolder.IsMatch(Path.GetFileName(hash))) continue;
                foreach (string installation in ChildDirectories(hash, budget))
                {
                    if (++installations > MaxInstallations) break;
                    string installationName = Path.GetFileName(installation);
                    if (IsBrowser(installationName)) continue;
                    string prefs = Path.Combine(installation, "Prefs");
                    if (!SafeDirectory(prefs)) continue;
                    string gamePath = FindGamePath(installation, budget);
                    bool exists = gamePath != null && SafeGameExecutable(gamePath);
                    var registration = registered.FirstOrDefault(item => gamePath != null && string.Equals(item.GamePath, gamePath, StringComparison.OrdinalIgnoreCase));
                    string displayName = installationName;
                    if (registration != null)
                    {
                        if (!string.IsNullOrWhiteSpace(registration.DisplayName)) displayName = registration.DisplayName;
                        if (registration.IsSteam) displayName += " (Steam)";
                    }

                    foreach (string account in ChildDirectories(prefs, budget))
                    {
                        string accountName = Path.GetFileName(account);
                        if (IsBrowser(accountName)) continue;
                        var ids = new HashSet<int>();
                        DateTime lastUsed = ModifiedUtc(account);
                        foreach (string character in ChildDirectories(account, budget))
                        {
                            int id;
                            if (!TryCharacterId(Path.GetFileName(character), out id)) continue;
                            ids.Add(id);
                            DateTime modified = ModifiedUtc(character);
                            if (modified > lastUsed) lastUsed = modified;
                        }
                        if (ids.Count == 0) continue;
                        results.Add(new PrefsCandidate {
                            PrefsPath = account,
                            InstallationName = displayName,
                            AccountName = accountName,
                            GamePath = gamePath,
                            CharacterCount = ids.Count,
                            LastUsedUtc = lastUsed,
                            GameExists = exists,
                            MatchesGamePath = selectedPath != null && string.Equals(gamePath, selectedPath, StringComparison.OrdinalIgnoreCase),
                            MatchesGameFolderName = gamePath == null && selectedName != null && string.Equals(installationName, selectedName, StringComparison.OrdinalIgnoreCase),
                            MatchesUsername = !string.IsNullOrEmpty(requestedUser) && string.Equals(accountName, requestedUser, StringComparison.OrdinalIgnoreCase)
                        });
                    }
                }
                if (installations >= MaxInstallations || budget.Directories <= 0) break;
            }

            return results.OrderByDescending(item => item.MatchesGamePath)
                .ThenByDescending(item => item.MatchesUsername)
                .ThenByDescending(item => item.GameExists)
                .ThenByDescending(item => item.MatchesGameFolderName)
                .ThenByDescending(item => item.LastUsedUtc)
                .ThenBy(item => item.PrefsPath, StringComparer.OrdinalIgnoreCase)
                .ToList().AsReadOnly();
        }

        private static string FindGamePath(string installation, ScanBudget budget)
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in new[] { "AnarchyPatcher.err", "Anarchy.err" })
            {
                bool incomplete;
                string text = ReadBoundedLog(Path.Combine(installation, name), budget, out incomplete);
                if (incomplete) return null;
                if (text == null) continue;
                try
                {
                    foreach (Match match in GameExecutablePath.Matches(text))
                    {
                        string executable = NormalizeAbsolutePath(match.Groups["path"].Value);
                        if (executable != null) paths.Add(Path.GetDirectoryName(executable));
                        if (paths.Count > 1) return null;
                    }
                }
                catch (RegexMatchTimeoutException) { return null; }
            }
            if (paths.Count != 1) return null;
            return paths.First();
        }

        private static IReadOnlyList<PrefsInstallation> ReadRegisteredInstallations()
        {
            const string uninstall = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
            var results = new List<PrefsInstallation>();
            foreach (RegistryHive hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (var registry = RegistryKey.OpenBaseKey(hive, view))
                    using (var entries = registry.OpenSubKey(uninstall, false))
                    {
                        if (entries == null) continue;
                        foreach (string name in entries.GetSubKeyNames().Take(MaxRegistryEntriesPerView))
                        {
                            if (results.Count >= MaxInstallations) return results.AsReadOnly();
                            try
                            {
                                using (var entry = entries.OpenSubKey(name, false))
                                {
                                    if (entry == null) continue;
                                    string displayName = entry.GetValue("DisplayName", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                                    if (displayName == null || displayName.Length > 256 || displayName.IndexOf("Anarchy Online", StringComparison.OrdinalIgnoreCase) < 0 || displayName.Any(char.IsControl)) continue;
                                    string path = entry.GetValue("InstallLocation", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                                    path = NormalizeAbsolutePath(path);
                                    if (path == null) continue;
                                    results.Add(new PrefsInstallation { GamePath = path, DisplayName = displayName.Trim(), IsSteam = name.StartsWith("Steam App ", StringComparison.OrdinalIgnoreCase) });
                                }
                            }
                            catch (IOException) { }
                            catch (UnauthorizedAccessException) { }
                            catch (SecurityException) { }
                        }
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                catch (SecurityException) { }
                catch (PlatformNotSupportedException) { }
            }
            return results.AsReadOnly();
        }

        private static string ReadBoundedLog(string path, ScanBudget budget, out bool incomplete)
        {
            incomplete = false;
            try
            {
                FileAttributes attributes;
                try { attributes = File.GetAttributes(path); }
                catch (FileNotFoundException) { return null; }
                catch (DirectoryNotFoundException) { return null; }
                incomplete = true;
                if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0) return null;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    // Do not infer a unique installation from only part of a log.
                    if (stream.Length > MaxLogBytes || stream.Length > budget.LogBytes) return null;
                    int length = checked((int)stream.Length);
                    budget.LogBytes -= length;
                    byte[] bytes = new byte[length];
                    int read = 0;
                    while (read < bytes.Length)
                    {
                        int count = stream.Read(bytes, read, bytes.Length - read);
                        if (count == 0) break;
                        read += count;
                    }
                    if (read != length || stream.Length != length) return null;
                    using (var memory = new MemoryStream(bytes))
                    using (var reader = new StreamReader(memory, Encoding.Default, true))
                    {
                        incomplete = false;
                        return reader.ReadToEnd();
                    }
                }
            }
            catch (IOException) { incomplete = true; return null; }
            catch (UnauthorizedAccessException) { incomplete = true; return null; }
            catch (SecurityException) { incomplete = true; return null; }
        }

        private static List<string> ChildDirectories(string path, ScanBudget budget)
        {
            var paths = new List<string>();
            if (budget.Directories <= 0 || !SafeDirectory(path)) return paths;
            try
            {
                foreach (string child in Directory.EnumerateDirectories(path, "*", SearchOption.TopDirectoryOnly))
                {
                    if (budget.Directories-- <= 0) break;
                    if (SafeDirectory(child)) paths.Add(child);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (SecurityException) { }
            return paths;
        }

        private static bool SafeDirectory(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                FileAttributes attributes = File.GetAttributes(path);
                return (attributes & FileAttributes.Directory) != 0 && (attributes & FileAttributes.ReparsePoint) == 0;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (SecurityException) { return false; }
        }

        private static bool SafeGameExecutable(string path)
        {
            try { return File.Exists(Path.Combine(path, "AnarchyOnline.exe")); }
            catch (ArgumentException) { return false; }
            catch (NotSupportedException) { return false; }
        }

        private static DateTime ModifiedUtc(string path)
        {
            try { return Directory.GetLastWriteTimeUtc(path); }
            catch (IOException) { return DateTime.MinValue; }
            catch (UnauthorizedAccessException) { return DateTime.MinValue; }
            catch (SecurityException) { return DateTime.MinValue; }
        }

        private static bool IsBrowser(string name) => string.Equals(name, "Browser", StringComparison.OrdinalIgnoreCase);

        private static bool TryCharacterId(string name, out int id)
        {
            id = 0;
            if (name.Length <= 4 || !name.StartsWith("Char", StringComparison.OrdinalIgnoreCase)) return false;
            string digits = name.Substring(4);
            return digits.All(character => character >= '0' && character <= '9') &&
                int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;
        }

        private static string NormalizeAbsolutePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            string value = path.Trim();
            bool drivePath = value.Length >= 3 && char.IsLetter(value[0]) && value[1] == ':' && (value[2] == '\\' || value[2] == '/');
            if (!drivePath && !value.StartsWith(@"\\", StringComparison.Ordinal)) return null;
            try
            {
                string fullPath = Path.GetFullPath(value);
                string root = Path.GetPathRoot(fullPath);
                return fullPath.Length == root.Length ? fullPath : fullPath.TrimEnd('\\', '/');
            }
            catch (ArgumentException) { return null; }
            catch (NotSupportedException) { return null; }
            catch (IOException) { return null; }
            catch (SecurityException) { return null; }
        }

        private sealed class ScanBudget
        {
            public int Directories = MaxDirectories;
            public int LogBytes = MaxTotalLogBytes;
        }
    }

    internal sealed class PrefsInstallation
    {
        public string GamePath { get; set; }
        public string DisplayName { get; set; }
        public bool IsSteam { get; set; }
    }
}
