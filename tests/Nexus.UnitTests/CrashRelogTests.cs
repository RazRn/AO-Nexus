using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nexus.Core;
using AOLauncher;

namespace Nexus.UnitTests
{
    [TestClass]
    public class CrashRelogTests
    {
        static readonly DateTime Now = new DateTime(2026, 11, 3, 19, 0, 0, DateTimeKind.Utc);
        const int AccessViolation = unchecked((int)0xc0000005);
        static CrashRelogPolicy Enabled() { var policy = new CrashRelogPolicy(); policy.SetEnabled(true); return policy; }
        static void Crash(CrashRelogPolicy policy, string instance, DateTime now)
        { policy.ObserveAlive("char", instance); policy.ObserveExit("char", instance, AccessViolation, now, now); }

        [TestMethod] public void NormalForcedAndUnknownExitsAreNotCrashes()
        {
            foreach (int? code in new int?[] { null, 0, 1, -1, 259, unchecked((int)0xc000013a), unchecked((int)0xc0000142) })
                Assert.IsFalse(CrashRelogPolicy.IsCrash(code));
            Assert.IsTrue(CrashRelogPolicy.IsCrash(AccessViolation));
            Assert.IsTrue(CrashRelogPolicy.IsCrash(unchecked((int)0xe0434352)));
        }
        [TestMethod] public void NeverRecoversHistoricalOrUnobservedExit()
        {
            var p = Enabled();
            Assert.IsNull(p.ObserveExit("char", "old", AccessViolation, Now, Now));
            p.ObserveAlive("char", "stale");
            Assert.IsNull(p.ObserveExit("char", "stale", AccessViolation, Now.AddMinutes(-1), Now));
            Assert.IsNull(p.Next(Now.AddMinutes(1)));
        }
        [TestMethod] public void DebouncesExitAndWaitsTenSeconds()
        {
            var p = Enabled(); p.ObserveAlive("char", "one");
            Assert.AreEqual("Scheduled", p.ObserveExit("char", "one", AccessViolation, Now, Now));
            Assert.IsNull(p.ObserveExit("char", "one", AccessViolation, Now, Now.AddSeconds(9)));
            Assert.IsNull(p.Next(Now.AddSeconds(9)));
            var job = p.Next(Now.AddSeconds(10)); Assert.IsNotNull(job);
            Assert.IsTrue(p.BeginAttempt(job, Now.AddSeconds(10)));
            Assert.IsFalse(p.BeginAttempt(job, Now.AddSeconds(11)));
        }
        [TestMethod] public void LimitsCrashLoopAndUsesLongerSecondDelay()
        {
            var p = Enabled(); Crash(p,"one",Now); p.BeginAttempt(p.Next(Now.AddSeconds(10)),Now.AddSeconds(10));
            Crash(p,"two",Now.AddSeconds(20)); Assert.IsNull(p.Next(Now.AddSeconds(49)));
            Assert.IsTrue(p.BeginAttempt(p.Next(Now.AddSeconds(50)),Now.AddSeconds(50)));
            p.ObserveAlive("char","three");
            Assert.AreEqual("Limit",p.ObserveExit("char","three",AccessViolation,Now.AddMinutes(1),Now.AddMinutes(1)));
            Assert.IsNull(p.Next(Now.AddMinutes(2)));
            Crash(p,"four",Now.AddMinutes(11)); Assert.IsNotNull(p.Next(Now.AddMinutes(11).AddSeconds(10)));
        }
        [TestMethod] public void LogoutSuppressionWinsEvenIfClosingClientCrashes()
        {
            var p = Enabled(); p.ObserveAlive("char","one"); p.Suppress("char","one");
            Assert.IsNull(p.ObserveExit("char","one",AccessViolation,Now,Now)); Assert.IsNull(p.Next(Now.AddMinutes(1)));
        }
        [TestMethod] public void DisableCancelAndManualReplacementPreventQueuedRestart()
        {
            var p = Enabled(); Crash(p,"one",Now); p.ObserveAlive("char","replacement"); Assert.IsNull(p.Next(Now.AddSeconds(10)));
            Crash(p,"two",Now); p.SetEnabled(false); p.SetEnabled(true); Assert.IsNull(p.Next(Now.AddSeconds(10)));
            Crash(p,"three",Now); p.CancelAll(); Assert.IsNull(p.Next(Now.AddSeconds(10)));
        }
        [TestMethod] public void BusyQueueExpiresInsteadOfLaunchingMuchLater()
        { var p = Enabled(); Crash(p,"one",Now); Assert.IsNull(p.Next(Now.AddMinutes(2))); }
        [TestMethod] public void CharactersHaveIndependentRetryBudgets()
        {
            var p=Enabled(); Crash(p,"one",Now); p.BeginAttempt(p.Next(Now.AddSeconds(10)),Now.AddSeconds(10));
            p.ObserveAlive("other","other-process"); Assert.AreEqual("Scheduled",p.ObserveExit("other","other-process",AccessViolation,Now,Now));
            var job=p.Next(Now.AddSeconds(10)); Assert.AreEqual("other",job.CharacterKey);
        }
        [TestMethod] public void PendingOrZoningReplacementBlocksAnotherLaunch()
        {
            string path = System.IO.Path.GetTempFileName();
            try
            {
                using (var process = System.Diagnostics.Process.GetCurrentProcess())
                {
                    var crashed = new PresetCamping.Client { AccountIdentity="id:account", CharacterId=17, Server="fixture", Port=7500, GamePath=@"C:\Fixture" };
                    var replacement = new PresetCamping.Client { AccountIdentity=crashed.AccountIdentity, CharacterId=17, Server="fixture", Port=7500, GamePath=@"c:\fixture",
                        Pending=true, ProcessId=process.Id, StartTimeUtcTicks=process.StartTime.ToUniversalTime().Ticks };
                    var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
                    System.IO.File.WriteAllText(path, serializer.Serialize(new[] { replacement }));
                    Assert.IsTrue(MainForm.HasCrashRelogReplacement(crashed, path));
                    replacement.Pending=false; replacement.CharacterName="not the window title";
                    System.IO.File.WriteAllText(path, serializer.Serialize(new[] { replacement }));
                    Assert.IsTrue(MainForm.HasCrashRelogReplacement(crashed, path));
                    replacement.StartTimeUtcTicks--;
                    System.IO.File.WriteAllText(path, serializer.Serialize(new[] { replacement }));
                    Assert.IsFalse(MainForm.HasCrashRelogReplacement(crashed, path));
                    System.IO.File.WriteAllText(path,"invalid");
                    bool rejected=false; try { MainForm.HasCrashRelogReplacement(crashed,path); } catch { rejected=true; }
                    Assert.IsTrue(rejected);
                }
            }
            finally { System.IO.File.Delete(path); }
        }
        [TestMethod] public void SettingDefaultsOffAndPersistsWhenExplicitlyEnabled()
        {
            Assert.IsFalse(new LauncherSettings().AutoRelogCrashedClients);
            Assert.IsFalse(SettingsStore.Deserialize("{\"Version\":2,\"Accounts\":[]}").AutoRelogCrashedClients);
            Assert.IsTrue(SettingsStore.Deserialize("{\"Version\":2,\"Accounts\":[],\"AutoRelogCrashedClients\":true}").AutoRelogCrashedClients);
        }
    }
}
