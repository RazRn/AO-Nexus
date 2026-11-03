using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using AOSharp.Core;
using AOSharp.Core.Movement;
using AOSharp.Core.UI;
using Path = System.IO.Path;
using AOSharp.Common.GameData;
using NexusPlayTools;

namespace NexusShortcuts
{
    internal sealed class NexusFormation : IDisposable
    {
        readonly string folder;
        readonly int pid = Process.GetCurrentProcess().Id;
        readonly long start = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks;
        readonly Timer io;
        volatile FormationBeacon outgoing, incoming;
        volatile FormationBeacon teamRequest;
        volatile int teamChannel = -1;
        readonly long readyUtc = DateTime.UtcNow.Ticks;
        string teamSession, observedSession;
        long sessionUtc;
        int[] teamMembers;
        string leaderName;
        volatile string ioError;
        int ioBusy;
        internal FormationOptions Options = new FormationOptions();
        internal int Mode; // 0 stopped, 1 leader, 2 follower
        internal string Status = "Stopped";
        SyncUiModel sync;
        bool priorMove, walking, moving;
        double lastProgress, lastSample;
        Vector3 lastPosition;
        int character;
        string beaconPath;
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
        internal NexusFormation(string scope)
        {
            folder = Path.Combine(ToolFiles.Root, "Formations", scope);
            io = new Timer(Exchange, null, 0, 200);
        }
        void Exchange(object unused)
        {
            if (Interlocked.Exchange(ref ioBusy, 1) != 0) return;
            try {
                int channel = teamChannel;
                if (channel >= 0) teamRequest = ToolFiles.Read<FormationBeacon>(Path.Combine(folder, "team-" + channel + ".json"), 8192);
                var state = outgoing;
                if (state == null) return;
                if (state.Leader == character) {
                    ToolFiles.Write(Path.Combine(folder, character + ".json"), state);
                    if (state.Session != null) PublishTeam(state);
                }
                else {
                    if (state.Options != null) ToolFiles.Write(Path.Combine(folder, state.Leader + "-slot-" + state.Options.Slot + ".json"), state);
                    if (state.Active) incoming = ToolFiles.Read<FormationBeacon>(Path.Combine(folder, state.Leader + ".json"), 8192);
                }
                if (!state.Active && ReferenceEquals(outgoing, state)) outgoing = null;
            } catch (Exception ex) { ioError = ex.GetType().Name; }
            finally { Volatile.Write(ref ioBusy, 0); }
        }
        void PublishTeam(FormationBeacon state)
        {
            string path=Path.Combine(folder,"team-"+state.Channel+".json");
            using(var gate=new Mutex(false,"Local\\NexusFormation.Team."+ToolFiles.Hash(path))) {
                bool held=false;
                try {
                    try {held=gate.WaitOne(100);}catch(AbandonedMutexException){held=true;}
                    if(!held)return;
                    var current=ToolFiles.Read<FormationBeacon>(path,8192);
                    if(current.Session!=state.Session && (!state.Active || current.SessionUtc>=state.SessionUtc))return;
                    ToolFiles.Write(path,state);
                }finally{if(held)gate.ReleaseMutex();}
            }
        }
        internal void LeadTeam(FormationShape shape, float spacing, int channel)
        {
            var roster = ClientTeamRoster.Capture();
            var members = roster.Ready && roster.InTeam ? roster.Members.OrderBy(id => id).ToArray() : new int[0];
            if (members.Length < 2 || members.Length > 6) throw new InvalidOperationException("Join a team first. Your local teammates need SyncManager and Nexus Shortcuts on the same channel.");
            var options = new FormationOptions { Shape = shape, Side = spacing, Row = spacing, Behind = spacing };
            options.Validate(); Stop("Updating formation"); Options = options; Start(true);
            teamChannel = channel; teamSession = Guid.NewGuid().ToString("N"); sessionUtc = DateTime.UtcNow.Ticks;
            observedSession = teamSession; teamMembers = members; leaderName = DynelManager.LocalPlayer.Name;
            Status = "Leading: " + leaderName + " - " + shape + ", " + spacing + " m";
        }
        internal void UpdateTeam(int channel)
        {
            if (teamChannel != channel) { if (teamSession != null) Stop("Sync channel changed"); teamChannel = channel; teamRequest = null; }
            if (channel < 0 || Game.IsZoning || DynelManager.LocalPlayer == null) return;
            var roster = ClientTeamRoster.Capture();
            if (!roster.Ready) { if (teamSession != null) Stop("Waiting for fresh team state"); return; }
            var members = roster.InTeam ? roster.Members : new int[0];
            if (teamSession != null && (teamMembers == null || teamMembers.Any(id => !members.Contains(id)))) { Stop("Team changed; enable formation again"); return; }
            var request = teamRequest;
            if (request == null || request.Session == null || request.Session == observedSession) return;
            observedSession = request.Session; // A manual/safety stop cannot restart the same request.
            int slot = request.SlotFor(Game.ClientInst, channel, members, DateTime.UtcNow, readyUtc);
            if (slot == 0 || request.Options == null || !DynelManager.LocalPlayer.IsAlive) return;
            if (request.Playfield != Playfield.Identity.Instance) return;
            using (var process = Process.GetProcessById(request.Pid)) if (process.HasExited || process.StartTime.ToUniversalTime().Ticks != request.StartTicks) return;
            Stop("Joining team formation");
            var o = request.Options;
            Options = new FormationOptions { Shape=o.Shape, Side=o.Side, Row=o.Row, Behind=o.Behind, Leader=request.Leader, Slot=slot, Walk=o.Walk, Hold=o.Hold };
            Start(false); teamSession=request.Session; sessionUtc=request.SessionUtc; teamMembers=request.Members; leaderName=request.LeaderName;
            Status = "Following " + leaderName + " - place " + slot;
        }
        internal void Start(bool lead)
        {
            Options.Validate(); Stop("Stopped");
            if (Game.IsZoning || DynelManager.LocalPlayer == null) throw new InvalidOperationException("Wait until zoning finishes.");
            character = Game.ClientInst;
            if (!lead && (Options.Leader <= 0 || Options.Leader == character)) throw new InvalidOperationException("Choose another character's leader ID.");
            if (!lead) {
                string path = Path.Combine(folder, Options.Leader + "-slot-" + Options.Slot + ".json");
                using (var gate = new Mutex(false, "Local\\NexusFormation." + ToolFiles.Hash(path))) {
                    bool held = false;
                    try {
                        try { held = gate.WaitOne(250); } catch (AbandonedMutexException) { held = true; }
                        if (!held) throw new IOException("Formation is busy.");
                        var existing = ToolFiles.Read<FormationBeacon>(path,8192);
                        if (existing.Fresh(DateTime.UtcNow) && (existing.Pid != pid || existing.StartTicks != start)) throw new InvalidOperationException("This formation slot is already in use.");
                        ToolFiles.Write(path, new FormationBeacon { Active=true, Leader=Options.Leader, Pid=pid, StartTicks=start, UtcTicks=DateTime.UtcNow.Ticks });
                    } finally { if (held) gate.ReleaseMutex(); }
                }
            }
            // Reuse the same reviewed SyncManager adapter as the settings window.
            var instance = Game.OnUpdate?.GetInvocationList().Select(d => d.Target).FirstOrDefault(o => o != null && o.GetType().FullName == "SyncManager.SyncManager");
            if (instance != null) { sync = new SyncUiModel(instance); priorMove = (sync.Values & 4) != 0; sync.Set(2, false); }
            Mode = lead ? 1 : 2;
            incoming = null; ioError = null; lastProgress = Time.NormalTime; lastSample = 0;
            lastPosition = DynelManager.LocalPlayer.Position;
            beaconPath = Path.Combine(folder, character + ".json");
            Status = lead ? "Leading - ID " + character : "Waiting for leader " + Options.Leader;
        }
        internal void Stop(string reason)
        {
            int previous = Mode; Mode = 0;
            var old = outgoing;
            outgoing = old == null ? null : new FormationBeacon { Leader = old.Leader, Pid = pid, StartTicks = start, Options = old.Options, Session=old.Session, SessionUtc=old.SessionUtc, Channel=old.Channel, Active = false, UtcTicks = DateTime.UtcNow.Ticks };
            teamSession = null; teamMembers = null;
            incoming = null;
            if (moving) { MovementController.Instance.SetMovement(MovementAction.ForwardStop); moving = false; }
            if (sync != null) { try { sync.Set(2, priorMove); } catch { } sync = null; }
            Status = reason;
            if (previous != 0) Chat.WriteLine("Formation: " + reason);
        }
        internal void Update()
        {
            if (Mode == 0) return;
            try {
                if (Game.IsZoning || DynelManager.LocalPlayer == null || !DynelManager.LocalPlayer.IsAlive) { Stop("Paused after zoning/death; start again when ready"); return; }
                var player = DynelManager.LocalPlayer;
                if (ioError != null) { Stop("Local formation link failed: " + ioError); return; }
                if (sync != null && (sync.Values & 4) != 0) { Stop("Movement sync was enabled"); return; }
                if (Mode == 2 && ManualInput()) { Stop("Manual movement took control"); return; }
                if (Time.NormalTime - lastSample < .15) return;
                lastSample = Time.NormalTime;
                var forward = player.Forward;
                outgoing = new FormationBeacon { Active = true, Leader = Mode == 1 ? character : Options.Leader, Pid = pid, StartTicks = start,
                    Session=teamSession, SessionUtc=sessionUtc, Channel=teamChannel, Members=teamMembers, LeaderName=leaderName,
                    UtcTicks = DateTime.UtcNow.Ticks, Playfield = Playfield.Identity.Instance, X = player.Position.X, Y = player.Position.Y, Z = player.Position.Z,
                    ForwardX = forward.X, ForwardZ = forward.Z, Options = new FormationOptions { Slot = Options.Slot, Shape = Options.Shape, Side = Options.Side, Row = Options.Row, Behind = Options.Behind, Walk = Options.Walk, Hold = Options.Hold } };
                if (Mode == 1) return;
                var leader = incoming;
                if (leader == null) { if (Time.NormalTime - lastProgress > 3) Stop("Leader unavailable"); return; }
                if (!leader.Fresh(DateTime.UtcNow) || leader.Leader != Options.Leader || leader.Playfield != Playfield.Identity.Instance) { Stop("Leader stale or in another playfield"); return; }
                using (var process = Process.GetProcessById(leader.Pid)) if (process.HasExited || process.StartTime.ToUniversalTime().Ticks != leader.StartTicks) { Stop("Leader client closed"); return; }
                var config = leader.Options;
                if (config == null) { Stop("Leader settings unavailable"); return; }
                config.Slot = Options.Slot; config.Validate();
                var point = FormationGeometry.Target(config, leader.X, leader.Z, leader.ForwardX, leader.ForwardZ);
                var target = new Vector3(point.X, leader.Y, point.Z);
                float distance = player.Position.Distance2DFrom(target);
                if (distance > 60 || Math.Abs(player.Position.Y - target.Y) > 5) { Stop("Formation too far away or on another level"); return; }
                if (MovementController.Instance.IsNavigating) { Stop("Another movement plugin took control"); return; }
                if (player.IsAttacking || Spell.HasPendingCast) { if (moving) { MovementController.Instance.SetMovement(MovementAction.ForwardStop); moving = false; } Status = "Paused for combat / nano"; lastProgress = Time.NormalTime; return; }
                if (distance <= (moving ? .65f : 1.1f)) {
                    if (moving) { MovementController.Instance.SetMovement(MovementAction.ForwardStop); moving = false; }
                    Status = "In position - slot " + Options.Slot;
                    lastProgress = Time.NormalTime;
                    if (config.Hold) Stop("Position reached - holding");
                    return;
                }
                if (!Playfield.LineOfSight(player.Position + new Vector3(0, 1, 0), target + new Vector3(0, 1, 0))) { Stop("Path obstructed; reposition or use column"); return; }
                if (Time.NormalTime - lastProgress > 3) {
                    if (lastPosition.Distance2DFrom(player.Position) < .5) { Stop("No movement progress"); return; }
                    lastPosition = player.Position; lastProgress = Time.NormalTime;
                }
                if (!moving || walking != config.Walk) { MovementController.Instance.SetMovement(config.Walk ? MovementAction.SwitchToWalk : MovementAction.SwitchToRun); walking = config.Walk; }
                MovementController.Instance.LookAt(target);
                if (!moving) MovementController.Instance.SetMovement(MovementAction.ForwardStart);
                MovementController.Instance.SetMovement(MovementAction.Update); moving = true;
                Status = "Following slot " + Options.Slot + " - " + distance.ToString("0.0") + " m to position";
            } catch (Exception ex) { Stop("Stopped: " + ex.GetType().Name); }
        }
        bool ManualInput()
        {
            uint foreground; GetWindowThreadProcessId(GetForegroundWindow(), out foreground);
            return foreground == pid && new[] { 0x1b, 0x20, 0x25, 0x26, 0x27, 0x28, 0x57, 0x41, 0x53, 0x44, 0x51, 0x45, 0x52, 0x02 }.Any(k => (GetAsyncKeyState(k) & 0x8000) != 0);
        }
        public void Dispose()
        {
            Stop("Unloaded");
            using (var done = new ManualResetEvent(false)) { if (io.Dispose(done)) done.WaitOne(1000); }
            if (beaconPath != null && File.Exists(beaconPath)) {
                try { var saved = ToolFiles.Read<FormationBeacon>(beaconPath); if (saved.Pid == pid && saved.StartTicks == start) ToolFiles.Write(beaconPath, new FormationBeacon()); } catch { }
            }
        }
    }
}
