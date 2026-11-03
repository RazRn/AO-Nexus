using System;
using System.Linq;
using System.Reflection;
using AOSharp.Core;
using AOSharp.Core.UI;
using AOSharp.Core.Movement;
using AOSharp.Common.GameData;

namespace NexusTelemetry
{
    internal static class SyncMovementProbe
    {
        internal static MovementSnapshot Read(SimpleChar player,long now)
        {
            var sample=new MovementSnapshot {Qpc=now,CharacterId=player.Identity.Instance,
                Playfield=Playfield.Identity.Instance,X=player.Position.X,Y=player.Position.Y,Z=player.Position.Z,
                RunSpeed=player.GetStat(Stat.RunSpeed),
                VisiblePlayers=DynelManager.Players.Select(p=>p.Identity.Instance).Take(512).ToArray()};
            try { sample.ZoneName=Playfield.Name; } catch { sample.ZoneName=null; }
            try {
                // Read the loaded AOSP instance; never replace IPC callbacks or
                // mutate its settings. Unsupported versions are explicit.
                var instance=Game.OnUpdate?.GetInvocationList().Select(d=>d.Target)
                    .FirstOrDefault(o=>o?.GetType().FullName=="ManagerSync.ManagerSync");
                if(instance==null){sample.Adapter="Unavailable";return sample;}
                var type=instance.GetType();
                var settings=(Settings)type.GetField("_settings",BindingFlags.Public|BindingFlags.Static).GetValue(null);
                sample.Adapter="AOSP";
                sample.Enabled=settings["Enable"].AsBool();
                sample.SendMovement=settings["SyncMove"].AsBool();
                sample.Channel=settings["IPCChannel"].AsInt32();
                sample.LastAcceptedSender=((Identity)type.GetField("MoveSenderID",BindingFlags.Public|BindingFlags.Static).GetValue(null)).Instance;
            } catch(Exception ex){sample.Adapter="Unsupported";sample.Error=ex.GetType().Name;}
            return sample;
        }

        internal static string Regroup(int leaderId,int channel,int[] verifiedTeamMembers)
        {
            if(Game.IsZoning || DynelManager.LocalPlayer==null)return "Wait until zoning finishes.";
            var player=DynelManager.LocalPlayer;
            if(!player.IsAlive || player.IsAttacking || Spell.HasPendingCast)return "Finish combat or casting before regrouping.";
            // The caller captures a fresh GUI roster on this game update. The
            // engine's Team.Members can be empty on Rebirth after zoning.
            if(leaderId==player.Identity.Instance || verifiedTeamMembers==null
                || !verifiedTeamMembers.Contains(player.Identity.Instance) || !verifiedTeamMembers.Contains(leaderId))
                return "The selected character must be another member of your team.";
            var state=Read(player,System.Diagnostics.Stopwatch.GetTimestamp());
            if(state.Adapter!="AOSP" || !state.Enabled || state.Channel!=channel)return "SyncManager is unavailable, disabled or on another channel.";
            if(!DynelManager.Find(new Identity(IdentityType.SimpleChar,leaderId),out SimpleChar leader) || !leader.IsPlayer)
                return "Leader is not visible. Bring the characters closer in the same area.";
            if(player.Position.Distance2DFrom(leader.Position)>60 || Math.Abs(player.Position.Y-leader.Position.Y)>5
                || !Playfield.LineOfSight(player.Position+new Vector3(0,1,0),leader.Position+new Vector3(0,1,0)))
                return "No clear nearby route. Reposition the characters before regrouping.";
            if(MovementController.Instance.IsNavigating)return "Another movement controller is active. Stop it before regrouping.";
            // Ordinary game follow, never a coordinate write or a larger sync limit.
            MovementController.Instance.Follow(leader.Identity);
            return "Follow requested. Resume movement from the selected leader when the group is together.";
        }
    }
}
