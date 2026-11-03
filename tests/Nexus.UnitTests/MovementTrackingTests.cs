using System;
using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NexusTelemetry;

namespace AOLauncher
{
    [TestClass]
    public class MovementTrackingTests
    {
        static readonly long Now=100*Stopwatch.Frequency;
        static ClientObservation Client(int id,float x=0)=>new ClientObservation {
            Pid=id,StartTicks=id,
            Client=new PresetCamping.Client {CharacterId=id,Server="server",Port=7500},
            Pulse=new ClientPulse {Active=true,Movement=new MovementSnapshot {
                Qpc=Now,CharacterId=id,Adapter="AOSP",Enabled=true,SendMovement=true,Channel=4,
                Playfield=100,X=x,RunSpeed=100,VisiblePlayers=new[]{1,2}}}
        };
        [TestMethod] public void RangeUsesFollowerRunSpeedAndDoesNotClaimDelivery()
        {
            var leader=Client(1);var follower=Client(2,2);
            var result=MovementTracking.Assess(follower,leader,Now);
            Assert.AreEqual("InRange",result.Code);Assert.AreEqual(4d,result.Limit.Value);
            Assert.IsTrue(result.CanRegroup);
            follower.Pulse.Movement.X=3.5f;
            Assert.AreEqual("NearLimit",MovementTracking.Assess(follower,leader,Now).Code);
            follower.Pulse.Movement.X=5;
            Assert.AreEqual("BeyondRange",MovementTracking.Assess(follower,leader,Now).Code);
            follower.Pulse.Movement.RunSpeed=300;
            Assert.AreEqual("InRange",MovementTracking.Assess(follower,leader,Now).Code);
        }
        [TestMethod] public void StaleMissingAndInvalidCoordinatesCannotAllowRegroup()
        {
            var leader=Client(1);var follower=Client(2);
            follower.Pulse.Movement.Qpc=Now-4*Stopwatch.Frequency;
            Assert.AreEqual("Unavailable",MovementTracking.Assess(follower,leader,Now).Code);
            follower.Pulse.Movement.Qpc=Now+1;
            Assert.IsFalse(MovementTracking.Assess(follower,leader,Now).CanRegroup);
            follower.Pulse.Movement.Qpc=Now;follower.Pulse.Movement.CharacterId=99;
            Assert.IsFalse(MovementTracking.Assess(follower,leader,Now).CanRegroup);
            follower.Pulse.Movement.CharacterId=2;
            follower.Pulse.Movement.Qpc=Now;follower.Pulse.Movement.X=float.NaN;
            Assert.IsFalse(MovementTracking.Assess(follower,leader,Now).CanRegroup);
            follower.Pulse.Movement=null;
            Assert.AreEqual("Unavailable",MovementTracking.Assess(follower,leader,Now).Code);
            follower.Pulse=null;
            Assert.AreEqual("Unavailable",MovementTracking.Assess(follower,leader,Now).Code);
            Assert.AreEqual("Unavailable",MovementTracking.Assess(null,leader,Now).Code);
        }
        [TestMethod] public void ScopeAndZoningPreventCrossAreaActions()
        {
            var leader=Client(1);var follower=Client(2);
            follower.Client.Server="other";
            Assert.AreEqual("OtherServer",MovementTracking.Assess(follower,leader,Now).Code);
            follower.Client.Server="server";follower.Pulse.Movement.Channel=5;
            Assert.AreEqual("OtherChannel",MovementTracking.Assess(follower,leader,Now).Code);
            follower.Pulse.Movement.Channel=4;follower.Pulse.Movement.Playfield=200;
            Assert.AreEqual("OtherArea",MovementTracking.Assess(follower,leader,Now).Code);
            follower.Pulse.Movement.Playfield=100;leader.Pulse.Zoning=true;
            Assert.IsFalse(MovementTracking.Assess(follower,leader,Now).CanRegroup);
            follower.Pulse.Zoning=true;
            Assert.AreEqual("Zoning",MovementTracking.Assess(follower,leader,Now).Code);
        }
        [TestMethod] public void RegroupRequiresNearbyVisibleIdleFollower()
        {
            var leader=Client(1);var follower=Client(2,61);
            Assert.IsFalse(MovementTracking.Assess(follower,leader,Now).CanRegroup);
            follower.Pulse.Movement.X=1;follower.Pulse.Movement.Y=6;
            Assert.IsFalse(MovementTracking.Assess(follower,leader,Now).CanRegroup);
            follower.Pulse.Movement.Y=0;follower.Pulse.Attacking=true;
            Assert.IsFalse(MovementTracking.Assess(follower,leader,Now).CanRegroup);
            follower.Pulse.Attacking=false;follower.Pulse.Movement.VisiblePlayers=new int[0];
            Assert.AreEqual("LeaderNotVisible",MovementTracking.Assess(follower,leader,Now).Code);
        }
        [TestMethod] public void DisabledAndUnsupportedSyncAreNotReportedAsFollowing()
        {
            var leader=Client(1);var follower=Client(2);
            leader.Pulse.Movement.SendMovement=false;
            Assert.AreEqual("DriverOff",MovementTracking.Assess(follower,leader,Now).Code);
            Assert.AreEqual("MoveOff",MovementTracking.Assess(leader,leader,Now).Code);
            follower.Pulse.Movement.Enabled=false;
            Assert.AreEqual("Disabled",MovementTracking.Assess(follower,leader,Now).Code);
            follower.Pulse.Movement.Adapter="Unsupported";
            Assert.AreEqual("Unsupported",MovementTracking.Assess(follower,leader,Now).Code);
        }
    }
}
