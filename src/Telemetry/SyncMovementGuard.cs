using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.IPC;
using AOSharp.Core.Movement;
using AOSharp.Core.UI;

namespace NexusTelemetry
{
    // Uses the already loaded AOSP channel, ordinary FullStop, and same-frame game
    // positions. Never expands SyncManager's teleport/correction distance.
    internal sealed partial class SyncMovementGuard : IDisposable
    {
        readonly SyncSeparationPolicy policy=new SyncSeparationPolicy();
        IPCChannel channel;
        Type manager;
        Settings settings;
        Action<int,IPCMessage> callback;
        int owner, area, ownerChannel;
        double resumeAfter, nextCheck;
        bool disposed;
        string status="Waiting";
        string eventKind;
        long eventQpc;
        double eventDistance;
        int eventLeader;
        internal Action Stopped;
        internal Action<string> RegroupBlocked;
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
        readonly int pid=Process.GetCurrentProcess().Id;
        internal Func<bool> ForegroundOverride;
        internal Func<double> ClockOverride;
        bool Foreground { get { if(ForegroundOverride!=null)return ForegroundOverride();GetWindowThreadProcessId(GetForegroundWindow(),out uint active);return active==pid; } }
        double Clock=>ClockOverride!=null?ClockOverride():Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;

        bool Available => settings!=null && settings["Enable"].AsBool() && settings["SyncMove"].AsBool();
        void Disarm(double now) { EndRegroup();owner=0;policy.Reset();resumeAfter=now+3;status="Waiting"; }
        void Attach()
        {
            var instance=Game.OnUpdate?.GetInvocationList().Select(d=>d.Target)
                .FirstOrDefault(o=>o?.GetType().FullName=="ManagerSync.ManagerSync");
            if(instance==null)return;
            var type=instance.GetType();
            var current=type.GetField("_IPCChannel",BindingFlags.Public|BindingFlags.Static)?.GetValue(null) as IPCChannel;
            if(current==null || current==channel)return;
            Detach();manager=type;channel=current;
            settings=type.GetField("_settings",BindingFlags.Public|BindingFlags.Static)?.GetValue(null) as Settings;
            callback=ObserveMove;
            channel.RegisterCallback(200,callback);
        }
        void ObserveMove(int sender,IPCMessage message)
        {
            if(disposed)return;
            if(regroupLease!=null)return; // Recovery owns movement, including the blocked hold.
            try {
                double now=Clock;
                var player=DynelManager.LocalPlayer;
                if(Game.IsZoning || player==null || Foreground || !Available) { Disarm(now);return; }
                if(now<resumeAfter || message.GetType().FullName!="ManagerSync.IPCMessages.MoveMessage")return;
                var type=message.GetType();
                var from=(Identity)type.GetProperty("Sender").GetValue(message,null);
                int playfield=(int)type.GetProperty("PlayfieldId").GetValue(message,null);
                var position=(Vector3)type.GetProperty("Position").GetValue(message,null);
                var accepted=(Identity)manager.GetField("MoveSenderID",BindingFlags.Public|BindingFlags.Static).GetValue(null);
                if(sender!=from.Instance || from==player.Identity || accepted!=from || playfield!=Playfield.Identity.Instance)return;
                if(!DynelManager.Players.Any(p=>p.Identity==from))return;
                // Callback runs after AOSP's receiver. An accepted coordinate correction
                // leaves us within its limit; a rejected distant message cannot arm us.
                double limit=2*(1+Math.Max(0,player.GetStat(Stat.RunSpeed))/100.0);
                if(player.Position.Distance2DFrom(position)>limit)return;
                if(owner!=from.Instance || area!=playfield)policy.Reset();
                owner=from.Instance;area=playfield;ownerChannel=settings["IPCChannel"].AsInt32();status="Watching";
            } catch(Exception ex) { Disarm(Clock);status="Unavailable: "+ex.GetType().Name; }
        }
        internal void Update()
        {
            if(disposed)return;
            double now=Clock;
            if(now<nextCheck)return;nextCheck=now+.2;
            try {
                Attach();
                if(UpdateRegroup(now))return;
                var player=DynelManager.LocalPlayer;
                if(Game.IsZoning || player==null || !player.IsAlive || Foreground || !Available
                    || MovementController.Instance.IsNavigating || player.IsAttacking || Spell.HasPendingCast) { Disarm(now);return; }
                if(owner==0 || now<resumeAfter)return;
                if(ownerChannel!=settings["IPCChannel"].AsInt32()) { Disarm(now);return; }
                if(area!=Playfield.Identity.Instance) { Disarm(now);return; }
                var leader=DynelManager.Players.FirstOrDefault(p=>p.Identity.Instance==owner);
                // Do not infer distance across zoning or visibility boundaries.
                if(leader==null) { Disarm(now);return; }
                double distance=player.Position.Distance2DFrom(leader.Position);
                double limit=2*(1+Math.Max(0,player.GetStat(Stat.RunSpeed))/100.0);
                string action=policy.Observe(now,distance,limit,player.IsMoving);
                if(action==null)return;
                if(action=="Stopped")MovementController.Instance.SetMovement(MovementAction.FullStop);
                status=action;eventKind=action;eventQpc=Stopwatch.GetTimestamp();eventDistance=distance;eventLeader=owner;
                if(action=="Stopped" && TryRegroup(now,distance))return;
                if(action=="Stopped")pendingBeacon=true;
                Chat.WriteLine(action=="Stopped"?"Nexus: sync lost — movement stopped. Regroup before continuing.":"Nexus: sync separation — regroup before continuing.");
            } catch(Exception ex) { Disarm(now);status="Unavailable: "+ex.GetType().Name;Trace.TraceWarning("Sync guard update: {0}",ex); }
        }
        bool pendingBeacon;
        internal void SendStoppedBeacon()
        {
            // FullStop is queued; wait for the client to observe a stopped character.
            if(!pendingBeacon)return;
            if(Game.IsZoning || Foreground) { pendingBeacon=false;return; }
            if(DynelManager.LocalPlayer?.IsMoving==false) {
                pendingBeacon=false;
                if(blockedMessage!=null) {var message=blockedMessage;blockedMessage=null;RegroupBlocked?.Invoke(message);}
                else Stopped?.Invoke();
            }
        }
        internal void Capture(MovementSnapshot snapshot)
        {
            snapshot.GuardStatus=status;snapshot.GuardEvent=eventKind;snapshot.GuardEventQpc=eventQpc;
            snapshot.GuardDistance=eventDistance;snapshot.GuardLeader=eventLeader;
            snapshot.AutoRegroupSupported=true;
        }
        void Detach()
        {
            // AO# exposes registration but no public unregister. Remove only our delegate.
            if(channel!=null && callback!=null) {
                var field=typeof(IPCChannelBase).GetField("_callbacks",BindingFlags.Instance|BindingFlags.NonPublic);
                var callbacks=field?.GetValue(channel) as Dictionary<int,List<Action<int,IPCMessage>>>;
                if(callbacks!=null && callbacks.TryGetValue(200,out var list))list.Remove(callback);
            }
            channel=null;callback=null;settings=null;Disarm(Clock);
        }
        public void Dispose() { disposed=true;try { Detach(); } catch(Exception ex) { Trace.TraceWarning("Sync guard detach: {0}",ex.Message); } }
    }
}
