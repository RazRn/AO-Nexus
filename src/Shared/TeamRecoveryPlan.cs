using System;
using System.Linq;

namespace NexusTeams
{
    public sealed class TeamView
    {
        public int Character {get;set;}
        public long Start {get;set;}
        public long Utc {get;set;}
        public bool Ready {get;set;}
        public bool CanChooseLeader {get;set;}
        public int Leader {get;set;}
        public int[] Members {get;set;}=new int[0];
    }
    internal interface IRecoveryGame : ITeamGame
    {
        int CurrentLeader {get;}
        void TransferLeader(int character);
    }
    // One explicit handover, never an invitation or team reconstruction.
    internal sealed class TeamLeaderChange
    {
        internal string Session;
        internal int Self,Target;
        internal long Expires;
        internal int[] Members;
        internal static TeamLeaderChange Parse(string command,long start,DateTime now)
        {
            var p=command.Split(' ');
            if(p.Length!=7 || p[0]!="TEAMLEADER" || !Guid.TryParseExact(p[1],"N",out _)
                || !long.TryParse(p[2],out var expected) || expected!=start
                || !int.TryParse(p[3],out var self) || !int.TryParse(p[4],out var target)
                || !long.TryParse(p[5],out var expires) || expires<=now.Ticks || expires>now.AddSeconds(15).Ticks)return null;
            int[] members;try{members=p[6].Split(',').Select(int.Parse).ToArray();}catch{return null;}
            if(members.Length<2 || members.Length>6 || members.Any(id=>id<=0) || members.Distinct().Count()!=members.Length
                || self==target || !members.Contains(self) || !members.Contains(target))return null;
            return new TeamLeaderChange {Session=p[1],Self=self,Target=target,Expires=expires,Members=members};
        }
        internal bool CanApply(IRecoveryGame game,DateTime now)=>now.Ticks<Expires && game.Ready && game.InTeam
            && game.IsLeader && game.Character==Self && game.CurrentLeader==Self
            && game.Members.Concat(new[]{Self}).Distinct().OrderBy(x=>x).SequenceEqual(Members.OrderBy(x=>x));
    }
    // A bounded recovery episode, separate from the one-shot startup plan.
    internal sealed class TeamRecoveryPlan
    {
        internal AutoTeamPlan Formation;
        internal int RestoreLeader;
        internal string Status="Waiting";
        internal bool Finished;
        int transfers;
        DateTime nextTransfer;
        internal static TeamRecoveryPlan Parse(string request,long start,DateTime now)
        {
            var parts=request.Split(' ');
            if(parts.Length!=9 || parts[0]!="TEAMRESTORE" || !int.TryParse(parts[8],out var leader))return null;
            parts[0]="TEAMPLAN";
            var formation=AutoTeamPlan.Parse(string.Join(" ",parts.Take(8)),start,now);
            if(formation==null || !formation.Members.Contains(leader))return null;
            return new TeamRecoveryPlan {Formation=formation,RestoreLeader=leader};
        }
        internal bool Merge(TeamRecoveryPlan next)=>next.RestoreLeader==RestoreLeader && Formation.Merge(next.Formation);
        internal bool Accepts(int inviter,IRecoveryGame game,DateTime now)=>!Finished && Formation.Accepts(inviter,game,now);
        internal void Tick(IRecoveryGame game,DateTime now)
        {
            if(Finished)return;
            if(now>=Formation.Expires){Finish("Expired: team recovery timed out");return;}
            if(!game.Ready)return;
            if(game.Character!=Formation.Self){Finish("Stopped: character changed");return;}
            var members=game.InTeam?game.Members.Concat(new[]{game.Character}).Distinct().ToArray():new[]{game.Character};
            if(members.Any(id=>!Formation.Members.Contains(id))){Finish("Stopped: team changed");return;}
            if(Formation.Members.All(members.Contains) && game.CurrentLeader==RestoreLeader){Finish("Team restored");return;}
            Formation.Tick(game,now);Status=Formation.Status;
            if(!Formation.Finished)return;
            if(Formation.Status!="Team formed" && Formation.Status!="Joined"){Finish(Formation.Status);return;}
            if(game.Character!=Formation.Leader){Status="Waiting for leadership restoration";return;}
            if(!game.IsLeader || !members.Contains(RestoreLeader)){Finish("Stopped: leader changed");return;}
            Status="Waiting for leadership restoration";
            if(now<nextTransfer)return;
            if(transfers>=3){Finish("Stopped: leadership transfer not confirmed");return;}
            transfers++;nextTransfer=now.AddSeconds(5);
            game.TransferLeader(RestoreLeader);
        }
        internal void Cancel(){Formation.Cancel();Finish("Cancelled");}
        void Finish(string text){Status=text;Finished=true;}
    }
}
