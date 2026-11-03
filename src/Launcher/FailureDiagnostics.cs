using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AOLauncher
{
    // One bounded history per component. Diagnostics must not interrupt recovery,
    // and a failing poll must not append a stack trace every second.
    internal sealed class FailureDiagnostics
    {
        sealed class Entry
        {
            internal DateTime Written;
            internal string Signature;
            internal int Suppressed;
        }

        readonly object gate = new object();
        readonly Dictionary<string, Entry> failures = new Dictionary<string, Entry>();
        readonly Action<string, string> write;
        readonly Func<DateTime> clock;
        readonly TimeSpan interval;
        readonly int capacity;
        readonly string category;

        internal static readonly FailureDiagnostics Plugins = new FailureDiagnostics(DiagnosticJournal.Record);
        internal static readonly FailureDiagnostics Launch = new FailureDiagnostics(DiagnosticJournal.Record, category: "login");
        internal static readonly FailureDiagnostics Monitor = new FailureDiagnostics(DiagnosticJournal.Record, category: "monitor");
        internal static readonly FailureDiagnostics Install = new FailureDiagnostics(DiagnosticJournal.Record, category: "install");

        internal FailureDiagnostics(Action<string, string> write, Func<DateTime> clock = null,
            TimeSpan? interval = null, int capacity = 256, string category = "plugins")
        {
            this.category = category;
            this.write = write ?? throw new ArgumentNullException(nameof(write));
            this.clock = clock ?? (() => DateTime.UtcNow);
            this.interval = interval ?? TimeSpan.FromSeconds(30);
            this.capacity = Math.Max(1, capacity);
        }

        internal void Report(string operation, string outcome, Exception error = null,
            string detail = null, int pid = 0, long startTicks = 0, string plugin = null)
        {
            try
            {
                string key = Key(operation, pid, startTicks, plugin);
                string description = Clip(error == null ? detail : DiagnosticRedactor.Describe(error), 8192);
                string signature = outcome + "|" + description;
                int suppressed = 0;
                var now = clock();
                lock (gate)
                {
                    if (failures.TryGetValue(key, out var previous))
                    {
                        if (previous.Signature == signature && now >= previous.Written && now - previous.Written < interval)
                        {
                            if (previous.Suppressed < int.MaxValue) previous.Suppressed++;
                            return;
                        }
                        suppressed = previous.Suppressed;
                    }
                    else if (failures.Count >= capacity)
                        failures.Remove(failures.OrderBy(pair => pair.Value.Written).First().Key);

                    failures[key] = new Entry { Written = now, Signature = signature };
                }
                Emit(Context(operation, pid, startTicks, plugin) + " outcome=" + Clip(outcome, 512)
                    + " suppressed=" + suppressed + (string.IsNullOrEmpty(description) ? "" : "\n" + description));
            }
            catch (Exception) { /* A broken diagnostic sink must not change plugin loading. */ }
        }

        internal void Recovered(string operation, string outcome, int pid = 0, long startTicks = 0, string plugin = null)
        {
            try
            {
                Entry previous;
                lock (gate)
                {
                    string key = Key(operation, pid, startTicks, plugin);
                    if (!failures.TryGetValue(key, out previous)) return;
                    failures.Remove(key);
                }
                Emit(Context(operation, pid, startTicks, plugin) + " recovered=" + Clip(outcome, 512)
                    + " suppressed=" + previous.Suppressed);
            }
            catch (Exception) { /* Never recursively report a diagnostic failure. */ }
        }

        void Emit(string message) => write(category, DiagnosticRedactor.Current.Redact(message));
        static string Clip(string value, int length) => value == null ? "" : value.Substring(0, Math.Min(length, value.Length));
        static string Key(string operation, int pid, long startTicks, string plugin)
            => Clip(operation, 128) + "|" + pid + "|" + startTicks + "|" + Clip(plugin, 512);
        static string Context(string operation, int pid, long startTicks, string plugin)
            => "operation=" + Clip(operation, 128) + " pid=" + pid.ToString(CultureInfo.InvariantCulture)
                + " start=" + startTicks.ToString(CultureInfo.InvariantCulture)
                + (string.IsNullOrEmpty(plugin) ? "" : " plugin=" + Clip(plugin, 512));
    }
}
