using System;
using System.Threading;
using System.Threading.Tasks;

namespace NexusEngine
{
    // Events carry no account data. The child watches this worker's lifetime and
    // keeps its UI responsive while waiting; no sleep decides when to authenticate.
    internal sealed class LoginGate : IDisposable
    {
        internal string Name { get; } = @"Local\AONexus.LoginGate." + Guid.NewGuid().ToString("N");
        private readonly EventWaitHandle ready, cancel;
        private readonly Task release;
        private readonly object sync = new object();
        private readonly TaskCompletionSource<bool> stopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool disposed;
        internal LoginGate(Task<bool> previousAdmission)
        {
            ready = new EventWaitHandle(false, EventResetMode.ManualReset, Name + ".ready");
            try { cancel = new EventWaitHandle(false, EventResetMode.ManualReset, Name + ".cancel"); }
            catch { ready.Dispose(); throw; }
            release = ReleaseAsync(previousAdmission);
        }
        private async Task ReleaseAsync(Task<bool> previous)
        {
            bool admitted;
            try {
                await Task.WhenAny(previous, stopped.Task).ConfigureAwait(false);
                if (stopped.Task.IsCompleted) return;
                admitted = await previous.ConfigureAwait(false);
            }
            catch { admitted = false; }
            lock (sync) if (!disposed) (admitted ? ready : cancel).Set();
        }
        internal async Task<bool> UntilConfirmedAsync(Task<bool> confirmation)
        {
            try { return await confirmation.ConfigureAwait(false); }
            finally { Dispose(); }
        }
        public void Dispose() {
            lock (sync) {
                if (disposed) return;
                disposed = true;
                cancel.Set(); // A preloaded child must not authenticate after abandonment.
                stopped.TrySetResult(true);
                ready.Dispose(); cancel.Dispose();
            }
        }
    }
}
