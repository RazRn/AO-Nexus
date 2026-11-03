using System;
using System.Collections.Generic;
using System.Linq;

namespace NexusTeams
{
    internal sealed class TeamClientState
    {
        internal string Scope,Name;
        internal int Id;
        internal long Start;
        internal bool Alive,Ready;
        internal int Leader;
        internal int[] Members=new int[0];
    }
    internal sealed class TeamRecoveryEpisode
    {
        internal string Scope,Session;
        internal int Leader,Inviter;
        internal int[] Members;
        internal Dictionary<int,long> Starts=new Dictionary<int,long>();
        internal bool Pending;
        internal DateTime Expires,PlanExpires;
    }
    // Records actual teams. Only a process exit/replacement or explicit relog arms recovery.
    internal sealed class TeamRecoveryTracker
    {
        readonly List<TeamRecoveryEpisode> groups=new List<TeamRecoveryEpisode>();
        internal List<TeamRecoveryEpisode> Forget(string scope,IEnumerable<int> members)
        {
            var ids=new HashSet<int>(members);
            var removed=groups.Where(g=>g.Scope==scope && g.Members.Any(ids.Contains)).ToList();
            foreach(var group in removed)groups.Remove(group);
            return removed;
        }
        internal void Remember(string scope,int leader,int[] members,IEnumerable<TeamClientState> states,bool relog,DateTime now)
        {
            if(members==null || members.Length<2 || members.Length>6 || members.Any(i=>i<=0) || members.Distinct().Count()!=members.Length || !members.Contains(leader))return;
            groups.RemoveAll(g=>g.Scope==scope && g.Members.Intersect(members).Any());
            var group=new TeamRecoveryEpisode {Scope=scope,Leader=leader,Members=members,Pending=relog,Expires=now.AddMinutes(10)};
            foreach(var state in states.Where(s=>s.Scope==scope && members.Contains(s.Id)))group.Starts[state.Id]=state.Start;
            groups.Add(group);
        }
        internal List<TeamRecoveryEpisode> Poll(List<TeamClientState> states,DateTime now)
        {
            var work=new List<TeamRecoveryEpisode>();
            foreach(var g in groups.ToArray()) {
                var current=states.Where(s=>s.Scope==g.Scope && g.Members.Contains(s.Id)).ToList();
                if(current.GroupBy(s=>s.Id).Any(x=>x.Count()>1)){groups.Remove(g);continue;}
                bool restarted=g.Starts.Any(old=>!current.Any(c=>c.Id==old.Key && c.Start==old.Value && c.Alive));
                if(restarted && !g.Pending){g.Pending=true;g.Expires=now.AddMinutes(10);}
                if(!g.Pending) {
                    if(current.Any(s=>s.Id==g.Leader && s.Ready && s.Alive && (s.Leader!=g.Leader || !g.Members.All(s.Members.Contains))))groups.Remove(g);
                    continue;
                }
                if(now>=g.Expires){groups.Remove(g);continue;}
                if(g.Session==null && !restarted)continue; // Explicit relog is armed before /quit.
                var ready=current.Where(s=>s.Alive && s.Ready).ToList();
                if(ready.Any(s=>s.Members.Any(id=>!g.Members.Contains(id)))){groups.Remove(g);continue;}
                var leaders=ready.Where(s=>s.Leader==s.Id && s.Members.Length>0).ToList();
                if(leaders.Count>1){groups.Remove(g);continue;}
                if(leaders.Count==1 && leaders[0].Id==g.Leader && g.Members.All(leaders[0].Members.Contains)) {
                    g.Pending=false;g.Session=null;g.Starts=current.ToDictionary(s=>s.Id,s=>s.Start);continue;
                }
                int inviter=leaders.Count==1?leaders[0].Id:ready.Any(s=>s.Id==g.Leader && s.Members.Length==0)?g.Leader:0;
                if(inviter==0 || ready.Count<2)continue;
                if(g.Session==null || g.Inviter!=inviter) {
                    g.Session=Guid.NewGuid().ToString("N");g.Inviter=inviter;g.PlanExpires=now.AddMinutes(5);
                }
                // No endless retries after a declined invitation or failed transfer.
                if(now>=g.PlanExpires){groups.Remove(g);continue;}
                work.Add(g);
            }
            foreach(var leader in states.Where(s=>s.Alive && s.Ready && s.Leader==s.Id && s.Members.Length>=2)) {
                var roster=leader.Members.Distinct().ToArray();
                if(groups.Any(g=>g.Pending && g.Scope==leader.Scope && g.Members.Intersect(roster).Any()))continue;
                if(!roster.All(id=>states.Count(s=>s.Scope==leader.Scope && s.Id==id && s.Alive)==1))continue;
                // Refresh deliberate leadership / roster changes while every process is still alive.
                Remember(leader.Scope,leader.Id,roster,states,false,now);
            }
            return work;
        }
        internal void Clear()=>groups.Clear();
    }
}
