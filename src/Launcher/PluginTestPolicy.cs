using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using static AOLauncher.SidebarText;

namespace AOLauncher
{
    // Admission for the startup test only; this does not change normal plugin sets/loading.
    // Unknown files are not treated as passive merely because their catalog has no warning.
    internal static class PluginTestPolicy
    {
        internal sealed class Entry
        {
            public string Id { get; set; }
            public string Sha256 { get; set; }
            public string Class { get; set; }
            public string Profession { get; set; }
        }
        static readonly Lazy<List<Entry>> entries = new Lazy<List<Entry>>(() => {
            using (var stream = typeof(PluginTestPolicy).Assembly.GetManifestResourceStream("AOLauncher.PluginTestPolicy.json"))
            {
                if (stream == null) return new List<Entry>();
                using (var reader = new StreamReader(stream)) return new JavaScriptSerializer().Deserialize<List<Entry>>(reader.ReadToEnd());
            }
        });
        internal static IEnumerable<Entry> Entries => entries.Value;
        internal static string FileHash(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }
        internal static string Rejection(PluginInfo plugin, bool includeScenarios, string profession, out string hash)
        {
            hash = null;
            var entry = entries.Value.FirstOrDefault(item => string.Equals(item.Id, plugin.Id, StringComparison.OrdinalIgnoreCase));
            if (entry?.Class == "Blocked") return AOLauncher.Localization.Texts.Get("Text_Excluded_requires_a_separate_manual_review_02C47F30312F");
            try { hash = FileHash(plugin.Path); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            { return AOLauncher.Localization.Texts.Get("Text_Cannot_verify_the_plugin_file_A979B9724486") + ex.Message; }
            // A renamed copy of a blocked binary remains excluded.
            string actualHash = hash;
            if (entries.Value.Any(item => item.Class == "Blocked" && item.Sha256 == actualHash))
                return AOLauncher.Localization.Texts.Get("Text_Excluded_requires_a_separate_manual_review_02C47F30312F");
            if (entry == null || !new[] { "Candidate", "Scenario", "Instrument" }.Contains(entry.Class)
                || !string.Equals(entry.Sha256, hash, StringComparison.OrdinalIgnoreCase))
                return AOLauncher.Localization.Texts.Get("Text_Unknown_or_updated_file_needs_a_42C37F1B3969");
            if (entry.Class == "Instrument") return AOLauncher.Localization.Texts.Get("Text_Used_as_test_instrumentation_not_a_9970DBDB0E04");
            if (entry.Class == "Scenario" && !includeScenarios)
                return AOLauncher.Localization.Texts.Get("Text_Requires_the_specialised_plugin_option_and_A1AB95C0AE3B");
            if (!string.IsNullOrEmpty(entry.Profession)) {
                string key;
                if (profession == null || !PluginHealth.ProfessionKeys.TryGetValue(profession, out key) || key != entry.Profession)
                    return AOLauncher.Localization.Texts.Get("Text_Requires_profession_3A155D305B95") + entry.Profession;
            }
            return null;
        }
        internal static bool InstrumentMatches(PluginInfo plugin)
        {
            var entry = entries.Value.FirstOrDefault(item => item.Id == plugin?.Id && item.Class == "Instrument");
            try { return entry != null && FileHash(plugin.Path) == entry.Sha256; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
        internal static bool ResultMatches(PluginTestRecord record, string hash)
            => record != null && record.TestKind == "Startup" && hash != null &&
                string.Equals(record.PluginSha256, hash, StringComparison.OrdinalIgnoreCase);
    }
}
