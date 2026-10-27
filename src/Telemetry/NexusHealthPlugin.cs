using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using AOSharp.Core;
using AOSharp.Core.IPC;
using AOSharp.Core.UI;
using AOSharp.Common.SharedEventArgs;
using SmokeLounge.AOtomation.Messaging.Messages;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;
using SmokeLounge.AOtomation.Messaging.GameData;
using System.Reflection;

[assembly: AssemblyTitle("Nexus Health Monitor")]
[assembly: AssemblyProduct("Nexus Health Monitor")]
[assembly: AssemblyDescription("AO Nexus update-loop and IPC measurements")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

namespace NexusTelemetry
{
    // No graphics changes, game actions, injection or process-priority changes.
    public sealed class NexusHealthPlugin : AOPluginEntry
    {
        readonly object gate = new object();
        readonly long[] times = new long[2048];
        readonly double[] gaps = new double[2048];
        readonly Pulse pulse = new Pulse { Instance = Guid.NewGuid().ToString("N") };
        Timer writer;
        IPCChannel channel;
        string path;
        int cursor;
        bool stopped;
        int writing;
        ActionIssueTracker actionIssues;

        public override void Run()
        {
            using (var process = Process.GetCurrentProcess()) {
                pulse.Pid = process.Id; pulse.StartTicks = process.StartTime.ToUniversalTime().Ticks;
            }
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AONexus", "Telemetry");
            Directory.CreateDirectory(root);
            path = Path.Combine(root, pulse.Pid + "-" + pulse.StartTicks + ".json");
            pulse.Active = true;
            actionIssues = new ActionIssueTracker(pulse);
            IPCChannel.LoadMessages(typeof(HealthPing).Assembly);
            channel = new IPCChannel(238);
            channel.RegisterCallback(32002, (sender, message) => {
                var ping = (HealthPing)message;
                if (stopped || ping.Reply != 0 || ping.Target != pulse.Pid || ping.StartTicks != pulse.StartTicks) return;
                try { channel.Broadcast(new HealthPing { Origin = ping.Origin, Epoch = ping.Epoch, Sequence = ping.Sequence,
                    Target = pulse.Pid, StartTicks = pulse.StartTicks, Reply = 1 }); } catch { }
            });
            Game.OnUpdate += Update;
            MiscClientEvents.AttemptingSpellCast += AttemptingSpellCast;
            Network.N3MessageSent += MessageSent;
            Chat.FeedbackReceived += FeedbackReceived;
            writer = new Timer(Publish, null, 0, 500);
        }
        void AttemptingSpellCast(object sender, AttemptingSpellCastEventArgs args) { ObserveCast(); }
        void MessageSent(object sender, N3Message message)
        {
            if (message is CharacterActionMessage action && action.Action == CharacterActionType.CastNano) ObserveCast();
        }
        void ObserveCast()
        {
            try {
                if (stopped || Game.IsZoning) return;
                var player = DynelManager.LocalPlayer;
                if (player == null) return;
                bool falling = player.IsFalling;
                lock (gate) { if (!stopped) actionIssues.ObserveCast(falling, false, Stopwatch.GetTimestamp()); }
            } catch { /* Monitoring must never interfere with casting. */ }
        }
        void FeedbackReceived(string text)
        {
            try { lock (gate) { if (!stopped) actionIssues.ObserveFeedback(text, Stopwatch.GetTimestamp()); } }
            catch { /* Ignore unavailable diagnostic evidence. */ }
        }
        void Update(object sender, float delta)
        {
            lock (gate) {
                if (stopped) return;
                long now = Stopwatch.GetTimestamp();
                if (pulse.LastUpdateQpc != 0) {
                    double ms = (now - pulse.LastUpdateQpc) * 1000.0 / Stopwatch.Frequency;
                    pulse.MaxMs = Math.Max(pulse.MaxMs, ms);
                    if (ms > 100) pulse.Over100++;
                    times[cursor] = now; gaps[cursor] = ms; cursor = (cursor + 1) % times.Length;
                }
                pulse.LastUpdateQpc = now; pulse.Updates++; pulse.Zoning = Game.IsZoning;
            }
        }
        void Publish(object unused)
        {
            if (Interlocked.Exchange(ref writing, 1) != 0) return;
            try {
                string json;
                lock (gate) {
                    pulse.PublishedQpc = Stopwatch.GetTimestamp();
                    pulse.RecentMaxMs = 0;
                    long oldest = pulse.PublishedQpc - 10 * Stopwatch.Frequency;
                    for (int i = 0; i < times.Length; i++) if (times[i] >= oldest) pulse.RecentMaxMs = Math.Max(pulse.RecentMaxMs, gaps[i]);
                    json = new JavaScriptSerializer().Serialize(pulse);
                }
                File.WriteAllText(path + ".tmp", json);
                if (File.Exists(path)) File.Replace(path + ".tmp", path, null); else File.Move(path + ".tmp", path);
            } catch { /* A diagnostic failure must never interrupt the game. */ }
            finally { Volatile.Write(ref writing, 0); }
        }
        public override void Teardown()
        {
            lock (gate) { stopped = true; pulse.Active = false; }
            Game.OnUpdate -= Update;
            MiscClientEvents.AttemptingSpellCast -= AttemptingSpellCast;
            Network.N3MessageSent -= MessageSent;
            Chat.FeedbackReceived -= FeedbackReceived;
            if (writer != null) using (var finished = new ManualResetEvent(false)) {
                if (writer.Dispose(finished)) finished.WaitOne(2000);
            }
            Publish(null);
        }
    }
}
