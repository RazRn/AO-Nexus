using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using static AOLauncher.SidebarText;

namespace AOLauncher
{
    // Watches the clients Nexus logged in (the same verified session list camping and
    // monitoring use) and asks the plugin host to load each character's plugin set once the
    // game window shows that character. Runs off the UI thread.
    internal sealed class PluginAutoInjector
    {
        internal sealed class Row
        {
            internal string AccountId, CharacterName, SetId, SetName, State, StateText, Detail, PlayStyle, Suite;
            internal int CharacterId, Pid;
            internal long StartTicks;
            internal bool StartupPending, ShortcutsRequested;
            // What Nexus Shortcuts reported about each plugin; empty until the first check.
            internal List<PluginCheck> Health = new List<PluginCheck>();
            internal string HealthSummary;
        }

        sealed class Track
        {
            internal DateTime FirstSeenUtc = DateTime.UtcNow, LastAttemptUtc = DateTime.MinValue;
            internal int Attempts;
            internal bool Finished;
            internal int? MissingPackRevision;
            internal string MissingPackPath;
            internal string LastError;
            internal DateTime ForeignCheckedUtc = DateTime.MinValue;
            internal ForeignAOSharp.Finding Foreign;
            internal string ForeignLogged;
            // Automatic set: Nexus Shortcuts alone is loaded to read the profession, then the set.
            internal bool Probing, ProfessionReported;
            internal DateTime ProbeStartedUtc;
            // Health: what was sent, when, and what Nexus Shortcuts reported.
            internal string[] Paths;
            internal DateTime LoadedUtc = DateTime.MinValue, HealthCheckedUtc = DateTime.MinValue;
            internal List<PluginCheck> Checks = new List<PluginCheck>();
            internal readonly HashSet<string> Logged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            internal bool Testing;
            internal bool MonitorRetryAttempted;
            internal bool StartupPending, StartupFailed, ReadyLogged;
            internal long StartupQpc;
            internal string StartupInstance;
        }

        const int ProbeTimeoutSeconds = 90, HealthEverySeconds = 10;
        const string StartupLabel = "Nexus startup check";

        // Raised on the polling thread when an automatic character's profession is known:
        // account id, character id, profession as AOSharp names it. The handler assigns a set.
        internal event Action<string, int, string> ProfessionFound;

        const int ForeignCheckSeconds = 6;
        readonly HashSet<string> notedControllers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Clients this launcher is injecting right now: their AOSharp pipe is ours before the host reports them.
        readonly HashSet<int> injecting = new HashSet<int>();

        const int LegacySettleSeconds = 8, RetrySeconds = 20, MaximumAttempts = 3;
        internal static bool ReadyToStart(bool? gameReady, double secondsSinceSeen)
            => gameReady ?? secondsSinceSeen >= LegacySettleSeconds;
        readonly object gate = new object();
        readonly Dictionary<string, Track> tracks = new Dictionary<string, Track>();
        // Which team launched each character, kept on disk: after a Nexus restart a character launched
        // with "plugins off for this team" must not fall back to its default set.
        internal sealed class LaunchRecord { public string PresetId { get; set; } public long RecordedUtcTicks { get; set; } }
        internal static string LaunchContextPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AONexus", "Sessions", "LaunchContext.json");
        Dictionary<string, LaunchRecord> launchPresets;
        AOSharpPack pack;
        string packError;
        bool packLocated;
        string packLocation;
        int packRevision;
        List<Row> rows = new List<Row>();

        internal List<Row> Rows { get { lock (gate) return rows.ToList(); } }
        internal bool AnyLoaded { get { lock (gate) return rows.Any(row => row.State == "Loaded"); } }

        static string CharacterKey(string accountId, int characterId) => accountId + "|" + characterId;

        // Remembers which team preset launched a character (null: launched without a team), so its
        // override and play style apply, also after Nexus restarts.
        internal void RecordLaunch(string accountId, int characterId, string presetId)
        {
            lock (gate)
            {
                string key = CharacterKey(accountId, characterId);
                var records = Records();
                // A duplicate login can be skipped by the engine. Keep the earlier context
                // for the still-running process instead of changing its plugin policy.
                if (records.TryGetValue(key, out var previous)) records[key + "|" + previous.RecordedUtcTicks] = previous;
                records[key] = new LaunchRecord { PresetId = presetId, RecordedUtcTicks = DateTime.UtcNow.Ticks };
                SaveRecords();
            }
        }

        // The team a running client was launched with: only a record made before the client started counts.
        internal string LaunchPresetFor(string accountId, int characterId, long clientStartUtcTicks)
        {
            lock (gate)
                return RecordFor(Records(), CharacterKey(accountId, characterId), clientStartUtcTicks)?.Value.PresetId;
        }

        static KeyValuePair<string, LaunchRecord>? RecordFor(Dictionary<string, LaunchRecord> records, string key, long started)
        {
            return records.Where(pair => (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)
                    || pair.Key.StartsWith(key + "|", StringComparison.OrdinalIgnoreCase)) && pair.Value.RecordedUtcTicks <= started)
                .OrderByDescending(pair => pair.Value.RecordedUtcTicks).Select(pair => (KeyValuePair<string, LaunchRecord>?)pair).FirstOrDefault();
        }

