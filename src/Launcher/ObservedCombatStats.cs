using System;
using System.Collections.Generic;
using System.Linq;
using NexusTelemetry;

namespace AOLauncher
{
    // Observations from exactly one client. Never sum several views of the same hit.
    internal sealed class ObservedCombatRow
    {
        internal string Key, Name, Profession;
        internal int CharacterId;
        internal CombatStats Stats;
        internal List<int> RawIds=new List<int>();
    }
    internal sealed class ObservedCombatStats
    {
        readonly bool npc;
        internal ObservedCombatStats(bool npc=false) {this.npc=npc;}
        CombatMeterSnapshot Data(ClientObservation row)=>npc?row?.Pulse?.NpcCombatMeter:row?.Pulse?.ObservedCombatMeter;
        sealed class Baseline { internal CombatMeterActor Actor; internal double Seconds; }
        readonly Dictionary<int,Baseline> cards=new Dictionary<int,Baseline>(), totals=new Dictionary<int,Baseline>();
        string sourceKey, epoch;
        double resetSeconds;
        sealed class PetOwner {internal int Id;internal string Name,Profession,PetName;}
        bool automatic=true;
        readonly HashSet<int> activePets=new HashSet<int>();
        readonly Dictionary<int,PetOwner> petOwners=new Dictionary<int,PetOwner>();
        readonly HashSet<int> ambiguousPets=new HashSet<int>();
        internal ClientObservation Source { get; private set; }
        internal CombatMeterSnapshot Snapshot { get; private set; }
        internal bool Ready=>Snapshot!=null;
        static string Key(ClientObservation row)=>row.Client?.Server+":"+row.Client?.Port+":"+row.Pid+":"+row.StartTicks;
        bool Fresh(ClientObservation row,long now)=>Data(row)?.Source==(npc?"NexusNPC":"NexusObserved")
            && TeamCombatStats.Fresh(row,now,Data(row))
            && Data(row).Actors.All(a=>(a.Name?.Length??0)<=96 && (a.Profession?.Length??0)<=32);
        internal void SelectSource(ClientObservation row) { automatic=row==null;sourceKey=row==null?null:Key(row);epoch=null;Snapshot=null;Source=row;Clear(); }
        void Clear() { cards.Clear();totals.Clear();resetSeconds=0;petOwners.Clear();ambiguousPets.Clear(); }
        internal void Update(IEnumerable<ClientObservation> rows,long now)
        {
            var list=rows.ToList();
            if(automatic && !list.Any(r=>Key(r)==sourceKey && !r.ExitedUtc.HasValue))sourceKey=null;
            if(sourceKey==null) { var first=list.Where(r=>Fresh(r,now)).OrderBy(r=>r.Pid).FirstOrDefault();if(first!=null)sourceKey=Key(first); }
            Source=list.FirstOrDefault(r=>Key(r)==sourceKey);
            Snapshot=Fresh(Source,now)?Data(Source):null;
            if(Snapshot!=null && epoch!=Snapshot.Epoch) {epoch=Snapshot.Epoch;Clear();}
            activePets.Clear();
            if(!npc && Snapshot!=null && Source.Pulse.PlayfieldId>0)foreach(var row in list) {
                if(row.Pulse?.CombatMeter?.Source!="Nexus" || !TeamCombatStats.Fresh(row,now,row.Pulse.CombatMeter)
                    || row.Pulse.PlayfieldId!=Source.Pulse.PlayfieldId || row.Client.Server!=Source.Client.Server || row.Client.Port!=Source.Client.Port)continue;
                var owner=row.Pulse.CombatMeter.Actors.SingleOrDefault(a=>a.CharacterId==row.Client.CharacterId);if(owner==null)continue;
                foreach(var pet in (owner.Pets??new List<CombatMeterPet>()).Where(p=>p.Active)) {
                    if(petOwners.TryGetValue(pet.CharacterId,out var prior) && prior.Id!=owner.CharacterId) {ambiguousPets.Add(pet.CharacterId);petOwners.Remove(pet.CharacterId);}
                    if(!ambiguousPets.Contains(pet.CharacterId)) {
                        petOwners[pet.CharacterId]=new PetOwner {Id=owner.CharacterId,Name=row.Name,Profession=row.Pulse.Profession,PetName=Clean(pet.Name)};
                        activePets.Add(pet.CharacterId);
                    }
                }
            }
        }
        static string Clean(string value)=>new string((value??"").Where(c=>!char.IsControl(c)).ToArray());
        internal List<ObservedCombatRow> Rows()
        {
            if(!Ready)return new List<ObservedCombatRow>();
            var result=Snapshot.Actors.Select(a=> {
                cards.TryGetValue(a.CharacterId,out var baseline);
                string name=Clean(a.Name);if(string.IsNullOrWhiteSpace(name))name=(npc?"NPC #":"Player #")+a.CharacterId;
                if(a.UnassignedPet)name+=" · "+AOLauncher.Localization.Texts.Get("Text_pet_owner_unknown_028F63EE2EF3");
                return new ObservedCombatRow {Key=sourceKey+":"+epoch+":"+a.CharacterId,CharacterId=a.CharacterId,Name=name,
                    Profession=npc?"NPC":a.UnassignedPet?"Pet":Clean(a.Profession),RawIds=new List<int>{a.CharacterId},Stats=TeamCombatStats.Difference(a,baseline?.Actor,Snapshot.ElapsedSeconds-(baseline?.Seconds??0))};
            }).ToList();
            if(!npc)foreach(var actor in Snapshot.Actors.Where(a=>a.UnassignedPet)) {
                if(!petOwners.TryGetValue(actor.CharacterId,out var owner))continue;
                var pet=result.FirstOrDefault(r=>r.CharacterId==actor.CharacterId);if(pet?.Stats==null)continue;
                var parent=result.FirstOrDefault(r=>r.CharacterId==owner.Id);
                if(parent==null) {
                    cards.TryGetValue(owner.Id,out var baseline);
                    parent=new ObservedCombatRow {Key=sourceKey+":"+epoch+":"+owner.Id,CharacterId=owner.Id,Name=owner.Name,Profession=owner.Profession,
                        Stats=new CombatStats {Seconds=Snapshot.ElapsedSeconds-(baseline?.Seconds??0),Observed=1,Requested=1}};result.Add(parent);
                }
                if(parent.Stats==null)continue;
                parent.Stats.Damage+=pet.Stats.Damage;parent.Stats.PetDamage+=pet.Stats.Damage;
                parent.Stats.Healing+=pet.Stats.Healing;parent.Stats.NanoDamage+=pet.Stats.NanoDamage;
                pet.Stats.Seconds=parent.Stats.Seconds;
                var existing=parent.Stats.Pets.FirstOrDefault(p=>p.Id==actor.CharacterId);
                if(existing==null)parent.Stats.Pets.Add(new PetCombatStats {Id=actor.CharacterId,Name=Clean(actor.Name),Stats=pet.Stats});
                else {existing.Stats.Damage+=pet.Stats.Damage;existing.Stats.PetDamage+=pet.Stats.Damage;
                    existing.Stats.NanoDamage+=pet.Stats.NanoDamage;existing.Stats.Healing+=pet.Stats.Healing;}
                parent.RawIds.Add(actor.CharacterId);result.Remove(pet);
            }
            // Roster metadata supplies current names and zero-damage rows, never damage totals.
            if(!npc)foreach(var entry in petOwners) {
                var owner=entry.Value;
                var parent=result.FirstOrDefault(r=>r.CharacterId==owner.Id);
                if(parent==null && activePets.Contains(entry.Key)) {
                    cards.TryGetValue(owner.Id,out var baseline);
                    parent=new ObservedCombatRow {Key=sourceKey+":"+epoch+":"+owner.Id,CharacterId=owner.Id,Name=owner.Name,Profession=owner.Profession,
                        Stats=new CombatStats {Seconds=Snapshot.ElapsedSeconds-(baseline?.Seconds??0),Observed=1,Requested=1}};
                    result.Add(parent);
                }
                if(parent?.Stats==null)continue;
                var child=parent.Stats.Pets.FirstOrDefault(p=>p.Id==entry.Key);
                if(child==null && activePets.Contains(entry.Key)) {
                    child=new PetCombatStats {Id=entry.Key,Stats=new CombatStats {Seconds=parent.Stats.Seconds}};
                    parent.Stats.Pets.Add(child);
                }
                if(child!=null && !string.IsNullOrWhiteSpace(owner.PetName))child.Name=owner.PetName;
            }
            return result;
        }
        internal void ResetRow(ObservedCombatRow row)
        {
            if(!Ready || row==null || row.Key!=sourceKey+":"+epoch+":"+row.CharacterId)return;
            var current=Rows().FirstOrDefault(r=>r.Key==row.Key);if(current==null)return;
            foreach(var actor in Snapshot.Actors.Where(a=>current.RawIds.Contains(a.CharacterId)))
                cards[actor.CharacterId]=new Baseline {Actor=TeamCombatStats.Copy(actor),Seconds=Snapshot.ElapsedSeconds};
            if(!current.RawIds.Contains(row.CharacterId))cards[row.CharacterId]=new Baseline {Actor=new CombatMeterActor {CharacterId=row.CharacterId},Seconds=Snapshot.ElapsedSeconds};
        }
        internal void ResetAll()
        {
            if(!Ready)return;
            totals.Clear();resetSeconds=Snapshot.ElapsedSeconds;
            foreach(var actor in Snapshot.Actors)totals[actor.CharacterId]=new Baseline {Actor=TeamCombatStats.Copy(actor),Seconds=resetSeconds};
        }
        internal CombatStats Total()
        {
            if(!Ready)return null;
            var sum=new CombatStats {Seconds=Snapshot.ElapsedSeconds-resetSeconds,Requested=Snapshot.Actors.Count};
            foreach(var actor in Snapshot.Actors) {
                totals.TryGetValue(actor.CharacterId,out var baseline);
                var stats=TeamCombatStats.Difference(actor,baseline?.Actor,sum.Seconds);if(stats==null)continue;
                sum.Observed++;sum.Damage+=stats.Damage;sum.DamageTaken+=stats.DamageTaken;sum.Healing+=stats.Healing;sum.NanoDamage+=stats.NanoDamage;sum.PetDamage+=stats.PetDamage;
            }
            if(!npc){sum.Requested=Rows().Count;sum.Observed=Rows().Count(r=>r.Stats!=null);}
            return sum;
        }
    }
}
