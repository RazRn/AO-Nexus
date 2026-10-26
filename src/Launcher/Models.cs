using AOLauncher.Localization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Web.Script.Serialization;

namespace AOLauncher
{
    internal sealed class LauncherSettings
    {
        public int Version { get; set; } = 2;
        public string Language { get; set; } = "auto";
        public bool CheckForUpdatesOnStartup { get; set; }
        public bool HasChosenUpdatePreference { get; set; }
        public bool HasCompletedSetup { get; set; }
        public bool SkipTrayConfirmation { get; set; }
        public bool SkipQuitConfirmation { get; set; }
        public int MaxLogSizeMb { get; set; } = 5;
        public bool KeepDiagnostics { get; set; } = true;
        // The login handler skips drawing the login and character screens (measured ~1.5 s faster per character).
        // Missing in older profiles: they get the faster default as well.
        public bool QuietLaunch { get; set; } = true;
        public bool FastStartup { get; set; } = true;
        public bool MinimizeInactiveClients { get; set; }
        public bool MonitorClientHealth { get; set; }
        public bool CollectClientExitReports { get; set; }
        public int OptionsReviewVersion { get; set; }
        public string GamePath { get; set; } = string.Empty;
        public string Server { get; set; } = "2.24.96.30";
        public int Port { get; set; } = 7500;
        public List<AccountConfig> Accounts { get; set; } = new List<AccountConfig>();
        public List<LaunchPreset> Presets { get; set; } = new List<LaunchPreset>();
        public string SelectedPresetId { get; set; }
        public string DefaultPresetId { get; set; }
        public string DefaultGuiTemplateId { get; set; }
        public List<LoginServerProfile> LoginServers { get; set; } = LoginServerProfile.Defaults();
        public List<string> CollapsedServers { get; set; } = new List<string>();
        public List<string> NewsSourceIds { get; set; } = ServerNews.NormalizeSourceIds(null).ToList();
        public bool ShowServerNews { get; set; } = true;
        public bool ShowServerHealth { get; set; } = true;
        public bool BackgroundHealthChecks { get; set; }
        public bool HealthTrayNotifications { get; set; }
        public Dictionary<string, HealthEndpoints> HealthCheckEndpoints { get; set; } = new Dictionary<string, HealthEndpoints>();
        public List<string> HealthServerIds { get; set; } = new List<string> { "rebirth", "prk", "live", "live-2019" };
    }

