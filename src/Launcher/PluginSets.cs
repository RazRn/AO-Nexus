using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using static AOLauncher.SidebarText;

namespace AOLauncher
{
    // A named list of plugins, e.g. "Doc" = Doc handler + Manager · Sync.
    internal sealed class PluginSet
    {
        internal const string None = "none";
        internal const string All = "nexus:all";
        internal const string HealthMonitorId = "nexus:health-monitor";
        internal const string ShortcutsId = "pack:Nexus.Shortcuts";
        public string Id { get; set; } = Guid.NewGuid().ToString("D");
        public string Name { get; set; }
        public List<string> Plugins { get; set; } = new List<string>();
        public override string ToString() => Name;
    }

    // A plugin DLL the user added from outside the pack (or imported from AOSharp's config).
    internal sealed class CustomPlugin
    {
        public string Id { get; set; } = "custom:" + Guid.NewGuid().ToString("N");
        public string Name { get; set; }
        public string Path { get; set; }
    }

    internal static class PluginSets
    {
        static readonly Dictionary<string, string> Professions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Adv", "Adventurer" }, { "Agent", "Agent" }, { "Crat", "Bureaucrat" }, { "Doc", "Doctor" }, { "Enf", "Enforcer" },
            { "Engi", "Engineer" }, { "Fixer", "Fixer" }, { "Keeper", "Keeper" }, { "MA", "Martial Artist" }, { "MP", "Meta-Physicist" },
            { "NT", "Nano-Technician" }, { "Shade", "Shade" }, { "Soldier", "Soldier" }, { "Trader", "Trader" }
        };

        // One starter set per profession: its handler + the sync manager, from the Z suite when the
        // pack has it (the main suite), otherwise from AOSP. Team activities come from play styles.
        internal static void EnsureStarterSets(LauncherSettings settings, AOSharpPack pack)
        {
            if (pack == null) return;
            if (settings.PluginSetsInitialized) return;
            bool z = pack.Plugins.Any(plugin => plugin.Id.StartsWith("pack:Z.Handler.", StringComparison.OrdinalIgnoreCase));
            string handlerPrefix = z ? "pack:Z.Handler." : "pack:ProfessionHandler.", sync = z ? "pack:Z.SyncManager" : "pack:Manager.Sync";
            bool hasSync = pack.Plugins.Any(plugin => plugin.Id == sync);
            foreach (var plugin in pack.Plugins.Where(item => item.Id.StartsWith(handlerPrefix, StringComparison.OrdinalIgnoreCase)).OrderBy(item => item.Name))
            {
                string key = plugin.Id.Substring(handlerPrefix.Length);
                string name = Professions.TryGetValue(key, out var full) ? full : key;
                if (settings.PluginSets.Any(existing => string.Equals(existing.Name, name, StringComparison.OrdinalIgnoreCase))) continue;
                var set = new PluginSet { Name = name };
                set.Plugins.Add(plugin.Id);
                if (hasSync) set.Plugins.Add(sync);
                settings.PluginSets.Add(set);
            }
            settings.PluginSetsInitialized = true;
            settings.PluginSuiteVersion = 1;
        }

