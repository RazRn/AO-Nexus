using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AOLauncher
{
    /// <summary>
    /// Local, anonymised diagnostics. Every entry is redacted before it reaches the disk and
    /// nothing is ever uploaded automatically; the user decides whether to share a report.
    /// </summary>
    internal static class DiagnosticJournal
    {
        internal const int LimitBytes = 2 * 1024 * 1024;
        private static readonly object Sync = new object();
        private static bool enabled;
        private static string root;

        internal static string DefaultRoot => Path.Combine(SettingsStore.DirectoryPath, "Diagnostics");
        internal static string Root { get { lock (Sync) return root ?? DefaultRoot; } }
        internal static string JournalPath => Path.Combine(Root, "diagnostics.log");
        internal static string CrashRoot => Path.Combine(Root, "Crashes");
        internal static bool Enabled { get { lock (Sync) return enabled; } }

        internal static void Configure(bool enable, string directory = null)
        {
            lock (Sync)
            {
                enabled = enable;
                if (directory != null) root = directory;
            }
        }

        internal static void Record(string area, string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            string entry;
            try { entry = Format(DateTime.UtcNow, area, DiagnosticRedactor.Current.Redact(message)); }
            catch (Exception) { return; } // Diagnostics must never become a new source of failures.
            lock (Sync)
            {
                if (!enabled) return;
                try
                {
                    Directory.CreateDirectory(root ?? DefaultRoot);
                    string path = Path.Combine(root ?? DefaultRoot, "diagnostics.log");
                    // Trim to three quarters when full, so later appends do not rewrite the whole file each time.
                    var file = new FileInfo(path);
                    if (file.Exists && file.Length + Encoding.UTF8.GetByteCount(entry) > LimitBytes) LogRetention.AppendFile(path, "", LimitBytes * 3 / 4);
                    LogRetention.AppendFile(path, entry, LimitBytes);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                catch (System.Security.SecurityException) { }
            }
        }

        internal static void Exception(string area, string context, Exception error)
        {
            if (error == null) return;
            Record(area, (string.IsNullOrEmpty(context) ? "" : context + ": ") + DiagnosticRedactor.Describe(error));
        }

        internal static string Format(DateTime utc, string area, string message)
        {
            string text = (message ?? "").Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n');
            // Indent continuation lines (stack traces) so each entry stays one logical record.
            text = text.Replace("\n", Environment.NewLine + "    ");
            return utc.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + "Z [" + (area ?? "app") + "] " + text + Environment.NewLine;
        }

        internal static string ReadTail(int bytes)
        {
            lock (Sync)
            {
                if (!enabled) return ""; // Also keeps synthetic UI previews free of real data.
                try
                {
                    string path = Path.Combine(root ?? DefaultRoot, "diagnostics.log");
                    if (!File.Exists(path)) return "";
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                        return LogRetention.Tail(reader.ReadToEnd(), bytes);
                }
                catch (IOException) { return ""; }
                catch (UnauthorizedAccessException) { return ""; }
            }
        }

        /// <summary>Deletes the journal, crash reports and the anonymisation key.</summary>
        internal static bool Clear()
        {
            bool result = ClearFiles();
            DiagnosticRedactor.ResetKey();
            return result;
        }

        private static bool ClearFiles()
        {
            lock (Sync)
            {
                bool cleared = true;
                string directory = root ?? DefaultRoot;
                foreach (string file in new[] { Path.Combine(directory, "diagnostics.log"), Path.Combine(directory, "anonymisation.key") })
                    cleared &= TryDelete(file);
                string crashes = Path.Combine(directory, "Crashes");
                try
                {
                    if (Directory.Exists(crashes))
                        foreach (string file in Directory.GetFiles(crashes, "*.txt", SearchOption.TopDirectoryOnly)) cleared &= TryDelete(file);
                }
                catch (IOException) { cleared = false; }
                catch (UnauthorizedAccessException) { cleared = false; }
                return cleared;
            }
        }

        private static bool TryDelete(string file)
        {
            try { if (File.Exists(file)) File.Delete(file); return true; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }

    /// <summary>
    /// Replaces everything account-specific with stable pseudonyms (for example Char-3fa2c1),
    /// so a report still shows which character failed without revealing who it is.
    /// </summary>
    internal sealed class DiagnosticRedactor
    {
        private static DiagnosticRedactor current = new DiagnosticRedactor(null, null);
        private static readonly object KeySync = new object();
        private static byte[] key;
        private static readonly Regex EmailPattern = new Regex(@"[\w.+-]+@[\w-]+(\.[\w-]+)+", RegexOptions.CultureInvariant);
        // Character IDs also appear glued to text (Prefs folders are named Char123456789) or before
        // punctuation ("ID 123456789..."); only digits and decimal fractions such as 1.123456 are excluded.
        private static readonly Regex NumberPattern = new Regex(@"(?<prefix>Char)?(?<!\d|\d\.)(?<id>\d{3,10})(?!\d|\.\d)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex SourcePathPattern = new Regex(@" in [^\r\n]*[\\/]([^\\/\r\n]+\.cs):", RegexOptions.CultureInvariant);

        private readonly Dictionary<string, string> paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> words = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> numbers = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Regex wordPattern;

        internal static DiagnosticRedactor Current => Volatile.Read(ref current);

        /// <summary>Rebuild from the current settings. Earlier terms are kept, so renamed or removed entries stay hidden.</summary>
        internal static void Update(LauncherSettings settings)
        {
            try { Volatile.Write(ref current, new DiagnosticRedactor(settings, Current)); }
            catch (Exception) { } // Keep the previous redactor if the settings are unusual.
        }

        internal DiagnosticRedactor(LauncherSettings settings, DiagnosticRedactor previous)
        {
            if (previous != null)
            {
                foreach (var pair in previous.paths) paths[pair.Key] = pair.Value;
                foreach (var pair in previous.words) words[pair.Key] = pair.Value;
                foreach (var pair in previous.numbers) numbers[pair.Key] = pair.Value;
            }
            AddEnvironment();
            if (settings != null) AddSettings(settings);
            var alternatives = words.Keys.Where(word => word.Length > 0).OrderByDescending(word => word.Length).Select(Regex.Escape).ToArray();
            wordPattern = alternatives.Length == 0 ? null : new Regex(@"(?<![\p{L}\p{N}_])(?:" + string.Join("|", alternatives) + @")(?![\p{L}\p{N}_])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private void AddEnvironment()
        {
            // Longer paths first: OneDrive and LocalAppData live inside the profile folder.
            AddPath(Environment.GetEnvironmentVariable("OneDriveCommercial"), "%OneDrive%");
            AddPath(Environment.GetEnvironmentVariable("OneDriveConsumer"), "%OneDrive%");
            AddPath(Environment.GetEnvironmentVariable("OneDrive"), "%OneDrive%");
            AddPath(SafeFolder(Environment.SpecialFolder.LocalApplicationData), "%LOCALAPPDATA%");
            AddPath(SafeFolder(Environment.SpecialFolder.ApplicationData), "%APPDATA%");
            AddPath(SafeFolder(Environment.SpecialFolder.UserProfile), "%USERPROFILE%");
            AddWord(SafeValue(() => Environment.UserName), "<windows-user>");
            AddWord(SafeValue(() => Environment.MachineName), "<pc-name>");
            string domain = SafeValue(() => Environment.UserDomainName);
            if (!string.Equals(domain, SafeValue(() => Environment.MachineName), StringComparison.OrdinalIgnoreCase)) AddWord(domain, "<windows-domain>");
        }

        private void AddSettings(LauncherSettings settings)
        {
            var publicServers = new HashSet<string>(LoginServerProfile.Defaults().Select(profile => profile.Server), StringComparer.OrdinalIgnoreCase);
            foreach (var profile in settings.LoginServers ?? new List<LoginServerProfile>())
            {
                if (profile == null) continue;
                if (!string.IsNullOrWhiteSpace(profile.Server) && !publicServers.Contains(profile.Server.Trim()))
                    AddWord(profile.Server.Trim(), Token("Server", profile.Server.Trim()));
                if (!LoginServerProfile.Defaults().Any(item => string.Equals(item.Name, profile.Name, StringComparison.Ordinal)))
                    AddWord(profile.Name, Token("ServerName", profile.Id));
            }
            foreach (var preset in settings.Presets ?? new List<LaunchPreset>())
                if (preset != null) AddWord(preset.Name, Token("Team", preset.Id));
            foreach (var account in settings.Accounts ?? new List<AccountConfig>())
            {
                if (account == null) continue;
                string username = (account.Username ?? "").Trim();
                string login = Token("Login", username.ToLowerInvariant());
                AddWord(username, login);
                AddWord(account.Id, Token("Account", account.Id));
                AddWord(account.Label, string.Equals((account.Label ?? "").Trim(), username, StringComparison.OrdinalIgnoreCase) ? login : Token("Account", account.Id));
                AddPath(account.GamePath, Token("GameFolder", Normalize(account.GamePath)));
                AddPath(account.PrefsPath, Token("PrefsFolder", Normalize(account.PrefsPath)));
                if (!string.IsNullOrWhiteSpace(account.Server) && !publicServers.Contains(account.Server.Trim()))
                    AddWord(account.Server.Trim(), Token("Server", account.Server.Trim()));
                foreach (var character in account.Characters ?? new List<CharacterConfig>())
                {
                    if (character == null || character.CharacterId <= 0) continue;
                    string token = Token("Char", character.CharacterId.ToString(CultureInfo.InvariantCulture));
                    numbers[character.CharacterId.ToString(CultureInfo.InvariantCulture)] = token;
                    AddWord(character.Name, token);
                    // Names such as "Aster · Doctor" also appear without the label suffix.
                    string bare = (character.Name ?? "").Split('·')[0].Trim();
                    if (bare.Length >= 2) AddWord(bare, token);
                }
            }
            if (!string.IsNullOrWhiteSpace(settings.GamePath)) AddPath(settings.GamePath, Token("GameFolder", Normalize(settings.GamePath)));
        }

        internal string Redact(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            string result = SourcePathPattern.Replace(text, match => " in " + match.Groups[1].Value + ":");
            foreach (var pair in paths.OrderByDescending(item => item.Key.Length))
                result = ReplaceIgnoreCase(result, pair.Key, pair.Value);
            if (wordPattern != null)
                result = wordPattern.Replace(result, match => words.TryGetValue(match.Value, out var replacement) ? replacement : "<redacted>");
            result = EmailPattern.Replace(result, "<email>");
            if (numbers.Count > 0)
                result = NumberPattern.Replace(result, match => numbers.TryGetValue(match.Groups["id"].Value, out var replacement) ? replacement : match.Value);
            return result;
        }

        internal static string Describe(Exception error)
        {
            var text = new StringBuilder();
            int depth = 0;
            for (var item = error; item != null && depth < 8; item = item.InnerException, depth++)
            {
                if (depth > 0) text.AppendLine().Append("--- inner: ");
                text.Append(item.GetType().FullName).Append(" (HResult 0x").Append(item.HResult.ToString("X8", CultureInfo.InvariantCulture)).Append(')');
                if (item is System.ComponentModel.Win32Exception win32) text.Append(" Win32 error ").Append(win32.NativeErrorCode.ToString(CultureInfo.InvariantCulture));
                text.Append(": ").Append(item.Message);
                if (!string.IsNullOrEmpty(item.StackTrace)) text.AppendLine().Append(item.StackTrace.TrimEnd());
                if (item is AggregateException aggregate && aggregate.InnerExceptions.Count > 1)
                    foreach (var inner in aggregate.InnerExceptions.Skip(1).Take(4))
                        text.AppendLine().Append("--- also: ").Append(inner.GetType().FullName).Append(": ").Append(inner.Message);
            }
            return text.ToString();
        }

        internal static string Token(string kind, string value)
        {
            byte[] secret = Key();
            using (var hmac = new HMACSHA256(secret))
            {
                byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(kind + "\0" + (value ?? "")));
                return kind + "-" + BitConverter.ToString(hash, 0, 3).Replace("-", "").ToLowerInvariant();
            }
        }

        internal static void ResetKey() { lock (KeySync) key = null; }

        private static byte[] Key()
        {
            // Read journal state before taking the key lock; Clear() takes these locks in the other order.
            string directory = DiagnosticJournal.Root;
            bool persist = DiagnosticJournal.Enabled;
            lock (KeySync)
            {
                if (key != null) return key;
                // A per-installation secret keeps pseudonyms stable across sessions without
                // making them reversible by anyone who only sees a shared report.
                string file = Path.Combine(directory, "anonymisation.key");
                try
                {
                    if (File.Exists(file))
                    {
                        byte[] stored = File.ReadAllBytes(file);
                        if (stored.Length == 32) return key = stored;
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                var fresh = new byte[32];
                using (var random = RandomNumberGenerator.Create()) random.GetBytes(fresh);
                if (persist)
                {
                    try { Directory.CreateDirectory(directory); File.WriteAllBytes(file, fresh); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                return key = fresh;
            }
        }

        private void AddWord(string value, string replacement)
        {
            string term = (value ?? "").Trim();
            if (term.Length < 2 || term.Length > 256) return;
            words[term] = replacement;
        }

        private void AddPath(string value, string replacement)
        {
            string path = Normalize(value);
            if (path.Length < 4) return;
            paths[path] = replacement;
        }

        private static string Normalize(string path)
        {
            string value = (path ?? "").Trim().TrimEnd('\\', '/');
            return value;
        }

        private static string ReplaceIgnoreCase(string text, string find, string replacement)
        {
            if (string.IsNullOrEmpty(find)) return text;
            var result = new StringBuilder();
            int start = 0, index;
            while ((index = text.IndexOf(find, start, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                result.Append(text, start, index - start).Append(replacement);
                start = index + find.Length;
            }
            return start == 0 ? text : result.Append(text, start, text.Length - start).ToString();
        }

        private static string SafeFolder(Environment.SpecialFolder folder)
        {
            try { return Environment.GetFolderPath(folder); }
            catch (Exception) { return null; }
        }

        private static string SafeValue(Func<string> read)
        {
            try { return read(); }
            catch (Exception) { return null; }
        }
    }

    /// <summary>Captures unhandled exceptions as local, anonymised crash reports.</summary>
    internal static class CrashReporter
    {
        internal const int MaxReports = 20;
        private static int showingDialog;
        internal static Func<LauncherSettings> SettingsProvider;
        internal static bool Quiet;

        internal static string PendingMarker => Path.Combine(DiagnosticJournal.CrashRoot, "pending.txt");

        internal static void Install()
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (sender, e) => OnInterfaceException(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (sender, e) => Save(e.ExceptionObject as Exception, e.IsTerminating ? "fatal" : "background");
            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                DiagnosticJournal.Exception("task", "Unobserved background task error", e.Exception);
                e.SetObserved();
            };
        }

        private static void OnInterfaceException(Exception error)
        {
            string saved = Save(error, "interface");
            if (Quiet || Interlocked.Exchange(ref showingDialog, 1) == 1) return;
            try
            {
                // A plain system dialog: the themed controls may be what failed.
                string message = SidebarText.L(
                    "AO Nexus ran into an unexpected error. " + (saved != null ? "An anonymised report was saved on this PC.\r\n\r\nYou can share it with Settings → General → Report a problem…" : "The details could not be saved.") + "\r\n\r\nContinue using AO Nexus? Choose No to close it. Game windows stay open.",
                    "AO Nexus fik en uventet fejl. " + (saved != null ? "En anonymiseret rapport er gemt på denne pc.\r\n\r\nDu kan dele den via Indstillinger → Generelt → Rapportér et problem…" : "Detaljerne kunne ikke gemmes.") + "\r\n\r\nVil du fortsætte med at bruge AO Nexus? Vælg Nej for at lukke. Spilvinduer forbliver åbne.");
                if (MessageBox.Show(message, "AO Nexus", MessageBoxButtons.YesNo, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1) == DialogResult.No)
                    Application.Exit();
            }
            catch (Exception) { }
            finally { Interlocked.Exchange(ref showingDialog, 0); }
        }

        /// <summary>Writes a crash report and returns its file name, or null when nothing was saved.</summary>
        internal static string Save(Exception error, string kind)
        {
            if (error == null || !DiagnosticJournal.Enabled) return null;
            try
            {
                DiagnosticJournal.Exception("crash", "Unhandled " + kind + " exception", error);
                LauncherSettings settings = null;
                try { settings = SettingsProvider?.Invoke(); } catch (Exception) { }
                var report = new StringBuilder();
                report.AppendLine("AO Nexus crash report (" + kind + ")");
                report.AppendLine("Time (UTC): " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                report.AppendLine();
                report.AppendLine(DiagnosticRedactor.Describe(error));
                report.AppendLine();
                report.AppendLine(DiagnosticSnapshot.Build(settings, false));
                report.AppendLine("Recent diagnostics:");
                report.AppendLine(DiagnosticJournal.ReadTail(12 * 1024));
                string text = DiagnosticRedactor.Current.Redact(report.ToString());
                Directory.CreateDirectory(DiagnosticJournal.CrashRoot);
                string name = "crash-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + ".txt";
                File.WriteAllText(Path.Combine(DiagnosticJournal.CrashRoot, name), text, new UTF8Encoding(false));
                File.WriteAllText(PendingMarker, name, new UTF8Encoding(false));
                Prune();
                return name;
            }
            catch (Exception) { return null; }
        }

        internal static IReadOnlyList<string> Reports()
        {
            try
            {
                if (!DiagnosticJournal.Enabled || !Directory.Exists(DiagnosticJournal.CrashRoot)) return new string[0];
                return Directory.GetFiles(DiagnosticJournal.CrashRoot, "crash-*.txt", SearchOption.TopDirectoryOnly)
                    .OrderByDescending(Path.GetFileName, StringComparer.Ordinal).ToArray();
            }
            catch (IOException) { return new string[0]; }
            catch (UnauthorizedAccessException) { return new string[0]; }
        }

        internal static bool HasPending
        {
            get
            {
                try { return DiagnosticJournal.Enabled && File.Exists(PendingMarker); }
                catch (Exception) { return false; }
            }
        }

        internal static void AcknowledgePending()
        {
            try { if (File.Exists(PendingMarker)) File.Delete(PendingMarker); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static void Prune()
        {
            foreach (string file in Reports().Skip(MaxReports))
            {
                try { File.Delete(file); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    /// <summary>Environment and configuration facts that help explain a problem. Contains counts and categories, not identities.</summary>
    internal static class DiagnosticSnapshot
    {
        [StructLayout(LayoutKind.Sequential)]
        private sealed class MemoryStatus
        {
            public uint Length = (uint)Marshal.SizeOf(typeof(MemoryStatus));
            public uint MemoryLoad;
            public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatus buffer);

        internal static string Build(LauncherSettings settings, bool detailed)
        {
            var text = new StringBuilder();
            text.AppendLine("Environment:");
            Line(text, "AO Nexus", Safe(() => AppUpdates.CurrentVersion.ToString(3)) + " (" + (Environment.Is64BitProcess ? "64-bit" : "32-bit") + " process)");
            Line(text, "Nexus location", Safe(() => LocationCategory(Application.StartupPath)));
            Line(text, "Windows", Safe(WindowsVersion) + (Environment.Is64BitOperatingSystem ? ", 64-bit" : ", 32-bit"));
            Line(text, ".NET Framework", Safe(FrameworkVersion));
            Line(text, "Running as administrator", Safe(() => IsElevated() ? "yes" : "no"));
            Line(text, "Language", Safe(() => "Nexus " + Localization.Texts.CurrentLanguage + ", Windows " + CultureInfo.CurrentUICulture.Name));
            Line(text, "Display", Safe(Display));
            Line(text, "Processors / memory", Safe(Memory));
            Line(text, "Running AnarchyOnline.exe clients", Safe(() => CountProcesses("AnarchyOnline").ToString(CultureInfo.InvariantCulture)));
            if (detailed) Line(text, "Antivirus (Windows Security Center)", Safe(Antivirus));
            if (settings == null) return text.ToString();
            try { AppendConfiguration(text, settings); }
            catch (Exception error) { text.AppendLine("- Configuration: unavailable (" + error.GetType().Name + ")"); }
            return text.ToString();
        }

        private static void AppendConfiguration(StringBuilder text, LauncherSettings settings)
        {
            text.AppendLine().AppendLine("Configuration (anonymised):");
            var accounts = (settings.Accounts ?? new List<AccountConfig>()).Where(item => item != null).ToList();
            int characters = accounts.Sum(account => (account.Characters ?? new List<CharacterConfig>()).Count);
            int selected = accounts.Sum(account => (account.Characters ?? new List<CharacterConfig>()).Count(character => character != null && character.Selected));
            Line(text, "Accounts / characters / selected", accounts.Count + " / " + characters + " / " + selected);
            Line(text, "Accounts with remembered password", accounts.Count(account => account.RememberPassword).ToString(CultureInfo.InvariantCulture));
            Line(text, "Team presets", (settings.Presets?.Count ?? 0) + (string.IsNullOrEmpty(settings.DefaultPresetId) ? "" : ", startup default set")
                + ", " + (settings.Presets ?? new List<LaunchPreset>()).Count(preset => preset?.StartHotkey != null || preset?.CampHotkey != null) + " with shortcuts");
            foreach (var group in accounts.GroupBy(AccountTargets.Endpoint, StringComparer.OrdinalIgnoreCase))
            {
                var account = group.First();
                string endpoint = group.Key;
                string name = Safe(() => AccountTargets.ServerName(settings, account));
                int logins = group.Select(item => (item.Username ?? "").Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                Line(text, "Server " + name + " (" + endpoint + ")",
                    group.Count() + " accounts (" + logins + " distinct logins), "
                    + group.Sum(item => item.Characters?.Count ?? 0) + " characters, "
                    + group.Sum(item => item.Characters?.Count(character => character.Selected) ?? 0) + " selected; limits: accounts "
                    + Limit(AccountTargets.AccountLimit(settings, endpoint)) + ", characters per account "
                    + Limit(AccountTargets.CharacterLimit(settings, endpoint)) + ", slots " + Limit(AccountTargets.ServerCharacterSlotLimit(settings, endpoint)));
            }
            int folder = 0;
            foreach (var installation in accounts.Where(account => !string.IsNullOrWhiteSpace(account.GamePath))
                .GroupBy(account => account.GamePath.Trim().TrimEnd('\\', '/'), StringComparer.OrdinalIgnoreCase))
            {
                folder++;
                Line(text, "Game installation " + folder, installation.Key + " · " + Safe(() => Installation(installation.Key))
                    + " · used by " + installation.Count() + " account(s) on " + installation.Select(AccountTargets.Endpoint).Distinct(StringComparer.OrdinalIgnoreCase).Count() + " server(s)");
            }
            Line(text, "Sidebar", "news " + OnOff(settings.ShowServerNews) + ", health " + OnOff(settings.ShowServerHealth)
                + ", background checks " + OnOff(settings.BackgroundHealthChecks) + ", tray notifications " + OnOff(settings.HealthTrayNotifications));
            Line(text, "Updates", "startup check " + OnOff(settings.CheckForUpdatesOnStartup));
            Line(text, "Game GUI", (string.IsNullOrEmpty(settings.DefaultGuiTemplateId) ? "no default template" : "default template set") + ", "
                + accounts.Sum(account => account.Characters?.Count(character => !string.IsNullOrEmpty(character.PendingGuiTemplateId)) ?? 0) + " pending template(s)");
        }

        private static void Line(StringBuilder text, string label, string value) => text.Append("- ").Append(label).Append(": ").AppendLine(value);
        private static string OnOff(bool value) => value ? "on" : "off";
        private static string Limit(int value) => value == 0 ? "unlimited" : value.ToString(CultureInfo.InvariantCulture);

        private static string Safe(Func<string> read)
        {
            try { return read() ?? "unknown"; }
            catch (Exception error) { return "unavailable (" + error.GetType().Name + ")"; }
        }

        internal static string LocationCategory(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "unknown";
            string full = Path.GetFullPath(path);
            var categories = new List<string>();
            if (full.StartsWith(@"\\", StringComparison.Ordinal)) categories.Add("network share");
            foreach (var variable in new[] { "OneDrive", "OneDriveCommercial", "OneDriveConsumer" })
            {
                string root = Environment.GetEnvironmentVariable(variable);
                if (!string.IsNullOrEmpty(root) && full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) { categories.Add("OneDrive"); break; }
            }
            foreach (var folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
            {
                string root = Environment.GetFolderPath(folder);
                if (!string.IsNullOrEmpty(root) && full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) { categories.Add("Program Files (UAC-protected)"); break; }
            }
            if (full.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)) categories.Add("temporary folder (still inside a ZIP?)");
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (categories.Count == 0 && !string.IsNullOrEmpty(profile) && full.StartsWith(profile, StringComparison.OrdinalIgnoreCase))
                categories.Add(full.IndexOf(@"\Downloads", StringComparison.OrdinalIgnoreCase) >= 0 ? "user profile (Downloads)" : "user profile");
            if (categories.Count == 0) categories.Add("drive " + Path.GetPathRoot(full));
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(full));
                if (drive.IsReady) categories.Add(drive.DriveType + " " + drive.DriveFormat + ", " + (drive.AvailableFreeSpace / (1024L * 1024 * 1024)) + " GB free");
            }
            catch (Exception) { }
            return string.Join(", ", categories);
        }

        private static string Installation(string folder)
        {
            string exe = Path.Combine(folder, "AnarchyOnline.exe");
            if (!Directory.Exists(folder)) return "folder missing";
            if (!File.Exists(exe)) return "AnarchyOnline.exe missing; " + LocationCategory(folder);
            var info = FileVersionInfo.GetVersionInfo(exe);
            var file = new FileInfo(exe);
            return "client " + (info.FileVersion ?? "?") + " / " + (info.ProductVersion ?? "?") + ", " + (file.Length / 1024) + " KB, modified "
                + file.LastWriteTimeUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "; Prefs folder " + (Directory.Exists(Path.Combine(folder, "Prefs")) ? "present" : "missing")
                + "; " + LocationCategory(folder);
        }

        private static string WindowsVersion()
        {
            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
            {
                if (key == null) return Environment.OSVersion.VersionString;
                string product = key.GetValue("ProductName") as string ?? "Windows";
                string build = key.GetValue("CurrentBuild") as string ?? "";
                int buildNumber;
                if (int.TryParse(build, out buildNumber) && buildNumber >= 22000) product = product.Replace("Windows 10", "Windows 11");
                string display = key.GetValue("DisplayVersion") as string ?? key.GetValue("ReleaseId") as string ?? "";
                object revision = key.GetValue("UBR");
                return product + (display.Length > 0 ? " " + display : "") + " (build " + build + (revision != null ? "." + revision : "") + ")";
            }
        }

        private static string FrameworkVersion()
        {
            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
            {
                int release = key?.GetValue("Release") is int value ? value : 0;
                string name = release >= 533320 ? "4.8.1" : release >= 528040 ? "4.8" : release >= 461808 ? "4.7.2" : release > 0 ? "older than 4.7.2" : "unknown";
                return name + " (release " + release.ToString(CultureInfo.InvariantCulture) + ")";
            }
        }

        private static bool IsElevated()
        {
            using (var identity = WindowsIdentity.GetCurrent())
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }

        private static string Display()
        {
            var screens = Screen.AllScreens;
            using (var graphics = Graphics())
                return screens.Length + " screen(s), primary " + Screen.PrimaryScreen.Bounds.Width + "×" + Screen.PrimaryScreen.Bounds.Height
                    + ", scaling " + Math.Round(graphics.DpiX / 96f * 100) + "%";
        }

        private static System.Drawing.Graphics Graphics() => System.Drawing.Graphics.FromHwnd(IntPtr.Zero);

        private static string Memory()
        {
            var status = new MemoryStatus();
            string memory = GlobalMemoryStatusEx(status)
                ? (status.TotalPhysical / (1024 * 1024 * 1024.0)).ToString("0.0", CultureInfo.InvariantCulture) + " GB RAM, "
                    + (status.AvailablePhysical / (1024 * 1024 * 1024.0)).ToString("0.0", CultureInfo.InvariantCulture) + " GB available"
                : "memory unknown";
            return Environment.ProcessorCount + " logical processors, " + memory;
        }

        internal static int CountProcesses(string name)
        {
            var processes = Process.GetProcessesByName(name);
            foreach (var process in processes) process.Dispose();
            return processes.Length;
        }

        private static string Antivirus()
        {
            // Security Center can be slow or missing (Windows Server); never block the report on it.
            var query = Task.Run(() =>
            {
                var names = new List<string>();
                using (var searcher = new ManagementObjectSearcher(@"root\SecurityCenter2", "SELECT displayName FROM AntiVirusProduct"))
                using (var results = searcher.Get())
                    foreach (ManagementBaseObject item in results)
                        using (item) names.Add(Convert.ToString(item["displayName"], CultureInfo.InvariantCulture));
                return names;
            });
            if (!query.Wait(TimeSpan.FromSeconds(3))) return "not available (timed out)";
            return query.Result.Count == 0 ? "none reported" : string.Join(", ", query.Result.Distinct(StringComparer.OrdinalIgnoreCase));
        }
    }

    internal enum ProblemArea { Login, GameGui, Presets, NewsHealth, Updates, Crash, Other }

    /// <summary>Builds the anonymised text a user can review and share.</summary>
    internal static class DiagnosticReport
    {
        internal const string IssueUrl = AppUpdates.Repository + "/issues/new";
        internal const int MaxUrlLength = 7500;
        internal const int JournalBytes = 48 * 1024;

        internal static string AreaName(ProblemArea area)
        {
            switch (area)
            {
                case ProblemArea.Login: return "Login and characters";
                case ProblemArea.GameGui: return "Game GUI and skins";
                case ProblemArea.Presets: return "Team presets and shortcuts";
                case ProblemArea.NewsHealth: return "News and server health";
                case ProblemArea.Updates: return "Updates";
                case ProblemArea.Crash: return "Crash or freeze";
                default: return "Other";
            }
        }

        internal static string Compose(ProblemArea area, string summary, string description, string environment, bool includeJournal, bool includeCrashes)
        {
            var text = new StringBuilder();
            text.AppendLine("### Area").AppendLine(AreaName(area)).AppendLine();
            text.AppendLine("### Summary").AppendLine(string.IsNullOrWhiteSpace(summary) ? "_(not given)_" : summary.Trim()).AppendLine();
            text.AppendLine("### What happened").AppendLine(string.IsNullOrWhiteSpace(description) ? "_(not given)_" : description.Trim()).AppendLine();
            text.AppendLine("### Environment").AppendLine("```text").AppendLine((environment ?? "").Trim()).AppendLine("```").AppendLine();
            if (includeCrashes)
            {
                foreach (string file in CrashReporter.Reports().Take(3))
                {
                    string content;
                    try { content = File.ReadAllText(file); }
                    catch (Exception) { continue; }
                    // The environment (and, when included, the diagnostics log) is already in the report: keep the crash itself.
                    int environmentStart = content.IndexOf("Environment:", StringComparison.Ordinal);
                    int recent = content.IndexOf("Recent diagnostics:", StringComparison.Ordinal);
                    if (environmentStart > 0)
                        content = content.Substring(0, environmentStart).TrimEnd() + (!includeJournal && recent > environmentStart ? Environment.NewLine + Environment.NewLine + content.Substring(recent) : "");
                    text.AppendLine("<details><summary>" + Path.GetFileName(file) + "</summary>").AppendLine()
                        .AppendLine("```text").AppendLine(LogRetention.Tail(content, 12 * 1024).Trim()).AppendLine("```").AppendLine("</details>").AppendLine();
                }
            }
            if (includeJournal)
            {
                string journal = DiagnosticJournal.ReadTail(JournalBytes).Trim();
                text.AppendLine("### Diagnostics log (anonymised, most recent last)").AppendLine("```text")
                    .AppendLine(journal.Length == 0 ? "(empty)" : journal).AppendLine("```");
            }
            // A final pass: terms added since an entry was written are hidden as well.
            return DiagnosticRedactor.Current.Redact(text.ToString());
        }

        internal static string Title(ProblemArea area, string summary)
        {
            string text = string.IsNullOrWhiteSpace(summary) ? AreaName(area) : summary.Trim();
            text = DiagnosticRedactor.Current.Redact(text.Replace('\r', ' ').Replace('\n', ' '));
            if (text.Length > 120) text = text.Substring(0, 117) + "…";
            return "[" + AreaName(area) + "] " + text;
        }

        /// <summary>Returns the prefilled issue URL; <paramref name="truncated"/> is true when the body did not fit.</summary>
        internal static string BuildIssueUrl(ProblemArea area, string summary, string report, out bool truncated)
        {
            string prefix = IssueUrl + "?template=bug_report.yml&title=" + Uri.EscapeDataString(Title(area, summary))
                + "&version=" + Uri.EscapeDataString(AppUpdates.CurrentVersion.ToString(3)) + "&diagnostics=";
            string body = report ?? "";
            truncated = false;
            string url = prefix + Uri.EscapeDataString(body);
            if (url.Length <= MaxUrlLength) return url;
            truncated = true;
            int low = 0, high = body.Length;
            while (low < high)
            {
                int middle = (low + high + 1) / 2;
                if ((prefix + Uri.EscapeDataString(Shorten(body, middle))).Length <= MaxUrlLength) low = middle;
                else high = middle - 1;
            }
            return prefix + Uri.EscapeDataString(Shorten(body, low));
        }

        private static string Shorten(string body, int length)
        {
            const string notice = "\n\n…(shortened to fit the link. The full report is on your clipboard: paste it here, or attach the saved report file.)";
            // Avoid ending inside a surrogate pair, and close an open code block.
            if (length > 0 && char.IsHighSurrogate(body[length - 1])) length--;
            string shortened = body.Substring(0, length);
            if (Regex.Matches(shortened, "```").Count % 2 == 1) shortened += "\n```";
            return shortened + notice;
        }
    }
}
