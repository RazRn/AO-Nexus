using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Xml;
using AOLauncher;

internal static class GuiSkinsTests
{
    private static int checks;
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    private static void Reject(Action action, string message)
    {
        bool rejected = false;
        try { action(); } catch (Exception error) when (error is IOException || error is InvalidDataException || error is ArgumentException || error is XmlException) { rejected = true; }
        Assert(rejected, message);
    }
    private static byte[] Zip(params string[] names)
    {
        using (var stream = new MemoryStream())
        {
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
                foreach (string name in names)
                {
                    var entry = zip.CreateEntry(name);
                    using (var writer = new StreamWriter(entry.Open())) writer.Write(name.EndsWith(".xml") ? "<Root/>" : "fixture data");
                }
            return stream.ToArray();
        }
    }
    private static GuiSkinInstallResult Install(string prefs, byte[] zip, bool trusted = false) => GuiSkins.InstallPackages(prefs, "Fixture", new[] { zip }, trusted, "offline fixture");
    private static string NewPrefs(string root)
    {
        string prefs = Path.Combine(root, "Prefs"); Directory.CreateDirectory(Path.Combine(prefs, "example-account", "Char123"));
        File.WriteAllText(Path.Combine(prefs, "Prefs.xml"), "<Root><Value name=\"GUIName\" value='&quot;Default&quot;'/><Value name=\"DisplayWidth\" value=\"1920\"/><Archive name=\"Other\"><Value name=\"Keep\" value=\"true\"/></Archive></Root>");
        return prefs;
    }
    private static void VerifyRetention(string root)
    {
        string prefs = NewPrefs(Path.Combine(root, "retention-client")), other = NewPrefs(Path.Combine(root, "retention-other"));
        string backups = Path.Combine(root, "retention-backups");
        string first = GuiSkins.SelectSkin(prefs, "Default", backups);
        var serializer = new JavaScriptSerializer();
        string firstMetadata = Path.Combine(first, "skin-selection.json");
        var old = serializer.Deserialize<GuiSkinBackup>(File.ReadAllText(firstMetadata)); old.CreatedUtc = DateTime.UtcNow.AddDays(-1);
        File.WriteAllText(firstMetadata, serializer.Serialize(old));
        var preserved = new List<string>();
        foreach (string kind in new[] { "corrupt", "extra-file", "extra-directory", "pending", "unknown" })
        {
            string directory = Path.Combine(backups, kind == "unknown" ? "skin-unrecognized" : "skin-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            File.Copy(Path.Combine(first, "Prefs.xml"), Path.Combine(directory, "Prefs.xml"));
            File.Copy(firstMetadata, Path.Combine(directory, "skin-selection.json"));
            if (kind == "corrupt") File.AppendAllText(Path.Combine(directory, "Prefs.xml"), "tampered");
            if (kind == "extra-file") File.WriteAllText(Path.Combine(directory, "keep.txt"), "user data");
            if (kind == "extra-directory") Directory.CreateDirectory(Path.Combine(directory, "keep"));
            if (kind == "pending") { old.SelectionCompleted = false; File.WriteAllText(Path.Combine(directory, "skin-selection.json"), serializer.Serialize(old)); old.SelectionCompleted = true; }
            preserved.Add(directory);
        }
        string foreign = GuiSkins.SelectSkin(other, "Default", backups);
        byte[] foreignMetadata = File.ReadAllBytes(Path.Combine(foreign, "skin-selection.json"));
        string current = null;
        for (int i = 0; i < 15; i++) current = GuiSkins.SelectSkin(prefs, "Default", backups);
        var known = Directory.GetDirectories(backups).Except(preserved).Where(path => path != foreign).ToArray();
        Assert(known.Length == 10, "Retain ten successful selection backups per Prefs root");
        Assert(!Directory.Exists(first) && Directory.Exists(current), "Prune oldest selection and retain latest undo record");
        Assert(preserved.All(Directory.Exists) && preserved.All(path => File.Exists(Path.Combine(path, "Prefs.xml")) && File.Exists(Path.Combine(path, "skin-selection.json"))), "Preserve corrupt, extra-content, pending and unknown backup folders");
        Assert(File.ReadAllBytes(Path.Combine(foreign, "skin-selection.json")).SequenceEqual(foreignMetadata) && GuiSkins.ListBackups(other, backups).Length == 1, "Pruning leaves another installation's backups untouched");
        Assert(File.Exists(Path.Combine(preserved[1], "keep.txt")) && Directory.Exists(Path.Combine(preserved[2], "keep")), "Pruning never deletes unrelated files or directories");
        File.WriteAllText(Path.Combine(prefs, "Prefs.xml"), "<Root><Value name=\"GUIName\" value='&quot;Default&quot;'/><Value name=\"GUIName\" value='&quot;Default&quot;'/></Root>");
        Reject(() => GuiSkins.SelectSkin(prefs, "Default", backups), "Failed selection is not committed");
        Assert(known.All(Directory.Exists), "Failed selection does not prune existing successful backups");
    }
    public static int Main(string[] args)
    {
        string root = Path.Combine(Path.GetTempPath(), "AONexus-SkinTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            VerifyRetention(root);
            string prefs = NewPrefs(Path.Combine(root, "client")), backups = Path.Combine(root, "backups");
            Assert(GuiSkins.ResolvePrefsRoot(Path.Combine(prefs, "example-account", "Char123")) == prefs, "Resolve owning installation from character path");
            Assert(GuiSkins.CurrentSkin(prefs) == "Default", "Decode quoted Default selector");
            Assert(GuiSkins.InstalledSkins(prefs).SequenceEqual(new[] { "Default" }), "Built-in default works without a custom skin folder");
            Assert(GuiSkins.Catalog.Count == 7 && GuiSkins.Catalog.All(item => item.Sha256.Length == 64 && item.SourceUrl.StartsWith("https://sites.google.com/")), "Seven attributed, pinned author themes");
            var skin = Install(prefs, Zip("skin/Graphics.uvga", "skin/Graphics.uvgi", "skin/Views/ControlCenter.xml"));
            Assert(skin.InstalledPath.StartsWith(Path.Combine(Path.GetDirectoryName(prefs), "Gui") + Path.DirectorySeparatorChar), "Uses local data Gui, not game install Default");
            Assert(File.Exists(Path.Combine(skin.InstalledPath, "Graphics.uvga")) && File.Exists(Path.Combine(skin.InstalledPath, "Nexus-source.txt")), "Data and provenance installed");
            Assert(GuiSkins.CurrentSkin(prefs) == "Default", "Installing alone does not activate");
            Assert(GuiSkins.InstalledSkins(prefs).Contains(skin.SkinName), "Imported skin listed");
            Assert(Install(prefs, Zip("skin/Graphics.uvga", "skin/Graphics.uvgi", "skin/Views/ControlCenter.xml")).SkinName == skin.SkinName, "Repeated identical skin import reuses verified contents");
            string backup = GuiSkins.SelectSkin(prefs, skin.SkinName, backups);
            Assert(GuiSkins.CurrentSkin(prefs) == skin.SkinName, "Selected skin activates through GUIName");
            string text = File.ReadAllText(Path.Combine(prefs, "Prefs.xml"));
            Assert(text.Contains("1920") && text.Contains("Other") && text.Contains("Keep"), "Activation preserves other settings");
            File.WriteAllText(Path.Combine(prefs, "Prefs.xml"), text.Replace("1920", "2560"));
            GuiSkins.RestoreSelection(prefs, backup);
            Assert(GuiSkins.CurrentSkin(prefs) == "Default", "Restore reselects original skin");
            Assert(File.ReadAllText(Path.Combine(prefs, "Prefs.xml")).Contains("2560"), "Restore preserves settings changed since backup");
            Assert(GuiSkins.ListBackups(prefs, backups).Length == 2, "Apply and restore both create undo backups");
            string other = NewPrefs(Path.Combine(root, "other"));
            var copied = GuiSkins.CopyInstalledSkin(prefs, skin.SkinName, other);
            Assert(File.ReadAllBytes(Path.Combine(copied.InstalledPath, "Graphics.uvga")).SequenceEqual(File.ReadAllBytes(Path.Combine(skin.InstalledPath, "Graphics.uvga"))), "Cross-installation copy preserves skin texture bytes");
            Assert(GuiSkins.CurrentSkin(other) == "Default" && copied.SkinName != skin.SkinName, "Skin copy has unique destination and does not activate");
            Assert(GuiSkins.CopyInstalledSkin(prefs, skin.SkinName, prefs).SkinName == skin.SkinName, "Same installation copy reuses its existing skin");
            Assert(GuiSkins.CopyInstalledSkin(prefs, "Default", other).SkinName == "Default", "Built-in Default needs no copied files");
            Assert(GuiSkins.CopyInstalledSkin(prefs, skin.SkinName, other).SkinName == copied.SkinName, "Repeated rollout copy reuses destination skin");
            Reject(() => GuiSkins.RestoreSelection(other, backup), "Cross-installation restore rejected");
            Assert(GuiSkins.ListBackups(other, backups).Length == 0, "Backup list is installation scoped");
            string savedSnapshot = File.ReadAllText(Path.Combine(backup, "Prefs.xml"));
            File.WriteAllText(Path.Combine(backup, "Prefs.xml"), savedSnapshot.Replace("1920", "9999"));
            Reject(() => GuiSkins.RestoreSelection(prefs, backup), "Corrupted backup cannot be restored");
            File.WriteAllText(Path.Combine(backup, "Prefs.xml"), savedSnapshot);
            foreach (string name in new[] { "skin/../escape.xml", "/absolute.xml", "C:/escape.xml", "skin/file.xml:stream", "skin\\evil.xml", "skin/CON.xml", "skin/trailing .xml/../evil.xml", "skin/bad./evil.xml" })
                Reject(() => Install(prefs, Zip("skin/Graphics.uvga", "skin/Graphics.uvgi", name)), "Reject unsafe entry " + name);
            Reject(() => Install(prefs, Zip("skin/Graphics.uvga", "skin/Graphics.uvgi", "skin/Run.exe")), "Reject executable import");
            Reject(() => Install(prefs, Zip("skin/Graphics.uvga", "skin/Graphics.uvgi", "skin/Run.cmd")), "Reject script import");
            Reject(() => Install(prefs, Zip("skin/Graphics.uvga", "skin/Graphics.uvgi", "skin/Colors.xml", "skin/colors.xml")), "Reject Windows case collisions");
            Reject(() => Install(prefs, Zip("skin/Views/ControlCenter.xml")), "Require complete graphic package");
            byte[] linked = Zip("skin/Graphics.uvga", "skin/Graphics.uvgi", "skin/link.xml");
            using (var memory = new MemoryStream())
            {
                memory.Write(linked, 0, linked.Length); memory.Position = 0;
                using (var zip = new ZipArchive(memory, ZipArchiveMode.Update, true)) zip.GetEntry("skin/link.xml").ExternalAttributes = unchecked((int)0xA1FF0000);
                Reject(() => Install(prefs, memory.ToArray()), "Reject archive symlinks");
            }
            var trusted = Install(prefs, Zip("ndGUI/Graphics.uvga", "ndGUI/Graphics.uvgi", "ndGUI/ndGUI_Config_Tool.cmd"), true);
            Assert(!File.Exists(Path.Combine(trusted.InstalledPath, "ndGUI_Config_Tool.cmd")), "Known author helper is omitted, not executed");
            Assert(!Directory.EnumerateDirectories(Path.Combine(Path.GetDirectoryName(prefs), "Gui"), ".nexus-stage-*").Any(), "Failed/successful imports leave no staging garbage");
            Reject(() => GuiSkins.SelectSkin(prefs, "../escape", backups), "Selector cannot leave skin folder");
            File.WriteAllText(Path.Combine(other, "Prefs.xml"), "<!DOCTYPE Root [<!ENTITY x SYSTEM 'file:///missing'>]><Root>&x;</Root>");
            Reject(() => GuiSkins.CurrentSkin(other), "XML DTD is prohibited");
            if (args.Length > 0)
            {
                var core = File.ReadAllBytes(Path.Combine(args[0], "ndGUI_4.3.4.8_Core.zip"));
                var theme = File.ReadAllBytes(Path.Combine(args[0], "ndGUI_4.3.4.5_Theme_Dovve.zip"));
                var patch = File.ReadAllBytes(Path.Combine(args[0], "ndGUI_4.3.4.8_New_Engine_Patch.zip"));
                var real = GuiSkins.InstallPackages(prefs, "ReviewedOffline", new[] { core, theme, patch }, true, GuiSkins.AuthorUrl);
                Assert(File.ReadAllText(Path.Combine(real.InstalledPath, "ndGUI_Core_version_info.txt")).Contains("4.3.4.8"), "Reviewed core/theme/DX9 packages overlay correctly");
                Assert(!File.Exists(Path.Combine(real.InstalledPath, "ndGUI_Config_Tool.cmd")), "Author archive helper absent in real install fixture");
            }
            if (args.Length > 1 && args[1] == "--download")
            {
                var downloaded = GuiSkins.InstallNdGuiAsync(prefs, "dovvetech", true, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                Assert(File.Exists(Path.Combine(downloaded.InstalledPath, "Graphics.uvga")), "Explicit download follows reviewed hosts and hashes");
                Assert(GuiSkins.CurrentSkin(prefs) == "Default", "Downloaded package does not select itself");
            }
            Console.WriteLine("PASS: " + checks + " GUI skin archive, selection, restoration and isolation checks. Temporary fixtures only."); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            string resolved = Path.GetFullPath(root);
            if (resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar + "AONexus-SkinTests-", StringComparison.OrdinalIgnoreCase)) Directory.Delete(resolved, true);
        }
    }
}
