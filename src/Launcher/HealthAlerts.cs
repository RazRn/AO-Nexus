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
        internal static string ServiceName(string key) => key == "login" ? AOLauncher.Localization.Texts.Get("Text_Login_connection_FCAB618BCA91")
            : key == "zone" ? AOLauncher.Localization.Texts.Get("Text_World_zone_connection_5913DA769000")
            : key == "chat" ? AOLauncher.Localization.Texts.Get("Text_Chat_connection_3F0928C8585A") : AOLauncher.Localization.Texts.Get("Text_Official_service_report_0B0A5458EEA6");
        internal string Status(DateTime now)
        {
            if (now - CheckedUtc > TimeSpan.FromSeconds(150) || State == HealthState.Unknown)
                return Issue ? AOLauncher.Localization.Texts.Get("Text_Issue_unresolved_no_fresh_confirmation_22BF3160FC7E") : AOLauncher.Localization.Texts.Get("Text_Unknown_D8D7AE0368FB");
            if (Issue) return Service == "report" ? AOLauncher.Localization.Texts.Get("Text_Reported_issue_A629ABE96E16") : AOLauncher.Localization.Texts.Get("Text_Unreachable_from_this_PC_227C73CAE214");
            if (Failures > 0) return AOLauncher.Localization.Texts.Get("Text_Failed_check_confirming_00036207BC98");
            return Service == "report" ? AOLauncher.Localization.Texts.Get("Text_Reported_online_E847C0DF1229") : AOLauncher.Localization.Texts.Get("Text_Reachable_54DB153474A9");
        }
    }
    internal sealed class HealthTransition
    {
        internal HealthObservation Observation;
        internal bool Recovered;
        internal string Text => Observation.Label + ": " + (Recovered ? AOLauncher.Localization.Texts.Get("Text_recovered_6CD4F048F2BA")
            : Observation.Service == "report" ? AOLauncher.Localization.Texts.Get("Text_reported_issue_09DF6D000C0D") : AOLauncher.Localization.Texts.Get("Text_unreachable_from_this_PC_B0C4C465DE77"));
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
