using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using static AOLauncher.SidebarText;

namespace AOLauncher
{
    internal static class PluginRuntimeReport
    {
        internal sealed class Entry { public string Path { get; set; } public string State { get; set; } public string Error { get; set; } }
        internal sealed class Conflict { public string Resource { get; set; } public string Previous { get; set; } public string Current { get; set; } }
        internal sealed class Report
        {
            public int Format { get; set; } public int Pid { get; set; } public long StartTicks { get; set; }
            public string RequestId { get; set; }
            public List<Entry> Plugins { get; set; } public List<Conflict> Conflicts { get; set; }
        }
        internal static List<PluginCheck> Read(PluginHostClient.ClientState client, Func<string,string> nameOf,
            string directory = null, FailureDiagnostics diagnostics = null)
        {
            if (client == null || string.IsNullOrEmpty(client.RequestId)) return null;
            diagnostics = diagnostics ?? FailureDiagnostics.Plugins;
            string root = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AONexus", "PluginRuntime");
            string file = Path.Combine(root, client.Pid + "-" + client.StartTicks + ".json");
            try
            {
                if (new FileInfo(file).Length > 512 * 1024)
                {
                    diagnostics.Report("plugin-report/read", "Startup remains unconfirmed; report rejected",
                        detail: "Plugin startup report exceeds 512 KiB.", pid: client.Pid, startTicks: client.StartTicks);
                    return null;
                }
                var result = Parse(File.ReadAllText(file), client, nameOf, diagnostics);
                if (result != null)
                {
                    diagnostics.Recovered("plugin-report/read", "Current startup report readable", client.Pid, client.StartTicks);
                    foreach (var check in result)
                    {
                        if (check.Status == PluginStatus.Failed)
                            diagnostics.Report("plugin-startup/initialize", "Plugin failed to initialize; no automatic reload",
                                detail: check.Message, pid: client.Pid, startTicks: client.StartTicks, plugin: check.Name);
                        else if (check.Status == PluginStatus.Confirmed)
                            diagnostics.Recovered("plugin-startup/initialize", "Initialization acknowledged", client.Pid, client.StartTicks, check.Name);
                    }
                }
                return result;
            }
            catch (FileNotFoundException) { return null; } // Pending startup is expected.
            catch (DirectoryNotFoundException) { return null; }
            catch (IOException ex) { diagnostics.Report("plugin-report/read", "Startup remains unconfirmed; retry on next poll", ex, pid: client.Pid, startTicks: client.StartTicks); return null; }
            catch (UnauthorizedAccessException ex) { diagnostics.Report("plugin-report/read", "Startup remains unconfirmed; retry on next poll", ex, pid: client.Pid, startTicks: client.StartTicks); return null; }
        }
        internal static List<PluginCheck> Parse(string json, PluginHostClient.ClientState client, Func<string,string> nameOf, FailureDiagnostics diagnostics = null)
        {
            Report report;
            try { report = new JavaScriptSerializer { MaxJsonLength = 512 * 1024, RecursionLimit = 8 }.Deserialize<Report>(json); }
            catch (ArgumentException ex) { InvalidReport(client, diagnostics, ex); return null; }
            catch (InvalidOperationException ex) { InvalidReport(client, diagnostics, ex); return null; }
            if (report == null || report.Format != 1 || report.Pid != client.Pid || report.StartTicks != client.StartTicks
                || string.IsNullOrEmpty(client.RequestId) || report.RequestId != client.RequestId || report.Plugins == null) return null;
            var results = new List<PluginCheck>();
            foreach (string path in client.Plugins ?? new string[0])
            {
                var entry = report.Plugins.FirstOrDefault(item => item != null && string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase));
                var check = new PluginCheck { Path = path, Name = nameOf(path) ?? Path.GetFileNameWithoutExtension(path), Status = PluginStatus.Pending,
                    Message = AOLauncher.Localization.Texts.Get("Text_Waiting_for_runtime_acknowledgement_BE6D5DC88B59") };
                if (entry != null)
                {
                    if (entry.State == "Started") { check.Status = PluginStatus.Confirmed; check.Message = AOLauncher.Localization.Texts.Get("Text_Initialization_returned_successfully_gameplay_unverified_7AB09B991E89"); }
                    else if (entry.State == "Failed") { check.Status = PluginStatus.Failed; check.Message = entry.Error; }
                    else if (entry.State == "Stopped") { check.Status = PluginStatus.NotLoaded; check.Message = AOLauncher.Localization.Texts.Get("Text_Stopped_3C340B8E0600"); }
                }
                results.Add(check);
            }
            foreach (var clash in report.Conflicts ?? new List<Conflict>())
            {
                if (clash == null) continue;
                string previous = nameOf(clash.Previous) ?? SafeName(clash.Previous), current = nameOf(clash.Current) ?? SafeName(clash.Current);
                results.Add(new PluginCheck { Name = clash.Resource, Status = PluginStatus.Conflict,
                    Message = string.Format(AOLauncher.Localization.Texts.Get("Text_0_and_1_both_claim_this_6F4BB5E75AE0"), previous, current) });
            }
            return results;
        }
        static void InvalidReport(PluginHostClient.ClientState client, FailureDiagnostics diagnostics, Exception error)
        {
            // Parser messages can quote the input document. Record its type/HResult,
            // never the raw report or a plugin's arbitrary JSON payload.
            (diagnostics ?? FailureDiagnostics.Plugins).Report("plugin-report/read", "Invalid startup report; retry on next poll",
                detail: error.GetType().FullName + " HResult=0x" + error.HResult.ToString("X8"), pid: client?.Pid ?? 0, startTicks: client?.StartTicks ?? 0);
        }
        static string SafeName(string path) { try { return Path.GetFileNameWithoutExtension(path ?? "Unknown"); } catch (ArgumentException) { return "Unknown"; } }
    }
}
