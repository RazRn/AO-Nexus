using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace AOLauncher
{
    // What Nexus looked like when a diagnostic ZIP was saved: which characters are ticked,
    // the team, plugin sets, servers, monitoring and plugin switches, and what is running.
    // ClientReport passes both texts through DiagnosticRedactor, which replaces account,
    // character and team names, character IDs, private servers and personal folders.
    internal static class AppStateReport
    {
        // Never copied into the snapshot, even though settings hold none of these today.
        static readonly Regex SensitiveKey = new Regex("^(?!Remember)\\w*(password|credential|secret|token|apikey|cookie)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        internal sealed class Extras
        {
            internal string Section;
            internal bool LoginRunning;
            internal string Language;
            internal float DisplayScale = 1f;
            internal List<PluginAutoInjector.Row> PluginRows = new List<PluginAutoInjector.Row>();
            internal List<PresetCamping.Client> Sessions;
        }

        internal static string Text(LauncherSettings settings, Extras extras)
        {
            extras = extras ?? new Extras();
            var text = new StringBuilder();
            Func<bool, string> box = value => value ? "[x]" : "[ ]";
            text.AppendLine("AO Nexus app state · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
            text.AppendLine("Version " + AppUpdates.DisplayVersion + " · " + Environment.OSVersion.VersionString + (Environment.Is64BitOperatingSystem ? " (64-bit)" : "")
                + " · language " + (extras.Language ?? CultureInfo.CurrentUICulture.Name) + " · display scale " + Math.Round(extras.DisplayScale * 100) + "%");
            text.AppendLine("Page: " + (extras.Section ?? "?") + " · login running: " + (extras.LoginRunning ? "yes" : "no"));
            text.AppendLine();

            var preset = settings.Presets?.FirstOrDefault(item => item.Id == settings.SelectedPresetId);
            text.AppendLine("TEAM");
            text.AppendLine("  Selected: " + (preset == null ? "(none)" : preset.Name + (PresetService.Matches(settings, preset) ? "" : " (ticks changed since it was chosen)")
                + " · plugins " + (preset.LoadPlugins ? "on" : "off for this team")));
            text.AppendLine("  Default at startup: " + (settings.Presets?.FirstOrDefault(item => item.Id == settings.DefaultPresetId)?.Name ?? "(none)"));
            foreach (var team in settings.Presets ?? new List<LaunchPreset>())
                text.AppendLine("  - " + team.Name + ": " + team.Characters.Count + " characters · plugins " + (team.LoadPlugins ? "on" : "off")
                    + (team.Characters.Any(item => item.PluginSetId != null) ? " · " + team.Characters.Count(item => item.PluginSetId != null) + " set overrides" : ""));
            text.AppendLine();

            text.AppendLine("ACCOUNTS AND CHARACTERS ([x] = ticked for login)");
            int ticked = 0;
            foreach (var account in settings.Accounts ?? new List<AccountConfig>())
            {
                var profile = settings.LoginServers?.FirstOrDefault(item => item.Id == account.ServerProfileId);
                text.AppendLine("  " + (account.Label ?? account.Username) + " · " + (profile?.Name ?? account.Server + ":" + account.Port)
                    + " · slots " + (account.CharacterSlots?.ToString(CultureInfo.InvariantCulture) ?? "default")
                    + " · remember password " + (account.RememberPassword ? "yes" : "no") + (account.Collapsed ? " · collapsed" : ""));
                text.AppendLine("    game folder: " + (account.GamePath ?? "(global)") + " · prefs: " + (account.PrefsPath ?? "(none)"));
                foreach (var character in account.Characters ?? new List<CharacterConfig>())
                {
                    if (character.Selected) ticked++;
                    string set = string.IsNullOrEmpty(character.PluginSetId) ? "set: automatic (by profession)" : character.PluginSetId == PluginSet.None ? "set: no plugins"
                        : "set: " + (PluginSets.Find(settings, character.PluginSetId)?.Name ?? character.PluginSetId + " (missing)");
                    text.AppendLine("    " + box(character.Selected) + " " + character.Name + " (ID " + character.CharacterId + ") · " + set + (character.NamePending ? " · name pending" : ""));
                }
            }
            text.AppendLine("  " + ticked + " ticked in total.");
            text.AppendLine();

            text.AppendLine("SWITCHES");
            text.AppendLine("  " + box(settings.AutoInjectPlugins) + " Load plugins automatically");
            text.AppendLine("  " + box(settings.IncludeHealthMonitor) + " Include the Health Monitor plugin");
            text.AppendLine("  " + box(settings.CheckServerRulesOnStartup) + " Check server rules at startup · revision " + settings.ServerRulesRevision
                + " (built-in " + ServerRules.Current.Revision + ")");
            text.AppendLine("  AO# folder: " + (string.IsNullOrWhiteSpace(settings.AOSharpPackPath) ? "built-in pack" : settings.AOSharpPackPath));
            foreach (var other in ForeignAOSharp.RunningAOSharp()) text.AppendLine("  Also running: " + other);
            text.AppendLine();

            text.AppendLine("PLUGIN SETS");
            foreach (var set in settings.PluginSets ?? new List<PluginSet>())
            {
                int users = (settings.Accounts ?? new List<AccountConfig>()).Sum(a => a.Characters.Count(c => c.PluginSetId == set.Id));
                text.AppendLine("  - " + set.Name + ": " + (set.Plugins?.Count ?? 0) + " plugins · " + users + " characters chosen directly");
            }
            if ((settings.CustomPlugins?.Count ?? 0) > 0) text.AppendLine("  Own DLLs: " + settings.CustomPlugins.Count);
            text.AppendLine();

            text.AppendLine("PLUGIN TESTS (latest per plugin)");
            var tests = settings.PluginTests ?? new List<PluginTestRecord>();
            if (tests.Count == 0) text.AppendLine("  (none run)");
            foreach (var group in tests.GroupBy(test => test.Status).OrderBy(group => group.Key))
                text.AppendLine("  " + group.Key + " (" + group.Count() + "): " + string.Join(", ", group.Select(test => (test.PluginId ?? "?").Replace("pack:", "")).OrderBy(id => id)));
            foreach (var test in tests.Where(test => test.Status != "Running" && test.Status != "Started" && !string.IsNullOrEmpty(test.Message)))
                text.AppendLine("  - " + (test.PluginId ?? "?").Replace("pack:", "") + ": " + test.Message);
            text.AppendLine();

            text.AppendLine("LOGIN SERVERS");
            foreach (var server in settings.LoginServers ?? new List<LoginServerProfile>())
                text.AppendLine("  - " + server.Name + " · " + server.Server + ":" + server.Port + " · accounts " + Limit(server.MaxAccounts)
                    + " · chars/account " + Limit(server.MaxCharactersPerAccount) + (server.Customized ? " · edited by you" : ""));
            text.AppendLine();

            text.AppendLine("GAME CLIENTS NEXUS KNOWS ABOUT");
            if (extras.Sessions == null) text.AppendLine("  (session list could not be read)");
            else if (extras.Sessions.Count == 0) text.AppendLine("  (none)");
            else foreach (var client in extras.Sessions)
                text.AppendLine("  - " + (client.CharacterName ?? "?") + " (ID " + client.CharacterId + ") · PID " + client.ProcessId + " · " + (client.Pending ? "still logging in" : "in game")
                    + (client.CreateCharacter ? " · character creation" : ""));
            text.AppendLine();

            text.AppendLine("PLUGINS RUNNING NOW");
            if (extras.PluginRows.Count == 0) text.AppendLine("  (no characters in game)");
            foreach (var row in extras.PluginRows)
                text.AppendLine("  - " + row.CharacterName + " · PID " + row.Pid + " · " + (row.SetName ?? "(no set)") + " · " + (row.StateText ?? row.State)
                    + (string.IsNullOrEmpty(row.Detail) ? "" : " · " + row.Detail.Replace("\r\n", " | ")));
            return text.ToString();
        }

        static string Limit(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "no limit";

        // All settings as JSON, minus anything credential-like.
        // Redacted value by value before it is written, so the result stays valid JSON: a character
        // ID becomes the string "Char-…" (not a bare word), and paths are matched unescaped.
        internal static string SettingsJson(LauncherSettings settings, DiagnosticRedactor redactor = null)
        {
            var json = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024, RecursionLimit = 64 };
            object tree = json.DeserializeObject(json.Serialize(settings));
            Strip(tree, 0);
            tree = Redact(tree, redactor ?? DiagnosticRedactor.Current, 0);
            return Indent(json.Serialize(tree));
        }

        static object Redact(object node, DiagnosticRedactor redactor, int depth)
        {
            if (depth > 48 || node == null) return node;
            if (node is string text) return redactor.Redact(text);
            if (node is int || node is long || node is decimal)
            {
                string value = Convert.ToString(node, CultureInfo.InvariantCulture);
                string redacted = redactor.Redact(value);
                return redacted == value ? node : (object)redacted;
            }
            if (node is IDictionary<string, object> map)
            {
                foreach (string key in map.Keys.ToList()) map[key] = Redact(map[key], redactor, depth + 1);
                return map;
            }
            if (node is object[] array)
            {
                for (int i = 0; i < array.Length; i++) array[i] = Redact(array[i], redactor, depth + 1);
                return array;
            }
            if (node is ArrayList list)
            {
                for (int i = 0; i < list.Count; i++) list[i] = Redact(list[i], redactor, depth + 1);
                return list;
            }
            return node;
        }

        static void Strip(object node, int depth)
        {
            if (depth > 48) return;
            if (node is IDictionary<string, object> map)
            {
                foreach (string key in map.Keys.ToList())
                    if (SensitiveKey.IsMatch(key)) map[key] = "(removed)";
                    else Strip(map[key], depth + 1);
            }
            else if (node is IEnumerable list && !(node is string))
                foreach (object item in list) Strip(item, depth + 1);
        }

        static string Indent(string json)
        {
            var output = new StringBuilder(json.Length + json.Length / 4);
            int level = 0; bool quoted = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (quoted) { output.Append(c); if (c == '\\' && i + 1 < json.Length) output.Append(json[++i]); else if (c == '"') quoted = false; continue; }
                switch (c)
                {
                    case '"': quoted = true; output.Append(c); break;
                    case '{': case '[': output.Append(c).Append("\r\n").Append(' ', ++level * 2); break;
                    case '}': case ']': output.Append("\r\n").Append(' ', --level * 2).Append(c); break;
                    case ',': output.Append(c).Append("\r\n").Append(' ', level * 2); break;
                    case ':': output.Append(": "); break;
                    default: output.Append(c); break;
                }
            }
            return output.ToString();
        }
    }
}
