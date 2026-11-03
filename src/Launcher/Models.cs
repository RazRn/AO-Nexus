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
        public bool SkipLoginConfirmation { get; set; }
        public bool SkipTeamLogoutConfirmation { get; set; }
        public int MaxLogSizeMb { get; set; } = 5;
        public bool WrapActivityLog { get; set; }
        public bool KeepDiagnostics { get; set; } = true;
        // The login handler skips drawing the login and character screens (measured ~1.5 s faster per character).
        // Missing in older profiles: they get the faster default as well.
        public bool QuietLaunch { get; set; } = true;
        public bool FastStartup { get; set; } = true;
        public bool ExperimentalAovk { get; set; }
        public bool ExperimentalTeamAutomation { get; set; }
        public bool ExperimentalNearbyCombat { get; set; }
        public bool MinimizeInactiveClients { get; set; }
        public bool MonitorClientHealth { get; set; }
        public bool AutoRelogCrashedClients { get; set; }
        public List<string> TeamCardOrder { get; set; } = new List<string>();
        public ControlPanelPreferences ControlPanel { get; set; } = new ControlPanelPreferences();
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
        // Server rules (config/servers.json): last applied revision, optional source override,
        // and catalog servers the user removed so updates do not add them back.
        public int ServerRulesRevision { get; set; }
        public string ServerRulesSource { get; set; }
        public bool CheckServerRulesOnStartup { get; set; } = true;
        public List<string> RemovedServerIds { get; set; } = new List<string>();
        // AOSharp plugins: named plugin sets, per-character defaults (CharacterConfig) and
        // per-preset overrides (PresetCharacter). Plugins load automatically when enabled.
        public bool AutoInjectPlugins { get; set; }
        // Nexus Health Monitor rides along with every set Nexus loads, so loaded clients are measured.
        public bool IncludeHealthMonitor { get; set; } = true;
        public bool PluginSetsInitialized { get; set; }
        // Nexus Shortcuts rides along like the health monitor, so the in-game buttons work.
        public bool IncludeShortcuts { get; set; } = true;
        // Play style for characters launched outside a team (see PlayStyles).
        public string DefaultPlayStyle { get; set; }
        // 1 = starter sets moved to the Z suite (done once; see PluginSets.MigrateToZ).
        public int PluginSuiteVersion { get; set; }
        public string AOSharpPackPath { get; set; }
        // Preserve existing configurations; fresh installs have no runtime and never download automatically.
        public bool AOSharpDisabled { get; set; }
        public List<string> SharedPlugins { get; set; } = new List<string>();
        public List<PluginSet> PluginSets { get; set; } = new List<PluginSet>();
        public List<CustomPlugin> CustomPlugins { get; set; } = new List<CustomPlugin>();
        // Latest in-game test result per plugin (Plugins page > Test plugins).
        public List<PluginTestRecord> PluginTests { get; set; } = new List<PluginTestRecord>();
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
        // True when the user edited a server that also exists in the server list.
        public bool Customized { get; set; }
        public override string ToString() => Name;
        // Defaults come from config/servers.json (built in, or a newer cached revision).
        internal static List<LoginServerProfile> Defaults() => ServerRules.Current.Servers.Select(ServerRules.ToProfile).ToList();
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
        // Off: characters launched with this team get no plugins (e.g. a crafting team).
        public bool LoadPlugins { get; set; } = true;
        // Adds the plugins for an activity (team play, Inferno, City raid...) to every member's set.
        public string PlayStyle { get; set; }
        public bool OrderedLogin { get; set; }
        public bool AutoFormTeam { get; set; }
        public override string ToString() => Name;
    }

    internal sealed class PresetCharacter
    {
        public string AccountId { get; set; }
        public int CharacterId { get; set; }
        // Null uses the character's default plugin set; PluginSet.None loads nothing.
        public string PluginSetId { get; set; }
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

    internal sealed class PluginTestRecord
    {
        public string TestKind { get; set; }
        public string PluginSha256 { get; set; }
        public string Profession { get; set; }
        public string ServerEndpoint { get; set; }
        public string PluginId { get; set; }
        public string Status { get; set; }
        public string Message { get; set; }
        public string Character { get; set; }
        public long TestedUtcTicks { get; set; }
    }

    internal sealed class CharacterConfig
    {
        public string PendingGuiTemplateId { get; set; }
        public string Name { get; set; }
        public int CharacterId { get; set; }
        public bool Selected { get; set; } = true;
        public bool NamePending { get; set; }
        public string PluginSetId { get; set; }
    }

    internal sealed class LaunchRequest
    {
        public string Language { get; set; } = "auto";
        public string GamePath { get; set; }
        public string Server { get; set; }
        public int Port { get; set; }
        public int ReadyTimeoutSeconds { get; set; } = 120;
        public bool QuietLaunch { get; set; }
        public bool AllowExperimentalAovk { get; set; }
        public bool FastStartup { get; set; }
        public List<LaunchCharacter> Characters { get; set; } = new List<LaunchCharacter>();
        public bool OrderedStarts { get; set; }
        public bool KeepLeaderActive { get; set; }
        public int LauncherProcessId { get; set; }
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
            return ServerRules.Find(server, port)?.CharacterSlots ?? 0;
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
            ServerRules.Kind(account.Server ?? "", account.Port) == "live";

        internal static bool IsProjectRubika(AccountConfig account) =>
            ServerRules.Kind(account.Server ?? "", account.Port) == "prk";

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

        private static string GetReadPath(string directory) => Nexus.Core.SettingsStorage.ReadPath(directory);
        private static bool PathExists(string path) => Nexus.Core.SettingsStorage.Exists(path);

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
            value.RemovedServerIds = (value.RemovedServerIds ?? new List<string>()).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            // Settings from before server rules (1.2.x) have no "customised"/"removed" markers. Record
            // what the user already chose, so the first rules update cannot overwrite it.
            if (!fields.ContainsKey(nameof(LauncherSettings.ServerRulesRevision))) ServerRules.MarkExistingChoices(value);
            value.PluginSets = (value.PluginSets ?? new List<PluginSet>()).Where(set => set != null && !string.IsNullOrWhiteSpace(set.Id)).ToList();
            foreach (var set in value.PluginSets) set.Plugins = (set.Plugins ?? new List<string>()).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            value.SharedPlugins = (value.SharedPlugins ?? new List<string>()).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            value.TeamCardOrder = (value.TeamCardOrder ?? new List<string>()).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToList();
            value.CustomPlugins = (value.CustomPlugins ?? new List<CustomPlugin>()).Where(plugin => plugin != null && !string.IsNullOrWhiteSpace(plugin.Id) && !string.IsNullOrWhiteSpace(plugin.Path)).ToList();
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
            try
            {
                Nexus.Core.SettingsStorage.Save(directory, new JavaScriptSerializer().Serialize(settings),
                    json => new JavaScriptSerializer().Deserialize<LauncherSettings>(json)?.Version == 1);
            }
            catch (Exception error)
            {
                FailureDiagnostics.Install.Report("settings-save", "Settings were not saved", error);
                throw;
            }
        }
    }
}
