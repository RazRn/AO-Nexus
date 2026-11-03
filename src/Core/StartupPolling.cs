using System;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Core
{
    // Independent lanes: a slow team helper cannot postpone the next plugin pass.
    // Ticks within a busy lane are coalesced, never queued or run concurrently.
    public sealed class StartupPolling
    {
        private readonly Lane plugins = new Lane();
        private readonly Lane teams = new Lane();

        public Task Plugins(Func<Task> poll) => plugins.Run(poll);
        public Task Teams(Func<Task> poll) => teams.Run(poll);

        private sealed class Lane
        {
            private int running;
            internal async Task Run(Func<Task> poll)
            {
                if (Interlocked.CompareExchange(ref running, 1, 0) != 0) return;
                try { await poll(); }
                finally { Volatile.Write(ref running, 0); }
            }
        }
    }
}
