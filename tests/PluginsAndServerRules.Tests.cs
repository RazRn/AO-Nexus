using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;

namespace AOLauncher
{
    // Compiled together with the production launcher sources (entry point: -main:AOLauncher.PluginsAndServerRulesTests).
    // Offline only: no game, no injection, no network. Optional args:
    //   --pack <folder laid out like AO#\dist>   --aosharp-config <AOSharp config.json>   --payload <folder with AOSharpPack.zip + manifest>
    internal static class PluginsAndServerRulesTests
    {
        static int checks, failures;
        static void Check(bool condition, string name)
        {
            checks++;
            if (!condition) { failures++; Console.WriteLine("FAIL " + name); }
        }

        [STAThread]
        public static int Main(string[] args)
        {
            Localization.Texts.SetLanguage("en");
            string packFolder = Arg(args, "--pack"), aosharpConfig = Arg(args, "--aosharp-config"), payload = Arg(args, "--payload");
            ServerRules.Install();
            ServerRulesTests();
            PluginSetTests(packFolder, aosharpConfig);
            ActivityLogTests();
            PreflightTests();
            MergeReviewTests(packFolder);
            ShortcutTimeoutTests();
            LogToneTests();
            AppStateTests();
            ReviewFindingTests();
            SuiteAndPlayStyleTests();
            StartupPolicyTests(packFolder);
            if (payload != null)
            {
                try { PackExtractionTests(payload); }
                catch (NotImplementedException) { Console.WriteLine("SKIP pack extraction (needs Windows identity APIs)"); }
            }
            Console.WriteLine(checks + " checks, " + failures + " failures");
            return failures == 0 ? 0 : 1;
        }

        static void ShortcutTimeoutTests()
        {
            foreach (string reply in new[] { "OK form raid\n", "OK form", "" })
            {
                // Negative synthetic ID cannot name a real game process.
                int pid = -new Random(Guid.NewGuid().GetHashCode()).Next(1, int.MaxValue);
                using (var server = new System.IO.Pipes.NamedPipeServerStream(ShortcutClient.PipeName(pid), System.IO.Pipes.PipeDirection.InOut, 1,
                    System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous))
                using (var release = new ManualResetEvent(false))
                {
                    var connection = server.BeginWaitForConnection(null, null);
                    var worker = System.Threading.Tasks.Task.Run(() =>
                    {
                        server.EndWaitForConnection(connection);
                        var request = new byte[64]; server.Read(request, 0, request.Length);
                        foreach (byte value in System.Text.Encoding.UTF8.GetBytes(reply)) { server.WriteByte(value); server.Flush(); }
                        release.WaitOne(5000);
                    });
                    var elapsed = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        var commands = ShortcutClient.Commands(pid);
                        Check(reply.EndsWith("\n") ? commands != null && commands.SetEquals(new[] { "form", "raid" }) : commands == null,
                            "shortcut exchange requires a complete reply (length " + reply.Length + ")");
                        Check(elapsed.ElapsedMilliseconds < 4000, "silent or partially responding shortcut pipe has a bounded read");
                    }
                    finally { release.Set(); if (!worker.Wait(6000)) throw new Exception("Synthetic shortcut server did not stop."); }
                }
            }
        }

