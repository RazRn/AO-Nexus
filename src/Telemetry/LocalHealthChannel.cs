using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;

namespace NexusTelemetry
{
    // Session-local, pagefile-backed mailbox. No sockets, disk IO or waiting on the game thread.
    // Every field is accessed under the mutex, including 64-bit fields in these x86 processes.
    public sealed class LocalHealthChannel : IDisposable
    {
        const int Slots = 64, Stride = 48;
        MemoryMappedFile map;
        MemoryMappedViewAccessor view;
        Mutex mutex;
        readonly int pid;
        readonly long start;
        public static string Name(int pid, long start) => "Local\\AONexus.Health.v2." + WindowsIdentity.GetCurrent().User.Value + "." + pid + "." + start;
        public LocalHealthChannel(int pid, long start, bool create)
        {
            this.pid = pid; this.start = start;
            string name = Name(pid, start);
            try {
                if (create) {
                    var sid = WindowsIdentity.GetCurrent().User;
                    var security = new MemoryMappedFileSecurity();
                    security.SetAccessRuleProtection(true, false);
                    security.AddAccessRule(new AccessRule<MemoryMappedFileRights>(sid, MemoryMappedFileRights.FullControl, AccessControlType.Allow));
                    var mutexSecurity = new MutexSecurity();
                    mutexSecurity.SetAccessRuleProtection(true, false);
                    mutexSecurity.AddAccessRule(new MutexAccessRule(sid, MutexRights.FullControl, AccessControlType.Allow));
                    bool created;
                    mutex = new Mutex(false, name + ".Lock", out created, mutexSecurity);
                    map = MemoryMappedFile.CreateOrOpen(name, Slots * Stride, MemoryMappedFileAccess.ReadWrite,
                        MemoryMappedFileOptions.None, security, HandleInheritability.None);
                } else {
                    mutex = Mutex.OpenExisting(name + ".Lock");
                    map = MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.ReadWrite);
                }
                view = map.CreateViewAccessor(0, Slots * Stride, MemoryMappedFileAccess.ReadWrite);
            } catch { Dispose(); throw; }
        }
        bool Enter()
        {
            try { return mutex.WaitOne(0); }
            catch (AbandonedMutexException) { return true; }
        }
        public bool Send(HealthPing ping)
        {
            if (ping.Target != pid || ping.StartTicks != start || !Enter()) return false;
            try {
                long now = Stopwatch.GetTimestamp();
                for (int i = 0; i < Slots; i++) {
                    int offset = i * Stride;
                    if (view.ReadInt32(offset) != 0 && now - view.ReadInt64(offset + 32) < 30 * Stopwatch.Frequency) continue;
                    view.Write(offset, 0);
                    view.Write(offset + 4, ping.Origin); view.Write(offset + 8, ping.Epoch);
                    view.Write(offset + 12, ping.Sequence); view.Write(offset + 16, pid);
                    view.Write(offset + 24, start); view.Write(offset + 32, now);
                    view.Write(offset, 1);
                    return true;
                }
                return false;
            } finally { mutex.ReleaseMutex(); }
        }
        // Called only from Game.OnUpdate. A live publisher cannot manufacture a game-loop reply.
        public void Respond()
        {
            if (!Enter()) return;
            try {
                for (int i = 0; i < Slots; i++) {
                    int offset = i * Stride;
                    if (view.ReadInt32(offset) == 1 && view.ReadInt32(offset + 16) == pid && view.ReadInt64(offset + 24) == start)
                        view.Write(offset, 2);
                }
            } finally { mutex.ReleaseMutex(); }
        }
        public List<HealthPing> Receive(int origin, int epoch)
        {
            var replies = new List<HealthPing>();
            if (!Enter()) return replies;
            try {
                for (int i = 0; i < Slots; i++) {
                    int offset = i * Stride;
                    if (view.ReadInt32(offset) != 2 || view.ReadInt32(offset + 4) != origin || view.ReadInt32(offset + 8) != epoch) continue;
                    replies.Add(new HealthPing { Origin = origin, Epoch = epoch, Sequence = view.ReadInt32(offset + 12),
                        Target = view.ReadInt32(offset + 16), StartTicks = view.ReadInt64(offset + 24), Reply = 1 });
                    view.Write(offset, 0);
                }
                return replies;
            } finally { mutex.ReleaseMutex(); }
        }
        public void Dispose() { view?.Dispose(); map?.Dispose(); mutex?.Dispose(); view = null; map = null; mutex = null; }
    }
}
