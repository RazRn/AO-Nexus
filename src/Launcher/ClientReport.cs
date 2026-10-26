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
            public DateTime? ExitedUtc { get; set; }
            public int? ExitCode { get; set; }
            public string ExecutableVersion { get; set; }
            public string MeasurementInstance { get; set; }
            public string Assessment { get; set; }
            public ClientSample[] Samples { get; set; }
        }
        internal static string WindowsEvidence(IEnumerable<ClientObservation> clients)
        {
            var exits = clients.Where(c => c.ExitedUtc.HasValue).ToList();
            if (Directory.Exists(ClientMonitor.ReportRoot)) foreach (var file in new DirectoryInfo(ClientMonitor.ReportRoot).GetFiles("*.json").OrderByDescending(f => f.LastWriteTimeUtc).Take(20)) {
                try {
                    if (file.Length > 1024 * 1024) continue;
                    var saved = new JavaScriptSerializer().Deserialize<SavedExit>(File.ReadAllText(file.FullName));
                    if (saved == null || !saved.ExitedUtc.HasValue || exits.Any(c => c.Pid == saved.ProcessId && c.StartTicks == saved.StartedUtc.Ticks)) continue;
                    exits.Add(new ClientObservation { Alias = "Client-" + saved.ProcessId, Pid = saved.ProcessId, StartTicks = saved.StartedUtc.Ticks, ExitedUtc = saved.ExitedUtc });
                } catch { }
            }
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
            // Explicit allowlist: no settings, credentials, chat, dumps, raw event XML or arbitrary game logs.
            using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create)) {
                Add(zip, "README.txt", "AO Nexus client diagnostics (local preview)\r\n\r\n" +
                    "Client names/accounts are replaced by aliases. Includes process IDs, times, OS/Nexus version, memory usage and local responsiveness.\r\n" +
                    "Timeout means no reply within 2 seconds, not proven packet loss. Late replies remain counted as timeouts AND late replies.\r\n" +
                    "No response may mean game/update stall, plugin unload/failure, firewall, or a stopped measurement helper. Zoning can cause pauses.\r\n" +
                    "An exit alone does not prove a crash. Exit code 0 does not guarantee a clean session. Windows evidence may be unavailable or delayed.\r\n" +
                    "A faulting module does not establish root cause. No stack or full memory dump is captured.\r\n" +
                    "Current samples cover the last ~120 seconds while Nexus runs. IPC counters reset when the helper restarts.\r\n" +
                    "Review the ZIP before sharing. Nothing is uploaded automatically.\r\n");
                Add(zip, "current-session.json", current);
                Add(zip, "windows-evidence.txt", evidence);
                string root = savedRoot ?? ClientMonitor.ReportRoot;
                if (Directory.Exists(root))
                    foreach (var file in new DirectoryInfo(root).GetFiles("*.json").OrderByDescending(f => f.LastWriteTimeUtc).Take(20)) {
                        if (file.Length >= 1024 * 1024) continue;
                        try {
                            var json = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024, RecursionLimit = 16 };
                            var report = json.Deserialize<SavedExit>(File.ReadAllText(file.FullName));
                            if (report == null) continue;
                            report.Client = "Client-" + report.ProcessId;
                            report.Samples = (report.Samples ?? new ClientSample[0]).Take(120).ToArray();
                            Add(zip, "saved-exits/exit-" + report.ProcessId + "-" + report.StartedUtc.Ticks + ".json", json.Serialize(report));
                        } catch { Add(zip, "saved-exits/unreadable-" + Guid.NewGuid().ToString("N") + ".txt", "A saved exit report was unreadable; no raw contents included."); }
                    }
            }
        }
        static void Add(ZipArchive zip, string name, string content)
        {
            using (var writer = new StreamWriter(zip.CreateEntry(name, CompressionLevel.Optimal).Open(), new UTF8Encoding(false)))
                writer.Write(DiagnosticRedactor.Current.Redact(content));
        }
    }
}
