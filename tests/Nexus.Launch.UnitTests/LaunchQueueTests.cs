using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NexusEngine;

[assembly: DoNotParallelize]
namespace Nexus.Tests
{
    [TestClass]
    public class LaunchQueueTests
    {
        static CharacterLaunch Character(string account, int id) => new CharacterLaunch
        { Account = account, AccountId = "saved-" + id, CharacterId = id, Server = "192.0.2.1", Port = 7500, Password = "fixture-secret" };

        [TestMethod]
        public async Task DifferentAccountsRunInParallelButOneAccountNeverOverlaps()
        {
            var first = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondAccount = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var seen = new ConcurrentQueue<int>();
            var characters = new[] { Character("alpha", 1), Character("ALPHA", 2), Character("beta", 3) };
            var run = LaunchQueue.RunAccountQueuesAsync(characters, async c =>
            {
                seen.Enqueue(c.CharacterId);
                if (c.CharacterId == 1) await first.Task;
                if (c.CharacterId == 3) secondAccount.TrySetResult(true);
                return true;
            });
            try
            {
                Assert.AreSame(secondAccount.Task, await Task.WhenAny(secondAccount.Task, Task.Delay(5000)));
                Assert.IsFalse(seen.Contains(2), "A second character cannot overtake its own account's admission.");
            }
            finally { first.TrySetResult(true); }
            Assert.AreEqual(0, await run);
            Assert.IsTrue(Array.IndexOf(seen.ToArray(), 1) < Array.IndexOf(seen.ToArray(), 2));
            Assert.IsTrue(characters.All(c => c.Password == null));
        }

        [TestMethod]
        public async Task OrderedPresetKeepsOrderAndStopsAfterFailure()
        {
            var characters = new[] { Character("tank", 3), Character("doc", 1), Character("crat", 2) };
            var seen = new ConcurrentQueue<int>();
            int result = await LaunchQueue.RunAccountQueuesAsync(characters, c =>
            {
                seen.Enqueue(c.CharacterId);
                return Task.FromResult(c.CharacterId != 1);
            }, ordered: true);
            Assert.AreEqual(2, result);
            CollectionAssert.AreEqual(new[] { 3, 1 }, seen.ToArray());
            Assert.IsTrue(characters.All(c => c.Password == null), "Unstarted entries must also release credentials.");
        }

        [TestMethod]
        public async Task CallbackExceptionDoesNotLeakCredentialsOrStopOtherAccounts()
        {
            var characters = new[] { Character("alpha", 1), Character("alpha", 2), Character("beta", 3) };
            var seen = new ConcurrentQueue<int>();
            var original = Console.Error;
            using var captured = new StringWriter();
            Console.SetError(captured);
            try
            {
                Assert.AreEqual(2, await LaunchQueue.RunAccountQueuesAsync(characters, c =>
                {
                    seen.Enqueue(c.CharacterId);
                    if (c.CharacterId == 1) throw new InvalidOperationException("fixture-secret must never be logged");
                    return Task.FromResult(true);
                }));
            }
            finally { Console.SetError(original); }
            Assert.IsTrue(seen.Contains(3)); Assert.IsFalse(seen.Contains(2));
            Assert.IsFalse(captured.ToString().Contains("fixture-secret"));
            Assert.IsTrue(characters.All(c => c.Password == null));
        }

        static LaunchTarget Target(string host)
        {
            // Build a validated-target fixture without touching an installation or loading native code.
            var target = new LaunchTarget();
            typeof(LaunchTarget).GetProperty(nameof(LaunchTarget.Server)).SetValue(target, host);
            typeof(LaunchTarget).GetProperty(nameof(LaunchTarget.Port)).SetValue(target, 7500);
            return target;
        }

        [TestMethod]
        public void PipelineRequiresExplicitOptInAndTheVerifiedEndpoint()
        {
            var request = new LaunchRequest { AcceptancePipeline = true, FastStartup = true };
            var supported = Target("2.24.96.30");
            Assert.IsTrue(LaunchQueue.CanPipeline(request, supported, true));
            Assert.IsFalse(LaunchQueue.CanPipeline(request, supported, false));
            Assert.IsFalse(LaunchQueue.CanPipeline(request, Target("192.0.2.1"), true));
            request.FastStartup = false;
            Assert.IsFalse(LaunchQueue.CanPipeline(request, supported, true));
        }

        [TestMethod]
        public async Task MissingAdmissionTelemetryWaitsForFullConfirmation()
        {
            var confirmation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var admission = LaunchQueue.AdmissionOrConfirmationAsync(confirmation.Task, null);
            Assert.IsFalse(admission.IsCompleted);
            confirmation.SetResult(false);
            Assert.IsFalse(await admission);
        }

        [TestMethod]
        public void FocusRecoveryRespectsAnotherApplication()
        {
            Assert.IsTrue(PresetLaunchWindows.MayRestoreLeader(2, 10, 1, new[] { 1, 2, 3 }));
            Assert.IsFalse(PresetLaunchWindows.MayRestoreLeader(77, 10, 1, new[] { 1, 2, 3 }));
            Assert.IsTrue(PresetLaunchWindows.ShouldSuspendFocus(true, 2, 10, 1, new[] { 1, 2, 3 }));
            Assert.IsFalse(PresetLaunchWindows.ShouldSuspendFocus(true, 1, 10, 1, new[] { 1, 2, 3 }));
        }
    }
}
