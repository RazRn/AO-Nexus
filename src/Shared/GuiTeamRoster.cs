using System;
using System.Collections.Generic;
using System.Linq;

namespace NexusTeams
{
    // Decoder for the verified 32-bit GUI TeamMemberNode list. It reads identities
    // only, never names or ownership pointers. Null means unavailable, not solo.
    internal static class GuiTeamRoster
    {
        internal static int[] Decode(uint list, int self, Func<uint, int, byte[]> read)
        {
            if (list == 0 || self <= 0) return null;
            var header = read(list, 8);
            if (header == null || header.Length != 8) return null;
            uint head = BitConverter.ToUInt32(header, 0), count = BitConverter.ToUInt32(header, 4);
            if (head == 0 || count < 1 || count > 6) return null;
            var sentinel = read(head, 8);
            if (sentinel == null || sentinel.Length != 8) return null;
            uint node = BitConverter.ToUInt32(sentinel, 0), previous = head;
            var visited = new HashSet<uint>();
            var ids = new HashSet<int>();
            for (int i = 0; i < count; i++)
            {
                if (node == 0 || node == head || !visited.Add(node)) return null;
                var data = read(node, 16);
                if (data == null || data.Length != 16 || BitConverter.ToUInt32(data, 4) != previous) return null;
                int type = BitConverter.ToInt32(data, 8), id = BitConverter.ToInt32(data, 12);
                if (type != 50000 || id <= 0 || !ids.Add(id)) return null;
                previous = node;
                node = BitConverter.ToUInt32(data, 0);
            }
            if (node != head || BitConverter.ToUInt32(sentinel, 4) != previous || !ids.Contains(self)) return null;
            // Reject a list modified during the read rather than publishing a partial roster.
            var after = read(list, 8);
            if (after == null || !header.SequenceEqual(after)) return null;
            return ids.OrderBy(id => id).ToArray();
        }
    }
}
