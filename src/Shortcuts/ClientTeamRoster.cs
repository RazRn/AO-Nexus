using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using AOSharp.Core;
using AOSharp.Common.Unmanaged.Imports;
using NexusTeams;

namespace NexusShortcuts
{
    internal sealed class ClientTeamState
    {
        internal bool Ready, InTeam, IsLeader;
        internal int Leader;
        internal int[] Members = new int[0];
    }

    internal static class ClientTeamRoster
    {
        // GUI.dll from the tested classic/Rebirth client. GetMemberList is an
        // exported method; the hash gates its compiler-specific std::list ABI.
        const string SupportedGui = "E485384721E2FE13972E840DFB6A9FE29B1BA4EB71B42CC049E5097A570B6DE1";
        static bool? supported;
        static string lastFailure;

        [DllImport("GUI.dll", EntryPoint="?GetMemberList@TeamViewModule_c@@QBEABV?$list@UTeamMemberNode_c@@V?$allocator@UTeamMemberNode_c@@@std@@@std@@XZ", CallingConvention=CallingConvention.ThisCall)]
        static extern IntPtr GetMemberList(IntPtr view);
        [DllImport("kernel32.dll")]
        static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, UIntPtr size, out UIntPtr read);

        static bool SupportsGui()
        {
            if (supported.HasValue) return supported.Value;
            using (var process = Process.GetCurrentProcess())
            {
                var module = process.Modules.Cast<ProcessModule>().FirstOrDefault(m => string.Equals(m.ModuleName, "GUI.dll", StringComparison.OrdinalIgnoreCase));
                if (module == null) return false; // Retry when GUI has loaded.
                using (var stream = File.OpenRead(module.FileName))
                using (var sha = SHA256.Create())
                    supported = string.Equals(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", ""), SupportedGui, StringComparison.Ordinal);
            }
            return supported.Value;
        }

        static byte[] Read(uint address, int size)
        {
            if (address < 0x10000 || (ulong)address + (uint)size > uint.MaxValue) return null;
            var bytes = new byte[size];
            UIntPtr read;
            return ReadProcessMemory(new IntPtr(-1), new IntPtr(unchecked((int)address)), bytes, (UIntPtr)(uint)size, out read)
                && read.ToUInt64() == (ulong)size ? bytes : null;
        }

        // Called only on the game update thread. No cached preset or last-known
        // roster is used to authorize invitations, leadership or movement.
        internal static ClientTeamState Capture()
        {
            var state = new ClientTeamState();
            if (Game.IsZoning || DynelManager.LocalPlayer == null || Team.IsRaid) return state;
            try
            {
                var view = TeamViewModule_c.GetInstanceIfAny();
                if (view == IntPtr.Zero) return state;
                state.InTeam = TeamViewModule_c.IsInTeam(view) != 0;
                if (!state.InTeam) { state.Ready = true; return state; }
                state.IsLeader = TeamViewModule_c.IsTeamLeader(view) != 0;
                if (SupportsGui())
                {
                    var list = GetMemberList(view);
                    var members = GuiTeamRoster.Decode(unchecked((uint)list.ToInt32()), Game.ClientInst, Read);
                    if (members == null) { Failure("GUI roster is incomplete or unreadable"); return state; }
                    state.Members = members;
                    // Followers may not know the leader via the engine after zoning.
                    // Nexus resolves their leader from a fresh, matching peer roster.
                    state.Leader = state.IsLeader ? Game.ClientInst : 0;
                }
                else
                {
                    if (!Team.IsInTeam) { Failure("Unsupported GUI roster and engine roster unavailable"); return state; }
                    var members = Team.Members;
                    state.Members = members.Select(m => m.Identity.Instance).Concat(new[] { Game.ClientInst }).Distinct().OrderBy(id => id).ToArray();
                    if (state.Members.Length > 6 || state.Members.Any(id => id <= 0)) return state;
                    state.Leader = state.IsLeader ? Game.ClientInst : members.FirstOrDefault(m => m.IsLeader)?.Identity.Instance ?? 0;
                }
                state.Ready = true;
                lastFailure = null;
            }
            catch (Exception ex) { Failure(ex.GetType().Name + ": " + ex.Message); }
            return state;
        }

        static void Failure(string reason)
        {
            if (reason == lastFailure) return;
            lastFailure = reason;
            Trace.TraceWarning("Client team roster unavailable: {0}", reason);
        }
    }
}
