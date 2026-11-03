using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using static AOLauncher.SidebarText;

namespace AOLauncher
{
    internal sealed class AutoTeamStartup
    {
        readonly string session=Guid.NewGuid().ToString("N");
        readonly DateTime expires=DateTime.UtcNow.AddMinutes(5);
        readonly LaunchPreset preset;
        readonly Dictionary<int,string> sent = new Dictionary<int,string>(), reported = new Dictionary<int,string>();
        readonly Dictionary<int,long> starts = new Dictionary<int,long>();
        readonly Dictionary<int,DateTime> acknowledgedUntil = new Dictionary<int,DateTime>();
        DateTime nextPoll;
        volatile bool expired;
        readonly object gate = new object();
        internal bool Active=>!expired && Now()<expires;
        internal Func<int,string> ReadStatus=ShortcutClient.TeamStatus;
        internal Func<int,string,string> SendPlan=ShortcutClient.TeamPlan;
        internal Func<PluginAutoInjector.Row,bool> SameProcess=row=> {
            try { using(var p=Process.GetProcessById(row.Pid))return !p.HasExited && p.StartTime.ToUniversalTime().Ticks==row.StartTicks; }
            catch {return false;}
        };
        internal Func<DateTime> Now=()=>DateTime.UtcNow;
        internal AutoTeamStartup(LaunchPreset preset) { this.preset=preset; }
        internal static string Validate(LauncherSettings settings, List<PresetCharacter> members, bool loadPlugins)
        {
            if (members.Count < 2 || members.Count > 6 || members.Select(m=>m.CharacterId).Distinct().Count()!=members.Count)
                return AOLauncher.Localization.Texts.Get("Text_Automatic_teams_need_2_6_different_E96F17ECD6A7");
            if (settings.AOSharpDisabled || !settings.AutoInjectPlugins || !settings.IncludeShortcuts || !loadPlugins)
                return AOLauncher.Localization.Texts.Get("Text_Enable_AO_Auto_load_plugins_and_1F703C4204A9");
            var accounts=members.Select(m=>settings.Accounts.FirstOrDefault(a=>a.Id==m.AccountId)).ToList();
            if (accounts.Any(a=>a==null) || accounts.Select(a=>AccountTargets.Endpoint(a)).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=1)
                return AOLauncher.Localization.Texts.Get("Text_All_team_members_must_use_the_E34455B9C39B");
            foreach(var member in members) {
                var character=settings.Accounts.First(a=>a.Id==member.AccountId).Characters.FirstOrDefault(c=>c.CharacterId==member.CharacterId);
                if(character==null || (member.PluginSetId ?? character.PluginSetId)==PluginSet.None)
                    return AOLauncher.Localization.Texts.Get("Text_Every_member_needs_a_character_and_068073D44DEF");
            }
            return null;
        }
        internal List<string> Poll(LauncherSettings settings, List<PluginAutoInjector.Row> rows)
        {
            lock (gate) return PollCore(settings, rows);
        }
        List<string> PollCore(LauncherSettings settings, List<PluginAutoInjector.Row> rows)
        {
            var messages=new List<string>();
            if(expired || Now()<nextPoll)return messages;
            nextPoll=Now().AddMilliseconds(250);
            var current=settings.Presets.FirstOrDefault(p=>p.Id==preset.Id);
            if(!settings.ExperimentalTeamAutomation || current?.AutoFormTeam!=true || Validate(settings,preset.Characters,current.LoadPlugins)!=null) {
                Cancel(rows); return messages;
            }
            if(Now()>=expires) {
                var missing = preset.Characters.Where(m => m.CharacterId != preset.Characters[0].CharacterId
                    && (!reported.TryGetValue(m.CharacterId, out var state) || state != "Joined")).ToList();
                string names = string.Join(", ", missing.Select(m => MemberName(settings, m)));
                if (names.Length == 0) names = MemberName(settings, preset.Characters[0]) + " (" + AOLauncher.Localization.Texts.Get("Startup.LeaderRoster") + ")";
                messages.Add(string.Format(AOLauncher.Localization.Texts.Get("Startup.TeamTimeout"), preset.Name, names));
                Cancel(rows); return messages;
            }
            var ready=new List<PluginAutoInjector.Row>();
            foreach(var member in preset.Characters) {
                var matches=rows.Where(r=>r.AccountId==member.AccountId && r.CharacterId==member.CharacterId && PluginAutoInjector.CanUseShortcuts(r)).ToList();
                if(matches.Count!=1)continue;
                var row=matches[0];
                if(!SameProcess(row))continue;
                if(starts.TryGetValue(row.CharacterId,out var start) && start!=row.StartTicks)continue;
                starts[row.CharacterId]=row.StartTicks; ready.Add(row);
            }
            int leader=preset.Characters[0].CharacterId;
            string all=string.Join(",",preset.Characters.Select(m=>m.CharacterId));
            // A transient pipe reconnect must not withdraw an already acknowledged follower.
            // Keep this short and retain the process/start-time checks above.
            var armed=ready.Where(r=>acknowledgedUntil.TryGetValue(r.CharacterId,out var until) && Now()<until)
                .Select(r=>r.CharacterId).ToList();
            // Arm followers first, then give the leader only the IDs of followers that acknowledged.
            foreach(var row in ready.OrderBy(r=>r.CharacterId==leader ? 1 : 0)) {
                string status=ReadStatus(row.Pid);
                if (status == null || status == "BUSY" || status.StartsWith("ERROR", StringComparison.Ordinal)) {
                    ReportHelperWait(row, messages, status == "ERROR unknown request");
                    continue;
                }
                bool acknowledged=status?.StartsWith(session+" ",StringComparison.Ordinal)==true;
                if(acknowledged) {
                    string detail=status.Substring(session.Length+1);
                    if(detail=="Waiting for leader invitation" || detail=="Joined" || row.CharacterId==leader) {
                        acknowledgedUntil[row.CharacterId]=Now().AddSeconds(3);
                        if(!armed.Contains(row.CharacterId))armed.Add(row.CharacterId);
                    } else {
                        acknowledgedUntil.Remove(row.CharacterId);
                        armed.Remove(row.CharacterId);
                    }
                    if(!reported.TryGetValue(row.CharacterId,out var previous) || previous!=detail) {
                        reported[row.CharacterId]=detail; messages.Add(row.CharacterName+": auto team · "+detail);
                    }
                }
                var active= row.CharacterId==leader ? preset.Characters.Select(m=>m.CharacterId)
                    .Where(id=>id==leader || armed.Contains(id)).ToArray() : new[]{row.CharacterId};
                string payload="TEAMPLAN "+session+" "+row.StartTicks+" "+row.CharacterId+" "+leader+" "+expires.Ticks+" "+all+" "+string.Join(",",active);
                if(!acknowledged || !sent.TryGetValue(row.CharacterId,out var before) || before!=payload) {
                    string result=SendPlan(row.Pid,payload);
                    if(result=="OK queued") sent[row.CharacterId]=payload;
                    else ReportHelperWait(row, messages, result == "ERROR unknown request");
                }
            }
            if(reported.TryGetValue(leader,out var leaderStatus) && leaderStatus=="Team formed") {
                expired=true;
                messages.Add(string.Format(AOLauncher.Localization.Texts.Get("Startup.TeamReady"), preset.Name));
            }
            return messages;
        }
        static string MemberName(LauncherSettings settings, PresetCharacter member)
            => settings.Accounts.FirstOrDefault(a => a.Id == member.AccountId)?.Characters.FirstOrDefault(c => c.CharacterId == member.CharacterId)?.Name
                ?? member.CharacterId.ToString();
        void ReportHelperWait(PluginAutoInjector.Row row, List<string> messages, bool unsupported)
        {
            string state = unsupported ? "unsupported-helper" : "waiting-helper";
            if (reported.TryGetValue(row.CharacterId, out var previous) && previous == state) return;
            reported[row.CharacterId] = state;
            messages.Add(row.CharacterName + ": " + AOLauncher.Localization.Texts.Get(unsupported ? "Startup.HelperUnsupported" : "Startup.HelperPending"));
        }
        internal void Cancel(List<PluginAutoInjector.Row> rows)
        {
            lock (gate) CancelCore(rows);
        }
        void CancelCore(List<PluginAutoInjector.Row> rows)
        {
            if(expired)return;
            foreach(var row in rows.Where(r=>sent.ContainsKey(r.CharacterId) && starts.TryGetValue(r.CharacterId,out var start) && start==r.StartTicks && SameProcess(r)))
                SendPlan(row.Pid,"TEAMCANCEL "+session+" "+row.StartTicks);
            expired=true;
        }
    }
    internal sealed partial class MainForm
    {
        AutoTeamStartup autoTeamStartup;
        internal static void ApplyPresetLaunchOrder(LauncherSettings settings,LaunchRequest request,bool relog=false)
        {
            var preset=relog?settings.Presets.OrderByDescending(p=>p.Id==settings.SelectedPresetId)
                .FirstOrDefault(p=>request.Characters.Count==p.Characters.Count && p.Characters.All(m=>request.Characters.Any(c=>c.AccountId==m.AccountId && c.CharacterId==m.CharacterId)))
                :settings.Presets.FirstOrDefault(p=>p.Id==settings.SelectedPresetId);
            if(preset==null || !(preset.OrderedLogin || (settings.ExperimentalTeamAutomation && preset.AutoFormTeam)) || (!relog && !PresetService.Matches(settings,preset))
                || request.Characters.Count!=preset.Characters.Count
                || !preset.Characters.All(p=>request.Characters.Any(c=>c.AccountId==p.AccountId && c.CharacterId==p.CharacterId)))return;
            var ordered = Nexus.Core.LaunchPlan.InPresetOrder(request.Characters,
                preset.Characters.Select(p => p.AccountId + "/" + p.CharacterId), c => c.AccountId + "/" + c.CharacterId);
            if (ordered == null) return;
            request.Characters = ordered;
            request.OrderedStarts=true;
            request.KeepLeaderActive=request.Characters.Count>1;
        }
        void BeginAutoTeam(LaunchRequest request)
        {
            if (!settings.ExperimentalTeamAutomation) { autoTeamStartup?.Cancel(pluginInjector.Rows); return; }
            var preset=settings.Presets.FirstOrDefault(p=>p.Id==settings.SelectedPresetId && p.AutoFormTeam);
            if(preset==null || !PresetService.Matches(settings,preset)
                || request.Characters.Count!=preset.Characters.Count
                || !preset.Characters.All(p=>request.Characters.Any(c=>c.AccountId==p.AccountId && c.CharacterId==p.CharacterId)))return;
            string error=AutoTeamStartup.Validate(settings,preset.Characters,preset.LoadPlugins);
            if(error!=null) { AppendLog(error); return; }
            var json=new System.Web.Script.Serialization.JavaScriptSerializer();
            autoTeamStartup?.Cancel(pluginInjector.Rows);
            autoTeamStartup=new AutoTeamStartup(json.Deserialize<LaunchPreset>(json.Serialize(preset)));
            teamRecovery.BeginStartup(settings,preset,autoTeamStartup);
            AppendLog(preset.Name+": "+AOLauncher.Localization.Texts.Get("Text_first_character_leads_inviting_members_as_9DD92023EA15"));
        }
    }
}
