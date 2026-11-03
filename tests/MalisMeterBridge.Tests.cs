using System;
using System.Diagnostics;
using NexusTelemetry;
using MalisDamageMeter;
static class MalisMeterBridgeTests {
    static int checks;
    static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
    static int Main(){try{Run();Console.WriteLine("PASS: "+checks+" read-only meter adapter checks using a synthetic DLL; no toolkit or game executed.");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
    static void Run(){
        var bridge=new MalisMeterBridge();long now=Stopwatch.GetTimestamp();
        var player=new PlayerCharData();player.DamageSources.Total=1000;player.DamageSources.PetTotal=100;player.DamageSources.Nanobots.Total=300;player.HealSource.Total=250;
        HitRegisters.Characters.Add(123,player);HitRegisters.Characters.Add(999,new PetData());
        MalisDamageMeter.Main.Window.ViewSettings.Seconds=10;
        var first=bridge.Capture(now);
        Check(first.Status=="Ready" && first.Actors.Count==1,"Only player records are exported");
        Check(first.Actors[0].Damage==1000 && first.Actors[0].PetDamage==100 && first.Actors[0].NanoDamage==300 && first.Actors[0].Healing==250,"Public inherited counters are read independently");
        Check(first.ElapsedSeconds==10 && !first.Paused && first.ObservedQpc==now,"Meter clock and sample time are preserved");
        MalisDamageMeter.Main.Window.ViewSettings.IsPaused=true;
        Check(bridge.Capture(now+1).Paused,"Pause is reported without toggling the plugin");
        player.DamageSources.Total=1100;
        Check(bridge.Capture(now+2).Epoch==first.Epoch && first.Actors[0].Damage==1000,"Snapshot copies remain immutable as plugin counters advance");
        MalisDamageMeter.Main.Window.ViewSettings.Seconds=0;player.DamageSources.Total=500;
        var reset=bridge.Capture(now+3);Check(reset.Epoch!=first.Epoch,"Underlying timer/counter resets establish a new measurement epoch");
        HitRegisters.Characters.Clear();Check(bridge.Capture(now+4).Epoch!=reset.Epoch,"Clearing a meter invalidates previous baselines");
        HitRegisters.Characters.Add(123,player);player.DamageSources.Total=-1;
        Check(bridge.Capture(now+5).Status=="Unavailable","Negative overflowed totals are not exported as valid damage");
        HitRegisters.Characters.Clear();for(int i=1;i<=129;i++)HitRegisters.Characters.Add(i,new PlayerCharData());
        Check(bridge.Capture(now+6).Status=="TooManyEntries","Payload size is bounded and not silently truncated");
        HitRegisters.Characters.Clear();MalisDamageMeter.Main.Window=null;
        Check(bridge.Capture(now+7).Status=="NotReady","A plugin without initialized UI is not reported ready");
    }
}