        // AOSP plugin -> its Z counterpart (same job). Used to move sets between the suites.
        internal static readonly Dictionary<string, string> AospToZ = BuildSuiteMap();
        static Dictionary<string, string> BuildSuiteMap()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string profession in Professions.Keys) map["pack:ProfessionHandler." + profession] = "pack:Z.Handler." + profession;
            foreach (var pair in new[] { "Sync:SyncManager", "Assist:AssistManager", "Bag:BagManager", "GMI:GMIManager", "Help:HelpManager", "Loot:LootManager",
                "Mail:MailManager", "Research:ResearchManager", "Follow:MovementManager" })
                map["pack:Manager." + pair.Split(':')[0]] = "pack:Z." + pair.Split(':')[1];
            foreach (var pair in new[] { "City:CityBuddy", "Inf:InfBuddy", "KiteHill:KHBuddy", "Mitaar:MitaarBuddy", "Roamba:RoamBuddy", "Vortexx:VortBuddy" })
                map["pack:Automaton." + pair.Split(':')[0]] = "pack:Z." + pair.Split(':')[1];
            foreach (var pair in new[] { "Coffee:CoffeeUtility", "Cookies:CookiesUtility", "Pearls:PearlsUtility", "Plasma:BloodPlasmaUtility" })
                map["pack:Utility." + pair.Split(':')[0]] = "pack:Z." + pair.Split(':')[1];
            return map;
        }

        // Converts a set to the other suite where a counterpart exists; returns the plugins that have none.
        internal static List<string> Convert(PluginSet set, bool toZ)
        {
            var map = toZ ? AospToZ : AospToZ.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);
            var result = new List<string>(); var unmatched = new List<string>();
            foreach (string id in set.Plugins)
            {
                string suite = AOSharpPack.SuiteOf(id);
                bool fromSuite = toZ ? suite == "AOSP" : suite == "Z";
                if (fromSuite && map.TryGetValue(id, out var counterpart)) result.Add(counterpart);
                else if (fromSuite) unmatched.Add(id);
                else result.Add(id);
            }
            set.Plugins = result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return unmatched;
        }

        // Once, when the Z suite arrives: starter sets nobody changed (profession handler + sync
        // manager from AOSP) move to Z. Sets the user built themselves are left alone; the Plugins
        // page offers to convert those.
        internal static int MigrateToZ(LauncherSettings settings, AOSharpPack pack)
        {
            if (settings.PluginSuiteVersion >= 1 || !pack.Plugins.Any(plugin => plugin.Id.StartsWith("pack:Z.Handler.", StringComparison.OrdinalIgnoreCase))) return 0;
            int moved = 0;
            foreach (var set in settings.PluginSets)
            {
                var ids = set.Plugins.Where(id => id != PluginSet.HealthMonitorId).ToList();
                bool starter = ids.Count == 2 && ids.Any(id => id.StartsWith("pack:ProfessionHandler.", StringComparison.OrdinalIgnoreCase))
                    && ids.Contains("pack:Manager.Sync", StringComparer.OrdinalIgnoreCase);
                bool handlerOnly = ids.Count == 1 && ids[0].StartsWith("pack:ProfessionHandler.", StringComparison.OrdinalIgnoreCase);
                if ((starter || handlerOnly) && Convert(set, true).Count == 0) moved++;
            }
            settings.PluginSuiteVersion = 1;
            return moved;
        }

        // "Z", "AOSP" or null: the suite a set's profession handler / managers come from.
        internal static string SuiteOf(PluginSet set)
        {
            if (set == null) return null;
            var suites = set.Plugins.Select(AOSharpPack.SuiteOf).Where(suite => suite == "Z" || suite == "AOSP").ToList();
            if (suites.Count == 0) return null;
            string handler = set.Plugins.FirstOrDefault(id => id.StartsWith("pack:Z.Handler.") || id.StartsWith("pack:ProfessionHandler."));
            return handler != null ? AOSharpPack.SuiteOf(handler) : suites.GroupBy(suite => suite).OrderByDescending(group => group.Count()).First().Key;
        }

        internal static bool MixesSuites(PluginSet set) =>
            set != null && set.Plugins.Select(AOSharpPack.SuiteOf).Where(suite => suite == "Z" || suite == "AOSP").Distinct().Count() > 1;

        internal static PluginSet Find(LauncherSettings settings, string id) =>
            id == null ? null : settings.PluginSets.FirstOrDefault(set => string.Equals(set.Id, id, StringComparison.OrdinalIgnoreCase));

        // A character's set: empty = automatic (Nexus picks its profession's set the first time it is
        // in the game), PluginSet.None = no plugins, otherwise a set id. A team override wins.
        internal static bool IsAutomatic(AccountConfig account, CharacterConfig character, LaunchPreset preset)
        {
            if (preset != null && !preset.LoadPlugins) return false;
            var reference = preset?.Characters.FirstOrDefault(item => item.CharacterId == character.CharacterId && string.Equals(item.AccountId, account.Id, StringComparison.OrdinalIgnoreCase));
            return string.IsNullOrEmpty(reference?.PluginSetId) && string.IsNullOrEmpty(character.PluginSetId);
        }

        // The starter set for a profession ("Engineer", "MartialArtist" as AOSharp names them): the
        // existing set with the profession's name, or a new one with its handler and the sync manager.
        internal static PluginSet ForProfession(LauncherSettings settings, AOSharpPack pack, string profession, out bool created)
        {
            created = false;
            if (profession == null || !PluginHealth.ProfessionKeys.TryGetValue(profession, out string key) || pack == null) return null;
            string name = Professions[key];
            var existing = settings.PluginSets.FirstOrDefault(set => string.Equals(set.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing != null) return existing;
            bool z = pack.Plugins.Any(plugin => plugin.Id == "pack:Z.Handler." + key);
            string handler = z ? "pack:Z.Handler." + key : "pack:ProfessionHandler." + key, sync = z ? "pack:Z.SyncManager" : "pack:Manager.Sync";
            if (!pack.Plugins.Any(plugin => plugin.Id == handler)) return null;
            var made = new PluginSet { Name = name };
            made.Plugins.Add(handler);
            if (pack.Plugins.Any(plugin => plugin.Id == sync)) made.Plugins.Add(sync);
            settings.PluginSets.Add(made);
            created = true;
            return made;
        }

        // "Martial Artist" for AOSharp's "MartialArtist"; the name as given when unknown.
        internal static string ProfessionName(string profession) =>
            profession != null && PluginHealth.ProfessionKeys.TryGetValue(profession, out string key) ? Professions[key] : profession;

        // Preset override wins; then the character's default; otherwise nothing is loaded.
        internal static string Resolve(LauncherSettings settings, AccountConfig account, CharacterConfig character, LaunchPreset preset)
        {
            var reference = preset?.Characters.FirstOrDefault(item => item.CharacterId == character.CharacterId && string.Equals(item.AccountId, account.Id, StringComparison.OrdinalIgnoreCase));
            string id = !string.IsNullOrEmpty(reference?.PluginSetId) ? reference.PluginSetId : character.PluginSetId;
            if (string.IsNullOrEmpty(id) || id == PluginSet.None) return null;
            return Find(settings, id) == null ? null : id;
        }

        // ALL is an editor for shared selections, never a character's assigned set.
        internal static List<string> EffectiveIds(LauncherSettings settings, PluginSet set)
        {
            if (set == null || set.Id == PluginSet.None) return new List<string>();
            return (settings.SharedPlugins ?? new List<string>()).Concat(set.Plugins ?? new List<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        internal static void SetSharedPlugin(LauncherSettings settings, string id, bool selected)
        {
            settings.SharedPlugins = (settings.SharedPlugins ?? new List<string>())
                .Where(item => !string.Equals(item, id, StringComparison.OrdinalIgnoreCase)).ToList();
            if (selected) settings.SharedPlugins.Add(id);
            // Promote existing copies so removing it from ALL later removes it everywhere.
            // Also cover sets created or imported while this shared selection was active.
            foreach (var set in settings.PluginSets)
                set.Plugins.RemoveAll(item => string.Equals(item, id, StringComparison.OrdinalIgnoreCase));
        }

        internal static string[] Paths(LauncherSettings settings, AOSharpPack pack, PluginSet set, bool includeHealthMonitor, out List<string> missing) =>
            Paths(settings, pack, set, includeHealthMonitor, null, false, out missing, out _);

        // The DLLs to load for a set: its plugins, the play style's plugins for the set's suite,
        // and Nexus's own helpers. Plugins from the other suite are left out (they would fight).
        internal static string[] Paths(LauncherSettings settings, AOSharpPack pack, PluginSet set, bool includeHealthMonitor, string playStyle, bool includeShortcuts,
            out List<string> missing, out List<string> skipped)
        {
            missing = new List<string>(); skipped = new List<string>();
            if (set == null || set.Id == PluginSet.None) return new string[0];
            var catalog = AOSharpPack.AllPlugins(settings, pack).GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            string suite = SuiteOf(set) ?? (catalog.Keys.Any(id => id.StartsWith("pack:Z.")) ? "Z" : "AOSP");
            if (settings.SharedPlugins != null && settings.SharedPlugins.Count > 0)
            {
                // Missing handlers from an old installation must not suppress usable shared plugins.
                Func<string, bool> available = id => catalog.TryGetValue(id, out var plugin) && File.Exists(plugin.Path);
                suite = SuiteOf(new PluginSet { Plugins = set.Plugins.Where(available).ToList() })
                    ?? SuiteOf(new PluginSet { Plugins = settings.SharedPlugins.Where(available).ToList() }) ?? suite;
            }
            var ids = EffectiveIds(settings, set);
            ids.AddRange(PlayStyles.Extras(playStyle, suite).Where(id => !ids.Contains(id, StringComparer.OrdinalIgnoreCase)));
            foreach (string id in ids.ToList())
            {
                string other = AOSharpPack.SuiteOf(id);
                if ((other == "Z" || other == "AOSP") && other != suite && catalog.TryGetValue(id, out var candidate) && File.Exists(candidate.Path)) { ids.Remove(id); skipped.Add(id); }
            }
            // Nexus Shortcuts goes first: it watches the others load and start (see PluginHealth).
            ids.RemoveAll(id => string.Equals(id, PluginSet.ShortcutsId, StringComparison.OrdinalIgnoreCase));
            if (includeShortcuts && catalog.ContainsKey(PluginSet.ShortcutsId)) ids.Insert(0, PluginSet.ShortcutsId);
            if (includeHealthMonitor && !ids.Contains(PluginSet.HealthMonitorId)) ids.Add(PluginSet.HealthMonitorId);
            var paths = new List<string>();
            foreach (string id in ids)
            {
                if (catalog.TryGetValue(id, out var plugin) && File.Exists(plugin.Path)) paths.Add(plugin.Path);
                else missing.Add(id);
            }
            return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        internal sealed class ImportResult
        {
            internal int Sets, Characters, CustomPlugins;
            internal readonly List<string> Unmatched = new List<string>();
        }

        // Turns AOSharp's config.json profiles (one per character name) into plugin sets
        // and character defaults. Identical plugin lists share one set.
        internal static ImportResult ImportAOSharpConfig(LauncherSettings settings, AOSharpPack pack, string path)
        {
            var result = new ImportResult();
            if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException(AOLauncher.Localization.Texts.Get("Text_The_AO_configuration_is_too_large_A796E7E1833C"));
            var json = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };
            var config = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
            if (config == null || !config.ContainsKey("Plugins") || !config.ContainsKey("Profiles")) throw new InvalidDataException(AOLauncher.Localization.Texts.Get("Text_This_is_not_an_AO_config_AB886D7453AD"));
            var known = AOSharpPack.AllPlugins(settings, pack);
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // AOSharp hash -> Nexus plugin id
            foreach (var entry in (Dictionary<string, object>)config["Plugins"])
            {
                var plugin = entry.Value as Dictionary<string, object>;
                if (plugin == null) continue;
                string bundled = plugin.ContainsKey("BundledId") ? plugin["BundledId"] as string : null;
                string name = plugin.ContainsKey("Name") ? plugin["Name"] as string : null;
                string dll = plugin.ContainsKey("Path") ? plugin["Path"] as string : null;
                if (bundled != null && known.Any(item => item.Id == "pack:" + bundled)) { map[entry.Key] = "pack:" + bundled; continue; }
                if (string.Equals(name, "Nexus Health Monitor", StringComparison.OrdinalIgnoreCase) || (dll ?? "").EndsWith("NexusHealthPlugin.dll", StringComparison.OrdinalIgnoreCase))
                { map[entry.Key] = PluginSet.HealthMonitorId; continue; }
                if (string.IsNullOrWhiteSpace(dll)) continue;
                var custom = settings.CustomPlugins.FirstOrDefault(item => string.Equals(item.Path, dll, StringComparison.OrdinalIgnoreCase));
                if (custom == null)
                {
                    custom = new CustomPlugin { Name = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(dll) : name.Trim(), Path = dll };
                    settings.CustomPlugins.Add(custom); result.CustomPlugins++;
                }
                map[entry.Key] = custom.Id;
            }
            foreach (var profileObject in (System.Collections.ArrayList)config["Profiles"])
            {
                var profile = profileObject as Dictionary<string, object>;
                string characterName = (profile?["Name"] as string ?? "").Trim();
                var enabled = (profile?["EnabledPlugins"] as System.Collections.ArrayList)?.Cast<object>().Select(item => item as string)
                    .Where(hash => hash != null && map.ContainsKey(hash)).Select(hash => map[hash])
                    .Where(id => id != PluginSet.HealthMonitorId).Distinct().OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
                if (string.IsNullOrEmpty(characterName) || enabled == null || enabled.Count == 0) continue;
                var targets = settings.Accounts.SelectMany(account => account.Characters).Where(character => string.Equals(character.Name, characterName, StringComparison.OrdinalIgnoreCase)).ToList();
                if (targets.Count == 0) { result.Unmatched.Add(characterName); continue; }
                var set = settings.PluginSets.FirstOrDefault(item => item.Plugins.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).SequenceEqual(enabled, StringComparer.OrdinalIgnoreCase));
                if (set == null)
                {
                    string handler = enabled.FirstOrDefault(id => id.StartsWith("pack:ProfessionHandler.", StringComparison.OrdinalIgnoreCase));
                    string baseName = handler != null && Professions.TryGetValue(handler.Substring("pack:ProfessionHandler.".Length), out var full) ? full : characterName;
                    string setName = baseName; int suffix = 2;
                    while (settings.PluginSets.Any(item => string.Equals(item.Name, setName, StringComparison.OrdinalIgnoreCase))) setName = baseName + " " + suffix++;
                    set = new PluginSet { Name = setName, Plugins = enabled };
                    settings.PluginSets.Add(set); result.Sets++;
                }
                foreach (var character in targets) { character.PluginSetId = set.Id; result.Characters++; }
            }
            settings.PluginSetsInitialized = true;
            return result;
        }

        // As the confirmation says: everyone who used the set loads no plugins afterwards. A team
        // override becomes "No plugins" rather than "Character default", which could load a set.
        internal static void Delete(LauncherSettings settings, PluginSet set)
        {
            settings.PluginSets.Remove(set);
            // Not empty: that would mean "automatic" and could load a set.
            foreach (var character in settings.Accounts.SelectMany(account => account.Characters))
                if (character.PluginSetId == set.Id) character.PluginSetId = PluginSet.None;
            foreach (var reference in settings.Presets.SelectMany(preset => preset.Characters))
                if (reference.PluginSetId == set.Id) reference.PluginSetId = PluginSet.None;
        }
    }
}
