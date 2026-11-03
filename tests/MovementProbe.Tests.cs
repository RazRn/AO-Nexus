using System;
using AOSharp.Core;
using AOSharp.Core.UI;
using AOSharp.Core.Movement;
using AOSharp.Common.GameData;
using NexusTelemetry;

// A stand-in for the supported plugin's public settings; no game is launched.
namespace ManagerSync
{
    public sealed class ManagerSync
    {
        public static Settings _settings=new Settings();
        public static Identity MoveSenderID;
        public void Update(object sender,float delta) { }
    }
}

static class MovementProbeTests
{
    static int checks;
    static int[] verifiedMembers;
    static void Check(bool ok,string reason) { if(!ok)throw new Exception(reason);checks++; }
    static void Refuses(Action change,Action restore,string expected)
    {
        int before=MovementController.Instance.FollowCalls;
        change();
        string result=SyncMovementProbe.Regroup(20,4,verifiedMembers);
        restore();
        Check(result.Contains(expected) && MovementController.Instance.FollowCalls==before,expected);
    }
    static int Main()
    {
        try {
            var player=DynelManager.LocalPlayer;
            var leader=new SimpleChar {Instance=20,Position=new Vector3(3,0,0)};
            DynelManager.RegroupLeader=leader;DynelManager.Players=new[]{leader};
            Team.IsInTeam=true;Team.Members=new[]{player,leader};
            var readyMembers=new[]{player.Identity.Instance,leader.Identity.Instance};
            verifiedMembers=readyMembers;
            var settings=ManagerSync.ManagerSync._settings;
            settings.Values["Enable"]=new Setting {Value=1};
            settings.Values["SyncMove"]=new Setting {Value=1};
            settings.Values["IPCChannel"]=new Setting {Value=4};
            var manager=new ManagerSync.ManagerSync();
            Game.OnUpdate+=manager.Update;
            var beforeHandlers=Game.OnUpdate.GetInvocationList().Length;
            var sample=SyncMovementProbe.Read(player,123);
            Check(sample.Adapter=="AOSP" && sample.Enabled && sample.Channel==4 && sample.VisiblePlayers[0]==20,"Read public settings and visible players");
            Check(Game.OnUpdate.GetInvocationList().Length==beforeHandlers,"Probe leaves callbacks unchanged");
            Refuses(()=>Game.IsZoning=true,()=>Game.IsZoning=false,"zoning");
            Refuses(()=>player.IsAlive=false,()=>player.IsAlive=true,"combat");
            Refuses(()=>player.IsAttacking=true,()=>player.IsAttacking=false,"combat");
            Refuses(()=>Spell.HasPendingCast=true,()=>Spell.HasPendingCast=false,"casting");
            Refuses(()=>verifiedMembers=null,()=>verifiedMembers=readyMembers,"team");
            Refuses(()=>verifiedMembers=new[]{player.Identity.Instance},()=>verifiedMembers=readyMembers,"team");
            Refuses(()=>verifiedMembers=new[]{leader.Identity.Instance},()=>verifiedMembers=readyMembers,"team");
            Refuses(()=>player.Instance=20,()=>player.Instance=readyMembers[0],"team");
            Refuses(()=>settings.Values["IPCChannel"].Value=5,()=>settings.Values["IPCChannel"].Value=4,"channel");
            Refuses(()=>settings.Values["Enable"].Value=0,()=>settings.Values["Enable"].Value=1,"disabled");
            Refuses(()=>leader.Player=false,()=>leader.Player=true,"not visible");
            Refuses(()=>leader.Position=new Vector3(61,0,0),()=>leader.Position=new Vector3(3,0,0),"route");
            Refuses(()=>leader.Position=new Vector3(3,6,0),()=>leader.Position=new Vector3(3,0,0),"route");
            Refuses(()=>Playfield.ClearRoute=false,()=>Playfield.ClearRoute=true,"route");
            Refuses(()=>MovementController.Instance.IsNavigating=true,()=>MovementController.Instance.IsNavigating=false,"controller");
            var original=player.Position;
            // Rebirth can expose a valid GUI roster while the engine roster is empty.
            Team.IsInTeam=false;Team.Members=new SimpleChar[0];
            var reply=SyncMovementProbe.Regroup(20,4,verifiedMembers);
            Check(reply.StartsWith("Follow requested") && MovementController.Instance.FollowCalls==1 && MovementController.Instance.LastFollow.Instance==20,"Ordinary follow is sent once");
            Check(player.Position.X==original.X && player.Position.Y==original.Y && player.Position.Z==original.Z,"No position writes");
            Game.OnUpdate-=manager.Update;
            Check(SyncMovementProbe.Read(player,124).Adapter=="Unavailable","Absent plugin is explicit");
            Console.WriteLine("PASS: "+checks+" movement-probe and regroup checks; no game interaction.");return 0;
        } catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
