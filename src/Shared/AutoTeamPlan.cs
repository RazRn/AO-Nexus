using System;
using System.Collections.Generic;
using System.Linq;

namespace NexusTeams
{
    // Game-thread state only. No broadcasts, disbanding, leader transfers or arbitrary commands.
    internal interface ITeamGame
    {
        int Character { get; }
        bool Ready { get; }
        bool InTeam { get; }
        bool IsLeader { get; }
        int[] Members { get; }
        bool Visible(int character);
        void Invite(int character);
    }
    internal sealed class AutoTeamPlan
    {
        internal string Session, Status = "Waiting";
        internal int Self, Leader;
        internal int[] Members = new int[0], ReadyMembers = new int[0];
        internal DateTime Expires;
        internal bool Finished;
        readonly Dictionary<int,int> attempts = new Dictionary<int,int>();
        readonly Dictionary<int,DateTime> retryAfter = new Dictionary<int,DateTime>();
        DateTime nextInvite;
        int lastInvited;

        internal static AutoTeamPlan Parse(string request, long processStart, DateTime now)
        {
            var p = request.Split(' ');
            if (p.Length != 8 || p[0] != "TEAMPLAN" || !Guid.TryParseExact(p[1], "N", out _)
                || !long.TryParse(p[2], out var start) || start != processStart
                || !int.TryParse(p[3], out var self) || !int.TryParse(p[4], out var leader)
                || !long.TryParse(p[5], out var expires) || expires <= now.Ticks || expires > now.AddMinutes(6).Ticks) return null;
            int[] members, ready;
            try { members = p[6].Split(',').Select(int.Parse).ToArray(); ready = p[7].Split(',').Select(int.Parse).ToArray(); }
            catch { return null; }
            if (members.Length < 2 || members.Length > 6 || members.Any(i => i <= 0) || members.Distinct().Count() != members.Length
                || members[0] != leader || !members.Contains(self) || ready.Length > 6 || ready.Any(i => !members.Contains(i))) return null;
            return new AutoTeamPlan { Session=p[1], Self=self, Leader=leader, Members=members, ReadyMembers=ready,
                Expires=new DateTime(expires,DateTimeKind.Utc) };
        }
        internal bool Merge(AutoTeamPlan next)
        {
            if (next.Session != Session || next.Self != Self || next.Leader != Leader || next.Expires != Expires || !Members.SequenceEqual(next.Members)) return false;
            ReadyMembers = next.ReadyMembers;
            return true; // Repeated requests cannot reset completion, expiry or retry budgets.
        }
        internal bool Accepts(int inviter, ITeamGame game, DateTime now)
            => !Finished && now < Expires && game.Ready && game.Character == Self && Self != Leader && !game.InTeam && inviter == Leader;
        internal void Tick(ITeamGame game, DateTime now)
        {
            if (Finished) return;
            if (now >= Expires) { Finish("Expired: team did not finish forming"); return; }
            if (!game.Ready) { Status="Waiting for character"; return; }
            if (game.Character != Self) { Finish("Stopped: character changed"); return; }
            var current = game.InTeam ? game.Members.Concat(new[]{Self}).Distinct().ToArray() : new[]{Self};
            if (game.InTeam && (current.Any(i => !Members.Contains(i)) || (Self == Leader && !game.IsLeader))) {
                Finish("Skipped: already in a different team"); return;
            }
            if (Self != Leader) {
                if (game.InTeam) Finish(current.Contains(Leader) ? "Joined" : "Skipped: already in a different team");
                else Status="Waiting for leader invitation";
                return;
            }
            if (Members.All(current.Contains)) { Finish("Team formed"); return; }
            Status="Waiting for remaining members";
            if(lastInvited!=0 && current.Contains(lastInvited)) { nextInvite=now; lastInvited=0; }
            if (now < nextInvite) return;
            // First ready missing member in preset order. A slow login does not block ready clients.
            int target = Members.FirstOrDefault(id => id != Self && !current.Contains(id) && ReadyMembers.Contains(id)
                && game.Visible(id) && (!attempts.ContainsKey(id) || attempts[id] < 3)
                && (!retryAfter.ContainsKey(id) || now >= retryAfter[id]));
            if (target == 0) return;
            attempts[target] = attempts.ContainsKey(target) ? attempts[target]+1 : 1;
            // Space different invitations by at most one second. A joined member releases
            // the next invitation immediately; retries of the same person still wait 10s.
            nextInvite=now.AddSeconds(1);
            retryAfter[target]=now.AddSeconds(10);
            lastInvited=target;
            game.Invite(target);
            Status="Invited " + target;
        }
        void Finish(string status) { Finished=true; Status=status; }
        internal void Cancel() { Finish("Cancelled"); }
    }
}
