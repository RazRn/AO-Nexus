using System;
using System.Diagnostics;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AOSharp.Common.GameData;
using SmokeLounge.AOtomation.Messaging.Messages;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;

namespace NexusTelemetry
{
    // Game-thread lookup state, scoped to one playfield. Missing values never
    // replace a confirmed profession. Requests are bounded even if no reply arrives.
    internal sealed class NearbyProfessions
    {
        sealed class Entry
        {
            internal string Profession;
            internal int Attempts;
            internal long NextRequest;
        }
        readonly Dictionary<int,Entry> entries = new Dictionary<int,Entry>();
        long nextRequest;
        internal static bool Valid(string value) => Enum.TryParse(value, out Profession profession)
            && Enum.IsDefined(typeof(Profession), profession)
            && profession != Profession.Unknown && profession != Profession.Monster;

        internal string Remember(int id, string value)
        {
            if (id <= 0) return null;
            if (!entries.TryGetValue(id, out var entry)) {
                if (entries.Count >= 128) return Valid(value) ? value : null;
                entries.Add(id, entry = new Entry());
            }
            if (Valid(value)) entry.Profession = value;
            return entry.Profession;
        }
        internal void Poll(long now, Func<int,string> read, Action<int> request, Action<int,string> resolved)
        {
            foreach (var pair in entries.ToArray()) {
                var entry = pair.Value;
                if (entry.Profession != null) continue;
                // null means the character is no longer present; do not request it.
                string value = read(pair.Key);
                if (value == null) continue;
                if (Valid(value)) {
                    entry.Profession = value;
                    resolved(pair.Key, value);
                } else if (entry.Attempts < 3 && now >= entry.NextRequest && now >= nextRequest) {
                    entry.Attempts++;
                    entry.NextRequest = now + 5 * Stopwatch.Frequency;
                    nextRequest = now + 2 * Stopwatch.Frequency;
                    request(pair.Key);
                }
            }
        }
        internal void Clear() { entries.Clear(); nextRequest = 0; }
    }

    // Counts only the local player and their pets from inbound game events.
    // No chat logging, packet injection, game actions, or third-party meter code.
    internal sealed class NativeCombatMeter
    {
        internal sealed class SourceActor
        {
            internal int CharacterId;
            internal string Name, Profession;
            internal string PetName;
            internal bool Pet, UnassignedPet, Npc;
        }
        readonly Dictionary<int,CombatMeterActor> nearby = new Dictionary<int,CombatMeterActor>();
        readonly Dictionary<int,CombatMeterActor> npcs = new Dictionary<int,CombatMeterActor>();
        bool npcLimit;
        string nearbyEpoch;
        long nearbyStarted;
        bool nearbyLimit;
        CombatMeterActor actor;
        string epoch;
        long started;
        bool petLimit;
        static readonly Regex award = new Regex(@"\A(?:You received (?<xp>[0-9]+) xp\.|You gained (?<axp>[0-9]+) new Alien Experience Points\.|You gained (?<sk>[0-9]+) points of Shadowknowledge\.)\z", RegexOptions.CultureInvariant);

