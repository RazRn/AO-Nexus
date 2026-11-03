using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NexusTelemetry;

namespace Nexus.Tests
{
    [TestClass]
    public class SyncSafetyTests
    {
        [TestMethod]
        public void BriefSpikesAndNormalSyncJitterNeverWarn()
        {
            var policy=new SyncSeparationPolicy();
            for(int i=0;i<100;i++)Assert.IsNull(policy.Observe(i*.2,i%4==0?12:3,2.7,true));
        }
        [TestMethod]
        public void SustainedSeparationWarnsBeforeStoppingAndDoesNotSpam()
        {
            var policy=new SyncSeparationPolicy();
            Assert.IsNull(policy.Observe(0,9,2.7,true));
            Assert.IsNull(policy.Observe(1,14,2.7,true));
            Assert.IsNull(policy.Observe(1.9,18,2.7,true));
            Assert.AreEqual("Warning",policy.Observe(2,19,2.7,true));
            Assert.IsNull(policy.Observe(3,22,2.7,true));
            Assert.IsNull(policy.Observe(3.9,25,2.7,true));
            Assert.AreEqual("Stopped",policy.Observe(4,29,2.7,true));
            for(int i=5;i<50;i++)Assert.IsNull(policy.Observe(i,30,2.7,true));
        }
        [TestMethod]
        public void StationaryAndShortDistancesDoNotTriggerStop()
        {
            var policy=new SyncSeparationPolicy();
            policy.Observe(0,9,2.7,true);
            policy.Observe(1,10,2.7,true);
            Assert.AreEqual("Warning",policy.Observe(2,12,2.7,true));
            Assert.IsNull(policy.Observe(10,14,2.7,true));
            Assert.IsNull(policy.Observe(11,25,2.7,false));
        }
        [TestMethod]
        public void ManualControlOrZoningResetRequiresFreshGracePeriod()
        {
            var policy=new SyncSeparationPolicy();
            policy.Observe(0,20,2.7,true);policy.Observe(1,25,2.7,true);policy.Observe(2,30,2.7,true);
            policy.Reset();
            Assert.IsNull(policy.Observe(10,30,2.7,true));
            Assert.IsNull(policy.Observe(11,30,2.7,true));
            Assert.AreEqual("Warning",policy.Observe(12,30,2.7,true));
        }
        [TestMethod]
        public void RegroupingRearmsOnlyAfterReturningInsideHysteresis()
        {
            var policy=new SyncSeparationPolicy();
            for(int i=0;i<=4;i++)policy.Observe(i,30,2.7,true);
            Assert.IsNull(policy.Observe(5,3,2.7,false));
            Assert.IsNull(policy.Observe(10,20,2.7,true));
            Assert.IsNull(policy.Observe(11,20,2.7,true));
            Assert.AreEqual("Warning",policy.Observe(12,20,2.7,true));
        }
        [TestMethod]
        public void ClientStallDoesNotCountAsObservedSeparation()
        {
            var policy=new SyncSeparationPolicy();
            policy.Observe(0,20,2.7,true);
            policy.Observe(1,20,2.7,true);
            Assert.IsNull(policy.Observe(10,30,2.7,true));
            Assert.IsNull(policy.Observe(11,30,2.7,true));
            Assert.AreEqual("Warning",policy.Observe(12,30,2.7,true));
            Assert.IsNull(policy.Observe(20,30,2.7,true));
            for(int i=21;i<24;i++)Assert.IsNull(policy.Observe(i,30,2.7,true));
            Assert.AreEqual("Stopped",policy.Observe(24,30,2.7,true));
            Assert.IsNull(policy.Observe(50,30,2.7,true));
        }
        [TestMethod]
        public void WaypointContainsOnlyNumericCommandArgumentsAndSanitizedLabels()
        {
            var text=RallyLink.Create("Doc\"<bad>\n","ICC & /quit",1234.6,789.4,655,false);
            StringAssert.Contains(text,"chatcmd:///waypoint 1235 789 655");
            Assert.IsFalse(text.Contains("<bad>"));Assert.IsFalse(text.Contains("\n"));
            Assert.IsTrue(text.All(c=>c<128),"Game chat separators must survive ASCII transport");
            Assert.AreEqual(1,text.Split(new[]{"chatcmd:///"},StringSplitOptions.None).Length-1);
            Assert.IsNull(RallyLink.Create("Doc","ICC",double.NaN,2,655,false));
        }
    }
}
