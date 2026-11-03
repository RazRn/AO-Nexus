using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using static AOLauncher.Localization.Texts;
using static AOLauncher.SidebarText;
using Timer = System.Windows.Forms.Timer;

namespace AOLauncher
{
    // AOSharp plugins (Plugins window, automatic loading) and server-rule updates.
    internal sealed partial class MainForm
    {
        readonly PluginAutoInjector pluginInjector = new PluginAutoInjector();
        readonly TeamRecoveryController teamRecovery = new TeamRecoveryController();
        readonly Timer pluginTimer = new Timer { Interval = 1000 };
        readonly Timer teamTimer = new Timer { Interval = 250 };
        readonly Nexus.Core.StartupPolling startupPolling = new Nexus.Core.StartupPolling();
        bool pluginsDisposed, serverRulesBusy;

        void InitializePlugins()
        {
            ServerRules.Install();
            if (smoke || storageBlocked) return;
            pluginTimer.Tick += async (sender, e) => { await PollPlugins(); };
            teamTimer.Tick += async (sender, e) => {
                teamTimer.Interval = autoTeamStartup?.Active == true ? 250 : 1000;
                await PollTeams();
            };
            teamTimer.Start();
            pluginTimer.Start();
            // Start only after the main window is visible. This prepares files, never a host
            // or game process. Locate's existing lock/cache is shared with first-page loading.
            Shown += (sender, e) => BeginInvoke((Action)(() =>
            {
                if (pluginsDisposed || closing || IsDisposed) return;
                var snapshot = new LauncherSettings { AOSharpPackPath = settings.AOSharpPackPath, AOSharpDisabled = settings.AOSharpDisabled };
                _ = Task.Run(() =>
                {
                    try { AOSharpPack.MonitorPlugin(); } catch (Exception ex) { DiagnosticJournal.Record("plugins", "Monitor preparation failed: " + ex.Message); }
                    try { AOSharpPack.Locate(snapshot, out _); } catch (Exception ex) { DiagnosticJournal.Record("plugins", "Pack preparation failed: " + ex.Message); }
                });
            }));
            pluginInjector.ProfessionFound += (accountId, characterId, profession) =>
            {
                if (pluginsDisposed || closing || IsDisposed) return;
                try { BeginInvoke((Action)(() => AssignSetByProfession(accountId, characterId, profession))); } catch (InvalidOperationException) { }
            };
            FormClosed += (sender, e) => { pluginsDisposed = true; pluginTimer.Stop(); pluginTimer.Dispose(); teamTimer.Stop(); teamTimer.Dispose(); };
        }

        Task PollPlugins() => startupPolling.Plugins(PollPluginPass);
        async Task PollPluginPass()
        {
            if (pluginsDisposed || closing) return;
            try
            {
                var serializer = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
                var snapshot = serializer.Deserialize<LauncherSettings>(serializer.Serialize(settings));
                var messages = await Task.Run(() => pluginInjector.Poll(snapshot));
                if (pluginsDisposed || IsDisposed) return;
                foreach (string message in messages) AppendLog(message);
                SyncPluginControls();
                FailureDiagnostics.Plugins.Recovered("plugin-poll", "Polling resumed");
            }
            catch (Exception ex) { FailureDiagnostics.Plugins.Report("plugin-poll", "Polling deferred until next tick", ex); }

        }

        Task PollTeams() => startupPolling.Teams(PollTeamPass);
        async Task PollTeamPass()
        {
            if (pluginsDisposed || closing) return;
            try {
                var serializer = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
                var snapshot = serializer.Deserialize<LauncherSettings>(serializer.Serialize(settings));
                var rows = pluginInjector.Rows;
                var formation = autoTeamStartup;
                bool cancel = stopRequested;
                var messages = await Task.Run(() => {
                    var result = new System.Collections.Generic.List<string>();
                    if (formation != null) {
                        if (cancel) formation.Cancel(rows);
                        else result.AddRange(formation.Poll(snapshot, rows));
                    }
                    result.AddRange(teamRecovery.Poll(snapshot, rows));
                    return result;
                });
                if (pluginsDisposed || IsDisposed) return;
                foreach (var message in messages) AppendLog(message);
                FailureDiagnostics.Plugins.Recovered("team-poll", "Team polling resumed");
            }
            catch (Exception ex) { FailureDiagnostics.Plugins.Report("team-poll", "Team polling deferred until next tick", ex); }
        }

