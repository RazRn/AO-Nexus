using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using AOSharp.Core.IPC;

namespace NexusTelemetry
{
    sealed class Endpoint : IPCChannelBase
    {
        protected override int _localDynelId => -Process.GetCurrentProcess().Id;
        internal Endpoint() : base(238) { }
        internal void Pump() => Update();
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
                IPCChannelBase.LoadMessages(typeof(HealthPing).Assembly);
                var endpoint = new Endpoint();
                endpoint.RegisterCallback(32002, (sender, message) => {
                    var reply = (HealthPing)message;
                    Pending request; PingResult result;
                    if (reply.Reply != 1 || reply.Origin != origin || reply.Epoch != epoch || !pending.TryGetValue(reply.Sequence, out request)
                        || request.Pid != reply.Target || request.Start != reply.StartTicks || !results.TryGetValue(reply.Target, out result)) return;
                    long now = Stopwatch.GetTimestamp();
                    double ms = (now - request.At) * 1000.0 / Stopwatch.Frequency;
                    if (!request.Expired && ms >= 2000) { request.Expired = true; result.Timeouts++; }
                    if (request.Expired) result.Late++;
                    result.Replies++; result.LastMs = ms; result.MaxMs = Math.Max(result.MaxMs, ms); result.LastReplyQpc = now;
                    pending.Remove(reply.Sequence);
                });
                long next = 0;
                while (!Volatile.Read(ref ended)) {
                    string update = Interlocked.Exchange(ref command, null);
                    if (update != null) {
                        var targets = serializer.Deserialize<Target[]>(update) ?? new Target[0];
                        if (targets.Length > 128) throw new InvalidOperationException("Too many clients");
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
            } catch (Exception error) { Console.Error.WriteLine(error.GetType().Name); return 1; }
        }
    }
}
