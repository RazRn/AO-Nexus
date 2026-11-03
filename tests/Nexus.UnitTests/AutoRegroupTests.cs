using System;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using AOLauncher;
using NexusPlayTools;
using NexusTelemetry;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Nexus.UnitTests
{
    [TestClass]
    public class AutoRegroupTests
    {
        [TestMethod]
        public void PermissionsRemainCompleteDuringAtomicReplacement()
        {
            string folder=Path.Combine(Path.GetTempPath(),"NexusRegroup-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string path=Path.Combine(folder,"permission.json");
            try {
                Assert.IsTrue(AutoRegroupPermission.Write(path,new AutoRegroupPermission {Character=173,Leader=170}));
                var writer=Task.Run(()=> {
                    for(int i=0;i<500;i++) {
                        AutoRegroupPermission.Write(path,new AutoRegroupPermission {Character=i%2==0?173:170,Leader=i%2==0?170:173});
                    }
                });
                try {
                    for(int i=0;i<1000;i++) {
                        var permission=AutoRegroupPermission.Read(path);
                        if(permission!=null)Assert.AreEqual(343,permission.Character+permission.Leader);
                    }
                } finally { writer.GetAwaiter().GetResult(); }
                Assert.IsNotNull(AutoRegroupPermission.Read(path));
                File.WriteAllText(path,new string(' ',8193));
                Assert.IsNull(AutoRegroupPermission.Read(path));
                Assert.IsNull(AutoRegroupPermission.Read(Path.Combine(folder,"missing.json")));
            } finally { Directory.Delete(folder,true); }
        }

        [TestMethod]
        public void ClearRouteReturnsAndLatchesArrival()
        {
            var progress=new AutoRegroupProgress();progress.Start(0,20);
            for(int i=1;i<19;i++)Assert.AreEqual("Regrouping",progress.Observe(i*.2,20-i,1.5,true));
            Assert.AreEqual("Regrouped",progress.Observe(3.8,1,1.5,true));
            Assert.AreEqual("Regrouped",progress.Observe(4,20,1.5,true));
        }
        [TestMethod]
        public void ObstructionAndStalledMovementStopWithoutRetrying()
        {
            var progress=new AutoRegroupProgress();progress.Start(0,20);
            Assert.AreEqual("Path obstructed",progress.Observe(.2,20,1.5,false));
            Assert.AreEqual("Path obstructed",progress.Observe(.4,19,1.5,true));
            progress.Start(1,20);
            for(int i=1;i<20;i++)Assert.AreEqual("Regrouping",progress.Observe(1+i*.2,20,1.5,true));
            Assert.AreEqual("No progress toward leader",progress.Observe(5,20,1.5,true));
            Assert.AreEqual("No progress toward leader",progress.Observe(5.2,19,1.5,true));
        }
        [TestMethod]
        public void SlowProgressCannotRunIndefinitely()
        {
            var progress=new AutoRegroupProgress();progress.Start(0,40);
            for(int i=1;i<100;i++)Assert.AreEqual("Regrouping",progress.Observe(i*.2,40-i*.1,1.5,true));
            Assert.AreEqual("Regroup timed out",progress.Observe(20,30,1.5,true));
        }
        [TestMethod]
        public void FrozenLoopAndInvalidPositionsCannotResumeRecovery()
        {
            var progress=new AutoRegroupProgress();progress.Start(0,20);
            Assert.AreEqual("Client update delayed",progress.Observe(1.1,19,1.5,true));
            progress.Start(2,20);
            Assert.AreEqual("Client update delayed",progress.Observe(1,19,1.5,true));
            progress.Start(2,20);
            Assert.AreEqual("Invalid position",progress.Observe(2.2,double.NaN,1.5,true));
            progress.Start(2,20);
            Assert.AreEqual("Leader too far away",progress.Observe(2.2,61,1.5,true));
        }
        static AutoRegroupPermission Grant(long now)=>new AutoRegroupPermission {
            Pid=1,StartTicks=100,Character=10,Leader=20,LeaderPid=2,LeaderStartTicks=200,
            IssuedQpc=now,LeaderUpdateQpc=now,Enabled=true,Area=42,Channel=1,Members=new[]{10,20}};
        [TestMethod]
        public void PermissionRequiresFreshLeaderAndExactProcessLifetime()
        {
            long now=Stopwatch.GetTimestamp();var grant=Grant(now);
            Assert.IsTrue(grant.Fresh(now,1,100));
            Assert.IsFalse(grant.Fresh(now,1,101));Assert.IsFalse(grant.Fresh(now,3,100));
            Assert.IsFalse(grant.Fresh(now+3*Stopwatch.Frequency,1,100));
            grant.LeaderUpdateQpc=now-2*Stopwatch.Frequency;Assert.IsFalse(grant.Fresh(now,1,100));
            grant.LeaderUpdateQpc=now+1;Assert.IsFalse(grant.Fresh(now,1,100));
            grant=Grant(now);grant.IssuedQpc=now+1;Assert.IsFalse(grant.Fresh(now,1,100));
        }
        [TestMethod]
        public void PermissionRejectsInvalidRosterOrSelfLeadership()
        {
            long now=Stopwatch.GetTimestamp();var grant=Grant(now);
            grant.Members=new[]{10,10,20};Assert.IsFalse(grant.Fresh(now,1,100));
            grant.Members=new[]{10,30};Assert.IsFalse(grant.Fresh(now,1,100));
            grant=Grant(now);grant.Leader=10;Assert.IsFalse(grant.Fresh(now,1,100));
            grant=Grant(now);grant.Enabled=false;Assert.IsFalse(grant.Fresh(now,1,100));
        }
        static Tuple<ClientObservation,FormationReport>[] Team()
        {
            long now=Stopwatch.GetTimestamp();
            return new[]{1,2}.Select(id=>Tuple.Create(new ClientObservation {
                Pid=id,StartTicks=id*100,
                Pulse=new ClientPulse {Active=true,LastUpdateQpc=now,TeamStateQpc=now,IsTeamLeader=id==1,
                    Movement=new MovementSnapshot {Qpc=now,CharacterId=id*10,Playfield=42,Channel=1,AutoRegroupSupported=true}}},
                new FormationReport {Pid=id,StartTicks=id*100,Character=id*10,Playfield=42,Channel=1,
                    UtcTicks=DateTime.UtcNow.Ticks,Scope="same-server",Ready=true,Members=new[]{10,20}})).ToArray();
        }
        [TestMethod]
        public void OnlyFollowerOfConfirmedLeaderReceivesPermission()
        {
            var team=Team();var grants=TeamWindow.BuildRegroupPermissions(team,true);
            Assert.IsFalse(grants[0].Enabled);Assert.IsTrue(grants[1].Enabled);Assert.AreEqual(10,grants[1].Leader);
            Assert.IsFalse(TeamWindow.BuildRegroupPermissions(team,false).Any(g=>g.Enabled));
            team[1].Item1.Pulse.IsTeamLeader=true;
            Assert.IsFalse(TeamWindow.BuildRegroupPermissions(team,true).Any(g=>g.Enabled));
        }
        [TestMethod]
        public void ChangedZoneRosterOrProcessRevokesPermission()
        {
            Action<Action<Tuple<ClientObservation,FormationReport>[]>> rejected=mutate=>{
                var team=Team();mutate(team);
                Assert.IsFalse(TeamWindow.BuildRegroupPermissions(team,true).Any(g=>g.Enabled));
            };
            rejected(t=>t[1].Item2.Scope="other-server");
            rejected(t=>t[1].Item2.Members=new[]{20,30});
            rejected(t=>t[1].Item2.StartTicks++);
            rejected(t=>t[0].Item2.StartTicks++);
            rejected(t=>t[1].Item2.Character=30);
            rejected(t=>t[1].Item1.Pulse.Zoning=true);
            rejected(t=>t[0].Item1.Pulse.Zoning=true);
            rejected(t=>t[0].Item1.Pulse.Movement.Playfield=43);
            rejected(t=>t[0].Item1.Pulse.TeamStateQpc=1);
            rejected(t=>t[0].Item1.Pulse.LastUpdateQpc=1);
            rejected(t=>t[1].Item1.Pulse.Movement.AutoRegroupSupported=false);
            rejected(t=>t[1].Item2.Active=true);
        }
        [TestMethod]
        public void BlockedLocationUsesClickableWaypointAndSanitizesText()
        {
            var link=RallyLink.Create("Geek","ICC",123.4,456.7,42,"Stuck while regrouping");
            StringAssert.Contains(link,"Stuck while regrouping");
            StringAssert.Contains(link,"chatcmd:///waypoint 123 457 42");
            var escaped=RallyLink.Create("<bad>","ICC",1,2,42,"<script>\n");
            Assert.IsFalse(escaped.Contains("<script>"));Assert.IsFalse(escaped.Contains("<bad>"));
            Assert.IsNull(RallyLink.Create("Geek","ICC",double.NaN,2,42,"Stuck"));
        }
    }
}
