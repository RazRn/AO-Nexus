using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using NexusTelemetry;

static class LocalHealthChannelTests
{
    static int checks;
    static void Check(bool value, string reason) { if (!value) throw new Exception(reason); checks++; }
    static int Main()
    {
        using (var process = Process.GetCurrentProcess()) {
            int pid = process.Id; long start = process.StartTime.ToUniversalTime().Ticks;
            using (var game = new LocalHealthChannel(pid, start, true))
            using (var helper = new LocalHealthChannel(pid, start, false)) {
                Func<int, HealthPing> ping = seq => new HealthPing { Target=pid, StartTicks=start, Origin=100, Epoch=200, Sequence=seq };
                Check(!helper.Send(new HealthPing { Target=pid + 1, StartTicks=start }), "Reject wrong process");
                Check(!helper.Send(new HealthPing { Target=pid, StartTicks=start + 1 }), "Reject reused process ID");
                bool absent = false;
                try { using (new LocalHealthChannel(pid, start + 1, false)) {} } catch (WaitHandleCannotBeOpenedException) { absent = true; }
                Check(absent, "A different process lifetime has a different channel");
                for (int i=0; i<64; i++) Check(helper.Send(ping(i)), "Queue bounded probe " + i);
                Check(!helper.Send(ping(64)), "Full mailbox does not overwrite outstanding probes");
                Check(helper.Receive(100,200).Count == 0, "No replies before game loop runs");
                game.Respond();
                Check(helper.Receive(101,200).Count == 0 && helper.Receive(100,201).Count == 0, "Another host or epoch cannot consume replies");
                Check(helper.Receive(100,200).Select(p=>p.Sequence).SequenceEqual(Enumerable.Range(0,64)), "Queued probes survive a paused game loop");
                Check(helper.Receive(100,200).Count == 0 && helper.Send(ping(65)), "Replies are consumed once and slots reused");
                using (var acquired = new ManualResetEvent(false))
                using (var release = new ManualResetEvent(false)) {
                    var owner = new Thread(() => { using (var mutex = Mutex.OpenExisting(LocalHealthChannel.Name(pid,start)+".Lock")) {
                        mutex.WaitOne(); acquired.Set(); release.WaitOne(); mutex.ReleaseMutex(); } });
                    owner.Start(); acquired.WaitOne();
                    try { var watch=Stopwatch.StartNew(); game.Respond(); Check(watch.ElapsedMilliseconds < 100, "Busy mailbox never blocks the game thread"); }
                    finally { release.Set(); owner.Join(); }
                }
                game.Respond(); Check(helper.Receive(100,200).Single().Sequence == 65, "Contended probe recovers on next frame");
            }
            bool gone=false;
            try { using (new LocalHealthChannel(pid,start,false)) {} } catch (WaitHandleCannotBeOpenedException) { gone=true; }
            Check(gone, "Last owner disposal releases the channel");
        }
        Console.WriteLine("PASS: " + checks + " local mailbox checks; no game or networking.");
        return 0;
    }
}
