using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Xml;

namespace AOLauncher
{
    [Flags]
    internal enum GuiLayoutComponents { Layout = 1, Hotbars = 2, Keybindings = 4, PetNames = 8 }
    internal sealed class GuiLayoutTemplate
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string SourceCharacter { get; set; }
        public string GameSignature { get; set; }
        public DateTime CreatedUtc { get; set; }
        public int FileCount { get; set; }
        public GuiLayoutComponents Components { get; set; } = GuiLayoutComponents.Layout;
        public List<GuiLayoutFile> Files { get; set; } = new List<GuiLayoutFile>();
        public override string ToString() => Name;
    }
    internal sealed class GuiLayoutFile
    {
        public string Path { get; set; }
        public string Hash { get; set; }
    }
    internal sealed class GuiLayoutBackup
    {
        public string Id { get; set; }
        public string TemplateId { get; set; }
        public string TargetPath { get; set; }
        public DateTime CreatedUtc { get; set; }
        public string State { get; set; }
        public GuiLayoutComponents Components { get; set; } = GuiLayoutComponents.Layout;
        public List<GuiLayoutChange> Changes { get; set; } = new List<GuiLayoutChange>();
        public override string ToString() => CreatedUtc.ToLocalTime().ToString("g") + " · " + State;
    }
    internal sealed class GuiLayoutChange
    {
        public string Path { get; set; }
        public bool Existed { get; set; }
        public string BeforeHash { get; set; }
        public string AfterHash { get; set; }
    }

    // Character layouts only. Callers must establish that the affected game clients are offline.
    // Layout-only is the default. Hotbar actions, key bindings and pet names require explicit component flags.
    // Credentials, chat history, arbitrary item maps and macro files are never captured.
    internal static class GuiLayouts
    {
        internal const int MaxFiles = 256, MaxFileBytes = 1024 * 1024, MaxTotalBytes = 8 * 1024 * 1024;
        internal const string GameDefaultId = "00000000000000000000000000000001";
        internal static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AONexus", "GuiLayouts");
        private static readonly Regex IdPattern = new Regex(@"\A[a-f0-9]{32}\z", RegexOptions.CultureInvariant);
        private static readonly Regex FilePattern = new Regex(@"\A(?:Prefs\.xml|DockAreas/(?:DockArea[0-9]{1,3}|RollupArea)\.xml|Containers/(?:Inventory|Bank|SpecialActions|ShortcutBar_[0-9]{1,2})\.xml|Chat/Windows/Window[0-9]{1,3}/Config\.xml)\z", RegexOptions.CultureInvariant);
        private static readonly HashSet<string> Geometry = Set("WindowFrame WindowPinButtonState DockableViewDockName borderless labels_on_top listview_mode selected_tab bar_orientation IsPrimary scroll_offset Minimized is_backmost is_frontmost window_transparency_inactive window_transparency_active show_timestamps hide_input_when_inactive deactivate_on_send is_textinput_enabled is_clickthrough is_message_fading_enabled is_window_open tab_index is_default_window is_startup_window window_name name visual_mode");
        private static readonly HashSet<string> Configs = Set("BattlestationConfig BrowserWindowConfig CCAlienXPBarConfig CCFriendlyHealthBarConfig CCHealthBarConfig CCHostileHealthBarConfig CCNanoBarConfig CCXPBarConfig ChatGroupWindowConfig CityTerminalBuyDlgConfig CityTerminalWindowConfig CompassWindowConfig FactionWindowConfig FightTargetNCUWindowConfig FriendsWindowConfig HelpButtonWindowConfig InfoViewConfig InspectViewConfig ItemShopWindowConfig KnowledgeViewConfig LFTWindowConfig LaserTargetConfig MailIconWindowConfig MailWindowConfig MarketWindowConfig MechSelectConfig MissionSelectionViewConfig MissionViewConfig NCUWindowConfig NPCChatWindowConfig NanoTargetNCUWindowConfig NanoViewConfig NotificationWindowConfig OptionWindowConfig PFMapWindowConfig PerkWindowConfig PetWindowConfig PetitionWindowConfig PlanetMapViewConfig RaidBarConfig RaidConfig ResearchConfig ShopViewConfig SignupTerminalConfig SkillConfig StatViewConfig StatWindowConfig TeamViewConfig TeleportTargetConfig TipViewConfig TowerBuildMenuViewConfig TradeSkillViewConfig TradeViewConfig TweakPanelConfig WearViewConfig");
        private static readonly HashSet<string> Visibility = Set("AutoCloseWindows LockHotbars LockRaidBars NumHotbars IconTimers IconTimerText ShowFightTargetNCU ShowNanoTargetNCU battlestation_window camera_menu cc_alienxp_bar cc_compass cc_control_center cc_friendly_health_bar cc_health_bar cc_hostile_health_bar cc_left_bar cc_left_menu cc_left_target_control cc_left_wing cc_mini_toolbar cc_nano_bar cc_right_bar cc_right_menu cc_right_target_control cc_right_wing cc_rollup_controller_fade_level cc_rollup_panel cc_section1 cc_xp_bar char_menu chat_group_window chat_system_active command_menu control_center_active faction_window friends_window inventory_window itemshop_window knowledge_window lasertarget_window lft_window mail_window map_menu map_window mechselect_window mission_window nano_window ncu_window notification_window perk_window pet_window petition_window planetmap_window raid_window research_window shortcutbar_window signupterminal_window skill_window specialaction_window stat_window team_menu team_view teleport_target_window tradeskill_window wear_window");
        private static readonly HashSet<string> PortableViews = Set("bank_window faction_window friends_window info_window inspect_window inventory_window knowledge_window map_window mission_window nano_window ncu_window ShopViewConfig specialaction_window stat_window team_view tip_window tradeskill_window TradeViewConfig wear_window pet_window raid_window research_window perk_window skill_window mail_window lft_window planetmap_window");
        private static HashSet<string> Set(string values) => new HashSet<string>(values.Split(' '), StringComparer.Ordinal);
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private static readonly object Sync = new object();
        // Injected only by synthetic tests, never exposed as a user setting.
        internal static Action<int> BeforeWrite;

        internal static List<GuiLayoutTemplate> ListTemplates(string storeRoot)
        {
            lock (Sync) return ListMetadata<GuiLayoutTemplate>(storeRoot, "Templates").Where(item => ValidId(item.Id)).OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        internal static List<GuiLayoutBackup> ListBackups(string storeRoot, string targetCharPath)
        {
            string target = CharacterPath(targetCharPath);
            lock (Sync) return ListMetadata<GuiLayoutBackup>(storeRoot, "Backups").Where(item => ValidId(item.Id) && SamePath(item.TargetPath, target)).OrderByDescending(item => item.CreatedUtc).ToList();
        }
        internal static GuiLayoutTemplate Capture(string storeRoot, string name, string sourceCharPath, string sourceCharName, string gameSignature)
            => CaptureWithOptions(storeRoot, name, sourceCharPath, sourceCharName, gameSignature, GuiLayoutComponents.Layout);
        internal static GuiLayoutTemplate CaptureWithOptions(string storeRoot, string name, string sourceCharPath, string sourceCharName, string gameSignature, GuiLayoutComponents components)
        {
            ValidateComponents(components);
            if (string.IsNullOrWhiteSpace(name) || name.Length > 80 || name.Any(char.IsControl)) throw new ArgumentException("Enter a template name of 1–80 characters.");
            lock (Sync)
            {
                string source = CharacterPath(sourceCharPath), root = Store(storeRoot);
                var data = new Dictionary<string, byte[]>(StringComparer.Ordinal);
                int total = 0;
                foreach (string file in CharacterFiles(source))
                {
                    byte[] original = Read(file, ref total);
                    string relative = Relative(source, file);
                    XmlDocument filtered = Filter(Parse(original), relative, components);
                    if (!filtered.DocumentElement.HasChildNodes) continue;
                    data.Add(relative, Encode(filtered));
                }
                if (data.Count == 0) throw new IOException("No supported layout settings were found in this character's saved preferences.");
                var metadata = new GuiLayoutTemplate { Id = Guid.NewGuid().ToString("N"), Name = name.Trim(), SourceCharacter = Limit(sourceCharName), GameSignature = Limit(gameSignature), CreatedUtc = DateTime.UtcNow, FileCount = data.Count, Components = components };
                string folder = Item(root, "Templates", metadata.Id);
                Directory.CreateDirectory(folder);
                foreach (var pair in data)
                {
                    string target = Child(folder, "Files/" + pair.Key); WriteAtomic(target, pair.Value);
                    metadata.Files.Add(new GuiLayoutFile { Path = pair.Key, Hash = Hash(pair.Value) });
                }
                SaveMetadata(folder, metadata); return metadata;
            }
        }
        internal static GuiLayoutBackup Apply(string storeRoot, string templateId, string targetCharPath)
            => ApplyWithOptions(storeRoot, templateId, targetCharPath, LoadMetadata<GuiLayoutTemplate>(Item(Store(storeRoot), "Templates", templateId)).Components);
        internal static GuiLayoutBackup ResetToGameDefault(string storeRoot, string gamePath, string targetCharPath, bool preflight)
        {
            lock (Sync)
            {
                string root = Store(storeRoot), target = CharacterPath(targetCharPath), game = Store(gamePath);
                int total = 0;
                var defaults = Filter(Parse(Read(Child(game, "cd_image/gui/Default/CharPrefs.xml"), ref total)), "Prefs.xml", GuiLayoutComponents.Layout);
                // Bar count is a personal choice; reset presentation without hiding additional bars.
                foreach (XmlElement node in defaults.DocumentElement.ChildNodes.OfType<XmlElement>().Where(n => n.GetAttribute("name") == "NumHotbars").ToArray()) defaults.DocumentElement.RemoveChild(node);
                if (!defaults.DocumentElement.HasChildNodes) throw new IOException("The game installation has no supported default layout settings.");
                var originals = new Dictionary<string, byte[]>(StringComparer.Ordinal);
                var outputs = new Dictionary<string, byte[]>(StringComparer.Ordinal);
                var files = CharacterFiles(target).Select(file => Relative(target, file)).ToList();
                if (!files.Contains("Prefs.xml")) files.Add("Prefs.xml");
                foreach (string relative in files)
                {
                    string path = Child(target, relative);
                    byte[] before = File.Exists(path) ? Read(path, ref total) : null;
                    var document = before == null ? Empty("Root") : Parse(before);
                    var layout = Filter(document, relative, GuiLayoutComponents.Layout);
                    RemoveLayoutOverrides(document.DocumentElement, layout.DocumentElement, relative);
                    if (relative == "Prefs.xml") Merge(document.DocumentElement, defaults.DocumentElement, false);
                    byte[] after = Encode(document);
                    if (before != null && Hash(before) == Hash(after)) continue;
                    originals[relative] = before; outputs[relative] = after;
                }
                if (preflight) return null;
                return CommitLayout(root, GameDefaultId, target, GuiLayoutComponents.Layout, originals, outputs);
            }
        }
        private static void RemoveLayoutOverrides(XmlElement target, XmlElement layout, string relative)
        {
            foreach (XmlElement node in layout.ChildNodes.OfType<XmlElement>())
            {
                string name = node.GetAttribute("name");
                if (name == "NumHotbars" || name == "IsPrimary") continue;
                if (relative.StartsWith("Chat/", StringComparison.Ordinal) && (name == "name" || name == "window_name" || name == "is_default_window" || name == "is_startup_window" || name == "is_textinput_enabled")) continue;
                var existing = target.ChildNodes.OfType<XmlElement>().FirstOrDefault(item => item.LocalName == node.LocalName && item.GetAttribute("name") == name);
                if (existing == null) continue;
                if (relative.StartsWith("DockAreas/", StringComparison.Ordinal))
                {
                    if (name != "dock_config") continue;
                    var retained = DockMembers(existing).Where(member => !PortableViews.Contains(member.Item1)).ToList();
                    // Keep containers used by backpacks and other non-portable views intact.
                    if (retained.Count == 0)
                        foreach (XmlElement child in existing.ChildNodes.OfType<XmlElement>().Where(item => Geometry.Contains(item.GetAttribute("name"))).ToArray()) existing.RemoveChild(child);
                    SetDockMembers(existing, retained);
                }
                else if (node.LocalName == "Archive") RemoveLayoutOverrides(existing, node, relative);
                else target.RemoveChild(existing);
            }
        }
        internal static GuiLayoutBackup ApplyWithOptions(string storeRoot, string templateId, string targetCharPath, GuiLayoutComponents components)
        {
            ValidateComponents(components);
            lock (Sync)
            {
                string root = Store(storeRoot), target = CharacterPath(targetCharPath), templateFolder = Item(root, "Templates", templateId);
                var template = LoadMetadata<GuiLayoutTemplate>(templateFolder);
                if (template == null || template.Id != templateId || template.Files == null || template.Files.Count == 0 || template.Files.Count > MaxFiles) throw new IOException("Invalid layout template.");
                if ((components & template.Components) != components) throw new IOException("The template does not contain all selected components.");
                var inputs = new Dictionary<string, XmlDocument>(StringComparer.Ordinal);
                int total = 0;
                foreach (var file in template.Files)
                {
                    if (file == null) throw new IOException("Invalid template file.");
                    RequireRelative(file.Path); byte[] data = Read(Child(templateFolder, "Files/" + file.Path), ref total);
                    if (Hash(data) != file.Hash) throw new IOException("Template integrity check failed.");
                    // Re-filter stored templates: metadata and library files are untrusted too.
                    var filtered = Filter(Parse(data), file.Path, components);
                    if (filtered.DocumentElement.HasChildNodes) inputs.Add(file.Path, filtered);
                }
                var originals = new Dictionary<string, byte[]>(StringComparer.Ordinal);
                var outputs = new Dictionary<string, byte[]>(StringComparer.Ordinal);
                foreach (var input in inputs)
                {
                    string path = Child(target, input.Key);
                    byte[] before = File.Exists(path) ? Read(path, ref total) : null;
                    XmlDocument destination = before == null ? Empty(input.Value.DocumentElement.LocalName) : Parse(before);
                    if (destination.DocumentElement.LocalName != input.Value.DocumentElement.LocalName) throw new IOException("Incompatible preference XML: " + input.Key);
                    Merge(destination.DocumentElement, input.Value.DocumentElement, input.Key.StartsWith("DockAreas/", StringComparison.Ordinal));
                    byte[] after = Encode(destination);
                    if (before != null && Hash(before) == Hash(after)) continue;
                    originals[input.Key] = before; outputs[input.Key] = after;
                }
                if (components.HasFlag(GuiLayoutComponents.Layout)) RemoveDuplicateDockMemberships(target, inputs, originals, outputs, ref total);
                return CommitLayout(root, templateId, target, components, originals, outputs);
            }
        }
        private static GuiLayoutBackup CommitLayout(string root, string templateId, string target, GuiLayoutComponents components, Dictionary<string, byte[]> originals, Dictionary<string, byte[]> outputs)
        {
                if (outputs.Count == 0) return new GuiLayoutBackup { TemplateId = templateId, TargetPath = target, CreatedUtc = DateTime.UtcNow, State = "Unchanged" };
                var backup = new GuiLayoutBackup { Id = Guid.NewGuid().ToString("N"), TemplateId = templateId, TargetPath = target, CreatedUtc = DateTime.UtcNow, State = "Applying", Components = components };
                string backupFolder = Item(root, "Backups", backup.Id); Directory.CreateDirectory(backupFolder);
                foreach (var pair in outputs)
                {
                    byte[] before = originals[pair.Key];
                    backup.Changes.Add(new GuiLayoutChange { Path = pair.Key, Existed = before != null, BeforeHash = before == null ? null : Hash(before), AfterHash = Hash(pair.Value) });
                    if (before != null) WriteAtomic(Child(backupFolder, "Files/" + pair.Key), before);
                    WriteAtomic(Child(backupFolder, "Applied/" + pair.Key), pair.Value);
                }
                SaveMetadata(backupFolder, backup);
                var written = new List<string>();
                try
                {
                    foreach (var pair in outputs)
                    {
                        BeforeWrite?.Invoke(written.Count);
                        string path = Child(target, pair.Key);
                        VerifyCurrent(path, originals[pair.Key] == null ? null : Hash(originals[pair.Key]));
                        WriteAtomic(path, pair.Value); written.Add(pair.Key);
                    }
                    backup.State = "Applied"; SaveMetadata(backupFolder, backup); PruneBackups(root, target, backup.Id); return backup;
                }
                catch
                {
                    bool complete = true;
                    foreach (string relative in written.AsEnumerable().Reverse())
                    {
                        try { string path = Child(target, relative); VerifyCurrent(path, Hash(outputs[relative])); RestoreBytes(path, originals[relative]); }
                        catch { complete = false; }
                    }
                    backup.State = complete ? "Rolled back" : "Recovery required";
                    SaveMetadata(backupFolder, backup); throw;
                }
        }
        internal static void PreflightApply(string storeRoot, string templateId, string targetCharPath)
            => PreflightApplyWithOptions(storeRoot, templateId, targetCharPath, LoadMetadata<GuiLayoutTemplate>(Item(Store(storeRoot), "Templates", templateId)).Components);
        internal static void PreflightApplyWithOptions(string storeRoot, string templateId, string targetCharPath, GuiLayoutComponents components)
        {
            lock (Sync)
            {
                ValidateComponents(components); string target = CharacterPath(targetCharPath), folder = Item(Store(storeRoot), "Templates", templateId);
                var template = LoadMetadata<GuiLayoutTemplate>(folder);
                if (template == null || template.Id != templateId || template.Files == null || template.Files.Count == 0 || template.Files.Count > MaxFiles || (components & template.Components) != components) throw new IOException("Invalid template or unavailable components.");
                int total = 0; var names = new HashSet<string>();
                foreach (var file in template.Files)
                {
                    if (file == null) throw new IOException("Invalid template file."); RequireRelative(file.Path);
                    if (!names.Add(file.Path)) throw new IOException("Duplicate template file.");
                    byte[] data = Read(Child(folder, "Files/" + file.Path), ref total);
                    if (Hash(data) != file.Hash) throw new IOException("Template integrity check failed.");
                    var source = Filter(Parse(data), file.Path, components);
                    string destination = Child(target, file.Path);
                    if (File.Exists(destination))
                    {
                        var existing = Parse(Read(destination, ref total));
                        if (source.DocumentElement.LocalName != existing.DocumentElement.LocalName) throw new IOException("Incompatible preference XML: " + file.Path);
                        Merge(existing.DocumentElement, source.DocumentElement, file.Path.StartsWith("DockAreas/", StringComparison.Ordinal)); Encode(existing);
                    }
                }
            }
        }
        private static void ValidateComponents(GuiLayoutComponents components)
        {
            if (components == 0 || ((int)components & ~15) != 0) throw new ArgumentException("Select at least one supported GUI component.");
        }
        internal static void Restore(string storeRoot, string backupId, string targetCharPath)
        {
            lock (Sync)
            {
                string target = CharacterPath(targetCharPath), folder = Item(Store(storeRoot), "Backups", backupId);
                var backup = LoadMetadata<GuiLayoutBackup>(folder);
                if (backup == null || backup.Id != backupId || !SamePath(backup.TargetPath, target) || backup.Changes == null || backup.Changes.Count > MaxFiles) throw new IOException("This backup does not belong to the selected character.");
                var originals = new Dictionary<string, byte[]>(); var current = new Dictionary<string, byte[]>(); int total = 0;
                foreach (var change in backup.Changes)
                {
                    if (change == null) throw new IOException("Invalid backup entry.");
                    RequireRelative(change.Path); string path = Child(target, change.Path);
                    byte[] before = change.Existed ? Read(Child(folder, "Files/" + change.Path), ref total) : null;
                    if (before != null && Hash(before) != change.BeforeHash) throw new IOException("Backup integrity check failed.");
                    byte[] now = File.Exists(path) ? Read(path, ref total) : null;
                    string currentHash = now == null ? null : Hash(now);
                    if (currentHash == change.BeforeHash) continue;
                    byte[] restored = before;
                    if (currentHash != change.AfterHash)
                    {
                        // The game normally rewrites these files on logout. Reverse only the
                        // portable fields changed by this operation, preserving newer personal data.
                        byte[] applied = change.AfterHash == null ? null : Read(Child(folder, "Applied/" + change.Path), ref total);
                        if (applied != null && Hash(applied) != change.AfterHash) throw new IOException("Backup integrity check failed.");
                        string rootName = change.Path == "Prefs.xml" ? "Root" : "Archive";
                        XmlDocument afterLayout = applied == null ? Empty(rootName) : Filter(Parse(applied), change.Path, backup.Components);
                        XmlDocument beforeLayout = before == null ? Empty(afterLayout.DocumentElement.LocalName) : Filter(Parse(before), change.Path, backup.Components);
                        XmlDocument present = now == null ? Empty(afterLayout.DocumentElement.LocalName) : Parse(now);
                        UndoLayout(present.DocumentElement, beforeLayout.DocumentElement, afterLayout.DocumentElement, change.Path.StartsWith("DockAreas/", StringComparison.Ordinal));
                        restored = now == null && !present.DocumentElement.HasChildNodes ? null : Encode(present);
                    }
                    originals.Add(change.Path, restored); current.Add(change.Path, now);
                }
                if (originals.Count == 0) return;
                var undo = new GuiLayoutBackup { Id = Guid.NewGuid().ToString("N"), TemplateId = backup.TemplateId, TargetPath = target, CreatedUtc = DateTime.UtcNow, State = "Restoring", Components = backup.Components };
                string undoFolder = Item(Store(storeRoot), "Backups", undo.Id); Directory.CreateDirectory(undoFolder);
                foreach (var pair in originals)
                {
                    byte[] now = current[pair.Key];
                    undo.Changes.Add(new GuiLayoutChange { Path = pair.Key, Existed = now != null, BeforeHash = now == null ? null : Hash(now), AfterHash = pair.Value == null ? null : Hash(pair.Value) });
                    if (now != null) WriteAtomic(Child(undoFolder, "Files/" + pair.Key), now);
                    if (pair.Value != null) WriteAtomic(Child(undoFolder, "Applied/" + pair.Key), pair.Value);
                }
                SaveMetadata(undoFolder, undo);
                var written = new List<string>();
                try
                {
                    foreach (var pair in originals)
                    {
                        BeforeWrite?.Invoke(written.Count);
                        string path = Child(target, pair.Key); VerifyCurrent(path, current[pair.Key] == null ? null : Hash(current[pair.Key]));
                        RestoreBytes(path, pair.Value); written.Add(pair.Key);
                    }
                    backup.State = "Restored"; SaveMetadata(folder, backup);
                    undo.State = "Applied"; SaveMetadata(undoFolder, undo); PruneBackups(Store(storeRoot), target, undo.Id);
                }
                catch
                {
                    foreach (string relative in written.AsEnumerable().Reverse())
                    {
                        string path = Child(target, relative); VerifyCurrent(path, originals[relative] == null ? null : Hash(originals[relative])); RestoreBytes(path, current[relative]);
                    }
                    undo.State = "Rolled back"; SaveMetadata(undoFolder, undo);
                    throw;
                }
            }
        }
        private static void RemoveDuplicateDockMemberships(string target, Dictionary<string, XmlDocument> inputs, Dictionary<string, byte[]> originals, Dictionary<string, byte[]> outputs, ref int total)
        {
            var assigned = new HashSet<string>(StringComparer.Ordinal);
            foreach (var input in inputs.Where(pair => pair.Key.StartsWith("DockAreas/", StringComparison.Ordinal)))
            {
                var config = input.Value.DocumentElement.ChildNodes.OfType<XmlElement>().FirstOrDefault(node => node.GetAttribute("name") == "dock_config");
                if (config != null) foreach (var member in DockMembers(config)) assigned.Add(member.Item1);
            }
            if (assigned.Count == 0) return;
            foreach (string file in CharacterFiles(target).Where(path => Relative(target, path).StartsWith("DockAreas/", StringComparison.Ordinal)))
            {
                string relative = Relative(target, file); if (inputs.ContainsKey(relative)) continue;
                byte[] before = Read(file, ref total); var xml = Parse(before);
                var config = xml.DocumentElement.ChildNodes.OfType<XmlElement>().FirstOrDefault(node => node.GetAttribute("name") == "dock_config");
                if (config == null) continue;
                var members = DockMembers(config); var retained = members.Where(member => !assigned.Contains(member.Item1)).ToList();
                if (retained.Count == members.Count) continue;
                SetDockMembers(config, retained); originals[relative] = before; outputs[relative] = Encode(xml);
                if (outputs.Count > MaxFiles) throw new IOException("Too many affected layout files.");
            }
        }
        private static void UndoLayout(XmlElement current, XmlElement before, XmlElement after, bool dock)
        {
            foreach (XmlElement changed in after.ChildNodes.OfType<XmlElement>())
            {
                string name = changed.GetAttribute("name");
                var old = before.ChildNodes.OfType<XmlElement>().FirstOrDefault(node => node.LocalName == changed.LocalName && node.GetAttribute("name") == name);
                if (old != null && old.OuterXml == changed.OuterXml) continue;
                var present = current.ChildNodes.OfType<XmlElement>().FirstOrDefault(node => node.LocalName == changed.LocalName && node.GetAttribute("name") == name);
                if (changed.LocalName == "Archive")
                {
                    if (present == null) { present = (XmlElement)current.OwnerDocument.ImportNode(changed, false); current.AppendChild(present); }
                    var empty = current.OwnerDocument.CreateElement("Archive");
                    if (dock && name == "dock_config")
                    {
                        var retained = DockMembers(present).Where(member => !PortableViews.Contains(member.Item1));
                        var restored = old == null ? new List<Tuple<string, XmlElement>>() : DockMembers(old);
                        SetDockMembers(present, restored.Concat(retained).ToList());
                    }
                    UndoLayout(present, old ?? empty, changed, dock);
                    if (!present.HasChildNodes && old == null) current.RemoveChild(present);
                }
                else
                {
                    if (dock && (name == "dock_node_configs" || name == "docked_view_identities")) continue;
                    if (old == null) { if (present != null) current.RemoveChild(present); }
                    else if (present == null) current.AppendChild(current.OwnerDocument.ImportNode(old, true));
                    else current.ReplaceChild(current.OwnerDocument.ImportNode(old, true), present);
                }
            }
            foreach (XmlElement old in before.ChildNodes.OfType<XmlElement>())
            {
                string name = old.GetAttribute("name");
                if (after.ChildNodes.OfType<XmlElement>().Any(node => node.LocalName == old.LocalName && node.GetAttribute("name") == name)) continue;
                if (dock && (name == "dock_node_configs" || name == "docked_view_identities")) continue;
                var present = current.ChildNodes.OfType<XmlElement>().FirstOrDefault(node => node.LocalName == old.LocalName && node.GetAttribute("name") == name);
                if (present == null) current.AppendChild(current.OwnerDocument.ImportNode(old, true));
                else current.ReplaceChild(current.OwnerDocument.ImportNode(old, true), present);
            }
        }
        // Retain ten complete snapshots per character; failed/incomplete recoveries are never pruned.
        private static void PruneBackups(string root, string target, string keepId)
        {
            foreach (var old in ListMetadata<GuiLayoutBackup>(root, "Backups").Where(item => item.Id != keepId && ValidId(item.Id) && SamePath(item.TargetPath, target) && (item.State == "Applied" || item.State == "Restored" || item.State == "Rolled back")).OrderByDescending(item => item.CreatedUtc).Skip(9))
            {
                try { string folder = Item(root, "Backups", old.Id); ValidateTree(folder, 0, new int[1]); Directory.Delete(folder, true); }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { }
            }
        }
        internal static void DeleteTemplate(string storeRoot, string templateId)
        {
            lock (Sync)
            {
                string folder = Item(Store(storeRoot), "Templates", templateId);
                if (!Directory.Exists(folder)) return;
                // Verify every descendant first; never recursively follow a junction.
                ValidateTree(folder, 0, new int[1]); Directory.Delete(folder, true);
            }
        }
        internal static void RenameTemplate(string storeRoot, string templateId, string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 80 || name.Any(char.IsControl)) throw new ArgumentException("Enter a template name of 1–80 characters.");
            lock (Sync) { string folder = Item(Store(storeRoot), "Templates", templateId); var template = LoadMetadata<GuiLayoutTemplate>(folder); if (template.Id != templateId) throw new IOException("Invalid layout template."); template.Name = name.Trim(); SaveMetadata(folder, template); }
        }
        private static void ValidateTree(string folder, int depth, int[] count)
        {
            SafePath(folder); if (depth > 8 || ++count[0] > 1024) throw new IOException("Layout library directory exceeds limits.");
            foreach (string path in Directory.EnumerateFileSystemEntries(folder)) { SafePath(path); if (Directory.Exists(path)) ValidateTree(path, depth + 1, count); else if (++count[0] > 1024) throw new IOException("Too many layout files."); }
        }
        private static XmlDocument Filter(XmlDocument source, string relative, GuiLayoutComponents components)
        {
            string expected = relative == "Prefs.xml" ? "Root" : "Archive";
            if (source.DocumentElement?.LocalName != expected) throw new IOException("Unsupported preference XML: " + relative);
            var result = Empty(expected);
            foreach (XmlElement element in source.DocumentElement.ChildNodes.OfType<XmlElement>())
            {
                string name = element.GetAttribute("name");
                if (relative == "Prefs.xml")
                {
                    if (components.HasFlag(GuiLayoutComponents.Layout) && element.LocalName == "Value" && Visibility.Contains(name)) result.DocumentElement.AppendChild(result.ImportNode(element, true));
                    else if (components.HasFlag(GuiLayoutComponents.Layout) && element.LocalName == "Archive" && Configs.Contains(name)) AddGeometry(result.DocumentElement, element);
                    else if (components.HasFlag(GuiLayoutComponents.Keybindings) && name == "KeyBindings" && element.LocalName == "Archive") result.DocumentElement.AppendChild(result.ImportNode(element, true));
                    else if (components.HasFlag(GuiLayoutComponents.PetNames) && Regex.IsMatch(name, @"\APetName[1-6]\z") && element.LocalName == "Value") result.DocumentElement.AppendChild(result.ImportNode(element, true));
                }
                else if (relative.StartsWith("DockAreas/", StringComparison.Ordinal))
                {
                    if (!components.HasFlag(GuiLayoutComponents.Layout)) continue;
                    if ((name == "dock_name" || name == "dock_type") && element.LocalName == "String") result.DocumentElement.AppendChild(result.ImportNode(element, true));
                    else if (name == "dock_config" && element.LocalName == "Archive")
                    {
                        var copied = (XmlElement)result.ImportNode(element, false); result.DocumentElement.AppendChild(copied);
                        foreach (XmlElement child in element.ChildNodes.OfType<XmlElement>()) if (Geometry.Contains(child.GetAttribute("name"))) copied.AppendChild(result.ImportNode(child, true));
                        SetDockMembers(copied, DockMembers(element).Where(member => PortableViews.Contains(member.Item1)).ToList());
                    }
                }
                else
                {
                    if (components.HasFlag(GuiLayoutComponents.Layout)) AddGeometry(result.DocumentElement, element);
                    if (components.HasFlag(GuiLayoutComponents.Hotbars) && relative.StartsWith("Containers/ShortcutBar_", StringComparison.Ordinal) &&
                        ((name == "position_map" && element.LocalName == "Archive") || (name == "selected_bar" && element.LocalName == "Int32"))) result.DocumentElement.AppendChild(result.ImportNode(element, true));
                }
            }
            return result;
        }
        private static void AddGeometry(XmlElement parent, XmlElement source)
        {
            string name = source.GetAttribute("name");
            if (Geometry.Contains(name) && source.LocalName != "Archive" && source.LocalName != "Array") { parent.AppendChild(parent.OwnerDocument.ImportNode(source, true)); return; }
            // Only known presentation containers, never item_position_map, key bindings or arbitrary archives.
            if (source.LocalName != "Archive" || !(Configs.Contains(name) || name.StartsWith("listview_config", StringComparison.Ordinal) || name == "chat_window_config" || name == "chat_view_config")) return;
            var node = (XmlElement)parent.OwnerDocument.ImportNode(source, false);
            foreach (XmlElement child in source.ChildNodes.OfType<XmlElement>()) AddGeometry(node, child);
            if (node.HasChildNodes) parent.AppendChild(node);
        }
        private static void Merge(XmlElement target, XmlElement source, bool dock)
        {
            foreach (XmlElement node in source.ChildNodes.OfType<XmlElement>())
            {
                string name = node.GetAttribute("name");
                var existing = target.ChildNodes.OfType<XmlElement>().FirstOrDefault(item => item.LocalName == node.LocalName && item.GetAttribute("name") == name);
                if (dock && name == "dock_config" && existing != null)
                {
                    var retained = DockMembers(existing).Where(member => !PortableViews.Contains(member.Item1)).ToList();
                    var incoming = DockMembers(node); Merge(existing, node, false);
                    SetDockMembers(existing, incoming.Concat(retained.Where(member => incoming.All(other => other.Item1 != member.Item1))).ToList());
                }
                else if (node.LocalName == "Archive" && existing != null && name != "KeyBindings" && name != "position_map") Merge(existing, node, dock);
                else
                {
                    XmlNode copy = target.OwnerDocument.ImportNode(node, true);
                    if (existing == null) target.AppendChild(copy); else target.ReplaceChild(copy, existing);
                }
            }
        }
        private static List<Tuple<string, XmlElement>> DockMembers(XmlElement config)
        {
            var identities = config.ChildNodes.OfType<XmlElement>().FirstOrDefault(item => item.GetAttribute("name") == "docked_view_identities");
            var pages = config.ChildNodes.OfType<XmlElement>().FirstOrDefault(item => item.GetAttribute("name") == "dock_node_configs");
            var ids = identities == null ? new List<string>() : identities.LocalName == "String" ? new List<string> { Unquote(identities.GetAttribute("value")) }
                : identities.ChildNodes.OfType<XmlElement>().Where(item => item.LocalName == "String").Select(item => Unquote(item.GetAttribute("value"))).ToList();
            var pageList = pages?.ChildNodes.OfType<XmlElement>().ToList() ?? new List<XmlElement>();
            return ids.Select((id, index) => Tuple.Create(id, index < pageList.Count ? pageList[index] : null)).ToList();
        }
        private static void SetDockMembers(XmlElement config, List<Tuple<string, XmlElement>> members)
        {
            foreach (var item in config.ChildNodes.OfType<XmlElement>().Where(item => item.GetAttribute("name") == "docked_view_identities" || item.GetAttribute("name") == "dock_node_configs").ToArray()) config.RemoveChild(item);
            var ids = config.OwnerDocument.CreateElement("Array"); ids.SetAttribute("name", "docked_view_identities");
            var pages = config.OwnerDocument.CreateElement("Array"); pages.SetAttribute("name", "dock_node_configs");
            foreach (var member in members)
            {
                var id = config.OwnerDocument.CreateElement("String"); id.SetAttribute("value", "\"" + member.Item1 + "\""); ids.AppendChild(id);
                var page = config.OwnerDocument.CreateElement("Archive"); page.SetAttribute("code", "0");
                if (member.Item2 != null) foreach (XmlElement node in member.Item2.ChildNodes.OfType<XmlElement>())
                    if (node.GetAttribute("name") == "is_page_expanded" || node.GetAttribute("name") == "page_height") page.AppendChild(config.OwnerDocument.ImportNode(node, true));
                pages.AppendChild(page);
            }
            config.AppendChild(pages); config.AppendChild(ids);
        }
        private static string Unquote(string value) => value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"' ? value.Substring(1, value.Length - 2) : value;
        private static XmlDocument Empty(string root) { var document = new XmlDocument { XmlResolver = null }; document.AppendChild(document.CreateElement(root)); return document; }
        private static XmlDocument Parse(byte[] bytes)
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxFileBytes, MaxCharactersFromEntities = 0 };
            using (var input = new MemoryStream(bytes)) using (var reader = XmlReader.Create(input, settings)) { var document = new XmlDocument { XmlResolver = null }; document.Load(reader); return document; }
        }
        private static byte[] Encode(XmlDocument xml)
        {
            using (var output = new MemoryStream()) { using (var writer = XmlWriter.Create(output, new XmlWriterSettings { Encoding = Utf8, Indent = true, OmitXmlDeclaration = true })) xml.Save(writer); byte[] result = output.ToArray(); if (result.Length > MaxFileBytes) throw new IOException("Layout XML exceeds size limit."); return result; }
        }
        private static IEnumerable<string> CharacterFiles(string root)
        {
            var results = new List<string>();
            foreach (string folder in new[] { root, Path.Combine(root, "DockAreas"), Path.Combine(root, "Containers") })
            {
                SafePath(folder); if (!Directory.Exists(folder)) continue;
                foreach (string file in Directory.EnumerateFiles(folder).Take(MaxFiles + 1)) if (FilePattern.IsMatch(Relative(root, file))) results.Add(file);
                if (results.Count > MaxFiles) throw new IOException("Too many layout files.");
            }
            string windows = Child(root, "Chat/Windows");
            if (Directory.Exists(windows)) foreach (string folder in Directory.EnumerateDirectories(windows).Take(MaxFiles + 1))
            { SafePath(folder); string file = Path.Combine(folder, "Config.xml"); if (File.Exists(file) && FilePattern.IsMatch(Relative(root, file))) results.Add(file); if (results.Count > MaxFiles) throw new IOException("Too many chat windows."); }
            return results.OrderBy(path => path, StringComparer.Ordinal).ToArray();
        }
        private static List<T> ListMetadata<T>(string root, string category)
        {
            string folder = Child(Store(root), category); var result = new List<T>(); if (!Directory.Exists(folder)) return result;
            foreach (string path in Directory.EnumerateDirectories(folder).Take(1024))
            {
                if (!ValidId(Path.GetFileName(path))) continue;
                try { SafePath(path); var item = LoadMetadata<T>(path); if (item != null) result.Add(item); }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException || error is InvalidOperationException) { }
            }
            return result;
        }
        private static JavaScriptSerializer Json() => new JavaScriptSerializer { MaxJsonLength = MaxFileBytes, RecursionLimit = 16 };
        private static T LoadMetadata<T>(string folder) { int bytes = 0; return Json().Deserialize<T>(Utf8.GetString(Read(Child(folder, "manifest.json"), ref bytes))); }
        private static void SaveMetadata(string folder, object value) => WriteAtomic(Child(folder, "manifest.json"), Utf8.GetBytes(Json().Serialize(value)));
        private static string Limit(string value) => (value ?? "").Substring(0, Math.Min((value ?? "").Length, 200));
        private static string Store(string root) { if (string.IsNullOrWhiteSpace(root) || !Path.IsPathRooted(root)) throw new IOException("Use an absolute layout library path."); string result = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar); SafePath(result); return result; }
        private static string CharacterPath(string path)
        {
            string value = Store(path); if (!Directory.Exists(value) || !Regex.IsMatch(Path.GetFileName(value), @"\AChar[1-9][0-9]{0,9}\z", RegexOptions.CultureInvariant)) throw new IOException("Choose an existing character preferences folder (Char123)."); return value;
        }
        private static bool ValidId(string id) => id != null && IdPattern.IsMatch(id);
        private static string Item(string root, string category, string id) { if (!ValidId(id)) throw new IOException("Invalid layout identifier."); return Child(root, category + "/" + id); }
        private static void RequireRelative(string path) { if (path == null || !FilePattern.IsMatch(path)) throw new IOException("Unsupported layout file path."); }
        private static string Relative(string root, string path) => Path.GetFullPath(path).Substring(root.Length + 1).Replace('\\', '/');
        private static string Child(string root, string relative)
        {
            if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(":") || relative.Split('/', '\\').Any(part => part == ".." || part == "." || part.Length == 0)) throw new IOException("Invalid layout path.");
            string value = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!value.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Layout path escapes its folder."); SafePath(value); return value;
        }
        private static void SafePath(string path)
        {
            for (string cursor = Path.GetFullPath(path); !string.IsNullOrEmpty(cursor); cursor = Path.GetDirectoryName(cursor))
            {
                if ((File.Exists(cursor) || Directory.Exists(cursor)) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0) throw new IOException("Symbolic links and junctions are not supported for GUI files.");
            }
        }
        private static byte[] Read(string path, ref int total)
        {
            SafePath(path); using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            { if (file.Length > MaxFileBytes || total + file.Length > MaxTotalBytes) throw new IOException("GUI files exceed size limits."); byte[] bytes = new byte[file.Length]; int offset = 0, count; while (offset < bytes.Length && (count = file.Read(bytes, offset, bytes.Length - offset)) > 0) offset += count; if (offset != bytes.Length) throw new IOException("GUI file changed while reading."); total += bytes.Length; return bytes; }
        }
        private static string Hash(byte[] data) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant(); }
        private static bool SamePath(string a, string b) { try { return string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); } catch { return false; } }
        private static void VerifyCurrent(string path, string expected) { int total = 0; string actual = File.Exists(path) ? Hash(Read(path, ref total)) : null; if (actual != expected) throw new IOException("Preferences changed during this operation. Close the game and retry."); }
        private static void RestoreBytes(string path, byte[] bytes) { SafePath(path); if (bytes == null) { if (File.Exists(path)) File.Delete(path); } else WriteAtomic(path, bytes); }
        private static void WriteAtomic(string path, byte[] data)
        {
            SafePath(path); Directory.CreateDirectory(Path.GetDirectoryName(path)); SafePath(path);
            string temporary = path + ".nexus-" + Guid.NewGuid().ToString("N") + ".tmp";
            try { using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { output.Write(data, 0, data.Length); output.Flush(true); } SafePath(path); if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
