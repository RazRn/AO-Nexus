using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using static AOLauncher.SidebarText;

namespace AOLauncher
{
    // Server definitions (addresses, connection limits, character slots and health
    // endpoints) live in config/servers.json instead of code. A copy is embedded as the
    // offline fallback; Nexus can fetch a newer revision from a local file or GitHub.
    internal sealed class ServerRuleEndpoint
    {
        public string Host { get; set; }
        public int Port { get; set; }
    }

    internal sealed class ServerRuleHealth
    {
        public ServerRuleEndpoint Zone { get; set; }
        public ServerRuleEndpoint Chat { get; set; }
    }

    internal sealed class ServerRule
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Kind { get; set; }
        public string Host { get; set; }
        public int Port { get; set; }
        public int MaxAccountsOnline { get; set; }
        public int MaxCharactersOnlinePerAccount { get; set; }
        public int CharacterSlots { get; set; }
        public ServerRuleHealth Health { get; set; }
        internal string Endpoint => Host + ":" + Port;
    }

    internal sealed class ServerCatalog
    {
        public string Schema { get; set; }
        public int SchemaVersion { get; set; }
        public int Revision { get; set; }
        public string Updated { get; set; }
        public string Notes { get; set; }
        public string DefaultServerId { get; set; }
        public List<ServerRule> Servers { get; set; } = new List<ServerRule>();
        internal string Source { get; set; }
    }

    internal sealed class ServerRulesPlan
    {
        internal List<LoginServerProfile> Profiles = new List<LoginServerProfile>();
        internal readonly List<string> Changes = new List<string>();
        internal readonly List<string> AddressChanges = new List<string>();
        internal bool HasChanges => Changes.Count > 0 || AddressChanges.Count > 0;
    }

    internal static class ServerRules
    {
        internal const string GitHubSource = "https://raw.githubusercontent.com/RazRn/AO-Nexus/main/config/servers.json";
        internal const int MaximumBytes = 256 * 1024;
        static ServerCatalog current;
        static readonly object gate = new object();

        internal static string CachePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AONexus", "ServerRules", "servers.json");

        internal static ServerCatalog Current
        {
            get
            {
                lock (gate)
                {
                    if (current != null) return current;
                    var embedded = Embedded();
                    ServerCatalog cached = null;
                    try { if (File.Exists(CachePath)) cached = Parse(ReadBounded(CachePath), "cache"); } catch { cached = null; }
                    current = cached != null && cached.Revision >= embedded.Revision ? cached : embedded;
                    return current;
                }
            }
        }

        internal static ServerCatalog Embedded()
        {
            using (var stream = typeof(ServerRules).Assembly.GetManifestResourceStream("AOLauncher.servers.json"))
            {
                if (stream == null) throw new InvalidDataException("The built-in server list is missing. Download AO Nexus again.");
                using (var reader = new StreamReader(stream, Encoding.UTF8)) return Parse(reader.ReadToEnd(), "built-in");
            }
        }

        internal static ServerCatalog Parse(string json, string source)
        {
            if (string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > MaximumBytes) throw new InvalidDataException("The server list is empty or too large.");
            var catalog = new JavaScriptSerializer { MaxJsonLength = MaximumBytes, RecursionLimit = 16 }.Deserialize<ServerCatalog>(json);
            if (catalog == null || catalog.Schema != "AONexus.ServerRules") throw new InvalidDataException("This file is not an AO Nexus server list.");
            if (catalog.SchemaVersion != 1) throw new InvalidDataException("This server list needs a newer AO Nexus.");
            if (catalog.Revision < 1) throw new InvalidDataException("The server list has no revision number.");
            if (catalog.Servers == null || catalog.Servers.Count == 0 || catalog.Servers.Count > 64) throw new InvalidDataException("The server list must contain 1 to 64 servers.");
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var server in catalog.Servers)
            {
                if (server == null || string.IsNullOrWhiteSpace(server.Id) || server.Id.Length > 64 || server.Id.Any(c => !(char.IsLetterOrDigit(c) || c == '-' || c == '_')))
                    throw new InvalidDataException("Each server needs a simple identifier (letters, digits, - and _).");
                if (!ids.Add(server.Id)) throw new InvalidDataException("Server identifier '" + server.Id + "' is used twice.");
                if (string.IsNullOrWhiteSpace(server.Name) || server.Name.Length > 100 || server.Name.Any(char.IsControl)) throw new InvalidDataException("Server '" + server.Id + "' needs a name.");
                IPAddress address;
                if (!IPAddress.TryParse(server.Host, out address) || address.AddressFamily != AddressFamily.InterNetwork) throw new InvalidDataException("Server '" + server.Id + "' needs an IPv4 address.");
                server.Host = address.ToString();
                if (server.Port < 1 || server.Port > 65535) throw new InvalidDataException("Server '" + server.Id + "' has an invalid port.");
                foreach (int limit in new[] { server.MaxAccountsOnline, server.MaxCharactersOnlinePerAccount })
                    if (limit < 0 || limit > 100) throw new InvalidDataException("Server '" + server.Id + "' has a limit outside 0-100.");
                if (server.CharacterSlots < 0 || server.CharacterSlots > 100) throw new InvalidDataException("Server '" + server.Id + "' has invalid character slots.");
                server.Kind = (server.Kind ?? "custom").Trim().ToLowerInvariant();
                if (server.Kind != "rebirth" && server.Kind != "live" && server.Kind != "prk") server.Kind = "custom";
                foreach (var endpoint in new[] { server.Health?.Zone, server.Health?.Chat })
                {
                    if (endpoint == null) continue;
                    if (endpoint.Port < 1 || endpoint.Port > 65535) throw new InvalidDataException("Server '" + server.Id + "' has an invalid health-check port.");
                    if (endpoint.Host != null && (endpoint.Host.Length > 253 || Uri.CheckHostName(endpoint.Host) == UriHostNameType.Unknown))
                        throw new InvalidDataException("Server '" + server.Id + "' has an invalid health-check host.");
                }
            }
            if (catalog.DefaultServerId != null && !ids.Contains(catalog.DefaultServerId)) catalog.DefaultServerId = catalog.Servers[0].Id;
            catalog.Source = source;
            return catalog;
        }

        static string ReadBounded(string path)
        {
            var info = new FileInfo(path);
            if (info.Length > MaximumBytes) throw new InvalidDataException("The server list is too large.");
            return File.ReadAllText(path, Encoding.UTF8);
        }

        // Where updates come from, in order. A configured source wins. Otherwise a servers.json
        // next to the EXE, or the repository's config/servers.json when Nexus runs from dist\,
        // is the local source; GitHub is the published fallback.
        internal static List<string> Sources(LauncherSettings settings)
        {
            var result = new List<string>();
            if (!string.IsNullOrWhiteSpace(settings?.ServerRulesSource)) { result.Add(settings.ServerRulesSource.Trim()); return result; }
            try
            {
                string exe = Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath);
                foreach (var candidate in new[] { Path.Combine(exe, "servers.json"), Path.GetFullPath(Path.Combine(exe, "..", "config", "servers.json")) })
                    if (File.Exists(candidate)) { result.Add(candidate); break; }
            }
            catch { }
            result.Add(GitHubSource);
            return result;
        }

        internal static string DescribeSource(LauncherSettings settings)
        {
            var sources = Sources(settings);
            return sources.Count > 0 ? sources[0] : GitHubSource;
        }

        internal static async Task<ServerCatalog> FetchAsync(LauncherSettings settings, CancellationToken cancellation)
        {
            Exception last = null;
            foreach (string source in Sources(settings))
            {
                try { return Parse(await ReadSourceAsync(source, cancellation).ConfigureAwait(false), source); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { last = ex; }
            }
            throw new InvalidDataException(last?.Message ?? "No server list source is available.", last);
        }

        static async Task<string> ReadSourceAsync(string source, CancellationToken cancellation)
        {
            Uri uri;
            if (Uri.TryCreate(source, UriKind.Absolute, out uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                if (uri.Scheme != Uri.UriSchemeHttps) throw new InvalidDataException("Server lists can only be downloaded over HTTPS.");
                using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) })
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("AONexus/" + AppUpdates.CurrentVersion.ToString(3));
                    using (var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode) throw new InvalidDataException("The server list could not be downloaded (" + (int)response.StatusCode + ").");
                        if (response.Content.Headers.ContentLength > MaximumBytes) throw new InvalidDataException("The server list is too large.");
                        using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var buffer = new MemoryStream())
                        {
                            var chunk = new byte[8192]; int read;
                            while ((read = await stream.ReadAsync(chunk, 0, chunk.Length, cancellation).ConfigureAwait(false)) > 0)
                            {
                                buffer.Write(chunk, 0, read);
                                if (buffer.Length > MaximumBytes) throw new InvalidDataException("The server list is too large.");
                            }
                            return Encoding.UTF8.GetString(buffer.ToArray());
                        }
                    }
                }
            }
            return await Task.Run(() => ReadBounded(source), cancellation).ConfigureAwait(false);
        }

        // Keeps the last valid list for offline starts and for the limit/health lookups below.
        internal static void Adopt(ServerCatalog catalog)
        {
            lock (gate) current = catalog;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CachePath));
                string json = new JavaScriptSerializer().Serialize(catalog);
                string temporary = CachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temporary, json, Encoding.UTF8);
                if (File.Exists(CachePath)) File.Replace(temporary, CachePath, null); else File.Move(temporary, CachePath);
            }
            catch (Exception ex) { DiagnosticJournal.Record("server-rules", "Could not cache the server list: " + ex.Message); }
        }

        internal static ServerRule Find(string host, int port)
        {
            IPAddress address;
            string normalized = IPAddress.TryParse(host, out address) ? address.ToString() : (host ?? "").Trim();
            return Current.Servers.FirstOrDefault(server => server.Port == port && string.Equals(server.Host, normalized, StringComparison.OrdinalIgnoreCase));
        }

        internal static ServerRule FindById(string id) => id == null ? null : Current.Servers.FirstOrDefault(server => string.Equals(server.Id, id, StringComparison.OrdinalIgnoreCase));

        internal static string Kind(string host, int port) => Find(host, port)?.Kind ?? "custom";

        internal static LoginServerProfile ToProfile(ServerRule rule) => new LoginServerProfile
        {
            Id = rule.Id, Name = rule.Name, Server = rule.Host, Port = rule.Port,
            MaxAccounts = rule.MaxAccountsOnline, MaxCharactersPerAccount = rule.MaxCharactersOnlinePerAccount, CharacterSlots = rule.CharacterSlots
        };

        internal static bool Matches(LoginServerProfile profile, ServerRule rule) => profile != null && rule != null
            && profile.Name == rule.Name && profile.Server == rule.Host && profile.Port == rule.Port
            && (profile.MaxAccounts ?? rule.MaxAccountsOnline) == rule.MaxAccountsOnline
            && (profile.MaxCharactersPerAccount ?? rule.MaxCharactersOnlinePerAccount) == rule.MaxCharactersOnlinePerAccount
            && (profile.CharacterSlots ?? rule.CharacterSlots) == rule.CharacterSlots;

        // Marks servers the user changed by hand so that rule updates leave them alone.
        internal static void MarkCustomized(IEnumerable<LoginServerProfile> profiles, ServerCatalog catalog)
        {
            foreach (var profile in profiles)
            {
                var rule = catalog.Servers.FirstOrDefault(item => string.Equals(item.Id, profile.Id, StringComparison.OrdinalIgnoreCase));
                profile.Customized = rule != null && !Matches(profile, rule);
            }
        }

        // Servers that versions before server rules (1.2.x) created by default.
        internal static readonly string[] LegacyDefaultIds = { "rebirth", "prk", "live", "live-2019" };

        // Settings written before server rules have no markers. A server that differs from the list
        // was edited by the user; a legacy default that is missing was removed by the user. Both are
        // kept as they are, so the first rules update after upgrading cannot overwrite them.
        internal static void MarkExistingChoices(LauncherSettings settings)
        {
            foreach (var profile in settings.LoginServers ?? new List<LoginServerProfile>())
            {
                var rule = FindById(profile?.Id);
                if (rule != null && !Matches(profile, rule)) profile.Customized = true;
            }
            foreach (string id in LegacyDefaultIds)
                if (FindById(id) != null && !(settings.LoginServers ?? new List<LoginServerProfile>()).Any(profile => string.Equals(profile?.Id, id, StringComparison.OrdinalIgnoreCase))
                    && !settings.RemovedServerIds.Contains(id, StringComparer.OrdinalIgnoreCase))
                    settings.RemovedServerIds.Add(id);
        }

        internal static ServerRulesPlan Plan(IEnumerable<LoginServerProfile> existing, ServerCatalog catalog, ICollection<string> removedIds)
        {
            var plan = new ServerRulesPlan();
            var profiles = existing.Select(Copy).ToList();
            foreach (var rule in catalog.Servers)
            {
                var profile = profiles.FirstOrDefault(item => string.Equals(item.Id, rule.Id, StringComparison.OrdinalIgnoreCase));
                if (profile == null)
                {
                    if (removedIds != null && removedIds.Contains(rule.Id, StringComparer.OrdinalIgnoreCase)) continue;
                    profiles.Add(ToProfile(rule));
                    plan.Changes.Add(AOLauncher.Localization.Texts.Get("Text_Added_server_942B64347CC1") + rule.Name + " (" + rule.Endpoint + ")");
                    continue;
                }
                if (profile.Customized || Matches(profile, rule)) continue;
                if (profile.Server != rule.Host || profile.Port != rule.Port)
                    plan.AddressChanges.Add(profile.Name + ": " + profile.Server + ":" + profile.Port + " → " + rule.Endpoint);
                if (profile.Name != rule.Name) plan.Changes.Add(AOLauncher.Localization.Texts.Get("Text_Renamed_403A71EFFEF8") + profile.Name + " → " + rule.Name);
                if ((profile.MaxAccounts ?? -1) != rule.MaxAccountsOnline || (profile.MaxCharactersPerAccount ?? -1) != rule.MaxCharactersOnlinePerAccount || (profile.CharacterSlots ?? -1) != rule.CharacterSlots)
                    plan.Changes.Add(rule.Name + ": " + AOLauncher.Localization.Texts.Get("Text_accounts_online_5D315D49E61D") + Limit(rule.MaxAccountsOnline) + ", "
                        + AOLauncher.Localization.Texts.Get("Text_chars_online_account_EB3FEF9DB35F") + Limit(rule.MaxCharactersOnlinePerAccount) + ", "
                        + AOLauncher.Localization.Texts.Get("Text_character_slots_7E98AD64EF4A") + Limit(rule.CharacterSlots));
                profile.Name = rule.Name; profile.Server = rule.Host; profile.Port = rule.Port;
                profile.MaxAccounts = rule.MaxAccountsOnline; profile.MaxCharactersPerAccount = rule.MaxCharactersOnlinePerAccount; profile.CharacterSlots = rule.CharacterSlots;
            }
            plan.Profiles = profiles;
            return plan;
        }

        static string Limit(int value) => value == 0 ? "∞" : value.ToString();

        internal static LoginServerProfile Copy(LoginServerProfile item) => new LoginServerProfile
        {
            Id = item.Id, Name = item.Name, Server = item.Server, Port = item.Port, MaxAccounts = item.MaxAccounts,
            MaxCharactersPerAccount = item.MaxCharactersPerAccount, CharacterSlots = item.CharacterSlots, Customized = item.Customized
        };

        // Lets the shared account policy and health checks read limits from the list.
        internal static void Install()
        {
            ServerAccountPolicy.AccountLimitLookup = (host, port) => Find(host, port)?.MaxAccountsOnline;
            ServerAccountPolicy.CharacterLimitLookup = (host, port) => Find(host, port)?.MaxCharactersOnlinePerAccount;
        }
    }
}
