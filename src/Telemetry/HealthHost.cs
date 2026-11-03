using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using System.IO;

namespace NexusTelemetry
{
    sealed class Endpoint : IDisposable
    {
        readonly Dictionary<string, LocalHealthChannel> channels = new Dictionary<string, LocalHealthChannel>();
        readonly int origin, epoch;
        internal Action<HealthPing> Reply;
        internal Endpoint(int origin, int epoch) { this.origin = origin; this.epoch = epoch; }
        static string Key(int pid, long start) => pid + "-" + start;
        internal void SetTargets(Target[] targets)
        {
            var keys = new HashSet<string>(targets.Select(t => Key(t.Pid, t.StartTicks)));
            foreach (var key in channels.Keys.Where(key => !keys.Contains(key)).ToArray()) { channels[key].Dispose(); channels.Remove(key); }
        }
        internal void Broadcast(HealthPing ping)
        {
            string key = Key(ping.Target, ping.StartTicks);
            LocalHealthChannel channel;
            if (!channels.TryGetValue(key, out channel)) {
                try { channel = new LocalHealthChannel(ping.Target, ping.StartTicks, false); channels.Add(key, channel); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is WaitHandleCannotBeOpenedException) { return; }
            }
            channel.Send(ping);
        }
        internal void Pump() { foreach (var channel in channels.Values) foreach (var reply in channel.Receive(origin, epoch)) Reply?.Invoke(reply); }
        public void Dispose() { foreach (var channel in channels.Values) channel.Dispose(); channels.Clear(); }
    }
    static class HealthHost
    {
        sealed class Pending { internal int Pid; internal long Start, At; internal bool Expired; }
        static int Main()
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = 32768, RecursionLimit = 8 };
            var results = new Dictionary<int, PingResult>();
            var pending = new Dictionary<int, Pending>();
            int origin = Process.GetCurrentProcess().Id, epoch = Guid.NewGuid().GetHashCode(), sequence = 0;
            string command = null; bool ended = false;
            var input = new Thread(() => { try { string line; while ((line = Console.ReadLine()) != null) Volatile.Write(ref command, line); }
                finally { Volatile.Write(ref ended, true); } }) { IsBackground = true };
            input.Start();
            try {
                using (var endpoint = new Endpoint(origin, epoch)) {
                endpoint.Reply = reply => {
                    Pending request; PingResult result;
                    if (reply.Reply != 1 || reply.Origin != origin || reply.Epoch != epoch || !pending.TryGetValue(reply.Sequence, out request)
                        || request.Pid != reply.Target || request.Start != reply.StartTicks || !results.TryGetValue(reply.Target, out result)) return;
                    long now = Stopwatch.GetTimestamp();
                    double ms = (now - request.At) * 1000.0 / Stopwatch.Frequency;
                    if (!request.Expired && ms >= 2000) { request.Expired = true; result.Timeouts++; }
                    if (request.Expired) result.Late++;
                    result.Replies++; result.LastMs = ms; result.MaxMs = Math.Max(result.MaxMs, ms); result.LastReplyQpc = now;
                    pending.Remove(reply.Sequence);
                };
                long next = 0;
                while (!Volatile.Read(ref ended)) {
                    string update = Interlocked.Exchange(ref command, null);
                    if (update != null) {
                        var targets = serializer.Deserialize<Target[]>(update) ?? new Target[0];
                        if (targets.Length > 128) throw new InvalidOperationException("Too many clients");
                        endpoint.SetTargets(targets);
                        foreach (var old in results.Keys.Where(pid => !targets.Any(t => t.Pid == pid && t.StartTicks == results[pid].StartTicks)).ToArray()) results.Remove(old);
                        foreach (var target in targets) if (!results.ContainsKey(target.Pid)) results[target.Pid] = new PingResult { Pid = target.Pid, StartTicks = target.StartTicks };
                        foreach (var key in pending.Where(p => !results.ContainsKey(p.Value.Pid) || results[p.Value.Pid].StartTicks != p.Value.Start).Select(p => p.Key).ToArray()) pending.Remove(key);
                    }
                    endpoint.Pump();
                    long now = Stopwatch.GetTimestamp();
                    foreach (var pair in pending.ToArray()) {
                        double age = (now - pair.Value.At) / (double)Stopwatch.Frequency;
                        if (age >= 2 && !pair.Value.Expired) { pair.Value.Expired = true; results[pair.Value.Pid].Timeouts++; }
                        if (age >= 30) pending.Remove(pair.Key);
                    }
                    if (now >= next) {
                        foreach (var result in results.Values) {
                            result.PublishedQpc = now;
                            result.Pending = pending.Values.Count(p => p.Pid == result.Pid && !p.Expired);
                            Console.WriteLine(serializer.Serialize(result));
                            int id = ++sequence;
                            pending.Add(id, new Pending { Pid = result.Pid, Start = result.StartTicks, At = now }); result.Sent++;
                            endpoint.Broadcast(new HealthPing { Origin = origin, Epoch = epoch, Sequence = id, Target = result.Pid, StartTicks = result.StartTicks });
                        }
                        next = now + Stopwatch.Frequency;
                    }
                    Thread.Sleep(5);
                }
                return 0;
                }
            } catch (Exception error) { Console.Error.WriteLine(error.GetType().Name); return 1; }
        }
    }
}
