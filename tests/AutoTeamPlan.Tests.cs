using System;
using System.Collections.Generic;
using System.Linq;
using NexusTeams;
class AutoTeamPlanTests
{
    class Game : ITeamGame {
        public int Character {get;set;}=1;
        public bool Ready {get;set;}=true;
        public bool InTeam {get;set;}
        public bool IsLeader {get;set;}=true;
        public int[] Members {get;set;}=new int[0];
        internal List<int> Invites=new List<int>();
        internal int[] Nearby=new[]{2,3};
        public bool Visible(int id)=>Nearby.Contains(id);
        public void Invite(int id)=>Invites.Add(id);
    }
    static int checks;
    static void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;}
    static int Main() {
        var now=DateTime.UtcNow; var session=Guid.NewGuid().ToString("N");
        Func<int,string,string> command=(self,ready)=>"TEAMPLAN "+session+" 42 "+self+" 1 "+now.AddMinutes(5).Ticks+" 1,2,3 "+ready;
        var plan=AutoTeamPlan.Parse(command(1,"1,2"),42,now);
        Check(plan!=null,"Valid bounded plan");
        Check(AutoTeamPlan.Parse(command(1,"1,2"),43,now)==null,"PID reuse/start mismatch refused");
        Check(AutoTeamPlan.Parse(command(1,"1,9"),42,now)==null,"Ready list cannot add outsiders");
        Check(AutoTeamPlan.Parse(command(1,"1,2"),42,now.AddMinutes(6))==null,"Expired plan refused");
        var game=new Game {Ready=false}; plan.Tick(game,now);Check(game.Invites.Count==0,"No invitation during zoning/load");
        game.Ready=true; plan.Tick(game,now);Check(game.Invites.SequenceEqual(new[]{2}),"First ready member invited");
        plan.Tick(game,now.AddSeconds(1));Check(game.Invites.Count==1,"No repeated invitation every update");
        game.InTeam=true;game.Members=new[]{1,2};plan.Tick(game,now.AddSeconds(2));Check(game.Invites.Count==1,"Unarmed late member not invited");
        Check(plan.Merge(AutoTeamPlan.Parse(command(1,"1,2,3"),42,now)),"Same plan accepts newly ready clients");
        plan.Tick(game,now.AddSeconds(3));Check(game.Invites.SequenceEqual(new[]{2,3}),"Late member invited after helper ready without disband");
        game.Members=new[]{1,2,3};plan.Tick(game,now.AddSeconds(4));Check(plan.Finished && plan.Status=="Team formed","Completion needs observed membership");
        game.InTeam=false;plan.Merge(AutoTeamPlan.Parse(command(1,"1,2,3"),42,now));plan.Tick(game,now.AddSeconds(20));Check(game.Invites.Count==2,"Completed plan does not re-form a manually left team");
        plan=AutoTeamPlan.Parse(command(2,"2"),42,now);game=new Game {Character=2};
        Check(plan.Accepts(1,game,now),"Follower accepts selected leader");
        Check(!plan.Accepts(9,game,now),"Follower refuses unrelated inviter");
        Check(!plan.Accepts(1,game,now.AddMinutes(6)),"Invitation permission expires");
        game.InTeam=true;game.Members=new[]{2,9};plan.Tick(game,now);Check(plan.Finished && !plan.Accepts(1,game,now),"Existing other team preserved");
        plan=AutoTeamPlan.Parse(command(1,"1,2,3"),42,now);game=new Game {InTeam=true,IsLeader=false,Members=new[]{1,2}};plan.Tick(game,now);
        Check(plan.Finished && game.Invites.Count==0,"A nonleader cannot take over team");
        plan=AutoTeamPlan.Parse(command(1,"1,2,3"),42,now);game=new Game();
        plan.Tick(game,now);
        plan.Tick(game,now.AddMilliseconds(250));
        Check(game.Invites.SequenceEqual(new[]{2}),"Pending invitation is not spammed every tick");
        plan.Tick(game,now.AddSeconds(1));
        Check(game.Invites.SequenceEqual(new[]{2,3}),"Unanswered first invitation does not hold the next follower for ten seconds");
        plan.Tick(game,now.AddSeconds(2));
        Check(game.Invites.Count==2,"Each follower retains its own retry cooldown");
        plan.Tick(game,now.AddSeconds(10));
        Check(game.Invites.SequenceEqual(new[]{2,3,2}),"Retry returns to preset order after individual cooldown");
        plan=AutoTeamPlan.Parse(command(1,"1,2,3"),42,now);game=new Game();
        plan.Tick(game,now);game.InTeam=true;game.Members=new[]{1,2};
        plan.Tick(game,now.AddMilliseconds(250));
        Check(game.Invites.SequenceEqual(new[]{2,3}),"Confirmed join releases the next invitation on the next game tick");
        plan=AutoTeamPlan.Parse(command(1,"1,2,3"),42,now);game=new Game();
        for(int i=0;i<10;i++)plan.Tick(game,now.AddSeconds(i*10));
        Check(game.Invites.Count(i=>i==2)==3 && game.Invites.Count(i=>i==3)==3,"Invitation retries bounded per target");
        plan.Tick(game,now.AddMinutes(6));Check(plan.Finished,"Startup stops after timeout");
        plan=AutoTeamPlan.Parse(command(1,"1,2,3"),42,now);game=new Game {Character=4};plan.Tick(game,now);Check(plan.Finished && game.Invites.Count==0,"Character switch cancels stale plan");
        plan=AutoTeamPlan.Parse(command(1,"1,2,3"),42,now);game=new Game {Nearby=new int[0]};plan.Tick(game,now);Check(game.Invites.Count==0,"Out of range member waits");
        Console.WriteLine("PASS: "+checks+" automatic team state checks; no game interaction.");return 0;
    }
}
