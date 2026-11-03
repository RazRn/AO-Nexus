using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace NexusEngine
{
    internal static class ProcessLoaderReadiness
    {
        internal static long Wait(Process process)
        {
            var clock = Stopwatch.StartNew();
            WaitUntil(() => process.HasExited, () => {
                // Reloaded.Injector 1.2.5 indexes Process.Modules[0] in its
                // constructor. Immediately after Start(), the loader can still
                // expose an empty list. Refresh before probing; leave the valid
                // module collection cached for the injector on this Process.
                process.Refresh();
                return process.Modules.Count > 0 && process.Modules.Cast<ProcessModule>()
                    .Any(module => string.Equals(module.ModuleName, "kernel32.dll", StringComparison.OrdinalIgnoreCase));
            }, () => clock.ElapsedMilliseconds, Thread.Sleep);
            return clock.ElapsedMilliseconds;
        }

        internal static void WaitUntil(Func<bool> exited, Func<bool> ready, Func<long> elapsed, Action<int> delay)
        {
            while (true)
            {
                if (exited()) throw new InvalidOperationException("Game client exited before its Windows loader was ready.");
                try { if (ready()) return; }
                catch (Win32Exception ex) when (ex.NativeErrorCode == 299) { /* Loader snapshot changed; retry briefly. */ }
                if (elapsed() >= 5000) throw new TimeoutException("Game client Windows loader did not become ready within five seconds.");
                delay(5);
            }
        }
    }
}
