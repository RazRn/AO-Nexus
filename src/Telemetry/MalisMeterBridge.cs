using System;
using System.Collections;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace NexusTelemetry
{
    // Read only, on Game.OnUpdate. Never loads, patches or invokes the toolkit launcher.
    internal sealed class MalisMeterBridge
    {
        Assembly assembly;
        long lastDiscovery;
        object lastRegisters;
        CombatMeterSnapshot previous;
        string epoch=Guid.NewGuid().ToString("N");
        static object Field(object instance,string name) => instance.GetType().GetField(name,BindingFlags.Public|BindingFlags.Instance).GetValue(instance);
        static long Total(object instance,string name) => Convert.ToInt64(Field(Field(instance,name),"Total"));
        internal CombatMeterSnapshot Capture(long now)
        {
            try {
                if(assembly==null && (lastDiscovery==0 || now-lastDiscovery>=5*Stopwatch.Frequency)) {
                    lastDiscovery=now;
                    assembly=AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a=>a.GetName().Name=="Malis Damage Meter");
                }
                if(assembly==null) return new CombatMeterSnapshot {Status="NotLoaded",ObservedQpc=now};
                var registers=assembly.GetType("MalisDamageMeter.HitRegisters",true).GetField("Characters").GetValue(null) as IDictionary;
                var window=assembly.GetType("MalisDamageMeter.Main",true).GetField("Window").GetValue(null);
                if(registers==null || window==null) return new CombatMeterSnapshot {Status="NotReady",ObservedQpc=now};
                if(registers.Count>128) return new CombatMeterSnapshot {Status="TooManyEntries",ObservedQpc=now};
                var config=Field(window,"ViewSettings");
                double elapsed=Convert.ToDouble(config.GetType().GetMethod("GetElapsedTimeSeconds").Invoke(config,null));
                if(double.IsNaN(elapsed) || double.IsInfinity(elapsed) || elapsed<0) throw new InvalidOperationException();
                var result=new CombatMeterSnapshot {Status="Ready",ObservedQpc=now,ElapsedSeconds=elapsed,Paused=(bool)Field(config,"IsPaused")};
                foreach(DictionaryEntry entry in registers) {
                    // Match player identities, never name guesses or NPC/pet rows.
                    if(entry.Value.GetType().Name!="PlayerCharData") continue;
                    var damage=Field(entry.Value,"DamageSources");
                    var actor=new CombatMeterActor {CharacterId=Convert.ToInt32(entry.Key),Damage=Convert.ToInt64(Field(damage,"Total")),
                        Healing=Total(entry.Value,"HealSource"),NanoDamage=Total(damage,"Nanobots"),PetDamage=Convert.ToInt64(Field(damage,"PetTotal"))};
                    if(actor.CharacterId<=0 || actor.Damage<0 || actor.Healing<0 || actor.NanoDamage<0 || actor.PetDamage<0
                        || actor.NanoDamage>actor.Damage || actor.PetDamage>actor.Damage) throw new InvalidOperationException();
                    result.Actors.Add(actor);
                }
                bool reset=previous!=null && (!ReferenceEquals(lastRegisters,registers) || elapsed<previous.ElapsedSeconds
                    || previous.Actors.Any(old=>!result.Actors.Any(a=>a.CharacterId==old.CharacterId)
                        || result.Actors.Any(a=>a.CharacterId==old.CharacterId && (a.Damage<old.Damage || a.Healing<old.Healing || a.NanoDamage<old.NanoDamage || a.PetDamage<old.PetDamage))));
                if(reset) epoch=Guid.NewGuid().ToString("N");
                result.Epoch=epoch;previous=result;lastRegisters=registers;return result;
            } catch { return new CombatMeterSnapshot {Status="Unavailable",ObservedQpc=now}; }
        }
    }
}
