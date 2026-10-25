using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Xml;

namespace AOLauncher
{
    internal sealed class GuiSkinCatalogEntry
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string SourceUrl => GuiSkins.AuthorUrl;
        internal string DownloadId, Sha256;
        public override string ToString() => Name;
    }
    internal sealed class GuiSkinInstallResult
    {
        public string SkinName { get; set; }
        public string InstalledPath { get; set; }
        public string SourceUrl { get; set; }
    }
    internal sealed class GuiSkinBackup
    {
        public string Directory { get; set; }
        public DateTime CreatedUtc { get; set; }
        public string SkinName { get; set; }
        public string PrefsRoot { get; set; }
        public string PrefsSha256 { get; set; }
        public bool SelectionCompleted { get; set; }
        public override string ToString() => CreatedUtc.ToLocalTime().ToString("g") + " · " + SkinName;
    }

    // Skins are installation-wide. The caller must stop every affected game before
    // selecting/restoring a skin. This service never starts a script or a game.
    internal static class GuiSkins
    {
        internal const string AuthorUrl = "https://sites.google.com/site/notumdovvetech/";
        private const int MaxArchiveBytes = 8 * 1024 * 1024, MaxFileBytes = 16 * 1024 * 1024, MaxExpandedBytes = 64 * 1024 * 1024;
        private static readonly HashSet<string> DataExtensions = new HashSet<string>(new[] { ".xml", ".uvga", ".uvgi", ".png", ".jpg", ".jpeg", ".txt" }, StringComparer.OrdinalIgnoreCase);
        internal static IReadOnlyList<GuiSkinCatalogEntry> Catalog { get; } = new[] {
            Entry("dovvetech", "Notum Dovvetech", "1rw9Buudgfb_GjGxidvyeKrpX5yhocifW", "9e314a89e5c2a58356cea9fe1f8c72232577b4d735c684eb824f9b41befbf456"),
            Entry("dovvetech-dark", "Notum Dovvetech Dark", "1UfHZY6Fw3ZgAWtu2xt4FsX4gaMyk5l66", "7408a77453853f1209cf6a3849970122006fe216cbb98374e74af03b25a05255"),
            Entry("dovvetech-edge", "Notum Dovvetech Edge", "1lwVZ_jp87XKFnPw7ncGJi8IfZuUU5QpA", "e702ffdf8e933f6c2840c099e23a0e6e9619c1f7d5d1c13833168d08da8a3ad5"),
            Entry("darktech", "Notum Darktech", "1v64Ahjx-sW-DDToMRQZ0sWmlCJDWZUon", "de212cea0f0fe131b7bf51ed526744d614d1a1d9f70f07b14b840ef3e2b69eb0"),
            Entry("darktech-mini", "Notum Darktech Mini", "1g0_cUWKxHIGs1Lr_DzADCdTu-ZPv2SIY", "5ca80f80495db12bdfafedc61ce55ee8e7db87c2fca6f39515db526fb39a312a"),
            Entry("darktech-hud", "Notum Darktech Mini HUD", "1Vsv9gIF-T7ScODA9McLFeTQ69IHIKZj4", "37bcca05800f68daaee3bc1589199006bf6d799912f1f3c215ef28989af57b2d"),
            Entry("darktech-glass", "Notum Darktech Glass", "1mMzHVtuD_A0qzaI_m2eJRgxly1RKXi0_", "c1b1c24eb7ae129cd791691e086ff39d77f44a65396d69af99e718f76a62542c") };
        private static GuiSkinCatalogEntry Entry(string id, string name, string download, string hash)
            => new GuiSkinCatalogEntry { Id = id, Name = name, DownloadId = download, Sha256 = hash };

        internal static string ResolvePrefsRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Choose the account's Prefs folder first.");
            string current = Path.GetFullPath(path);
            for (int i = 0; i < 5 && !string.IsNullOrEmpty(current); i++, current = Path.GetDirectoryName(current))
                if (string.Equals(Path.GetFileName(current.TrimEnd(Path.DirectorySeparatorChar)), "Prefs", StringComparison.OrdinalIgnoreCase)
                    && File.Exists(Path.Combine(current, "Prefs.xml"))) { SafePath(current); return current; }
            throw new InvalidDataException("The installation's Prefs/Prefs.xml could not be found. Start the game once and choose its Prefs folder.");
        }
        internal static string CurrentSkin(string prefsRoot)
        {
            var document = ReadPrefs(ResolvePrefsRoot(prefsRoot));
            return SkinValue(document);
        }
        internal static string[] InstalledSkins(string prefsRoot)
        {
            string root = SkinRoot(prefsRoot); var result = new List<string> { "Default" };
            if (!System.IO.Directory.Exists(root)) return result.ToArray();
            SafePath(root);
            foreach (string directory in System.IO.Directory.EnumerateDirectories(root).Take(200))
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
                string name = Path.GetFileName(directory);
                if (ValidName(name) && File.Exists(Path.Combine(directory, "Graphics.uvga")) && File.Exists(Path.Combine(directory, "Graphics.uvgi"))) result.Add(name);
            }
            return result.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        internal static async Task<GuiSkinInstallResult> InstallNdGuiAsync(string prefsRoot, string themeId, bool newEngine, CancellationToken cancellation)
        {
            var theme = Catalog.FirstOrDefault(item => item.Id == themeId) ?? throw new ArgumentException("Unknown skin theme.");
            string root = SkinRoot(prefsRoot); SafePath(root);
            var packages = new List<byte[]>();
            packages.Add(await Download("1kCXEo8I6yi9EkW7PsUeHVC7QBk_Dbstp", "df750e710ee55b6044e4944cd7a2c35bf60e0025001968e53b54cb6c277fa2f8", cancellation).ConfigureAwait(false));
            packages.Add(await Download(theme.DownloadId, theme.Sha256, cancellation).ConfigureAwait(false));
            if (newEngine) packages.Add(await Download("1R9SbGUipBzX1jugxfNa_zH-6PG7fmKJ2", "90f55fafdf191347efadc59d3eb838d200bf641688d7992b9ced745c9e09861b", cancellation).ConfigureAwait(false));
            cancellation.ThrowIfCancellationRequested();
            return InstallPackages(prefsRoot, "ndGUI_" + theme.Id + (newEngine ? "_DX9" : "_Classic"), packages, true, AuthorUrl);
        }
        internal static GuiSkinInstallResult ImportZip(string prefsRoot, string zipPath)
        {
            SafePath(zipPath);
            if (!File.Exists(zipPath) || new FileInfo(zipPath).Length > MaxArchiveBytes) throw new InvalidDataException("Skin ZIP is missing or exceeds 8 MB.");
            return InstallPackages(prefsRoot, "Imported", new[] { File.ReadAllBytes(zipPath) }, false, "Imported locally");
        }
        internal static GuiSkinInstallResult CopyInstalledSkin(string sourcePrefsRoot, string skinName, string targetPrefsRoot)
        {
            sourcePrefsRoot = ResolvePrefsRoot(sourcePrefsRoot); targetPrefsRoot = ResolvePrefsRoot(targetPrefsRoot);
            if (!ValidName(skinName) || !InstalledSkins(sourcePrefsRoot).Contains(skinName, StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException("Choose an installed source skin.");
            if (string.Equals(skinName, "Default", StringComparison.OrdinalIgnoreCase)) return new GuiSkinInstallResult { SkinName = "Default", SourceUrl = "Built-in AO skin" };
            string source = Child(SkinRoot(sourcePrefsRoot), skinName);
            if (string.Equals(sourcePrefsRoot, targetPrefsRoot, StringComparison.OrdinalIgnoreCase)) return new GuiSkinInstallResult { SkinName = skinName, InstalledPath = source, SourceUrl = "Existing installed skin" };
            using (var stream = new MemoryStream())
            {
                int total = 0, count = 0;
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
                    CopySkinEntries(source, source, zip, ref total, ref count);
                if (stream.Length > MaxArchiveBytes) throw new InvalidDataException("Skin copy exceeds the 8 MB archive limit.");
                return InstallPackages(targetPrefsRoot, "Copied", new[] { stream.ToArray() }, false, "Copied from installed skin " + skinName);
            }
        }
        private static void CopySkinEntries(string root, string directory, ZipArchive zip, ref int total, ref int count)
        {
            SafePath(directory);
            foreach (string file in System.IO.Directory.EnumerateFileSystemEntries(directory))
            {
                if (++count > 1000) throw new InvalidDataException("Too many installed skin entries.");
                SafePath(file);
                if (System.IO.Directory.Exists(file)) { CopySkinEntries(root, file, zip, ref total, ref count); continue; }
                string relative = file.Substring(root.Length + 1).Replace(Path.DirectorySeparatorChar, '/');
                if (relative == "ndGUI_Config_Tool.cmd") continue;
                if (!DataExtensions.Contains(Path.GetExtension(relative))) throw new InvalidDataException("Installed skin contains unsupported files.");
                using (var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (input.Length > MaxFileBytes || input.Length > MaxExpandedBytes - total) throw new InvalidDataException("Installed skin is too large.");
                    total += (int)input.Length;
                    using (var output = zip.CreateEntry("skin/" + relative).Open()) input.CopyTo(output);
                }
            }
        }
        internal static GuiSkinInstallResult InstallPackages(string prefsRoot, string label, IEnumerable<byte[]> packages, bool trustedNdGui, string source)
        {
            string root = SkinRoot(prefsRoot); SafePath(root); System.IO.Directory.CreateDirectory(root);
            string name = "Nexus_" + label;
            if (!ValidName(name)) throw new InvalidDataException("Invalid skin name.");
            string stage = Path.Combine(root, ".nexus-stage-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(stage);
            try
            {
                int total = 0, files = 0;
                foreach (byte[] package in packages)
                {
                    if (package == null || package.Length > MaxArchiveBytes) throw new InvalidDataException("Skin archive exceeds 8 MB.");
                    using (var memory = new MemoryStream(package, false))
                    using (var zip = new ZipArchive(memory, ZipArchiveMode.Read))
                    {
                        if (zip.Entries.Count > 1000) throw new InvalidDataException("Too many skin archive entries.");
                        var names = zip.Entries.Select(entry => ValidateEntry(entry)).ToArray();
                        var fileNames = names.Where((value, index) => !zip.Entries[index].FullName.EndsWith("/")).ToArray();
                        string prefix = fileNames.Length > 0 && fileNames.All(value => value.Contains("/")) ? fileNames[0].Split('/')[0] + "/" : "";
                        if (prefix.Length > 0 && fileNames.Any(value => !value.StartsWith(prefix, StringComparison.Ordinal))) prefix = "";
                        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        for (int i = 0; i < zip.Entries.Count; i++)
                        {
                            var entry = zip.Entries[i]; string relative = names[i];
                            if (entry.FullName.EndsWith("/")) continue;
                            if (prefix.Length > 0) relative = relative.Substring(prefix.Length);
                            if (trustedNdGui && relative == "ndGUI_Config_Tool.cmd") continue; // Replace no scripts; never execute the author's optional config helper.
                            if (!DataExtensions.Contains(Path.GetExtension(relative)) || !seen.Add(relative)) throw new InvalidDataException("Skin ZIP contains unsupported or duplicate files.");
                            if (entry.Length > MaxFileBytes || entry.Length < 0 || entry.Length > MaxExpandedBytes - total || ++files > 1000) throw new InvalidDataException("Skin package is too large.");
                            string destination = Child(stage, relative.Replace('/', Path.DirectorySeparatorChar));
                            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(destination));
                            using (var input = entry.Open())
                            using (var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
                            {
                                byte[] buffer = new byte[8192]; int read, size = 0;
                                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                                { size += read; total += read; if (size > MaxFileBytes || total > MaxExpandedBytes) throw new InvalidDataException("Skin package expands beyond its limit."); output.Write(buffer, 0, read); }
                            }
                        }
                    }
                }
                if (!File.Exists(Path.Combine(stage, "Graphics.uvga")) || !File.Exists(Path.Combine(stage, "Graphics.uvgi"))) throw new InvalidDataException("Import a complete skin containing Graphics.uvga and Graphics.uvgi.");
                string contentHash = SkinContentHash(stage);
                name += "_" + contentHash.Substring(0, 12);
                string target = Child(root, name);
                if (System.IO.Directory.Exists(target))
                {
                    if (SkinContentHash(target) == contentHash) return new GuiSkinInstallResult { SkinName = name, InstalledPath = target, SourceUrl = source };
                    // User-modified installed skins are never overwritten to make room for a download.
                    name += "_" + Guid.NewGuid().ToString("N").Substring(0, 8); target = Child(root, name);
                }
                File.WriteAllText(Path.Combine(stage, "Nexus-source.txt"), "Source: " + source + "\r\nDownloaded/imported: " + DateTime.UtcNow.ToString("O") + "\r\nOriginal authors: Kitao, Misat0 and Honorbound (ndGUI packages).\r\nNo executable configuration helper installed.\r\n", new UTF8Encoding(false));
                SafePath(root); System.IO.Directory.Move(stage, target);
                return new GuiSkinInstallResult { SkinName = name, InstalledPath = target, SourceUrl = source };
            }
            finally { if (System.IO.Directory.Exists(stage)) DeleteStage(root, stage); }
        }
        internal static string SelectSkin(string prefsRoot, string skinName, string backupRoot)
        {
            prefsRoot = ResolvePrefsRoot(prefsRoot);
            if (!ValidName(skinName) || !InstalledSkins(prefsRoot).Contains(skinName, StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException("Choose an installed skin or Default.");
            string path = Path.Combine(prefsRoot, "Prefs.xml"); SafePath(path);
            byte[] before = ReadPrefsBytes(path); var document = ReadXml(before); string oldSkin = SkinValue(document);
            SafePath(backupRoot); System.IO.Directory.CreateDirectory(backupRoot);
            string backup = Path.Combine(Path.GetFullPath(backupRoot), "skin-" + Guid.NewGuid().ToString("N")); System.IO.Directory.CreateDirectory(backup);
            File.WriteAllBytes(Path.Combine(backup, "Prefs.xml"), before);
            var record = new GuiSkinBackup { PrefsRoot = prefsRoot, SkinName = oldSkin, CreatedUtc = DateTime.UtcNow, PrefsSha256 = Hash(before) };
            File.WriteAllText(Path.Combine(backup, "skin-selection.json"), new JavaScriptSerializer().Serialize(record), new UTF8Encoding(false));
            var values = document.SelectNodes("/Root/Value[@name='GUIName']");
            if (values.Count > 1) throw new InvalidDataException("Duplicate GUIName entries in installation preferences.");
            var node = values.Count == 1 ? (XmlElement)values[0] : document.CreateElement("Value");
            if (values.Count == 0) { node.SetAttribute("name", "GUIName"); document.DocumentElement.AppendChild(node); }
            node.SetAttribute("value", "\"" + skinName + "\"");
            string temporary = Child(prefsRoot, ".nexus-skin-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var writer = XmlWriter.Create(temporary, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true })) document.Save(writer);
                SafePath(path);
                if (!before.SequenceEqual(ReadPrefsBytes(path))) throw new IOException("Game preferences changed during the operation. Close the game and retry.");
                File.Replace(temporary, path, null);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            // A failed or interrupted change remains recoverable and is never pruned.
            // Retention is maintenance: a bookkeeping failure must not report that
            // an already committed GUIName change failed.
            try
            {
                record.SelectionCompleted = true;
                File.WriteAllText(Path.Combine(backup, "skin-selection.json"), new JavaScriptSerializer().Serialize(record), new UTF8Encoding(false));
                PruneBackups(prefsRoot, backupRoot, backup);
            }
            catch (Exception error) when (BackupMaintenanceError(error)) { }
            return backup;
        }
        private static bool BackupMaintenanceError(Exception error) => error is IOException || error is InvalidDataException || error is ArgumentException || error is UnauthorizedAccessException || error is InvalidOperationException || error is XmlException;
        private static GuiSkinBackup ReadPrunableBackup(string directory, string prefsRoot)
        {
            string name = Path.GetFileName(directory); Guid id;
            if (name.Length != 37 || !name.StartsWith("skin-", StringComparison.Ordinal) || !Guid.TryParseExact(name.Substring(5), "N", out id) || name.Substring(5) != id.ToString("N")) return null;
            SafePath(directory);
            var entries = System.IO.Directory.EnumerateFileSystemEntries(directory).Take(3).ToArray();
            if (entries.Length != 2 || !entries.Select(Path.GetFileName).OrderBy(entryName => entryName, StringComparer.Ordinal).SequenceEqual(new[] { "Prefs.xml", "skin-selection.json" })) return null;
            foreach (string entry in entries) { SafePath(entry); if (!File.Exists(entry)) return null; }
            var value = ReadBackup(directory);
            return value.SelectionCompleted && string.Equals(value.PrefsRoot, prefsRoot, StringComparison.OrdinalIgnoreCase) ? value : null;
        }
        private static void PruneBackups(string prefsRoot, string backupRoot, string current)
        {
            SafePath(backupRoot);
            var eligible = new List<GuiSkinBackup>();
            foreach (string directory in System.IO.Directory.EnumerateDirectories(backupRoot, "skin-*").Take(1000))
                try { var value = ReadPrunableBackup(directory, prefsRoot); if (value != null && !string.Equals(value.Directory, current, StringComparison.OrdinalIgnoreCase)) eligible.Add(value); }
                catch (Exception error) when (BackupMaintenanceError(error)) { }
            foreach (var old in eligible.OrderByDescending(value => value.CreatedUtc).Skip(9))
                try
                {
                    // Revalidate just before deletion, and never recurse into a folder.
                    var verified = ReadPrunableBackup(old.Directory, prefsRoot);
                    if (verified == null || verified.PrefsSha256 != old.PrefsSha256 || verified.CreatedUtc != old.CreatedUtc) continue;
                    File.Delete(Path.Combine(old.Directory, "Prefs.xml"));
                    File.Delete(Path.Combine(old.Directory, "skin-selection.json"));
                    System.IO.Directory.Delete(old.Directory, false);
                }
                catch (Exception error) when (BackupMaintenanceError(error)) { }
        }
        internal static GuiSkinBackup[] ListBackups(string prefsRoot, string backupRoot)
        {
            prefsRoot = ResolvePrefsRoot(prefsRoot); SafePath(backupRoot);
            if (!System.IO.Directory.Exists(backupRoot)) return new GuiSkinBackup[0];
            var results = new List<GuiSkinBackup>();
            foreach (string directory in System.IO.Directory.EnumerateDirectories(backupRoot, "skin-*").Take(1000))
                try { var value = ReadBackup(directory); if (string.Equals(value.PrefsRoot, prefsRoot, StringComparison.OrdinalIgnoreCase)) results.Add(value); }
                catch (Exception error) when (error is IOException || error is InvalidDataException || error is ArgumentException || error is UnauthorizedAccessException || error is InvalidOperationException || error is XmlException) { }
            return results.OrderByDescending(value => value.CreatedUtc).ToArray();
        }
        internal static void RestoreSelection(string prefsRoot, string backupDirectory)
        {
            prefsRoot = ResolvePrefsRoot(prefsRoot); var backup = ReadBackup(backupDirectory);
            if (!string.Equals(prefsRoot, backup.PrefsRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("This backup belongs to another game installation.");
            SelectSkin(prefsRoot, backup.SkinName, Path.GetDirectoryName(Path.GetFullPath(backupDirectory)));
        }
        private static GuiSkinBackup ReadBackup(string directory)
        {
            SafePath(directory); string path = Path.Combine(directory, "skin-selection.json"); SafePath(path);
            if (!File.Exists(path) || new FileInfo(path).Length > 16384) throw new InvalidDataException("Invalid skin backup.");
            var value = new JavaScriptSerializer().Deserialize<GuiSkinBackup>(File.ReadAllText(path));
            if (value == null || !ValidName(value.SkinName) || string.IsNullOrEmpty(value.PrefsRoot)) throw new InvalidDataException("Invalid skin backup.");
            string snapshot = Path.Combine(directory, "Prefs.xml"); SafePath(snapshot);
            byte[] original = ReadPrefsBytes(snapshot);
            if (Hash(original) != value.PrefsSha256 || SkinValue(ReadXml(original)) != value.SkinName) throw new InvalidDataException("Skin backup integrity check failed.");
            value.Directory = Path.GetFullPath(directory); return value;
        }
        private static string SkinRoot(string prefsRoot) => Path.Combine(Path.GetDirectoryName(ResolvePrefsRoot(prefsRoot)), "Gui");
        private static string SkinContentHash(string root)
        {
            var files = new List<string>(); int entries = 0; CollectSkinFiles(root, files, ref entries);
            long total = 0; var manifest = new StringBuilder();
            foreach (string file in files.OrderBy(value => value, StringComparer.Ordinal))
            {
                string relative = file.Substring(root.Length + 1).Replace(Path.DirectorySeparatorChar, '/');
                if (relative == "Nexus-source.txt") continue;
                using (var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var hash = SHA256.Create())
                {
                    if (input.Length > MaxFileBytes || (total += input.Length) > MaxExpandedBytes) throw new InvalidDataException("Installed skin exceeds its size limit.");
                    manifest.Append(relative).Append(':').Append(BitConverter.ToString(hash.ComputeHash(input))).Append('\n');
                }
            }
            return Hash(Encoding.UTF8.GetBytes(manifest.ToString()));
        }
        private static void CollectSkinFiles(string directory, List<string> files, ref int entries)
        {
            SafePath(directory);
            foreach (string path in System.IO.Directory.EnumerateFileSystemEntries(directory))
            {
                if (++entries > 1000) throw new InvalidDataException("Too many installed skin entries.");
                SafePath(path);
                if (System.IO.Directory.Exists(path)) CollectSkinFiles(path, files, ref entries);
                else { if (files.Count >= 1000) throw new InvalidDataException("Too many skin files."); files.Add(path); }
            }
        }
        private static string Hash(byte[] value) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(value)).Replace("-", "").ToLowerInvariant(); }
        private static XmlDocument ReadPrefs(string prefsRoot) { string path = Path.Combine(prefsRoot, "Prefs.xml"); SafePath(path); return ReadXml(ReadPrefsBytes(path)); }
        private static byte[] ReadPrefsBytes(string path)
        {
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (input.Length > 4 * 1024 * 1024) throw new InvalidDataException("Preferences file is too large.");
                using (var output = new MemoryStream()) { input.CopyTo(output); return output.ToArray(); }
            }
        }
        private static XmlDocument ReadXml(byte[] bytes)
        {
            if (bytes.Length > 4 * 1024 * 1024) throw new InvalidDataException("Preferences file is too large.");
            var result = new XmlDocument { XmlResolver = null, PreserveWhitespace = true };
            using (var stream = new MemoryStream(bytes, false))
            using (var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4 * 1024 * 1024 })) result.Load(reader);
            if (result.DocumentElement?.Name != "Root") throw new InvalidDataException("Unrecognized preferences file."); return result;
        }
        private static string SkinValue(XmlDocument document)
        {
            string value = (document.SelectSingleNode("/Root/Value[@name='GUIName']") as XmlElement)?.GetAttribute("value").Trim('"');
            if (string.IsNullOrWhiteSpace(value)) return "Default";
            if (!ValidName(value)) throw new InvalidDataException("Invalid current skin name."); return value;
        }
        private static bool ValidName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 100 || name == "." || name == ".." || name != name.Trim().TrimEnd('.')
                || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/') || name.Contains('\\')) return false;
            string stem = name.Split('.')[0].ToUpperInvariant();
            return stem != "CON" && stem != "PRN" && stem != "AUX" && stem != "NUL"
                && !(stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && stem[3] >= '0' && stem[3] <= '9');
        }
        private static string ValidateEntry(ZipArchiveEntry entry)
        {
            string name = entry.FullName;
            if (name.Length == 0 || name.Length > 240 || name.Contains('\\') || name.StartsWith("/") || name.Contains(':')
                || (entry.ExternalAttributes & 0x400) != 0 || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new InvalidDataException("Unsafe skin archive path.");
            foreach (string part in name.TrimEnd('/').Split('/')) if (!ValidName(part)) throw new InvalidDataException("Unsafe skin archive path.");
            return name;
        }
        private static string Child(string root, string relative)
        {
            string full = Path.GetFullPath(Path.Combine(root, relative));
            if (!full.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Path leaves the selected folder.");
            SafePath(full); return full;
        }
        private static void SafePath(string path)
        {
            string current = Path.GetFullPath(path);
            while (!string.IsNullOrEmpty(current))
            {
                if ((File.Exists(current) || System.IO.Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked folders are not supported for skin operations.");
                current = Path.GetDirectoryName(current);
            }
        }
        private static void DeleteStage(string root, string stage)
        {
            stage = Child(root, Path.GetFileName(stage));
            if (!Path.GetFileName(stage).StartsWith(".nexus-stage-", StringComparison.Ordinal)) throw new IOException("Invalid staging path.");
            // Enumerate each level only after rejecting reparse points, never follow a link.
            DeleteContents(stage); System.IO.Directory.Delete(stage);
        }
        private static void DeleteContents(string directory)
        {
            SafePath(directory);
            foreach (string path in System.IO.Directory.EnumerateFileSystemEntries(directory))
            { SafePath(path); if (System.IO.Directory.Exists(path)) { DeleteContents(path); System.IO.Directory.Delete(path); } else File.Delete(path); }
        }
        private static async Task<byte[]> Download(string id, string expectedHash, CancellationToken cancellation)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            using (var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseDefaultCredentials = false }))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(60));
                var uri = new Uri("https://drive.google.com/uc?export=download&id=" + id);
                for (int redirect = 0; redirect < 5; redirect++)
                {
                    if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || (uri.Host != "drive.google.com" && uri.Host != "drive.usercontent.google.com")) throw new InvalidDataException("Unexpected skin download host.");
                    using (var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                    {
                        if ((int)response.StatusCode >= 300 && (int)response.StatusCode <= 399)
                        { if (response.Headers.Location == null) throw new InvalidDataException("Missing download redirect."); uri = new Uri(uri, response.Headers.Location); continue; }
                        response.EnsureSuccessStatusCode();
                        if (response.Content.Headers.ContentLength > MaxArchiveBytes) throw new InvalidDataException("Skin download is too large.");
                        using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var output = new MemoryStream())
                        {
                            byte[] buffer = new byte[8192]; int read;
                            while ((read = await input.ReadAsync(buffer, 0, buffer.Length, timeout.Token).ConfigureAwait(false)) > 0)
                            { if (output.Length + read > MaxArchiveBytes) throw new InvalidDataException("Skin download is too large."); output.Write(buffer, 0, read); }
                            byte[] result = output.ToArray(); using (var sha = SHA256.Create())
                                if (BitConverter.ToString(sha.ComputeHash(result)).Replace("-", "").ToLowerInvariant() != expectedHash) throw new InvalidDataException("The skin download differs from the reviewed package. No skin was installed.");
                            return result;
                        }
                    }
                }
            }
            throw new InvalidDataException("Too many skin download redirects.");
        }
    }
}