        internal void Begin(int characterId, long now) {
            if(characterId<=0 || (actor!=null && actor.CharacterId==characterId)) return;
            actor=new CombatMeterActor {CharacterId=characterId};started=now;epoch=Guid.NewGuid().ToString("N");petLimit=false;
            ResetObserved(now);
        }
        internal void ResetObserved(long now) { nearby.Clear();npcs.Clear(); nearbyStarted=now; nearbyEpoch=Guid.NewGuid().ToString("N"); nearbyLimit=npcLimit=false; }
        internal void Observe(N3Message message, Func<Identity,int> ownership, Func<Identity,SourceActor> resolve=null) {
            if(actor==null || message==null) return;
            Identity source,recipient; long amount; bool nano=false, heal=false;
            if(message is AttackInfoMessage attack) {source=attack.Identity;recipient=attack.Target;amount=attack.Amount;}
            else if(message is SpecialAttackInfoMessage special) {source=special.Identity;recipient=special.Target;amount=special.Amount;}
            // For HealthDamage, Target is the caster; Identity is the recipient.
            else if(message is HealthDamageMessage health) {source=health.Target;recipient=health.Identity;amount=Math.Abs((long)health.Amount);heal=health.Amount>0;nano=!heal;}
            else if(message is ReflectAttackMessage reflect) {source=reflect.Target;recipient=reflect.Identity;amount=reflect.Amount;}
            else if(message is ShieldAttackMessage shield) {source=shield.Target;recipient=shield.Identity;amount=shield.Amount;}
            else return;
            if(amount<=0 || amount>int.MaxValue) return;
            int owner=ownership(source);
            if(owner==1 || owner==2) Count(actor,amount,heal,nano,owner==2);
            var sourceActor=resolve?.Invoke(source);
            if(owner==2)CountPet(actor,source.Instance,sourceActor?.PetName,amount,heal,nano,true);
            if(resolve==null)return;
            // Resolve both ends independently: unknown attackers can still damage a known NPC.
            var target=heal?null:resolve(recipient);
            if(target?.Npc==true) {var victim=Observed(target);if(victim!=null)victim.DamageTaken=Add(victim.DamageTaken,amount);}
            var observed=Observed(sourceActor);
            if(observed==null)return;
            Count(observed,amount,heal,nano,sourceActor.Pet);
            if(sourceActor.Pet && !sourceActor.UnassignedPet)CountPet(observed,source.Instance,sourceActor.PetName,amount,heal,nano,false);
        }
        internal void SetLocalPets(IEnumerable<CombatMeterPet> roster)
        {
            if(actor==null)return;
            foreach(var pet in actor.Pets)pet.Active=false;
            foreach(var item in roster.Where(p=>p.CharacterId>0).Take(64)) {
                var pet=Pet(actor,item.CharacterId,item.Name,true);
                if(pet!=null)pet.Active=true;
            }
        }
        CombatMeterPet Pet(CombatMeterActor owner,int id,string name,bool local)
        {
            if(id<=0)return null;
            var pet=owner.Pets.FirstOrDefault(p=>p.CharacterId==id);
            if(pet==null) {
                if(owner.Pets.Count>=64 || (!local && nearby.Values.Concat(npcs.Values).Sum(a=>a.Pets.Count)>=256))
                    {if(local)petLimit=true;else nearbyLimit=npcLimit=true;return null;}
                pet=new CombatMeterPet {CharacterId=id};owner.Pets.Add(pet);
            }
            if(!string.IsNullOrWhiteSpace(name))pet.Name=Clean(name,96);
            return pet;
        }
        void CountPet(CombatMeterActor owner,int id,string name,long amount,bool heal,bool nano,bool local)
        {
            var pet=Pet(owner,id,name,local);if(pet==null)return;
            if(heal)pet.Healing=Add(pet.Healing,amount);
            else {pet.Damage=Add(pet.Damage,amount);if(nano)pet.NanoDamage=Add(pet.NanoDamage,amount);}
        }
        static List<CombatMeterPet> CopyPets(CombatMeterActor owner)=>owner.Pets.Select(p=>new CombatMeterPet {
            CharacterId=p.CharacterId,Name=p.Name,Active=p.Active,Damage=p.Damage,NanoDamage=p.NanoDamage,Healing=p.Healing}).ToList();
        CombatMeterActor Observed(SourceActor sourceActor) {
            if(sourceActor==null || sourceActor.CharacterId<=0)return null;
            var bucket=sourceActor.Npc?npcs:nearby;
            if(!bucket.TryGetValue(sourceActor.CharacterId,out var observed)) {
                if(bucket.Count>=128){if(sourceActor.Npc)npcLimit=true;else nearbyLimit=true;return null;}
                observed=new CombatMeterActor {CharacterId=sourceActor.CharacterId};bucket.Add(observed.CharacterId,observed);
            }
            observed.Name=Clean(sourceActor.Name,96);
            if(NearbyProfessions.Valid(sourceActor.Profession))observed.Profession=Clean(sourceActor.Profession,32);
            observed.UnassignedPet=sourceActor.UnassignedPet;
            return observed;
        }
        internal void SetObservedProfession(int id,string profession)
        {
            if(NearbyProfessions.Valid(profession) && nearby.TryGetValue(id,out var observed))
                observed.Profession=profession;
        }
        static string Clean(string value,int max)=>new string((value??"").Where(c=>!char.IsControl(c)).Take(max).ToArray());
        static void Count(CombatMeterActor target,long amount,bool heal,bool nano,bool pet) {
            if(heal) target.Healing=Add(target.Healing,amount);
            else {
                target.Damage=Add(target.Damage,amount);
                if(nano) target.NanoDamage=Add(target.NanoDamage,amount);
                if(pet) target.PetDamage=Add(target.PetDamage,amount);
            }
        }
        internal CombatMeterSnapshot CaptureObserved(long now,bool npc=false) {
            var snapshot=new CombatMeterSnapshot {Source=npc?"NexusNPC":"NexusObserved",Status=actor==null?"NotReady":"Ready",Epoch=nearbyEpoch,ObservedQpc=now,
                ElapsedSeconds=actor==null?0:Math.Max(0,(now-nearbyStarted)/(double)Stopwatch.Frequency),LimitReached=npc?npcLimit:nearbyLimit};
            foreach(var a in (npc?npcs:nearby).Values) snapshot.Actors.Add(new CombatMeterActor {CharacterId=a.CharacterId,Name=a.Name,Profession=a.Profession,UnassignedPet=a.UnassignedPet,
                Damage=a.Damage,DamageTaken=a.DamageTaken,Healing=a.Healing,NanoDamage=a.NanoDamage,PetDamage=a.PetDamage,Pets=CopyPets(a)});
            return snapshot;
        }
        static long Add(long value,long amount) => value>long.MaxValue-amount?long.MaxValue:value+amount;
        internal void Feedback(string text) {
            if(actor==null || string.IsNullOrEmpty(text) || text.Length>256) return;
            var match=award.Match(text.Trim());
            if(!match.Success) return;
            string kind=match.Groups["xp"].Success?"xp":match.Groups["axp"].Success?"axp":"sk";
            if(!long.TryParse(match.Groups[kind].Value,NumberStyles.None,CultureInfo.InvariantCulture,out var amount) || amount<=0 || amount>int.MaxValue) return;
            if(kind=="xp")actor.Xp=Add(actor.Xp,amount);
            else if(kind=="axp")actor.Axp=Add(actor.Axp,amount);
            else actor.Sk=Add(actor.Sk,amount);
            actor.ExperienceEvents=Add(actor.ExperienceEvents,1);
        }
        internal CombatMeterSnapshot Capture(long now) {
            var result=new CombatMeterSnapshot {Source="Nexus",Status=actor==null?"NotReady":"Ready",Epoch=epoch,ObservedQpc=now,
                ElapsedSeconds=actor==null?0:Math.Max(0,(now-started)/(double)Stopwatch.Frequency),LimitReached=petLimit};
            if(actor!=null)result.Actors.Add(new CombatMeterActor {CharacterId=actor.CharacterId,Damage=actor.Damage,Healing=actor.Healing,
                NanoDamage=actor.NanoDamage,PetDamage=actor.PetDamage,Xp=actor.Xp,Axp=actor.Axp,Sk=actor.Sk,ExperienceEvents=actor.ExperienceEvents,Pets=CopyPets(actor)});
            return result;
        }
    }
}
