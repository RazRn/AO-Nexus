using AOLauncher.Localization;
using System.Collections.Generic;
using System.Linq;

namespace NexusEngine
{
    // Per-client time includes its preload/admission wait. Team time is elapsed
    // wall time to the last world-ready report, never the sum of overlapping loads.
    internal sealed class LaunchTimings
    {
        private sealed class Timing
        {
            internal long StartedAtMs;
            internal long? WorldReadyMs;
            internal bool Confirmed;
        }
        private readonly object sync = new object();
        private readonly int selected;
        private readonly Dictionary<CharacterLaunch, Timing> started = new Dictionary<CharacterLaunch, Timing>();
        private readonly HashSet<CharacterLaunch> reused = new HashSet<CharacterLaunch>();

        internal LaunchTimings(int selected) { this.selected = selected; }
        internal void Begin(CharacterLaunch entry, long batchMs)
        {
            lock (sync) started.Add(entry, new Timing { StartedAtMs = batchMs });
        }
        internal void AlreadyOpen(CharacterLaunch entry) { lock (sync) reused.Add(entry); }
        internal void Observe(CharacterLaunch entry, string stage, long clientMs)
        {
            const string prefix = "world-ready meshes=";
            if (clientMs < 0 || stage == null || !stage.StartsWith(prefix, System.StringComparison.Ordinal)
                || !uint.TryParse(stage.Substring(prefix.Length), out _)) return;
            lock (sync)
                if (started.TryGetValue(entry, out var timing) && !timing.Confirmed && !timing.WorldReadyMs.HasValue)
                    timing.WorldReadyMs = clientMs;
        }
        internal string Confirmed(CharacterLaunch entry, string name, long clientMs)
        {
            lock (sync)
            {
                var timing = started[entry];
                if (timing.Confirmed) return null;
                timing.Confirmed = true;
                return timing.WorldReadyMs.HasValue
                    ? Texts.F("{0} loaded in {1:0.00} s.", name, timing.WorldReadyMs.Value / 1000.0)
                    : Texts.F("{0}: login confirmed after {1:0.00} s (world-ready timing unavailable).", name, clientMs / 1000.0);
            }
        }
        internal string Summary(bool succeeded, long elapsedMs)
        {
            lock (sync)
            {
                int confirmed = started.Values.Count(t => t.Confirmed) + reused.Count;
                if (succeeded && started.Count == 0 && reused.Count == selected)
                    return Texts.F("No clients needed loading; {0} already open.", reused.Count);
                if (succeeded && started.Count > 0 && confirmed == selected
                    && started.Values.All(t => t.Confirmed && t.WorldReadyMs.HasValue))
                {
                    double total = started.Values.Max(t => t.StartedAtMs + t.WorldReadyMs.Value) / 1000.0;
                    return reused.Count == 0
                        ? Texts.F(started.Count == 1 ? "{0} client loaded in {1:0.00} s total." : "All {0} clients loaded in {1:0.00} s total.", started.Count, total)
                        : Texts.F(started.Count == 1 ? "{0} new client loaded in {1:0.00} s total; {2} already open." : "{0} new clients loaded in {1:0.00} s total; {2} already open.", started.Count, total, reused.Count);
                }
                // Missing telemetry, pending clients and partial failures must
                // never masquerade as a complete rendered-world measurement.
                return Texts.F("Login run finished in {0:0.00} s; {1}/{2} clients confirmed.", elapsedMs / 1000.0, confirmed, selected);
            }
        }
    }
}
