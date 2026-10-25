using System;
using System.Collections.Generic;
using System.Linq;
using static AOLauncher.SidebarText;

namespace AOLauncher
{
    internal sealed class HealthSnapshot
    {
        internal LoginServerProfile Server;
        internal string Scope;
        internal HealthResult[] Results;
    }
    internal sealed class HealthObservation
    {
        internal string Scope, ServerName, Service;
        internal HealthState State;
        internal DateTime CheckedUtc;
        internal int Failures;
        internal bool Issue;
        internal string Label => ServerName + " · " + ServiceName(Service);
        internal static string ServiceName(string key) => key == "login" ? L("Login connection", "Loginforbindelse")
            : key == "zone" ? L("World / zone connection", "Verden / zoneforbindelse")
            : key == "chat" ? L("Chat connection", "Chatforbindelse") : L("Official service report", "Officiel servicerapport");
        internal string Status(DateTime now)
        {
            if (now - CheckedUtc > TimeSpan.FromSeconds(150) || State == HealthState.Unknown)
                return Issue ? L("Issue unresolved · no fresh confirmation", "Problem uafklaret · ingen ny bekræftelse") : L("Unknown", "Ukendt");
            if (Issue) return Service == "report" ? L("Reported issue", "Rapporteret problem") : L("Unreachable from this PC", "Utilgængelig fra denne pc");
            if (Failures > 0) return L("Failed check · confirming", "Fejlet tjek · bekræfter");
            return Service == "report" ? L("Reported online", "Rapporteret online") : L("Reachable", "Tilgængelig");
        }
    }
    internal sealed class HealthTransition
    {
        internal HealthObservation Observation;
        internal bool Recovered;
        internal string Text => Observation.Label + ": " + (Recovered ? L("recovered", "genoprettet")
            : Observation.Service == "report" ? L("reported issue", "rapporteret problem") : L("unreachable from this PC", "utilgængelig fra denne pc"));
    }
    // Session-local evidence. Unknown/stale reports never resolve an existing outage.
    internal sealed class HealthAlerts
    {
        private readonly Dictionary<string, HealthObservation> observations = new Dictionary<string, HealthObservation>(StringComparer.Ordinal);
        internal IEnumerable<HealthObservation> Observations => observations.Values;
        internal int IssueCount => observations.Values.Count(item => item.Issue);
        internal void Retain(IEnumerable<string> scopes)
        {
            var valid = new HashSet<string>(scopes, StringComparer.Ordinal);
            foreach (string key in observations.Where(pair => !valid.Contains(pair.Value.Scope)).Select(pair => pair.Key).ToArray()) observations.Remove(key);
        }
        internal List<HealthTransition> Apply(HealthSnapshot snapshot, DateTime now)
        {
            var changes = new List<HealthTransition>();
            foreach (var result in snapshot.Results.Where(item => item != null && new[] { "login", "zone", "chat", "report" }.Contains(item.Key)).GroupBy(item => item.Key).Select(group => group.First()))
            {
                string key = snapshot.Scope + "|" + result.Key;
                if (!observations.TryGetValue(key, out var item)) observations[key] = item = new HealthObservation { Scope = snapshot.Scope, Service = result.Key };
                item.ServerName = snapshot.Server.Name;
                if (result.CheckedUtc <= item.CheckedUtc) continue; // Re-rendering/replayed samples cannot confirm an outage.
                if (now - result.CheckedUtc > TimeSpan.FromSeconds(150) || result.CheckedUtc > now.AddSeconds(10)) continue;
                if (result.CheckedUtc - item.CheckedUtc > TimeSpan.FromSeconds(150)) item.Failures = 0;
                item.CheckedUtc = result.CheckedUtc; item.State = result.State;
                bool down = result.State == HealthState.Unreachable || result.State == HealthState.ReportedIssue;
                bool up = result.State == HealthState.Reachable || result.State == HealthState.ReportedOnline;
                if (down)
                {
                    item.Failures = Math.Min(2, item.Failures + 1);
                    if (item.Failures == 2 && !item.Issue) { item.Issue = true; changes.Add(new HealthTransition { Observation = item }); }
                }
                else
                {
                    item.Failures = 0;
                    if (up && item.Issue) { item.Issue = false; changes.Add(new HealthTransition { Observation = item, Recovered = true }); }
                }
            }
            return changes;
        }
    }
}