        // An automatic character is in the game and its profession is known: give it that profession's
        // set (creating the starter set if it was deleted). The injector loads it on its next poll.
        void AssignSetByProfession(string accountId, int characterId, string profession)
        {
            if (closing || IsDisposed) return;
            var account = settings.Accounts.FirstOrDefault(item => item.Id == accountId);
            var character = account?.Characters.FirstOrDefault(item => item.CharacterId == characterId);
            if (character == null || !string.IsNullOrEmpty(character.PluginSetId)) return;
            var pack = pluginInjector.Pack(settings, out _);
            var set = PluginSets.ForProfession(settings, pack, profession, out bool created);
            if (set == null)
            {
                AppendLog(string.Format(AOLauncher.Localization.Texts.Get("Text_0_no_plugin_set_fits_the_4E1A03E055BB"), character.Name, PluginSets.ProfessionName(profession)));
                return;
            }
            character.PluginSetId = set.Id;
            settings.PluginSetsInitialized = true;
            SaveSettings();
            tree.Invalidate();
            pluginsView?.AdoptAutomaticSet(accountId, characterId, set);
            AppendLog(string.Format(AOLauncher.Localization.Texts.Get("Text_0_is_a_1_it_now_A10D82194B30"),
                character.Name, PluginSets.ProfessionName(profession), set.Name, created ? AOLauncher.Localization.Texts.Get("Text_created_4679DB7F51CA") : ""));
        }

        void MigrateSetsToZ(AOSharpPack pack)
        {
            if (closing || IsDisposed || settings.PluginSuiteVersion >= 1) return;
            int moved = PluginSets.MigrateToZ(settings, pack);
            if (settings.PluginSuiteVersion < 1) return;
            SaveSettings();
            if (moved > 0)
                AppendLog(string.Format(AOLauncher.Localization.Texts.Get("Text_Plugins_0_starter_sets_now_use_BE9729DB11D3"), moved));
        }

        // The team preset that launched a character decides its plugin-set override.
        void RecordLaunchPresets(IEnumerable<AccountConfig> accounts)
        {
            var preset = settings.Presets.FirstOrDefault(item => item.Id == settings.SelectedPresetId);
            bool presetActive = preset != null && PresetService.Matches(settings, preset);
            foreach (var account in accounts)
                foreach (var character in account.Characters.Where(item => item.Selected))
                    pluginInjector.RecordLaunch(account.Id, character.CharacterId, presetActive ? preset.Id : null);
        }

        void ApplyPluginSettings(LauncherSettings working)
        {
            bool packChanged = settings.AOSharpDisabled != working.AOSharpDisabled || !string.Equals(settings.AOSharpPackPath ?? "", working.AOSharpPackPath ?? "", StringComparison.OrdinalIgnoreCase);
            settings.AOSharpDisabled = working.AOSharpDisabled;
            settings.PluginSets = working.PluginSets;
            settings.SharedPlugins = working.SharedPlugins;
            settings.CustomPlugins = working.CustomPlugins;
            settings.AutoInjectPlugins = working.AutoInjectPlugins;
            settings.IncludeHealthMonitor = working.IncludeHealthMonitor;
            settings.IncludeShortcuts = working.IncludeShortcuts;
            settings.DefaultPlayStyle = working.DefaultPlayStyle;
            settings.PluginSuiteVersion = Math.Max(settings.PluginSuiteVersion, working.PluginSuiteVersion);
            settings.AOSharpPackPath = working.AOSharpPackPath;
            settings.PluginSetsInitialized = true;
            foreach (var account in settings.Accounts)
            {
                var copy = working.Accounts.FirstOrDefault(item => item.Id == account.Id);
                if (copy == null) continue;
                foreach (var character in account.Characters)
                    character.PluginSetId = copy.Characters.FirstOrDefault(item => item.CharacterId == character.CharacterId)?.PluginSetId;
            }
            foreach (var preset in settings.Presets)
            {
                var copy = working.Presets.FirstOrDefault(item => item.Id == preset.Id);
                if (copy == null) continue;
                preset.LoadPlugins = copy.LoadPlugins;
                preset.PlayStyle = copy.PlayStyle;
                foreach (var reference in preset.Characters)
                    reference.PluginSetId = copy.Characters.FirstOrDefault(item => item.CharacterId == reference.CharacterId
                        && string.Equals(item.AccountId, reference.AccountId, StringComparison.OrdinalIgnoreCase))?.PluginSetId;
            }
            if (packChanged) pluginInjector.ForgetPack();
            SaveSettings();
            tree?.Invalidate();
            SyncPluginControls();
            AppendLog(settings.AutoInjectPlugins
                ? AOLauncher.Localization.Texts.Get("Text_Plugin_settings_saved_Plugins_load_automatically_DD8A4437D47B")
                : AOLauncher.Localization.Texts.Get("Text_Plugin_settings_saved_Automatic_loading_is_8B71ED7FDD43"));
        }

