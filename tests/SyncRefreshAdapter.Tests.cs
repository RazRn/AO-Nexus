using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.IPC;
using AOSharp.Core.Movement;
using AOSharp.Core.UI;
using NexusPlayTools;
using NexusShortcuts;

namespace AOSharp.Core {
    public static class Game {public static Action<object,float> OnUpdate;public static bool IsZoning;}
    public class SimpleChar {public Identity Identity;public Vector3 Position;public Quaternion Rotation;public bool IsAlive=true,IsMoving,IsAttacking;public int GetStat(Stat stat)=>0;}
    public static class DynelManager {public static SimpleChar LocalPlayer=new SimpleChar {Identity=new Identity(IdentityType.SimpleChar,1)};public static SimpleChar[] Players=new SimpleChar[0];}
    public static class Playfield {public static Identity Identity=new Identity(IdentityType.Playfield,42);}
    public static class Spell {public static bool HasPendingCast;}
    public static class Network {public static event EventHandler<SmokeLounge.AOtomation.Messaging.Messages.N3Message> N3MessageSent; public static void Send(SmokeLounge.AOtomation.Messaging.Messages.N3Message m)=>N3MessageSent?.Invoke(null,m);}
}
namespace AOSharp.Core.UI {
    public class Settings {public Dictionary<string,Setting> Values=new Dictionary<string,Setting>();public Setting this[string key]=>Values[key];}
    public class Setting {public int Value;public bool AsBool()=>Value!=0;public int AsInt32()=>Value;}
}
namespace AOSharp.Core.Movement {
    public class MovementController {public static MovementController Instance=new MovementController();public bool IsNavigating;public int Stops,Updates;
        public void SetMovement(MovementAction action){if(action==MovementAction.FullStop){Stops++;DynelManager.LocalPlayer.IsMoving=false;}if(action==MovementAction.Update)Updates++;}}
}
namespace AOSharp.Core.IPC {
    public class IPCChannelBase {readonly Dictionary<int,List<Action<int,IPCMessage>>> _callbacks=new Dictionary<int,List<Action<int,IPCMessage>>>();
        public void RegisterCallback(int id,Action<int,IPCMessage> callback){if(!_callbacks.ContainsKey(id))_callbacks[id]=new List<Action<int,IPCMessage>>();_callbacks[id].Add(callback);}
        public void Receive(int id,IPCMessage message){foreach(var callback in _callbacks[200].ToArray())callback(id,message);}
        public int Count=>_callbacks[200].Count;}
    public class IPCChannel:IPCChannelBase {}
}
namespace ManagerSync {
    public class ManagerSync {public static Settings _settings=new Settings();public static IPCChannel _IPCChannel=new IPCChannel();public static Identity MoveSenderID;public void Update(object sender,float delta){}}
}
namespace ManagerSync.IPCMessages {
    public class MoveMessage:IPCMessage {public override short Opcode=>200;public Identity Sender {get;set;}public int PlayfieldId {get;set;}public Vector3 Position {get;set;}}
}
class SyncRefreshAdapterTests {
    static int count;
    static void Check(bool result,string reason){if(!result)throw new Exception(reason);count++;}
    static FieldInfo Field(string name)=>typeof(SyncRefreshController).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance);
    static void Main(){
        Game.OnUpdate=new ManagerSync.ManagerSync().Update;
        var settings=ManagerSync.ManagerSync._settings;
        foreach(var name in new[]{"Enable","SyncMove","IPCChannel"})settings.Values[name]=new Setting {Value=1};
        settings.Values["SyncAttack"]=new Setting {Value=1};
        var player=DynelManager.LocalPlayer;var movement=MovementController.Instance;
        var leader=new SimpleChar {Identity=new Identity(IdentityType.SimpleChar,2)};DynelManager.Players=new[]{leader};
        bool foreground=false;var refresh=new SyncRefreshController("fixture",false) {ForegroundOverride=()=>foreground};
        refresh.Configure(5);refresh.Update(false);
        Check(ManagerSync.ManagerSync._IPCChannel.Count==1,"Observer installed once");
        Action arm=()=>{ManagerSync.ManagerSync.MoveSenderID=leader.Identity;ManagerSync.ManagerSync._IPCChannel.Receive(2,new ManagerSync.IPCMessages.MoveMessage {Sender=leader.Identity,PlayfieldId=42,Position=player.Position});};
        Action<bool,float> receive=(moving,x)=>{var process=Process.GetCurrentProcess();Field("incoming").SetValue(refresh,new SyncRefreshFrame {
            Pid=process.Id,StartTicks=process.StartTime.ToUniversalTime().Ticks,Character=2,Channel=1,Area=42,Sequence=1,
            Qpc=Stopwatch.GetTimestamp(),Moving=moving,X=x,Qw=1});refresh.Update(false);};
        player.IsMoving=true;receive(false,50);Check(movement.Stops==0,"Unowned stop ignored");
        arm();receive(true,1);Check(player.Position.X==1 && movement.Updates==1,"Fresh nearby state applied to moving follower");
        receive(true,50);Check(player.Position.X==1 && movement.Updates==1,"Distant moving state cannot teleport follower");
        receive(false,50);Check(movement.Stops==1 && player.Position.X==1,"Distant stop only stops; position unchanged");
        receive(true,1);Check(!player.IsMoving && movement.Updates==1,"Periodic state cannot undo safety stop");
        player.IsMoving=true;foreground=true;receive(false,50);Check(movement.Stops==1,"Foreground manual client cannot be stopped");
        foreground=false;arm();refresh.Update(true);receive(false,50);Check(movement.Stops==1 && settings["SyncAttack"].AsBool(),"Formation ownership clears refresh; attack setting preserved");
        arm();Game.IsZoning=true;refresh.Update(false);Game.IsZoning=false;receive(false,50);Check(movement.Stops==1,"Zoning invalidates owner");
        arm();settings.Values["SyncMove"].Value=0;receive(false,50);Check(movement.Stops==1,"Disabled movement sync blocks refresh");settings.Values["SyncMove"].Value=1;
        arm();settings.Values["IPCChannel"].Value=2;receive(false,50);Check(movement.Stops==1,"Channel change invalidates owner");settings.Values["IPCChannel"].Value=1;
        foreground=true;player.IsMoving=true;refresh.Configure(10);refresh.Update(false);
        var sent=(SyncRefreshFrame)Field("outgoing").GetValue(refresh);Check(sent!=null && sent.Moving && refresh.Capture().Rate==10,"Foreground publishes current state at selected trial rate");
        player.IsMoving=false;refresh.Update(false);sent=(SyncRefreshFrame)Field("outgoing").GetValue(refresh);Check(sent!=null && !sent.Moving,"Stop immediately publishes fresh stopped state");
        Field("renewed").SetValue(refresh,Stopwatch.GetTimestamp()-6*Stopwatch.Frequency);refresh.Update(false);Check(refresh.Capture().Rate==0 && Field("outgoing").GetValue(refresh)==null,"Nexus lease expiration disables experiment");
        refresh.Dispose();Check(ManagerSync.ManagerSync._IPCChannel.Count==0,"Disposal removes only experiment observer");
        Console.WriteLine("PASS: "+count+" sync refresh game adapter checks; no live game interaction.");
    }
}
