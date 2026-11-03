using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using AOSharp.Core;
using AOSharp.Core.UI;
using AOSharp.Common.SharedEventArgs;
using SmokeLounge.AOtomation.Messaging.Messages;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;
using SmokeLounge.AOtomation.Messaging.GameData;
using System.Reflection;

[assembly: AssemblyTitle("Nexus Health Monitor")]
[assembly: AssemblyProduct("Nexus Health Monitor")]
[assembly: AssemblyDescription("AO Nexus update-loop and IPC measurements")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.3.0.0")]

namespace NexusTelemetry
{
    // Monitoring plus a conservative sync run-off guard on the game thread.
    public sealed class NexusHealthPlugin : AOPluginEntry
    {
        readonly object gate = new object();
        readonly SyncMovementGuard movementGuard=new SyncMovementGuard();
        RallyBeacon rally;
        readonly long[] times = new long[2048];
        readonly double[] gaps = new double[2048];
        readonly Pulse pulse = new Pulse { Instance = Guid.NewGuid().ToString("N") };
        Timer writer;
        LocalHealthChannel channel;
        string path, regroupPath;
        int cursor;
        bool stopped;
        int writing;
        long lastNameLookupQpc;
        readonly NativeCombatMeter meter=new NativeCombatMeter();
        readonly NearbyProfessions professions=new NearbyProfessions();
        readonly System.Collections.Generic.Dictionary<int,string> classification=new System.Collections.Generic.Dictionary<int,string>();
        long lastMeterQpc;
        long lastAggroErrorQpc;
        long lastMoveSentQpc;
        ActionIssueTracker actionIssues;

        public override void Run()
        {
            rally=new RallyBeacon();movementGuard.Stopped=()=>rally.Send(true);
            movementGuard.RegroupBlocked=reason=>rally.Send(reason);
            using (var process = Process.GetCurrentProcess()) {
                pulse.Pid = process.Id; pulse.StartTicks = process.StartTime.ToUniversalTime().Ticks;
            }
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AONexus", "Telemetry");
            Directory.CreateDirectory(root);
            path = Path.Combine(root, pulse.Pid + "-" + pulse.StartTicks + ".json");
            regroupPath = Path.Combine(Path.GetDirectoryName(root), "Regroup", pulse.Pid + "-" + pulse.StartTicks + ".json");
            pulse.Active = true;
            actionIssues = new ActionIssueTracker(pulse);
            channel = new LocalHealthChannel(pulse.Pid, pulse.StartTicks, true);
            Game.OnUpdate += ReplyToProbe;
            Game.OnUpdate += Update;
            MiscClientEvents.AttemptingSpellCast += AttemptingSpellCast;
            Network.N3MessageSent += MessageSent;
            Network.N3MessageReceived += MessageReceived;
            Chat.FeedbackReceived += FeedbackReceived;
            writer = new Timer(Publish, null, 0, 500);
        }
        void AttemptingSpellCast(object sender, AttemptingSpellCastEventArgs args) { ObserveCast(args.Nano.Instance); }
        void MessageSent(object sender, N3Message message)
        {
            if(message is CharDCMoveMessage)lastMoveSentQpc=Stopwatch.GetTimestamp();
            if (message is CharacterActionMessage action && action.Action == CharacterActionType.CastNano) ObserveCast(action.Parameter2);
        }
        void ObserveCast(int nanoId)
        {
            try {
                if (stopped || Game.IsZoning) return;
                var player = DynelManager.LocalPlayer;
                if (player == null) return;
                bool falling = player.IsFalling;
                lock (gate) { if (!stopped) {
                    long now=Stopwatch.GetTimestamp(); actionIssues.ObserveCast(falling, false, now);
                    if(pulse.CastNanoId!=nanoId) pulse.CastNanoName=null;
                    pulse.CastAttemptQpc=now; pulse.CastNanoId=nanoId;
                } }
            } catch { /* Monitoring must never interfere with casting. */ }
        }
        void FeedbackReceived(string text)
        {
            try { lock (gate) { if (!stopped) { actionIssues.ObserveFeedback(text, Stopwatch.GetTimestamp()); meter.Feedback(text); } } }
            catch { /* Ignore unavailable diagnostic evidence. */ }
        }
        void MessageReceived(object sender,N3Message message) {
            try { lock(gate) {
                if(stopped || Game.IsZoning || DynelManager.LocalPlayer==null)return;
                var player=DynelManager.LocalPlayer;
                meter.Begin(player.Identity.Instance,Stopwatch.GetTimestamp());
                Func<AOSharp.Common.GameData.Identity,int> ownership=identity=> {
                    if(identity==player.Identity)return 1;
                    if(identity.Type!=AOSharp.Common.GameData.IdentityType.SimpleChar || identity.Instance<=0)return 0;
                    // The local pet window supplies ownership even when a freshly
                    // summoned pet has no dynel, Pet flag or owner stat yet (Rebirth).
                    // Use the current roster, never retain ownership across zoning,
                    // charm release or replacement. A roster read failure can still
                    // fall back to explicit owner metadata below.
                    try { if(player.Pets.Any(pet=>pet!=null && pet.Identity==identity))return 2; } catch { }
                    if(DynelManager.Find(identity,out SimpleChar character) && !character.IsPlayer
                        && character.GetStat(AOSharp.Common.GameData.Stat.ConflictReputation)==player.Identity.Instance)return 2;
                    return 0;
                };
                meter.Observe(message,ownership,identity=> {
                    int localOwner=ownership(identity);
                    if(localOwner!=0) {
                        var result=ObservedPlayer(player,localOwner==2);
                        if(localOwner==2 && DynelManager.Find(identity,out SimpleChar ownPet))result.PetName=ownPet.Name;
                        return result;
                    }
                    if(identity.Type!=AOSharp.Common.GameData.IdentityType.SimpleChar || identity.Instance<=0
                        || !DynelManager.Find(identity,out SimpleChar source))return null;
                    if(source.IsPlayer)return ObservedPlayer(source,false);
                    // Rebirth starter pets can omit CharacterFlags.Pet. Ownership
                    // and the client pet type/family are independent evidence.
                    int owner=source.GetStat(AOSharp.Common.GameData.Stat.ConflictReputation);
                    bool pet=IsPetCreature(source);
                    if(owner>0 && owner!=source.Identity.Instance
                        && DynelManager.Find(new AOSharp.Common.GameData.Identity(AOSharp.Common.GameData.IdentityType.SimpleChar,owner),out SimpleChar master)
                        && (master.IsPlayer || pet))
                    {
                        var result=master.IsPlayer?ObservedPlayer(master,true):new NativeCombatMeter.SourceActor {CharacterId=master.Identity.Instance,Name=master.Name,Npc=true,Pet=true};
                        result.PetName=source.Name;return result;
                    }
                    if(pet) {
                        return new NativeCombatMeter.SourceActor {CharacterId=source.Identity.Instance,Name=source.Name,Pet=true,UnassignedPet=true};
                    }
                    return new NativeCombatMeter.SourceActor {CharacterId=source.Identity.Instance,Name=source.Name,Npc=true};
                });
            } } catch { /* Meter failures must never interrupt game events. */ }
        }
        bool IsPetCreature(SimpleChar character)
        {
            int flags=character.GetStat(AOSharp.Common.GameData.Stat.Flags);
            var type=(AOSharp.Common.GameData.PetType)character.GetStat(AOSharp.Common.GameData.Stat.PetType);
            int family=character.GetStat(AOSharp.Common.GameData.Stat.NPCFamily);
            if(classification.Count<64 || classification.ContainsKey(character.Identity.Instance))
                classification[character.Identity.Instance]=character.Identity.Instance+": flags="+flags+", owner="
                    +character.GetStat(AOSharp.Common.GameData.Stat.ConflictReputation)+", type="+(int)type+", family="+family;
            // -1 means unavailable; treating it as a flags bitmask would mark
            // every unknown creature as a pet.
            if(flags!=-1 && (flags&(int)AOSharp.Common.GameData.CharacterFlags.Pet)!=0)return true;
            if(type==AOSharp.Common.GameData.PetType.Attack || type==AOSharp.Common.GameData.PetType.Heal
                || type==AOSharp.Common.GameData.PetType.Support || type==AOSharp.Common.GameData.PetType.Social)return true;
            return family==(int)AOSharp.Common.GameData.NpcClan.EngineerAttackPet
                || family==(int)AOSharp.Common.GameData.NpcClan.MPAttackPets
                || family==(int)AOSharp.Common.GameData.NpcClan.MPHealPets
                || family==(int)AOSharp.Common.GameData.NpcClan.MPMezzPets
                || family==(int)AOSharp.Common.GameData.NpcFamily.AttackPet
                || family==(int)AOSharp.Common.GameData.NpcFamily.HealPet
                || family==(int)AOSharp.Common.GameData.NpcFamily.SupportPet;
        }
        NativeCombatMeter.SourceActor ObservedPlayer(SimpleChar player,bool pet)=>new NativeCombatMeter.SourceActor {
            CharacterId=player.Identity.Instance,Name=player.Name,
            Profession=professions.Remember(player.Identity.Instance,player.Profession.ToString()),Pet=pet};
        // Current visible combat targets, not a server threat table. Read only on
        // the game thread, at the existing half-second telemetry cadence.
        internal Tuple<int,string[]> ReadAggro(LocalPlayer player)
        {
            var ownPets=new System.Collections.Generic.HashSet<int>(player.Pets.Where(p=>p!=null).Select(p=>p.Identity.Instance));
            var attackers=new System.Collections.Generic.Dictionary<int,string>();
            foreach(var mob in DynelManager.Characters) {
                if(mob==null || mob.IsPlayer || !mob.IsAlive || !mob.IsAttacking
                    || mob.FightingTarget?.Identity!=player.Identity || ownPets.Contains(mob.Identity.Instance))continue;
                if(IsPetCreature(mob))continue;
                int owner=mob.GetStat(AOSharp.Common.GameData.Stat.ConflictReputation);
                if(owner>0 && owner!=mob.Identity.Instance
                    && DynelManager.Find(new AOSharp.Common.GameData.Identity(AOSharp.Common.GameData.IdentityType.SimpleChar,owner),out SimpleChar master)
                    && master.IsPlayer)continue;
                string name=new string((mob.Name??"").Where(c=>!char.IsControl(c)).Take(96).ToArray()).Trim();
                attackers[mob.Identity.Instance]=name;
            }
            return Tuple.Create(attackers.Count,attackers.OrderBy(p=>p.Value,StringComparer.OrdinalIgnoreCase)
                .ThenBy(p=>p.Key).Take(3).Select(p=>p.Value).ToArray());
        }
        void ResolveProfessions(long now)
        {
            try {
                var local=DynelManager.LocalPlayer;
                if(local==null)return;
                professions.Poll(now,id=> {
                    var identity=new AOSharp.Common.GameData.Identity(AOSharp.Common.GameData.IdentityType.SimpleChar,id);
                    return DynelManager.Find(identity,out SimpleChar player) && player.IsPlayer
                        ? player.Profession.ToString() : null;
                },id=>Network.Send(new CharacterActionMessage {
                    Action=CharacterActionType.InfoRequest,Identity=local.Identity,
                    Target=new AOSharp.Common.GameData.Identity(AOSharp.Common.GameData.IdentityType.SimpleChar,id)
                }),meter.SetObservedProfession);
            } catch(Exception ex) {
                Trace.TraceWarning("Nearby profession lookup failed: {0}",ex.Message);
            }
        }
        void Update(object sender, float delta)
        {
            lock (gate) {
                if (stopped) return;
                long now = Stopwatch.GetTimestamp();
                movementGuard.Update();
                rally?.Update();
                try { movementGuard.SendStoppedBeacon(); } catch(Exception ex) { Trace.TraceWarning("Rally message failed: {0}",ex.Message); }
                if (pulse.LastUpdateQpc != 0) {
                    double ms = (now - pulse.LastUpdateQpc) * 1000.0 / Stopwatch.Frequency;
                    pulse.MaxMs = Math.Max(pulse.MaxMs, ms);
                    if (ms > 100) pulse.Over100++;
                    times[cursor] = now; gaps[cursor] = ms; cursor = (cursor + 1) % times.Length;
                }
                if(Game.IsZoning && !pulse.Zoning){meter.ResetObserved(now);classification.Clear();professions.Clear();}
                pulse.LastUpdateQpc = now; pulse.Updates++; pulse.Zoning = Game.IsZoning;
                if(!pulse.Zoning && now-lastMeterQpc>=Stopwatch.Frequency/2) {
                    if(DynelManager.LocalPlayer!=null)meter.Begin(DynelManager.LocalPlayer.Identity.Instance,now);
                    try {
                        pulse.PlayfieldId=Playfield.Identity.Instance;
                        meter.SetLocalPets(DynelManager.LocalPlayer.Pets.Where(p=>p!=null).Select(p=>new CombatMeterPet {
                            CharacterId=p.Identity.Instance,Name=p.Character?.Name}).ToArray());
                    } catch { meter.SetLocalPets(new CombatMeterPet[0]);pulse.PlayfieldId=0; }
                    ResolveProfessions(now);
                    try {
                        var player=DynelManager.LocalPlayer;
                        if(player==null)pulse.AggroQpc=0;
                        else {
                            var aggro=ReadAggro(player);
                            pulse.AggroCount=aggro.Item1;pulse.AggroNames=aggro.Item2;pulse.AggroQpc=now;
                        }
                    } catch(Exception ex) {
                        pulse.AggroQpc=0;
                        if(now-lastAggroErrorQpc>30*Stopwatch.Frequency) {
                            lastAggroErrorQpc=now;Trace.TraceWarning("Aggro observation failed: {0}",ex.Message);
                        }
                    }
                    try {
                        if(DynelManager.LocalPlayer!=null) {
                            pulse.Movement=SyncMovementProbe.Read(DynelManager.LocalPlayer,now);
                            pulse.Movement.LastMoveSentQpc=lastMoveSentQpc;
                            movementGuard.Capture(pulse.Movement);
                        }
                    } catch(Exception ex) {pulse.Movement=new MovementSnapshot {Qpc=now,Adapter="Unavailable",Error=ex.GetType().Name};}
                    lastMeterQpc=now;pulse.CombatMeter=meter.Capture(now);
                    pulse.ObservedCombatMeter=meter.CaptureObserved(now);
                    pulse.NpcCombatMeter=meter.CaptureObserved(now,true);
                    pulse.CombatClassification=classification.Values.ToArray();
                }
                // Resolve through the installed client's spell list on the game
                // thread. Never query game objects from the publishing timer.
                if(!pulse.Zoning && pulse.CastNanoId>0 && pulse.CastNanoName==null
                    && now-pulse.CastAttemptQpc<5*Stopwatch.Frequency && now-lastNameLookupQpc>=Stopwatch.Frequency/2) {
                    lastNameLookupQpc=now;
                    try {
                        if(Spell.Find(pulse.CastNanoId,out var spell)) {
                            string name=spell.Name;
                            if(!string.IsNullOrWhiteSpace(name)) {
                                name=new string(name.Where(c=>!char.IsControl(c)).Take(96).ToArray()).Trim();
                                if(name.Length>0) pulse.CastNanoName=name;
                            }
                        }
                    } catch { /* A failed name lookup must not affect casting or monitoring. */ }
                }
                if (pulse.Zoning) { pulse.VitalsQpc=0; pulse.TeamStateQpc=0; pulse.IsTeamLeader=null; pulse.AggroQpc=0; }
                else if (now-pulse.VitalsQpc >= Stopwatch.Frequency/5) {
                    // Sample only on the game thread. Publishing never dereferences game objects.
                    try {
                        var player=DynelManager.LocalPlayer;
                        if(player==null) pulse.VitalsQpc=0;
                        else {
                            pulse.Health=player.Health; pulse.MaxHealth=player.MaxHealth;
                            pulse.Nano=player.Nano; pulse.MaxNano=player.MaxNano;
                            pulse.Profession=player.Profession.ToString(); pulse.Attacking=player.IsAttacking;
                            string target=player.FightingTarget?.Name;
                            pulse.FightingTarget=target==null ? null : target.Substring(0,Math.Min(96,target.Length));
                            pulse.VitalsQpc=now;
                            try { pulse.IsTeamLeader=ReadTeamLeader(); pulse.TeamStateQpc=now; }
                            catch { pulse.IsTeamLeader=null; pulse.TeamStateQpc=0; }
                        }
                    } catch { pulse.VitalsQpc=0; }
                }
            }
        }
        // Display state only. The engine's team query can disagree with the game's
        // Team window after zoning. Prefer the same source as that visible window;
        // never infer leadership from preset order or the selected character.
        internal static bool ReadTeamLeader()
        {
            var view = AOSharp.Common.Unmanaged.Imports.TeamViewModule_c.GetInstanceIfAny();
            if (view != IntPtr.Zero)
                return AOSharp.Common.Unmanaged.Imports.TeamViewModule_c.IsInTeam(view) != 0
                    && AOSharp.Common.Unmanaged.Imports.TeamViewModule_c.IsTeamLeader(view) != 0;
            return Team.IsInTeam && Team.IsLeader;
        }

        void ReplyToProbe(object sender, float delta)
        {
            lock (gate) {
                if (stopped) return;
                try { channel.Respond(); } catch { /* Measurement failures must not interrupt the game. */ }
            }
        }
        void Publish(object unused)
        {
            if (Interlocked.Exchange(ref writing, 1) != 0) return;
            try {
                string json;
                AutoRegroupPermission permission = null;
                try {
                    permission = AutoRegroupPermission.Read(regroupPath);
                } catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException) {
                    // No valid, fresh permission means no automatic movement.
                    Trace.TraceWarning("Regroup permission unavailable: {0}", ex.GetType().Name);
                }
                movementGuard.SetRegroupPermission(permission);
                lock (gate) {
                    pulse.PublishedQpc = Stopwatch.GetTimestamp();
                    pulse.RecentMaxMs = 0;
                    long oldest = pulse.PublishedQpc - 10 * Stopwatch.Frequency;
                    for (int i = 0; i < times.Length; i++) if (times[i] >= oldest) pulse.RecentMaxMs = Math.Max(pulse.RecentMaxMs, gaps[i]);
                    json = new JavaScriptSerializer().Serialize(pulse);
                }
                File.WriteAllText(path + ".tmp", json);
                if (File.Exists(path)) File.Replace(path + ".tmp", path, null); else File.Move(path + ".tmp", path);
            } catch { /* A diagnostic failure must never interrupt the game. */ }
            finally { Volatile.Write(ref writing, 0); }
        }
        public override void Teardown()
        {
            lock (gate) { stopped = true; pulse.Active = false; }
            Game.OnUpdate -= Update;
            movementGuard.Dispose();
            rally?.Dispose();
            Game.OnUpdate -= ReplyToProbe;
            channel?.Dispose();
            MiscClientEvents.AttemptingSpellCast -= AttemptingSpellCast;
            Network.N3MessageSent -= MessageSent;
            Network.N3MessageReceived -= MessageReceived;
            Chat.FeedbackReceived -= FeedbackReceived;
            if (writer != null) using (var finished = new ManualResetEvent(false)) {
                if (writer.Dispose(finished)) finished.WaitOne(2000);
            }
            Publish(null);
        }
    }
}