        static string Arg(string[] args, string name)
        {
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        static void MergeReviewTests(string packFolder)
        {
            string directory = Path.Combine(Path.GetTempPath(), "nexus-merge-review-" + Guid.NewGuid().ToString("N"));
            string previous = PluginAutoInjector.LaunchContextPath;
            PluginAutoInjector.LaunchContextPath = Path.Combine(directory, "LaunchContext.json");
            try
            {
                var injector = new PluginAutoInjector();
                injector.RecordLaunch("account", 123, "team-with-plugins-off");
                long existingStart = DateTime.UtcNow.Ticks;
                Thread.Sleep(20);
                injector.RecordLaunch("account", 123, "other-team");
                Check(injector.LaunchPresetFor("account", 123, existingStart) == "team-with-plugins-off", "duplicate login does not change the running client's plugin policy");
                Check(new PluginAutoInjector().LaunchPresetFor("account", 123, existingStart) == "team-with-plugins-off", "running client's old context survives launcher restart");
                Check(new PluginAutoInjector().LaunchPresetFor("account", 123, DateTime.UtcNow.AddSeconds(1).Ticks) == "other-team", "a new process gets the newly selected team");
                if (packFolder != null)
                {
                    var set = new PluginSet { Name = "empty" };
                    var character = new CharacterConfig { CharacterId = 456, PluginSetId = set.Id };
                    var account = new AccountConfig { Id = "empty-set-account", Characters = new List<CharacterConfig> { character } };
                    var settings = new LauncherSettings { AOSharpPackPath = packFolder, IncludeShortcuts = false, IncludeHealthMonitor = false,
                        Accounts = new List<AccountConfig> { account }, PluginSets = new List<PluginSet> { set } };
                    var row = new PluginAutoInjector.Row { AccountId = account.Id, CharacterId = 456, State = "Loaded", StartTicks = DateTime.UtcNow.Ticks };
                    Check(new PluginAutoInjector().LoadNow(row, settings) != null, "empty restore set cannot report success merely because the previous set was loaded");
                }
            }
            finally
            {
                PluginAutoInjector.LaunchContextPath = previous;
                string resolved = Path.GetFullPath(directory);
                if (!resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("nexus-merge-review-"))
                    throw new IOException("Unexpected temporary test path.");
                if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
            }
            var start = DateTime.Now;
            var pending = new LoginPreflight.StuckClient { StartedLocal = start, ExecutablePath = @"C:\Game\AnarchyOnline.exe" };
            Check(LoginPreflight.CanClose(pending, start, pending.ExecutablePath, "Anarchy Online", false), "unchanged abandoned login can close");
            Check(!LoginPreflight.CanClose(pending, start, pending.ExecutablePath, "Anarchy Online - Test", false), "client that entered the world cannot be closed as stale");
            Check(!LoginPreflight.CanClose(pending, start.AddSeconds(1), pending.ExecutablePath, "Anarchy Online", false), "reused process ID cannot be closed");
            Check(!LoginPreflight.CanClose(pending, start, @"C:\Other\AnarchyOnline.exe", "Anarchy Online", false), "different executable cannot be closed");
            Check(!LoginPreflight.CanClose(pending, start, pending.ExecutablePath, "Anarchy Online", true), "newly focused login needs approval");
            pending.CloseApproved = true;
            Check(LoginPreflight.CanClose(pending, start, pending.ExecutablePath, "Anarchy Online", true), "approved foreground login can close");
            Check(!LoginPreflight.CanClose(pending, start, pending.ExecutablePath, "Anarchy Online - Test", true), "approval does not permit closing a client that entered the world");
            Check(PluginAutoInjector.CanAdvanceProbe(true, false, false), "automatic profession lookup can advance when enabled");
            Check(!PluginAutoInjector.CanAdvanceProbe(false, false, false), "turning auto-load off stops a pending profession probe from loading the set");
            Check(!PluginAutoInjector.CanAdvanceProbe(true, true, false), "team opt-out stops a pending probe");
            Check(!PluginAutoInjector.CanAdvanceProbe(true, false, true), "automatic loading does not overwrite a plugin test batch");
        }

        static string CatalogJson(int revision, string rebirthHost = "2.24.96.30", int rebirthChars = 0, string extra = "")
            => "{\"schema\":\"AONexus.ServerRules\",\"schemaVersion\":1,\"revision\":" + revision + ",\"servers\":["
             + "{\"id\":\"rebirth\",\"name\":\"AO Rebirth - Live\",\"kind\":\"rebirth\",\"host\":\"" + rebirthHost + "\",\"port\":7500,\"maxAccountsOnline\":0,\"maxCharactersOnlinePerAccount\":" + rebirthChars + ",\"characterSlots\":6,\"health\":{\"zone\":{\"port\":7501},\"chat\":{\"port\":7012}}},"
             + "{\"id\":\"prk\",\"name\":\"Project Rubi-Ka - Ithaca\",\"kind\":\"prk\",\"host\":\"5.161.243.214\",\"port\":7000,\"maxAccountsOnline\":2,\"maxCharactersOnlinePerAccount\":1,\"characterSlots\":0}" + extra + "]}";

        static void ServerRulesTests()
        {
            var embedded = ServerRules.Embedded();
            Check(embedded.Revision >= 1 && embedded.Servers.Count == 5, "built-in list has the five known servers");
            var defaults = LoginServerProfile.Defaults();
            Check(defaults.Select(p => p.Id).SequenceEqual(new[] { "rebirth", "live", "live-2019", "prk", "rebirth-local" }), "defaults keep the original ids and order");
            var rebirth = defaults.First(p => p.Id == "rebirth");
            Check(rebirth.Server == "2.24.96.30" && rebirth.Port == 7500 && rebirth.CharacterSlots == 6 && rebirth.MaxAccounts == 0, "rebirth defaults unchanged");
            Check(defaults.First(p => p.Id == "prk").MaxAccounts == 2 && defaults.First(p => p.Id == "prk").MaxCharactersPerAccount == 1, "PRK limits unchanged");
            Check(defaults.First(p => p.Id == "rebirth-local").CharacterSlots == 0, "local development has no slot limit");
            Check(ServerAccountPolicy.DefaultLimit("5.161.243.214", 7000) == 2 && ServerAccountPolicy.DefaultCharacterLimit("37.18.192.41", 7506) == 1
                && ServerAccountPolicy.DefaultLimit("10.0.0.1", 7000) == 0, "account policy reads limits from the list");
            Check(AccountTargets.DefaultCharacterSlots("2.24.96.30", 7500) == 6 && AccountTargets.DefaultCharacterSlots("1.2.3.4", 7500) == 0, "character slots read from the list");
            Check(AccountTargets.IsOfficialLive(new AccountConfig { Server = "37.18.192.41", Port = 7506 }) && AccountTargets.IsProjectRubika(new AccountConfig { Server = "5.161.243.214", Port = 7000 })
                && !AccountTargets.IsOfficialLive(new AccountConfig { Server = "2.24.96.30", Port = 7500 }), "server kinds come from the list");
            var health = HealthEndpoints.Defaults(rebirth);
            Check(health.Zone.Port == 7501 && health.Chat.Port == 7012 && health.Zone.Host == "2.24.96.30" && health.Login.Port == 7500, "rebirth health endpoints");
            var live2019 = HealthEndpoints.Defaults(defaults.First(p => p.Id == "live-2019"));
            Check(live2019.Chat.Host == "chat.d1.funcom.com" && live2019.Chat.Port == 7106 && !live2019.Zone.Enabled, "live 2019 chat endpoint");
            Check(ServerHealth.Kind(rebirth) == "rebirth" && ServerHealth.Kind(new LoginServerProfile { Server = "9.9.9.9", Port = 1 }) == "custom", "health kinds");

            // Plans
            var same = ServerRules.Plan(defaults, embedded, new List<string>());
            Check(!same.HasChanges, "identical list makes no changes");
            var limits = ServerRules.Plan(defaults, ServerRules.Parse(CatalogJson(2, rebirthChars: 3), "test"), new List<string>());
            Check(limits.Changes.Count == 1 && limits.AddressChanges.Count == 0 && limits.Profiles.First(p => p.Id == "rebirth").MaxCharactersPerAccount == 3, "limit change applies without address change");
            var moved = ServerRules.Plan(defaults, ServerRules.Parse(CatalogJson(3, rebirthHost: "2.24.96.31"), "test"), new List<string>());
            Check(moved.AddressChanges.Count == 1 && moved.Profiles.First(p => p.Id == "rebirth").Server == "2.24.96.31", "address change is reported separately");
            var customized = defaults.Select(ServerRules.Copy).ToList();
            customized.First(p => p.Id == "rebirth").MaxCharactersPerAccount = 5;
            ServerRules.MarkCustomized(customized, embedded);
            Check(customized.First(p => p.Id == "rebirth").Customized && !customized.First(p => p.Id == "prk").Customized, "edited server is marked customized");
            var kept = ServerRules.Plan(customized, ServerRules.Parse(CatalogJson(4, rebirthHost: "2.24.96.31"), "test"), new List<string>());
            Check(!kept.HasChanges && kept.Profiles.First(p => p.Id == "rebirth").Server == "2.24.96.30", "customized server is left alone");
            var added = ServerRules.Plan(defaults, ServerRules.Parse(CatalogJson(5, extra: ",{\"id\":\"new-shard\",\"name\":\"New shard\",\"host\":\"10.1.2.3\",\"port\":7100}"), "test"), new List<string>());
            Check(added.Profiles.Any(p => p.Id == "new-shard" && p.Server == "10.1.2.3") && added.Changes.Any(c => c.Contains("New shard")), "new server is added");
            var removed = ServerRules.Plan(defaults.Where(p => p.Id != "prk").ToList(), embedded, new List<string> { "prk" });
            Check(!removed.Profiles.Any(p => p.Id == "prk"), "removed server is not added back");
            AccountTargets.ValidateProfiles(added.Profiles);
            Check(true, "planned profiles validate");

            // Validation
            foreach (var bad in new[] {
                CatalogJson(1).Replace("AONexus.ServerRules", "Other"),
                CatalogJson(0),
                CatalogJson(1, rebirthHost: "not-an-ip"),
                CatalogJson(1, rebirthChars: 101),
                CatalogJson(1, extra: ",{\"id\":\"rebirth\",\"name\":\"Dup\",\"host\":\"1.1.1.1\",\"port\":1}"),
                CatalogJson(1, extra: ",{\"id\":\"x y\",\"name\":\"Bad id\",\"host\":\"1.1.1.1\",\"port\":1}"),
                CatalogJson(1, extra: ",{\"id\":\"p\",\"name\":\"Bad port\",\"host\":\"1.1.1.1\",\"port\":70000}"),
                "{\"schema\":\"AONexus.ServerRules\",\"schemaVersion\":2,\"revision\":1,\"servers\":[]}",
                new string(' ', 300 * 1024) })
            {
                bool rejected = false;
                try { ServerRules.Parse(bad, "test"); } catch (InvalidDataException) { rejected = true; } catch (ArgumentException) { rejected = true; }
                Check(rejected, "invalid list rejected: " + bad.Substring(0, Math.Min(60, bad.Length)).Trim());
            }

            // Settings round trip keeps the new fields.
            var settings = new LauncherSettings { ServerRulesRevision = 7, ServerRulesSource = @"C:\x\servers.json", RemovedServerIds = new List<string> { "prk" }, AutoInjectPlugins = true };
            settings.PluginSets.Add(new PluginSet { Name = "Doctor", Plugins = new List<string> { "pack:ProfessionHandler.Doc" } });
            var json = new JavaScriptSerializer().Serialize(settings);
            var back = SettingsStore.Deserialize(json);
            Check(back.ServerRulesRevision == 7 && back.RemovedServerIds.SequenceEqual(new[] { "prk" }) && back.AutoInjectPlugins && back.PluginSets.Single().Plugins.Single() == "pack:ProfessionHandler.Doc", "settings round trip");
            var old = SettingsStore.Deserialize("{\"Version\":2,\"Accounts\":[]}");
            var oldTeam = SettingsStore.Deserialize("{\"Version\":2,\"Accounts\":[],\"Presets\":[{\"Id\":\"p1\",\"Name\":\"Team\",\"Characters\":[]}]}");
            Check(oldTeam.Presets.Single().LoadPlugins, "existing team presets load plugins by default");
            Check(old.IncludeHealthMonitor, "Nexus Health Monitor is included with plugin sets by default");
            Check(old.CheckServerRulesOnStartup && old.PluginSets.Count == 0 && old.RemovedServerIds.Count == 0 && !old.AutoInjectPlugins, "older settings get safe defaults");
        }

        static LauncherSettings TeamSettings()
        {
            var settings = new LauncherSettings();
            var account = new AccountConfig { Id = "a1", Label = "Main", Username = "user" };
            foreach (var name in new[] { "Wazoo", "Sexologist", "Fatale", "Calm", "Overwhelming", "Geek", "Nobody" })
                account.Characters.Add(new CharacterConfig { Name = name, CharacterId = 1000 + account.Characters.Count });
            settings.Accounts.Add(account);
            return settings;
        }

        static void PluginSetTests(string packFolder, string aosharpConfig)
        {
            var settings = TeamSettings();
            var account = settings.Accounts[0];
            var doc = new PluginSet { Name = "Doctor", Plugins = new List<string> { "pack:ProfessionHandler.Doc" } };
            var tank = new PluginSet { Name = "Enforcer", Plugins = new List<string> { "pack:ProfessionHandler.Enf" } };
            settings.PluginSets.Add(doc); settings.PluginSets.Add(tank);
            var calm = account.Characters.First(c => c.Name == "Calm");
            calm.PluginSetId = doc.Id;
            var preset = new LaunchPreset { Name = "Team", Characters = new List<PresetCharacter> { new PresetCharacter { AccountId = "a1", CharacterId = calm.CharacterId, PluginSetId = tank.Id } } };
            settings.Presets.Add(preset);
            Check(PluginSets.Resolve(settings, account, calm, null) == doc.Id, "character default set");
            Check(PluginSets.Resolve(settings, account, calm, preset) == tank.Id, "preset override wins");
            preset.Characters[0].PluginSetId = null;
            Check(PluginSets.Resolve(settings, account, calm, preset) == doc.Id, "empty override falls back to default");
            calm.PluginSetId = PluginSet.None;
            Check(PluginSets.Resolve(settings, account, calm, null) == null, "none loads nothing");
            calm.PluginSetId = "deleted-set";
            Check(PluginSets.Resolve(settings, account, calm, null) == null, "missing set loads nothing");
            calm.PluginSetId = doc.Id; preset.Characters[0].PluginSetId = tank.Id;
            PluginSets.Delete(settings, tank);
            Check(preset.Characters[0].PluginSetId == PluginSet.None && calm.PluginSetId == doc.Id, "deleting a set sets team references to no plugins (review 5)");

            // Updating a preset keeps the overrides of characters that stay in it.
            preset.Characters[0].PluginSetId = doc.Id;
            foreach (var c in account.Characters) c.Selected = c == calm;
            PresetService.Update(settings, preset);
            Check(preset.Characters.Single().PluginSetId == doc.Id, "preset update keeps plugin overrides");

            if (packFolder == null) return;
            string error;
            var pack = AOSharpPack.FromFolder(packFolder, out error);
            Check(pack != null, "AO#\\dist style folder is a pack: " + error);
            if (pack == null) return;
            Check(pack.Plugins.Count > 0 && pack.Plugins.All(plugin => File.Exists(plugin.Path))
                && pack.Plugins.Select(plugin => plugin.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == pack.Plugins.Count,
                "selected installation exposes existing unique plugins (found " + pack.Plugins.Count + ")");
            Check(!pack.Plugins.Any(p => p.Id == "pack:ProfessionHandler.Generic" || p.Id == "pack:Shared"), "dependencies are not listed as plugins");
            var viaSubfolder = AOSharpPack.FromFolder(Path.Combine(packFolder, "AOSharp"), out error);
            Check(viaSubfolder != null && viaSubfolder.Root == pack.Root, "choosing the AOSharp subfolder works too");
            var fresh = TeamSettings();
            PluginSets.EnsureStarterSets(fresh, pack);
            bool hasZ = pack.Plugins.Any(plugin => plugin.Id.StartsWith("pack:Z.Handler.", StringComparison.OrdinalIgnoreCase));
            string handlerPrefix = hasZ ? "pack:Z.Handler." : "pack:ProfessionHandler.", syncId = hasZ ? "pack:Z.SyncManager" : "pack:Manager.Sync";
            Check(fresh.PluginSets.Count == 14 && fresh.PluginSets.All(set => set.Plugins.Count == 2 && set.Plugins.Contains(syncId)
                && set.Plugins.Any(id => id.StartsWith(handlerPrefix, StringComparison.Ordinal))), "14 starter sets use the installed suite's handler and sync manager");
            var trader = fresh.PluginSets.First(set => set.Name == "Trader");
            List<string> missing;
            var paths = PluginSets.Paths(fresh, pack, trader, false, out missing);
            Check(paths.Length == 2 && missing.Count == 0 && paths.All(File.Exists), "set resolves to existing DLLs");
            int before = fresh.PluginSets.Count;
            PluginSets.EnsureStarterSets(fresh, pack);
            Check(fresh.PluginSets.Count == before, "starter sets are created once");

            if (aosharpConfig == null) return;
            var imported = TeamSettings();
            var result = PluginSets.ImportAOSharpConfig(imported, pack, aosharpConfig);
            var chars = imported.Accounts[0].Characters;
            Check(result.Characters == 6 && result.Unmatched.Count == 0, "all six AOSharp profiles matched characters (" + result.Characters + ")");
            Check(chars.First(c => c.Name == "Nobody").PluginSetId == null, "characters without a profile get no set");
            var wazooSet = PluginSets.Find(imported, chars.First(c => c.Name == "Wazoo").PluginSetId);
            Check(wazooSet != null && wazooSet.Name == "Trader" && wazooSet.Plugins.Contains("pack:ProfessionHandler.Trader") && wazooSet.Plugins.Contains("pack:Manager.Sync")
                && !wazooSet.Plugins.Contains(PluginSet.HealthMonitorId), "Wazoo gets a Trader set without the health monitor");
            Check(PluginSets.Find(imported, chars.First(c => c.Name == "Sexologist").PluginSetId)?.Name == "Doctor", "Sexologist gets the Doctor set");
            Check(result.Sets == 6, "six distinct sets created (" + result.Sets + ")");
            var again = PluginSets.ImportAOSharpConfig(imported, pack, aosharpConfig);
            Check(again.Sets == 0 && imported.PluginSets.Count == 6, "importing twice reuses identical sets");
            foreach (var line in imported.PluginSets.Select(set => "  " + set.Name + ": " + string.Join(", ", set.Plugins))) Console.WriteLine(line);
        }

        static void LogToneTests()
        {
            Check(LogTone.Classify("ID 135: a tracked game client is still logging in; skipped. Check its game window.") == LogToneKind.Warning, "a skipped character is yellow");
            Check(LogTone.Classify("Raxi: loaded plugin set Doctor (12 plugins).") == LogToneKind.Good, "loaded plugins are green");
            Check(LogTone.Classify("Could not check for updates: timeout") == LogToneKind.Error, "a failed update check is red");
            Check(LogTone.Classify("Raxi: plugins ikke indlæst · Plugin-værten startede ikke.") == LogToneKind.Error, "Danish failures are red");
            Check(LogTone.Classify("Starting Raxi (ID 135)...") == LogToneKind.Neutral, "plain progress stays neutral");
            Check(LogTone.Classify("Raxi: earlier window closed; logging in now.") == LogToneKind.Good, "a cleaned-up window is green");
            Check(LogTone.Classify("Raxi: another AOSharp (AOSharp.exe (PID 4)) was attached to this client while Nexus plugins are loaded. The game is now hooked twice and can freeze or crash; close it.") == LogToneKind.Error, "two AOSharps are red");
            string rtf = LogTone.Rtf("[19:37:01] Plugin-sæt gemt {ok}\r\n[19:37:02] Failed\r\n", 9F);
            Check(rtf.StartsWith(@"{\rtf1") && rtf.Contains(@"\u230?") && rtf.Contains(@"\{ok\}") && rtf.Contains(@"\cf5 Failed"), "log RTF escapes text and colours lines");
            Check(ForeignAOSharp.IsOwn(@"C:\Users\x\AppData\Local\AONexus\AOSharp\abc\AOSharp".Replace('\\', System.IO.Path.DirectorySeparatorChar),
                new[] { @"C:\Users\x\AppData\Local\AONexus\AOSharp".Replace('\\', System.IO.Path.DirectorySeparatorChar) }), "modules inside the Nexus pack count as Nexus's own");
            Check(!ForeignAOSharp.IsOwn(@"C:\Games\AOSharp".Replace('\\', System.IO.Path.DirectorySeparatorChar),
                new[] { @"C:\Games\AOSharpPack".Replace('\\', System.IO.Path.DirectorySeparatorChar) }), "a folder with a similar name is not Nexus's own");
        }

        static void AppStateTests()
        {
            var settings = new LauncherSettings();
            var account = new AccountConfig { Label = "Main", Username = "raz", Server = "x", Port = 7505, LegacyCredentialTarget = "target-secret" };
            account.Characters.Add(new CharacterConfig { Name = "Raxi", CharacterId = 135, Selected = true });
            account.Characters.Add(new CharacterConfig { Name = "Other", CharacterId = 136, Selected = false });
            settings.Accounts.Add(account);
            string text = AppStateReport.Text(settings, new AppStateReport.Extras { Section = "characters", Sessions = new List<PresetCamping.Client>() });
            Check(text.Contains("[x] Raxi") && text.Contains("[ ] Other") && text.Contains("1 ticked in total"), "the app state lists ticked and unticked characters");
            Check(text.Contains("Load plugins automatically") && text.Contains("LOGIN SERVERS"), "the app state lists switches and servers");
            string json = AppStateReport.SettingsJson(settings);
            Check(json.Contains("\"LegacyCredentialTarget\": \"(removed)\"") && !json.Contains("target-secret") && json.Contains("\"RememberPassword\": true") && json.Contains("\"Raxi\""), "the settings snapshot removes credential-like fields and keeps the rest");
        }

        // The five findings from the 1.3.0 review, one regression check each (or more).
        static void ReviewFindingTests()
        {
            // 1. Settings from 1.2.x (no ServerRulesRevision) keep edited servers and removed defaults.
            var old = new LauncherSettings();
            var edited = LoginServerProfile.Defaults().First(p => p.Id == "rebirth");
            edited.MaxAccounts = 3; edited.MaxCharactersPerAccount = 2; edited.Name = "My Rebirth";
            old.LoginServers = new List<LoginServerProfile> { edited, LoginServerProfile.Defaults().First(p => p.Id == "live") };
            var json = new JavaScriptSerializer().Serialize(old);
            var fields = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            fields.Remove("ServerRulesRevision"); fields.Remove("RemovedServerIds");
            var migrated = SettingsStore.Deserialize(new JavaScriptSerializer().Serialize(fields));
            var rebirth = migrated.LoginServers.First(p => p.Id == "rebirth");
            Check(rebirth.Customized, "review 1: an edited server from 1.2.x settings is marked as the user's own");
            Check(migrated.RemovedServerIds.Contains("prk") && migrated.RemovedServerIds.Contains("live-2019") && !migrated.RemovedServerIds.Contains("rebirth-local"),
                "review 1: legacy defaults the user removed stay removed; newer servers are not hidden");
            var plan = ServerRules.Plan(migrated.LoginServers, ServerRules.Embedded(), migrated.RemovedServerIds);
            var kept = plan.Profiles.First(p => p.Id == "rebirth");
            Check(kept.MaxAccounts == 3 && kept.MaxCharactersPerAccount == 2 && kept.Name == "My Rebirth", "review 1: the first rules update keeps custom limits and the name");
            Check(!plan.Profiles.Any(p => p.Id == "prk") && plan.AddressChanges.Count == 0, "review 1: no removed server comes back and no address changes");
            var current = SettingsStore.Deserialize(new JavaScriptSerializer().Serialize(new LauncherSettings { LoginServers = LoginServerProfile.Defaults() }));
            Check(!current.LoginServers.Any(p => p.Customized) && current.RemovedServerIds.Count == 0, "review 1: current settings are not touched by the migration");

            // 2. The team that launched a character survives a Nexus restart.
            string context = Path.Combine(Path.GetTempPath(), "nexus-test-" + Guid.NewGuid().ToString("N"), "LaunchContext.json");
            string previous = PluginAutoInjector.LaunchContextPath;
            PluginAutoInjector.LaunchContextPath = context;
            try
            {
                long before = DateTime.UtcNow.AddMinutes(-5).Ticks;
                new PluginAutoInjector().RecordLaunch("acct", 135, "team-off");
                long after = DateTime.UtcNow.AddSeconds(5).Ticks;
                var restarted = new PluginAutoInjector();
                Check(restarted.LaunchPresetFor("acct", 135, after) == "team-off", "review 2: a new Nexus session still knows the team that launched the client");
                Check(restarted.LaunchPresetFor("acct", 135, before) == null, "review 2: a client started before the record is not attributed to that team");
                restarted.RecordLaunch("acct", 135, null);
                Check(new PluginAutoInjector().LaunchPresetFor("acct", 135, DateTime.UtcNow.AddSeconds(5).Ticks) == null, "review 2: a later launch without a team replaces the record");
                var settings = new LauncherSettings();
                var team = new LaunchPreset { Name = "Crafting", LoadPlugins = false };
                settings.Presets.Add(team);
                Check(!team.LoadPlugins && PlayStyles.For(settings, team) == PlayStyles.None, "review 2: a team with plugins off keeps that setting");
            }
            finally { PluginAutoInjector.LaunchContextPath = previous; try { Directory.Delete(Path.GetDirectoryName(context), true); } catch { } }

            // 3. Build-PluginHost.ps1 uses a Roslyn compiler (modern C#), not Windows' C# 5 csc.exe.
            string script = File.ReadAllText(Path.Combine(RepoRoot(), "build", "Build-PluginHost.ps1"));
            Check(script.Contains("Roslyn") && script.Contains("/langversion:latest") && !script.Contains("$compiler=Join-Path $env:WINDIR"), "review 3: the plugin host builds with Roslyn");

            // 4. The settings snapshot is valid JSON after redaction.
            var s4 = new LauncherSettings();
            var account = new AccountConfig { Label = "Main", Username = "raz", Server = "x", Port = 7505, GamePath = @"C:\Users\someone\AO" };
            account.Characters.Add(new CharacterConfig { Name = "Raxi", CharacterId = 135135, Selected = true });
            s4.Accounts.Add(account);
            var redactor = new DiagnosticRedactor(s4, null);
            string snapshot = AppStateReport.SettingsJson(s4, redactor);
            bool parses; try { new JavaScriptSerializer().DeserializeObject(snapshot); parses = true; } catch { parses = false; }
            Check(parses, "review 4: the redacted settings snapshot parses as JSON");
            Check(!snapshot.Contains("135135") && !snapshot.Contains("Raxi") && snapshot.Contains("\"CharacterId\": \"Char-"), "review 4: the character ID and name are replaced by a quoted code");

            // 5. Deleting a set used by a team override gives "no plugins", as the confirmation says.
            var s5 = new LauncherSettings();
            var set = new PluginSet { Name = "Doctor" };
            var fallback = new PluginSet { Name = "Default" };
            s5.PluginSets.Add(set); s5.PluginSets.Add(fallback);
            var acc = new AccountConfig { Username = "u" };
            var character = new CharacterConfig { Name = "Doc", CharacterId = 9, PluginSetId = fallback.Id };
            acc.Characters.Add(character); s5.Accounts.Add(acc);
            var preset = new LaunchPreset { Name = "Team" };
            preset.Characters.Add(new PresetCharacter { AccountId = acc.Id, CharacterId = 9, PluginSetId = set.Id });
            s5.Presets.Add(preset);
            PluginSets.Delete(s5, set);
            Check(PluginSets.Resolve(s5, acc, character, preset) == null, "review 5: after deleting the team's set the team loads no plugins (not the character default)");
            Check(PluginSets.Resolve(s5, acc, character, null) == fallback.Id, "review 5: the character's own default is unaffected outside the team");
        }

        static string RepoRoot()
        {
            string configured = Environment.GetEnvironmentVariable("NEXUS_REPO");
            if (!string.IsNullOrEmpty(configured)) return configured;
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "build", "Build-PluginHost.ps1"))) return dir.FullName;
            return Directory.GetCurrentDirectory();
        }

        static void SuiteAndPlayStyleTests()
        {
            string root = Path.Combine(Path.GetTempPath(), "nexus-pack-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "AOSharp"));
                foreach (string file in new[] { "AOSharp.Bootstrap.dll", "AOSharp.Core.dll", "EasyHook.dll", "EasyHook32.dll" }) File.WriteAllText(Path.Combine(root, "AOSharp", file), "");
                var entries = new List<Dictionary<string, object>>();
                Action<string, string, string, string> plugin = (id, group, suite, path) =>
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root, path)));
                    File.WriteAllText(Path.Combine(root, path), "");
                    entries.Add(new Dictionary<string, object> { { "Id", id }, { "Group", group }, { "Suite", suite }, { "Path", path }, { "Commands", new[] { "handler", "form" } }, { "Settings", "handler" } });
                };
                plugin("Z.Handler.Doc", "Profession handlers", "Z", "Plugins/Z/Handlers/Handler.Doc.dll");
                plugin("Z.SyncManager", "Managers", "Z", "Plugins/Z/Managers/SyncManager.dll");
                plugin("Z.AssistManager", "Managers", "Z", "Plugins/Z/Managers/AssistManager.dll");
                plugin("Z.HelpManager", "Managers", "Z", "Plugins/Z/Managers/HelpManager.dll");
                plugin("Z.MovementManager", "Managers", "Z", "Plugins/Z/Managers/MovementManager.dll");
                plugin("Z.InfBuddy", "Bots", "Z", "Plugins/Z/Buddies/InfBuddy.dll");
                plugin("ProfessionHandler.Doc", "Profession handlers", "AOSP", "Plugins/AOSP/ProfessionHandler.Doc.dll");
                plugin("Manager.Sync", "Managers", "AOSP", "Plugins/AOSP/Manager.Sync.dll");
                plugin("Nexus.Shortcuts", "AO Nexus", "Nexus", "Plugins/Nexus/NexusShortcuts.dll");
                File.WriteAllText(Path.Combine(root, "plugins.json"), new JavaScriptSerializer().Serialize(new Dictionary<string, object> { { "Plugins", entries } }));
                var pack = AOSharpPack.FromFolder(root, out string error);
                var doc = pack?.Plugins.FirstOrDefault(p => p.Id == "pack:Z.Handler.Doc");
                Check(doc != null && doc.Suite == "Z" && doc.Settings == "handler" && doc.Commands.Contains("form"), "catalog reads suite, settings and commands");

                // New starter sets prefer Z; existing choices are preserved until explicit conversion.
                var fresh = new LauncherSettings();
                PluginSets.EnsureStarterSets(fresh, pack);
                var doctor = fresh.PluginSets.FirstOrDefault(p => p.Name == "Doctor");
                Check(doctor != null && doctor.Plugins.SequenceEqual(new[] { "pack:Z.Handler.Doc", "pack:Z.SyncManager" }) && fresh.PluginSuiteVersion == 1, "new starter sets use the Z handler + SyncManager");
                var upgraded = new LauncherSettings { PluginSetsInitialized = true };
                var starter = new PluginSet { Name = "Doctor", Plugins = new List<string> { "pack:ProfessionHandler.Doc", "pack:Manager.Sync" } };
                var own = new PluginSet { Name = "Mine", Plugins = new List<string> { "pack:ProfessionHandler.Doc", "pack:Manager.Sync", "pack:Manager.Loot" } };
                upgraded.PluginSets.Add(starter); upgraded.PluginSets.Add(own);
                PluginSets.EnsureStarterSets(upgraded, pack);
                Check(starter.Plugins.SequenceEqual(new[] { "pack:ProfessionHandler.Doc", "pack:Manager.Sync" }) && own.Plugins.Contains("pack:Manager.Loot") && PluginSets.SuiteOf(own) == "AOSP",
                    "upgrade preserves both starter and custom handler choices");
                PluginSets.EnsureStarterSets(upgraded, pack);
                Check(upgraded.PluginSuiteVersion == 0 && PluginSets.SuiteOf(own) == "AOSP", "opening the library never migrates existing choices");
                var unmatched = PluginSets.Convert(own, true);
                Check(own.Plugins.SequenceEqual(new[] { "pack:Z.Handler.Doc", "pack:Z.SyncManager", "pack:Z.LootManager" }) && unmatched.Count == 0, "Move sets to Z swaps each AOSP plugin for its counterpart");
                var mixed = new PluginSet { Plugins = new List<string> { "pack:Z.Handler.Doc", "pack:Manager.Sync" } };
                Check(PluginSets.MixesSuites(mixed) && PluginSets.SuiteOf(mixed) == "Z", "a set mixing suites is detected; its handler decides the suite");

                // Loading: play style extras for the set's suite, Nexus Shortcuts, other suite left out.
                var settings = new LauncherSettings();
                string[] paths = PluginSets.Paths(settings, pack, mixed, false, "team", true, out var missing, out var skipped);
                var names = paths.Select(Path.GetFileNameWithoutExtension).ToList();
                Check(names.Contains("Handler.Doc") && names.Contains("SyncManager") && names.Contains("AssistManager") && names.Contains("MovementManager") && names.Contains("NexusShortcuts"),
                    "team play adds Sync, Assist, Help and Movement managers plus Nexus Shortcuts");
                Check(!names.Contains("Manager.Sync") && skipped.Contains("pack:Manager.Sync"), "a plugin from the other suite is left out and reported");
                string[] none = PluginSets.Paths(settings, pack, doctor, false, null, false, out missing, out skipped);
                Check(none.Length == 2 && skipped.Count == 0, "without a play style or shortcuts only the set loads");
                string[] aosp = PluginSets.Paths(settings, pack, new PluginSet { Plugins = new List<string> { "pack:ProfessionHandler.Doc" } }, false, "inferno", false, out missing, out skipped);
                Check(aosp.Any(path => path.EndsWith("ProfessionHandler.Doc.dll")) && !aosp.Any(path => path.Contains("InfBuddy")) && missing.Contains("pack:Automaton.Inf"),
                    "an AOSP set gets the style's AOSP plugins, never Z's");
                Check(PlayStyles.All.Count >= 12 && PlayStyles.All.All(style => style.Z.All(id => id.StartsWith("Z.")) && style.AOSP.All(id => !id.StartsWith("Z."))), "every play style lists Z and AOSP plugins in the right suite");
                Check(!PlayStyles.All.Any(style => style.Z.Contains("Z.MailManager") || style.Z.Contains("Z.TeamAcceptUtility") || style.Z.Contains("Z.NCUManager")),
                    "no play style adds MailManager, TeamAcceptUtility or NCUManager");
                Check(PlayStyles.TeamActions.Any(action => action.Command == "form" && action.LeaderOnly) && PlayStyles.TeamActions.Any(action => action.Command == "rebuff" && !action.LeaderOnly),
                    "team actions: Form team runs on the leader, Rebuff on everyone");
                AutomaticSetAndHealthTests(pack, root);
                SharedPluginTests(pack);
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        // Automatic sets by profession, plugin health (Nexus Shortcuts' report) and the plugin test plan.
        static void SharedPluginTests(AOSharpPack pack)
        {
            var settings = new LauncherSettings { PluginSetsInitialized = true };
            var doctor = new PluginSet { Name = "Doctor", Plugins = new List<string> { "pack:ProfessionHandler.Doc", "pack:Manager.Sync" } };
            var other = new PluginSet { Name = "Other", Plugins = new List<string> { "PACK:MANAGER.SYNC" } };
            settings.PluginSets.Add(doctor); settings.PluginSets.Add(other);
            Check(settings.SharedPlugins.Count == 0, "old profiles start without shared plugins");
            PluginSets.SetSharedPlugin(settings, "pack:Manager.Sync", true);
            Check(doctor.Plugins.SequenceEqual(new[] { "pack:ProfessionHandler.Doc" }) && other.Plugins.Count == 0,
                "ALL promotes duplicate local choices without touching profession handlers");
            Check(PluginSets.EffectiveIds(settings, doctor).Count == 2 && PluginSets.EffectiveIds(settings, other).Count == 1,
                "ALL applies to every enabled set including otherwise empty sets");
            other.Plugins.Add("PACK:MANAGER.SYNC");
            Check(PluginSets.EffectiveIds(settings, other).Count == 1, "shared and local plugin IDs are deduplicated case-insensitively");
            PluginSets.SetSharedPlugin(settings, "pack:Manager.Sync", true);
            var paths = PluginSets.Paths(settings, pack, doctor, false, out var missing);
            Check(paths.Length == 2 && missing.Count == 0, "shared and profession plugins resolve together");
            Check(PluginSets.Paths(settings, pack, null, true, out missing).Length == 0
                && PluginSets.Paths(settings, pack, new PluginSet { Id = PluginSet.None }, true, out missing).Length == 0,
                "ALL never enables plugins or helpers for No plugins");
            var stale = new PluginSet { Plugins = new List<string> { "pack:Z.Handler.Adv" } };
            paths = PluginSets.Paths(settings, pack, stale, false, out missing);
            Check(paths.Length == 1 && paths[0].EndsWith("Manager.Sync.dll") && missing.Contains("pack:Z.Handler.Adv"),
                "a missing old-suite handler does not block an installed shared plugin and remains reported missing");
            var z = new PluginSet { Plugins = new List<string> { "pack:Z.Handler.Doc" } };
            paths = PluginSets.Paths(settings, pack, z, false, null, false, out missing, out var skipped);
            Check(paths.Length == 1 && skipped.Contains("pack:Manager.Sync"), "ALL preserves suite-conflict protection and reports skipped plugins");
            PluginSets.SetSharedPlugin(settings, "custom:missing", true);
            PluginSets.Paths(settings, pack, doctor, false, out missing);
            Check(missing.Contains("custom:missing"), "missing shared plugins are reported");
            PluginSets.SetSharedPlugin(settings, "custom:missing", false);
            var serializer = new JavaScriptSerializer();
            var copy = serializer.Deserialize<LauncherSettings>(serializer.Serialize(settings));
            Check(copy.SharedPlugins.SequenceEqual(settings.SharedPlugins) && copy.SharedPlugins != settings.SharedPlugins,
                "shared settings survive serialization and edit copies are isolated");
            other.Plugins.Add("pack:Manager.Sync"); // A set imported while ALL was enabled.
            PluginSets.SetSharedPlugin(settings, "pack:Manager.Sync", false);
            Check(!PluginSets.EffectiveIds(settings, doctor).Contains("pack:Manager.Sync") && PluginSets.EffectiveIds(settings, other).Count == 0,
                "removing ALL removes promoted selections from all professions");
            using (var dialog = new PluginsDialog(settings, pack, null))
            {
                var list = (System.Windows.Forms.ListBox)dialog.Controls.Find("pluginSetList", true).Single();
                var grid = (PluginGrid)dialog.Controls.Find("pluginGrid", true).Single();
                var all = (PluginSet)list.Items[0];
                Check(all.Id == PluginSet.All && all.Name == "ALL" && !dialog.Working.PluginSets.Contains(all), "ALL is pinned first and is not an assignable profession set");
                Check(!dialog.IsDirty, "opening ALL does not dirty saved settings");
                var toggle = typeof(PluginsDialog).GetMethod("PluginToggled", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var sync = pack.Plugins.Single(p => p.Id == "pack:Manager.Sync");
                toggle.Invoke(dialog, new object[] { sync, true });
                Check(dialog.IsDirty && settings.SharedPlugins.Count == 0 && dialog.Working.SharedPlugins.Count == 1,
                    "editing ALL changes the working copy only until Save");
                list.SelectedItem = dialog.Working.PluginSets.Single(p => p.Name == "Doctor");
                Check(grid.IsChecked(sync) && grid.IsInherited(sync), "profession view shows shared plugins checked and inherited");
                toggle.Invoke(dialog, new object[] { sync, false });
                Check(dialog.Working.SharedPlugins.Contains(sync.Id), "inherited selections cannot be removed from a profession");
                list.SelectedIndex = 0;
                toggle.Invoke(dialog, new object[] { sync, false });
                Check(!dialog.IsDirty && !grid.IsChecked(sync), "removing ALL can restore the original unchanged settings");
            }
        }

        static void AutomaticSetAndHealthTests(AOSharpPack pack, string root)
        {
            // Nexus Shortcuts loads first, so it sees the others load and start.
            var settings = new LauncherSettings();
            var doc = new PluginSet { Name = "Doctor", Plugins = new List<string> { "pack:Z.Handler.Doc", "pack:Z.SyncManager" } };
            string[] paths = PluginSets.Paths(settings, pack, doc, false, null, true, out _, out _);
            Check(Path.GetFileName(paths[0]) == "NexusShortcuts.dll" && paths.Length == 3, "Nexus Shortcuts is loaded before the other plugins");

            // Empty = automatic, "none" = no plugins, a team can override or switch plugins off.
            var account = new AccountConfig { Label = "Main" };
            var geek = new CharacterConfig { Name = "Geek", CharacterId = 77 };
            account.Characters.Add(geek);
            Check(PluginSets.IsAutomatic(account, geek, null), "a character without a set is automatic");
            geek.PluginSetId = PluginSet.None;
            Check(!PluginSets.IsAutomatic(account, geek, null) && PluginSets.Resolve(settings, account, geek, null) == null, "No plugins is not automatic and loads nothing");
            geek.PluginSetId = null;
            var team = new LaunchPreset { Name = "Team" };
            team.Characters.Add(new PresetCharacter { AccountId = account.Id, CharacterId = 77, PluginSetId = doc.Id });
            Check(!PluginSets.IsAutomatic(account, geek, team), "a team override is not automatic");
            team.Characters[0].PluginSetId = null;
            Check(PluginSets.IsAutomatic(account, geek, team), "Character default in a team follows an automatic character");
            team.LoadPlugins = false;
            Check(!PluginSets.IsAutomatic(account, geek, team), "plugins switched off for a team are never automatic");

            // The profession's starter set: reused by name, created when missing.
            var withSets = new LauncherSettings();
            PluginSets.EnsureStarterSets(withSets, pack);
            var found = PluginSets.ForProfession(withSets, pack, "Doctor", out bool created);
            Check(found != null && found.Name == "Doctor" && !created, "a Doctor gets the existing Doctor set");
            withSets.PluginSets.Remove(found);
            found = PluginSets.ForProfession(withSets, pack, "Doctor", out created);
            Check(found != null && created && found.Plugins.SequenceEqual(new[] { "pack:Z.Handler.Doc", "pack:Z.SyncManager" }), "a deleted starter set is made again from the main suite");
            Check(PluginSets.ForProfession(withSets, pack, "Engineer", out created) == null && !created, "no set is made when the pack has no handler for the profession");
            Check(PluginSets.ForProfession(withSets, pack, "Monster", out created) == null, "unknown professions get no set");
            Check(PluginSets.ProfessionName("MartialArtist") == "Martial Artist" && PluginSets.ProfessionName("Metaphysicist") == "Meta-Physicist", "AOSharp profession names map to set names");

            // Deleting a set must not turn its characters automatic (that could load another set).
            var owner = new LauncherSettings();
            var acc = new AccountConfig { Label = "A" };
            var user = new CharacterConfig { Name = "U", CharacterId = 5, PluginSetId = doc.Id };
            acc.Characters.Add(user); owner.Accounts.Add(acc); owner.PluginSets.Add(doc);
            PluginSets.Delete(owner, doc);
            Check(user.PluginSetId == PluginSet.None, "deleting a set leaves its characters with no plugins, not automatic");

            // Health: the report Nexus Shortcuts gave for good, failing, crashing and incomplete plugins.
            string folder = Path.Combine(root, "Plugins", "Test");
            string good = Path.Combine(folder, "Good", "Good.dll"), bad = Path.Combine(folder, "BadInit", "BadInit.dll"), crashy = Path.Combine(folder, "Crashy", "Crashy.dll"),
                missingDep = Path.Combine(folder, "MissingDep", "MissingDep.dll"), quiet = Path.Combine(folder, "Quiet", "Quiet.dll"), absent = Path.Combine(folder, "Absent", "Absent.dll"),
                shortcuts = Path.Combine(root, "Plugins", "Nexus", "NexusShortcuts.dll");
            var serializer = new JavaScriptSerializer();
            string json = serializer.Serialize(new Dictionary<string, object>
            {
                { "who", "Engineer Geek" },
                { "plugins", new object[]
                    {
                        new Dictionary<string, object> { { "a", "Good" }, { "f", good }, { "entry", true }, { "c", new[] { "goodcmd" } }, { "h", 1 }, { "s", null }, { "e", 0 }, { "m", null } },
                        new Dictionary<string, object> { { "a", "BadInit" }, { "f", bad }, { "entry", true }, { "c", new string[0] }, { "h", 0 }, { "s", "failed to start: InvalidOperationException: settings file is broken" }, { "e", 1 }, { "m", "x" } },
                        new Dictionary<string, object> { { "a", "Crashy" }, { "f", crashy }, { "entry", true }, { "c", new[] { "crashy" } }, { "h", 1 }, { "s", null }, { "e", 20 }, { "m", "NullReferenceException: Object reference not set" } },
                        new Dictionary<string, object> { { "a", "MissingDep" }, { "f", missingDep }, { "entry", true }, { "c", new string[0] }, { "h", 0 }, { "s", null }, { "e", 0 }, { "m", null } },
                        new Dictionary<string, object> { { "a", "Quiet" }, { "f", quiet }, { "entry", true }, { "c", new string[0] }, { "h", 0 }, { "s", null }, { "e", 0 }, { "m", null } },
                    } },
                { "load", new[] { "FileNotFoundException: Could not load file or assembly '" + Path.Combine(folder, "MissingDep", "Lib.dll") + "'." } },
            });
            var report = HealthReport.Parse(json);
            Check(report != null && report.who == "Engineer Geek" && report.plugins.Count == 5 && report.load.Count == 1, "the health report is read");
            Check(HealthReport.Parse("not json") == null && HealthReport.Parse(null) == null, "a broken health report is ignored");
            var checks = PluginHealth.Evaluate(new[] { shortcuts, good, bad, crashy, missingDep, quiet, absent }, report);
            Func<string, PluginCheck> result = name => checks.FirstOrDefault(check => check.Name == name);
            Check(checks.Count == 6 && result("NexusShortcuts") == null, "every plugin sent is judged, except Nexus Shortcuts itself");
            Check(result("Good").Status == PluginStatus.Running && result("Good").Commands.Contains("goodcmd"), "a plugin that hooked its commands works");
            Check(result("BadInit").Status == PluginStatus.Failed && result("BadInit").Message.Contains("settings file is broken"), "a plugin whose start failed is reported with AOSharp's hidden error");
            Check(result("Crashy").Status == PluginStatus.Errors && result("Crashy").Errors == 20, "a plugin that throws while running is flagged with its error count");
            Check(result("MissingDep").Status == PluginStatus.Failed && result("MissingDep").Message.Contains("Lib.dll"), "a plugin missing a library is reported with the missing file");
            Check(result("Quiet").Status == PluginStatus.Started && !result("Quiet").Problem, "a plugin that hooked nothing yet is started, not a problem");
            Check(result("Absent").Status == PluginStatus.NotLoaded && result("Absent").Problem, "a plugin AOSharp never loaded is reported");
            Check(PluginHealth.Summary(checks) == "3 did not start, 1 with observed exceptions", "Running now separates failures from observed exceptions");
            Check(result("Crashy").StatusText().Contains("may be handled"), "first-chance notifications do not prove an unhandled failure");
            Check(PluginHealth.Summary(checks.Where(check => !check.Problem).ToList()) == null, "no summary when everything works");

            // Test plan: suites apart, one target per startup check.
            var catalog = new List<PluginInfo>();
            foreach (string key in new[] { "Adv", "Agent", "Crat", "Doc", "Enf" })
            {
                catalog.Add(new PluginInfo { Id = "pack:Z.Handler." + key, Name = key + " handler", Group = "Profession handlers", Suite = "Z" });
                catalog.Add(new PluginInfo { Id = "pack:ProfessionHandler." + key, Name = key + " handler", Group = "Profession handlers", Suite = "AOSP" });
            }
            for (int i = 0; i < 11; i++) catalog.Add(new PluginInfo { Id = "pack:Z.M" + i, Name = "M" + i, Group = "Managers", Suite = "Z" });
            catalog.Add(new PluginInfo { Id = "pack:Neko", Name = "Neko", Group = "Classic", Suite = "Classic" });
            var batches = PluginTestDialog.Batches(catalog);
            Check(batches.Sum(batch => batch.Count) == catalog.Count && batches.All(batch => batch.Count == 1), "the test schedules each target alone beside instrumentation");
            Check(batches.All(batch => batch.Count(plugin => plugin.Group == "Profession handlers") <= 1), "never two profession handlers in one batch");
            Check(batches.All(batch => batch.Select(plugin => plugin.Suite).Distinct().Count() == 1), "the Z and AOSP suites are never tested together");

        }


        static void StartupPolicyTests(string packFolder)
        {
            var entries = PluginTestPolicy.Entries.ToList();
            Check(entries.Count == 113 && entries.Select(e => e.Id).Distinct().Count() == 113, "every packaged plugin has an explicit test classification");
            Check(entries.Count(e => e.Class == "Blocked") == 4 && entries.Count(e => e.Class == "Candidate") == 15, "four manual-review exclusions and fifteen startup targets plus instrumentation");
            Check(new PluginCheck { Status = PluginStatus.Running }.StatusText() == "Startup observed", "start evidence is not labelled Works");
            Check(!PluginTestPolicy.ResultMatches(new PluginTestRecord { Status = "Running" }, "abc"), "legacy unversioned results do not transfer");
            Check(PluginTestPolicy.ResultMatches(new PluginTestRecord { TestKind="Startup", PluginSha256="ABC" }, "abc"), "matching startup result applies to its binary");
            Check(!PluginTestPolicy.ResultMatches(new PluginTestRecord { TestKind="Startup", PluginSha256="ABC" }, "def"), "changed binary invalidates startup result");
            if (packFolder == null) return;
            var pack = AOSharpPack.FromFolder(packFolder, out var error);
            Check(pack != null, error);
            foreach (var plugin in pack.Plugins) {
                var entry = entries.SingleOrDefault(e => e.Id == plugin.Id);
                if (entry == null)
                {
                    Check(PluginTestPolicy.Rejection(plugin, true, "Doctor", out _) != null, plugin.Id + " unclassified installed plugin is not admitted to automated testing");
                    continue;
                }
                string profession = entry.Profession == null ? "Doctor" : PluginHealth.ProfessionKeys.First(pair => pair.Value == entry.Profession).Key;
                string limited = PluginTestPolicy.Rejection(plugin, false, profession, out var hash);
                string extended = PluginTestPolicy.Rejection(plugin, true, profession, out _);
                Check((limited == null) == (entry.Class == "Candidate"), plugin.Id + " default eligibility");
                Check((extended == null) == (entry.Class == "Candidate" || entry.Class == "Scenario"), plugin.Id + " scenario option never bypasses a blocked file");
                if (entry.Profession != null) {
                    Check(PluginTestPolicy.Rejection(plugin, true, "Unknown", out _) != null, plugin.Id + " unknown profession is skipped");
                    Check(PluginTestPolicy.Rejection(plugin, true, profession == "Doctor" ? "Trader" : "Doctor", out _) != null, plugin.Id + " wrong profession is skipped");
                }
            }
            string folder = Path.Combine(Path.GetTempPath(), "Nexus-Policy-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try {
                var original = pack.Plugins.FirstOrDefault(p => PluginTestPolicy.Rejection(p, false, "Doctor", out _) == null);
                if (original == null) { Console.WriteLine("SKIP binary-copy policy checks: selected installation has no eligible Doctor startup target"); return; }
                string path = Path.Combine(folder, "plugin.dll"); File.Copy(original.Path, path);
                var candidate = new PluginInfo { Id=original.Id, Path=path };
                Check(PluginTestPolicy.Rejection(candidate, false, "Doctor", out _) == null, "identical file copy is accepted");
                using(var stream=File.OpenWrite(path)){stream.Position=stream.Length;stream.WriteByte(1);}
                Check(PluginTestPolicy.Rejection(candidate, true, "Doctor", out _) != null, "modified known ID is not admitted even with scenario option");
                var blocked = pack.Plugins.FirstOrDefault(p => p.Id == "pack:Neko");
                candidate.Id="custom:renamed";
                if (blocked != null)
                {
                    File.Copy(blocked.Path, path, true);
                    Check(PluginTestPolicy.Rejection(candidate,true,"Doctor",out _).StartsWith("Excluded:"), "renamed blocked binary remains blocked");
                }
                File.WriteAllText(path,"unknown");
                Check(PluginTestPolicy.Rejection(candidate,true,"Doctor",out _) != null, "unknown custom binary needs classification");
            } finally { Directory.Delete(folder,true); }
        }

        static void PreflightTests()
        {
            Check(LoginPreflight.AbandonedAfter(120) == TimeSpan.FromSeconds(150), "a pending window counts as abandoned 30 s after the engine stops waiting");
            Check(LoginPreflight.AbandonedAfter(0) == TimeSpan.FromSeconds(40), "a missing ready timeout still gives a short grace period");
            string path = LoginPreflight.RegistryPath; string backup = File.Exists(path) ? File.ReadAllText(path) : null;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var settings = TeamSettings(); var account = settings.Accounts[0]; account.Server = "2.24.96.30"; account.Port = 7500; account.GamePath = Path.GetTempPath();
                foreach (var c in account.Characters) c.Selected = true;
                var raxi = account.Characters[2];
                // A pending record whose process no longer exists is not reported.
                File.WriteAllText(path, "[{\"AccountIdentity\":\"id:a1\",\"Username\":\"USER\",\"Pending\":true,\"Server\":\"2.24.96.30\",\"Port\":7500,\"CharacterId\":" + raxi.CharacterId
                    + ",\"GamePath\":\"" + Path.GetTempPath().Replace("\\", "\\\\") + "\",\"ProcessId\":999999,\"StartTimeUtcTicks\":1}]");
                Check(LoginPreflight.Read().Single().Pending, "the client registry is read with pending records");
                Check(LoginPreflight.FindStuck(settings, settings.Accounts).Count == 0, "a pending record without its process is not reported as stuck");
                // A pending record for this test process (not AnarchyOnline.exe) is not reported either.
                var self = System.Diagnostics.Process.GetCurrentProcess();
                File.WriteAllText(path, "[{\"AccountIdentity\":\"id:a1\",\"Pending\":true,\"Server\":\"2.24.96.30\",\"Port\":7500,\"CharacterId\":" + raxi.CharacterId
                    + ",\"GamePath\":\"" + Path.GetTempPath().Replace("\\", "\\\\") + "\",\"ProcessId\":" + self.Id + ",\"StartTimeUtcTicks\":" + self.StartTime.ToUniversalTime().Ticks + "}]");
                Check(LoginPreflight.FindStuck(settings, settings.Accounts).Count == 0, "only the game executable counts as a stuck client");
            }
            finally { if (backup != null) File.WriteAllText(path, backup); else try { File.Delete(path); } catch { } }
        }

        static void ActivityLogTests()
        {
            string zipPath = Path.Combine(Path.GetTempPath(), "nexus-report-test-" + Guid.NewGuid().ToString("N") + ".zip");
            try
            {
                string log = "[18:31:28] Wazoo: loaded plugin set Trader (3 plugins).\r\n";
                ClientReport.Export(zipPath, "{}", "No Windows evidence in this test.", Path.Combine(Path.GetTempPath(), "no-such-reports-" + Guid.NewGuid().ToString("N")), log);
                using (var zip = System.IO.Compression.ZipFile.OpenRead(zipPath))
                {
                    var entry = zip.GetEntry("activity-log.txt");
                    Check(entry != null, "the diagnostic ZIP contains activity-log.txt");
                    using (var reader = new StreamReader(entry.Open())) Check(reader.ReadToEnd().Contains("loaded plugin set Trader"), "the activity log text is included");
                    Check(zip.GetEntry("README.txt") != null && zip.GetEntry("report-summary.txt") != null, "the other report files are still there");
                }
                string big = string.Join("\r\n", Enumerable.Range(0, 40000).Select(i => "[00:00:00] line " + i + " of a long session"));
                string trimmed = ClientReport.ActivityLogText(big);
                Check(System.Text.Encoding.UTF8.GetByteCount(trimmed) <= ClientReport.ActivityLogLimitBytes && trimmed.Contains("line 39999") && !trimmed.Contains("line 0 of")
                    && trimmed.Contains("Earlier entries omitted"), "long logs keep the newest entries within the size limit");
                Check(ClientReport.ActivityLogText("").Contains("empty"), "an empty log is labelled");
            }
            finally { try { File.Delete(zipPath); } catch { } }
        }

        static void PackExtractionTests(string payload)
        {
            string cache = Path.Combine(Path.GetTempPath(), "nexus-pack-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                string catalog;
                using (var zip = File.OpenRead(Path.Combine(payload, "AOSharpPack.zip")))
                using (var manifest = File.OpenRead(Path.Combine(payload, "AOSharpPack.manifest.json")))
                    catalog = EmbeddedBackend.EnsureBundleAvailable(cache, zip, manifest, "plugins.json", CancellationToken.None);
                string error;
                var pack = AOSharpPack.FromFolder(Path.GetDirectoryName(catalog), out error);
                Check(pack != null && pack.Plugins.Count == 113, "extracted pack lists all 113 plugins: " + error);
                Check(File.Exists(Path.Combine(pack.AOSharpDirectory, "AOSharp.Bootstrap.dll")) && File.Exists(Path.Combine(pack.AOSharpDirectory, "EasyLoad32.dll"))
                    && !File.Exists(Path.Combine(pack.AOSharpDirectory, "AOSharp.exe")), "pack holds the runtime but not the AOSharp desktop app");
                Check(pack.Plugins.First(p => p.Id == "pack:ProfessionHandler.Doc").Name == "Doc handler", "display names");
                using (var zip = File.OpenRead(Path.Combine(payload, "AOSharpPack.zip")))
                using (var manifest = File.OpenRead(Path.Combine(payload, "AOSharpPack.manifest.json")))
                    Check(EmbeddedBackend.EnsureBundleAvailable(cache, zip, manifest, "plugins.json", CancellationToken.None) == catalog, "second extraction reuses the verified folder");
            }
            finally { try { Directory.Delete(cache, true); } catch { } }
        }
    }
}
