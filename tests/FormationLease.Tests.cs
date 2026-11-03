extern alias RealAOCommon;
using System;
using System.Collections.Generic;
using System.Linq;
using AOSharp.Core;
using AOSharp.Core.IPC;
using AOSharp.Core.UI;
using NexusShortcuts;
using NexusPlayTools;

namespace AOSharp.Core { public static class Game { public static Action<object,float> OnUpdate; } }
namespace AOSharp.Core.UI {
    public class Setting { public bool Value;public bool AsBool()=>Value; public int AsInt32()=>Value?1:0;public static implicit operator Setting(bool value)=>new Setting {Value=value}; }
    public class Settings {readonly Dictionary<string,Setting> values=new Dictionary<string,Setting>(); public Setting this[string key] {get=>values[key];set=>values[key]=value;} }
}
namespace AOSharp.Core.IPC {
    public class IPCMessage { }
    public class IPCChannelBase {
        readonly Dictionary<int,List<Action<int,IPCMessage>>> _callbacks=new Dictionary<int,List<Action<int,IPCMessage>>>();
        public void Add(int code,Action<int,IPCMessage> callback){if(!_callbacks.ContainsKey(code))_callbacks[code]=new List<Action<int,IPCMessage>>();_callbacks[code].Add(callback);}
        public Action<int,IPCMessage>[] Callbacks(int code)=>_callbacks[code].ToArray();
        public void Receive(int code){foreach(var action in _callbacks[code].ToArray())action(1,new IPCMessage());}
    }
    public class IPCChannel:IPCChannelBase { }
}
namespace ManagerSync {
    public sealed class ManagerSync {
        public static Settings _settings=new Settings();public static IPCChannel _IPCChannel;
        public void Tick(object sender,float delta) { }
    }
    public static class Receiver {public static int Moves,Attacks;public static void OnMoveMessage(int sender,IPCMessage message){Moves++;}public static void OnAttack(int sender,IPCMessage message){Attacks++;}}
}
static class FormationLeaseTests {
    static int count;static void Check(bool pass,string name){if(!pass)throw new Exception(name);count++;}
    static void Main(){
        Game.OnUpdate=new ManagerSync.ManagerSync().Tick;
        var settings=ManagerSync.ManagerSync._settings;settings["Enable"]=true;settings["SyncMove"]=true;settings["SyncAttack"]=true;
        var channel=new IPCChannel();ManagerSync.ManagerSync._IPCChannel=channel;
        int guard=0;channel.Add(200,ManagerSync.Receiver.OnMoveMessage);channel.Add(200,(s,m)=>guard++);channel.Add(201,ManagerSync.Receiver.OnAttack);
        var originals=channel.Callbacks(200);
        using(var lease=new FormationMovementLease()) {
            channel.Receive(200);channel.Receive(201);
            Check(ManagerSync.Receiver.Moves==0,"Incoming corrections are blocked, not just outgoing SyncMove");
            Check(ManagerSync.Receiver.Attacks==1 && settings["SyncAttack"].AsBool(),"Attack sync stays enabled and receives events");
            Check(guard==1 && !settings["SyncMove"].AsBool(),"Observer callback preserved, movement sending suspended");
            Check(lease.Intact,"Owned receiver remains suspended");
        }
        Check(channel.Callbacks(200).SequenceEqual(originals),"Stop restores original callbacks in original order");
        Check(settings["SyncMove"].AsBool(),"Stop restores previous movement setting");
        settings["SyncMove"]=false;
        var disabled=new FormationMovementLease();disabled.Dispose();disabled.Dispose();
        Check(!settings["SyncMove"].AsBool() && channel.Callbacks(200).Length==2,"Off stays off; double disposal is harmless");
        var changed=new FormationMovementLease();settings["SyncMove"]=true;
        Check(!changed.Intact,"Manual movement re-enable relinquishes control");changed.Dispose();
        Check(settings["SyncMove"].AsBool(),"Manual re-enable preserved");
        using(var changedChannel=new FormationMovementLease()) {
            ManagerSync.ManagerSync._IPCChannel=new IPCChannel();Check(!changedChannel.Intact,"Channel replacement invalidates ownership");
        }
        bool refused=false;try{new FormationMovementLease();}catch(InvalidOperationException){refused=true;}
        Check(refused && settings["SyncMove"].AsBool(),"Unknown receiver refused without changing settings");
        var trail=new FormationTrail();trail.Add(new TrailPoint(0,0,0));trail.Add(new TrailPoint(10,0,0));trail.Add(new TrailPoint(10,0,10));
        var point=FormationTrail.Behind(trail.Points.ToArray(),15,null);
        Check(Math.Abs(point.X-5)<.01 && Math.Abs(point.Z)<.01,"Spacing measured along route through corner");
        point=FormationTrail.Behind(trail.Points.ToArray(),4,null);
        Check(Math.Abs(point.X-10)<.01 && Math.Abs(point.Z-6)<.01,"Nearest follower interpolates last segment");
        for(int i=0;i<400;i++)trail.Add(new TrailPoint(i,0,0));Check(trail.Points.Count==256,"Route memory bounded");
        var request=new FormationRequest {Action="follow",Session=Guid.NewGuid().ToString("N"),Leader=1,Members=new[]{1,2},Spacing=2,Playfield=42,Slot=1,Channel=1};
        Check(request.Valid(),"Valid formation accepted");request.Spacing=float.NaN;Check(!request.Valid(),"NaN spacing rejected");
        request.Spacing=2;request.Members=new[]{1,1};Check(!request.Valid(),"Duplicate team identities rejected");
        request.Members=new[]{1,2};request.Action="circle";Check(request.Valid(),"Circle request accepted");
        request.Slot=2;Check(!request.Valid(),"Slot outside actual team rejected");
        foreach(int followers in new[]{1,2,3,4,5}) {
            var centre=new TrailPoint(10,4,20);var points=Enumerable.Range(1,followers).Select(slot=>CircleFormation.Target(centre,3,slot,followers)).ToArray();
            Check(points.All(p=>Math.Abs(p.Distance(centre)-3)<.001 && p.Y==4),"Circle radius and elevation for "+followers+" followers");
            if(followers>1){
                double chord=6*Math.Sin(Math.PI/followers);
                Check(Enumerable.Range(0,followers).All(i=>Math.Abs(points[i].Distance(points[(i+1)%followers])-chord)<.001),"Even full-circle spacing for "+followers+" followers");
            }
        }
        bool invalidCircle=false;try{CircleFormation.Target(new TrailPoint(),3,6,5);}catch(ArgumentException){invalidCircle=true;}
        Check(invalidCircle,"Invalid circle slot refused");
        request.Action="funny";request.Slot=1;Check(request.Valid(),"Funny business request accepted");
        var random=new Random(42);
        foreach(float radius in new[]{1f,3f,10f}) {
            var samples=Enumerable.Range(0,1000).Select(i=>FunnyBusiness.Offset(random,radius)).ToArray();
            Check(samples.All(p=>p.Distance(new TrailPoint())<=radius*.80001 && p.Y==0),"Wander destinations stay inside radius "+radius);
            Check(samples.Any(p=>p.X<0) && samples.Any(p=>p.X>0) && samples.Any(p=>p.Z<0) && samples.Any(p=>p.Z>0),"Wandering covers all quadrants");
        }
        Check(FunnyBusiness.GroundPace(2) && FunnyBusiness.GroundPace(3) && !FunnyBusiness.GroundPace(4) && !FunnyBusiness.GroundPace(8),"Only walk/run copied; swimming and sitting pause");
        Check(FunnyBusiness.Emotes.Length==65 && FunnyBusiness.Emotes.Distinct().Count()==65 && FunnyBusiness.Emotes.Contains("rocky"),"All 65 client-menu emotes available without duplicates");
        var social=Enum.GetNames(typeof(RealAOCommon::SmokeLounge.AOtomation.Messaging.Messages.N3Messages.SocialAction));
        Check(FunnyBusiness.Emotes.All(name=>social.Count(value=>FunnyBusiness.EmoteAlias(value)==name.Replace("-",""))==1),"Every menu emote maps to exactly one real AO# action, including flip/backflip");
        var travel=new FormationTravel();travel.Reset(new TrailPoint());travel.Observe(new TrailPoint(1,0,0));travel.Observe(new TrailPoint());
        Check(travel.Distance==2,"Random wandering that returns to its starting point still counts as progress");
        travel.Reset(new TrailPoint());travel.Observe(new TrailPoint());Check(travel.Distance==0,"Stationary follower still fails the movement-progress check");
        var turn=FunnyBusiness.TurnAway(new Random(1),new TrailPoint(),new TrailPoint(1,0,0),new TrailPoint(3,0,0),5,p=>true);
        Check(turn.X<1 && turn.Distance(new TrailPoint())<=4,"Obstacle first turns follower away and stays inside radius");
        int probes=0;turn=FunnyBusiness.TurnAway(new Random(2),new TrailPoint(),new TrailPoint(),new TrailPoint(3,0,0),5,p=>{probes++;return false;});
        Check(turn==null && probes<=13,"Enclosed follower waits after bounded checks rather than moving through obstacle");
        turn=FunnyBusiness.TurnAway(new Random(3),new TrailPoint(),new TrailPoint(1,0,0),new TrailPoint(3,0,0),5,p=>p.Z>1);
        Check(turn!=null && turn.Z>1 && turn.Distance(new TrailPoint())<=4.001,"Blocked turnaround tries a reachable alternate direction");
        Console.WriteLine("PASS: "+count+" formation ownership, restoration, route and request checks.");
    }
}
