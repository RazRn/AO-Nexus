using Microsoft.VisualStudio.TestTools.UnitTesting;
using NexusClientRecovery;

namespace Nexus.Tests
{
    [TestClass]
    public class CastPostureRecoveryTests
    {
        const string Error = CastPostureRecovery.StandingRequired;
        [TestMethod]
        public void RequiresRepeatedExplicitStandingRejection()
        {
            var recovery = new CastPostureRecovery();
            Assert.IsFalse(recovery.Observe("You can't execute nanoprograms while falling!", 1, true));
            Assert.IsFalse(recovery.Observe(Error, 2, true));
            Assert.IsFalse(recovery.Observe(Error, 2.1, true));
            Assert.IsTrue(recovery.Observe(Error, 2.5, true));
        }
        [TestMethod]
        public void NeverStandsSeatedMovingOrUnavailablePlayer()
        {
            var recovery = new CastPostureRecovery();
            Assert.IsFalse(recovery.Observe(Error, 1, true));
            Assert.IsFalse(recovery.Observe(Error, 1.5, false));
            Assert.IsFalse(recovery.Observe(Error, 2, true));
            Assert.IsTrue(recovery.Observe(Error, 2.5, true));
        }
        [TestMethod]
        public void DoesNotCombineStaleRejections()
        {
            var recovery = new CastPostureRecovery();
            Assert.IsFalse(recovery.Observe(Error, 1, true));
            Assert.IsFalse(recovery.Observe(Error, 5, true));
            Assert.IsFalse(recovery.Observe(Error, 4, true));
        }
        [TestMethod]
        public void BoundsCorrectionsEvenWhenServerContinuesRejecting()
        {
            var recovery = new CastPostureRecovery();
            int actions = 0;
            for (int i = 0; i < 1000; i++) if (recovery.Observe(Error, i * 0.5, true)) actions++;
            Assert.AreEqual(2, actions);
            Assert.AreEqual(2, recovery.Corrections);
        }
        [TestMethod]
        public void ZoneRequiresNewEvidenceAndPreservesCooldown()
        {
            var recovery = new CastPostureRecovery();
            recovery.Observe(Error, 1, true);
            Assert.IsTrue(recovery.Observe(Error, 1.5, true));
            recovery.ResetZone();
            Assert.IsFalse(recovery.Observe(Error, 2, true));
            Assert.IsFalse(recovery.Observe(Error, 2.5, true));
            recovery.Observe(Error, 6, true);
            Assert.IsTrue(recovery.Observe(Error, 6.5, true));
        }
    }
}
