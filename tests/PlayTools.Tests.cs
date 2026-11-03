using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using NexusPlayTools;
using NexusShortcuts;

static class PlayToolsTests
{
    static int count;
    static void Check(bool ok,string message) { if(!ok) throw new Exception(message); count++; }
    static void Reject(Action action,string message) { bool rejected=false;try{action();}catch{rejected=true;}Check(rejected,message); }
    static int Main()
    {
        string directory=Path.Combine(Path.GetTempPath(),"NexusPlayToolsTests-"+Guid.NewGuid().ToString("N"));
        try {
            var now=new DateTime(2026,10,30,12,0,0,DateTimeKind.Utc);
            Directory.CreateDirectory(directory);
            string snapshot=Path.Combine(directory,"snapshot.json");
            FormationMailbox.Write(snapshot,new FormationReport {Character=173});
            var writer=Task.Run(()=> {for(int i=0;i<200;i++)FormationMailbox.Write(snapshot,new FormationReport {Character=173,Channel=i});});
            var readers=Enumerable.Range(0,6).Select(n=>Task.Run(()=> {
                for(int i=0;i<300;i++)if(FormationMailbox.Read<FormationReport>(snapshot).Character!=173)throw new Exception("Torn formation snapshot");
            })).ToArray();
            Task.WaitAll(readers.Concat(new[]{writer}).ToArray());Check(true,"Six readers can read while formation snapshots are atomically replaced");
            var t=new BossClock {Name="Fixture boss",Scope="fixture",Seconds=3600,WarningSeconds=600};
            var tara=new BossClock {Name="Tara",Scope="fixture",Seconds=3660,WarningSeconds=600,ObserveDeaths=true};
            tara.Validate();Check(true,"61-minute observed boss with 10-minute warning is valid");
            tara.Repeat=true;
            try { tara.Validate();throw new Exception("Fixed repetition accepted"); }
            catch(InvalidDataException ex) { Check(ex.Message.Contains("Reset"),"Fixed repetition explains the incompatible timer type"); }
            Check(t.Status(now)=="Not started","new timers are inactive");t.Start(now,"manual fixture");
            Check(t.Status(now)=="01:00:00","countdown starts");Check(t.Remaining(now.AddHours(1))==0,"exact expiry");
            Check(t.Status(now.AddHours(1)).Contains("unconfirmed"),"elapsed is not a spawn claim");t.Kind=TimerKind.ChanceWindow;
            Check(t.Status(now.AddHours(2)).Contains("Possibly"),"chance remains unknown after window");
            var store=new ClockStore(directory);store.Edit(d=>d.Timers.Add(t));
            Check(store.Read().Timers.Single().Id==t.Id,"persistence roundtrip");
            Check(store.ClaimAlerts(now.AddMinutes(49)).Count==0,"no early notification");
            Check(store.ClaimAlerts(now.AddMinutes(50)).Count==1,"warning once");
            Check(new ClockStore(directory).ClaimAlerts(now.AddMinutes(51)).Count==0,"warning deduplicates across store instances");
            Check(store.ClaimAlerts(now.AddHours(1)).Count==1,"due notification");
            Check(store.ClaimAlerts(now.AddDays(1)).Count==0,"no repeated expired notification");
            store.Edit(d=>d.Timers[0].Start(now.AddDays(1),"second"));
            Check(store.ClaimAlerts(now.AddDays(1).AddHours(1)).Count==1,"restart rearms alerts");
            store.Edit(d=> {var r=d.Timers[0];r.Kind=TimerKind.Reset;r.Repeat=true;r.Seconds=86400;r.Start(now,"daily");});
            Check(store.ClaimAlerts(now.AddDays(4).AddHours(1)).Count==1,"offline catchup alerts once");
            Check(store.Read().Timers[0].DueUtcTicks==now.AddDays(5).Ticks,"repeat advances to future without drift");
            Reject(()=>{t.Seconds=double.NaN;t.Validate();},"NaN duration rejected");t.Seconds=3600;
            Reject(()=>{t.Name="bad\tname";t.Validate();},"native row delimiters forbidden");t.Name="Fixture boss";
            Reject(()=>{t.Kind=TimerKind.Personal;t.ObserveDeaths=true;t.Validate();},"death detection cannot reset personal cooldown");t.ObserveDeaths=false;
            string a=ToolFiles.Scope(Path.Combine(directory,"serverA","Prefs","accountA","Char166"));
            Check(a==ToolFiles.Scope(Path.Combine(directory,"serverA","Prefs","accountB","Char167")),"local accounts share a server scope");
            Check(a!=ToolFiles.Scope(Path.Combine(directory,"serverB","Prefs","accountA","Char166")),"same IDs on other servers are separate");
            Parallel.For(0,8,i=>new ClockStore(directory).Edit(d=>d.Timers.Add(new BossClock{Name="Concurrent "+i,Scope="fixture"})));
            Check(store.Read().Timers.Count==9,"concurrent edits preserve timers");
            var o=new FormationOptions{Shape=FormationShape.Wedge,Slot=1,Side=3,Row=4,Behind=5};
            var p=FormationGeometry.Target(o,100,100,0,1);Check(p.X==97&&p.Z==95,"north-facing wedge");
            p=FormationGeometry.Target(o,100,100,1,0);Check(p.X==95&&p.Z==103,"east-facing rotation");
            o.Slot=2;p=FormationGeometry.Offset(o);Check(p.X==3&&p.Z==-5,"stable opposite slot");
            o.Slot=3;p=FormationGeometry.Offset(o);Check(p.X==-6&&p.Z==-9,"next rank spacing");
            o.Shape=FormationShape.Column;p=FormationGeometry.Offset(o);Check(p.X==0&&p.Z==-13,"column spacing");
            foreach(FormationShape shape in Enum.GetValues(typeof(FormationShape))) {
                o.Shape=shape;var points=Enumerable.Range(1,6).Select(i=>{o.Slot=i;return FormationGeometry.Offset(o);}).ToArray();
                Check(points.Select(v=>v.X+":"+v.Z).Distinct().Count()==6,"distinct slots "+shape);
            }
            o.Shape=FormationShape.Wedge;o.Slot=1;o.Side=float.NaN;Reject(()=>o.Validate(),"invalid spacing rejected");o.Side=3;
            Reject(()=>FormationGeometry.Target(o,0,0,0,0),"unknown heading rejected");
            var beacon=new FormationBeacon{Active=true,UtcTicks=now.Ticks};Check(beacon.Fresh(now.AddSeconds(1)),"leader fresh");
            Check(!beacon.Fresh(now.AddSeconds(2))&&!beacon.Fresh(now.AddSeconds(-1)),"stale and future leader rejected");
            Check(Marshal.SizeOf(typeof(ToolsState))==2324&&Marshal.SizeOf(typeof(ToolsCommand))==260,"native ABI matches");
            var request=new FormationBeacon {Active=true, UtcTicks=now.Ticks,SessionUtc=now.Ticks,Session=Guid.NewGuid().ToString("N"),Leader=173,LeaderName="Leader",Pid=123,StartTicks=1,Channel=7,Members=new[]{173,166,180}};
            var team=new[]{180,173,166};
            Check(request.SlotFor(166,7,team,now,now.Ticks)==1,"automatic first slot");
            Check(request.SlotFor(180,7,team,now,now.Ticks)==2,"automatic stable second slot");
            Check(request.SlotFor(173,7,team,now,now.Ticks)==0,"leader never follows itself");
            Check(request.SlotFor(166,8,team,now,now.Ticks)==0,"different sync channel excluded");
            Check(request.SlotFor(99,7,new[]{173,99},now,now.Ticks)==0,"nonparticipant excluded");
            Check(request.SlotFor(166,7,new[]{166,180},now,now.Ticks)==0,"departed leader excluded");
            Check(request.SlotFor(166,7,team,now.AddSeconds(3),now.Ticks)==0,"stale automatic leader excluded");
            Check(request.SlotFor(166,7,team,now,now.Ticks+1)==0,"no auto-start after plugin reload");
            request.Active=false;Check(request.SlotFor(166,7,team,now,now.Ticks)==0,"stopped plan excluded");request.Active=true;
            request.Members=new[]{173,166,166};Check(request.SlotFor(166,7,team,now,now.Ticks)==0,"duplicate slots rejected");
            string xml="<root><View><OptionCheckBox label='Movement' opt_variable='SyncManager__SyncMove'/><OptionCheckBox opt_variable='SyncManager__SyncCombat'/></View></root>";
            string extended=SyncFormationControls.ExtendXml(xml);
            var doc=System.Xml.Linq.XDocument.Parse(extended);
            Check(doc.Descendants().Count(e=>(string)e.Attribute("name")=="NexusFormationType")==1,"formation selector inserted once");
            Check(doc.Descendants().Any(e=>(string)e.Attribute("opt_variable")=="SyncManager__SyncCombat"),"existing sync settings preserved");
            Check(SyncFormationControls.ExtendXml(extended)==extended,"classic extension idempotent");
            Reject(()=>SyncFormationControls.ExtendXml("<root/>"),"unknown layout left unchanged");
            Console.WriteLine("PASS: "+count+" timer persistence, notifications, concurrency, scopes, formations and ABI checks; no game interaction.");return 0;
        } catch(Exception e) {Console.Error.WriteLine(e);return 1;}
        finally {
            var target=Path.GetFullPath(directory);var parent=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(target.StartsWith(parent,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(target).StartsWith("NexusPlayToolsTests-")&&Directory.Exists(target))Directory.Delete(target,true);
        }
    }
}
