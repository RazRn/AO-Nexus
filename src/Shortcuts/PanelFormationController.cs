using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;
using AOSharp.Common.GameData;
using AOSharp.Common.Unmanaged.Imports;
using AOSharp.Core;
using AOSharp.Core.Movement;
using NexusPlayTools;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;
using Path = System.IO.Path;

namespace NexusShortcuts
{
    internal sealed class PanelFormationBeacon
    {
        public string Session;
        public long UtcTicks;
        public int Pid, Leader, Area;
        public long StartTicks;
        public bool Active;
        public float ForwardX, ForwardZ;
        public int Pace;
        public TrailPoint Position;
        public TrailPoint[] Trail;
    }
    internal sealed class PanelFormationController : IDisposable
    {
        readonly string scope, folder;
        readonly int pid=Process.GetCurrentProcess().Id;
        readonly long start=Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks;
        readonly ConcurrentQueue<FormationRequest> requests=new ConcurrentQueue<FormationRequest>();
        readonly Timer io;
        readonly FormationTrail trail=new FormationTrail();
        FormationRequest active;
        FormationMovementLease lease;
        volatile PanelFormationBeacon outgoing, incoming;
        volatile string file, ioError, report="WAIT";
        long renewed;
        double nextUpdate, nextReport, lastProgress;
        TrailPoint progressPoint;
        readonly FormationTravel funnyTravel=new FormationTravel();
        bool moving, disposed;
        int ioBusy;
        string status="Stopped";
        readonly Random random=new Random(Guid.NewGuid().GetHashCode());
        readonly System.Collections.Generic.Queue<string> emoteBag=new System.Collections.Generic.Queue<string>();
        readonly System.Collections.Generic.Dictionary<string,int> emoteActions=new System.Collections.Generic.Dictionary<string,int>();
        TrailPoint funnyOffset;
        double nextFunnyTarget,nextEmote;
        int emotesPerformed,leaderPace;
        string lastEmote;
        [DllImport("Gamecode.dll",EntryPoint="?N3Msg_DoSocialAction@n3EngineClientAnarchy_t@@QAEXW4AbstractAnimID_e@@@Z",CallingConvention=CallingConvention.ThisCall)]
        static extern void DoSocialAction(IntPtr engine,int action);
        internal Action BeforeStart;
        internal Action<int> SetSyncRate;
        internal Func<SyncRefreshReport> ReadSyncRefresh;
        internal string Report=>report;
        internal bool Active=>active!=null;
        static double Now=>Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
        internal static int[] CurrentTeam()
        {
            var state = ClientTeamRoster.Capture();
            return state.Ready && state.InTeam ? state.Members : new int[0];
        }
        internal PanelFormationController(string scope)
        {
            this.scope=scope;folder=Path.Combine(ToolFiles.Root,"Formations",scope);
            io=new Timer(Exchange,null,0,200);
        }
        internal string Queue(string json)
        {
            try {
                var request=new JavaScriptSerializer {MaxJsonLength=2048}.Deserialize<FormationRequest>(json);
                if(request==null || (request.Action=="sync-rate" ? !SyncRefreshPolicy.ValidRate(request.Rate)
                    : request.Action!="stop" && request.Action!="keep" && !request.Valid()))return "ERROR invalid formation request";
                if(requests.Count>=16)return "BUSY";
                requests.Enqueue(request);return "OK";
            } catch(Exception ex) {return "ERROR "+ex.GetType().Name;}
        }
        void Exchange(object unused)
        {
            if(Interlocked.Exchange(ref ioBusy,1)!=0)return;
            try {
                var path=file;var state=outgoing;if(path==null || state==null)return;
                if(state.Leader==GameCharacter)FormationMailbox.Write(path,state);
                else incoming=FormationMailbox.Read<PanelFormationBeacon>(path);
            } catch(IOException ex) {
                // A transient local sharing/replace failure does not invalidate a
                // fresh snapshot. The game-thread two-second freshness check still
                // halts movement, and its three-second deadline ends the session.
                Trace.TraceWarning("Formation snapshot exchange: {0}",ex.Message);
            } catch(Exception ex){ioError=ex.GetType().Name;}
            finally{Volatile.Write(ref ioBusy,0);}
        }
        int GameCharacter; // Snapshot only: the I/O timer never calls the game API.
        void Start(FormationRequest request)
        {
            if(!request.Valid() || Game.IsZoning || DynelManager.LocalPlayer==null || !DynelManager.LocalPlayer.IsAlive)
                throw new InvalidOperationException("Wait for a live character after zoning.");
            var members=CurrentTeam();
            if(!members.SequenceEqual(request.Members.OrderBy(x=>x)) || !members.Contains(Game.ClientInst))
                throw new InvalidOperationException("Waiting for a fresh matching team roster.");
            if(request.Channel!=FormationMovementLease.CurrentChannel() || request.Playfield!=Playfield.Identity.Instance)
                throw new InvalidOperationException("Team members must share an area and SyncManager channel.");
            if((request.Slot==0)!=(request.Leader==Game.ClientInst))throw new InvalidOperationException("Invalid formation slot.");
            if(MovementController.Instance.IsNavigating)throw new InvalidOperationException("Stop the other movement controller first.");
            if(request.Action=="funny") {
                emoteActions.Clear();var engine=N3Engine_t.GetInstance();
                if(engine==IntPtr.Zero)throw new InvalidOperationException("Emote engine unavailable.");
                // Menu/chat aliases are not names understood by GetActionByName.
                // AO# defines the protocol's social-animation IDs explicitly.
                var actions=Enum.GetValues(typeof(SocialAction)).Cast<SocialAction>().ToArray();
                foreach(var name in FunnyBusiness.Emotes) {
                    var matches=actions.Where(a=>FunnyBusiness.EmoteAlias(a.ToString())==name.Replace("-","")).ToArray();
                    if(matches.Length!=1)throw new InvalidOperationException("Unknown client emote: "+name);
                    emoteActions.Add(name,(int)matches[0]);
                }
                if(emoteActions.Count==0)throw new InvalidOperationException("Client emotes unavailable.");
            }
            Stop("Updating formation");BeforeStart?.Invoke();
            lease=new FormationMovementLease();active=request;GameCharacter=Game.ClientInst;
            trail.Points.Clear();incoming=null;ioError=null;
            file=Path.Combine(folder,"panel-"+request.Session+".json");
            renewed=Stopwatch.GetTimestamp();lastProgress=Now;
            progressPoint=Position();
            funnyTravel.Reset(progressPoint);
            funnyOffset=null;nextFunnyTarget=0;nextEmote=Now+.5+random.NextDouble()*2;
            emotesPerformed=0;lastEmote=null;leaderPace=0;emoteBag.Clear();
            if(request.Slot!=0)MovementController.Instance.SetMovement(MovementAction.FullStop);
            status=request.Slot==0?"Leading "+request.Action:"Waiting for leader";
        }
        TrailPoint Position() {var p=DynelManager.LocalPlayer.Position;return new TrailPoint(p.X,p.Y,p.Z);}
        internal void Stop(string reason)
        {
            var old=active;active=null;file=null;outgoing=null;incoming=null;
            if(old!=null && old.Slot!=0)MovementController.Instance.SetMovement(MovementAction.FullStop);
            moving=false;
            try{lease?.Dispose();}catch(Exception ex){reason+="; movement restore failed: "+ex.Message;Trace.TraceWarning(reason);}
            lease=null;status=reason;
        }
        internal void Update()
        {
            if(disposed)return;
            while(requests.TryDequeue(out var request)) {
                try {
                    if(request.Action=="sync-rate")SetSyncRate?.Invoke(request.Rate);
                    else if(request.Action=="stop")Stop("Stopped");
                    else if(request.Action=="keep") {if(active?.Session==request.Session)renewed=Stopwatch.GetTimestamp();}
                    else Start(request);
                }catch(Exception ex){Stop(ex.Message);}
                nextReport=0;
            }
            double now=Now;
            if(active!=null && now>=nextUpdate) {
                nextUpdate=now+.15;
                try {Step(now);}catch(Exception ex){Stop(ex.Message);}
            }
            if(now<nextReport)return;nextReport=now+.5;
            var state=new FormationReport {Pid=pid,StartTicks=start,UtcTicks=DateTime.UtcNow.Ticks,Scope=scope,
                Active=active!=null,Session=active?.Session,Status=status,SyncRefresh=ReadSyncRefresh?.Invoke(),CircleSupported=true,
                FunnySupported=true,PaceSupported=true,EmotesPerformed=emotesPerformed,EmotesAvailable=emoteActions.Count,LastEmote=lastEmote,LeaderPace=leaderPace};
            try {
                state.Members=CurrentTeam();state.Character=Game.ClientInst;
                state.Channel=FormationMovementLease.CurrentChannel();
                state.Playfield=Game.IsZoning?0:Playfield.Identity.Instance;
                state.Ready=!Game.IsZoning && DynelManager.LocalPlayer?.IsAlive==true && state.Members.Length>=2;
                state.ActualPace=DynelManager.LocalPlayer==null?0:(int)DynelManager.LocalPlayer.MovementState;
            }catch(Exception ex){if(active==null && status=="Stopped")state.Status=ex.Message;}
            report="OK "+new JavaScriptSerializer().Serialize(state);
        }
        void Step(double now)
        {
            if((Stopwatch.GetTimestamp()-renewed)/(double)Stopwatch.Frequency>5) {Stop("Nexus connection lost — stopped");return;}
            if(Game.IsZoning || DynelManager.LocalPlayer==null || !DynelManager.LocalPlayer.IsAlive
                || Playfield.Identity.Instance!=active.Playfield) {Stop("Paused after zoning/death — press Start when ready");return;}
            if(!CurrentTeam().SequenceEqual(active.Members.OrderBy(x=>x))) {Stop("Team changed or unavailable — stopped");return;}
            if(lease==null || !lease.Intact || FormationMovementLease.CurrentChannel()!=active.Channel) {Stop("SyncManager changed — stopped");return;}
            if(ioError!=null) {Stop("Formation link failed: "+ioError);return;}
            var player=DynelManager.LocalPlayer;var position=Position();
            if(active.Action=="funny")funnyTravel.Observe(position);
            if(active.Slot==0) {
                trail.Add(position);var forward=player.Forward;
                outgoing=new PanelFormationBeacon {Active=true,Session=active.Session,Leader=GameCharacter,Pid=pid,StartTicks=start,
                    UtcTicks=DateTime.UtcNow.Ticks,Area=active.Playfield,Position=position,ForwardX=forward.X,ForwardZ=forward.Z,
                    Pace=(int)player.MovementState,Trail=trail.Points.ToArray()};
                return;
            }
            if(ManualInput()){Stop("Manual movement took control");return;}
            outgoing=new PanelFormationBeacon {Leader=active.Leader};
            var leader=incoming;
            if(leader==null || !leader.Active || leader.Session!=active.Session || leader.Leader!=active.Leader
                || leader.Area!=active.Playfield || leader.Position==null || leader.UtcTicks>DateTime.UtcNow.Ticks
                || DateTime.UtcNow.Ticks-leader.UtcTicks>TimeSpan.TicksPerSecond*2) {
                Halt();status="Waiting for fresh leader state";
                if(now-lastProgress>3)Stop("Leader unavailable — stopped");return;
            }
            using(var process=Process.GetProcessById(leader.Pid))if(process.HasExited || process.StartTime.ToUniversalTime().Ticks!=leader.StartTicks){Stop("Leader client closed");return;}
            if(position.Distance(leader.Position)>60 || Math.Abs(position.Y-leader.Position.Y)>5){Stop("Cannot reach leader — too far or another level");return;}
            if(MovementController.Instance.IsNavigating){Stop("Another movement controller took control");return;}
            if(player.IsAttacking || Spell.HasPendingCast){Halt();status="Paused for combat / nano";lastProgress=now;return;}
            leaderPace=leader.Pace;
            if(!FunnyBusiness.GroundPace(leaderPace)){Halt();status="Waiting for leader to walk or run";lastProgress=now;return;}
            if(!FunnyBusiness.GroundPace((int)player.MovementState)){Halt();status="Waiting to walk or run";lastProgress=now;return;}
            if((int)player.MovementState!=leaderPace)
                MovementController.Instance.SetMovement(leaderPace==2?MovementAction.SwitchToWalk:MovementAction.SwitchToRun);
            if(active.Action=="funny") {
                if(now>=nextFunnyTarget) {
                    funnyOffset=FunnyBusiness.Offset(random,active.Spacing);
                    nextFunnyTarget=now+1+random.NextDouble()*3;
                }
                // Independent of arriving or finishing an animation. Never animate the leader.
                if(now>=nextEmote && position.Distance(leader.Position)<=active.Spacing) {
                    PlayRandomEmote();nextEmote=now+.8+random.NextDouble()*3.2;
                }
                if(funnyOffset==null){Halt();status="Waiting for a clear direction";return;}
            }
            var options=new FormationOptions {Shape=FormationShape.Column,Slot=active.Slot,Behind=active.Spacing,Row=active.Spacing};
            var offset=FormationGeometry.Target(options,leader.Position.X,leader.Position.Z,leader.ForwardX,leader.ForwardZ);
            var fallback=new TrailPoint(offset.X,leader.Position.Y,offset.Z);
            var destination=active.Action=="gather"?leader.Position:
                active.Action=="circle"?CircleFormation.Target(leader.Position,active.Spacing,active.Slot,active.Members.Length-1):
                active.Action=="funny"?new TrailPoint(leader.Position.X+funnyOffset.X,leader.Position.Y,leader.Position.Z+funnyOffset.Z):
                FormationTrail.Behind(leader.Trail,active.Spacing*active.Slot,fallback);
            double distance=position.Distance(destination);
            double tolerance=active.Action=="gather"?active.Spacing:active.Action=="circle" || active.Action=="funny"?(moving?.45:.65):(moving?.65:1.1);
            if(distance<=tolerance) {
                Halt();status=active.Action=="gather"?"Gathered":active.Action=="funny"?"Funny business":"In position";lastProgress=now;progressPoint=position;funnyTravel.Reset(position);return;
            }
            var target=destination;
            if(active.Action=="follow" && leader.Trail?.Length>2) {
                int closest=Enumerable.Range(0,leader.Trail.Length).OrderBy(i=>position.Distance(leader.Trail[i])).First();
                int finish=Enumerable.Range(0,leader.Trail.Length).OrderBy(i=>destination.Distance(leader.Trail[i])).First();
                if(closest<finish && position.Distance(leader.Trail[closest])<3)target=leader.Trail[closest+1];
            }
            var point=new Vector3(target.X,target.Y,target.Z);
            if(!Playfield.LineOfSight(player.Position+new Vector3(0,1,0),point+new Vector3(0,1,0))){
                if(active.Action=="funny")TurnAround(position,leader.Position,destination,now);
                else Stop("Cannot reach leader — path obstructed");
                return;
            }
            if(now-lastProgress>3) {
                if((active.Action=="funny"?funnyTravel.Distance:progressPoint.Distance(position))<.5){
                    if(active.Action=="funny")TurnAround(position,leader.Position,destination,now);
                    else Stop("Cannot reach leader — no movement progress");
                    return;
                }
                lastProgress=now;progressPoint=position;funnyTravel.Reset(position);
            }
            MovementController.Instance.LookAt(point);
            if(!moving || !player.IsMoving)MovementController.Instance.SetMovement(MovementAction.ForwardStart);
            MovementController.Instance.SetMovement(MovementAction.Update);moving=true;
            status=(active.Action=="gather"?"Gathering":active.Action=="circle"?"Forming circle":active.Action=="funny"?"Funny business":"Following")+" · "+distance.ToString("0.0")+" m";
        }
        void PlayRandomEmote()
        {
            if(emoteBag.Count==0) {
                var names=emoteActions.Keys.ToArray();
                for(int i=names.Length-1;i>0;i--){int j=random.Next(i+1);var swap=names[i];names[i]=names[j];names[j]=swap;}
                foreach(var name in names)emoteBag.Enqueue(name);
            }
            string selected=emoteBag.Dequeue();var engine=N3Engine_t.GetInstance();
            if(engine==IntPtr.Zero)throw new InvalidOperationException("Emote engine unavailable.");
            DoSocialAction(engine,emoteActions[selected]);lastEmote=selected;emotesPerformed++;
        }
        void TurnAround(TrailPoint position,TrailPoint centre,TrailPoint blocked,double now)
        {
            Halt();
            var target=FunnyBusiness.TurnAway(random,centre,position,blocked,active.Spacing,p=>
                Playfield.LineOfSight(new Vector3(position.X,position.Y+1,position.Z),new Vector3(p.X,p.Y+1,p.Z)));
            funnyOffset=target==null?null:new TrailPoint(target.X-centre.X,0,target.Z-centre.Z);
            nextFunnyTarget=now+(target==null?.75:1+random.NextDouble()*3);
            lastProgress=now;progressPoint=position;funnyTravel.Reset(position);
            status=target==null?"Waiting for a clear direction":"Turning away from obstacle";
        }
        void Halt(){if(moving)MovementController.Instance.SetMovement(MovementAction.FullStop);moving=false;}
        bool ManualInput()
        {
            GetWindowThreadProcessId(GetForegroundWindow(),out uint foreground);
            return foreground==pid && new[]{0x1b,0x20,0x25,0x26,0x27,0x28,0x57,0x41,0x53,0x44,0x51,0x45,0x52,0x02}.Any(k=>(GetAsyncKeyState(k)&0x8000)!=0);
        }
        public void Dispose(){disposed=true;Stop("Unloaded");io.Dispose();}
    }
}
