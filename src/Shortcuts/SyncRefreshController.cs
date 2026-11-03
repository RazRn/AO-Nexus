using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.IPC;
using AOSharp.Core.Movement;
using AOSharp.Core.UI;
using NexusPlayTools;
using SmokeLounge.AOtomation.Messaging.Messages;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;
using Path = System.IO.Path;

namespace NexusShortcuts
{
    // Supplemental LOCAL state only. The original plugin and its LAN packets are unchanged.
    // All game reads/writes and IPC registration run on the game thread; disk I/O does not.
    internal sealed class SyncRefreshController : IDisposable
    {
        readonly string folder;
        readonly int pid=Process.GetCurrentProcess().Id;
        readonly long started=Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks;
        readonly Timer io;
        readonly bool ioEnabled;
        IPCChannel channel;
        Settings settings;
        Type manager;
        Action<int,IPCMessage> observer;
        volatile SyncRefreshFrame outgoing,incoming;
        volatile string readPath,writePath;
        long renewed,nextFrame,sequence,armedAt,lastFrame,lastSent,lastReceived,nextAttach,stopUntil,lastSeen;
        int owner,ownerArea,ownerChannel,ioBusy;
        volatile int rate;
        bool disposed,wasMoving;
        long published,ioErrors;
        SyncRefreshReport report=new SyncRefreshReport();
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
        internal Func<bool> ForegroundOverride;
        bool Foreground {get{if(ForegroundOverride!=null)return ForegroundOverride();GetWindowThreadProcessId(GetForegroundWindow(),out uint active);return active==pid;}}
        bool Enabled=>settings!=null && settings["Enable"].AsBool() && settings["SyncMove"].AsBool();
        internal SyncRefreshController(string scope,bool startIo=true)
        {
            folder=Path.Combine(ToolFiles.Root,"SyncRefresh",scope);
            Network.N3MessageSent+=Sent;
            ioEnabled=startIo;
            io=new Timer(Exchange,null,Timeout.Infinite,Timeout.Infinite);
        }
        internal void Configure(int value)
        {
            if(!SyncRefreshPolicy.ValidRate(value))return;
            renewed=Stopwatch.GetTimestamp();
            if(rate==value)return;
            rate=value;ClearOwner();outgoing=null;wasMoving=false;nextFrame=0;lastSent=lastReceived=0;
            report=new SyncRefreshReport {Rate=value,Status=value==0?"Off":"Waiting for movement"};
            Interlocked.Exchange(ref published,0);Interlocked.Exchange(ref ioErrors,0);
            if(ioEnabled)io.Change(value==0?Timeout.Infinite:0,value==0?Timeout.Infinite:1000/value);
        }
        void ClearOwner(){owner=0;armedAt=lastFrame=lastSeen=0;readPath=null;incoming=null;}
        void Attach(long now)
        {
            if(now<nextAttach)return;nextAttach=now+Stopwatch.Frequency;
            var instance=Game.OnUpdate?.GetInvocationList().Select(d=>d.Target).FirstOrDefault(o=>o?.GetType().FullName=="ManagerSync.ManagerSync");
            var type=instance?.GetType();
            var current=type?.GetField("_IPCChannel",BindingFlags.Public|BindingFlags.Static)?.GetValue(null) as IPCChannel;
            if(current==channel)return;
            Detach();if(current==null)return;
            manager=type;channel=current;settings=type.GetField("_settings",BindingFlags.Public|BindingFlags.Static)?.GetValue(null) as Settings;
            observer=Received;channel.RegisterCallback(200,observer);
        }
        void Sent(object sender,N3Message message)
        {
            if(!(message is CharDCMoveMessage) || Game.IsZoning || DynelManager.LocalPlayer==null || !Foreground || !Enabled)return;
            if(message.Identity!=DynelManager.LocalPlayer.Identity)return;
            long now=Stopwatch.GetTimestamp();report.NativeSent++;
            if(lastSent!=0 && DynelManager.LocalPlayer.IsMoving){report.SendGapMs=(now-lastSent)*1000.0/Stopwatch.Frequency;report.MaxSendGapMs=Math.Max(report.MaxSendGapMs,report.SendGapMs);}
            lastSent=now;
        }
        void Received(int sender,IPCMessage message)
        {
            try {
                if(Game.IsZoning || DynelManager.LocalPlayer==null || Foreground || !Enabled || message.GetType().FullName!="ManagerSync.IPCMessages.MoveMessage")return;
                var type=message.GetType();var from=(Identity)type.GetProperty("Sender").GetValue(message,null);
                int area=(int)type.GetProperty("PlayfieldId").GetValue(message,null);
                var player=DynelManager.LocalPlayer;
                if(from.Instance!=sender || from==player.Identity || area!=Playfield.Identity.Instance || !DynelManager.Players.Any(p=>p.Identity==from))return;
                long now=Stopwatch.GetTimestamp();report.NativeReceived++;
                if(lastReceived!=0 && player.IsMoving){report.ReceiveGapMs=(now-lastReceived)*1000.0/Stopwatch.Frequency;report.MaxReceiveGapMs=Math.Max(report.MaxReceiveGapMs,report.ReceiveGapMs);}
                lastReceived=now;
                if(rate==0)return;
                var accepted=(Identity)manager.GetField("MoveSenderID",BindingFlags.Public|BindingFlags.Static).GetValue(null);
                var position=(Vector3)type.GetProperty("Position").GetValue(message,null);
                double limit=2*(1+Math.Max(0,player.GetStat(Stat.RunSpeed))/100.0);
                if(accepted!=from || player.Position.Distance2DFrom(position)>limit)return;
                int currentChannel=settings["IPCChannel"].AsInt32();
                if(owner!=sender || ownerArea!=area || ownerChannel!=currentChannel){ClearOwner();owner=sender;ownerArea=area;ownerChannel=currentChannel;armedAt=now;}
                readPath=Path.Combine(folder,ownerChannel+"-"+owner+".json");
            } catch(Exception ex){ClearOwner();report.Status="Observer unavailable: "+ex.GetType().Name;}
        }
        internal void Update(bool formationActive)
        {
            if(disposed)return;
            long now=Stopwatch.GetTimestamp();
            try {
                Attach(now);
                if(rate!=0 && now-renewed>5*Stopwatch.Frequency)Configure(0);
                report.Rate=rate;
                if(rate==0){outgoing=null;ClearOwner();report.Status="Event only";return;}
                var player=DynelManager.LocalPlayer;
                if(formationActive || !Enabled || Game.IsZoning || player==null || !player.IsAlive || player.IsAttacking || Spell.HasPendingCast || MovementController.Instance.IsNavigating){outgoing=null;ClearOwner();wasMoving=false;report.Status="Paused";return;}
                if(Foreground) {
                    ClearOwner();bool moving=player.IsMoving;
                    if(wasMoving && !moving){stopUntil=now+2*Stopwatch.Frequency;nextFrame=0;}
                    wasMoving=moving;
                    if(now<nextFrame)return;nextFrame=now+Stopwatch.Frequency/rate;
                    if(!moving && now>stopUntil){outgoing=null;report.Status="Ready";return;}
                    var p=player.Position;var q=player.Rotation;
                    writePath=Path.Combine(folder,settings["IPCChannel"].AsInt32()+"-"+player.Identity.Instance+".json");
                    outgoing=new SyncRefreshFrame {Character=player.Identity.Instance,Pid=pid,StartTicks=started,Qpc=now,Sequence=++sequence,
                        Area=Playfield.Identity.Instance,Channel=settings["IPCChannel"].AsInt32(),Moving=moving,X=p.X,Y=p.Y,Z=p.Z,Qx=q.X,Qy=q.Y,Qz=q.Z,Qw=q.W};
                    report.Status=moving?"Publishing":"Publishing stop";return;
                }
                outgoing=null;wasMoving=false;
                if(owner==0){report.Status="Waiting for movement";return;}
                if(ownerArea!=Playfield.Identity.Instance || ownerChannel!=settings["IPCChannel"].AsInt32() || !DynelManager.Players.Any(p=>p.Identity.Instance==owner)){ClearOwner();report.Status="Waiting for visible owner";return;}
                var frame=incoming;if(frame==null || frame.Qpc==lastSeen)return;lastSeen=frame.Qpc;
                double distance=player.Position.Distance2DFrom(new Vector3(frame.X,frame.Y,frame.Z));
                report.FrameAgeMs=(now-frame.Qpc)*1000.0/Stopwatch.Frequency;report.Distance=distance;
                var decision=SyncRefreshPolicy.Decide(frame,now,Stopwatch.Frequency,armedAt,lastFrame,owner,ownerArea,ownerChannel,distance,player.Position.Y-frame.Y,
                    2*(1+Math.Max(0,player.GetStat(Stat.RunSpeed))/100.0),player.IsMoving);
                if(decision==SyncRefreshDecision.Ignore){report.Rejected++;return;}
                using(var process=Process.GetProcessById(frame.Pid))if(process.HasExited || process.StartTime.ToUniversalTime().Ticks!=frame.StartTicks)return;
                lastFrame=frame.Qpc;
                if(decision==SyncRefreshDecision.Stop){MovementController.Instance.SetMovement(MovementAction.FullStop);report.Stops++;report.Status="Stop received";return;}
                player.Position=new Vector3(frame.X,frame.Y,frame.Z);player.Rotation=new Quaternion(frame.Qx,frame.Qy,frame.Qz,frame.Qw);
                MovementController.Instance.SetMovement(MovementAction.Update);report.Applied++;report.Status="Refreshing";
            }catch(Exception ex){outgoing=null;ClearOwner();report.Status="Unavailable: "+ex.GetType().Name;Trace.TraceWarning("Sync refresh: {0}",ex.Message);}
        }
        internal SyncRefreshReport Capture()
        {
            long now=Stopwatch.GetTimestamp();report.SendAgeMs=lastSent==0?-1:(now-lastSent)*1000.0/Stopwatch.Frequency;
            report.ReceiveAgeMs=lastReceived==0?-1:(now-lastReceived)*1000.0/Stopwatch.Frequency;
            report.Published=Interlocked.Read(ref published);report.IoErrors=Interlocked.Read(ref ioErrors);return report;
        }
        void Exchange(object unused)
        {
            if(Interlocked.Exchange(ref ioBusy,1)!=0)return;
            try {
                if(rate==0)return;
                var frame=outgoing;var path=writePath;
                if(frame!=null && path!=null && frame.Sequence!=written){FormationMailbox.Write(path,frame);written=frame.Sequence;Interlocked.Increment(ref published);}
                path=readPath;if(path!=null)incoming=FormationMailbox.Read<SyncRefreshFrame>(path);
            }catch(Exception ex){Interlocked.Increment(ref ioErrors);Trace.TraceWarning("Sync refresh exchange: {0}",ex.Message);}
            finally{Volatile.Write(ref ioBusy,0);}
        }
        long written;
        void Detach()
        {
            if(channel!=null && observer!=null){
                var callbacks=typeof(IPCChannelBase).GetField("_callbacks",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(channel) as Dictionary<int,List<Action<int,IPCMessage>>>;
                if(callbacks!=null && callbacks.TryGetValue(200,out var list))list.Remove(observer);
            }
            channel=null;settings=null;observer=null;ClearOwner();
        }
        public void Dispose(){disposed=true;outgoing=null;readPath=null;io.Dispose();Network.N3MessageSent-=Sent;Detach();}
    }
}
