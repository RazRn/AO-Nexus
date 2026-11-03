using System;
using System.Collections.Generic;
using System.Linq;

namespace Nexus.Core
{
    // No process creation or credentials here: decisions require an observed process exit.
    public sealed class CrashRelogPolicy
    {
        public sealed class Pending
        {
            public string CharacterKey { get; internal set; }
            public string InstanceKey { get; internal set; }
            public DateTime DueUtc { get; internal set; }
            public DateTime ExpiresUtc { get; internal set; }
        }
        readonly HashSet<string> observed = new HashSet<string>();
        readonly HashSet<string> handled = new HashSet<string>();
        readonly HashSet<string> intentional = new HashSet<string>();
        readonly Dictionary<string, Pending> pending = new Dictionary<string, Pending>();
        readonly Dictionary<string, List<DateTime>> attempts = new Dictionary<string, List<DateTime>>();
        bool enabled;
        public void SetEnabled(bool value)
        {
            if (!value) { pending.Clear(); observed.Clear(); }
            enabled = value;
        }
        public static bool IsCrash(int? exitCode)
        {
            if (!exitCode.HasValue) return false;
            switch (unchecked((uint)exitCode.Value))
            {
                case 0xc0000005: // access violation
                case 0xc000001d: // illegal instruction
                case 0xc0000094: // integer divide by zero
                case 0xc00000fd: // stack overflow
                case 0xc0000374: // heap corruption
                case 0xc0000409: // fail-fast / stack buffer overrun
                case 0xe06d7363: // unhandled C++ exception
                case 0xe0434352: // unhandled CLR exception
                    return true;
                default: return false; // Normal close, Task Manager termination and unknown exits stay closed.
            }
        }
        public void ObserveAlive(string character, string instance)
        {
            if (!enabled) return;
            observed.Add(instance);
            if (pending.TryGetValue(character, out var queued) && queued.InstanceKey != instance)
                pending.Remove(character); // A manual relaunch already replaced it.
        }
        public void Suppress(string character, string instance)
        {
            intentional.Add(instance);
            pending.Remove(character);
        }
        public string ObserveExit(string character, string instance, int? code, DateTime exitUtc, DateTime now)
        {
            if (!enabled || !handled.Add(instance)) return null;
            bool seen = observed.Remove(instance);
            if (!seen || intentional.Remove(instance) || !IsCrash(code) || exitUtc > now || now - exitUtc > TimeSpan.FromSeconds(30)) return null;
            var recent = Recent(character, now);
            if (recent.Count >= 2) return "Limit";
            pending[character] = new Pending { CharacterKey=character, InstanceKey=instance,
                DueUtc=now.AddSeconds(recent.Count == 0 ? 10 : 30), ExpiresUtc=now.AddMinutes(2) };
            return "Scheduled";
        }
        List<DateTime> Recent(string character, DateTime now)
        {
            if (!attempts.TryGetValue(character, out var history)) attempts[character] = history = new List<DateTime>();
            history.RemoveAll(t => t <= now.AddMinutes(-10));
            return history;
        }
        public Pending Next(DateTime now)
        {
            foreach (var stale in pending.Where(p => p.Value.ExpiresUtc <= now).Select(p => p.Key).ToArray()) pending.Remove(stale);
            return enabled ? pending.Values.Where(p => p.DueUtc <= now).OrderBy(p => p.DueUtc).FirstOrDefault() : null;
        }
        public void Cancel(string character) => pending.Remove(character);
        public void CancelAll() => pending.Clear();
        public bool BeginAttempt(Pending item, DateTime now)
        {
            if (!enabled || !pending.TryGetValue(item.CharacterKey, out var current) || !ReferenceEquals(current, item)
                || now < item.DueUtc || now >= item.ExpiresUtc) return false;
            pending.Remove(item.CharacterKey);
            var history = Recent(item.CharacterKey, now);
            if (history.Count >= 2) return false;
            history.Add(now);
            return true;
        }
    }
}
