using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using AOLauncher;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NexusEngine;
using NexusTeams;

namespace Nexus.Tests
{
    [TestClass]
    public class FeatureFreezeTests
    {
        [TestMethod]
        public void OldProfilesCannotEnableNewExperimentsButRetainTheirChoices()
        {
            var settings = SettingsStore.Deserialize("{\"MinimizeInactiveClients\":true,\"Presets\":[{\"Name\":\"Saved\",\"AutoFormTeam\":true}],\"ControlPanel\":{\"Tab\":2}}");
            Assert.IsFalse(settings.ExperimentalAovk);
            Assert.IsFalse(settings.ExperimentalTeamAutomation);
            Assert.IsFalse(settings.ExperimentalNearbyCombat);
            Assert.IsTrue(settings.MinimizeInactiveClients);
            Assert.IsTrue(settings.Presets[0].AutoFormTeam);
            Assert.AreEqual(2, settings.ControlPanel.Tab);
            settings.ExperimentalAovk = settings.ExperimentalTeamAutomation = settings.ExperimentalNearbyCombat = true;
            var saved = SettingsStore.Deserialize(new JavaScriptSerializer().Serialize(settings));
            Assert.IsTrue(saved.ExperimentalAovk && saved.ExperimentalTeamAutomation && saved.ExperimentalNearbyCombat);
        }

        [TestMethod]
        public void SavedFormationChoiceCannotForceOrderedLoginWhileExperimentIsOff()
        {
            var settings=new LauncherSettings();
            settings.Accounts.Add(new AccountConfig {Id="a",Characters=new List<CharacterConfig> {
                new CharacterConfig {CharacterId=1,Selected=true},new CharacterConfig {CharacterId=2,Selected=true}}});
            var preset=PresetService.Create(settings,"Fixture");
            preset.AutoFormTeam=true;preset.OrderedLogin=false;
            preset.Characters.Reverse();
            var request=new LaunchRequest {Characters=new List<LaunchCharacter> {
                new LaunchCharacter {AccountId="a",CharacterId=1},new LaunchCharacter {AccountId="a",CharacterId=2}}};
            MainForm.ApplyPresetLaunchOrder(settings,request);
            Assert.IsFalse(request.OrderedStarts);
            preset.OrderedLogin=true;
            MainForm.ApplyPresetLaunchOrder(settings,request);
            Assert.IsTrue(request.OrderedStarts && request.KeepLeaderActive);
            Assert.AreEqual(2,request.Characters[0].CharacterId);
        }

        [TestMethod]
        public void RendererOptInAllowsOnlyVerifiedAovkAndAlwaysAllowsStock()
        {
            Assert.AreEqual(StartupProfile.Stock, FastStartup.AllowedProfile(StartupProfile.Stock, false));
            Assert.AreEqual(StartupProfile.None, FastStartup.AllowedProfile(StartupProfile.AOVKVerified, false));
            Assert.AreEqual(StartupProfile.AOVKVerified, FastStartup.AllowedProfile(StartupProfile.AOVKVerified, true));
            Assert.AreEqual(StartupProfile.None, FastStartup.AllowedProfile(StartupProfile.AOVK, true));
            Assert.IsFalse(new LaunchRequest().AllowExperimentalAovk);
        }

        [TestMethod]
        public void TurningOffRecoveryCancelsIssuedPlansAndStopsReadingClients()
        {
            var settings = new LauncherSettings {ExperimentalTeamAutomation=true, AutoInjectPlugins=true, IncludeShortcuts=true};
            settings.Accounts = Enumerable.Range(1,3).Select(i=>new AccountConfig {Id="a"+i,Server="fixture",Port=7500}).ToList();
            var rows = Enumerable.Range(1,3).Select(i=>new PluginAutoInjector.Row {AccountId="a"+i,CharacterId=i,Pid=i,StartTicks=i,State="Loaded",ShortcutsRequested=true}).ToList();
            var now=DateTime.UtcNow;
            var alive=new HashSet<int>(new[]{1,2,3});
            var views=rows.ToDictionary(r=>r.Pid,r=>new TeamView {Start=r.StartTicks,Character=r.CharacterId,Ready=true,Leader=1,Members=new[]{1,2,3}});
            var sent=new List<string>();
            int reads=0;
            var json=new JavaScriptSerializer();
            var recovery=new TeamRecoveryController {
                Now=()=>now, IsAlive=r=>alive.Contains(r.Pid), ReadRecoveryStatus=pid=>"WAIT",
                ReadView=pid=>{reads++;views[pid].Utc=now.Ticks;return json.Serialize(views[pid]);},
                SendPlan=(pid,line)=>{sent.Add(line);return "OK queued";}
            };
            recovery.Poll(settings,rows);
            alive.Remove(1);
            foreach(var view in views.Values) {view.Leader=2;view.Members=new[]{2,3};}
            now=now.AddSeconds(3);recovery.Poll(settings,rows);
            Assert.IsTrue(sent.Any(c=>c.StartsWith("TEAMRESTORE ")));
            sent.Clear();int before=reads;
            settings.ExperimentalTeamAutomation=false;
            recovery.Poll(settings,rows); // Must cancel even inside the normal polling interval.
            Assert.AreEqual(2,sent.Count);
            Assert.IsTrue(sent.All(c=>c.StartsWith("TEAMCANCEL ")));
            recovery.Poll(settings,rows);
            Assert.AreEqual(before,reads);
            Assert.AreEqual(2,sent.Count);
        }

