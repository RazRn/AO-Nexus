using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Web.Script.Serialization;

// Launches only the inert TelemetryGameFixture, never a game client.
static class NpcTransportTests
{
    static int checks;
    static object Get(object value,string key)=>value.GetType().GetProperty(key).GetValue(value,null);
    static void Check(bool value,string label){if(!value)throw new Exception(label);checks++;}
    static int Main(string[] args)
    {
        var assembly=Assembly.LoadFrom(Path.GetFullPath(args[0]));
        var read=assembly.GetType("AOLauncher.ClientMonitor").GetMethod("ReadPulse",BindingFlags.Static|BindingFlags.NonPublic);
        var fixture=new Process {StartInfo=new ProcessStartInfo(Path.GetFullPath(args[1])) {UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true}};
        string path=null,large=Path.Combine(Path.GetTempPath(),"NexusNpcSize-"+Guid.NewGuid().ToString("N")+".json");
        try {
            fixture.Start();string[] identity=fixture.StandardOutput.ReadLine().Split(' ');
            path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AONexus","Telemetry",identity[0]+"-"+identity[1]+".json");
            fixture.StandardInput.WriteLine("nearby-combat");fixture.StandardInput.Flush();
            object pulse=null;var timeout=Stopwatch.StartNew();
            while(timeout.Elapsed.TotalSeconds<10) {
                Thread.Sleep(100);if(!File.Exists(path))continue;
                pulse=read.Invoke(null,new object[]{path});
                if(pulse!=null && Get(pulse,"NpcCombatMeter")!=null && ((IList)Get(Get(pulse,"NpcCombatMeter"),"Actors")).Count==1)break;
            }
            Check(pulse!=null && Get(pulse,"NpcCombatMeter")!=null,"Production reader accepts NPC feed");
            var npcs=(IList)Get(Get(pulse,"NpcCombatMeter"),"Actors");Check(npcs.Count==1,"Only known NPC appears in NPC feed");
            Check((long)Get(npcs[0],"Damage")==1004 && (long)Get(npcs[0],"DamageTaken")==168,"Both directions and NPC-owned pets survive production resolver, event endpoints, writer and reader");
            Check((long)Get(npcs[0],"PetDamage")==5,"Explicit NPC-owned pet stays attributed to its NPC owner");
            var players=(IList)Get(Get(pulse,"ObservedCombatMeter"),"Actors");Check(players.Count==5,"Player roster and unflagged pet families/types stay separate from NPCs");
            object visitor=null;foreach(var actor in players)if((int)Get(actor,"CharacterId")==20)visitor=actor;
            Check(visitor!=null && (long)Get(visitor,"Damage")==356 && (long)Get(visitor,"PetDamage")==111,"Remote player pets without a Pet flag stay attributed to player owners");
            foreach(var actor in players)if((int)Get(actor,"CharacterId")==25 || (int)Get(actor,"CharacterId")==26)
                Check((bool)Get(actor,"UnassignedPet") && (long)Get(actor,"PetDamage")>0,"Pet family/type without an owner remains an explicit unknown-owner pet");
            Check((long)Get(((IList)Get(Get(pulse,"CombatMeter"),"Actors"))[0],"Damage")==123,"Incoming NPC hits do not enter local outgoing damage");
            // Max-length names and full independent buckets exceed the old 32 KiB ceiling.
            var json=new JavaScriptSerializer();var data=json.DeserializeObject(json.Serialize(pulse)) as System.Collections.Generic.Dictionary<string,object>;
            foreach(string key in new[]{"NpcCombatMeter","ObservedCombatMeter"}) {
                var snapshot=(System.Collections.Generic.Dictionary<string,object>)data[key];var actors=new object[128];
                for(int i=0;i<actors.Length;i++)actors[i]=new {CharacterId=i+1,Name=new string('\u754c',96),Profession=new string('x',32),Damage=123L,DamageTaken=456L};
                snapshot["Actors"]=actors;
            }
            File.WriteAllText(large,json.Serialize(data));Check(new FileInfo(large).Length>32768,"Fixture exercises expanded snapshot size");
            Check(read.Invoke(null,new object[]{large})!=null,"Both full feeds remain readable without losing health data");
            File.WriteAllText(large,new string(' ',512*1024));Check(read.Invoke(null,new object[]{large})==null,"Oversized snapshots remain rejected");
            Console.WriteLine("PASS: "+checks+" NPC production event/transport/size checks; no game interaction.");return 0;
        } finally {
            if(!fixture.HasExited){fixture.StandardInput.WriteLine("quit");fixture.StandardInput.Flush();if(!fixture.WaitForExit(3000))fixture.Kill();}
            fixture.Dispose();foreach(var file in new[]{path,path==null?null:path+".tmp",large})if(file!=null && File.Exists(file))File.Delete(file);
        }
    }
}
