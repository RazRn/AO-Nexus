using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AOLauncher;

internal static class AovkGraphicsTests
{
    private static int checks;
    private static string lastCheck;
    private static bool skipLegacyUi15;
    private static bool clientCompatibilityOnly;
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; lastCheck = message; }
    private static void Reject(Action action, string message)
    {
        bool rejected = false;
        try { action(); } catch (IOException) { rejected = true; }
        Check(rejected, message);
    }
    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);
    private static void Main(string[] args)
    {
        skipLegacyUi15 = args.Contains("--skip-legacy-ui15");
        clientCompatibilityOnly = args.Contains("--client-compatibility-only");
        bool installed081Only = args.Contains("--installed-081-only");
        args = args.Where(arg => arg != "--skip-legacy-ui15" && arg != "--client-compatibility-only" && arg != "--installed-081-only").ToArray();
        string temporary = Path.Combine(Path.GetTempPath(), "NexusGraphicsTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string a = Path.Combine(temporary, "a"), b = Path.Combine(temporary, "b");
            File.WriteAllText(a, "original A"); File.WriteAllText(b, "original B");
            var writes = new[] {
                new AovkGraphics.Write { Path = a, Before = Bytes("original A"), After = Bytes("replacement A") },
                new AovkGraphics.Write { Path = b, Before = Bytes("original B"), After = Bytes("replacement B") }
            };
            Reject(() => AovkGraphics.Commit(writes, index => { if (index == 1) throw new IOException("injected failure"); }), "fault is reported");
            Check(File.ReadAllText(a) == "original A" && File.ReadAllText(b) == "original B", "both files roll back after second write");
            using (var locked = new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.None))
                Reject(() => AovkGraphics.Commit(writes), "locked second destination prevents all writes");
            Check(File.ReadAllText(a) == "original A", "first destination unchanged after lock rejection");
            File.WriteAllText(b, "external B");
            Reject(() => AovkGraphics.Commit(writes), "external change rejected");
            Check(File.ReadAllText(a) == "original A" && File.ReadAllText(b) == "external B", "external file preserved");
            File.WriteAllText(b, "original B");
            AovkGraphics.Commit(writes);
            Check(File.ReadAllText(a) == "replacement A" && File.ReadAllText(b) == "replacement B", "transaction completes");
            Reject(() => AovkGraphics.ReadRenderer("[Renderer]\nRVK_Dof=0\nrvk_dof=1"), "ambiguous duplicate setting rejected");

            if (args.Length == 1 && args[0] == "--transaction-only") {
                Console.WriteLine("PASS: " + checks + " graphics transaction checks; synthetic temporary files only.");
                return;
            }
            if (args.Length < 1) throw new ArgumentException("Supply the prepared test installation as a read-only fixture.");
            if (installed081Only) {
                Installed081Tests(temporary, args[0]);
                Console.WriteLine("PASS: " + checks + " transaction and installed AO-VK 0.8.1 checks; source installation read-only.");
                return;
            }
            FreshInstallTests(temporary, args[0], args.Length > 1 ? args[1] : null);
            if (clientCompatibilityOnly) {
                Console.WriteLine("PASS: " + checks + " transaction and client fingerprint checks; renderer installation not tested.");
                return;
            }
            string game = Path.Combine(temporary, "game"); Directory.CreateDirectory(game);
            foreach (string name in new[] { "AnarchyOnline.exe", "GUI.dll", "Interfaces.dll", "N3.dll", "randy31_orig.dll", "randy31-aovk-test.dll",
                "version-stock-rebirth.dll", "version-aovk-test.dll", "randy31.dll", "version.dll", "randy-vk.ini", "NEXUS-AOVK-TEST.txt" })
                File.Copy(Path.Combine(args[0], name), Path.Combine(game, name));
            var initial = AovkGraphics.Read(game);
            Check(!initial.Changed, "read is not dirty");
            var ini = File.ReadAllBytes(Path.Combine(game, "randy-vk.ini"));
            initial.Enabled = false;
            AovkGraphics.Apply(new[] { initial });
            var stock = AovkGraphics.Read(game);
            Check(!stock.Enabled, "off selects stock renderer and patch");
            Check(ini.SequenceEqual(stock.Ini), "off alone preserves exact preferences");
            stock.Values["RVK_PixelLight"] = "0"; stock.Values["RVK_Aniso"] = "4";
            stock.Enabled = true;
            AovkGraphics.Apply(new[] { stock });
            var vk = AovkGraphics.Read(game);
            Check(vk.Enabled, "on selects AO-VK renderer and patch");
            Check(vk.Values["RVK_PixelLight"] == "0" && vk.Values["RVK_Aniso"] == "4", "individual choices persist");
            var all = AovkGraphics.ReadRenderer(Encoding.UTF8.GetString(vk.Ini));
            Check(AovkGraphics.Unavailable.All(key => all[key] == "0"), "unsupported effects remain disabled");
            Check(all["RVK_GrassOn"] == AovkGraphics.ReadRenderer(Encoding.UTF8.GetString(ini))["RVK_GrassOn"], "ordinary edit preserves environment trial");
            Check(Encoding.UTF8.GetString(vk.Ini).Contains("Visuals=off"), "native compatibility profile preserved");
            vk.Enabled = false; AovkGraphics.Apply(new[] { vk });
            var roundTrip = AovkGraphics.Read(game); roundTrip.Enabled = true; AovkGraphics.Apply(new[] { roundTrip });
            var reenabled = AovkGraphics.Read(game);
            Check(reenabled.Values["RVK_PixelLight"] == "0" && reenabled.Values["RVK_Aniso"] == "4", "off/on remembers graphics");
            reenabled.Preset = true; AovkGraphics.Apply(new[] { reenabled });
            var preset = AovkGraphics.Read(game);
            all = AovkGraphics.ReadRenderer(Encoding.UTF8.GetString(preset.Ini));
            Check(new[] { "RVK_GrassOn", "RVK_LeafOn", "RVK_WaterOn", "RVK_Particles", "RVK_NormalMaps", "RVK_Pbr", "RVK_Albedo" }.All(key => all[key] == "0"), "preset disables unverified effects");
            Check(Directory.GetDirectories(Path.Combine(game, "NexusGraphicsBackups")).Length >= 5, "recovery backups retained");
            var originalPair = new[] { File.ReadAllBytes(Path.Combine(game, "randy31.dll")), File.ReadAllBytes(Path.Combine(game, "version.dll")) };
            preset.Enabled = false;
            File.AppendAllText(Path.Combine(game, "randy-vk.ini"), "\r\n; external edit\r\n");
            Reject(() => AovkGraphics.Apply(new[] { preset }), "stale settings view rejected");
            Check(originalPair[0].SequenceEqual(File.ReadAllBytes(Path.Combine(game, "randy31.dll"))) && originalPair[1].SequenceEqual(File.ReadAllBytes(Path.Combine(game, "version.dll"))), "stale settings leaves pair untouched");
            var latest = AovkGraphics.Read(game); latest.Enabled = false;
            using (var locked = new FileStream(Path.Combine(game, "randy31.dll"), FileMode.Open, FileAccess.Read, FileShare.Read))
                Reject(() => AovkGraphics.Apply(new[] { latest }), "loaded-style renderer lock prevents switching");
            Check(originalPair[1].SequenceEqual(File.ReadAllBytes(Path.Combine(game, "version.dll"))), "patch unchanged after renderer lock");
            File.WriteAllText(Path.Combine(game, "randy31-aovk-test.dll"), "unknown renderer");
            Reject(() => AovkGraphics.Apply(new[] { latest }), "unknown payload rejected even when switching off");
            Check(originalPair[0].SequenceEqual(File.ReadAllBytes(Path.Combine(game, "randy31.dll"))), "unknown payload cannot alter active renderer");
            Console.WriteLine("PASS: " + checks + " graphics checks; temporary files only, no game launched.");
            if (skipLegacyUi15) Console.WriteLine("NOT TESTED: legacy UI 15 patch fixture (Defender quarantine); no protection bypassed.");
        }
        catch {
            Console.Error.WriteLine("Failed after check " + checks + ": " + lastCheck);
            throw;
        }
        finally
        {
            string root = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string resolved = Path.GetFullPath(temporary);
            if (!resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("NexusGraphicsTests-", StringComparison.Ordinal))
                throw new IOException("Unexpected cleanup path.");
            Directory.Delete(resolved, true);
        }
    }

    static void Installed081Tests(string root, string source)
    {
        string game = Path.Combine(root, "installed081"); Directory.CreateDirectory(game);
        foreach (string name in new[] { "AnarchyOnline.exe", "GUI.dll", "Interfaces.dll", "N3.dll",
            "randy31.dll", "version.dll", "randy31_orig.dll", "version-stock-rebirth.dll", "randy-vk.ini" })
            File.Copy(Path.Combine(source, name), Path.Combine(game, name));
        var initial = AovkGraphics.Read(game);
        Check(initial.Enabled && initial.Rebirth && !initial.Changed && !initial.UpgradeAvailable,
            "installed 0.8.1 is recognized without an implicit upgrade");
        Check(AovkGraphics.Hash(initial.Renderer) == AovkGraphics.Release081Renderer &&
            AovkGraphics.Hash(initial.Patch) == AovkGraphics.Release081Patch, "exact 0.8.1 pair");
        Check(!AovkGraphics.NeedsRendererDownload(initial), "installed 0.8.1 needs no old payload download");
        initial.Values["RVK_PerfHud"] = initial.Values["RVK_PerfHud"] == "1" ? "0" : "1";
        AovkGraphics.Apply(new[] { initial });
        var changed = AovkGraphics.Read(game);
        Check(changed.Renderer.SequenceEqual(initial.Renderer) && changed.Patch.SequenceEqual(initial.Patch),
            "settings preserve official renderer and matching repair exactly");
        Check(changed.Values["RVK_PerfHud"] == initial.Values["RVK_PerfHud"], "settings change persisted");
        changed.Values["RVK_NexusUI"] = "1";
        Reject(() => AovkGraphics.Apply(new[] { changed }), "unsupported UI renderer switch rejected");
        Check(!AovkGraphics.HasPending(game) && File.ReadAllBytes(Path.Combine(game, "randy31.dll")).SequenceEqual(initial.Renderer),
            "rejected switch leaves renderer and recovery state unchanged");
        File.Copy(Path.Combine(game, "version-stock-rebirth.dll"), Path.Combine(game, "version.dll"), true);
        Reject(() => AovkGraphics.Read(game), "0.8.1 with stock repair is rejected");
        File.WriteAllBytes(Path.Combine(game, "version.dll"), initial.Patch);
        byte[] corrupt = (byte[])initial.Renderer.Clone(); corrupt[100] ^= 1;
        File.WriteAllBytes(Path.Combine(game, "randy31.dll"), corrupt);
        Reject(() => AovkGraphics.Read(game), "modified renderer rejected");
        File.WriteAllBytes(Path.Combine(game, "randy31.dll"), initial.Renderer);
        var disable = AovkGraphics.Read(game); disable.Enabled = false;
        AovkGraphics.Apply(new[] { disable });
        var stock = AovkGraphics.Read(game);
        Check(!stock.Enabled && AovkGraphics.Hash(stock.Renderer) == AovkGraphics.StockRenderer &&
            AovkGraphics.Hash(stock.Patch) == AovkGraphics.StockPatch, "disable restores matching stock pair");
    }

    static string Fresh(string root, string source, bool rebirth, bool prk = false)
    {
        string folder = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        foreach (string name in new[] { "AnarchyOnline.exe", "GUI.dll", "Interfaces.dll", "N3.dll" }) File.Copy(Path.Combine(source, name), Path.Combine(folder, name));
        File.Copy(Path.Combine(source, prk ? "randy31.dll" : "randy31_orig.dll"), Path.Combine(folder, "randy31.dll"));
        if (rebirth) File.Copy(Path.Combine(source, "version-stock-rebirth.dll"), Path.Combine(folder, "version.dll"));
        return folder;
    }

    static void FreshInstallTests(string temporary, string source, string prkSource)
    {
        foreach (string kind in prkSource == null ? new[] { "Rebirth", "Official" } : new[] { "Rebirth", "Official", "PRK" }) {
            bool rebirth = kind == "Rebirth", prk = kind == "PRK";
            string game = Fresh(temporary, prk ? prkSource : source, rebirth, prk);
            var original = File.ReadAllBytes(Path.Combine(game, "randy31.dll"));
            var fresh = AovkGraphics.Read(game);
            Check(!fresh.Enabled && !fresh.Prepared && !fresh.Changed && fresh.Ini == null, kind + " fresh folder is eligible without a marker or INI");
            if (!prk) {
                string clientPath = Path.Combine(game, "AnarchyOnline.exe");
                byte[] client = File.ReadAllBytes(clientPath), large = (byte[])client.Clone();
                large[BitConverter.ToInt32(large, 0x3c) + 22] |= 0x20;
                File.WriteAllBytes(clientPath, large);
                var largeProfile = AovkGraphics.Read(game);
                Check(largeProfile.ProfileName == fresh.ProfileName && largeProfile.Rebirth == fresh.Rebirth,
                    kind + " LAA preserves graphics profile");
                Check(AovkGraphics.Hash(File.ReadAllBytes(clientPath)) == AovkGraphics.LargeAddressClient,
                    kind + " reading graphics preserves LAA");
                large[100] ^= 1; File.WriteAllBytes(clientPath, large);
                Reject(() => AovkGraphics.Read(game), kind + " LAA does not accept other executable changes");
                File.WriteAllBytes(clientPath, client);
            }
            if (clientCompatibilityOnly) continue;
            int initialCount = Directory.GetFiles(game).Length;
            AovkGraphics.Apply(new[] { fresh });
            Check(Directory.GetFiles(game).Length == initialCount, kind + " merely opening/cancelling Settings writes nothing");
            fresh.Enabled = true;
            Reject(() => AovkGraphics.Apply(new[] { fresh }, index => { if (index == 1) throw new IOException("first activation failure"); }), kind + " first-install fault is reported");
            Check(original.SequenceEqual(File.ReadAllBytes(Path.Combine(game, "randy31.dll"))) && !File.Exists(Path.Combine(game, "randy31_orig.dll")) && !File.Exists(Path.Combine(game, "randy-vk.ini")), kind + " rollback removes newly created files and preserves stock");
            Check(!AovkGraphics.HasPending(game), kind + " complete rollback clears recovery marker");
            AovkGraphics.Apply(new[] { fresh });
            var enabled = AovkGraphics.Read(game);
            Check(enabled.Enabled && enabled.Prepared && original.SequenceEqual(File.ReadAllBytes(Path.Combine(game, "randy31_orig.dll"))), kind + " auto-installs with verified original backup");
            Check(!enabled.ModernUi, kind + " modern UI is an explicit opt-in");
            enabled.Values["RVK_NexusUI"] = "1";
            Reject(() => AovkGraphics.Apply(new[] { enabled }, index => { if (index == 1) throw new IOException("UI upgrade failure"); }), kind + " failed UI upgrade reports rollback");
            Check(AovkGraphics.Hash(File.ReadAllBytes(Path.Combine(game, "randy31.dll"))) == AovkGraphics.VkRenderer, kind + " failed upgrade retains accepted renderer");
            AovkGraphics.Apply(new[] { enabled });
            var modern = AovkGraphics.Read(game);
            Check(modern.Enabled && modern.ModernUi && AovkGraphics.Hash(modern.Renderer) == AovkGraphics.UiRenderer, kind + " UI opt-in installs verified preview");
            if (rebirth) Check(AovkGraphics.Hash(modern.Patch) == AovkGraphics.UiPatch, "UI renderer receives matching Rebirth repair");
            string payloads = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../runtime/AOVKPackage"));
            foreach (string revision in skipLegacyUi15 ? new[] { "11", "13", "14" } : new[] { "11", "13", "14", "15" }) {
            File.Copy(Path.Combine(payloads, "randy31-ui" + revision + ".dll"), Path.Combine(game, "randy31.dll"), true);
            if (rebirth) {
                if (revision == "13") Check(AovkGraphics.Read(game).Rebirth, "startup repair supports retained UI 13 renderer");
                else Reject(() => AovkGraphics.Read(game), "unsupported old UI renderer cannot use new repair");
                File.Copy(Path.Combine(payloads, "version-rebirth-ui" + revision + ".dll"), Path.Combine(game, "version.dll"), true);
                var repair = File.ReadAllBytes(Path.Combine(payloads, "version-rebirth-ui.dll"));
                if (revision == "13") {
                    var before = AovkGraphics.Read(game);
                    Reject(() => AovkGraphics.InstallStartupRepair(game, Bytes("unverified")), "unverified repair rejected");
                    Reject(() => AovkGraphics.InstallStartupRepair(game, repair, index => { throw new IOException("repair write failed"); }), "repair failure reported");
                    Check(!AovkGraphics.HasPending(game) && before.Patch.SequenceEqual(AovkGraphics.Read(game).Patch), "repair failure restores original and clears pending marker");
                    AovkGraphics.InstallStartupRepair(game, repair);
                    var repaired = AovkGraphics.Read(game);
                    Check(repaired.Patch.SequenceEqual(repair) && repaired.Renderer.SequenceEqual(before.Renderer) && repaired.Ini.SequenceEqual(before.Ini), "repair preserves renderer and exact preferences");
                    Check(Directory.GetDirectories(Path.Combine(game, "NexusGraphicsBackups")).Any(), "repair retains recovery backup");
                    AovkGraphics.InstallStartupRepair(game, repair);
                    Check(!AovkGraphics.HasPending(game), "already repaired install is a no-op");
                    File.Copy(Path.Combine(payloads, "version-rebirth-startup1.dll"), Path.Combine(game, "version.dll"), true);
                    Check(AovkGraphics.Hash(AovkGraphics.Read(game).Patch)==AovkGraphics.PreviousStartupPatch, "first startup repair remains readable for replacement");
                    AovkGraphics.InstallStartupRepair(game, repair);
                    Check(AovkGraphics.Read(game).Patch.SequenceEqual(repair), "second startup repair replaces first without changing renderer");
                }
                else Reject(() => AovkGraphics.InstallStartupRepair(game, repair), "startup repair rejects unsupported older renderer");
            }
            var upgrade = AovkGraphics.Read(game);
            Check(upgrade.ModernUi && upgrade.UpgradeAvailable && upgrade.Changed, "previous UI has a Save upgrade");
            Reject(() => AovkGraphics.Apply(new[] { upgrade }, index => { if (index == 1) throw new IOException("preview update failed"); }), "preview update rollback");
            Check(AovkGraphics.Read(game).UpgradeAvailable, "failed preview update restores old matched pair");
            AovkGraphics.Apply(new[] { upgrade });
            modern = AovkGraphics.Read(game);
            Check(!modern.UpgradeAvailable && !modern.Changed && AovkGraphics.Hash(modern.Renderer) == AovkGraphics.UiRenderer, "previous preview updates and clears pending change");
            }
            modern.Enabled = false; AovkGraphics.Apply(new[] { modern });
            var modernOff = AovkGraphics.Read(game);
            Check(!modernOff.Enabled && modernOff.ModernUi && original.SequenceEqual(modernOff.Renderer), kind + " stock fallback retains UI preference");
            modernOff.Enabled = true; AovkGraphics.Apply(new[] { modernOff });
            modern = AovkGraphics.Read(game);
            Check(modern.ModernUi && AovkGraphics.Hash(modern.Renderer) == AovkGraphics.UiRenderer, kind + " AO-VK reactivation remembers preview");
            modern.Values["RVK_NexusUI"] = "0"; AovkGraphics.Apply(new[] { modern });
            enabled = AovkGraphics.Read(game);
            Check(!enabled.ModernUi && AovkGraphics.Hash(enabled.Renderer) == AovkGraphics.VkRenderer, kind + " preview opt-out restores accepted AO-VK");
            Check(!File.Exists(Path.Combine(game, "NEXUS-AOVK-TEST.txt")), kind + " has no test-marker dependency");
            Check(rebirth || !File.Exists(Path.Combine(game, "version.dll")), kind + " does not install an unrelated version.dll");
            var config = AovkGraphics.ReadRenderer(Encoding.UTF8.GetString(enabled.Ini));
            Check(AovkGraphics.Unavailable.All(key => config[key] == "0") && config["RVK_GrassOn"] == "0" && config["RVK_NormalMaps"] == "0", kind + " conservative first-start effects");
            enabled.Values["RVK_Aniso"] = "4"; AovkGraphics.Apply(new[] { enabled });
            var off = AovkGraphics.Read(game); var saved = off.Ini; off.Enabled = false; AovkGraphics.Apply(new[] { off });
            Check(original.SequenceEqual(File.ReadAllBytes(Path.Combine(game, "randy31.dll"))), kind + " disabling restores byte-identical original renderer");
            Check(saved.SequenceEqual(File.ReadAllBytes(Path.Combine(game, "randy-vk.ini"))), kind + " disabling preserves exact graphics settings");
            if (rebirth) Check(AovkGraphics.Hash(File.ReadAllBytes(Path.Combine(game, "version.dll"))) == AovkGraphics.StockPatch, "Rebirth disables both files together");
            var onAgain = AovkGraphics.Read(game); onAgain.Enabled = true; AovkGraphics.Apply(new[] { onAgain });
            Check(AovkGraphics.Read(game).Values["RVK_Aniso"] == "4", kind + " reactivation remembers individual settings");
        }
        if (clientCompatibilityOnly) return;
        string recoverGame = Fresh(temporary, source, true);
        var read = AovkGraphics.Read(recoverGame); read.Enabled = true;
        var transaction = new[] {
            new AovkGraphics.Write { Path=Path.Combine(recoverGame,"randy31.dll"), Before=read.Renderer, After=File.ReadAllBytes(Path.Combine(source,"randy31-aovk-test.dll")) },
            new AovkGraphics.Write { Path=Path.Combine(recoverGame,"version.dll"), Before=read.Patch, After=File.ReadAllBytes(Path.Combine(source,"version-aovk-test.dll")) },
            new AovkGraphics.Write { Path=Path.Combine(recoverGame,"randy-vk.ini"), Before=null, After=AovkGraphics.Configuration(read) },
            new AovkGraphics.Write { Path=Path.Combine(recoverGame,"randy31_orig.dll"), Before=null, After=read.Renderer }
        };
        AovkGraphics.PrepareRecovery(recoverGame, transaction);
        // Simulate process termination after the renderer/INI/alias write, before patch write.
        foreach (int index in new[] { 0, 2, 3 }) File.WriteAllBytes(transaction[index].Path, transaction[index].After);
        Reject(() => AovkGraphics.Read(recoverGame), "interrupted pair cannot appear ready");
        Reject(() => AovkGraphics.EnsureReadyForLaunch(recoverGame), "interrupted folder blocks Nexus login");
        File.WriteAllText(Path.Combine(recoverGame, "randy-vk.ini"), "external edit");
        Reject(() => AovkGraphics.Recover(recoverGame), "recovery preserves external edits");
        Check(AovkGraphics.HasPending(recoverGame) && File.ReadAllText(Path.Combine(recoverGame, "randy-vk.ini")) == "external edit", "rejected recovery retains evidence and user's file");
        File.WriteAllBytes(transaction[2].Path, transaction[2].After);
        AovkGraphics.Recover(recoverGame);
        Check(!AovkGraphics.Read(recoverGame).Enabled && !File.Exists(transaction[2].Path) && !File.Exists(transaction[3].Path), "recovery restores files and original absence");
        Check(!AovkGraphics.HasPending(recoverGame), "recovery clears launch block");
        string other = Fresh(temporary, source, false);
        var first = AovkGraphics.Read(recoverGame); first.Enabled = true;
        var second = AovkGraphics.Read(other); second.Enabled = true;
        File.WriteAllText(Path.Combine(other, "version.dll"), "unrecognized third-party patch");
        Reject(() => AovkGraphics.Apply(new[] { first, second }), "all folders are validated before changing any renderer");
        Check(!AovkGraphics.Read(recoverGame).Enabled, "later unsupported folder leaves earlier folder unchanged");
        Reject(() => AovkGraphics.Read(other), "unknown local/third-party patch is not overwritten");
        using (var held = new FileStream(Path.Combine(recoverGame, "NexusGraphics.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None)) {
            Reject(() => AovkGraphics.Apply(new[] { first }), "another Nexus graphics operation prevents concurrent installation");
            Reject(() => AovkGraphics.EnsureReadyForLaunch(recoverGame), "active graphics transaction blocks login");
        }
        File.WriteAllText(Path.Combine(recoverGame, "randy-vk.ini"), "[Renderer]\nRVK_Enhance=0\n[Native]\nDevice=on\nScene=on\n");
        var adopted = AovkGraphics.Read(recoverGame); adopted.Enabled = true; AovkGraphics.Apply(new[] { adopted });
        string adoptedIni = File.ReadAllText(Path.Combine(recoverGame, "randy-vk.ini"));
        Check(adoptedIni.Contains("Device=off") && adoptedIni.Contains("Scene=off") && adoptedIni.Contains("Visuals=off"), "adopted partial settings receive compatible native defaults");
    }
}
