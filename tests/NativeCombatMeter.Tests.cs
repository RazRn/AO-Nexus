using System;
using System.Diagnostics;
using AOSharp.Common.GameData;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;
using NexusTelemetry;
static class NativeCombatMeterTests {
    static int checks;
    static void Check(bool value,string name){if(!value)throw new Exception(name);checks++;}
    static Identity Id(int n)=>new Identity(IdentityType.SimpleChar,n);
    static int Main(){
        ProfessionLookups();
        long now=Stopwatch.GetTimestamp();var meter=new NativeCombatMeter();
        Check(meter.Capture(now).Status=="NotReady","No character means unavailable");meter.Begin(1,now);
        Func<Identity,int> own=id=>id==Id(1)?1:id==Id(11)?2:0;
        meter.Observe(new AttackInfoMessage {Identity=Id(2),Target=Id(1),Amount=900},own);
        Check(meter.Capture(now).Actors[0].Damage==0,"Incoming damage belongs to its attacker, not its victim");
        meter.Observe(new AttackInfoMessage {Identity=Id(1),Amount=100},own);
        meter.Observe(new SpecialAttackInfoMessage {Identity=Id(1),Amount=200},own);
        meter.Observe(new HealthDamageMessage {Identity=Id(2),Target=Id(1),Amount=-50},own);
        meter.Observe(new HealthDamageMessage {Identity=Id(1),Target=Id(2),Amount=75},own);
        meter.Observe(new HealthDamageMessage {Identity=Id(2),Target=Id(1),Amount=60},own);
        meter.Observe(new ReflectAttackMessage {Identity=Id(2),Target=Id(1),Amount=7},own);
        meter.Observe(new ShieldAttackMessage {Identity=Id(2),Target=Id(1),Amount=3},own);
        meter.Observe(new AttackInfoMessage {Identity=Id(11),Amount=40},own);
        meter.Observe(new HealthDamageMessage {Target=Id(11),Amount=-10},own);
        var data=meter.Capture(now+10*Stopwatch.Frequency);var a=data.Actors[0];
        Check(a.Damage==410 && a.NanoDamage==60 && a.PetDamage==50,"Weapon, special, nano, reflect, shield and own pet damage; subsets not added twice");
        Check(a.Healing==60,"Healing attributed to caster; received healing excluded");
        Check(data.Source=="Nexus" && data.ElapsedSeconds==10,"Native meter session clock");
        Check(a.Pets.Count==1 && a.Pets[0].CharacterId==11 && a.Pets[0].Damage==50 && a.Pets[0].NanoDamage==10,"Individual local pet records retain identity and damage subsets");
        var multi=new NativeCombatMeter();multi.Begin(1,now);
        multi.SetLocalPets(new[]{new CombatMeterPet {CharacterId=11,Name="Same pet"},new CombatMeterPet {CharacterId=12,Name="Same pet"}});
        Func<Identity,int> twoPets=id=>id.Instance==1?1:(id.Instance==11||id.Instance==12)?2:0;
        multi.Observe(new AttackInfoMessage {Identity=Id(11),Amount=40},twoPets);
        multi.Observe(new AttackInfoMessage {Identity=Id(12),Amount=70},twoPets);
        multi.Observe(new HealthDamageMessage {Target=Id(12),Amount=20},twoPets);
        var multiActor=multi.Capture(now).Actors[0];
        Check(multiActor.Damage==110 && multiActor.Pets.Count==2 && multiActor.Pets[0].Damage==40 && multiActor.Pets[1].Damage==70 && multiActor.Pets[1].Healing==20,"Same-name pets stay distinct and are included in owner totals exactly once");
        multi.SetLocalPets(new[]{new CombatMeterPet {CharacterId=12,Name="Renamed pet"}});
        Check(!multi.Capture(now).Actors[0].Pets[0].Active && multi.Capture(now).Actors[0].Pets[1].Active && multiActor.Pets[1].Name=="Same pet","Current roster retires old pets and published names and counters remain immutable");
        meter.Feedback("You received 123 xp.");meter.Feedback("You received 123 xp.");
        meter.Feedback("You gained 2191 new Alien Experience Points.");meter.Feedback("You gained 42 points of Shadowknowledge.");
        meter.Feedback("Someone: You received 999 xp.");meter.Feedback("You lost 100 xp.");meter.Feedback("You gained 10 points of Shadowknowledge as a side bonus.");
        meter.Feedback("You received 99999999999999999999999 xp.");meter.Feedback("You received -1 xp.");
        a=meter.Capture(now).Actors[0];
        Check(a.Xp==246 && a.Axp==2191 && a.Sk==42 && a.ExperienceEvents==4,"Exact award formats; repeated equal awards counted; losses, bonus notices, spoofed prefixes and overflow ignored");
        Check(data.Actors[0].Xp==0,"Published snapshots cannot mutate afterwards");
        meter.Observe(new AttackInfoMessage {Identity=Id(1),Amount=int.MaxValue},own);
        Check(meter.Capture(now).Actors[0].Damage==(long)int.MaxValue+410,"Long sessions exceed signed 32-bit totals safely");
        meter.Observe(new HealthDamageMessage {Target=Id(1),Amount=int.MinValue},own);
        Check(meter.Capture(now).Actors[0].Damage==(long)int.MaxValue+410,"Invalid minimum integer is rejected without overflow");
        meter.Begin(1,now+Stopwatch.Frequency);Check(meter.Capture(now).Epoch==data.Epoch,"Same character updates preserve session");
        meter.Begin(2,now+Stopwatch.Frequency);data=meter.Capture(now+2*Stopwatch.Frequency);
        Check(data.Actors[0].CharacterId==2 && data.Actors[0].Damage==0 && data.Actors[0].Xp==0,"Character switch starts an independent session");
        var nearby=new NativeCombatMeter();nearby.Begin(1,now);
        Func<Identity,NativeCombatMeter.SourceActor> resolve=id=>id==Id(20)?null:new NativeCombatMeter.SourceActor {
            CharacterId=id==Id(12)?2:id.Instance,Name=id==Id(12)?"Visitor":"Actor "+id.Instance,Profession="Doctor",Pet=id==Id(12)||id==Id(13),UnassignedPet=id==Id(13)};
        nearby.Observe(new AttackInfoMessage {Identity=Id(2),Amount=100},own,resolve);
        nearby.Observe(new AttackInfoMessage {Identity=Id(12),Amount=40},own,resolve);
        nearby.Observe(new HealthDamageMessage {Identity=Id(1),Target=Id(12),Amount=-10},own,resolve);
        nearby.Observe(new HealthDamageMessage {Identity=Id(1),Target=Id(2),Amount=30},own,resolve);
        nearby.Observe(new AttackInfoMessage {Identity=Id(13),Amount=7},own,resolve);
        nearby.Observe(new AttackInfoMessage {Identity=Id(20),Amount=999},own,resolve);
        var observed=nearby.CaptureObserved(now+10*Stopwatch.Frequency);
        Check(observed.Actors.Count==2 && observed.Actors[0].Damage==150 && observed.Actors[0].PetDamage==50 && observed.Actors[0].NanoDamage==10 && observed.Actors[0].Healing==30,
            "Nearby players and owned pets aggregate once under the owner; NPC events excluded");
        Check(observed.Actors[1].UnassignedPet && observed.Actors[1].PetDamage==7,"Unknown pet owners remain explicit separate rows");
        Check(observed.Actors[0].Pets.Count==1 && observed.Actors[0].Pets[0].CharacterId==12 && observed.Actors[0].Pets[0].Damage==50,"All captures individual pet IDs beneath confirmed remote owners");
        Check(nearby.Capture(now).Actors[0].Damage==0,"Nearby observations never contaminate the local player's totals");
        nearby.Feedback("You received 123 xp.");
        Check(nearby.CaptureObserved(now).Actors[0].Xp==0,"Local XP is never attributed to nearby players");
        nearby.Observe(new AttackInfoMessage {Identity=Id(2),Amount=5},own,resolve);
        Check(observed.Actors[0].Damage==150,"Observer snapshots are detached from later events");
        for(int i=100;i<240;i++)nearby.Observe(new AttackInfoMessage {Identity=Id(i),Amount=1},own,resolve);
        Check(nearby.CaptureObserved(now).Actors.Count==128 && nearby.CaptureObserved(now).LimitReached,"Observed actor memory is bounded and truncation is reported");
        string prior=observed.Epoch;nearby.ResetObserved(now);
        Check(nearby.CaptureObserved(now).Epoch!=prior && nearby.CaptureObserved(now).Actors.Count==0 && !nearby.CaptureObserved(now).LimitReached && nearby.Capture(now).Actors[0].Xp==123,
            "Zoning resets nearby identities and counters without resetting local totals");
        var npc=new NativeCombatMeter();npc.Begin(1,now);
        Func<Identity,NativeCombatMeter.SourceActor> npcResolve=id=>id.Instance<=0?null:new NativeCombatMeter.SourceActor {
            CharacterId=id.Instance,Name=id.Instance>=50?"Same-name NPC":"Player",Npc=id.Instance>=50};
        npc.Observe(new AttackInfoMessage {Identity=Id(50),Target=Id(1),Amount=100},own,npcResolve);
        npc.Observe(new AttackInfoMessage {Identity=Id(1),Target=Id(50),Amount=200},own,npcResolve);
        npc.Observe(new SpecialAttackInfoMessage {Identity=Id(1),Target=Id(50),Amount=30},own,npcResolve);
        npc.Observe(new HealthDamageMessage {Target=Id(50),Identity=Id(1),Amount=-40},own,npcResolve);
        npc.Observe(new HealthDamageMessage {Target=Id(1),Identity=Id(50),Amount=-60},own,npcResolve);
        npc.Observe(new ReflectAttackMessage {Target=Id(50),Identity=Id(1),Amount=7},own,npcResolve);
        npc.Observe(new ShieldAttackMessage {Target=Id(1),Identity=Id(50),Amount=3},own,npcResolve);
        npc.Observe(new HealthDamageMessage {Target=Id(50),Identity=Id(50),Amount=25},own,npcResolve);
        npc.Observe(new AttackInfoMessage {Identity=Id(50),Target=Id(51),Amount=10},own,npcResolve);
        npc.Observe(new AttackInfoMessage {Identity=Id(0),Target=Id(51),Amount=15},own,npcResolve);
        var npcData=npc.CaptureObserved(now+10*Stopwatch.Frequency,true);
        Check(npcData.Source=="NexusNPC" && npcData.Actors.Count==2,"NPCs with the same name retain separate identities");
        Check(npcData.Actors[0].Damage==157 && npcData.Actors[0].DamageTaken==293 && npcData.Actors[0].NanoDamage==40 && npcData.Actors[0].Healing==25,
            "Both NPC event directions include attacks, specials, nano, reflect and shield; healing is not damage taken");
        Check(npcData.Actors[1].DamageTaken==25,"NPC-to-NPC and unknown-source hits count only once at each known endpoint");
        Check(npc.CaptureObserved(now).Actors.Count==1 && npc.CaptureObserved(now).Actors[0].Damage==293 && npc.Capture(now).Actors[0].Damage==293,
            "NPC data does not contaminate the All or local player damage totals");
        for(int i=100;i<240;i++)npc.Observe(new AttackInfoMessage {Identity=Id(i),Target=Id(1),Amount=1},own,npcResolve);
        Check(npc.CaptureObserved(now,true).Actors.Count==128 && npc.CaptureObserved(now,true).LimitReached && !npc.CaptureObserved(now).LimitReached,
            "NPC limit is bounded independently from the player feed");
        npc.ResetObserved(now);Check(npc.CaptureObserved(now,true).Actors.Count==0 && !npc.CaptureObserved(now,true).LimitReached,"Zoning clears NPC identities and limits");
        Console.WriteLine("PASS: "+checks+" native meter checks; no game interaction.");return 0;
    }
    static void ProfessionLookups()
    {
        long second=Stopwatch.Frequency;
        var lookup=new NearbyProfessions();
        var requests=new System.Collections.Generic.List<int>();
        var resolved=new System.Collections.Generic.List<string>();
        Check(lookup.Remember(2,"4294967295")==null,"Unavailable profession queues lookup");
        lookup.Remember(3,"Unknown");
        lookup.Poll(0,id=>"Unknown",requests.Add,(id,p)=>resolved.Add(p));
        Check(requests.Count==1,"One request per poll across unknown players");
        lookup.Poll(second,id=>"Unknown",requests.Add,(id,p)=>resolved.Add(p));
        Check(requests.Count==1,"Global request cooldown");
        lookup.Poll(2*second,id=>"Unknown",requests.Add,(id,p)=>resolved.Add(p));
        Check(requests.Count==2 && requests[0]!=requests[1],"Other unknown player can use next request slot");
        lookup.Poll(3*second,id=>"Doctor",requests.Add,(id,p)=>resolved.Add(p));
        Check(resolved.Count==2 && requests.Count==2,"Delayed professions resolve without new combat or another request");
        Check(lookup.Remember(2,"Unknown")=="Doctor","Unknown value cannot erase cached profession");
        lookup.Clear();requests.Clear();resolved.Clear();lookup.Remember(2,"Unknown");
        lookup.Poll(0,id=>null,requests.Add,(id,p)=>resolved.Add(p));
        Check(requests.Count==0,"Absent character receives no request");
        lookup.Poll(0,id=>"Unknown",requests.Add,(id,p)=>resolved.Add(p));
        lookup.Poll(4*second,id=>"Unknown",requests.Add,(id,p)=>resolved.Add(p));
        Check(requests.Count==1,"Per-player retry interval");
        lookup.Poll(5*second,id=>"Unknown",requests.Add,(id,p)=>resolved.Add(p));
        lookup.Poll(10*second,id=>"Unknown",requests.Add,(id,p)=>resolved.Add(p));
        lookup.Poll(60*second,id=>"Unknown",requests.Add,(id,p)=>resolved.Add(p));
        Check(requests.Count==3,"Three-attempt cap even without server reply");
        lookup.Poll(61*second,id=>"Engineer",requests.Add,(id,p)=>resolved.Add(p));
        Check(resolved.Count==1,"Late reply is accepted after retry cap");
        lookup.Clear();Check(lookup.Remember(2,"Unknown")==null,"Zoning clears profession cache");
        int reads=0;for(int id=3;id<300;id++)lookup.Remember(id,"Unknown");
        lookup.Poll(0,id=>{reads++;return null;},requests.Add,(id,p)=>{});
        Check(reads==128,"Lookup state is bounded");
        Check(!NearbyProfessions.Valid("Monster") && !NearbyProfessions.Valid("-1")
            && !NearbyProfessions.Valid("4294967295") && !NearbyProfessions.Valid("Unknown"),"NPC and unavailable professions are rejected");
        var meter=new NativeCombatMeter();meter.Begin(1,0);
        meter.Observe(new AttackInfoMessage {Identity=Id(2),Target=Id(1),Amount=25},id=>0,
            id=>new NativeCombatMeter.SourceActor {CharacterId=id.Instance,Name="Player"});
        meter.SetObservedProfession(2,"Doctor");meter.SetObservedProfession(2,"Unknown");
        var row=meter.CaptureObserved(second).Actors[0];
        Check(row.Profession=="Doctor" && row.Damage==25,"Resolved profession updates existing meter row without changing counters");
        meter.ResetObserved(second);meter.SetObservedProfession(2,"Doctor");
        Check(meter.CaptureObserved(second).Actors.Count==0,"Late metadata cannot recreate old zone rows");
    }
}
