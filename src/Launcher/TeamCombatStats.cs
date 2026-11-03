using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NexusTelemetry;

namespace AOLauncher
{
    internal sealed class PetCombatStats
    {
        internal int Id;
        internal string Name;
        internal CombatStats Stats;
    }
    internal sealed class CombatStats
    {
        internal List<PetCombatStats> Pets=new List<PetCombatStats>();
        internal long Damage, DamageTaken, Healing, NanoDamage, PetDamage;
        internal long Xp, Axp, Sk, ExperienceEvents;
        internal bool Native;
        internal double? TotalDps, TotalNanoDps, TotalPetDps, TotalHps, TotalXpHour, TotalAxpHour, TotalSkHour;
        internal double Seconds;
        internal int Observed, Requested;
        internal double? Dps => TotalDps ?? (Seconds>0 ? Damage/Seconds : (double?)null);
        internal double? Dpm => Dps*60;
        internal double? Dph => Dps*3600;
        internal double? TakenDps => Seconds>0 ? DamageTaken/Seconds : (double?)null;
        internal double? NanoDps => TotalNanoDps ?? (Seconds>0 ? NanoDamage/Seconds : (double?)null);
        internal double? PetDps => TotalPetDps ?? (Seconds>0 ? PetDamage/Seconds : (double?)null);
        internal double? PetDpm => PetDps*60;
        internal double? PetDph => PetDps*3600;
        internal double? Hps => TotalHps ?? (Seconds>0 ? Healing/Seconds : (double?)null);
        internal double? XpHour => TotalXpHour ?? (Seconds>0 ? Xp*3600.0/Seconds : (double?)null);
        internal double? AxpHour => TotalAxpHour ?? (Seconds>0 ? Axp*3600.0/Seconds : (double?)null);
        internal double? SkHour => TotalSkHour ?? (Seconds>0 ? Sk*3600.0/Seconds : (double?)null);
    }
    internal sealed class TeamCombatStats
    {
        sealed class Baseline { internal CombatMeterActor Actor; internal double Seconds; }
        readonly Dictionary<int,Baseline> cards=new Dictionary<int,Baseline>();
        Dictionary<int,CombatMeterActor> team=new Dictionary<int,CombatMeterActor>();
        double teamSeconds;
        List<ClientObservation> nativeRows=new List<ClientObservation>();
        readonly Dictionary<string,Baseline> nativeCards=new Dictionary<string,Baseline>(), nativeTeam=new Dictionary<string,Baseline>();
        readonly HashSet<string> pendingTeamReset=new HashSet<string>();
        long observedNow;
        internal bool Native {get; private set;}
        string sourceKey, epoch;
        internal ClientObservation Source { get; private set; }
        internal CombatMeterSnapshot Snapshot { get; private set; }
        internal bool Ready => Native ? nativeRows.Any(r=>NativeActor(r)!=null) : Snapshot!=null;
        static string Key(ClientObservation row) => row.Pid+":"+row.StartTicks;
        internal static bool Valid(CombatMeterSnapshot value) => value!=null && value.Status=="Ready" && !string.IsNullOrEmpty(value.Epoch) && value.Epoch.Length<=64
            && !double.IsNaN(value.ElapsedSeconds) && !double.IsInfinity(value.ElapsedSeconds) && value.ElapsedSeconds>=0
            && value.Actors!=null && value.Actors.Count<=128 && value.Actors.All(a=>a!=null && a.CharacterId>0 && a.Damage>=0 && a.Damage<=1000000000000000L
                && a.DamageTaken>=0 && a.DamageTaken<=1000000000000000L && a.Healing>=0 && a.Healing<=1000000000000000L && a.NanoDamage>=0 && a.NanoDamage<=a.Damage && a.PetDamage>=0 && a.PetDamage<=a.Damage
                && a.Xp>=0 && a.Xp<=1000000000000000L && a.Axp>=0 && a.Axp<=1000000000000000L && a.Sk>=0 && a.Sk<=1000000000000000L && a.ExperienceEvents>=0 && a.ExperienceEvents<=1000000000000000L
                && ValidPets(a)) && value.Actors.Sum(a=>a.Pets?.Count??0)<=256
            && value.Actors.Select(a=>a.CharacterId).Distinct().Count()==value.Actors.Count;
        static bool ValidPets(CombatMeterActor a)=>a.Pets==null || (a.Pets.Count<=64 && a.Pets.All(p=>p!=null && p.CharacterId>0 && (p.Name?.Length??0)<=96
            && p.Damage>=0 && p.Damage<=a.PetDamage && p.Healing>=0 && p.Healing<=a.Healing && p.NanoDamage>=0 && p.NanoDamage<=p.Damage)
            && a.Pets.Select(p=>p.CharacterId).Distinct().Count()==a.Pets.Count && a.Pets.Sum(p=>p.Damage)<=a.PetDamage && a.Pets.Sum(p=>p.Healing)<=a.Healing);
        static bool Fresh(ClientObservation row,long now) => Fresh(row,now,row?.Pulse?.CombatMeter);
        internal static bool Fresh(ClientObservation row,long now,CombatMeterSnapshot snapshot) => row!=null && !row.ExitedUtc.HasValue && row.Client!=null && row.Pulse!=null && row.Pulse.Active && !row.Pulse.Zoning
            && row.Pid==row.Pulse.Pid && row.StartTicks==row.Pulse.StartTicks && row.Pid==row.Client.ProcessId && row.StartTicks==row.Client.StartTimeUtcTicks
            && ClientMonitor.Age(now,row.Pulse.PublishedQpc)<3000 && Valid(snapshot)
            && snapshot.ObservedQpc<=row.Pulse.PublishedQpc && ClientMonitor.Age(now,snapshot.ObservedQpc)<3000;
        internal void SelectSource(ClientObservation row) {
            sourceKey=row==null?null:Key(row);Source=row;Snapshot=null;epoch=null;ClearBaselines();
        }
        void ClearBaselines() {cards.Clear();team.Clear();teamSeconds=0;}
        internal void Update(IEnumerable<ClientObservation> rows,long now) {
            var list=rows.ToList();
            nativeRows=list;observedNow=now;Native=list.Any(r=>r.Pulse?.CombatMeter?.Source=="Nexus");
            foreach(var row in list.Where(r=>pendingTeamReset.Contains(Key(r))).ToList()) {
                var actor=NativeActor(row);if(actor==null)continue;
                nativeTeam[NativeKey(row)]=new Baseline {Actor=Copy(actor),Seconds=row.Pulse.CombatMeter.ElapsedSeconds};pendingTeamReset.Remove(Key(row));
            }
            if(sourceKey==null) {
                var first=list.Where(r=>Fresh(r,now)).OrderBy(r=>r.Pid).FirstOrDefault();
                if(first!=null) sourceKey=Key(first);
            }
            Source=list.FirstOrDefault(r=>Key(r)==sourceKey);
            Snapshot=Fresh(Source,now)?Source.Pulse.CombatMeter:null;
            if(Snapshot!=null && epoch!=Snapshot.Epoch) {epoch=Snapshot.Epoch;ClearBaselines();}
        }
        bool SameWorld(ClientObservation row) => row?.Client!=null && Source?.Client!=null && !string.IsNullOrWhiteSpace(row.Client.Server) && row.Client.Port>0
            && string.Equals(row.Client.Server,Source.Client.Server,StringComparison.OrdinalIgnoreCase) && row.Client.Port==Source.Client.Port;
        CombatMeterActor Actor(ClientObservation row) => Ready && SameWorld(row) ? Snapshot.Actors.FirstOrDefault(a=>a.CharacterId==row.Client.CharacterId) : null;
        internal static CombatMeterActor Copy(CombatMeterActor a) => new CombatMeterActor {CharacterId=a.CharacterId,Name=a.Name,Profession=a.Profession,UnassignedPet=a.UnassignedPet,Damage=a.Damage,DamageTaken=a.DamageTaken,Healing=a.Healing,NanoDamage=a.NanoDamage,PetDamage=a.PetDamage,Xp=a.Xp,Axp=a.Axp,Sk=a.Sk,ExperienceEvents=a.ExperienceEvents,
            Pets=(a.Pets??new List<CombatMeterPet>()).Select(p=>new CombatMeterPet {CharacterId=p.CharacterId,Name=p.Name,Active=p.Active,Damage=p.Damage,NanoDamage=p.NanoDamage,Healing=p.Healing}).ToList()};
        internal static CombatStats Difference(CombatMeterActor a,CombatMeterActor b,double seconds) {
            long damage=a.Damage-(b?.Damage??0),healing=a.Healing-(b?.Healing??0),nano=a.NanoDamage-(b?.NanoDamage??0),pet=a.PetDamage-(b?.PetDamage??0);
            long taken=a.DamageTaken-(b?.DamageTaken??0);
            if(damage<0 || taken<0 || healing<0 || nano<0 || pet<0 || seconds<0) return null;
            if(a.Xp<(b?.Xp??0) || a.Axp<(b?.Axp??0) || a.Sk<(b?.Sk??0) || a.ExperienceEvents<(b?.ExperienceEvents??0))return null;
            var result=new CombatStats {Damage=damage,DamageTaken=taken,Healing=healing,NanoDamage=nano,PetDamage=pet,Seconds=seconds,Observed=1,Requested=1,
                Xp=a.Xp-(b?.Xp??0),Axp=a.Axp-(b?.Axp??0),Sk=a.Sk-(b?.Sk??0),ExperienceEvents=a.ExperienceEvents-(b?.ExperienceEvents??0)};
            foreach(var child in a.Pets??new List<CombatMeterPet>()) {
                var prior=b?.Pets?.FirstOrDefault(p=>p.CharacterId==child.CharacterId);
                long d=child.Damage-(prior?.Damage??0),h=child.Healing-(prior?.Healing??0),n=child.NanoDamage-(prior?.NanoDamage??0);
                if(d<0 || h<0 || n<0)continue;
                string name=new string((child.Name??"").Where(c=>!char.IsControl(c)).ToArray()).Trim();
                result.Pets.Add(new PetCombatStats {Id=child.CharacterId,Name=name.Length==0?"Pet":name,
                    Stats=new CombatStats {Damage=d,PetDamage=d,Healing=h,NanoDamage=n,Seconds=seconds,Observed=1,Requested=1}});
            }
            return result;
        }
        static string NativeKey(ClientObservation row)=>Key(row)+":"+row.Pulse.CombatMeter.Epoch;
        CombatMeterActor NativeActor(ClientObservation row)=>Fresh(row,observedNow) && row.Pulse.CombatMeter.Source=="Nexus"
            && row.Pulse.CombatMeter.Actors.Count==1 && row.Pulse.CombatMeter.Actors[0].CharacterId==row.Client.CharacterId ? row.Pulse.CombatMeter.Actors[0] : null;
        CombatStats NativeStats(ClientObservation row,Dictionary<string,Baseline> baselines) {
            var actor=NativeActor(row);if(actor==null)return null;
            baselines.TryGetValue(NativeKey(row),out var baseline);
            var result=Difference(actor,baseline?.Actor,row.Pulse.CombatMeter.ElapsedSeconds-(baseline?.Seconds??0));
            if(result!=null)result.Native=true;return result;
        }
        internal CombatStats Card(ClientObservation row) {
            if(Native)return NativeStats(row,nativeCards);
            var actor=Actor(row);if(actor==null)return null;
            cards.TryGetValue(actor.CharacterId,out var baseline);
            return Difference(actor,baseline?.Actor,Snapshot.ElapsedSeconds-(baseline?.Seconds??0));
        }
        internal void ResetCard(ClientObservation row) {
            if(Native) {var own=NativeActor(row);if(own!=null)nativeCards[NativeKey(row)]=new Baseline {Actor=Copy(own),Seconds=row.Pulse.CombatMeter.ElapsedSeconds};return;}
            var actor=Actor(row);if(actor!=null)cards[actor.CharacterId]=new Baseline {Actor=Copy(actor),Seconds=Snapshot.ElapsedSeconds};
        }
        internal void ResetTeam(IEnumerable<ClientObservation> selected=null) {
            if(Native) {
                foreach(var row in selected??nativeRows) {
                    var own=NativeActor(row);
                    if(own!=null)nativeTeam[NativeKey(row)]=new Baseline {Actor=Copy(own),Seconds=row.Pulse.CombatMeter.ElapsedSeconds};
                    else pendingTeamReset.Add(Key(row));
                }
                return;
            }
            if(!Ready)return;
            team=Snapshot.Actors.ToDictionary(a=>a.CharacterId,Copy);teamSeconds=Snapshot.ElapsedSeconds;
        }
        internal CombatStats Total(IEnumerable<ClientObservation> rows) {
            if(!Ready)return null;
            if(Native) {
                var sum=new CombatStats {Native=true,TotalDps=0,TotalNanoDps=0,TotalPetDps=0,TotalHps=0,TotalXpHour=0,TotalAxpHour=0,TotalSkHour=0};
                foreach(var row in rows.Where(r=>!r.ExitedUtc.HasValue).GroupBy(r=>r.Client?.Server+":"+r.Client?.Port+":"+r.Client?.CharacterId).Select(g=>g.OrderByDescending(r=>NativeActor(r)!=null).First())) {
                    sum.Requested++;var own=NativeStats(row,nativeTeam);if(own==null)continue;
                    sum.Observed++;sum.Damage+=own.Damage;sum.Healing+=own.Healing;sum.NanoDamage+=own.NanoDamage;sum.PetDamage+=own.PetDamage;
                    sum.Xp+=own.Xp;sum.Axp+=own.Axp;sum.Sk+=own.Sk;sum.ExperienceEvents+=own.ExperienceEvents;sum.Seconds=Math.Max(sum.Seconds,own.Seconds);
                    sum.TotalDps+=own.Dps??0;sum.TotalHps+=own.Hps??0;sum.TotalXpHour+=own.XpHour??0;sum.TotalAxpHour+=own.AxpHour??0;sum.TotalSkHour+=own.SkHour??0;
                    sum.TotalPetDps+=own.PetDps??0;
                    sum.TotalNanoDps+=own.NanoDps??0;
                }
                return sum;
            }
            var result=new CombatStats {Seconds=Snapshot.ElapsedSeconds-teamSeconds};
            foreach(var row in rows.Where(r=>!r.ExitedUtc.HasValue).GroupBy(r=>r.Client?.Server+":"+r.Client?.Port+":"+r.Client?.CharacterId).Select(g=>g.First())) {
                result.Requested++;var actor=Actor(row);if(actor==null)continue;
                team.TryGetValue(actor.CharacterId,out var baseline);var delta=Difference(actor,baseline,result.Seconds);if(delta==null)continue;
                result.Observed++;result.Damage+=delta.Damage;result.Healing+=delta.Healing;result.NanoDamage+=delta.NanoDamage;result.PetDamage+=delta.PetDamage;
            }
            return result;
        }
        internal static string Rate(double? value) => value.HasValue?value.Value.ToString("N1"):"—";
        internal static string CompactRate(double? value) => !value.HasValue?"—":value>=1000000000?(value.Value/1000000000).ToString("0.0")+"B":
            value>=1000000?(value.Value/1000000).ToString("0.0")+"M":value>=1000?(value.Value/1000).ToString("0.0")+"k":Rate(value);
    }
}
