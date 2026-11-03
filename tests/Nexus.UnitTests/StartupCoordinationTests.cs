using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AOLauncher;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nexus.Core;

namespace Nexus.Tests
{
    [TestClass]
    public class StartupCoordinationTests
    {
        [TestMethod]
        public async Task BlockedTeamWorkDoesNotBlockPluginsOrQueueDuplicatePolls()
        {
            var polling = new StartupPolling();
            var release = new TaskCompletionSource<bool>();
            int teamCalls = 0, pluginCalls = 0;
            var blocked = polling.Teams(async () => { teamCalls++; await release.Task; });
            try {
                await polling.Teams(() => { teamCalls++; return Task.CompletedTask; });
                await polling.Plugins(() => { pluginCalls++; return Task.CompletedTask; });
                await polling.Plugins(() => { pluginCalls++; return Task.CompletedTask; });
                Assert.IsFalse(blocked.IsCompleted);
                Assert.AreEqual(1, teamCalls);
                Assert.AreEqual(2, pluginCalls);
            } finally { release.SetResult(true); await blocked; }
            await polling.Teams(() => { teamCalls++; return Task.CompletedTask; });
            Assert.AreEqual(2, teamCalls);
        }

        [TestMethod]
        public async Task PollFailureReleasesOnlyItsOwnLane()
        {
            var polling = new StartupPolling();
            try { await polling.Plugins(() => { throw new InvalidOperationException("fixture"); }); Assert.Fail("Expected failure"); }
            catch (InvalidOperationException) { }
            bool called = false;
            await polling.Plugins(() => { called = true; return Task.CompletedTask; });
            Assert.IsTrue(called);
        }

        sealed class FormationFixture
        {
            internal readonly LauncherSettings Settings = new LauncherSettings {ExperimentalTeamAutomation=true,AutoInjectPlugins=true,IncludeShortcuts=true};
            internal readonly LaunchPreset Preset;
            internal readonly List<PluginAutoInjector.Row> Rows;
            internal DateTime Now = DateTime.UtcNow;
            internal readonly AutoTeamStartup Startup;
            internal readonly List<int> Reads = new List<int>();
            internal readonly List<Tuple<int,string>> Sent = new List<Tuple<int,string>>();
            internal string Reply = "WAIT";
            internal FormationFixture()
            {
                AOLauncher.Localization.Texts.SetLanguage("en");
                Settings.Accounts.Add(new AccountConfig {Id="a",Server="fixture",Port=7500,Characters=new List<CharacterConfig> {
                    new CharacterConfig {CharacterId=1,Name="Tank",Selected=true}, new CharacterConfig {CharacterId=2,Name="Doctor",Selected=true}}});
                Preset = PresetService.Create(Settings,"Fixture team"); Preset.AutoFormTeam=true;
                Rows = Settings.Accounts[0].Characters.Select(c=>new PluginAutoInjector.Row {
                    AccountId="a",CharacterId=c.CharacterId,CharacterName=c.Name,Pid=c.CharacterId,StartTicks=c.CharacterId,State="Loaded"}).ToList();
                Startup = new AutoTeamStartup(Preset) {Now=()=>Now,SameProcess=r=>true,
                    ReadStatus=pid=>{Reads.Add(pid);return Reply;},
                    SendPlan=(pid,line)=>{Sent.Add(Tuple.Create(pid,line));return "OK queued";}};
            }
            internal List<string> Poll() { Now=Now.AddSeconds(3); return Startup.Poll(Settings,Rows); }
        }

        [TestMethod]
        public void HealthOnlyWarmupAndProfessionProbeNeverContactTeamHelpers()
        {
            var f = new FormationFixture();
            f.Rows[0].StartupPending=true; f.Rows[0].ShortcutsRequested=true;
            // Second client has only the monitor, even though host dispatch says Loaded.
            f.Poll();
            Assert.AreEqual(0,f.Reads.Count);
            Assert.AreEqual(0,f.Sent.Count);
            foreach(var row in f.Rows) {row.StartupPending=false;row.ShortcutsRequested=true;}
            f.Poll();
            CollectionAssert.AreEqual(new[]{2,1},f.Sent.Select(s=>s.Item1).ToArray());
        }

        [TestMethod]
        public void NewlyArmedFollowerReachesLeaderWithinQuarterSecondAndSurvivesBriefPipeGap()
        {
            var f = new FormationFixture();
            foreach (var row in f.Rows) row.ShortcutsRequested=true;
            f.Poll();
            string session=f.Sent.First().Item2.Split(' ')[1];
            f.Startup.ReadStatus=pid=>session+(pid==2 ? " Waiting for leader invitation" : " Waiting for remaining members");
            f.Sent.Clear();
            f.Now=f.Now.AddMilliseconds(250);
            f.Startup.Poll(f.Settings,f.Rows);
            Assert.AreEqual("1,2",f.Sent.Single(s=>s.Item1==1).Item2.Split(' ')[7]);
            f.Sent.Clear();
            f.Startup.ReadStatus=pid=>pid==2 ? null : session+" Waiting for remaining members";
            f.Now=f.Now.AddMilliseconds(250);
            f.Startup.Poll(f.Settings,f.Rows);
            Assert.AreEqual(0,f.Sent.Count,"A brief pipe gap must not withdraw the armed follower");
            f.Now=f.Now.AddSeconds(4);
            f.Startup.Poll(f.Settings,f.Rows);
            Assert.AreEqual("1",f.Sent.Single(s=>s.Item1==1).Item2.Split(' ')[7],"Stale readiness expires");
        }

