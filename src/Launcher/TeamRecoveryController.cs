using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Web.Script.Serialization;
using NexusTeams;

namespace AOLauncher
{
    internal sealed class TeamRecoveryController
    {
        readonly TeamRecoveryTracker tracker=new TeamRecoveryTracker();
        readonly Dictionary<string,string> reports=new Dictionary<string,string>();
        readonly Dictionary<string,PluginAutoInjector.Row> known=new Dictionary<string,PluginAutoInjector.Row>();
        List<TeamClientState> last=new List<TeamClientState>();
        DateTime nextPoll;
        readonly object gate=new object();
        AutoTeamStartup startup;
        string startupScope;
        HashSet<int> startupMembers=new HashSet<int>();
        readonly List<TeamRecoveryEpisode> cancelled=new List<TeamRecoveryEpisode>();
        readonly Dictionary<string,TeamRecoveryEpisode> issued=new Dictionary<string,TeamRecoveryEpisode>();
        string leadershipScope;
        HashSet<int> leadershipMembers=new HashSet<int>();
        DateTime leadershipUntil;
        internal void BeforeLeaderChange(LauncherSettings settings,PresetCamping.Client client,int[] members)
        {
            lock(gate) {
                var row=known.Values.FirstOrDefault(r=>r.Pid==client.ProcessId && r.StartTicks==client.StartTimeUtcTicks);
                var account=row==null?null:settings.Accounts.FirstOrDefault(a=>a.Id==row.AccountId);
                if(account==null)throw new InvalidOperationException(AOLauncher.Localization.Texts.Get("Formation.Unavailable"));
                string scope=AccountTargets.Endpoint(account);var ids=new HashSet<int>(members);
                var removed=tracker.Forget(scope,ids).Concat(issued.Values.Where(p=>p.Scope==scope && p.Members.Any(ids.Contains)))
                    .Concat(cancelled.Where(p=>p.Scope==scope && p.Members.Any(ids.Contains))).GroupBy(p=>p.Session).Select(g=>g.First()).ToList();
                foreach(var episode in removed) {
                    issued.Remove(episode.Session??"");cancelled.RemoveAll(p=>p.Session==episode.Session);
                    if(episode.Session==null)continue;
                    // Keep failed cancellations queued for the normal retry loop.
                    cancelled.Add(episode);
                    foreach(var peer in known.Values.Where(r=>ids.Contains(r.CharacterId) && IsAlive(r))) {
                        var a=settings.Accounts.FirstOrDefault(x=>x.Id==peer.AccountId);
                        if(a!=null && AccountTargets.Endpoint(a)==scope && SendPlan(peer.Pid,"TEAMCANCEL "+episode.Session+" "+peer.StartTicks)!="OK queued")
                            throw new InvalidOperationException(AOLauncher.Localization.Texts.Get("Formation.Unavailable"));
                    }
                    cancelled.RemoveAll(p=>p.Session==episode.Session);
                }
                if(startupScope==scope && startupMembers.Any(ids.Contains))startup?.Cancel(known.Values.ToList());
                leadershipScope=scope;leadershipMembers=ids;leadershipUntil=Now().AddSeconds(12);
            }
        }
        internal void BeginStartup(LauncherSettings settings,LaunchPreset preset,AutoTeamStartup formation)
        {
            lock(gate) {
                startup=formation;
                startupScope=AccountTargets.Endpoint(settings.Accounts.First(a=>a.Id==preset.Characters[0].AccountId));
                startupMembers=new HashSet<int>(preset.Characters.Select(c=>c.CharacterId));
                cancelled.AddRange(tracker.Forget(startupScope,startupMembers));
                foreach(var plan in issued.Values.Where(p=>p.Scope==startupScope && p.Members.Any(startupMembers.Contains)).ToList()) {
                    cancelled.Add(plan);issued.Remove(plan.Session);
                }
                // Do not let relog preparation reuse a partial or older roster.
                last.RemoveAll(s=>Reserved(s));
            }
        }
        bool Reserved(TeamClientState state)=>(startup?.Active==true && state.Scope==startupScope && startupMembers.Contains(state.Id))
            || (Now()<leadershipUntil && state.Scope==leadershipScope && leadershipMembers.Contains(state.Id));
        internal Func<PluginAutoInjector.Row,bool> IsAlive=Alive;
        internal Func<int,string> ReadView=ShortcutClient.TeamView, ReadRecoveryStatus=ShortcutClient.TeamRecoveryStatus;
        internal Func<int,string,string> SendPlan=ShortcutClient.TeamPlan;
        internal Func<DateTime> Now=()=>DateTime.UtcNow;
        internal static bool Alive(PluginAutoInjector.Row row) {
            try {using(var p=Process.GetProcessById(row.Pid))return !p.HasExited && p.StartTime.ToUniversalTime().Ticks==row.StartTicks;}
            catch{return false;}
        }
        internal List<string> Poll(LauncherSettings settings,List<PluginAutoInjector.Row> rows)
        {
            lock(gate) {
                var messages=new List<string>();var now=Now();
                if (!settings.ExperimentalTeamAutomation) {
                    foreach (var row in rows) known[row.Pid+":"+row.StartTicks]=row;
                    startup?.Cancel(rows);
                    foreach (var episode in cancelled.Concat(issued.Values).GroupBy(p=>p.Session).Select(g=>g.First()))
                        foreach (var row in known.Values.Where(r=>episode.Members.Contains(r.CharacterId) && IsAlive(r))) {
                            var account=settings.Accounts.FirstOrDefault(a=>a.Id==row.AccountId);
                            if(account!=null && AccountTargets.Endpoint(account)==episode.Scope)
                                SendPlan(row.Pid,"TEAMCANCEL "+episode.Session+" "+row.StartTicks);
                        }
                    tracker.Clear(); issued.Clear(); cancelled.Clear(); known.Clear(); last.Clear(); reports.Clear();
                    startup=null; startupMembers.Clear(); nextPoll=DateTime.MinValue;
                    return messages;
                }
                if(now<nextPoll)return messages;nextPoll=now.AddSeconds(2);
                if(settings.AOSharpDisabled || !settings.IncludeShortcuts || !settings.AutoInjectPlugins){tracker.Clear();return messages;}
                var available=new Dictionary<TeamClientState,PluginAutoInjector.Row>();
                foreach(var row in rows)known[row.Pid+":"+row.StartTicks]=row;
                // Cancel old game-thread plans before issuing any new recovery work.
                foreach(var episode in cancelled.GroupBy(p=>p.Session).Select(g=>g.First())) {
                    if(episode.Session==null)continue;
                    foreach(var row in known.Values.Where(r=>episode.Members.Contains(r.CharacterId) && IsAlive(r))) {
                        var account=settings.Accounts.FirstOrDefault(a=>a.Id==row.AccountId);
                        if(account!=null && AccountTargets.Endpoint(account)==episode.Scope)
                            SendPlan(row.Pid,"TEAMCANCEL "+episode.Session+" "+row.StartTicks);
                    }
                }
                cancelled.Clear();
                foreach(var session in issued.Where(p=>p.Value.PlanExpires<=now).Select(p=>p.Key).ToList())issued.Remove(session);
                foreach(var row in known.Values.ToArray()) {
                    var account=settings.Accounts.FirstOrDefault(a=>a.Id==row.AccountId);if(account==null)continue;
                    var state=new TeamClientState {Scope=AccountTargets.Endpoint(account),Id=row.CharacterId,Name=row.CharacterName,Start=row.StartTicks,Alive=IsAlive(row)};
                    if(state.Alive && !Reserved(state) && PluginAutoInjector.CanUseShortcuts(row))try {
                        var json=ReadView(row.Pid);
                        if(json!=null && json.StartsWith("{",StringComparison.Ordinal)) {
                            var v=new JavaScriptSerializer().Deserialize<TeamView>(json);
                            if(v.Start==row.StartTicks && v.Character==row.CharacterId && v.Ready && v.Utc<=now.AddSeconds(2).Ticks && v.Utc>=now.AddSeconds(-5).Ticks
                                && v.Members!=null && v.Members.Length<=6 && v.Members.All(id=>id>0) && v.Members.Distinct().Count()==v.Members.Length
                                && (v.Members.Length==0?v.Leader==0:v.Members.Contains(v.Character) && v.Members.Contains(v.Leader))) {
                                state.Ready=true;state.Leader=v.Leader;state.Members=v.Members;
                            }
                        }
                    }catch { }
                    available[state]=row;
                }
                // Injector may briefly retain both the exited and replacement processes.
                last=available.Keys.GroupBy(s=>s.Scope+"/"+s.Id).Select(g=>g.OrderByDescending(s=>s.Alive).ThenByDescending(s=>s.Start).First()).ToList();
                foreach(var episode in tracker.Poll(last.Where(s=>!Reserved(s)).ToList(),now)) {
                    // Keep cancellation information even if a later conflicting roster
                    // makes the tracker discard the episode before its client plans expire.
                    issued[episode.Session]=new TeamRecoveryEpisode {Session=episode.Session,Scope=episode.Scope,Members=episode.Members.ToArray(),PlanExpires=episode.PlanExpires};
                    var ready=last.Where(s=>s.Scope==episode.Scope && episode.Members.Contains(s.Id) && s.Alive && s.Ready).ToList();
                    var armed=new List<int>();
                    foreach(var state in ready.OrderBy(s=>s.Id==episode.Inviter?1:0)) {
                        var row=available[state];
                        string status=ReadRecoveryStatus(row.Pid);
                        if(status?.StartsWith(episode.Session+" ",StringComparison.Ordinal)==true) {
                            var detail=status.Substring(episode.Session.Length+1);
                            if(detail=="Waiting for leader invitation" || detail=="Waiting for leadership restoration" || detail=="Joined" || detail=="Team restored")armed.Add(state.Id);
                            string key=episode.Session+"/"+state.Id;
                            if(!reports.TryGetValue(key,out var prior) || prior!=detail){reports[key]=detail;messages.Add(state.Name+": team recovery · "+detail);}
                        }
                        var members=new[]{episode.Inviter}.Concat(episode.Members.Where(id=>id!=episode.Inviter));
                        var active=state.Id==episode.Inviter?armed.Concat(new[]{state.Id}):new[]{state.Id};
                        SendPlan(row.Pid,"TEAMRESTORE "+episode.Session+" "+state.Start+" "+state.Id+" "+episode.Inviter+" "+episode.PlanExpires.Ticks+" "+string.Join(",",members)+" "+string.Join(",",active)+" "+episode.Leader);
                    }
                }
                foreach(var stale in known.Where(pair=>!IsAlive(pair.Value)).Select(pair=>pair.Key).ToArray())known.Remove(stale);
                if(reports.Count>256)reports.Clear();
                return messages;
            }
        }
        internal void PrepareRelog(LauncherSettings settings,List<PresetCamping.Client> clients)
        {
            if (!settings.ExperimentalTeamAutomation) return;
            lock(gate) {
                var now=Now();
                // Capture actual teams before the first /quit. Crash recovery uses the same memory.
                foreach(var leader in last.Where(s=>!Reserved(s) && s.Ready && s.Leader==s.Id && s.Members.Length>=2).ToList())
                    if(clients.Any(c=>c.CharacterId==leader.Id || leader.Members.Contains(c.CharacterId))
                        && leader.Members.All(id=>last.Any(s=>s.Scope==leader.Scope && s.Id==id && s.Alive)))
                        tracker.Remember(leader.Scope,leader.Id,leader.Members,last,true,now);
                // A full relog of an automatic saved team also forms it when it was not teamed yet.
                var preset=settings.Presets.FirstOrDefault(p=>p.AutoFormTeam && p.Characters.Count==clients.Count && p.Characters.All(m=>clients.Any(c=>c.AccountIdentity=="id:"+m.AccountId && c.CharacterId==m.CharacterId)));
                if(preset==null || AutoTeamStartup.Validate(settings,preset.Characters,preset.LoadPlugins)!=null)return;
                var account=settings.Accounts.First(a=>a.Id==preset.Characters[0].AccountId);
                var scope=AccountTargets.Endpoint(account);
                if(last.Any(s=>s.Scope==scope && s.Ready && s.Members.Length>0 && preset.Characters.Any(m=>m.CharacterId==s.Id)))return;
                tracker.Remember(scope,preset.Characters[0].CharacterId,preset.Characters.Select(m=>m.CharacterId).ToArray(),last,true,now);
            }
        }
    }
}
