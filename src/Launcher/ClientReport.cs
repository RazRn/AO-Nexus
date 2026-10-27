using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using System.Web.Script.Serialization;

namespace AOLauncher
{
    internal static class ClientReport
    {
        internal sealed class SavedExit
        {
            public string Client { get; set; }
            public int ProcessId { get; set; }
            public DateTime StartedUtc { get; set; }
            public long StartedUtcTicks { get; set; }
            public DateTime? ExitedUtc { get; set; }
            public int? ExitCode { get; set; }
            public string ExecutableVersion { get; set; }
            public string MeasurementInstance { get; set; }
            public string Assessment { get; set; }
            public string SnapshotStatus { get; set; }
            public ClientObservationSummary Summary { get; set; }
            public ClientSample[] Samples { get; set; }
        }
        internal sealed class SessionReport
        {
            public int Format { get; set; }
            public DateTime CreatedUtc { get; set; }
            public string NexusVersion { get; set; }
            public string WindowsVersion { get; set; }
            public string WindowsVersionSource { get; set; }
            public int LogicalProcessors { get; set; }
            public bool? MonitorClientHealth { get; set; }
            public bool? CollectClientExitReports { get; set; }
            public string ObserverError { get; set; }
            public string Note { get; set; }
            public SavedExit[] Clients { get; set; }
        }
        internal static string WindowsEvidence(IEnumerable<ClientObservation> clients)
        {
            // Export supplies current and historical exits with one shared alias map.
            var exits = clients.Where(c => c.ExitedUtc.HasValue).GroupBy(c => c.Pid + "/" + c.StartTicks).Select(g => g.First()).ToList();
            if (exits.Count == 0) return "No observed or saved exits.\r\n";
            var output = new StringBuilder();
            try {
                var query = new EventLogQuery("Application", PathType.LogName,
                    "*[System[(EventID=1000 or EventID=1002) and TimeCreated[timediff(@SystemTime) <= 86400000]]]") { ReverseDirection = true };
                using (var reader = new EventLogReader(query)) {
                    for (int i = 0; i < 500; i++) {
                        using (var record = reader.ReadEvent()) {
                            if (record == null) break;
                            var parsed = MatchEvent(record.ToXml(), exits);
                            if (parsed != null) output.AppendLine(parsed);
                        }
                    }
                }
            } catch (Exception error) { output.AppendLine("Windows Application log unavailable: " + error.GetType().Name); }
            if (output.Length == 0) output.AppendLine("No matching Windows application-error/hang event found. Events can arrive late; export again later. This does not rule out a crash.");
            return output.ToString();
        }
        internal static string MatchEvent(string xml, IEnumerable<ClientObservation> clients)
        {
            var document = XDocument.Parse(xml);
            XNamespace ns = "http://schemas.microsoft.com/win/2004/08/events/event";
            var system = document.Root.Element(ns + "System");
            var fields = document.Descendants(ns + "Data").Where(e => e.Attribute("Name") != null)
                .GroupBy(e => (string)e.Attribute("Name")).ToDictionary(g => g.Key, g => g.First().Value);
            string app, pidText;
            if (!fields.TryGetValue("AppName", out app) || !string.Equals(app, "AnarchyOnline.exe", StringComparison.OrdinalIgnoreCase)
                || !fields.TryGetValue("ProcessId", out pidText)) return null;
            int pid;
            try { pid = pidText.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? Convert.ToInt32(pidText.Substring(2), 16) : int.Parse(pidText); } catch { return null; }
            DateTime utc;
            if (!DateTime.TryParse((string)system.Element(ns + "TimeCreated")?.Attribute("SystemTime"), null, System.Globalization.DateTimeStyles.RoundtripKind, out utc)) return null;
            utc = utc.ToUniversalTime();
            // PID and narrow lifetime/time correlation; never attribute another game's event by filename alone.
            var candidates = clients.Where(c => c.Pid == pid && c.ExitedUtc.HasValue && utc >= new DateTime(c.StartTicks, DateTimeKind.Utc)
                && utc >= c.ExitedUtc.Value.AddMinutes(-2) && utc <= c.ExitedUtc.Value.AddMinutes(2)).ToArray();
            string creation;
            if (fields.TryGetValue("ProcessCreationTime", out creation)) {
                try {
                    long fileTime = creation.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? Convert.ToInt64(creation.Substring(2), 16) : long.Parse(creation);
                    long startTicks = DateTime.FromFileTimeUtc(fileTime).Ticks;
                    candidates = candidates.Where(c => c.StartTicks == startTicks).ToArray();
                } catch { return null; }
            }
            // Without a creation time, ambiguous PID reuse is evidence we cannot safely attribute.
            if (candidates.Length != 1) return null;
            var client = candidates[0];
            var output = new StringBuilder(client.Alias + " · " + utc.ToString("o") + " · Event " + (string)system.Element(ns + "EventID") + "\r\n");
            foreach (string key in new[] { "AppName", "AppVersion", "ModuleName", "ModuleVersion", "ExceptionCode", "FaultingOffset" }) {
                string value;
                if (fields.TryGetValue(key, out value)) {
                    if (key.EndsWith("Name")) value = Path.GetFileName(value);
                    output.AppendLine(key + ": " + DiagnosticRedactor.Current.Redact(value.Length > 160 ? value.Substring(0, 160) : value));
                }
            }
            output.AppendLine("A faulting module identifies where Windows observed the failure; it is not necessarily the root cause.");
            return output.ToString();
        }
        internal static void Export(string path, string current, string evidence, string savedRoot = null)
        {
            var json = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024, RecursionLimit = 32 };
            var session = json.Deserialize<SessionReport>(current) ?? new SessionReport();
            session.Clients = (session.Clients ?? new SavedExit[0]).Where(c => c != null).Take(148).ToArray();
            var identities = new List<SavedExit>();
            int nextAlias = 0, duplicateExits = 0, unreadableExits = 0;
            Action<SavedExit> prepare = report => {
                var same = identities.FirstOrDefault(c => SameProcess(c, report));
                report.Client = same?.Client ?? "Client-" + (++nextAlias);
                if (same == null) identities.Add(report);
                var samples = (report.Samples ?? new ClientSample[0]).Where(s => s != null).ToArray();
                report.Samples = samples.Skip(Math.Max(0, samples.Length - 120)).ToArray();
            };
            foreach (var report in session.Clients) prepare(report);
            var history = new List<SavedExit>();
            string root = savedRoot ?? ClientMonitor.ReportRoot;
            if (Directory.Exists(root)) foreach (var file in new DirectoryInfo(root).GetFiles("*.json").OrderByDescending(f => f.LastWriteTimeUtc).ThenBy(f => f.Name, StringComparer.Ordinal).Take(20)) {
                try {
                    if (file.Length >= 1024 * 1024) { unreadableExits++; continue; }
                    var report = json.Deserialize<SavedExit>(File.ReadAllText(file.FullName));
                    if (report == null) { unreadableExits++; continue; }
                    prepare(report);
                    bool duplicate = session.Clients.Concat(history).Any(c => SameProcess(c, report)
                        && c.ExitedUtc == report.ExitedUtc && c.ExitCode == report.ExitCode
                        && c.MeasurementInstance == report.MeasurementInstance
                        && c.SnapshotStatus == report.SnapshotStatus && c.Assessment == report.Assessment
                        && c.ExecutableVersion == report.ExecutableVersion
                        && SameSamples(c.Samples, report.Samples)
                        && (report.Summary == null || SameSummary(c.Summary, report.Summary)));
                    if (duplicate) duplicateExits++; else history.Add(report);
                } catch { unreadableExits++; }
            }
            var reports = session.Clients.Concat(history).ToArray();
            if (evidence == null) evidence = WindowsEvidence(reports.Select(r => new ClientObservation {
                Alias = r.Client, Pid = r.ProcessId, StartTicks = StartTicks(r), ExitedUtc = r.ExitedUtc, ExitCode = r.ExitCode }));
            // Explicit allowlist: no settings, credentials, chat, dumps, raw event XML or arbitrary game logs.
            using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create)) {
                Add(zip, "README.txt", "AO Nexus client diagnostics (local preview)\r\n\r\n" +
                    "Client names/accounts are replaced by aliases. Includes process IDs, times, OS/Nexus version, memory usage and local responsiveness.\r\n" +
                    "Timeout means no reply within 2 seconds, not proven packet loss. Late replies remain counted as timeouts AND late replies.\r\n" +
                    "No response may mean game/update stall, plugin unload/failure, firewall, or a stopped measurement helper. Zoning can cause pauses.\r\n" +
                    "An exit alone does not prove a crash. Exit code 0 does not guarantee a clean session. Windows evidence may be unavailable or delayed.\r\n" +
                    "A faulting module does not establish root cause. No stack or full memory dump is captured.\r\n" +
                    "Read report-summary.txt first: it states whether responsiveness evidence is present and why monitoring was unavailable when known.\r\n" +
                    "A steady process-sampling interval does not prove that the game was responsive. Memory samples alone cannot prove or exclude a leak.\r\n" +
                    "Recent detail retains up to 120 samples (roughly two minutes). Summary covers the period observed by this Nexus session, including earlier peaks and state counts. Older reports may have no summary. IPC counters reset when the helper restarts.\r\n" +
                    "Aliases are shared across this ZIP and identify a process lifetime, not a permanent account. Duplicate saved exits are omitted when their data is already present; distinct evidence is retained.\r\n" +
                    "Review the ZIP before sharing. Nothing is uploaded automatically.\r\n");
                Add(zip, "current-session.json", json.Serialize(session));
                Add(zip, "report-summary.txt", SummaryText(session, reports, duplicateExits, unreadableExits));
                Add(zip, "windows-evidence.txt", evidence);
                for (int i = 0; i < history.Count; i++)
                    Add(zip, "saved-exits/" + history[i].Client + "-" + (i + 1) + ".json", json.Serialize(history[i]));
            }
        }
        static long StartTicks(SavedExit report) => report.StartedUtcTicks > 0 ? report.StartedUtcTicks : report.StartedUtc.ToUniversalTime().Ticks;
        // Compare evidence values, not JSON property ordering (reflection order can vary).
        static readonly System.Reflection.PropertyInfo[] SampleFields = typeof(ClientSample).GetProperties();
        static readonly System.Reflection.PropertyInfo[] SummaryFields = typeof(ClientObservationSummary).GetProperties()
            .Where(p => p.Name != nameof(ClientObservationSummary.StateSamples)).ToArray();
        static bool SameSamples(ClientSample[] a, ClientSample[] b) => a.Length == b.Length
            && a.Zip(b, (left, right) => SampleFields.All(p => Equals(p.GetValue(left), p.GetValue(right)))).All(equal => equal);
        static bool SameSummary(ClientObservationSummary a, ClientObservationSummary b)
        {
            if (a == null || b == null) return a == b;
            if (!SummaryFields.All(p => Equals(p.GetValue(a), p.GetValue(b)))) return false;
            var left = a.StateSamples ?? new Dictionary<string, long>();
            var right = b.StateSamples ?? new Dictionary<string, long>();
            return left.Count == right.Count && left.All(pair => right.ContainsKey(pair.Key) && right[pair.Key] == pair.Value);
        }
        internal static bool SameProcess(SavedExit a, SavedExit b)
        {
            if (a.ProcessId != b.ProcessId) return false;
            if (a.StartedUtcTicks > 0 && b.StartedUtcTicks > 0) return a.StartedUtcTicks == b.StartedUtcTicks;
            // Legacy JavaScriptSerializer dates retain milliseconds, not every OS tick.
            return StartTicks(a) / TimeSpan.TicksPerMillisecond == StartTicks(b) / TimeSpan.TicksPerMillisecond;
        }
        static string SummaryText(SessionReport session, SavedExit[] reports, int duplicates, int unreadable)
        {
            var clients = reports.GroupBy(r => r.Client).ToArray();
            int measured = clients.Count(g => g.Any(r => !string.IsNullOrEmpty(r.MeasurementInstance)
                || r.Summary?.SamplesWithPluginData > 0 || r.Samples.Any(s => s.PulseAgeMs.HasValue)));
            var text = new StringBuilder();
            text.AppendLine(measured == 0 ? "NO RESPONSIVENESS EVIDENCE: this report contains no accepted monitoring-plugin measurements."
                : "Monitoring-plugin evidence is present for " + measured + " of " + clients.Length + " process lifetimes; inspect freshness and state before interpreting it.");
            text.AppendLine("Regular process samples do not prove the game was responding. Exit code 0 and missing Windows events do not rule out a crash or hang. A short memory window cannot establish whether a leak exists.");
            text.AppendLine("Responsiveness monitoring at export: " + (session.MonitorClientHealth.HasValue ? session.MonitorClientHealth.Value ? "enabled" : "disabled" : "not recorded by this Nexus version"));
            text.AppendLine("Earlier detail outside the retained sample window is unavailable. Summaries, when present, cover only the time this Nexus session observed the client; they cannot reconstruct unobserved history.");
            text.AppendLine("Saved exits omitted as duplicates: " + duplicates + ". Unreadable/oversized saved reports omitted: " + unreadable + ".");
            text.AppendLine();
            foreach (var group in clients) {
                var report = group.First();
                text.AppendLine(report.Client + " (PID " + report.ProcessId + ") · snapshot status: " + (report.SnapshotStatus ?? "not recorded"));
                text.AppendLine(ClientMonitor.SnapshotStatusText(report.SnapshotStatus));
                var summary = group.Where(r => r.Summary != null).Select(r => r.Summary).OrderByDescending(s => s.TotalSamples).FirstOrDefault();
                if (summary != null) text.AppendLine("Observed samples: " + summary.TotalSamples + "; samples with plugin data: " + summary.SamplesWithPluginData + "; peak observed working set: " + summary.PeakObservedWorkingSetBytes + " bytes.");
            }
            if (measured < clients.Length) text.AppendLine("For missing measurements: enable monitoring in Nexus Settings > Accounts; load Nexus Health Monitor into each affected character through AOSharp under the same Windows user. Merely opening AOSharp or adding the DLL is not enough. Export again after the status changes from Not measured. Missing/invalid snapshot reasons do not by themselves establish the root cause.");
            return text.ToString();
        }
        static void Add(ZipArchive zip, string name, string content)
        {
            using (var writer = new StreamWriter(zip.CreateEntry(name, CompressionLevel.Optimal).Open(), new UTF8Encoding(false)))
                writer.Write(DiagnosticRedactor.Current.Redact(content));
        }
    }
}