    internal sealed class LoginServerProfile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("D");
        public string Name { get; set; }
        public string Server { get; set; }
        public int Port { get; set; }
        public int? MaxAccounts { get; set; }
        public int? MaxCharactersPerAccount { get; set; }
        public int? CharacterSlots { get; set; }
        public override string ToString() => Name;
        internal static List<LoginServerProfile> Defaults()
        {
            var profiles = new List<LoginServerProfile>
            {
                new LoginServerProfile { Id = "rebirth", Name = "AO Rebirth - Live", Server = "2.24.96.30", Port = 7500, MaxAccounts = 0, MaxCharactersPerAccount = 0 },
                new LoginServerProfile { Id = "live", Name = "Funcom AO - Rubi-Ka", Server = "37.18.192.41", Port = 7505, MaxAccounts = 0, MaxCharactersPerAccount = 1 },
                new LoginServerProfile { Id = "live-2019", Name = "Funcom AO - Rubi-Ka 2019", Server = "37.18.192.41", Port = 7506, MaxAccounts = 0, MaxCharactersPerAccount = 1 },
                new LoginServerProfile { Id = "prk", Name = "Project Rubi-Ka - Ithaca", Server = "5.161.243.214", Port = 7000, MaxAccounts = 2, MaxCharactersPerAccount = 1 },
                new LoginServerProfile { Id = "rebirth-local", Name = "AO Rebirth — Local development (localhost)", Server = "127.0.0.1", Port = 7500, MaxAccounts = 0, MaxCharactersPerAccount = 0 }
            };
            foreach (var profile in profiles)
                profile.CharacterSlots = AccountTargets.DefaultCharacterSlots(profile.Server, profile.Port);
            return profiles;
        }
    }

    internal sealed class HotkeyBinding
    {
        // Stable Win32 virtual-key and modifier codes, independent of display language.
        public uint Modifiers { get; set; }
        public int Key { get; set; }
        internal bool IsValid => (Modifiers & ~7u) == 0 && (Modifiers & 3) != 0
            && ((Key >= 65 && Key <= 90) || (Key >= 48 && Key <= 57) || (Key >= 112 && Key <= 135 && Key != 123))
            && !(Key == 115 && Modifiers == 1);
        internal bool SameAs(HotkeyBinding other) => other != null && Key == other.Key && Modifiers == other.Modifiers;
        public override string ToString() => ((Modifiers & 2) != 0 ? "Ctrl + " : "")
            + ((Modifiers & 1) != 0 ? "Alt + " : "") + ((Modifiers & 4) != 0 ? "Shift + " : "")
            + (Key >= 112 && Key <= 135 ? "F" + (Key - 111).ToString() : Key >= 48 && Key <= 90 ? ((char)Key).ToString() : "?");
    }

    internal sealed class LaunchPreset
    {
        public HotkeyBinding StartHotkey { get; set; }
        public HotkeyBinding CampHotkey { get; set; }
        public string Id { get; set; } = Guid.NewGuid().ToString("D");
        public string Name { get; set; }
        public List<PresetCharacter> Characters { get; set; } = new List<PresetCharacter>();
        public override string ToString() => Name;
    }

    internal sealed class PresetCharacter
    {
        public string AccountId { get; set; }
        public int CharacterId { get; set; }
    }

    internal sealed class AccountConfig
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("D");
        public string Label { get; set; }
        public string Username { get; set; }
        public string PrefsPath { get; set; }
        public string GamePath { get; set; }
        public string Server { get; set; }
        public int Port { get; set; }
        public string LegacyCredentialTarget { get; set; }
        // Missing in older profiles: retain their existing remembered-password behavior.
        // The new-account dialog explicitly opts new accounts out by default.
        public bool RememberPassword { get; set; } = true;
        public string ServerProfileId { get; set; }
        public int? CharacterSlots { get; set; }
        public bool Collapsed { get; set; }
        public List<CharacterConfig> Characters { get; set; } = new List<CharacterConfig>();
    }

    internal sealed class CharacterConfig
    {
        public string PendingGuiTemplateId { get; set; }
        public string Name { get; set; }
        public int CharacterId { get; set; }
        public bool Selected { get; set; } = true;
        public bool NamePending { get; set; }
    }

    internal sealed class LaunchRequest
    {
        public string Language { get; set; } = "auto";
        public string GamePath { get; set; }
        public string Server { get; set; }
        public int Port { get; set; }
        public int ReadyTimeoutSeconds { get; set; } = 120;
        public bool QuietLaunch { get; set; }
        public bool FastStartup { get; set; }
        public List<LaunchCharacter> Characters { get; set; } = new List<LaunchCharacter>();
    }

    internal sealed class LaunchCharacter
    {
        public bool CreateCharacter { get; set; }
        public string AccountId { get; set; }
        public string GamePath { get; set; }
        public string Server { get; set; }
        public string ServerName { get; set; }
        public int Port { get; set; }
        public int? MaxAccounts { get; set; }
        public int? MaxCharactersPerAccount { get; set; }
        public string Account { get; set; }
        public string Password { get; set; }
        public int CharacterId { get; set; }
        public string CharacterName { get; set; }
    }

    internal sealed class CharacterReadyEvent
    {
        public bool Created { get; set; }
        public string AccountId { get; set; }
        public int CharacterId { get; set; }
        public string CharacterName { get; set; }
    }

    internal sealed class CreationStartedEvent
    {
        public string AccountId { get; set; }
        public int ProcessId { get; set; }
        public long StartTimeUtcTicks { get; set; }
    }

    internal static class AccountTargets
    {
        internal static void Initialize(LauncherSettings settings, AccountConfig account, bool legacy = false)
        {
            if (account.GamePath == null && account.Server == null && account.Port == 0)
            {
                account.GamePath = settings.GamePath;
                account.Server = settings.Server;
                account.Port = settings.Port;
                if (legacy) account.LegacyCredentialTarget = Endpoint(account);
                account.ServerProfileId = settings.LoginServers?.FirstOrDefault(profile => profile.Server + ":" + profile.Port == Endpoint(account))?.Id;
            }
            ApplyProfile(settings, account);
        }

        internal static void ApplyProfile(LauncherSettings settings, AccountConfig account)
        {
            if (string.IsNullOrEmpty(account.ServerProfileId)) return;
            var profile = settings.LoginServers?.FirstOrDefault(item => item.Id == account.ServerProfileId);
            if (profile == null) { account.ServerProfileId = null; return; }
            account.Server = profile.Server;
            account.Port = profile.Port;
        }

        internal static void ValidateProfiles(IEnumerable<LoginServerProfile> profiles)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var profile in profiles)
            {
                if (profile == null || string.IsNullOrWhiteSpace(profile.Id) || !ids.Add(profile.Id) || string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Any(char.IsControl))
                    throw new ArgumentException(Texts.T("Each login server needs a unique identifier and a name."));
                Validate(new AccountConfig { GamePath = ".", Server = profile.Server, Port = profile.Port }, false);
                // Missing values from older settings use the current endpoint's default.
                // Explicit zero remains an intentional unlimited override.
                if (!profile.MaxAccounts.HasValue) profile.MaxAccounts = ServerAccountPolicy.DefaultLimit(profile.Server, profile.Port);
                ServerAccountPolicy.ValidateLimit(profile.MaxAccounts.Value);
                if (!profile.MaxCharactersPerAccount.HasValue) profile.MaxCharactersPerAccount = ServerAccountPolicy.DefaultCharacterLimit(profile.Server, profile.Port);
                ServerAccountPolicy.ValidateLimit(profile.MaxCharactersPerAccount.Value);
                if (!profile.CharacterSlots.HasValue) profile.CharacterSlots = DefaultCharacterSlots(profile.Server, profile.Port);
                ValidateCharacterSlots(profile.CharacterSlots.Value);
            }
        }

        internal static int DefaultCharacterSlots(string server, int port)
        {
            // Zero means no known launcher limit. Account overrides can reflect purchased slots.
            return NormalizeEndpoint(server + ":" + port) == "2.24.96.30:7500" ? 6 : 0;
        }

        internal static void ValidateCharacterSlots(int slots)
        {
            if (slots < 0 || slots > 1000)
                throw new ArgumentException(Texts.T("Character slots must be a whole number from 0 to 1000. Use 0 for no launcher limit."));
        }

        internal static int CharacterSlotLimit(LauncherSettings settings, AccountConfig account)
            => account.CharacterSlots ?? ServerCharacterSlotLimit(settings, Endpoint(account));

        internal static int ServerCharacterSlotLimit(LauncherSettings settings, string endpoint)
            => LimitForEndpoint(settings, endpoint, profile => profile.CharacterSlots, DefaultCharacterSlots);

        internal static int KnownCharacterCount(LauncherSettings settings, AccountConfig account)
        {
            string username = (account.Username ?? string.Empty).Trim();
            return (settings.Accounts ?? new List<AccountConfig>()).Concat(new[] { account })
                .Where(item => item != null && SameServer(item, account) &&
                    string.Equals((item.Username ?? string.Empty).Trim(), username, StringComparison.OrdinalIgnoreCase))
                .SelectMany(item => item.Characters ?? new List<CharacterConfig>())
                .Where(character => character != null && character.CharacterId > 0)
                .Select(character => character.CharacterId).Distinct().Count();
        }

        internal static bool CanCreateCharacter(LauncherSettings settings, AccountConfig account)
        {
            int limit = CharacterSlotLimit(settings, account);
            return limit == 0 || KnownCharacterCount(settings, account) < limit;
        }

        internal static int AccountLimit(LauncherSettings settings, string endpoint)
            => LimitForEndpoint(settings, endpoint, profile => profile.MaxAccounts, ServerAccountPolicy.DefaultLimit);

        internal static int CharacterLimit(LauncherSettings settings, string endpoint)
            => LimitForEndpoint(settings, endpoint, profile => profile.MaxCharactersPerAccount, ServerAccountPolicy.DefaultCharacterLimit);

        private static int LimitForEndpoint(LauncherSettings settings, string endpoint, Func<LoginServerProfile, int?> configured, Func<string, int, int> defaultLimit)
        {
            string normalized = NormalizeEndpoint(endpoint);
            var matching = (settings.LoginServers ?? new List<LoginServerProfile>())
                .Where(profile => profile != null && string.Equals(
                    Endpoint(new AccountConfig { Server = profile.Server, Port = profile.Port }), normalized, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matching.Count > 0)
                return ServerAccountPolicy.EffectiveLimit(matching.Select(profile => configured(profile) ?? defaultLimit(profile.Server, profile.Port)));

            int separator = normalized.LastIndexOf(':');
            int port;
            return separator > 0 && int.TryParse(normalized.Substring(separator + 1), out port)
                ? defaultLimit(normalized.Substring(0, separator), port)
                : 0;
        }

        internal static void ValidateSelectedAccountLimits(LauncherSettings settings)
        {
            var selected = settings.Accounts.Where(account => account.Characters.Any(character => character.Selected)).ToList();
            foreach (var group in selected.GroupBy(Endpoint, StringComparer.OrdinalIgnoreCase))
            {
                int limit = AccountLimit(settings, group.Key);
                int count = group.Select(account => account.Username.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                if (limit > 0 && count > limit)
                {
                    string serverName = string.Join(" / ", group.Select(account => ServerName(settings, account))
                        .Distinct(StringComparer.OrdinalIgnoreCase));
                    throw new ArgumentException(Texts.F(
                        "{0} ({1}) allows at most {2} selected accounts. You selected {3}. Deselect accounts or change the server's account limit in Settings.",
                        serverName, group.Key, limit, count));
                }

                if (group.Any(IsOfficialLive)) continue;
                ValidateCharacterSelection(settings, group, CharacterLimit(settings, group.Key));
            }

            var live = selected.Where(IsOfficialLive).ToList();
            int liveLimit = ServerAccountPolicy.EffectiveLimit(live.Select(account => Endpoint(account))
                .Distinct(StringComparer.OrdinalIgnoreCase).Select(endpoint => CharacterLimit(settings, endpoint)));
            ValidateCharacterSelection(settings, live, liveLimit);
        }

        internal static string ServerName(LauncherSettings settings, AccountConfig account)
        {
            string endpoint = Endpoint(account);
            var profiles = (settings.LoginServers ?? new List<LoginServerProfile>()).Where(profile => profile != null &&
                string.Equals(Endpoint(new AccountConfig { Server = profile.Server, Port = profile.Port }), endpoint, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(profile.Name)).ToList();
            var linked = profiles.FirstOrDefault(profile => string.Equals(profile.Id, account.ServerProfileId, StringComparison.OrdinalIgnoreCase));
            if (linked != null) return linked.Name;
            string[] names = profiles.Select(profile => profile.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            return names.Length > 0 ? string.Join(" / ", names) : Texts.T("Custom server");
        }

        private static void ValidateCharacterSelection(LauncherSettings settings, IEnumerable<AccountConfig> accounts, int limit)
        {
            if (limit == 0) return;
            foreach (var accountGroup in accounts.GroupBy(account => account.Username.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                int count = accountGroup.SelectMany(account => account.Characters.Where(character => character.Selected)
                    .Select(character => Endpoint(account) + "/" + character.CharacterId)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                if (count <= limit) continue;
                string server = string.Join(", ", accountGroup.Select(account => ServerName(settings, account) + " (" + Endpoint(account) + ")")
                    .Distinct(StringComparer.OrdinalIgnoreCase));
                throw new ArgumentException(Texts.F("Account {0} on {1} allows at most {2} selected characters. You selected {3}.", accountGroup.First().Label, server, limit, count));
            }
        }

        private static string NormalizeEndpoint(string endpoint)
        {
            string value = (endpoint ?? string.Empty).Trim();
            int separator = value.LastIndexOf(':');
            int port;
            if (separator <= 0 || !int.TryParse(value.Substring(separator + 1), out port)) return value;
            IPAddress address;
            string server = value.Substring(0, separator).Trim();
            return (IPAddress.TryParse(server, out address) ? address.ToString() : server) + ":" + port;
        }

        internal static void ChangeProfiles(LauncherSettings settings, List<LoginServerProfile> profiles)
        {
            ValidateProfiles(profiles);
            var proposed = new LauncherSettings { LoginServers = profiles, Accounts = settings.Accounts.Select(account => new AccountConfig {
                Id = account.Id, Label = account.Label, Username = account.Username, GamePath = account.GamePath,
                Server = account.Server, Port = account.Port, ServerProfileId = account.ServerProfileId, CharacterSlots = account.CharacterSlots, Characters = account.Characters
            }).ToList() };
            foreach (var account in proposed.Accounts) ApplyProfile(proposed, account);
            foreach (var group in proposed.Accounts.GroupBy(Endpoint))
            {
                if (group.GroupBy(account => account.Username.Trim(), StringComparer.OrdinalIgnoreCase).Any(accounts => accounts.Count() > 1) ||
                    group.SelectMany(account => account.Characters).GroupBy(character => character.CharacterId).Any(characters => characters.Count() > 1))
                    throw new ArgumentException(Texts.T("This change would put duplicate accounts or character IDs on the same server. No changes were saved."));
            }
            settings.LoginServers = profiles;
            for (int index = 0; index < settings.Accounts.Count; index++)
            {
                settings.Accounts[index].Server = proposed.Accounts[index].Server;
                settings.Accounts[index].Port = proposed.Accounts[index].Port;
                settings.Accounts[index].ServerProfileId = proposed.Accounts[index].ServerProfileId;
            }
        }

        internal static string Endpoint(AccountConfig account)
        {
            IPAddress address;
            string server = account.Server ?? "2.24.96.30";
            return (IPAddress.TryParse(server, out address) ? address.ToString() : server.Trim()) + ":" + (account.Port == 0 ? 7500 : account.Port);
        }

        internal static bool SameServer(AccountConfig left, AccountConfig right) =>
            string.Equals(Endpoint(left), Endpoint(right), StringComparison.OrdinalIgnoreCase);

        internal static bool IsOfficialLive(AccountConfig account) =>
            (Endpoint(account) == "37.18.192.41:7505" || Endpoint(account) == "37.18.192.41:7506");

        internal static bool IsProjectRubika(AccountConfig account) =>
            Endpoint(account) == "5.161.243.214:7000";

        internal static bool GameExists(string path)
        {
            try { return !string.IsNullOrWhiteSpace(path) && File.Exists(Path.Combine(path, "AnarchyOnline.exe")); }
            catch (ArgumentException) { return false; }
            catch (NotSupportedException) { return false; }
        }

        internal static void Validate(AccountConfig account, bool checkFiles)
        {
            IPAddress address;
            if (string.IsNullOrWhiteSpace(account.GamePath))
                throw new ArgumentException(Texts.T("Choose the game folder containing AnarchyOnline.exe."));
            if (!IPAddress.TryParse(account.Server, out address) || address.AddressFamily != AddressFamily.InterNetwork)
                throw new ArgumentException(Texts.T("Enter a valid IPv4 address."));
            if (account.Port < 1 || account.Port > 65535)
                throw new ArgumentException(Texts.T("Port must be between 1 and 65535."));
            if (account.CharacterSlots.HasValue) ValidateCharacterSlots(account.CharacterSlots.Value);
            if (checkFiles && !GameExists(account.GamePath))
                throw new ArgumentException(Texts.T("Choose the game folder containing AnarchyOnline.exe."));
        }
    }

    internal static class SettingsStore
    {
        // Nexus reads only its own profile directory.
        internal static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AONexus");
        public static string FilePath => Path.Combine(DirectoryPath, "AONexus.json");
        internal static string ReadFilePath => GetReadPath(DirectoryPath);

        public static LauncherSettings Read()
        {
            return ReadFromDirectory(DirectoryPath);
        }

        private static string GetReadPath(string directory)
        {
            string current = Path.Combine(directory, "AONexus.json");
            string legacy = Path.Combine(directory, "RebirthLauncher.json");
            // A present current file is authoritative, including when it cannot be read.
            return PathExists(current) || !PathExists(legacy) ? current : legacy;
        }

        private static bool PathExists(string path)
        {
            // File.Exists also hides access errors and directory collisions as "absent".
            // Those must reach startup error handling instead of restoring stale settings.
            try { File.GetAttributes(path); return true; }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
        }

        internal static LauncherSettings ReadFromDirectory(string directory)
        {
            string path = GetReadPath(directory);
            if (!PathExists(path)) return new LauncherSettings();
            return Deserialize(File.ReadAllText(path));
        }

        internal static LauncherSettings Deserialize(string json)
        {
            var value = new JavaScriptSerializer().Deserialize<LauncherSettings>(json);
            if (value == null || (value.Version != 1 && value.Version != 2) || value.Accounts == null)
                throw new InvalidDataException(Texts.T("The settings file has an unsupported format."));
            // Profiles from before the wizard are already configured. Never force
            // existing or imported users through setup, or replace their preferences.
            var fields = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            if (!fields.ContainsKey(nameof(LauncherSettings.HasCompletedSetup))) value.HasCompletedSetup = true;
            if (value.LoginServers == null) value.LoginServers = LoginServerProfile.Defaults();
            value.CollapsedServers = (value.CollapsedServers ?? new List<string>())
                .Where(endpoint => !string.IsNullOrWhiteSpace(endpoint)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            value.NewsSourceIds = ServerNews.NormalizeSourceIds(value.NewsSourceIds).ToList();
            AccountTargets.ValidateProfiles(value.LoginServers);
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var account in value.Accounts)
            {
                if (account == null || string.IsNullOrWhiteSpace(account.Username) || account.Characters == null)
                    throw new InvalidDataException(Texts.T("The settings file contains an invalid account."));
                if (string.IsNullOrWhiteSpace(account.Id) || !ids.Add(account.Id))
                {
                    account.Id = Guid.NewGuid().ToString("D");
                    ids.Add(account.Id);
                }
                if (string.IsNullOrWhiteSpace(account.Label)) account.Label = account.Username;
                AccountTargets.Initialize(value, account, value.Version == 1);
                AccountTargets.Validate(account, false);
                if (account.Characters.Any(c => c == null || c.CharacterId <= 0 || string.IsNullOrWhiteSpace(c.Name)) ||
                    account.Characters.Select(c => c.CharacterId).Distinct().Count() != account.Characters.Count)
                    throw new InvalidDataException(Texts.T("The settings file contains an invalid character."));
            }
            PresetService.Normalize(value);
            value.Language = AOLauncher.Localization.Texts.NormalizePreference(value.Language);
            value.Version = 2;
            return value;
        }

        public static void Save(LauncherSettings settings) => SaveToDirectory(settings, DirectoryPath);

        internal static void SaveToDirectory(LauncherSettings settings, string directory)
        {
            string path = Path.Combine(directory, "AONexus.json");
            Directory.CreateDirectory(directory);
            string tempFile = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                if (File.Exists(path) && !File.Exists(path + ".before-account-targets.bak"))
                {
                    var previous = new JavaScriptSerializer().Deserialize<LauncherSettings>(File.ReadAllText(path));
                    if (previous?.Version == 1) File.Copy(path, path + ".before-account-targets.bak");
                }
                File.WriteAllText(tempFile, new JavaScriptSerializer().Serialize(settings));
                if (File.Exists(path)) File.Replace(tempFile, path, null);
                else File.Move(tempFile, path);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }
    }
}
