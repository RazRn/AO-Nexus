using System;
using System.ComponentModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NexusEngine;

namespace Nexus.Tests
{
    [TestClass]
    public class ProcessLoaderReadinessTests
    {
        [TestMethod]
        public void ReadyLoaderAddsNoFixedDelay()
        {
            ProcessLoaderReadiness.WaitUntil(() => false, () => true, () => 0, _ => Assert.Fail("Unnecessary delay"));
        }
        [TestMethod]
        public void EmptyModuleListWaitsUntilLoaderPublishesModules()
        {
            int polls = 0, elapsed = 0;
            ProcessLoaderReadiness.WaitUntil(() => false, () => ++polls == 4, () => elapsed, ms => elapsed += ms);
            Assert.AreEqual(4, polls); Assert.AreEqual(15, elapsed);
        }
        [TestMethod]
        public void TransientPartialReadIsRetriedButAccessDeniedIsNot()
        {
            int polls = 0;
            ProcessLoaderReadiness.WaitUntil(() => false, () => { if (++polls == 1) throw new Win32Exception(299); return true; }, () => 0, _ => {});
            Assert.AreEqual(2, polls);
            Assert.ThrowsExactly<Win32Exception>(() => ProcessLoaderReadiness.WaitUntil(() => false, () => throw new Win32Exception(5), () => 0, _ => Assert.Fail("Access denied must remain visible")));
        }
        [TestMethod]
        public void ExitStopsBeforeAnyInjectionReadinessProbe()
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => ProcessLoaderReadiness.WaitUntil(() => true, () => throw new Exception("Must not probe"), () => 0, _ => {}));
        }
        [TestMethod]
        public void StartupWaitIsBounded()
        {
            int elapsed = 0;
            Assert.ThrowsExactly<TimeoutException>(() => ProcessLoaderReadiness.WaitUntil(() => false, () => false, () => elapsed, ms => elapsed += ms));
            Assert.AreEqual(5000, elapsed);
        }
    }
}