        // For the diagnostic ZIP: what is ticked, used and set up right now (redacted on export).
        string[] AppStateSnapshot()
        {
            List<PresetCamping.Client> sessions;
            try { sessions = LoginPreflight.Read(); } catch { sessions = null; }
            var extras = new AppStateReport.Extras
            {
                Section = currentSection, LoginRunning = running, Language = System.Globalization.CultureInfo.CurrentUICulture.Name,
                DisplayScale = DeviceDpi / 96f, PluginRows = pluginInjector.Rows, Sessions = sessions
            };
            return new[] { AppStateReport.Text(settings, extras), AppStateReport.SettingsJson(settings) };
        }

        // ---------- Server rules (config/servers.json) ----------

        async void CheckServerRulesAtStartup()
        {
            if (smoke || storageBlocked || !settings.CheckServerRulesOnStartup || serverRulesBusy) return;
            serverRulesBusy = true;
            try
            {
                ServerCatalog catalog;
                try
                {
                    var snapshot = settings;
                    using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
                        catalog = await ServerRules.FetchAsync(snapshot, timeout.Token);
                }
                catch (Exception ex)
                {
                    DiagnosticJournal.Record("server-rules", "Startup check failed: " + ex.Message);
                    if (ServerRules.Current.Revision > settings.ServerRulesRevision) ApplyServerRules(ServerRules.Current, false);
                    return;
                }
                if (IsDisposed || closing) return;
                if (catalog.Revision > settings.ServerRulesRevision) ApplyServerRules(catalog, false);
            }
            finally { serverRulesBusy = false; }
        }

        // Applies a server list to the saved login servers. Limits, names and new servers apply
        // directly; address changes are shown first because they decide where passwords go.
        internal bool ApplyServerRules(ServerCatalog catalog, bool interactive, IWin32Window owner = null)
        {
            if (catalog.Revision < settings.ServerRulesRevision && !interactive) return false;
            ServerRules.Adopt(catalog);
            var plan = ServerRules.Plan(settings.LoginServers, catalog, settings.RemovedServerIds);
            if (plan.AddressChanges.Count > 0)
            {
                string question = AOLauncher.Localization.Texts.Get("Text_The_server_list_changes_these_login_680BE6A476AB") + "\r\n\r\n" + string.Join("\r\n", plan.AddressChanges)
                    + "\r\n\r\n" + AOLauncher.Localization.Texts.Get("Text_Accounts_on_these_servers_will_ask_82B98C0FE08B");
                if (NexusMessageBox.Show(owner ?? this, question, AOLauncher.Localization.Texts.Get("Text_Server_rules_update_546588B6568B"), MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                {
                    // Keep the current addresses; mark those servers as the user's own choice.
                    foreach (var profile in plan.Profiles)
                    {
                        var old = settings.LoginServers.FirstOrDefault(item => item.Id == profile.Id);
                        if (old != null && (old.Server != profile.Server || old.Port != profile.Port)) { profile.Server = old.Server; profile.Port = old.Port; profile.Customized = true; }
                    }
                    plan.AddressChanges.Clear();
                }
            }
            if (plan.HasChanges)
            {
                var previousTargets = settings.Accounts.ToDictionary(account => account.Id, AccountTargets.Endpoint);
                try { AccountTargets.ChangeProfiles(settings, plan.Profiles); }
                catch (ArgumentException ex) { AppendLog(AOLauncher.Localization.Texts.Get("Text_Server_rules_could_not_be_applied_5149D0A16EB0") + ex.Message); return false; }
                foreach (var account in settings.Accounts)
                    if (previousTargets[account.Id] != AccountTargets.Endpoint(account)) { passwords.Remove(account.Id); DeleteSavedPassword(account); }
                foreach (string change in plan.Changes.Concat(plan.AddressChanges)) AppendLog(AOLauncher.Localization.Texts.Get("Text_Server_rules_C9B335C85AAB") + change);
            }
            settings.ServerRulesRevision = Math.Max(settings.ServerRulesRevision, catalog.Revision);
            SaveSettings();
            RefreshTree(tree.SelectedNode?.Tag);
            if (serverSidebar != null) ConfigureSidebar();
            AppendLog(plan.HasChanges
                ? string.Format(AOLauncher.Localization.Texts.Get("Text_Server_rules_updated_to_revision_0_A7BEBD1E1919"), catalog.Revision)
                : string.Format(AOLauncher.Localization.Texts.Get("Text_Server_rules_checked_revision_0_no_6963C53C374B"), catalog.Revision));
            return plan.HasChanges;
        }
    }
}
