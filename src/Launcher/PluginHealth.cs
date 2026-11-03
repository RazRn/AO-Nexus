using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using static AOLauncher.SidebarText;

namespace AOLauncher
{
    // What Nexus Shortcuts (src/Shortcuts) sees inside a client: which plugin assemblies AOSharp
    // loaded, what each one hooked, and the errors AOSharp itself swallows. Plugins are judged
    // here, in Nexus, against the list that was sent to the client.
    internal sealed class HealthReport
    {
        public string who { get; set; }
        public List<Entry> plugins { get; set; } = new List<Entry>();
        public List<string> load { get; set; } = new List<string>();

        internal sealed class Entry
        {
            public string a { get; set; }        // assembly name
            public string f { get; set; }        // file it was loaded from
            public bool entry { get; set; }      // has an AOSharp plugin class
            public List<string> c { get; set; } = new List<string>(); // chat commands it registered
            public int h { get; set; }           // game events it subscribed to
            public string s { get; set; }        // the error that stopped its constructor or start
            public int e { get; set; }           // first-chance exceptions, possibly caught (up to 25 a second)
            public string m { get; set; }        // the last of those errors
        }

        internal static HealthReport Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > 512 * 1024) return null;
            try
            {
                var report = new JavaScriptSerializer { MaxJsonLength = 512 * 1024 }.Deserialize<HealthReport>(json);
                if (report == null) return null;
                report.plugins = report.plugins ?? new List<Entry>();
                report.load = report.load ?? new List<string>();
                foreach (var entry in report.plugins) entry.c = entry.c ?? new List<string>();
                return report;
            }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
        }
    }

    internal enum PluginStatus { Running, Started, Errors, Failed, NotLoaded, Pending, Confirmed, Conflict }

    internal sealed class PluginCheck
    {
        internal string Path, Name, Message;
        internal PluginStatus Status;
        internal int Errors;
        internal List<string> Commands = new List<string>();

        internal bool Problem => Status == PluginStatus.Failed || Status == PluginStatus.NotLoaded || Status == PluginStatus.Errors || Status == PluginStatus.Conflict;

        internal string StatusText()
        {
            switch (Status)
            {
                case PluginStatus.Pending: return AOLauncher.Localization.Texts.Get("Text_Starting_unconfirmed_74F9358DDC75");
                case PluginStatus.Confirmed: return AOLauncher.Localization.Texts.Get("Text_Startup_confirmed_F4EEBA62C5B1");
                case PluginStatus.Conflict: return AOLauncher.Localization.Texts.Get("Text_Plugin_conflict_0CFBB7283E77");
                case PluginStatus.Running: return AOLauncher.Localization.Texts.Get("Text_Startup_observed_484F543FCB0D");
                case PluginStatus.Started: return AOLauncher.Localization.Texts.Get("Text_Loaded_activity_unverified_A4016A2BB17A");
                case PluginStatus.Errors: return string.Format(AOLauncher.Localization.Texts.Get("Text_Exceptions_observed_0_may_be_handled_22AE1DA53266"), Errors >= 1000 ? "1000+" : Errors.ToString());
                case PluginStatus.Failed: return AOLauncher.Localization.Texts.Get("Text_Failed_to_start_1B501F022262");
                default: return AOLauncher.Localization.Texts.Get("Text_Did_not_load_B88AF34B2E9A");
            }
        }

        internal string Line() => Name + ": " + StatusText() + (string.IsNullOrEmpty(Message) ? "" : " · " + Message);
    }

    internal static class PluginHealth
    {
        // Plugins hook their commands and events in Init, which AOSharp runs right after loading;
        // a few wait for the first game frames, so a plugin is only called "Started" after this.
        internal const int SettleSeconds = 6;

        static string Norm(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            try { return System.IO.Path.GetFullPath(path).TrimEnd('\\', '/'); } catch (Exception) { return path; }
        }

        // One result per plugin that was sent to the client. Nexus Shortcuts reports on itself
        // by answering, so it is left out.
        internal static List<PluginCheck> Evaluate(IEnumerable<string> requested, HealthReport report, Func<string, string> nameOf = null)
        {
            var results = new List<PluginCheck>();
            if (report == null) return results;
            foreach (string path in requested ?? new string[0])
            {
                string file = System.IO.Path.GetFileNameWithoutExtension(path);
                if (string.Equals(file, "NexusShortcuts", StringComparison.OrdinalIgnoreCase)) continue;
                var check = new PluginCheck { Path = path, Name = nameOf?.Invoke(path) ?? file };
                var entry = report.plugins.FirstOrDefault(item => string.Equals(Norm(item.f), Norm(path), StringComparison.OrdinalIgnoreCase))
                    ?? report.plugins.FirstOrDefault(item => string.Equals(item.a, file, StringComparison.OrdinalIgnoreCase));
                // A failed load is reported with the file AOSharp looked for, which sits in the plugin's folder.
                string folder = System.IO.Path.GetDirectoryName(path) ?? "";
                string loadFault = report.load.FirstOrDefault(fault => folder.Length > 0 && fault.IndexOf(folder, StringComparison.OrdinalIgnoreCase) >= 0
                    || fault.IndexOf(System.IO.Path.GetFileName(path), StringComparison.OrdinalIgnoreCase) >= 0);
                if (entry == null)
                {
                    check.Status = PluginStatus.NotLoaded;
                    check.Message = loadFault ?? AOLauncher.Localization.Texts.Get("Text_AO_did_not_load_the_file_2377B019B73A");
                }
                else
                {
                    check.Commands = entry.c.ToList();
                    check.Errors = entry.e;
                    if (!string.IsNullOrEmpty(entry.s)) { check.Status = PluginStatus.Failed; check.Message = entry.s; }
                    else if (entry.c.Count == 0 && entry.h == 0 && loadFault != null) { check.Status = PluginStatus.Failed; check.Message = AOLauncher.Localization.Texts.Get("Text_did_not_start_A771575D7F20") + loadFault; }
                    else if (entry.e > 0) { check.Status = PluginStatus.Errors; check.Message = entry.m; }
                    else if (entry.c.Count > 0 || entry.h > 0) check.Status = PluginStatus.Running;
                    else { check.Status = PluginStatus.Started; check.Message = AOLauncher.Localization.Texts.Get("Text_no_commands_or_game_events_seen_6F5A968C2758"); }
                }
                results.Add(check);
            }
            return results;
        }

        // "2 problems" for the Running now list; null when everything is fine.
        internal static string Summary(List<PluginCheck> checks)
        {
            int failed = checks.Count(check => check.Status == PluginStatus.Failed || check.Status == PluginStatus.NotLoaded);
            int errors = checks.Count(check => check.Status == PluginStatus.Errors);
            int conflicts = checks.Count(check => check.Status == PluginStatus.Conflict);
            if (failed == 0 && errors == 0 && conflicts == 0) return null;
            var parts = new List<string>();
            if (failed > 0) parts.Add(string.Format(AOLauncher.Localization.Texts.Get("Text_0_did_not_start_E6232A7C8B28"), failed));
            if (errors > 0) parts.Add(string.Format(AOLauncher.Localization.Texts.Get("Text_0_with_observed_exceptions_9278DD6DBEF2"), errors));
            if (conflicts > 0) parts.Add(string.Format(AOLauncher.Localization.Texts.Get("Text_0_conflicts_06E537F5EC5D"), conflicts));
            return string.Join(", ", parts);
        }

        // Profession names as AOSharp spells them -> the starter set's handler key.
        internal static readonly Dictionary<string, string> ProfessionKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Adventurer", "Adv" }, { "Agent", "Agent" }, { "Bureaucrat", "Crat" }, { "Doctor", "Doc" }, { "Enforcer", "Enf" }, { "Engineer", "Engi" },
            { "Fixer", "Fixer" }, { "Keeper", "Keeper" }, { "MartialArtist", "MA" }, { "Metaphysicist", "MP" }, { "NanoTechnician", "NT" },
            { "Shade", "Shade" }, { "Soldier", "Soldier" }, { "Trader", "Trader" }
        };
    }
}
