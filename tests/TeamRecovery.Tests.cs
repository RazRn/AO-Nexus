using System;
using System.Collections.Generic;
using System.Linq;
using NexusTeams;
class TeamRecoveryTests
{
    class Game:IRecoveryGame {
        public int Character{get;set;}=2;
        public bool Ready{get;set;}=true;
        public bool InTeam{get;set;}=true;
        public bool IsLeader{get;set;}=true;
        public int CurrentLeader{get;set;}=2;
        public int[] Members{get;set;}=new[]{2,3};
        public bool Visible(int id)=>true;
        public List<int> Invites=new List<int>(),Transfers=new List<int>();
        public void Invite(int id)=>Invites.Add(id);
        public void TransferLeader(int id)=>Transfers.Add(id);
    }
    static int checks;
    static void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;}
    static List<TeamClientState> Team()=>Enumerable.Range(1,3).Select(id=>new TeamClientState {Scope="server",Id=id,Start=id,Alive=true,Ready=true,Leader=1,Members=new[]{1,2,3}}).ToList();
    static int Main(){
        var now=DateTime.UtcNow;var session=Guid.NewGuid().ToString("N");
        string handover="TEAMLEADER "+session+" 42 2 3 "+now.AddSeconds(10).Ticks+" 1,2,3";
        var change=TeamLeaderChange.Parse(handover,42,now);
        Check(change!=null && change.CanApply(new Game {Members=new[]{1,2,3}},now),"Explicit leadership transfer accepts matching live team");
        Check(TeamLeaderChange.Parse(handover,43,now)==null && TeamLeaderChange.Parse(handover,42,now.AddSeconds(11))==null,"Handover rejects stale process and expired command");
        Check(!change.CanApply(new Game {Members=new[]{1,2,3},IsLeader=false},now),"Only current leader can transfer");
        Check(!change.CanApply(new Game {Members=new[]{2,3}},now) && !change.CanApply(new Game {Members=new[]{1,2,3,4}},now),"Roster changes refuse transfer without inviting anyone");
        Check(!change.CanApply(new Game {Members=new[]{1,2,3},Ready=false},now),"Zoning refuses transfer");
        Check(TeamLeaderChange.Parse(handover.Replace("42 2 3","42 2 9"),42,now)==null,"Outsider cannot become leader");
        string command="TEAMRESTORE "+session+" 42 2 2 "+now.AddMinutes(5).Ticks+" 2,1,3 1,2,3 1";
        var p=TeamRecoveryPlan.Parse(command,42,now);var game=new Game();
        Check(p!=null && TeamRecoveryPlan.Parse(command,43,now)==null,"Recovery binds process start");
        Check(TeamRecoveryPlan.Parse(command.Substring(0,command.Length-1)+"9",42,now)==null,"Cannot transfer to outsider");
        p.Tick(game,now);Check(game.Invites.SequenceEqual(new[]{1}) && game.Transfers.Count==0,"Successor invites returning leader before transfer");
        game.Members=new[]{1,2,3};p.Tick(game,now.AddSeconds(1));Check(game.Transfers.SequenceEqual(new[]{1}) && !p.Finished,"Transfer waits for actual membership and acknowledgement");
        game.IsLeader=false;game.CurrentLeader=1;p.Tick(game,now.AddSeconds(2));Check(p.Finished && p.Status=="Team restored","Observed leader confirms recovery");
        p.Merge(TeamRecoveryPlan.Parse(command,42,now));p.Tick(game,now.AddSeconds(3));Check(game.Transfers.Count==1,"Retries do not rearm completed recovery");
        p=TeamRecoveryPlan.Parse(command,42,now);game=new Game {Members=new[]{2,9}};p.Tick(game,now);Check(p.Finished && game.Invites.Count==0,"No action in unrelated team");
        p=TeamRecoveryPlan.Parse(command,42,now);game=new Game {Ready=false};p.Tick(game,now);Check(game.Invites.Count==0,"No actions while zoning");
        p=TeamRecoveryPlan.Parse(command,42,now);game=new Game {Members=new[]{1,2,3}};
        for(int i=0;i<5;i++)p.Tick(game,now.AddSeconds(i*5));Check(game.Transfers.Count==3 && p.Finished,"Transfer retries bounded");
        p=TeamRecoveryPlan.Parse(command.Replace("42 2 2","42 1 2"),42,now);game=new Game {Character=1,InTeam=false,IsLeader=false,CurrentLeader=0,Members=new int[0]};
        Check(p.Accepts(2,game,now) && !p.Accepts(3,game,now),"Returner accepts only authorized successor");
        Check(!p.Accepts(2,game,now.AddMinutes(6)),"Recovery invitation expires");
        var tracker=new TeamRecoveryTracker();var states=Team();Check(tracker.Poll(states,now).Count==0,"Intact team merely remembered");
        states[0].Alive=false;states[0].Ready=false;foreach(var s in states.Skip(1)){s.Leader=2;s.Members=new[]{2,3};}
        var work=tracker.Poll(states,now.AddSeconds(1));Check(work.Count==1 && work[0].Leader==1 && work[0].Inviter==2,"Crash retains original leader and selects actual successor");
        string episode=work[0].Session;states[0].Alive=true;states[0].Ready=true;states[0].Start=100;states[0].Leader=0;states[0].Members=new int[0];
        work=tracker.Poll(states,now.AddSeconds(2));Check(work.Count==1 && work[0].Session==episode,"Returning client joins existing episode");
        foreach(var s in states){s.Leader=1;s.Members=new[]{1,2,3};}
        Check(tracker.Poll(states,now.AddSeconds(3)).Count==0,"Restored team ends recovery");
        states=Team();tracker=new TeamRecoveryTracker();tracker.Poll(states,now);foreach(var s in states){s.Leader=2;}
        Check(tracker.Poll(states,now.AddSeconds(1)).Count==0,"Deliberate leader transfer does not restore former leader");
        states[1].Alive=false;states[1].Ready=false;states[0].Leader=3;states[0].Members=new[]{1,3};states[2].Leader=3;states[2].Members=new[]{1,3};
        Check(tracker.Poll(states,now.AddSeconds(2)).Single().Leader==2,"Crash remembers latest deliberate leader");
        tracker=new TeamRecoveryTracker();states=Team();tracker.Poll(states,now);foreach(var s in states){s.Leader=0;s.Members=new int[0];}tracker.Poll(states,now.AddSeconds(1));states[0].Alive=false;
        Check(tracker.Poll(states,now.AddSeconds(2)).Count==0,"Manual dissolution is not resurrected after later crash");
        tracker=new TeamRecoveryTracker();states=Team();tracker.Remember("server",1,new[]{1,2,3},states,true,now);
        Check(tracker.Poll(states,now).Count==0,"Relog preparation waits for process replacement");
        foreach(var s in states){s.Start+=100;s.Leader=0;s.Members=new int[0];}
        work=tracker.Poll(states,now.AddSeconds(1));Check(work.Count==1 && work[0].Inviter==1,"Full relog reforms with previous leader");
        Check(tracker.Poll(states,now.AddMinutes(6)).Count==0,"Recovery timeout cannot retry forever");
        tracker=new TeamRecoveryTracker();states=Team();tracker.Poll(states,now);states[0].Start+=100;states[1].Members=new[]{2,3,9};
        Check(tracker.Poll(states,now.AddSeconds(1)).Count==0,"New outsider in remaining team cancels recovery");
        tracker=new TeamRecoveryTracker();states=Team();tracker.Poll(states,now);states[0].Alive=false;states[0].Ready=false;foreach(var s in states.Skip(1)){s.Scope="other";s.Leader=2;s.Members=new[]{2,3};}
        Check(tracker.Poll(states,now.AddSeconds(1)).Count==0,"Recovery never crosses servers");
        Console.WriteLine("PASS: "+checks+" recovery checks");return 0;
    }
}
