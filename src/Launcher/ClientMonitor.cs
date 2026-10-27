using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;

namespace AOLauncher
{
    internal sealed class ClientPulse
    {
        public int Version { get; set; }
        public int Pid { get; set; }
        public long StartTicks { get; set; }
        public string Instance { get; set; }
        public long PublishedQpc { get; set; }
        public long LastUpdateQpc { get; set; }
        public long Updates { get; set; }
        public double RecentMaxMs { get; set; }
        public double MaxMs { get; set; }
        public long Over100 { get; set; }
        public bool Zoning { get; set; }
        public bool Active { get; set; }
        public int ActionIssue { get; set; }
        public long ActionIssueQpc { get; set; }
        public int ActionIssueCount { get; set; }
        public bool ActionIssueConfirmed { get; set; }
    }
    internal sealed class ClientPing
    {
        public int Pid { get; set; }
        public long StartTicks { get; set; }
        public long PublishedQpc { get; set; }
        public long LastReplyQpc { get; set; }
        public int Sent { get; set; }
        public int Replies { get; set; }
        public int Timeouts { get; set; }
        public int Pending { get; set; }
        public int Late { get; set; }
        public double LastMs { get; set; }
        public double MaxMs { get; set; }
    }
    internal enum ClientCondition { Unmeasured, Waiting, Healthy, Delayed, NoResponse, Unloaded, Exited, MeasurementUnavailable }
    internal sealed class ClientSample
    {
        public DateTime Utc { get; set; }
        public string State { get; set; }
        public string Reason { get; set; }
        public double? PulseAgeMs { get; set; }
        public double? ObservedUpdateGapMs { get; set; }
        public double? HelperAgeMs { get; set; }
        public double? ReplyAgeMs { get; set; }
        public double? UpdateAgeMs { get; set; }
        public double? RecentMaxGapMs { get; set; }
        public double? MaxGapMs { get; set; }
        public double? ReplyMs { get; set; }
        public int? Sent { get; set; }
        public int? Replies { get; set; }
        public int? Timeouts { get; set; }
        public int? Late { get; set; }
        public int? Pending { get; set; }
        public bool Minimized { get; set; }
        public bool Foreground { get; set; }
        public bool Zoning { get; set; }
        public long WorkingSetBytes { get; set; }
    }
    internal sealed class ClientObservation
    {
        internal string Name, Alias;
        internal int Pid;
        internal long StartTicks;
        internal ClientCondition Condition;
        internal ClientSample Sample;
        internal bool Measured;
        internal DateTime? ExitedUtc;
        internal int? ExitCode;
        internal bool CastWhileFalling, CastIssueConfirmed;
        internal int CastIssueCount;
        internal bool HasCastIssue => Measured && !ExitedUtc.HasValue && Condition != ClientCondition.Unloaded && CastWhileFalling;
    }
    internal sealed class ClientMonitor : IDisposable
    {
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        sealed class Tracked : IDisposable
        {
            internal PresetCamping.Client Client;
            internal Process Process;
            internal string Alias, Instance, Version;
            internal long FirstPulse;
            internal ClientPulse Pulse;
            internal ClientCondition Condition;
            internal DateTime? ExitedUtc;
            internal int? ExitCode;
            internal bool CastIssueVisible;
            internal readonly Queue<ClientSample> Samples = new Queue<ClientSample>();
            public void Dispose() { Process?.Dispose(); }
        }
        readonly Dictionary<string, Tracked> clients = new Dictionary<string, Tracked>();
        readonly object pingGate = new object();
        readonly Dictionary<int, ClientPing> pings = new Dictionary<int, ClientPing>();
        internal static string PulseRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AONexus", "Telemetry");
        internal static string ReportRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AONexus", "ClientReports");
        internal static string HelperRoot => EmbeddedMonitoring.EnsureAvailable();
        readonly string pulseRoot, reportRoot, helperRoot;
        internal ClientMonitor() : this(PulseRoot, ReportRoot, null) { }
        internal ClientMonitor(string pulses, string reports, string helper) { pulseRoot = pulses; reportRoot = reports; helperRoot = helper; }
        internal List<ClientObservation> Current { get; private set; } = new List<ClientObservation>();
        internal readonly List<string> Changes = new List<string>();
        internal string Error { get; private set; }
        Process host;
        bool disposed;
        long lastStart;
        int nextAlias;
        string targets = "";
        static JavaScriptSerializer Json() => new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024, RecursionLimit = 16 };
        internal static double Age(long now, long then) => then <= 0 || then > now ? double.PositiveInfinity : (now - then) * 1000.0 / Stopwatch.Frequency;
        internal static bool HasRecentCastIssue(ClientPulse pulse, long now) => pulse != null && pulse.Active && !pulse.Zoning
            && pulse.ActionIssue == 1 && pulse.ActionIssueCount > 0 && pulse.ActionIssueCount <= 9999
            && pulse.ActionIssueQpc <= pulse.PublishedQpc && Age(now, pulse.PublishedQpc) < 3000 && Age(now, pulse.ActionIssueQpc) < 30000;
        internal static ClientCondition Evaluate(ClientPulse pulse, ClientPing ping, long now, long firstPulse)
        {
            string reason;
            return Assess(pulse, ping, now, firstPulse, out reason);
        }
        static ClientCondition Assess(ClientPulse pulse, ClientPing ping, long now, long firstPulse, out string reason)
        {
            reason = null;
            if (pulse == null) return ClientCondition.Unmeasured;
            if (!pulse.Active) return ClientCondition.Unloaded;
            bool freshPulse = Age(now, pulse.PublishedQpc) < 3000;
            bool freshHelper = ping != null && Age(now, ping.PublishedQpc) < 3000;
            bool grace = Age(now, firstPulse) < 4000;
            // A snapshot's delivery age is not a measured pause in Game.OnUpdate.
            double gap = Age(pulse.PublishedQpc, pulse.LastUpdateQpc);
            if (freshPulse && (pulse.LastUpdateQpc != 0 || !grace) && gap >= 2000) {
                reason = SidebarText.L("No game update at snapshot", "Ingen spilopdatering ved måling");
                return ClientCondition.NoResponse;
            }
            if (freshHelper && !grace && (ping.LastReplyQpc < firstPulse || Age(now, ping.LastReplyQpc) >= 3000)) {
                reason = SidebarText.L("IPC reply missing; helper is reporting", "IPC-svar mangler; hjælperen rapporterer");
                return ClientCondition.NoResponse;
            }
            if (!freshPulse) {
                reason = SidebarText.L("Measurement snapshot is stale", "Målingens øjebliksbillede er forældet");
                return ClientCondition.MeasurementUnavailable;
            }
            if (!freshHelper) {
                reason = SidebarText.L("IPC helper measurement unavailable", "IPC-hjælperens måling er utilgængelig");
                return grace ? ClientCondition.Waiting : ClientCondition.MeasurementUnavailable;
            }
            if (ping.LastReplyQpc < firstPulse || pulse.LastUpdateQpc == 0) return ClientCondition.Waiting;
            if (pulse.RecentMaxMs >= 100 || gap >= 1000) {
                reason = SidebarText.L("Measured game update gap", "Målt pause i spilopdatering");
                return ClientCondition.Delayed;
            }
            if (ping.LastMs >= 100) {
                reason = SidebarText.L("Slow IPC round trip", "Langsom IPC-rundtur");
                return ClientCondition.Delayed;
            }
            return ClientCondition.Healthy;
        }
        internal static ClientPulse ReadPulse(string path)
        {
            // The plugin publishes atomically with File.Replace. Allow replacement while
            // reading the old complete file, and bound the read on the opened handle.
            for (int attempt = 0; ; attempt++) {
                try {
                    using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
                        if (file.Length >= 8192) return null;
                        using (var reader = new StreamReader(file)) {
                            var buffer = new char[8192];
                            int count = reader.ReadBlock(buffer, 0, buffer.Length);
                            return count == buffer.Length ? null : Json().Deserialize<ClientPulse>(new string(buffer, 0, count));
                        }
                    }
                } catch (IOException) when (attempt < 2) {
                    // Windows replacement/filter drivers can briefly hide the directory
                    // entry. Retry twice, then leave persistent failures to stale-data handling.
                    System.Threading.Thread.Sleep(1);
                }
            }
        }
        internal void Poll(LauncherSettings settings)
        {
            if (disposed) return;
            Changes.Clear(); Error = null;
            var all = new LaunchPreset { Characters = settings.Accounts.SelectMany(a => a.Characters.Select(c => new PresetCharacter { AccountId = a.Id, CharacterId = c.CharacterId })).ToList() };
            try {
                foreach (var client in PresetCamping.Find(settings, all)) {
                    string key = client.ProcessId + "-" + client.StartTimeUtcTicks;
                    if (clients.ContainsKey(key)) continue;
                    Process process = null;
                    try {
                        process = Process.GetProcessById(client.ProcessId);
                        // Retain the OS handle so exit status cannot be confused with PID reuse.
                        var handle = process.Handle;
                        if (process.StartTime.ToUniversalTime().Ticks != client.StartTimeUtcTicks) continue;
                        clients.Add(key, new Tracked { Client = client, Process = process, Alias = "Client-" + (++nextAlias),
                            Version = process.MainModule.FileVersionInfo.FileVersion });
                        process = null; // ownership moved to Tracked
                    } catch { } finally { process?.Dispose(); }
                }
            } catch { Error = "Session discovery unavailable"; }
            long now = Stopwatch.GetTimestamp();
            foreach (var entry in clients.ToArray()) {
                var item = entry.Value;
                if (item.ExitedUtc.HasValue) continue;
                try {
                    if (item.Process.HasExited) {
                        item.ExitedUtc = DateTime.UtcNow;
                        try { item.ExitCode = item.Process.ExitCode; } catch { }
                        item.Condition = ClientCondition.Exited;
                        Changes.Add(item.Client.CharacterName + ": " + SidebarText.L("client exited", "klienten afsluttet"));
                        if (settings.CollectClientExitReports) try { SaveExit(item); } catch { Error = "Could not save exit report"; }
                        continue;
                    }
                    string path = Path.Combine(pulseRoot, entry.Key + ".json");
                    try {
                        if (!settings.MonitorClientHealth) item.Pulse = null;
                        if (settings.MonitorClientHealth && File.Exists(path)) {
                            var pulse = ReadPulse(path);
                            now = Stopwatch.GetTimestamp(); // Validate against a clock sampled AFTER the read.
                            if (pulse != null && pulse.Version == 1 && pulse.Pid == item.Client.ProcessId && pulse.StartTicks == item.Client.StartTimeUtcTicks
                                && !string.IsNullOrEmpty(pulse.Instance) && pulse.Instance.Length <= 64 && pulse.PublishedQpc > 0 && pulse.PublishedQpc <= now
                                && pulse.LastUpdateQpc <= pulse.PublishedQpc && pulse.RecentMaxMs >= 0 && pulse.MaxMs >= 0) {
                                if (item.Instance != pulse.Instance) {
                                    item.FirstPulse = now; item.Instance = pulse.Instance;
                                    lock (pingGate) pings.Remove(item.Client.ProcessId);
                                }
                                item.Pulse = pulse;
                            }
                        }
                    } catch { /* Retain evidence; Assess distinguishes stale measurements from a stalled game. */ }
                    ClientPing ping;
                    lock (pingGate) pings.TryGetValue(item.Client.ProcessId, out ping);
                    if (ping != null && ping.StartTicks != item.Client.StartTimeUtcTicks) ping = null;
                    now = Stopwatch.GetTimestamp(); // The helper can publish while the file read is in progress.
                    string reason;
                    var condition = Assess(item.Pulse, ping, now, item.FirstPulse, out reason);
                    item.Process.Refresh();
                    IntPtr window = item.Process.MainWindowHandle;
                    var sample = new ClientSample { Utc = DateTime.UtcNow, State = condition.ToString(), Reason = reason,
                        PulseAgeMs = item.Pulse != null ? (double?)Age(now, item.Pulse.PublishedQpc) : null,
                        ObservedUpdateGapMs = item.Pulse?.LastUpdateQpc > 0 ? (double?)Age(item.Pulse.PublishedQpc, item.Pulse.LastUpdateQpc) : null,
                        HelperAgeMs = ping != null ? (double?)Age(now, ping.PublishedQpc) : null,
                        ReplyAgeMs = ping?.LastReplyQpc > 0 ? (double?)Age(now, ping.LastReplyQpc) : null,
                        UpdateAgeMs = item.Pulse?.LastUpdateQpc > 0 ? (double?)Age(now, item.Pulse.LastUpdateQpc) : null,
                        RecentMaxGapMs = item.Pulse?.RecentMaxMs, MaxGapMs = item.Pulse?.MaxMs,
                        ReplyMs = ping?.LastReplyQpc > 0 ? (double?)ping.LastMs : null,
                        Sent = ping?.Sent, Replies = ping?.Replies, Timeouts = ping?.Timeouts, Late = ping?.Late, Pending = ping?.Pending,
                        Zoning = item.Pulse?.Zoning ?? false, Minimized = window != IntPtr.Zero && IsIconic(window),
                        Foreground = window != IntPtr.Zero && window == GetForegroundWindow(), WorkingSetBytes = item.Process.WorkingSet64 };
                    if (condition != item.Condition && item.Pulse != null) {
                        string detail = reason == null ? "" : string.Format(" — {0} (snapshot {1:F0} ms; update gap {2:F0} ms; max/10s {3:F1} ms; helper {4:F0} ms; reply age {5:F0} ms; RTT {6:F1} ms; timeouts {7})",
                            reason, sample.PulseAgeMs, sample.ObservedUpdateGapMs, sample.RecentMaxGapMs, sample.HelperAgeMs, sample.ReplyAgeMs, sample.ReplyMs, sample.Timeouts);
                        Changes.Add(item.Client.CharacterName + ": AOSharp · " + ConditionText(condition) + detail);
                    }
                    item.Condition = condition;
                    bool castIssue = HasRecentCastIssue(item.Pulse, now);
                    if (castIssue && !item.CastIssueVisible)
                        Changes.Add(item.Client.CharacterName + ": " + SidebarText.L("Nano cast attempted while falling. Check the game window, let the character land, then retry. Repeated reports are grouped in character status.",
                            "Nanoprogram forsøgt under fald. Tjek spilvinduet, lad karakteren lande, og prøv igen. Gentagne rapporter samles i karakterstatus."));
                    item.CastIssueVisible = castIssue;
                    item.Samples.Enqueue(sample); while (item.Samples.Count > 120) item.Samples.Dequeue();
                } catch { Error = "One client could not be inspected"; }
            }
            foreach (var old in clients.Where(c => c.Value.ExitedUtc.HasValue).OrderByDescending(c => c.Value.ExitedUtc).Skip(20).ToArray()) {
                old.Value.Dispose(); clients.Remove(old.Key);
            }
            Current = clients.Values.Select(t => new ClientObservation { Name = t.Client.CharacterName, Alias = t.Alias, Pid = t.Client.ProcessId,
                StartTicks = t.Client.StartTimeUtcTicks, Condition = t.Condition, Measured = t.Pulse != null && t.Condition != ClientCondition.Unloaded,
                Sample = t.Samples.LastOrDefault(), ExitedUtc = t.ExitedUtc, ExitCode = t.ExitCode,
                CastWhileFalling = !t.ExitedUtc.HasValue && HasRecentCastIssue(t.Pulse, now),
                CastIssueConfirmed = t.Pulse?.ActionIssueConfirmed ?? false, CastIssueCount = t.Pulse?.ActionIssueCount ?? 0 }).ToList();
            UpdateHost(now);
        }
        void UpdateHost(long now)
        {
            var desired = clients.Values.Where(t => !t.ExitedUtc.HasValue && t.Pulse != null && t.Pulse.Active)
                .Select(t => new { Pid = t.Client.ProcessId, StartTicks = t.Client.StartTimeUtcTicks }).ToArray();
            if (desired.Length == 0) { StopHost(); return; }
            try {
                if (host == null || host.HasExited) {
                    StopHost();
                    if (lastStart != 0 && Age(now, lastStart) < 15000) { Error = "IPC helper unavailable; waiting to retry"; return; }
                    lastStart = now;
                    string resolvedHelperRoot = helperRoot ?? HelperRoot;
                    string exe = Path.Combine(resolvedHelperRoot, "NexusHealthHost.exe");
                    if (!File.Exists(exe)) { Error = "Optional Monitoring folder is missing"; return; }
                    host = new Process { StartInfo = new ProcessStartInfo(exe) { WorkingDirectory = resolvedHelperRoot, UseShellExecute = false,
                        CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true } };
                    host.OutputDataReceived += (sender, args) => {
                        if (args.Data == null || args.Data.Length > 4096) return;
                        try { var ping = Json().Deserialize<ClientPing>(args.Data); if (ping != null) lock (pingGate) pings[ping.Pid] = ping; } catch { }
                    };
                    host.ErrorDataReceived += (sender, args) => { };
                    host.Start(); host.BeginOutputReadLine(); host.BeginErrorReadLine(); targets = "";
                }
                string next = Json().Serialize(desired);
                if (targets != next) { host.StandardInput.WriteLine(next); host.StandardInput.Flush(); targets = next; }
            } catch { Error = "IPC helper unavailable"; StopHost(); }
        }
        void StopHost()
        {
            if (host == null) return;
            try { host.StandardInput.Close(); if (!host.WaitForExit(500)) host.Kill(); } catch { }
            host.Dispose(); host = null; targets = "";
            lock (pingGate) pings.Clear();
        }
        internal static string ConditionText(ClientCondition state)
        {
            switch (state) {
                case ClientCondition.Healthy: return SidebarText.L("Responding", "Svarer");
                case ClientCondition.Delayed: return SidebarText.L("Delayed", "Forsinket");
                case ClientCondition.NoResponse: return SidebarText.L("No response", "Intet svar");
                case ClientCondition.MeasurementUnavailable: return SidebarText.L("Measurement unavailable", "Måling utilgængelig");
                case ClientCondition.Waiting: return SidebarText.L("Waiting for measurement", "Venter på måling");
                case ClientCondition.Unloaded: return SidebarText.L("Measurement unloaded", "Måling afkoblet");
                case ClientCondition.Exited: return SidebarText.L("Client exited", "Klient afsluttet");
                default: return SidebarText.L("Not measured", "Ikke målt");
            }
        }
        internal string Capture()
        {
            return Json().Serialize(new { Format = 1, CreatedUtc = DateTime.UtcNow, NexusVersion = AppUpdates.CurrentVersion.ToString(),
                WindowsVersion = Environment.OSVersion.Version.ToString(), LogicalProcessors = Environment.ProcessorCount,
                ObserverError = Error, Note = "Local AOSharp responsiveness, not server packet loss. Timeouts may later receive late replies. A process exit alone is not proof of a crash.",
                Clients = clients.Values.Select(t => Report(t)).ToArray() });
        }
        static object Report(Tracked t) => new { Client = t.Alias, ProcessId = t.Client.ProcessId, StartedUtc = new DateTime(t.Client.StartTimeUtcTicks, DateTimeKind.Utc),
            t.ExitedUtc, t.ExitCode, ExecutableVersion = t.Version, MeasurementInstance = t.Instance,
            Assessment = t.ExitedUtc.HasValue ? (t.ExitCode.HasValue && t.ExitCode.Value != 0 ? "Non-zero process exit; inspect Windows evidence. Not proof of root cause." : "Process exited; normal logout and crash are not distinguishable from exit alone.") : "Client was still open at capture.",
            Samples = t.Samples.ToArray() };
        void SaveExit(Tracked item)
        {
            Directory.CreateDirectory(reportRoot);
            string path = Path.Combine(reportRoot, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + item.Client.ProcessId + ".json");
            File.WriteAllText(path, Json().Serialize(Report(item)));
            foreach (var old in new DirectoryInfo(reportRoot).GetFiles("*.json").OrderByDescending(f => f.LastWriteTimeUtc).Skip(20)) old.Delete();
        }
        public void Dispose()
        {
            disposed = true; StopHost(); foreach (var item in clients.Values) item.Dispose(); clients.Clear();
        }
    }
}