        Dictionary<string, LaunchRecord> Records()
        {
            if (launchPresets != null) return launchPresets;
            launchPresets = new Dictionary<string, LaunchRecord>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(LaunchContextPath) && new FileInfo(LaunchContextPath).Length < 1024 * 1024)
                    foreach (var pair in new JavaScriptSerializer().Deserialize<Dictionary<string, LaunchRecord>>(File.ReadAllText(LaunchContextPath)) ?? new Dictionary<string, LaunchRecord>())
                        if (pair.Value != null) launchPresets[pair.Key] = pair.Value;
            }
            catch (Exception ex) { DiagnosticJournal.Record("plugins", "Launch context could not be read: " + ex.Message); }
            return launchPresets;
        }

        void SaveRecords()
        {
            try
            {
                // Retain the latest context per character and any older context still used
                // by a registered process. An AO session may legitimately last days.
                long cutoff = DateTime.UtcNow.AddDays(-2).Ticks;
                List<PresetCamping.Client> active = null;
                try {
                    active = LoginPreflight.Read();
                    FailureDiagnostics.Plugins.Recovered("launch-context/active-sessions", "Session registry readable");
                } catch (Exception ex) {
                    FailureDiagnostics.Plugins.Report("launch-context/active-sessions", "Existing launch records retained", ex);
                }
                if (active != null)
                {
                    var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var client in active.Where(client => client?.AccountIdentity?.StartsWith("id:") == true))
                    {
                        var used = RecordFor(launchPresets, CharacterKey(client.AccountIdentity.Substring(3), client.CharacterId), client.StartTimeUtcTicks);
                        if (used.HasValue) keep.Add(used.Value.Key);
                    }
                    foreach (string key in launchPresets.Where(pair => pair.Key.Count(c => c == '|') > 1 && pair.Value.RecordedUtcTicks < cutoff && !keep.Contains(pair.Key)).Select(pair => pair.Key).ToList())
                        launchPresets.Remove(key);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(LaunchContextPath));
                string temporary = LaunchContextPath + ".tmp";
                File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(launchPresets));
                if (File.Exists(LaunchContextPath)) File.Replace(temporary, LaunchContextPath, null); else File.Move(temporary, LaunchContextPath);
            }
            catch (Exception ex) { DiagnosticJournal.Record("plugins", "Launch context could not be saved: " + ex.Message); }
        }

        readonly object packGate = new object();
        // First use extracts and verifies the built-in pack, so keep it off the row lock.
        internal AOSharpPack Pack(LauncherSettings settings, out string error)
        {
            if (settings.AOSharpDisabled) { error = AOLauncher.Localization.Texts.Get("Text_AO_is_disabled_Enable_it_in_2B8ABE072BE3"); return null; }
            lock (packGate)
            {
                string location = settings.AOSharpPackPath ?? "";
                if (!packLocated || !string.Equals(packLocation, location, StringComparison.OrdinalIgnoreCase))
                { pack = AOSharpPack.Locate(settings, out packError); packLocation = location; packLocated = true; }
                error = packError; return pack;
            }
        }

        internal void ForgetPack() { lock (packGate) { packLocated = false; pack = null; packError = null; System.Threading.Interlocked.Increment(ref packRevision); } }

        void RetryAfterPackChange(Track track, LauncherSettings settings)
        {
            if (!track.MissingPackRevision.HasValue || (track.MissingPackRevision.Value == System.Threading.Volatile.Read(ref packRevision)
                && string.Equals(track.MissingPackPath, settings.AOSharpPackPath ?? "", StringComparison.OrdinalIgnoreCase))) return;
            // Only a failure before injection is eligible. Never restart deliberately
            // unloaded plugins or retry a potentially partial injection on a settings save.
            track.MissingPackRevision = null; track.Finished = false; track.Attempts = 0;
            track.LastAttemptUtc = DateTime.MinValue; track.LastError = null;
        }

        internal List<string> Poll(LauncherSettings settings)
        {
            var messages = new List<string>();
            var all = new LaunchPreset { Characters = settings.Accounts.SelectMany(a => a.Characters.Select(c => new PresetCharacter { AccountId = a.Id, CharacterId = c.CharacterId })).ToList() };
            List<PresetCamping.Client> clients;
            try {
                clients = PresetCamping.Find(settings, all);
                FailureDiagnostics.Plugins.Recovered("plugin-discovery", "Client discovery resumed");
            }
            catch (Exception ex) {
                FailureDiagnostics.Plugins.Report("plugin-discovery", "Previous plugin rows retained; retry on next poll", ex);
                return messages;
            }
            if (clients.Count == 0)
            {
                lock (gate) { tracks.Clear(); rows = new List<Row>(); }
                return messages;
            }
            // Only ask the host for status; never start it just to look.
            var status = PluginHostClient.Send(new PluginHostClient.Request { Command = "status" }, null, false, 3000);
            var hostClients = status?.Clients ?? new List<PluginHostClient.ClientState>();
            var next = new List<Row>();
            var seen = new HashSet<string>();
            AOSharpPack located; lock (packGate) located = pack;
            var ownFolders = new[] { AOSharpPack.CacheRoot, settings.AOSharpPackPath, located?.Root, located?.AOSharpDirectory };
            foreach (var client in clients)
            {
                string key = client.ProcessId + "-" + client.StartTimeUtcTicks;
                seen.Add(key);
                string accountId = client.AccountIdentity != null && client.AccountIdentity.StartsWith("id:") ? client.AccountIdentity.Substring(3) : null;
                var account = settings.Accounts.FirstOrDefault(item => string.Equals(item.Id, accountId, StringComparison.OrdinalIgnoreCase));
                var character = account?.Characters.FirstOrDefault(item => item.CharacterId == client.CharacterId);
                if (account == null || character == null) continue;
                Track track;
                lock (gate) { if (!tracks.TryGetValue(key, out track)) tracks[key] = track = new Track(); }
                RetryAfterPackChange(track, settings);
                string presetId = LaunchPresetFor(account.Id, character.CharacterId, client.StartTimeUtcTicks);
                var preset = presetId == null ? null : settings.Presets.FirstOrDefault(item => item.Id == presetId);
                string setId = PluginSets.Resolve(settings, account, character, preset);
                var set = PluginSets.Find(settings, setId);
                string style = PlayStyles.For(settings, preset);
                var hosted = hostClients.FirstOrDefault(item => item.Pid == client.ProcessId && item.StartTicks == client.StartTimeUtcTicks);
                var row = new Row { AccountId = account.Id, CharacterId = character.CharacterId, CharacterName = client.CharacterName, Pid = client.ProcessId,
                    StartTicks = client.StartTimeUtcTicks, SetId = setId, SetName = set?.Name + (preset != null && set != null ? " · " + preset.Name : ""), State = hosted?.State, Detail = hosted?.Detail ?? track.LastError,
                    PlayStyle = style, Suite = PluginSets.SuiteOf(set) };
                bool teamOff = preset != null && !preset.LoadPlugins;
                if (hosted?.Label == StartupLabel && !track.StartupFailed && set != null
                    && hosted.Plugins?.Length == 1 && string.Equals(hosted.Plugins[0], AOSharpPack.MonitorPlugin(), StringComparison.OrdinalIgnoreCase))
                    track.StartupPending = true;
                if (teamOff) row.State = row.State ?? "TeamOff";
                string foreignMessage = CheckForeign(row, hosted, track, ownFolders);
                if (foreignMessage != null) messages.Add(foreignMessage);
                bool ready = settings.AutoInjectPlugins && !settings.AOSharpDisabled && !teamOff && !track.Testing && hosted == null && track.Foreign == null && !track.Finished && track.Attempts < MaximumAttempts
                    && (DateTime.UtcNow - track.LastAttemptUtc).TotalSeconds >= RetrySeconds;
                bool automatic = set == null && PluginSets.IsAutomatic(account, character, preset);
                if (ready && (set != null || automatic))
                    ready = ReadyToStart(PresetCamping.PluginsReady(client), (DateTime.UtcNow - track.FirstSeenUtc).TotalSeconds);
                if (ready && set != null)
                {
                    string message = Inject(settings, row, set, track, style, true);
                    if (message != null) messages.Add(message);
                }
                else if (ready && automatic)
                {
                    string message = Probe(settings, row, track);
                    if (message != null) messages.Add(message);
                }
                else if (track.StartupPending && CanAdvanceProbe(settings.AutoInjectPlugins && !settings.AOSharpDisabled, teamOff, track.Testing) && hosted?.State == "Loaded" && track.Foreign == null)
                {
                    var pulse = ReadStartupPulse(Path.Combine(ClientMonitor.PulseRoot, row.Pid + "-" + row.StartTicks + ".json"));
                    if (StartupReady(pulse, row.Pid, row.StartTicks, track.StartupQpc, Stopwatch.GetTimestamp())
                        && set != null && PresetCamping.PluginsReady(client) != false) {
                        track.StartupPending = false; track.StartupInstance = pulse.Instance;
                        var message = Inject(settings, row, set, track, style);
                        if (message != null) messages.Add(message);
                        if (row.State != "Loaded") track.StartupFailed = true;
                    } else {
                        messages.AddRange(CheckHealth(row, hosted, track, settings, true));
                        if (track.LoadedUtc != DateTime.MinValue && (DateTime.UtcNow - track.LoadedUtc).TotalSeconds >= 65) {
                            track.StartupPending = false; track.StartupFailed = true;
                            track.LastError = AOLauncher.Localization.Texts.Get("Text_AO_startup_did_not_produce_fresh_0ADA2708DFAC");
                            messages.Add(row.CharacterName + ": " + track.LastError);
                            FailureDiagnostics.Plugins.Report("plugin-startup/measurement", "Profession plugins withheld; manual retry required",
                                detail: track.LastError, pid: row.Pid, startTicks: row.StartTicks);
                        }
                    }
                }
                else if (track.Probing && CanAdvanceProbe(settings.AutoInjectPlugins && !settings.AOSharpDisabled, teamOff, track.Testing) && hosted?.State == "Loaded" && track.Foreign == null)
                {
                    string message = set != null ? FinishProbe(settings, row, set, track, style) : AskProfession(row, track, account, character);
                    if (message != null) messages.Add(message);
                }
                else if (hosted?.State == "Loaded" && track.Foreign == null && !track.Testing)
                    messages.AddRange(CheckHealth(row, hosted, track, settings, settings.AutoInjectPlugins && !settings.AOSharpDisabled && !teamOff));
                row.StateText = Describe(row, settings, track, automatic);
                if (track.StartupFailed) {row.State = "Failed";row.Detail = track.LastError;}
                if (row.State == "Loaded" && !track.Probing)
                {
                    row.Health = track.Checks.ToList();
                    row.HealthSummary = PluginHealth.Summary(row.Health);
                    if (row.HealthSummary != null) row.StateText += " · " + row.HealthSummary;
                    var problems = row.Health.Where(check => check.Problem).Select(check => check.Line()).ToList();
                    if (problems.Count > 0) row.Detail = string.Join("\r\n", problems) + (string.IsNullOrEmpty(row.Detail) ? "" : "\r\n" + row.Detail);
                }
                row.StartupPending = track.StartupPending || track.Probing;
                row.ShortcutsRequested = RequestsShortcuts(track.Paths ?? hosted?.Plugins);
                if (!track.ReadyLogged && StartupConfirmed(row.State, row.StartupPending, track.Testing, track.Checks)) {
                    track.ReadyLogged = true;
                    messages.Add(row.CharacterName + ": " + AOLauncher.Localization.Texts.Get("Startup.PluginsReady"));
                }
                next.Add(row);
            }
            lock (gate)
            {
                foreach (string stale in tracks.Keys.Where(key => !seen.Contains(key)).ToList()) tracks.Remove(stale);
                rows = next;
            }
            // AOSharp.exe next to loaded Nexus plugins: one attach away from hooking a client twice.
            if (next.Any(row => row.State == "Loaded"))
                foreach (string controller in ForeignAOSharp.RunningAOSharp())
                    if (notedControllers.Add(controller))
                        messages.Add(string.Format(AOLauncher.Localization.Texts.Get("Text_Warning_0_is_running_while_Nexus_F453146FD44A"), controller));
            return messages;
        }

        internal static bool RequestsShortcuts(IEnumerable<string> paths)
            => paths != null && paths.Any(path => string.Equals(Path.GetFileName(path), "NexusShortcuts.dll", StringComparison.OrdinalIgnoreCase));
        internal static bool CanUseShortcuts(Row row)
            => row.State == "Loaded" && !row.StartupPending && row.ShortcutsRequested;
        internal static bool StartupConfirmed(string state, bool warmup, bool testing, List<PluginCheck> checks)
            => state == "Loaded" && !warmup && !testing && checks.Count > 0
                && checks.All(c => c.Status == PluginStatus.Confirmed || c.Status == PluginStatus.Running);

        internal static bool CanAdvanceProbe(bool automaticLoading, bool teamOff, bool testing)
            => automaticLoading && !teamOff && !testing;

        internal static bool StartupReady(ClientPulse pulse,int pid,long started,long requestedQpc,long now)
            => ClientMonitor.ValidatePulse(pulse,pid,started,now)=="SnapshotAccepted" && pulse.Active && !pulse.Zoning
                && pulse.Updates>0 && pulse.LastUpdateQpc>=requestedQpc && pulse.VitalsQpc>=requestedQpc
                && pulse.VitalsQpc>0 && pulse.LastUpdateQpc>0 && now-pulse.PublishedQpc<=2*Stopwatch.Frequency
                && now-pulse.LastUpdateQpc<=2*Stopwatch.Frequency && now-pulse.VitalsQpc<=2*Stopwatch.Frequency;
        internal static ClientPulse ReadStartupPulse(string path)
        {
            try {return ClientMonitor.ReadPulse(path);}
            catch(IOException) {return null;} catch(UnauthorizedAccessException) {return null;}
            catch(ArgumentException) {return null;} catch(InvalidOperationException) {return null;}
        }

        // Another AOSharp in this client? Checked every few seconds; logged once per finding.
        string CheckForeign(Row row, PluginHostClient.ClientState hosted, Track track, IEnumerable<string> ownFolders)
        {
            if ((DateTime.UtcNow - track.ForeignCheckedUtc).TotalSeconds >= ForeignCheckSeconds)
            {
                track.ForeignCheckedUtc = DateTime.UtcNow;
                bool busy; lock (gate) busy = injecting.Contains(row.Pid);
                bool managed = busy || hosted != null && (hosted.State == "Loaded" || hosted.State == "Injecting");
                track.Foreign = ForeignAOSharp.Check(row.Pid, row.StartTicks, ownFolders, managed);
            }
            var found = track.Foreign;
            if (found == null) { track.ForeignLogged = null; return null; }
            bool ours = hosted != null && hosted.State == "Loaded";
            row.State = ours ? "Conflict" : "External";
            row.Detail = found.Describe();
            string key = row.State + "|" + found.Folder + "|" + found.Controller;
            if (track.ForeignLogged == key) return null;
            track.ForeignLogged = key;
            return row.CharacterName + ": " + (ours
                ? string.Format(AOLauncher.Localization.Texts.Get("Text_another_AO_0_was_attached_to_9F95858B471B"),
                    found.Describe(), found.Controller ?? "AOSharp.exe")
                : string.Format(AOLauncher.Localization.Texts.Get("Text_another_AO_controls_this_client_0_59A4A87C45E6"), found.Describe()));
        }

        string Inject(LauncherSettings settings, Row row, PluginSet set, Track track, string style, bool verifyStartup = false)
        {
            var preparation = Stopwatch.StartNew();
            track.Attempts++; track.LastAttemptUtc = DateTime.UtcNow;
            string error;
            int revision = System.Threading.Volatile.Read(ref packRevision);
            track.MissingPackPath = settings.AOSharpPackPath ?? "";
            var current = Pack(settings, out error);
            if (current == null) { track.MissingPackRevision = revision; track.Finished = true; track.LastError = error; row.State = "Failed"; row.Detail = error; return row.CharacterName + ": " + AOLauncher.Localization.Texts.Get("Text_plugins_not_loaded_4C7BCB33CB0E") + error; }
            track.MissingPackRevision = null;
            string[] paths = PluginSets.Paths(settings, current, set, settings.IncludeHealthMonitor, style, settings.IncludeShortcuts, out var missing, out var skipped);
            if (paths.Length == 0) { track.Finished = true; track.LastError = AOLauncher.Localization.Texts.Get("Text_The_set_has_no_available_plugins_62FA8DB44FF6"); row.Detail = track.LastError; return null; }
            string monitor = AOSharpPack.MonitorPlugin();
            bool warmup = verifyStartup && settings.IncludeHealthMonitor && paths.Length > 1
                && paths.Any(path => string.Equals(path, monitor, StringComparison.OrdinalIgnoreCase));
            if (warmup) paths = new[] { monitor };
            PluginHostClient.Response response;
            preparation.Stop();
            var attach = Stopwatch.StartNew();
            lock (gate) injecting.Add(row.Pid);
            try { response = PluginHostClient.Send(new PluginHostClient.Request { Command = "inject", Pid = row.Pid, StartTicks = row.StartTicks, Label = warmup ? StartupLabel : set.Name, Plugins = paths }, current, true); }
            finally { attach.Stop(); lock (gate) injecting.Remove(row.Pid); }
            if (response != null && response.Ok)
            {
                track.Finished = true; track.LastError = null; row.State = "Loaded";
                Loaded(track, paths);
                track.StartupPending = warmup; track.StartupQpc = Stopwatch.GetTimestamp(); track.StartupFailed = false;
                if (warmup) return row.CharacterName + ": " + AOLauncher.Localization.Texts.Get("Text_Checking_AO_startup_before_loading_profession_37C08C255C8B");
                var playStyle = PlayStyles.Find(style);
                row.Detail = paths.Length + " plugins" + (playStyle.Id != PlayStyles.None ? " · " + playStyle.Name : "")
                    + (missing.Count > 0 ? " · " + AOLauncher.Localization.Texts.Get("Text_missing_20FEE4782E10") + string.Join(", ", missing) : "")
                    + (skipped.Count > 0 ? " · " + AOLauncher.Localization.Texts.Get("Text_left_out_other_suite_194CAC23B056") + string.Join(", ", skipped) : "");
                return row.CharacterName + ": " + string.Format(AOLauncher.Localization.Texts.Get("Text_requested_plugin_set_0_1_plugins_9AEC09C418C9"),
                    set.Name + (playStyle.Id != PlayStyles.None ? " + " + playStyle.Name : ""), paths.Length)
                    + string.Format(AOLauncher.Localization.Texts.Get("Text_Preparation_0_0_00_s_AO_9FF5AFFA21C6"), preparation.Elapsed.TotalSeconds, attach.Elapsed.TotalSeconds)
                    + (skipped.Count > 0 ? " " + AOLauncher.Localization.Texts.Get("Text_Warning_plugins_from_the_other_suite_88738CE6DBF3") + string.Join(", ", skipped.Select(id => id.Replace("pack:", ""))) : "");
            }
            track.LastError = response?.Error ?? AOLauncher.Localization.Texts.Get("Text_The_plugin_host_did_not_start_7A4CFD03E3DD");
            if (response?.State == "External") track.Finished = true;
            row.State = response?.State ?? "Failed"; row.Detail = track.LastError;
            bool last = track.Finished || track.Attempts >= MaximumAttempts;
            return last ? row.CharacterName + ": " + AOLauncher.Localization.Texts.Get("Text_plugins_not_loaded_4C7BCB33CB0E") + track.LastError : null;
        }

        static void Loaded(Track track, string[] paths)
        {
            track.Paths = paths; track.LoadedUtc = DateTime.UtcNow; track.HealthCheckedUtc = DateTime.MinValue;
            track.Checks = new List<PluginCheck>(); track.Logged.Clear();
            track.MonitorRetryAttempted = false; track.ReadyLogged = false;
        }

        // Automatic set, step 1: load only Nexus Shortcuts, which can tell the character's profession.
        string Probe(LauncherSettings settings, Row row, Track track)
        {
            track.Attempts++; track.LastAttemptUtc = DateTime.UtcNow;
            int revision = System.Threading.Volatile.Read(ref packRevision);
            track.MissingPackPath = settings.AOSharpPackPath ?? "";
            var current = Pack(settings, out string error);
            track.MissingPackRevision = current == null ? (int?)revision : null;
            var shortcuts = current == null ? null : AOSharpPack.AllPlugins(settings, current).FirstOrDefault(plugin => plugin.Id == PluginSet.ShortcutsId);
            if (shortcuts == null || !File.Exists(shortcuts.Path))
            {
                track.Finished = true;
                track.LastError = current == null ? error : AOLauncher.Localization.Texts.Get("Text_Nexus_Shortcuts_is_not_in_the_9B882351988D");
                return row.CharacterName + ": " + track.LastError;
            }
            PluginHostClient.Response response;
            lock (gate) injecting.Add(row.Pid);
            try { response = PluginHostClient.Send(new PluginHostClient.Request { Command = "inject", Pid = row.Pid, StartTicks = row.StartTicks, Label = "profession check", Plugins = new[] { shortcuts.Path } }, current, true); }
            finally { lock (gate) injecting.Remove(row.Pid); }
            if (response != null && response.Ok)
            {
                row.State = "Loaded"; Loaded(track, new[] { shortcuts.Path });
                track.Probing = true; track.ProfessionReported = false; track.ProbeStartedUtc = DateTime.UtcNow;
                return row.CharacterName + ": " + AOLauncher.Localization.Texts.Get("Text_no_plugin_set_chosen_yet_reading_53BA043AF343");
            }
            track.LastError = response?.Error ?? AOLauncher.Localization.Texts.Get("Text_The_plugin_host_did_not_start_7A4CFD03E3DD");
            if (response?.State == "External") track.Finished = true;
            row.State = response?.State ?? "Failed"; row.Detail = track.LastError;
            return track.Finished || track.Attempts >= MaximumAttempts ? row.CharacterName + ": " + AOLauncher.Localization.Texts.Get("Text_plugins_not_loaded_4C7BCB33CB0E") + track.LastError : null;
        }

        // Step 2: ask for the profession until the character is in the game.
        string AskProfession(Row row, Track track, AccountConfig account, CharacterConfig character)
        {
            if ((DateTime.UtcNow - track.ProbeStartedUtc).TotalSeconds > ProbeTimeoutSeconds)
            {
                track.Probing = false; track.Finished = true;
                track.LastError = track.ProfessionReported
                    ? AOLauncher.Localization.Texts.Get("Text_No_plugin_set_was_chosen_for_74A21916C2FE")
                    : AOLauncher.Localization.Texts.Get("Text_The_profession_could_not_be_read_A7B42FE72DD6");
                return row.CharacterName + ": " + track.LastError;
            }
            if (track.ProfessionReported) return null;
            var who = ShortcutClient.Who(row.Pid);
            if (who == null) return null;
            track.ProfessionReported = true;
            if (!PluginHealth.ProfessionKeys.ContainsKey(who.Item1))
            {
                track.Probing = false; track.Finished = true;
                track.LastError = string.Format(AOLauncher.Localization.Texts.Get("Text_there_is_no_starter_set_for_7CFC63BDF344"), who.Item1);
                return row.CharacterName + ": " + track.LastError;
            }
            ProfessionFound?.Invoke(account.Id, character.CharacterId, who.Item1);
            return null;
        }

        // Step 3: the character has a set now; the host swaps the plugin list in place.
        string FinishProbe(LauncherSettings settings, Row row, PluginSet set, Track track, string style)
        {
            track.Probing = false; track.Attempts = 0; track.Finished = false;
            return Inject(settings, row, set, track, style);
        }

        // Asks Nexus Shortcuts how the loaded plugins are doing; logs each new problem once.
        internal static bool CanRetryMissingMonitor(string[] paths, string monitorPath, bool automatic, bool includeMonitor, bool attempted, double ageSeconds, bool snapshotExists)
            => automatic && includeMonitor && !attempted && ageSeconds >= 30 && !snapshotExists && monitorPath != null
                && paths != null && paths.Length == 1 && string.Equals(paths[0], monitorPath, StringComparison.OrdinalIgnoreCase);

        List<string> CheckHealth(Row row, PluginHostClient.ClientState hosted, Track track, LauncherSettings settings, bool automatic)
        {
            var messages = new List<string>();
            var requested = track.Paths ?? hosted.Plugins;
            if (requested == null) return messages;
            if (track.LoadedUtc == DateTime.MinValue) track.LoadedUtc = DateTime.UtcNow;
            if ((DateTime.UtcNow - track.LoadedUtc).TotalSeconds < PluginHealth.SettleSeconds || (DateTime.UtcNow - track.HealthCheckedUtc).TotalSeconds < HealthEverySeconds) return messages;
            track.HealthCheckedUtc = DateTime.UtcNow;
            // Legacy AOSharp acknowledges dispatch, not Run(). Retry only our own
            // health-only load with no published snapshot; never restart a user's
            // handler suite, a deliberately stopped monitor, or stale measurements.
            string pulseFile = Path.Combine(ClientMonitor.PulseRoot, row.Pid + "-" + row.StartTicks + ".json");
            if (automatic && settings.IncludeHealthMonitor && !track.MonitorRetryAttempted
                && requested.Length == 1 && (DateTime.UtcNow - track.LoadedUtc).TotalSeconds >= 30 && !File.Exists(pulseFile)
                && CanRetryMissingMonitor(requested, AOSharpPack.MonitorPlugin(), automatic, settings.IncludeHealthMonitor,
                    track.MonitorRetryAttempted, (DateTime.UtcNow - track.LoadedUtc).TotalSeconds, File.Exists(pulseFile))) {
                track.MonitorRetryAttempted = true;
                var response = SendPlugins(row, settings, requested, hosted.Label);
                messages.Add(row.CharacterName + ": " + (response?.Ok == true
                    ? AOLauncher.Localization.Texts.Get("Text_No_monitoring_data_arrived_retried_the_86584337CBCD")
                    : AOLauncher.Localization.Texts.Get("Text_Health_monitor_retry_failed_953B531E1AF6") + (response?.Error ?? AOLauncher.Localization.Texts.Get("Text_No_reply_01A57684A1D6"))));
                DevelopmentRecorder.Write("monitor-startup-retry", new { Pid=row.Pid, StartTicks=row.StartTicks, Accepted=response?.Ok==true });
                return messages;
            }
            var confirmed = PluginRuntimeReport.Read(hosted, NameOf(settings));
            if (confirmed != null) track.Checks = confirmed;
            else if (!string.IsNullOrEmpty(hosted.RequestId))
                track.Checks = requested.Select(path => new PluginCheck { Path = path, Name = NameOf(settings)(path) ?? Path.GetFileNameWithoutExtension(path), Status = PluginStatus.Pending,
                    Message = (DateTime.UtcNow - track.LoadedUtc).TotalSeconds > 30 ? AOLauncher.Localization.Texts.Get("Text_No_startup_acknowledgement_received_Check_the_0A5EADEF103A") : null }).ToList();
            else
            {
                var report = requested.Any(path => string.Equals(Path.GetFileName(path), "NexusShortcuts.dll", StringComparison.OrdinalIgnoreCase)) ? ShortcutClient.Health(row.Pid) : null;
                if (report != null) track.Checks = PluginHealth.Evaluate(requested, report, NameOf(settings));
                else {
                    string monitor=AOSharpPack.MonitorPlugin();
                    if (!requested.Any(path=>string.Equals(path,monitor,StringComparison.OrdinalIgnoreCase))) return messages;
                    var pulse=ReadStartupPulse(pulseFile);
                    bool alive=StartupReady(pulse,row.Pid,row.StartTicks,track.StartupQpc,Stopwatch.GetTimestamp())
                        && (track.StartupInstance==null || pulse.Instance!=track.StartupInstance);
                    bool overdue=(DateTime.UtcNow-track.LoadedUtc).TotalSeconds>=30;
                    track.Checks=requested.Select(path=>new PluginCheck {Path=path,Name=NameOf(settings)(path)??Path.GetFileNameWithoutExtension(path),
                        Status=string.Equals(path,monitor,StringComparison.OrdinalIgnoreCase)?(alive?PluginStatus.Confirmed:overdue?PluginStatus.Failed:PluginStatus.Pending):PluginStatus.Pending,
                        Message=string.Equals(path,monitor,StringComparison.OrdinalIgnoreCase)?(alive?AOLauncher.Localization.Texts.Get("Text_Fresh_game_measurements_received_E46CB2C7B89C"):overdue?AOLauncher.Localization.Texts.Get("Text_No_fresh_monitoring_after_plugin_load_92DAF4A58CC0"):null)
                            :AOLauncher.Localization.Texts.Get("Text_This_AO_version_does_not_report_C0410D657802")}).ToList();
                }
            }
            foreach (var check in track.Checks.Where(item => item.Problem))
                if (track.Logged.Add(check.Name + "|" + check.Status))
                    messages.Add(row.CharacterName + ": " + check.Line());
            return messages;
        }

        // Display names for plugin files, from the pack's catalog.
        internal Func<string, string> NameOf(LauncherSettings settings)
        {
            AOSharpPack current; lock (packGate) current = pack;
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var plugin in AOSharpPack.AllPlugins(settings, current))
                if (!string.IsNullOrEmpty(plugin.Path) && !names.ContainsKey(plugin.Path)) names[plugin.Path] = plugin.Name;
            return path => path != null && names.TryGetValue(path, out string name) ? name : null;
        }

        // ---- Plugin test (Plugins page): load batches into one client, then restore its set ----

        internal void SetTesting(Row row, bool testing)
        {
            lock (gate) if (tracks.TryGetValue(row.Pid + "-" + row.StartTicks, out var track)) track.Testing = testing;
        }

        internal PluginHostClient.Response SendPlugins(Row row, LauncherSettings settings, string[] paths, string label)
        {
            var current = Pack(settings, out string error);
            if (current == null) return new PluginHostClient.Response { Ok = false, Error = error };
            lock (gate) injecting.Add(row.Pid);
            try { return PluginHostClient.Send(new PluginHostClient.Request { Command = "inject", Pid = row.Pid, StartTicks = row.StartTicks, Label = label, Plugins = paths }, current, true); }
            finally { lock (gate) injecting.Remove(row.Pid); }
        }

        static string Describe(Row row, LauncherSettings settings, Track track, bool automatic)
        {
            if (track.StartupFailed) return AOLauncher.Localization.Texts.Get("Text_Startup_failed_CEE8D83F83EF");
            if (track.StartupPending) return AOLauncher.Localization.Texts.Get("Text_Checking_AO_startup_4D2B2ABDCABC");
            if (track.Probing && (row.State == "Loaded" || row.State == null)) return AOLauncher.Localization.Texts.Get("Text_Finding_the_profession_C8AAEA4CD7C5");
            switch (row.State)
            {
                case "Loaded": return track.Testing ? AOLauncher.Localization.Texts.Get("Text_Testing_plugins_BF0C49A2E948") : (track.Checks.Any(check => check.Status == PluginStatus.Failed) ? AOLauncher.Localization.Texts.Get("Text_Startup_failed_CEE8D83F83EF") : track.Checks.Any(check => check.Status == PluginStatus.Confirmed) && !track.Checks.Any(check => check.Status == PluginStatus.Pending) ? AOLauncher.Localization.Texts.Get("Text_Startup_confirmed_F4EEBA62C5B1") : AOLauncher.Localization.Texts.Get("Text_Load_requested_startup_unconfirmed_909527DAC825"));
                case "Injecting": return AOLauncher.Localization.Texts.Get("Text_Loading_51259B26F1F7");
                case "Ejected": return AOLauncher.Localization.Texts.Get("Text_Unloaded_23C6EB842740");
                case "Disconnected": return AOLauncher.Localization.Texts.Get("Text_Stopped_3C340B8E0600");
                case "External": return AOLauncher.Localization.Texts.Get("Text_Controlled_by_another_AO_6415FD4FFB92");
                case "Conflict": return AOLauncher.Localization.Texts.Get("Text_Two_AO_instances_attached_D74BD0A746A2");
                case "Failed": return AOLauncher.Localization.Texts.Get("Text_Failed_BCA3241B03D6");
                case "TeamOff": return AOLauncher.Localization.Texts.Get("Text_Off_for_this_team_35CE7FDF0DF1");
            }
            if (row.SetId == null && automatic) return settings.AutoInjectPlugins ? AOLauncher.Localization.Texts.Get("Text_Picks_a_set_by_profession_90A6F6692C34") : AOLauncher.Localization.Texts.Get("Text_Automatic_auto_load_is_off_E5ADF6603940");
            if (row.SetId == null) return AOLauncher.Localization.Texts.Get("Text_No_plugins_2445FC9071A5");
            if (!settings.AutoInjectPlugins) return AOLauncher.Localization.Texts.Get("Text_Not_loaded_92C64013E2BC");
            if (track.Attempts >= MaximumAttempts || track.Finished) return AOLauncher.Localization.Texts.Get("Text_Failed_BCA3241B03D6");
            return AOLauncher.Localization.Texts.Get("Text_Waiting_for_the_game_ED1ECEB363BE");
        }

        // Manual load from the Plugins window; uses that window's (possibly unsaved) sets.
        internal string LoadNow(Row row, LauncherSettings settings)
        {
            if (settings.AOSharpDisabled) return AOLauncher.Localization.Texts.Get("Text_Enable_AO_in_Plugins_first_FECCE861C809");
            var account = settings.Accounts.FirstOrDefault(item => item.Id == row.AccountId);
            var character = account?.Characters.FirstOrDefault(item => item.CharacterId == row.CharacterId);
            if (character == null) return AOLauncher.Localization.Texts.Get("Text_Character_not_found_32ECC4788012");
            string presetId = LaunchPresetFor(account.Id, character.CharacterId, row.StartTicks);
            var preset = settings.Presets.FirstOrDefault(item => item.Id == presetId);
            var set = PluginSets.Find(settings, PluginSets.Resolve(settings, account, character, preset));
            if (set == null) return AOLauncher.Localization.Texts.Get("Text_Choose_a_plugin_set_for_this_DF6669D6BCFF");
            var track = new Track();
            string message = Inject(settings, row, set, track, PlayStyles.For(settings, preset));
            lock (gate) { string key = row.Pid + "-" + row.StartTicks; tracks[key] = track; track.Finished = true; }
            return row.State == "Loaded" && track.LastError == null ? null : (track.LastError ?? message);
        }

        internal string UnloadNow(Row row)
        {
            var response = PluginHostClient.Send(new PluginHostClient.Request { Command = "eject", Pid = row.Pid, StartTicks = row.StartTicks }, null, false, 5000);
            if (response == null) return AOLauncher.Localization.Texts.Get("Text_Nexus_has_not_loaded_plugins_into_822544BF2F8F");
            lock (gate) { if (tracks.TryGetValue(row.Pid + "-" + row.StartTicks, out var track)) track.Finished = true; }
            return response.Ok ? null : response.Error;
        }
    }
}
