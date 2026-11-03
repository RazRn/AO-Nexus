using System;
using System.Diagnostics;
using System.Linq;
using AOSharp.Core;
using AOSharp.Common.GameData;
using NexusTeams;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using SmokeLounge.AOtomation.Messaging.GameData;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;

namespace NexusShortcuts
{
    internal sealed class NexusAutoTeam : IRecoveryGame, IDisposable
    {
        readonly long start = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks;
        AutoTeamPlan plan;
        AutoTeamPlan pending;
        string cancel;
        readonly object gate = new object();
        volatile string status="WAIT";
        DateTime nextUpdate;
        ClientTeamState team = new ClientTeamState();
        TeamRecoveryPlan recovery, pendingRecovery;
        TeamLeaderChange pendingLeader;
        readonly HashSet<string> leaderChanges=new HashSet<string>();
        internal string QueueLeader(string request)
        {
            var next=TeamLeaderChange.Parse(request,start,DateTime.UtcNow);
            if(next==null)return "ERROR invalid leader change";
            lock(gate) {
                if(leaderChanges.Contains(next.Session))return "OK queued";
                if(pendingLeader!=null || leaderChanges.Count>=128)return "BUSY";
                leaderChanges.Add(next.Session);pendingLeader=next;
            }
            return "OK queued";
        }
        readonly HashSet<string> recoverySessions=new HashSet<string>();
        volatile string view="WAIT", recoveryStatus="WAIT";
        internal string View=>view;
        internal string RecoveryStatus=>recoveryStatus;
        internal string QueueRecovery(string request)
        {
            var next=TeamRecoveryPlan.Parse(request,start,DateTime.UtcNow);
            if(next==null)return "ERROR invalid recovery plan";
            lock(gate)pendingRecovery=next;
            return "OK queued";
        }
        internal NexusAutoTeam() { Team.TeamRequest += OnRequest; }
        internal string Status => status;
        internal string Queue(string request)
        {
            var next=AutoTeamPlan.Parse(request,start,DateTime.UtcNow);
            if(next==null) return "ERROR invalid team plan";
            lock(gate) pending=next;
            return "OK queued";
        }
        internal string Cancel(string request)
        {
            var parts=request.Split(' ');
            if(parts.Length!=3 || !Guid.TryParseExact(parts[1],"N",out _) || !long.TryParse(parts[2],out var expected) || expected!=start)return "ERROR invalid cancellation";
            lock(gate)cancel=parts[1];return "OK queued";
        }
        internal void Update()
        {
            AutoTeamPlan next;TeamRecoveryPlan nextRecovery;TeamLeaderChange nextLeader;string cancelled;
            lock(gate) { next=pending; pending=null;nextRecovery=pendingRecovery;pendingRecovery=null;cancelled=cancel;cancel=null;nextLeader=pendingLeader;pendingLeader=null; }
            if (nextLeader != null || DateTime.UtcNow >= nextUpdate) team = ClientTeamRoster.Capture();
            if(nextLeader!=null)try {
                if(nextLeader.CanApply(this,DateTime.UtcNow)) {
                    plan?.Cancel();recovery?.Cancel();next=null;nextRecovery=null;
                    TransferLeader(nextLeader.Target);
                } else Trace.TraceWarning("Team leader change rejected: stale team or leadership.");
            }catch(Exception ex){Trace.TraceWarning("Team leader change failed: {0}",ex.Message);}
            if(nextRecovery!=null) {
                if(recovery?.Formation.Session==nextRecovery.Formation.Session)recovery.Merge(nextRecovery);
                else if(!recoverySessions.Contains(nextRecovery.Formation.Session) && recoverySessions.Count<128) {
                    recovery?.Cancel();plan?.Cancel();recovery=nextRecovery;recoverySessions.Add(nextRecovery.Formation.Session);
                }
            }
            if(next!=null) {
                if(plan==null) plan=next;
                else if(plan.Session==next.Session) plan.Merge(next);
                // One startup session per process: a launcher retry cannot re-form a team
                // the user subsequently left, or re-arm a completed/expired plan.
            }
            if(plan?.Session==cancelled && plan!=null)plan.Cancel();
            if(recovery?.Formation.Session==cancelled && recovery!=null)recovery.Cancel();
            var now=DateTime.UtcNow;
            if(now<nextUpdate)return;
            nextUpdate=now.AddMilliseconds(250);
            try {
                bool ready=Ready && !Team.IsRaid;
                view=new JavaScriptSerializer().Serialize(new TeamView {Character=Character,Start=start,Utc=now.Ticks,Ready=ready,CanChooseLeader=true,
                    Leader=ready?CurrentLeader:0,Members=ready && InTeam?Members.Concat(new[]{Character}).Distinct().ToArray():new int[0]});
                if(recovery!=null) {recovery.Tick(this,now);recoveryStatus=recovery.Formation.Session+" "+recovery.Status;}
                if(plan!=null && (recovery==null || recovery.Finished)) {plan.Tick(this,now);status=plan.Session+" "+plan.Status;}
            }
            catch (Exception ex) { view="WAIT"; Trace.TraceWarning("Team automation update failed: {0}", ex.Message); }
        }
        void OnRequest(object sender,TeamRequestEventArgs e)
        {
            try {
                team = ClientTeamRoster.Capture();
                if(!e.Responded && (recovery?.Accepts(e.Requester.Instance,this,DateTime.UtcNow)==true || plan?.Accepts(e.Requester.Instance,this,DateTime.UtcNow)==true)) e.Accept();
            }
            catch (Exception ex) { Trace.TraceWarning("Team invitation handling failed: {0}", ex.Message); }
        }
        public int Character => Game.ClientInst;
        public bool Ready => team.Ready;
        public bool InTeam => team.InTeam;
        public bool IsLeader => team.IsLeader;
        public int CurrentLeader => team.Leader;
        public void TransferLeader(int id)=>Network.Send(new CharacterActionMessage {Action=CharacterActionType.TransferLeader,Target=new Identity(IdentityType.SimpleChar,id)});
        public int[] Members => team.Members;
        public bool Visible(int id) => DynelManager.Characters.Any(c=>c.Identity.Instance==id && c.Identity.Type==IdentityType.SimpleChar);
        public void Invite(int id) => Team.Invite(new Identity(IdentityType.SimpleChar,id));
        public void Dispose() { Team.TeamRequest -= OnRequest; lock(gate)pending=null; plan=null; }
    }
}