        [TestMethod]
        public void AbsentHelperDoesNotSendPlansOrClaimAnOutdatedPlugin()
        {
            var f = new FormationFixture();
            foreach(var row in f.Rows) row.ShortcutsRequested=true;
            f.Reply=null;
            var messages=f.Poll();
            Assert.AreEqual(2,messages.Count);
            Assert.IsTrue(messages.All(m=>m.Contains("waiting for Nexus Shortcuts")));
            Assert.IsFalse(messages.Any(m=>m.Contains("restart") || m.Contains("updat")));
            Assert.AreEqual(0,f.Sent.Count);
            Assert.AreEqual(0,f.Poll().Count); // Stable pending state is not logged each tick.
            f.Reply="WAIT";f.Poll();
            Assert.AreEqual(2,f.Sent.Count); // A responsive helper can be armed without a relog.
        }

        [TestMethod]
        public void UnsupportedHelperWarningRequiresAnExplicitUnsupportedResponse()
        {
            var f = new FormationFixture();
            foreach(var row in f.Rows) row.ShortcutsRequested=true;
            f.Reply="ERROR unknown request";
            Assert.IsTrue(f.Poll().All(m=>m.Contains("does not support team requests")));
            Assert.AreEqual(0,f.Sent.Count);
        }

        [TestMethod]
        public void FormationTimeoutNamesUnconfirmedMembersAndCancelsQueuedPlans()
        {
            var f = new FormationFixture();
            foreach(var row in f.Rows) row.ShortcutsRequested=true;
            f.Poll(); f.Sent.Clear();
            f.Now=f.Now.AddMinutes(6);
            var messages=f.Poll();
            Assert.AreEqual(1,messages.Count);
            StringAssert.Contains(messages[0],"Not confirmed: Doctor");
            Assert.IsTrue(f.Sent.Count==2 && f.Sent.All(s=>s.Item2.StartsWith("TEAMCANCEL ")));
            Assert.IsFalse(f.Startup.Active);
        }

        [TestMethod]
        public void PluginReadyRequiresObservedFullSetAndNeverUsesDispatchAlone()
        {
            var checks = new List<PluginCheck> {new PluginCheck {Status=PluginStatus.Confirmed},new PluginCheck {Status=PluginStatus.Running}};
            Assert.IsTrue(PluginAutoInjector.StartupConfirmed("Loaded",false,false,checks));
            Assert.IsFalse(PluginAutoInjector.StartupConfirmed("Loaded",true,false,checks));
            Assert.IsFalse(PluginAutoInjector.StartupConfirmed("Loaded",false,true,checks));
            Assert.IsFalse(PluginAutoInjector.StartupConfirmed("Loaded",false,false,new List<PluginCheck>()));
            checks[1].Status=PluginStatus.Pending;
            Assert.IsFalse(PluginAutoInjector.StartupConfirmed("Loaded",false,false,checks));
            checks[1].Status=PluginStatus.Failed;
            Assert.IsFalse(PluginAutoInjector.StartupConfirmed("Loaded",false,false,checks));
            Assert.IsFalse(PluginAutoInjector.RequestsShortcuts(new[]{"NexusHealthMonitor.dll"}));
            Assert.IsTrue(PluginAutoInjector.RequestsShortcuts(new[]{@"C:\fixture\NexusShortcuts.dll"}));
        }

        [TestMethod]
        public void SixAbsentHelperPipesDoNotAccumulateConnectionTimeouts()
        {
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            // Deliberately outside the process IDs of any game client; no helper is started.
            for (int i=0;i<6;i++) {
                Assert.IsNull(ShortcutClient.TeamStatus(2000000000+i));
                Assert.IsNull(ShortcutClient.TeamView(2000000000+i));
            }
            Assert.IsTrue(elapsed.ElapsedMilliseconds < 1500,
                "Missing helpers accumulated waits: " + elapsed.ElapsedMilliseconds + " ms");
        }

        [TestMethod]
        public void RecoveryAlsoSkipsClientsThatOnlyHaveTheMonitor()
        {
            var f = new FormationFixture();
            int reads=0;
            var recovery = new TeamRecoveryController {IsAlive=r=>true,ReadView=pid=>{reads++;return "WAIT";}};
            recovery.Poll(f.Settings,f.Rows);
            Assert.AreEqual(0,reads);
        }
    }
}
