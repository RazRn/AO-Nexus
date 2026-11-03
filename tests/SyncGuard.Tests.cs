using System;
using System.Collections.Generic;
using System.Diagnostics;
using AOSharp.Core;
using AOSharp.Core.IPC;
using AOSharp.Core.UI;
using AOSharp.Core.Movement;
using AOSharp.Common.GameData;
using NexusTelemetry;

namespace AOSharp.Core.IPC {
    public class IPCChannelBase {
        readonly Dictionary<int,List<Action<int,IPCMessage>>> _callbacks=new Dictionary<int,List<Action<int,IPCMessage>>>();
        public int Count=>_callbacks.ContainsKey(200)?_callbacks[200].Count:0;
        public void RegisterCallback(int code,Action<int,IPCMessage> action){if(!_callbacks.ContainsKey(code))_callbacks[code]=new List<Action<int,IPCMessage>>();_callbacks[code].Add(action);}
        public void Receive(int sender,IPCMessage message){foreach(var action in _callbacks[200].ToArray())action(sender,message);}
    }
    public class IPCChannel : IPCChannelBase {}
}
namespace ManagerSync {
    public static class Receiver { public static int Calls; public static void OnMoveMessage(int sender,IPCMessage message){Calls++;} }
    public sealed class ManagerSync {
        public static Settings _settings=new Settings();
        public static IPCChannel _IPCChannel=new IPCChannel();
        public static Identity MoveSenderID;
        public void Tick(object sender,float delta){}
    }
}
namespace ManagerSync.IPCMessages {
    public class MoveMessage : IPCMessage {
        public override short Opcode=>200;
        public Identity Sender {get;set;}
        public int PlayfieldId {get;set;}=42;
        public Vector3 Position {get;set;}
    }
}
class SyncGuardTests {
    static int checks;
    static void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;}
    static void RegroupTests()
    {
        foreach(string outcome in new[]{"arrival","blocked","stalled","focus","zoning","combat","aggro","disabled","leader-closed","channel","frozen","pace","held-lease"}) {
            double now=0;bool foreground=false,leaderAlive=true;int links=0;string reason=null;
            var manager=new ManagerSync.ManagerSync();Game.OnUpdate=manager.Tick;
            ManagerSync.ManagerSync._IPCChannel=new IPCChannel();
            var channel=ManagerSync.ManagerSync._IPCChannel;
            channel.RegisterCallback(200,ManagerSync.Receiver.OnMoveMessage);
            foreach(var key in new[]{"Enable","SyncMove","IPCChannel"})ManagerSync.ManagerSync._settings.Values[key]=new Setting {Value=1};
            Game.IsZoning=false;Spell.HasPendingCast=false;Playfield.ClearRoute=true;DynelManager.Characters=new SimpleChar[0];
            var player=DynelManager.LocalPlayer;player.Position=new Vector3(0,0,0);player.IsMoving=true;player.IsAttacking=false;player.MovementState=2;
            var leader=new SimpleChar {Instance=2,Position=new Vector3(25,0,0)};DynelManager.Players=new[]{leader};
            MovementController.Instance=new MovementController();var movement=MovementController.Instance;
            var guard=new SyncMovementGuard {ClockOverride=()=>now,ForegroundOverride=()=>foreground,LeaderAliveOverride=p=>leaderAlive,
                RegroupBlocked=message=>{links++;reason=message;}};
            var process=Process.GetCurrentProcess();long qpc=Stopwatch.GetTimestamp();
            var grant=new AutoRegroupPermission {Pid=process.Id,StartTicks=process.StartTime.ToUniversalTime().Ticks,
                Character=1,Leader=2,LeaderPid=process.Id+1,LeaderStartTicks=1,Area=42,Channel=1,Members=new[]{1,2},
                IssuedQpc=qpc,LeaderUpdateQpc=qpc,Enabled=true};
            guard.SetRegroupPermission(grant);
            Action<double> tick=t=>{now=t;guard.Update();guard.SendStoppedBeacon();};
            Func<string> status=()=>{var snapshot=new MovementSnapshot();guard.Capture(snapshot);return snapshot.GuardStatus;};
            NexusShortcuts.FormationMovementLease existing=null;
            try {
                tick(0);tick(3.75);ManagerSync.ManagerSync.MoveSenderID=leader.Identity;
                channel.Receive(2,new ManagerSync.IPCMessages.MoveMessage {Sender=leader.Identity,Position=player.Position});
                if(outcome=="held-lease")existing=new NexusShortcuts.FormationMovementLease();
                for(int i=0;i<=16;i++)tick(4+i*.25);
                if(outcome=="held-lease") {
                    Check(movement.ForwardCalls==0,"Regroup never competes with an existing formation lease");
                    continue;
                }
                Check(status()=="Regrouping" && channel.Count==1 && !ManagerSync.ManagerSync._settings["SyncMove"].AsBool(),outcome+": recovery owns only movement callback");
                tick(8.25);Check(movement.ForwardCalls==1,outcome+": fresh clear route moves normally ("+status()+", calls="+movement.ForwardCalls+")");
                int forward=movement.ForwardCalls;
                if(outcome=="arrival")player.Position=new Vector3(24,0,0);
                if(outcome=="blocked")Playfield.ClearRoute=false;
                if(outcome=="focus")foreground=true;
                if(outcome=="zoning")Game.IsZoning=true;
                if(outcome=="combat")player.IsAttacking=true;
                if(outcome=="aggro")DynelManager.Characters=new[]{new SimpleChar {Instance=99,IsAttacking=true,FightingTarget=player}};
                if(outcome=="disabled")guard.SetRegroupPermission(null);
                if(outcome=="leader-closed")leaderAlive=false;
                if(outcome=="channel")ManagerSync.ManagerSync._settings["IPCChannel"].Value=2;
                if(outcome=="pace")player.MovementState=0;
                if(outcome=="stalled")for(int i=1;i<=16;i++)tick(8.25+i*.25);
                else tick(outcome=="frozen"?10:8.5);
                bool blocked=outcome=="blocked" || outcome=="stalled" || outcome=="frozen" || outcome=="pace";
                if(blocked) {
                    Check(status().StartsWith("Regroup blocked:") && !player.IsMoving,outcome+": blocked and stopped");
                    Check(links==1 && reason!=null,outcome+": single explanatory location notification");
                    forward=movement.ForwardCalls;
                    tick(now+.25);tick(now+.25);
                    Check(movement.ForwardCalls==forward && links==1 && channel.Count==1,outcome+": no retry, message spam or sync interference");
                    if(outcome=="blocked" || outcome=="stalled")Check(reason=="Stuck while regrouping","Obstruction explains stuck position");
                    foreground=true;tick(now+.25);
                } else {
                    Check(!player.IsMoving && movement.ForwardCalls==forward,outcome+": stops before any further movement");
                    Check(links==0,outcome+": no false stuck notification");
                }
                Check(channel.Count==2 && ManagerSync.ManagerSync._settings["SyncMove"].AsBool(),outcome+": original movement receiver and setting restored");
            } finally {
                existing?.Dispose();guard.Dispose();process.Dispose();
            }
            Check(channel.Count==1,outcome+": disposal retains original receiver only");
        }
    }

    static int Main(){
        double now=0;bool foreground=false;
        var manager=new ManagerSync.ManagerSync();Game.OnUpdate=manager.Tick;
        foreach(var key in new[]{"Enable","SyncMove","IPCChannel"})ManagerSync.ManagerSync._settings.Values[key]=new Setting {Value=1};
        var leader=new SimpleChar {Instance=2};DynelManager.Players=new[]{leader};
        var player=DynelManager.LocalPlayer;player.Position=new Vector3(0,0,0);
        var movement=MovementController.Instance;int beacons=0;
        var guard=new SyncMovementGuard {ClockOverride=()=>now,ForegroundOverride=()=>foreground,Stopped=()=>beacons++};
        Action<double> tick=t=>{now=t;guard.Update();guard.SendStoppedBeacon();};
        Action arm=()=> {ManagerSync.ManagerSync.MoveSenderID=leader.Identity;ManagerSync.ManagerSync._IPCChannel.Receive(2,new ManagerSync.IPCMessages.MoveMessage {Sender=leader.Identity,Position=player.Position});};
        tick(0);Check(ManagerSync.ManagerSync._IPCChannel.Count==1,"Guard attaches once");
        player.IsMoving=true;leader.Position=new Vector3(25,0,0);tick(4);tick(10);
        Check(movement.StopCalls==0,"Never stops movement without observed accepted sync");
        arm();tick(11);tick(12);tick(13);tick(13.3);
        var snapshot=new MovementSnapshot();guard.Capture(snapshot);Check(snapshot.GuardEvent=="Warning","Sustained same-frame separation warns");
        tick(14);tick(15);tick(15.3);guard.Capture(snapshot);Check(movement.StopCalls==1 && snapshot.GuardEvent=="Stopped","Stops synced run-off after grace");
        Check(beacons==1,"Beacon sent only after client confirms stopped");tick(20);Check(movement.StopCalls==1 && beacons==1,"No repeated stops or beacon spam");
        foreground=true;tick(21);player.IsMoving=true;arm();tick(30);
        Check(movement.StopCalls==1,"Foreground manual control is never stopped");
        foreground=false;tick(31);tick(35);arm();tick(36);Game.IsZoning=true;tick(39);Game.IsZoning=false;tick(45);
        Check(movement.StopCalls==1,"Zoning disarms previously owned movement");
        arm();tick(46);ManagerSync.ManagerSync._settings.Values["IPCChannel"].Value=2;tick(50);
        Check(movement.StopCalls==1,"Channel change disarms previous sender");
        guard.Dispose();Check(ManagerSync.ManagerSync._IPCChannel.Count==0,"Unload removes only owned callback");
        RegroupTests();
        Console.WriteLine("PASS: "+checks+" game-adapter guard checks; no live client interaction.");return 0;
    }
}
