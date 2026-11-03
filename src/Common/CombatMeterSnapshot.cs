using System.Collections.Generic;

namespace NexusTelemetry
{
    // Optional JSON data. Local totals and single-client observations are separate snapshots.
    // Older observer snapshots remain readable, without depending on Mali's DLL.
    public sealed class CombatMeterSnapshot
    {
        public string Status { get; set; }
        public string Source { get; set; }
        public string Epoch { get; set; }
        public long ObservedQpc { get; set; }
        public double ElapsedSeconds { get; set; }
        public bool Paused { get; set; }
        public bool LimitReached { get; set; }
        public List<CombatMeterActor> Actors { get; set; } = new List<CombatMeterActor>();
    }
    public sealed class CombatMeterActor
    {
        public int CharacterId { get; set; }
        public string Name { get; set; }
        public string Profession { get; set; }
        public bool UnassignedPet { get; set; }
        public List<CombatMeterPet> Pets { get; set; } = new List<CombatMeterPet>();
        public long Damage { get; set; }
        public long DamageTaken { get; set; }
        public long Healing { get; set; }
        public long NanoDamage { get; set; }
        public long PetDamage { get; set; }
        public long Xp { get; set; }
        public long Axp { get; set; }
        public long Sk { get; set; }
        public long ExperienceEvents { get; set; }
    }
    public sealed class CombatMeterPet
    {
        public int CharacterId { get; set; }
        public string Name { get; set; }
        public bool Active { get; set; }
        public long Damage { get; set; }
        public long NanoDamage { get; set; }
        public long Healing { get; set; }
    }
}
