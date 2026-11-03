using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nexus.Core;

namespace Nexus.Tests
{
    [TestClass]
    public class CoreTests
    {
        string directory;
        [TestInitialize] public void CreateDirectory() => directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "Nexus.Core.Tests-" + Guid.NewGuid().ToString("N"))).FullName;
        [TestCleanup] public void Cleanup() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

        [TestMethod]
        public void CoreIsASeparateAssemblyWithoutUIOrLauncherDependencies()
        {
            var core = typeof(LaunchPlan).Assembly;
            Assert.AreEqual("Nexus.Core", core.GetName().Name);
            Assert.IsFalse(core.GetReferencedAssemblies().Any(a => a.Name == "System.Windows.Forms" || a.Name == "AONexus"));
        }

        [TestMethod]
        public void PresetOrderPreservesInstancesAndRejectsStaleOrDuplicateMembership()
        {
            var selected = new[] { "doc", "enf", "crat" };
            CollectionAssert.AreEqual(new[] { "enf", "doc", "crat" }, LaunchPlan.InPresetOrder(selected, new[] { "enf", "doc", "crat" }, c => c));
            Assert.IsNull(LaunchPlan.InPresetOrder(selected, new[] { "enf", "doc" }, c => c));
            Assert.IsNull(LaunchPlan.InPresetOrder(selected, new[] { "enf", "doc", "doc" }, c => c));
            Assert.IsNull(LaunchPlan.InPresetOrder(selected, new[] { "enf", "doc", "mp" }, c => c));
        }

        [TestMethod]
        public void CurrentSettingsAreAuthoritativeEvenWhenTheyAreADirectory()
        {
            File.WriteAllText(Path.Combine(directory, "RebirthLauncher.json"), "legacy");
            Assert.AreEqual("RebirthLauncher.json", Path.GetFileName(SettingsStorage.ReadPath(directory)));
            Directory.CreateDirectory(Path.Combine(directory, "AONexus.json"));
            Assert.AreEqual("AONexus.json", Path.GetFileName(SettingsStorage.ReadPath(directory)));
            Assert.ThrowsExactly<UnauthorizedAccessException>(() => File.ReadAllText(SettingsStorage.ReadPath(directory)));
        }

        [TestMethod]
        public void SettingsBackupAndAtomicReplacementPreserveTheOriginal()
        {
            SettingsStorage.Save(directory, "old", text => false);
            SettingsStorage.Save(directory, "new", text => text == "old");
            Assert.AreEqual("new", File.ReadAllText(Path.Combine(directory, "AONexus.json")));
            Assert.AreEqual("old", File.ReadAllText(Path.Combine(directory, "AONexus.json.before-account-targets.bak")));
            SettingsStorage.Save(directory, "newer", text => true);
            Assert.AreEqual("old", File.ReadAllText(Path.Combine(directory, "AONexus.json.before-account-targets.bak")));
            Assert.AreEqual(0, Directory.GetFiles(directory, "*.tmp").Length);
        }

        [TestMethod]
        public void FailedSettingsValidationLeavesTheWorkingFileUntouched()
        {
            SettingsStorage.Save(directory, "old", text => false);
            Assert.ThrowsExactly<InvalidDataException>(() => SettingsStorage.Save(directory, "new", text => throw new InvalidDataException("bad previous schema")));
            Assert.AreEqual("old", File.ReadAllText(Path.Combine(directory, "AONexus.json")));
            Assert.AreEqual(0, Directory.GetFiles(directory, "*.tmp").Length);
        }

        [TestMethod]
        public void GraphicsFailureRestoresExistingFilesAndRemovesNewFiles()
        {
            string existing = Path.Combine(directory, "existing.ini"), created = Path.Combine(directory, "new.ini");
            File.WriteAllBytes(existing, new byte[] { 1, 2, 3 });
            var writes = new List<GraphicsTransaction.Write>
            {
                new GraphicsTransaction.Write { Path = existing, Before = new byte[] { 1, 2, 3 }, After = new byte[] { 4 } },
                new GraphicsTransaction.Write { Path = created, After = new byte[] { 5 } }
            };
            Assert.ThrowsExactly<IOException>(() => GraphicsTransaction.Commit(writes, i => { if (i == 1) throw new IOException("simulated write failure"); }));
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, File.ReadAllBytes(existing));
            Assert.IsFalse(File.Exists(created));
        }

        [TestMethod]
        public void GraphicsConcurrentChangePreventsAllWrites()
        {
            string first = Path.Combine(directory, "first"), second = Path.Combine(directory, "second");
            File.WriteAllBytes(first, new byte[] { 1 }); File.WriteAllBytes(second, new byte[] { 9 });
            var writes = new List<GraphicsTransaction.Write>
            {
                new GraphicsTransaction.Write { Path = first, Before = new byte[] { 1 }, After = new byte[] { 2 } },
                new GraphicsTransaction.Write { Path = second, Before = new byte[] { 1 }, After = new byte[] { 2 } }
            };
            Assert.ThrowsExactly<IOException>(() => GraphicsTransaction.Commit(writes));
            CollectionAssert.AreEqual(new byte[] { 1 }, File.ReadAllBytes(first));
            CollectionAssert.AreEqual(new byte[] { 9 }, File.ReadAllBytes(second));
        }

        [TestMethod]
        public async Task StoppingBeforeLaunchDoesNotStartAnything()
        {
            using (var runner = new BackendRunner())
            {
                runner.Stop();
                Assert.AreEqual(BackendRunner.StoppedExitCode, await runner.RunAsync(new ProcessStartInfo("does-not-exist.exe"), "private", null, null));
            }
        }

        [TestMethod]
        public async Task BackendExitAndCallbackFailuresAreObservable()
        {
            var reports = new List<string>();
            using (var runner = new BackendRunner(report: (operation, error) => { lock (reports) reports.Add(operation); }))
            {
                int code = await runner.RunAsync(Fixture("[Console]::WriteLine('ready'); exit 7"), "private test input", line => throw new InvalidOperationException("callback closed"), null);
                Assert.AreEqual(7, code);
                CollectionAssert.Contains(reports, "output-callback");
            }
        }

        [TestMethod]
        public async Task BackendCancellationStopsOnlyTheOwnedProcess()
        {
            var ready = new TaskCompletionSource<bool>();
            using (var runner = new BackendRunner())
            {
                var run = runner.RunAsync(Fixture("[Console]::WriteLine('ready'); Start-Sleep -Seconds 20"), "private", line => ready.TrySetResult(true), null);
                Assert.AreSame(ready.Task, await Task.WhenAny(ready.Task, Task.Delay(10000)), "Fixture did not start.");
                runner.Stop();
                Assert.AreSame(run, await Task.WhenAny(run, Task.Delay(5000)), "Cancellation must not wait for the fixture timeout.");
                Assert.AreEqual(BackendRunner.StoppedExitCode, await run);
            }
        }

        static ProcessStartInfo Fixture(string body) => new ProcessStartInfo(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
            "-NoProfile -NonInteractive -Command \"$null = [Console]::ReadLine(); " + body + "\"");
    }
}
