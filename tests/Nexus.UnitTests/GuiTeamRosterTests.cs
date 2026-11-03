using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NexusTeams;

namespace Nexus.Tests
{
    [TestClass]
    public class GuiTeamRosterTests
    {
        sealed class Memory
        {
            internal readonly Dictionary<uint, byte[]> Blocks = new Dictionary<uint, byte[]>();
            internal int Reads;
            internal void Put(uint address, params int[] values)
            {
                var bytes = new byte[values.Length * 4];
                Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
                Blocks[address] = bytes;
            }
            internal byte[] Read(uint address, int size)
            {
                Reads++;
                return Blocks.TryGetValue(address, out var data) && data.Length == size ? data : null;
            }
        }
        static Memory Valid()
        {
            var m = new Memory();
            m.Put(100, 200, 2); m.Put(200, 300, 400);
            m.Put(300, 400, 200, 50000, 173); m.Put(400, 200, 300, 50000, 170);
            return m;
        }
        [TestMethod]
        public void ReadsGuiRosterWithoutEngineTeamStateAndSortsIdentities()
        {
            var m = Valid();
            CollectionAssert.AreEqual(new[] {170, 173}, GuiTeamRoster.Decode(100, 173, m.Read));
        }
        [TestMethod]
        public void RejectsPartialMissingSelfOrNonCharacterRoster()
        {
            var m = Valid(); Assert.IsNull(GuiTeamRoster.Decode(100, 999, m.Read));
            m.Put(400, 200, 300, 51035, 170); Assert.IsNull(GuiTeamRoster.Decode(100, 173, m.Read));
            m.Blocks.Remove(400); Assert.IsNull(GuiTeamRoster.Decode(100, 173, m.Read));
        }
        [TestMethod]
        public void RejectsLoopsDuplicatesAndInvalidBackLinks()
        {
            var m = Valid(); m.Put(300, 300, 200, 50000, 173);
            Assert.IsNull(GuiTeamRoster.Decode(100, 173, m.Read));
            m = Valid(); m.Put(400, 200, 300, 50000, 173);
            Assert.IsNull(GuiTeamRoster.Decode(100, 173, m.Read));
            m = Valid(); m.Put(400, 200, 123, 50000, 170);
            Assert.IsNull(GuiTeamRoster.Decode(100, 173, m.Read));
        }
        [TestMethod]
        public void RejectsOversizedAndEmptyListsWithoutFollowingPointers()
        {
            foreach (int count in new[] {0, 7, int.MaxValue, -1})
            {
                var m = Valid(); m.Put(100, 200, count);
                Assert.IsNull(GuiTeamRoster.Decode(100, 173, m.Read)); Assert.AreEqual(1, m.Reads);
            }
        }
        [TestMethod]
        public void RejectsRosterThatChangesDuringRead()
        {
            var m = Valid(); int headers = 0;
            Assert.IsNull(GuiTeamRoster.Decode(100, 173, (address, size) => {
                if (address == 100 && ++headers == 2) m.Put(100, 200, 1);
                return m.Read(address, size);
            }));
        }
    }
}
