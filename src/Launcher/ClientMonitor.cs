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
        public long VitalsQpc { get; set; }
        public int Health { get; set; }
        public int MaxHealth { get; set; }
        public int Nano { get; set; }
        public int MaxNano { get; set; }
        public string Profession { get; set; }
        public bool? IsTeamLeader { get; set; }
        public long TeamStateQpc { get; set; }
        public string FightingTarget { get; set; }
        public bool Attacking { get; set; }
        public long AggroQpc { get; set; }
        public int AggroCount { get; set; }
        public string[] AggroNames { get; set; }
        public long CastAttemptQpc { get; set; }
        public int CastNanoId { get; set; }
        public string CastNanoName { get; set; }
        public NexusTelemetry.CombatMeterSnapshot CombatMeter { get; set; }
        public NexusTelemetry.CombatMeterSnapshot ObservedCombatMeter { get; set; }
        public NexusTelemetry.CombatMeterSnapshot NpcCombatMeter { get; set; }
        public int PlayfieldId { get; set; }
        public NexusTelemetry.MovementSnapshot Movement { get; set; }
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
        public string SnapshotStatus { get; set; }
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
        public bool? Zoning { get; set; }
        public long WorkingSetBytes { get; set; }
        public bool? WindowResponding { get; set; }
        public int WindowUnresponsiveSamples { get; set; }
        public double? CpuTotalMs { get; set; }
        public double? CpuPercent { get; set; }
        public long? FreeClientBytes { get; set; }
        public long? LargestFreeClientBytes { get; set; }
        public long? ClientAddressBytes { get; set; }
        public long MemoryMeasuredQpc { get; set; }
    }
    internal sealed class ClientObservationSummary
    {
        public DateTime? FirstObservedUtc { get; set; }
        public DateTime? LastObservedUtc { get; set; }
        public DateTime? FirstPluginSampleUtc { get; set; }
        public DateTime? LastPluginSampleUtc { get; set; }
        public long TotalSamples { get; set; }
        public long SamplesWithPluginData { get; set; }
        public long FreshPluginSamples { get; set; }
        public long PeakObservedWorkingSetBytes { get; set; }
        public double? LongestObservedUpdateGapMs { get; set; }
        public double? LargestPluginReportedGapMs { get; set; }
        public double? LargestObservedReplyMs { get; set; }
        public double LongestObserverSampleIntervalMs { get; set; }
        public Dictionary<string, long> StateSamples { get; set; } = new Dictionary<string, long>();
        internal void Observe(ClientSample sample)
        {
            if (LastObservedUtc.HasValue)
                LongestObserverSampleIntervalMs = Math.Max(LongestObserverSampleIntervalMs, (sample.Utc - LastObservedUtc.Value).TotalMilliseconds);
            if (!FirstObservedUtc.HasValue) FirstObservedUtc = sample.Utc;
            LastObservedUtc = sample.Utc; TotalSamples++;
            if (sample.PulseAgeMs.HasValue) {
                SamplesWithPluginData++;
                if (!FirstPluginSampleUtc.HasValue) FirstPluginSampleUtc = sample.Utc;
                LastPluginSampleUtc = sample.Utc;
                if (sample.PulseAgeMs.Value < 3000 && sample.State != ClientCondition.Unloaded.ToString()) FreshPluginSamples++;
            }
            PeakObservedWorkingSetBytes = Math.Max(PeakObservedWorkingSetBytes, sample.WorkingSetBytes);
            LongestObservedUpdateGapMs = Max(LongestObservedUpdateGapMs, sample.ObservedUpdateGapMs);
            LargestPluginReportedGapMs = Max(LargestPluginReportedGapMs, sample.MaxGapMs);
            LargestObservedReplyMs = Max(LargestObservedReplyMs, sample.ReplyMs);
            ClientCondition state;
            if (Enum.TryParse(sample.State, out state) && Enum.IsDefined(typeof(ClientCondition), state)) {
                string key = state.ToString(); long count;
                StateSamples.TryGetValue(key, out count); StateSamples[key] = count + 1;
            }
        }
        static double? Max(double? previous, double? value) => !value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value)
            ? previous : !previous.HasValue ? value : Math.Max(previous.Value, value.Value);
    }
    internal sealed class ClientObservation
    {
        internal PresetCamping.Client Client;
        internal ClientPulse Pulse;
        internal string Name, Alias;
        internal int Pid;
        internal long StartTicks;
        internal ClientCondition Condition;
        internal ClientSample Sample;
        internal string SnapshotStatus;
        internal bool Measured;
        internal DateTime? ExitedUtc;
        internal int? ExitCode;
        internal bool CastWhileFalling, CastIssueConfirmed;
        internal int CastIssueCount;
        internal bool HasCastIssue => Measured && !ExitedUtc.HasValue && Condition != ClientCondition.Unloaded && CastWhileFalling;
    }
    // Read-only address-map inspection; never reads or modifies game memory contents.
    // The process handle is retained by ClientMonitor, protecting against PID reuse.
    internal sealed class ClientAddressSpace
    {
        internal long Free, Largest, Total;
        [StructLayout(LayoutKind.Sequential)] struct Region {
            internal IntPtr Base, AllocationBase;
            internal uint AllocationProtect;
            internal UIntPtr Size;
            internal uint State, Protect, Type;
        }
        [DllImport("kernel32.dll",SetLastError=true)] static extern UIntPtr VirtualQueryEx(IntPtr process,IntPtr address,out Region region,UIntPtr size);
        [DllImport("kernel32.dll",SetLastError=true)] static extern bool IsWow64Process(IntPtr process,out bool wow64);
        internal static ClientAddressSpace Read(IntPtr process)
        {
            try {
                if(!Environment.Is64BitOperatingSystem || !IsWow64Process(process,out bool wow64) || !wow64)return null;
                var result=new ClientAddressSpace();ulong address=0x10000;
                var budget=Stopwatch.StartNew();var size=new UIntPtr((uint)Marshal.SizeOf(typeof(Region)));
                for(int count=0;count<65536 && budget.ElapsedMilliseconds<50;count++) {
                    var pointer=IntPtr.Size==4?new IntPtr(unchecked((int)(uint)address)):new IntPtr((long)address);
                    if(VirtualQueryEx(process,pointer,out var region,size)==UIntPtr.Zero) {
                        if(Marshal.GetLastWin32Error()!=87 || address<0x7fff0000)return null;
                        result.Total=(long)address;return result;
                    }
                    ulong start=IntPtr.Size==4?unchecked((uint)region.Base.ToInt32()):(ulong)region.Base.ToInt64();
                    ulong end=Math.Min(0x100000000UL,start+region.Size.ToUInt64());
                    if(start>address || end<=address)return null;
                    if(region.State==0x10000) {long bytes=(long)(end-address);result.Free+=bytes;result.Largest=Math.Max(result.Largest,bytes);}
                    address=end;
                    if(address==0x100000000UL) {result.Total=(long)address;return result;}
                }
            } catch { }
            return null; // Incomplete/denied measurements must never look like zero free memory.
        }
        internal static int Warning(long? free,long? largest) => !free.HasValue || !largest.HasValue?0:
            free<256L*1024*1024 || largest<32L*1024*1024?2:free<512L*1024*1024 || largest<64L*1024*1024?1:0;
        internal static double? CpuPercent(double? before,double? after,long ticks,int processors)
            => !before.HasValue || !after.HasValue || after<before || ticks<=0 || processors<=0?null:
                (double?)Math.Min(100,100*(after.Value-before.Value)/(ticks*1000.0/Stopwatch.Frequency)/processors);
        internal static bool Fresh(ClientSample sample,long now)=>sample?.MemoryMeasuredQpc>0 && now>=sample.MemoryMeasuredQpc
            && now-sample.MemoryMeasuredQpc<=12*Stopwatch.Frequency && sample.FreeClientBytes.HasValue;
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
            internal string SnapshotStatus;
            internal int WindowUnresponsiveSamples;
            internal ClientAddressSpace Memory;
            internal long MemoryCheckedQpc, CpuCheckedQpc;
            internal double? PreviousCpuMs;
            internal int MemoryWarning;
            internal readonly ClientObservationSummary Summary = new ClientObservationSummary();
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
        bool monitoringEnabled, collectExitReports;
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
        internal static ClientCondition Assess(ClientPulse pulse, ClientPing ping, long now, long firstPulse, out string reason)
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
                reason = AOLauncher.Localization.Texts.Get("Text_No_game_update_at_snapshot_283A80008284");
                return ClientCondition.NoResponse;
            }
            if (freshHelper && !grace && (ping.LastReplyQpc < firstPulse || Age(now, ping.LastReplyQpc) >= 3000)) {
                reason = AOLauncher.Localization.Texts.Get("Text_IPC_reply_missing_helper_is_reporting_A04D7E5C19F0");
                return ClientCondition.NoResponse;
            }
            if (!freshPulse) {
                reason = AOLauncher.Localization.Texts.Get("Text_Measurement_snapshot_is_stale_0C7996911B2C");
                return ClientCondition.MeasurementUnavailable;
            }
            if (!freshHelper) {
                reason = grace ? AOLauncher.Localization.Texts.Get("Text_Waiting_for_first_IPC_measurement_7129B72E59DA")
                    : AOLauncher.Localization.Texts.Get("Text_IPC_helper_measurement_unavailable_60EAE3168FBC");
                return grace ? ClientCondition.Waiting : ClientCondition.MeasurementUnavailable;
            }
            if (ping.LastReplyQpc < firstPulse || pulse.LastUpdateQpc == 0) return ClientCondition.Waiting;
            if (gap >= 1000) {
                reason = AOLauncher.Localization.Texts.Get("Text_Game_update_stalled_at_snapshot_BF20F7ACA3FC");
                return ClientCondition.Delayed;
            }
            if (ping.LastMs >= 100) {
                reason = AOLauncher.Localization.Texts.Get("Text_Slow_IPC_round_trip_E3C7593B1ABA");
                return ClientCondition.Delayed;
            }
            if (pulse.RecentMaxMs >= 100) {
                reason = AOLauncher.Localization.Texts.Get("Text_Recent_game_update_spike_last_10_587A7692084E");
                return ClientCondition.Delayed;
            }
            return ClientCondition.Healthy;
        }
        internal static string ChangeDetail(ClientSample sample)
        {
            if (string.IsNullOrEmpty(sample.Reason)) return "";
            Func<double?, string, string> ms = (value, format) => value.HasValue && !double.IsInfinity(value.Value) && !double.IsNaN(value.Value)
                ? value.Value.ToString(format) + " ms" : AOLauncher.Localization.Texts.Get("Text_unavailable_52FDD89E09B1");
            return string.Format(" — {0} (snapshot {1}; update gap {2}; max/10s {3}; helper {4}; reply age {5}; RTT {6}; timeouts {7})",
                sample.Reason, ms(sample.PulseAgeMs, "F0"), ms(sample.ObservedUpdateGapMs, "F0"), ms(sample.RecentMaxGapMs, "F1"),
                ms(sample.HelperAgeMs, "F0"), ms(sample.ReplyAgeMs, "F0"), ms(sample.ReplyMs, "F1"),
                sample.Timeouts.HasValue ? sample.Timeouts.Value.ToString() : AOLauncher.Localization.Texts.Get("Text_unavailable_52FDD89E09B1"));
        }
        internal static ClientPulse ReadPulse(string path)
        {
            // The plugin publishes atomically with File.Replace. Allow replacement while
            // reading the old complete file, and bound the read on the opened handle.
            for (int attempt = 0; ; attempt++) {
                try {
                    using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
                        // Two independent, bounded 128-actor feeds (players and NPCs).
                        if (file.Length >= 512 * 1024) return null;
                        using (var reader = new StreamReader(file)) {
                            var buffer = new char[(int)file.Length + 1];
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
        internal static string ValidatePulse(ClientPulse pulse, int pid, long startTicks, long now)
        {
            if (pulse == null) return "SnapshotInvalid";
            if (pulse.Version != 2) return "SnapshotVersionUnsupported";
            if (pulse.Pid != pid || pulse.StartTicks != startTicks) return "SnapshotIdentityMismatch";
            if (string.IsNullOrEmpty(pulse.Instance) || pulse.Instance.Length > 64 || pulse.PublishedQpc <= 0 || pulse.PublishedQpc > now
                || pulse.LastUpdateQpc < 0 || pulse.LastUpdateQpc > pulse.PublishedQpc || pulse.RecentMaxMs < 0 || pulse.MaxMs < 0
                || double.IsNaN(pulse.RecentMaxMs) || double.IsInfinity(pulse.RecentMaxMs) || double.IsNaN(pulse.MaxMs) || double.IsInfinity(pulse.MaxMs))
                return "SnapshotInvalid";
            return "SnapshotAccepted";
        }
        internal static string SnapshotStatusText(string status)
        {
            switch (status) {
                case "MonitoringDisabled": return AOLauncher.Localization.Texts.Get("Text_Responsiveness_monitoring_is_turned_off_in_437F19F1A683");
                case "SnapshotMissing": return AOLauncher.Localization.Texts.Get("Text_No_monitoring_snapshot_was_found_for_A3BEEB3479FD");
                case "SnapshotIdentityMismatch": return AOLauncher.Localization.Texts.Get("Text_A_snapshot_was_found_but_its_9251CEA08084");
                case "SnapshotVersionUnsupported": return AOLauncher.Localization.Texts.Get("Text_The_monitoring_snapshot_uses_an_unsupported_93A13FDCC2F3");
                case "SnapshotUnreadable": return AOLauncher.Localization.Texts.Get("Text_The_monitoring_snapshot_could_not_be_29E3C18E4B8F");
                case "SnapshotInvalid": return AOLauncher.Localization.Texts.Get("Text_The_monitoring_snapshot_was_incomplete_or_0F226EA6069B");
                case "SnapshotAccepted": return AOLauncher.Localization.Texts.Get("Text_A_snapshot_matching_this_process_was_DF908D5C65F8");
                default: return AOLauncher.Localization.Texts.Get("Text_The_reason_was_not_recorded_by_44FF701CBCCB");
            }
        }
        internal void Poll(LauncherSettings settings)
        {
            if (disposed) return;
            monitoringEnabled = settings.MonitorClientHealth; collectExitReports = settings.CollectClientExitReports;
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
                    } catch (ArgumentException) { /* The process exited before its handle was opened. */ }
                    catch (Exception error) { FailureDiagnostics.Monitor.Report("track-client", "Could not inspect client", error, pid: client.ProcessId, startTicks: client.StartTimeUtcTicks); } finally { process?.Dispose(); }
                }
                FailureDiagnostics.Monitor.Recovered("session-discovery", "Client discovery resumed");
            } catch (Exception error) { Error = "Session discovery unavailable"; FailureDiagnostics.Monitor.Report("session-discovery", Error, error); }
            long now = Stopwatch.GetTimestamp();
            foreach (var entry in clients.ToArray()) {
                var item = entry.Value;
                if (item.ExitedUtc.HasValue) continue;
                try {
                    if (item.Process.HasExited) {
                        item.ExitedUtc = DateTime.UtcNow;
                        try { item.ExitCode = item.Process.ExitCode; } catch { }
                        item.Condition = ClientCondition.Exited;
                        Changes.Add(item.Client.CharacterName + ": " + AOLauncher.Localization.Texts.Get("Text_client_exited_4C1F302F4AAB"));
                        if (settings.CollectClientExitReports) try { SaveExit(item); } catch (Exception error) { Error = "Could not save exit report"; FailureDiagnostics.Monitor.Report("save-exit", Error, error, pid: item.Client.ProcessId, startTicks: item.Client.StartTimeUtcTicks); }
                        continue;
                    }
                    string path = Path.Combine(pulseRoot, entry.Key + ".json");
                    try {
                        item.SnapshotStatus = settings.MonitorClientHealth ? "SnapshotMissing" : "MonitoringDisabled";
                        if (!settings.MonitorClientHealth) item.Pulse = null;
                        if (settings.MonitorClientHealth && File.Exists(path)) {
                            var pulse = ReadPulse(path);
                            now = Stopwatch.GetTimestamp(); // Validate against a clock sampled AFTER the read.
                            item.SnapshotStatus = ValidatePulse(pulse, item.Client.ProcessId, item.Client.StartTimeUtcTicks, now);
                            if (item.SnapshotStatus == "SnapshotAccepted") {
                                if (item.Instance != pulse.Instance) {
                                    item.FirstPulse = now; item.Instance = pulse.Instance;
                                    lock (pingGate) pings.Remove(item.Client.ProcessId);
                                }
                                item.Pulse = pulse;
                                FailureDiagnostics.Monitor.Recovered("read-snapshot", "Valid snapshot received", item.Client.ProcessId, item.Client.StartTimeUtcTicks);
                            }
                        }                    } catch (Exception error) {
                        item.SnapshotStatus = "SnapshotUnreadable";
                        // Parser exceptions may quote source data. Report the type without the payload.
                        FailureDiagnostics.Monitor.Report("read-snapshot", item.SnapshotStatus,
                            detail: error.GetType().Name + " HRESULT=" + error.HResult,
                            pid: item.Client.ProcessId, startTicks: item.Client.StartTimeUtcTicks);
                        // Retain prior evidence; Assess checks its age.
                    }
                    ClientPing ping;
                    lock (pingGate) pings.TryGetValue(item.Client.ProcessId, out ping);
                    if (item.Pulse == null || (ping != null && ping.StartTicks != item.Client.StartTimeUtcTicks)) ping = null;
                    now = Stopwatch.GetTimestamp(); // The helper can publish while the file read is in progress.
                    string reason;
                    var condition = Assess(item.Pulse, ping, now, item.FirstPulse, out reason);
                    if (condition == ClientCondition.Unmeasured) reason = SnapshotStatusText(item.SnapshotStatus);
                    item.Process.Refresh();
                    IntPtr window = item.Process.MainWindowHandle;
                    bool? windowResponding=ClientRecovery.WindowResponding(window,item.Client.ProcessId);
                    item.WindowUnresponsiveSamples=windowResponding==false?Math.Min(1000,item.WindowUnresponsiveSamples+1):0;
                    double? cpuMs=null;try {cpuMs=item.Process.TotalProcessorTime.TotalMilliseconds;}catch { }
                    double? cpuPercent=ClientAddressSpace.CpuPercent(item.PreviousCpuMs,cpuMs,now-item.CpuCheckedQpc,Environment.ProcessorCount);
                    item.PreviousCpuMs=cpuMs;item.CpuCheckedQpc=now;
                    if(item.MemoryCheckedQpc==0 || now-item.MemoryCheckedQpc>=5*Stopwatch.Frequency) {
                        item.Memory=ClientAddressSpace.Read(item.Process.Handle);item.MemoryCheckedQpc=Stopwatch.GetTimestamp();
                        int warning=ClientAddressSpace.Warning(item.Memory?.Free,item.Memory?.Largest);
                        if(warning>0 && warning!=item.MemoryWarning)Changes.Add(item.Client.CharacterName+": "+AOLauncher.Localization.Texts.Get("Text_Low_client_memory_368F71A32633")
                            +(item.Memory.Free/(1024*1024))+AOLauncher.Localization.Texts.Get("Text_MB_free_consider_relogging_before_zoning_174154FEA63A"));
                        item.MemoryWarning=warning;
                    }
                    var sample = new ClientSample { Utc = DateTime.UtcNow, State = condition.ToString(), Reason = reason, SnapshotStatus = item.SnapshotStatus,
                        WindowResponding=windowResponding,WindowUnresponsiveSamples=item.WindowUnresponsiveSamples,CpuTotalMs=cpuMs,
                        CpuPercent=cpuPercent,FreeClientBytes=item.Memory?.Free,LargestFreeClientBytes=item.Memory?.Largest,
                        ClientAddressBytes=item.Memory?.Total,MemoryMeasuredQpc=item.Memory!=null?item.MemoryCheckedQpc:0,
                        PulseAgeMs = item.Pulse != null ? (double?)Age(now, item.Pulse.PublishedQpc) : null,
                        ObservedUpdateGapMs = item.Pulse?.LastUpdateQpc > 0 ? (double?)Age(item.Pulse.PublishedQpc, item.Pulse.LastUpdateQpc) : null,
                        HelperAgeMs = ping != null ? (double?)Age(now, ping.PublishedQpc) : null,
                        ReplyAgeMs = ping?.LastReplyQpc > 0 ? (double?)Age(now, ping.LastReplyQpc) : null,
                        UpdateAgeMs = item.Pulse?.LastUpdateQpc > 0 ? (double?)Age(now, item.Pulse.LastUpdateQpc) : null,
                        RecentMaxGapMs = item.Pulse?.RecentMaxMs, MaxGapMs = item.Pulse?.MaxMs,
                        ReplyMs = ping?.LastReplyQpc > 0 ? (double?)ping.LastMs : null,
                        Sent = ping?.Sent, Replies = ping?.Replies, Timeouts = ping?.Timeouts, Late = ping?.Late, Pending = ping?.Pending,
                        Zoning = item.Pulse?.Zoning, Minimized = window != IntPtr.Zero && IsIconic(window),
                        Foreground = window != IntPtr.Zero && window == GetForegroundWindow(), WorkingSetBytes = item.Process.WorkingSet64 };
                    if(item.WindowUnresponsiveSamples==3)Changes.Add(item.Client.CharacterName+": "+AOLauncher.Localization.Texts.Get("Text_game_window_is_not_responding_use_B703DA225262"));
                    if (condition != item.Condition && item.Pulse != null) {
                        string detail = ChangeDetail(sample);
                        Changes.Add(item.Client.CharacterName + ": AO# · " + ConditionText(condition) + detail);
                    }
                    item.Condition = condition;
                    bool castIssue = HasRecentCastIssue(item.Pulse, now);
                    if (castIssue && !item.CastIssueVisible)
                        Changes.Add(item.Client.CharacterName + ": " + AOLauncher.Localization.Texts.Get("Text_Nano_cast_attempted_while_falling_Check_38960E16A363"));
                    item.CastIssueVisible = castIssue;
                    item.Summary.Observe(sample);
                    item.Samples.Enqueue(sample); while (item.Samples.Count > 120) item.Samples.Dequeue();
                } catch (Exception error) { Error = "One client could not be inspected"; FailureDiagnostics.Monitor.Report("inspect-client", Error, error, pid: item.Client.ProcessId, startTicks: item.Client.StartTimeUtcTicks); }
            }
            foreach (var old in clients.Where(c => c.Value.ExitedUtc.HasValue).OrderByDescending(c => c.Value.ExitedUtc).Skip(20).ToArray()) {
                old.Value.Dispose(); clients.Remove(old.Key);
            }
            Current = clients.Values.Select(t => new ClientObservation { Client=t.Client, Pulse=t.Pulse, Name = t.Client.CharacterName, Alias = t.Alias, Pid = t.Client.ProcessId,
                StartTicks = t.Client.StartTimeUtcTicks, Condition = t.Condition, Measured = t.Pulse != null && t.Condition != ClientCondition.Unloaded,
                Sample = t.Samples.LastOrDefault(), SnapshotStatus = t.SnapshotStatus, ExitedUtc = t.ExitedUtc, ExitCode = t.ExitCode,
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
                        try { var ping = Json().Deserialize<ClientPing>(args.Data); if (ping != null) lock (pingGate) pings[ping.Pid] = ping; } catch (Exception error) { FailureDiagnostics.Monitor.Report("parse-helper", "Invalid helper response", detail: error.GetType().Name + " HRESULT=" + error.HResult); }
                    };
                    host.ErrorDataReceived += (sender, args) => { if (!string.IsNullOrEmpty(args.Data)) FailureDiagnostics.Monitor.Report("helper-stderr", "Helper reported an error", detail: args.Data); };
                    host.Start(); host.BeginOutputReadLine(); host.BeginErrorReadLine(); targets = "";
                }
                string next = Json().Serialize(desired);
                if (targets != next) { host.StandardInput.WriteLine(next); host.StandardInput.Flush(); targets = next; }
                FailureDiagnostics.Monitor.Recovered("ipc-helper", "Helper available");
            } catch (Exception error) { Error = "IPC helper unavailable"; FailureDiagnostics.Monitor.Report("ipc-helper", Error, error); StopHost(); }
        }
        void StopHost()
        {
            if (host == null) return;
            try { host.StandardInput.Close(); if (!host.WaitForExit(500)) host.Kill(); } catch (InvalidOperationException) { /* Helper already exited. */ } catch (Exception error) { FailureDiagnostics.Monitor.Report("stop-helper", "Could not stop owned helper", error); }
            host.Dispose(); host = null; targets = "";
            lock (pingGate) pings.Clear();
        }
        internal static string ConditionText(ClientCondition state)
        {
            switch (state) {
                case ClientCondition.Healthy: return AOLauncher.Localization.Texts.Get("Text_Responding_35E6019771E7");
                case ClientCondition.Delayed: return AOLauncher.Localization.Texts.Get("Text_Delayed_17C12ABE1A14");
                case ClientCondition.NoResponse: return AOLauncher.Localization.Texts.Get("Text_No_response_1C26BD324EDD");
                case ClientCondition.MeasurementUnavailable: return AOLauncher.Localization.Texts.Get("Text_Measurement_unavailable_20443020E9B2");
                case ClientCondition.Waiting: return AOLauncher.Localization.Texts.Get("Text_Waiting_for_measurement_3C7611EBF965");
                case ClientCondition.Unloaded: return AOLauncher.Localization.Texts.Get("Text_Measurement_unloaded_A5A74531791E");
                case ClientCondition.Exited: return AOLauncher.Localization.Texts.Get("Text_Client_exited_F38A078665CD");
                default: return AOLauncher.Localization.Texts.Get("Text_Not_measured_61E24E1E3CEB");
            }
        }
        internal string Capture()
        {
            return Json().Serialize(new { Format = 2, CreatedUtc = DateTime.UtcNow, NexusVersion = AppUpdates.CurrentVersion.ToString(),
                WindowsVersion = DiagnosticSnapshot.WindowsVersion(), WindowsVersionSource = "Windows registry", LogicalProcessors = Environment.ProcessorCount,
                MonitorClientHealth = monitoringEnabled, CollectClientExitReports = collectExitReports,
                ObserverError = Error, Note = "Process sampling is independent of game responsiveness. Unmeasured clients have no responsiveness evidence. Exit code 0 and absent Windows events do not rule out a crash or hang. Summaries cover only the period observed by this Nexus session; recent samples retain up to 120 observations. Memory samples alone cannot prove or exclude a leak.",
                Clients = clients.Values.Select(t => Report(t)).ToArray() });
        }
        static object Report(Tracked t) => new { Client = t.Alias, ProcessId = t.Client.ProcessId, StartedUtc = new DateTime(t.Client.StartTimeUtcTicks, DateTimeKind.Utc),
            StartedUtcTicks = t.Client.StartTimeUtcTicks,
            t.ExitedUtc, t.ExitCode, ExecutableVersion = t.Version, MeasurementInstance = t.Instance,
            SnapshotStatus = t.SnapshotStatus, Summary = t.Summary,
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