        [TestMethod]
        public void CorePanelHidesNearbyReportsAndRestoresSavedTabAfterOptIn()
        {
            OnSta(()=> {
                var settings=new LauncherSettings {ControlPanel=new ControlPanelPreferences {Tab=2}};
                using(var panel=new TeamWindow(()=>new List<ClientObservation>(),()=>settings)) {
                    var tabs=Descendants(panel).OfType<TabControl>().Single(c=>c.Name=="teamDamageTabs");
                    panel.RefreshWall();
                    Assert.AreEqual(1,tabs.TabCount);
                    Assert.AreEqual(2,settings.ControlPanel.Tab);
                    Assert.IsFalse(panel.CaptureCombatReports().Any(r=>r.Command.Contains("npc") || r.Command.Contains("all")));
                    settings.ExperimentalNearbyCombat=true;panel.RefreshWall();
                    Assert.AreEqual(3,tabs.TabCount);
                    Assert.AreEqual(2,tabs.SelectedIndex);
                    settings.ExperimentalNearbyCombat=false;panel.RefreshWall();
                    Assert.AreEqual(1,tabs.TabCount);
                    Assert.AreEqual(0,tabs.SelectedIndex);
                    Assert.AreEqual(2,settings.ControlPanel.Tab);
                }
            });
        }

        [TestMethod]
        public void SettingsDraftDoesNotEnableExperimentsUntilSaved()
        {
            OnSta(()=> {
                var settings=new LauncherSettings();
                using(var dialog=new SettingsDialog(settings)) {
                    var checks=Descendants(dialog).OfType<CheckBox>().ToList();
                    foreach(var name in new[]{"ExperimentalAovk","ExperimentalTeamAutomation","ExperimentalNearbyCombat"})
                        checks.Single(c=>c.Name==name).Checked=true;
                    Assert.IsTrue(dialog.ExperimentalAovk && dialog.ExperimentalTeamAutomation && dialog.ExperimentalNearbyCombat);
                    Assert.IsFalse(settings.ExperimentalAovk || settings.ExperimentalTeamAutomation || settings.ExperimentalNearbyCombat);
                    Assert.AreEqual(7,dialog.settingsTabs.TabCount);
                }
            });
        }

        [TestMethod]
        public void RecommendedSettingsAreADraftAndPreserveOptIns()
        {
            OnSta(() => {
                var settings = new LauncherSettings {
                    QuietLaunch = false, FastStartup = false, MonitorClientHealth = false,
                    CollectClientExitReports = false, KeepDiagnostics = false, MaxLogSizeMb = 40,
                    CheckForUpdatesOnStartup = false, CheckServerRulesOnStartup = false,
                    SkipTrayConfirmation = true, SkipQuitConfirmation = true,
                    SkipLoginConfirmation = true, SkipTeamLogoutConfirmation = true,
                    ExperimentalAovk = true, ExperimentalTeamAutomation = true,
                    ExperimentalNearbyCombat = true, MinimizeInactiveClients = true, Language = "da"
                };
                string before = SettingsSnapshot(settings);
                using (var dialog = new SettingsDialog(settings)) {
                    int actions = 0;
                    dialog.StartWithWindows = true;
                    dialog.SaveStartupAction = enabled => { actions++; return null; };
                    dialog.ClearDiagnosticsAction = () => { actions++; return true; };
                    dialog.ForgetPasswordsAction = () => { actions++; return true; };
                    dialog.ApplyRecommendedSettings();
                    dialog.ApplyRecommendedSettings();
                    Assert.IsTrue(dialog.QuietLaunch && dialog.FastStartup && dialog.MonitorClientHealth);
                    Assert.IsTrue(dialog.CollectClientExitReports && dialog.KeepDiagnostics);
                    Assert.IsTrue(dialog.CheckForUpdatesOnStartup && dialog.CheckServerRulesOnStartup);
                    Assert.AreEqual(5, dialog.MaxLogSizeMb);
                    Assert.IsFalse(dialog.SkipTrayConfirmation || dialog.SkipQuitConfirmation ||
                        dialog.SkipLoginConfirmation || dialog.SkipTeamLogoutConfirmation);
                    Assert.IsTrue(dialog.ExperimentalAovk && dialog.ExperimentalTeamAutomation &&
                        dialog.ExperimentalNearbyCombat && dialog.MinimizeInactiveClients);
                    Assert.IsTrue(dialog.StartWithWindows);
                    Assert.AreEqual("da", dialog.Language);
                    Assert.AreEqual(before, SettingsSnapshot(settings));
                    Assert.AreEqual(0, actions);
                    Assert.AreEqual(DialogResult.None, dialog.DialogResult);
                }
                Assert.AreEqual(before, SettingsSnapshot(settings));
                using (var dialog = new SettingsDialog(new LauncherSettings())) {
                    dialog.ApplyRecommendedSettings();
                    Assert.IsFalse(dialog.ExperimentalAovk || dialog.ExperimentalTeamAutomation ||
                        dialog.ExperimentalNearbyCombat || dialog.MinimizeInactiveClients);
                }
            });
        }

        static string SettingsSnapshot(LauncherSettings settings)
        {
            var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
            return serializer.Serialize(SortJson(serializer.DeserializeObject(serializer.Serialize(settings))));
        }

        static object SortJson(object value)
        {
            if (value is IDictionary<string, object> map)
                return new SortedDictionary<string, object>(map.ToDictionary(pair => pair.Key, pair => SortJson(pair.Value)));
            if (value is object[] array) return array.Select(SortJson).ToArray();
            return value;
        }

        static IEnumerable<Control> Descendants(Control control)
        {
            foreach(Control child in control.Controls) { yield return child; foreach(var nested in Descendants(child)) yield return nested; }
        }
        static void OnSta(Action action)
        {
            Exception error=null;
            var thread=new Thread(()=>{try {action();} catch(Exception e){error=e;}});
            thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();
            if(error!=null) throw new AssertFailedException(error.ToString(),error);
        }
    }
}
