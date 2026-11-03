// This is an inert game-loop stand-in. It uses the production local IPC transport
// and the production plugin source; it never loads a game or injects anything.
using System;
using System.Diagnostics;
using System.Threading;
namespace AOSharp.Common.Unmanaged.Imports
{
    public static class TeamViewModule_c
    {
        public static IntPtr Instance;
        public static byte InTeam, Leader;
        public static IntPtr GetInstanceIfAny() => Instance;
        public static byte IsInTeam(IntPtr view) => InTeam;
        public static byte IsTeamLeader(IntPtr view) => Leader;
    }
}
namespace AOSharp.Core
{
    public static class Team { public static bool IsInTeam,IsLeader; public static SimpleChar[] Members=new SimpleChar[0]; }
    public class SimpleChar {
        public int Instance=1,Owner,Family,Type;
        public bool Pet,Player=true;
        public bool IsAlive=true;
        public bool IsAttacking {get;set;}
        public SimpleChar FightingTarget {get;set;}
        public AOSharp.Common.GameData.Vector3 Position;
        public AOSharp.Common.GameData.Identity Identity => new AOSharp.Common.GameData.Identity(AOSharp.Common.GameData.IdentityType.SimpleChar,Instance);
        public bool IsPet => Pet;
        public bool IsPlayer => Player;
        public string Profession => "Doctor";
        public string Name => Instance==1?"Synthetic target":"Nearby "+Instance;
        public int GetStat(AOSharp.Common.GameData.Stat stat)=>stat==AOSharp.Common.GameData.Stat.ConflictReputation?Owner:
            stat==AOSharp.Common.GameData.Stat.Flags?(Pet?(int)AOSharp.Common.GameData.CharacterFlags.Pet:Instance==23?-1:0):
            stat==AOSharp.Common.GameData.Stat.NPCFamily?Family:stat==AOSharp.Common.GameData.Stat.PetType?Type:0;
    }
    public sealed class LocalPlayer : SimpleChar {
        public Pet[] Pets {get;set;}=new Pet[0];
        public bool IsFalling { get; set; }
        public bool IsMoving { get; set; }
        public int MovementState=2;
        public int Health => 100;
        public int MaxHealth => 100;
        public int Nano => 50;
        public int MaxNano => 100;
    }
    public sealed class Pet {public AOSharp.Common.GameData.Identity Identity {get;set;} public SimpleChar Character=>new SimpleChar {Instance=Identity.Instance,Player=false,Pet=true}; }
    public static class Playfield {
        public static string Name=>"Fixture zone";
        public static AOSharp.Common.GameData.Identity Identity=>new AOSharp.Common.GameData.Identity(AOSharp.Common.GameData.IdentityType.Playfield,42);
        public static bool ClearRoute=true;
        public static bool LineOfSight(AOSharp.Common.GameData.Vector3 from,AOSharp.Common.GameData.Vector3 to)=>ClearRoute;
    }
    public static class DynelManager { public static LocalPlayer LocalPlayer { get; } = new LocalPlayer();
        public static SimpleChar[] Players=new SimpleChar[0];
        public static SimpleChar[] Characters=new SimpleChar[0];
        public static SimpleChar RegroupLeader;
        public static bool Find(AOSharp.Common.GameData.Identity identity,out SimpleChar character){
            int id=identity.Instance;
            if(RegroupLeader!=null && RegroupLeader.Instance==id){character=RegroupLeader;return true;}
            character=id>=20 && id<=27?new SimpleChar {Instance=id,Pet=id==21||id==22,Player=id==20,
                Owner=id==21||id==24?20:id==22||id==23?999:id==27?23:0,
                Family=id==25?(int)AOSharp.Common.GameData.NpcClan.EngineerAttackPet:0,
                Type=id==26||id==27?(int)AOSharp.Common.GameData.PetType.Attack:0}:null;
            return character!=null;
        }
    }
    public sealed class Spell {
        public static bool HasPendingCast;
        public string Name => "Synthetic healing nano";
        public static bool Find(int id,out Spell spell) { spell=id==1?new Spell():null;return spell!=null; }
    }
    public static class MiscClientEvents {
        public static event EventHandler<AOSharp.Common.SharedEventArgs.AttemptingSpellCastEventArgs> AttemptingSpellCast;
        public static void Cast() { AttemptingSpellCast?.Invoke(null,
            new AOSharp.Common.SharedEventArgs.AttemptingSpellCastEventArgs(
                new AOSharp.Common.GameData.Identity(AOSharp.Common.GameData.IdentityType.NanoProgram, 1),
                new AOSharp.Common.GameData.Identity(AOSharp.Common.GameData.IdentityType.SimpleChar, 2))); }
    }
    public static class Network {
        public static void Send(SmokeLounge.AOtomation.Messaging.Messages.N3Message message){N3MessageSent?.Invoke(null,message);}
        public static event EventHandler<SmokeLounge.AOtomation.Messaging.Messages.N3Message> N3MessageReceived;
        public static void Hit(){N3MessageReceived?.Invoke(null,new SmokeLounge.AOtomation.Messaging.Messages.N3Messages.AttackInfoMessage {Identity=DynelManager.LocalPlayer.Identity,Amount=100});}
        public static void PetHits(){
            var own=new AOSharp.Common.GameData.Identity(AOSharp.Common.GameData.IdentityType.SimpleChar,11);
            var other=new AOSharp.Common.GameData.Identity(AOSharp.Common.GameData.IdentityType.SimpleChar,12);
            DynelManager.LocalPlayer.Pets=new[]{new Pet {Identity=own}};
            // These identities have no dynels, owner stat or Pet flag.
            N3MessageReceived?.Invoke(null,new SmokeLounge.AOtomation.Messaging.Messages.N3Messages.AttackInfoMessage {Identity=own,Amount=40});
            N3MessageReceived?.Invoke(null,new SmokeLounge.AOtomation.Messaging.Messages.N3Messages.HealthDamageMessage {Target=own,Amount=-10});
            N3MessageReceived?.Invoke(null,new SmokeLounge.AOtomation.Messaging.Messages.N3Messages.AttackInfoMessage {Identity=other,Amount=999});
            DynelManager.LocalPlayer.Pets=new Pet[0];
            N3MessageReceived?.Invoke(null,new SmokeLounge.AOtomation.Messaging.Messages.N3Messages.AttackInfoMessage {Identity=own,Amount=999});
        }
        public static event EventHandler<SmokeLounge.AOtomation.Messaging.Messages.N3Message> N3MessageSent;
        public static void NearbyHits(){
            foreach(var pair in new[]{new[]{20,200},new[]{21,60},new[]{22,7},new[]{23,999},new[]{24,11},new[]{25,7},new[]{26,9},new[]{27,5}})
                N3MessageReceived?.Invoke(null,new SmokeLounge.AOtomation.Messaging.Messages.N3Messages.AttackInfoMessage {
                    Identity=new AOSharp.Common.GameData.Identity(AOSharp.Common.GameData.IdentityType.SimpleChar,pair[0]),Amount=pair[1]});
            N3MessageReceived?.Invoke(null,new SmokeLounge.AOtomation.Messaging.Messages.N3Messages.HealthDamageMessage {
                Target=new AOSharp.Common.GameData.Identity(AOSharp.Common.GameData.IdentityType.SimpleChar,21),Amount=-40});
            N3MessageReceived?.Invoke(null,new SmokeLounge.AOtomation.Messaging.Messages.N3Messages.AttackInfoMessage {
                Identity=DynelManager.LocalPlayer.Identity,Target=new AOSharp.Common.GameData.Identity(AOSharp.Common.GameData.IdentityType.SimpleChar,23),Amount=123});
            N3MessageReceived?.Invoke(null,new SmokeLounge.AOtomation.Messaging.Messages.N3Messages.HealthDamageMessage {
                Target=new AOSharp.Common.GameData.Identity(AOSharp.Common.GameData.IdentityType.SimpleChar,20),Identity=new AOSharp.Common.GameData.Identity(AOSharp.Common.GameData.IdentityType.SimpleChar,23),Amount=-45});
        }
        public static void Cast() { N3MessageSent?.Invoke(null, new SmokeLounge.AOtomation.Messaging.Messages.N3Messages.CharacterActionMessage {
            Action = SmokeLounge.AOtomation.Messaging.GameData.CharacterActionType.CastNano, Parameter2=2 }); }
    }
    public abstract class AOPluginEntry { public virtual void Run() { } public virtual void Teardown() { } }
    public static class Game
    {
        public static EventHandler<float> OnUpdate;
        public static bool IsZoning;
        public static int ClientInst=>DynelManager.LocalPlayer.Identity.Instance;
        public static void Tick() { OnUpdate?.Invoke(null, .01f); }
    }
}
namespace AOSharp.Core.UI
{
    public sealed class Settings {
        public readonly System.Collections.Generic.Dictionary<string,Setting> Values=new System.Collections.Generic.Dictionary<string,Setting>();
        public Setting this[string name] {get=>Values[name];set=>Values[name]=value;}
    }
    public sealed class Setting {
        public int Value;
        public static implicit operator Setting(bool value)=>new Setting {Value=value?1:0};
        public bool AsBool()=>Value!=0;
        public int AsInt32()=>Value;
    }
    public static class Chat {
        public static void WriteLine(string message){}
        public static Action<string> FeedbackReceived;
        public static void Feedback() { FeedbackReceived?.Invoke("You can't execute nanoprograms while falling!"); }
        public static void Award() { FeedbackReceived?.Invoke("You received 123 xp."); FeedbackReceived?.Invoke("You gained 45 new Alien Experience Points."); }
    }
}
namespace AOSharp.Core.Movement
{
    public sealed class MovementController {
        public static MovementController Instance=new MovementController();
        public bool IsNavigating;
        public int FollowCalls;
        public AOSharp.Common.GameData.Identity LastFollow;
        public void Follow(AOSharp.Common.GameData.Identity target){FollowCalls++;LastFollow=target;}
        public int StopCalls,ForwardCalls;
        public void LookAt(AOSharp.Common.GameData.Vector3 target){}
        public void SetMovement(AOSharp.Common.GameData.MovementAction action){if(action==AOSharp.Common.GameData.MovementAction.ForwardStart){ForwardCalls++;AOSharp.Core.DynelManager.LocalPlayer.IsMoving=true;}if(action==AOSharp.Common.GameData.MovementAction.FullStop){StopCalls++;AOSharp.Core.DynelManager.LocalPlayer.IsMoving=false;}}
    }
}
namespace NexusTelemetry
{
    // Transport/leader tests intentionally exclude movement and chat actions.
    // The real guard has its own isolated adapter tests.
#if !SYNC_GUARD_TEST
    internal sealed class SyncMovementGuard : IDisposable {
        internal Action Stopped;internal Action<string> RegroupBlocked;internal void SetRegroupPermission(AutoRegroupPermission p){} internal void Update(){} internal void Capture(MovementSnapshot s){}
        internal void SendStoppedBeacon(){} public void Dispose(){}
    }
#endif
    internal sealed class RallyBeacon : IDisposable {
        internal void Send(bool stopped){} internal void Send(string reason){} internal void Update(){} public void Dispose(){}
    }
}
namespace NexusTelemetryFixture
{
    static class Program
    {
        static string mode = "normal";
        static int Main()
        {
            var plugin = new NexusTelemetry.NexusHealthPlugin();
            plugin.Run();
            new Thread(() => { string line; while ((line = Console.ReadLine()) != null) Volatile.Write(ref mode, line); Volatile.Write(ref mode, "quit"); }) { IsBackground = true }.Start();
            using (var p = Process.GetCurrentProcess()) Console.WriteLine(p.Id + " " + p.StartTime.ToUniversalTime().Ticks);
            bool unloaded = false;
            while (true) {
                string state = Volatile.Read(ref mode);
                if (state == "crash") Environment.Exit(37); // Only this inert fixture exits.
                if (state == "quit") break;
                if (state == "unload" && !unloaded) { plugin.Teardown(); unloaded = true; }
                if (!unloaded) {
                    AOSharp.Core.Team.IsInTeam=state=="leader" || state=="member";
                    AOSharp.Core.Team.IsLeader=state=="leader" || state=="solo-leader";
                    AOSharp.Core.DynelManager.LocalPlayer.IsFalling = state == "falling" || state == "falling-network";
                    if (state == "falling") AOSharp.Core.MiscClientEvents.Cast();
                    if (state == "falling-network") AOSharp.Core.Network.Cast();
                    if (state == "falling-feedback") AOSharp.Core.UI.Chat.Feedback();
                    if (state == "combat") { AOSharp.Core.Network.Hit();AOSharp.Core.UI.Chat.Award();Volatile.Write(ref mode,"normal"); }
                    if (state == "pet-combat") { AOSharp.Core.Network.PetHits();Volatile.Write(ref mode,"normal"); }
                    if (state == "nearby-combat") { AOSharp.Core.Network.NearbyHits();Volatile.Write(ref mode,"normal"); }
                    if (state == "ticks-only" || state == "ipc-only") {
                        string method = state == "ticks-only" ? "Update" : "ReplyToProbe";
                        typeof(NexusTelemetry.NexusHealthPlugin).GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                            .Invoke(plugin, new object[] { null, .01f });
                    } else if (state != "pause") AOSharp.Core.Game.Tick();
                }
                Thread.Sleep(10);
            }
            if (!unloaded) plugin.Teardown();
            return 0;
        }
    }
}
