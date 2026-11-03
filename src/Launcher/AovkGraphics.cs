using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace AOLauncher
{
    // Client fingerprints choose the renderer profile. No account/server path is rewritten.
    // The Rebirth login/crash patch must travel with its matching renderer.
    internal static class AovkGraphics
    {
        internal const string LargeAddressClient = "60647245852D50F6EEA322F2CB49971C73646E2B6EFA0AAE9F7385CE85AC8424";
        internal const string StockRenderer = "9D9E0DA25AC6F8C7ECDCE7A47A2409575945966A29E3B77850F7C62033A04BE3";
        internal const string VkRenderer = "60759C61C7383EFA39AE4F09981ED3E42D7A25CE7C9D093E6791A5C4A98040C6";
        internal const string Release081Renderer = "191B55E33790ECA78FEC2926D12A95344734BB3FE7969C4FA70879E17E497E15";
        internal const string Release081Patch = "8691D59A0F4C13859FAA094432156E7461E40F7F58A3A3A9E931B7DE675425F6";
        internal const string StockPatch = "F392CB4EE8810D933C2A13D0DFEADE79981F39A42CAE65302D312726D0A58301";
        internal const string VkPatch = "BB7581048F77D3BD89D95F983BD472B5ABB7FDA8D0B920D2825E499998A25B2A";
        internal const string PreviousStartupPatch = "E97616E84EB27CACFD6B6460DB5C12DAF6BB784397692B896DA3C2A4C5F5B8F4";
        internal const string PreviousVkPatch = "F156D1C2F7FCFFA6BFEF2DFCE7A1EF50664F7ED15E67625F0E156A40EDEFC857";
        internal const string UiRenderer = "BAF83111E172FA8BB833F2615B02D1162C0F6125CF6836421F22C714445825EE";
        internal const string UiPatch = VkPatch;
        internal const string PreviousUi16Patch = "3E3DBE6DB4B12F8E5A5BABC7F3079C18D644CE44344A58398EA46FB903222826";
        internal const string PrkRenderer = "D922CCFDE62A5B270B13E61C5C8B1C986A27A2181FEFE6AF498E08AEBB6A83C9";
        internal const string PreviousUiRenderer = "FAF0E6811B6766FEB5B1EFBED65EE41DB7DB523E89428645205446C358851BD8";
        internal const string PreviousUiPatch = "FCA74BD19DBC6D19496FF671259F1D4BA2A4920EC9C9A9C861A60D110FED9407";
        internal const string PreviousUi13Renderer = "4BAED22CA29039224AD0167F74E0A9793AF0A396CF3FE868DC99B66992E88151";
        internal const string PreviousUi13Patch = "890AAC842971D8DDA0917A6E83D0CFF26F0E89E4779B2B90E6728FBFFDB59E1A";
        internal const string PreviousUi14Renderer = "8E76F6F587C7258CDD30F6C481FCC68503094F1AA3B83D854004CEDDA3D05D74";
        internal const string PreviousUi14Patch = "7E85971B6142303D9BF474E28CDAC30BE827DD3BEA34D6E49E465862BAC56231";
        internal const string PreviousUi15Renderer = "430BF85A8B23049C63A4FF0CD152FCF134860AA3D080967BDAF2EF3BB86B8A61";
        internal const string PreviousUi15Patch = "37587FD0315268ABD592C0971F2E8BB8F235EB0781226DCEC95B75E7E6D23132";
        const string PendingName = "NexusGraphics.pending.json";
        static readonly string[] ManagedNames = { "randy31.dll", "version.dll", "randy31_orig.dll", "version-stock-rebirth.dll", "randy-vk.ini" };
        internal static readonly string[] Controls = {
            "RVK_Enhance", "RVK_PixelLight", "RVK_LightOver", "RVK_SunShadow",
            "RVK_ContactOn", "RVK_PtOn", "RVK_Hdr", "RVK_BumpOn", "RVK_PerfHud", "RVK_Aniso", "RVK_NexusUI"
        };
        internal static readonly string[] Unavailable = {
            "RVK_FrameGen", "RVK_Taa", "RVK_Upscaler", "RVK_MBlurOn", "RVK_Dof",
            "RVK_WeatherOn", "RVK_CloudsOn", "RVK_ProcSky", "RVK_Atmos", "RVK_Twilight",
            "RVK_BlueHour", "RVK_SkyGlow", "RVK_Afterglow", "RVK_StarFade", "RVK_Stars", "RVK_Rainbow"
        };
        private static readonly Dictionary<string, string> ClientFiles = new Dictionary<string, string> {
            { "AnarchyOnline.exe", "370C0670CC9CB46626EF24692376AAF492BB1787BAD8A1125365A6BE4F663862" },
            { "GUI.dll", "E485384721E2FE13972E840DFB6A9FE29B1BA4EB71B42CC049E5097A570B6DE1" },
            { "Interfaces.dll", "3AA79A44E76C3413543404058C5D07323BD3B69F4C3493C7B136BEFA1A55B0A7" },
            { "N3.dll", "8C019EFD72D547879A06585B69147AB1546B9617A2FCE090E5863791AEC8B0BB" }
        };
        private static readonly Dictionary<string, string> PrkFiles = new Dictionary<string, string> {
            { "AnarchyOnline.exe", "B4AFB033EAEC24A88994FECA4A374C982F1851F4ECAD532861F97D9A8FF8ACED" },
            { "GUI.dll", "956D01D09DD795CF44931C16D7471E3AEB9B23BF943A1B9C659F4761C79B1021" },
            { "Interfaces.dll", "8995F83EBFFE43EFC945975136760F408DB9AE14A7F9BF4081E5F17C691D2105" },
            { "N3.dll", "C71D97F3050565780CFC47E3C1969ACEC7B3CA2182497ECF50342E2AB9C2637F" }
        };

        internal sealed class Snapshot
        {
            internal string Folder;
            internal byte[] Renderer, Patch, Ini;
            internal byte[] OriginalRenderer, OriginalPatch, RendererAlias, PatchBackup;
            internal string ProfileName;
            internal bool Rebirth, Prepared;
            internal bool Enabled, OriginalEnabled, Preset;
            internal bool UpgradeAvailable;
            internal bool ModernUi => Values.ContainsKey("RVK_NexusUI") && Values["RVK_NexusUI"] == "1";
            internal Dictionary<string, string> Values;
            internal Dictionary<string, string> OriginalValues;
            internal bool Changed => Enabled != OriginalEnabled || Preset || (Enabled && ModernUi && UpgradeAvailable) ||
                Values.Any(pair => OriginalValues[pair.Key] != pair.Value);
        }

        internal static string NormalizeFolder(string folder) => Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        internal static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
        }
        private static byte[] RequireFile(string folder, string name, params string[] hashes)
        {
            var path = Path.Combine(folder, name);
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked graphics files are not supported: " + name);
            var bytes = File.ReadAllBytes(path);
            if (!hashes.Contains(Hash(bytes))) throw new IOException("Unsupported or changed file: " + name + ". No graphics changes were made.");
            return bytes;
        }
        static byte[] OptionalFile(string folder, string name)
        {
            string path = Path.Combine(folder, name);
            if (!File.Exists(path)) { if (Directory.Exists(path)) throw new IOException("A graphics filename is a directory: " + name); return null; }
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked graphics files are not supported: " + name);
            return File.ReadAllBytes(path);
        }
        static bool Same(byte[] first, byte[] second) => first == null ? second == null : second != null && first.SequenceEqual(second);
        static void PlainFolder(string folder)
        {
            for (var directory = new DirectoryInfo(folder); directory != null; directory = directory.Parent)
                if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Linked game or backup folders are not supported: " + directory.FullName);
        }
        internal static Func<string, byte[]> DownloadedPayload;
        internal static bool HasEmbeddedPayload => typeof(AovkGraphics).Assembly.GetManifestResourceInfo("AOLauncher.AOVK.randy31.dll") != null;
        static byte[] Payload(string name, string expectedHash = null)
        {
            using (var stream = typeof(AovkGraphics).Assembly.GetManifestResourceStream("AOLauncher.AOVK." + name))
            {
                if (stream == null)
                {
                    var downloaded = DownloadedPayload?.Invoke(name);
                    if (downloaded == null) throw new IOException("Download AO-VK in Settings > Graphics before enabling it.");
                    if (expectedHash != null && Hash(downloaded) != expectedHash) throw new IOException("The downloaded AO-VK file failed verification: " + name);
                    return downloaded;
                }
                using (var bytes = new MemoryStream()) {
                    stream.CopyTo(bytes); var value = bytes.ToArray();
                    if (expectedHash != null && Hash(value) != expectedHash) throw new IOException("The bundled AO-VK file failed verification: " + name);
                    return value;
                }
            }
        }
        internal static bool HasPending(string folder) => File.Exists(Path.Combine(NormalizeFolder(folder), PendingName));
        internal static void EnsureReadyForLaunch(string folder)
        {
            if (HasPending(folder)) throw new IOException("Graphics installation was interrupted. Open Settings > Graphics and restore the previous state before launching this folder.");
            string path = Path.Combine(folder, "NexusGraphics.lock");
            if (File.Exists(path)) using (var check = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) { }
        }
        internal static Snapshot Read(string folder)
        {
            folder = NormalizeFolder(folder);
            PlainFolder(folder);
            if (HasPending(folder)) throw new IOException("Graphics installation was interrupted. Restore the previous state below before using this folder.");
            var fingerprints = ClientFiles.Keys.ToDictionary(name => name, name => Hash(RequireFile(folder, name,
                name == "AnarchyOnline.exe" ? new[] { ClientFiles[name], PrkFiles[name], LargeAddressClient }
                    : new[] { ClientFiles[name], PrkFiles[name] })));
            bool classic = ClientFiles.All(pair => fingerprints[pair.Key] == pair.Value
                || (pair.Key == "AnarchyOnline.exe" && fingerprints[pair.Key] == LargeAddressClient));
            bool prk = PrkFiles.All(pair => fingerprints[pair.Key] == pair.Value);
            if (!classic && !prk) throw new IOException("Mixed or unsupported client files. AO-VK supports the verified classic AO/Rebirth and PRK clients, not the new engine.");
            string originalHash = prk ? PrkRenderer : StockRenderer;
            var renderer = RequireFile(folder, "randy31.dll", originalHash, VkRenderer, Release081Renderer, UiRenderer, PreviousUiRenderer, PreviousUi13Renderer, PreviousUi14Renderer, PreviousUi15Renderer);
            var patch = OptionalFile(folder, "version.dll");
            bool rebirth = patch != null && classic && (Hash(patch) == StockPatch || Hash(patch) == Release081Patch || Hash(patch) == VkPatch || Hash(patch) == PreviousStartupPatch || Hash(patch) == PreviousVkPatch || Hash(patch) == PreviousUi16Patch || Hash(patch) == PreviousUiPatch || Hash(patch) == PreviousUi13Patch || Hash(patch) == PreviousUi14Patch || Hash(patch) == PreviousUi15Patch);
            if (patch != null && !rebirth) throw new IOException("This folder has an unverified version.dll patch. Nexus will not replace it. This patch needs a matching AO-VK compatibility build.");
            bool ui13=Hash(renderer)==PreviousUi13Renderer;
            bool ui14=Hash(renderer)==PreviousUi14Renderer;
            bool ui15=Hash(renderer)==PreviousUi15Renderer;
            bool previousUi = Hash(renderer) == PreviousUiRenderer || ui13 || ui14 || ui15;
            bool modernUi = Hash(renderer) == UiRenderer || previousUi;
            bool release081 = Hash(renderer) == Release081Renderer;
            bool enabled = Hash(renderer) == VkRenderer || release081 || modernUi;
            // Keep existing verified pairs readable for rollback. The startup repair
            // also supports UI test 13 without forcing a renderer upgrade.
            bool compatiblePreviousPatch = rebirth && (
                (new[] { VkRenderer, UiRenderer, PreviousUi13Renderer }.Contains(Hash(renderer)) && Hash(patch) == PreviousStartupPatch) ||
                (Hash(renderer) == VkRenderer && Hash(patch) == PreviousVkPatch) ||
                (Hash(renderer) == UiRenderer && Hash(patch) == PreviousUi16Patch) ||
                (ui13 && Hash(patch) == VkPatch));
            if (rebirth && !compatiblePreviousPatch && Hash(patch) != (release081 ? Release081Patch : enabled ? (modernUi ? (previousUi ? (ui15 ? PreviousUi15Patch : ui14 ? PreviousUi14Patch : ui13 ? PreviousUi13Patch : PreviousUiPatch) : UiPatch) : VkPatch) : StockPatch))
                throw new IOException("Renderer and Rebirth patch do not match. Restore the prepared pair before changing graphics.");
            var alias = OptionalFile(folder, "randy31_orig.dll");
            if (alias != null && Hash(alias) != originalHash) throw new IOException("Existing randy31_orig.dll does not match this client. It has been preserved.");
            if (enabled && alias == null) throw new IOException("The original renderer backup is missing: randy31_orig.dll.");
            var patchBackup = OptionalFile(folder, "version-stock-rebirth.dll");
            if (patchBackup != null && (!rebirth || Hash(patchBackup) != StockPatch)) throw new IOException("The saved Rebirth patch does not match this installation.");
            if (enabled && rebirth && patchBackup == null) throw new IOException("The original Rebirth patch backup is missing.");
            // Admit old test installations without depending on their marker or sidecars.
            if (File.Exists(Path.Combine(folder, "randy31-aovk-test.dll"))) RequireFile(folder, "randy31-aovk-test.dll", VkRenderer, UiRenderer, PreviousUiRenderer, PreviousUi13Renderer, PreviousUi14Renderer, PreviousUi15Renderer);
            if (File.Exists(Path.Combine(folder, "version-aovk-test.dll"))) RequireFile(folder, "version-aovk-test.dll", VkPatch, PreviousStartupPatch, PreviousVkPatch, PreviousUi16Patch, PreviousUiPatch, PreviousUi13Patch, PreviousUi14Patch, PreviousUi15Patch);
            var ini = OptionalFile(folder, "randy-vk.ini");
            var defaults = ReadRenderer(Encoding.UTF8.GetString(Payload("defaults.ini")));
            defaults["RVK_NexusUI"] = "0";
            var values = ini == null ? defaults : ReadRenderer(Encoding.UTF8.GetString(ini));
            if (enabled) values["RVK_NexusUI"] = modernUi ? "1" : "0";
            var selected = new Dictionary<string, string>();
            foreach (string key in Controls)
            {
                string value;
                if (!values.TryGetValue(key, out value)) value = defaults[key];
                if (key == "RVK_Aniso" ? !new[] { "1", "2", "4", "8", "16" }.Contains(value) : value != "0" && value != "1")
                    throw new IOException("Unsupported AO-VK setting: " + key + ". Reload the tested configuration.");
                selected.Add(key, value);
            }
            return new Snapshot { Folder = folder, Renderer = renderer, Patch = patch, Ini = ini,
                Rebirth = rebirth, Prepared = alias != null && (!rebirth || patchBackup != null),
                RendererAlias = alias, PatchBackup = patchBackup, OriginalRenderer = enabled ? alias : renderer,
                OriginalPatch = rebirth ? (enabled ? patchBackup : patch) : null,
                ProfileName = rebirth ? "AO Rebirth Classic" : prk ? "Project Rubi-Ka Classic" : "Anarchy Online Classic (Steam / official)",
                Enabled = enabled, OriginalEnabled = enabled, Values = selected, UpgradeAvailable = previousUi,
                OriginalValues = new Dictionary<string, string>(selected) };
        }

        internal static Dictionary<string, string> ReadRenderer(string text)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string section = "";
            foreach (var line in text.TrimStart('\uFEFF').Replace("\r\n", "\n").Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("[") && trimmed.EndsWith("]")) section = trimmed.Substring(1, trimmed.Length - 2).Trim();
                else if (section.Equals("Renderer", StringComparison.OrdinalIgnoreCase) && !trimmed.StartsWith(";") && !trimmed.StartsWith("#"))
                {
                    int equal = trimmed.IndexOf('=');
                    if (equal > 0)
                    {
                        var key = trimmed.Substring(0, equal).Trim();
                        if (values.ContainsKey(key)) throw new IOException("Duplicate AO-VK setting: " + key);
                        values.Add(key, trimmed.Substring(equal + 1).Trim());
                    }
                }
            }
            return values;
        }

        internal static byte[] Configuration(Snapshot snapshot)
        {
            var changes = new Dictionary<string, string>(snapshot.Values, StringComparer.OrdinalIgnoreCase);
            foreach (string key in Unavailable) changes[key] = "0";
            // The optional preset is the lighting baseline that passed gameplay tests.
            // Ordinary edits preserve other values, including unverified environment trials.
            if (snapshot.Preset)
            {
                foreach (string key in new[] { "RVK_GrassOn", "RVK_LeafOn", "RVK_SwayOn", "RVK_PushOn", "RVK_PlantDetOn",
                    "RVK_FolLodOn", "RVK_WaterOn", "RVK_Particles", "RVK_NormalMaps", "RVK_Pbr", "RVK_Albedo",
                    "RVK_GroundOn", "RVK_SmokeOn", "RVK_BloomOn", "RVK_AoOn", "RVK_GiOn", "RVK_VolOn",
                    "RVK_SsrOn", "RVK_GradeOn", "RVK_TessOn", "RVK_CloudShadow", "RVK_Fxaa" }) changes[key] = "0";
            }
            var text = Encoding.UTF8.GetString(snapshot.Ini ?? Payload("defaults.ini"));
            string newline = text.Contains("\r\n") ? "\r\n" : "\n";
            var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
            bool renderer = false;
            int insert = -1;
            for (int index = 0; index < lines.Count; index++)
            {
                string line = lines[index].Trim().TrimStart('\uFEFF');
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    if (renderer) insert = index;
                    renderer = line.Substring(1, line.Length - 2).Trim().Equals("Renderer", StringComparison.OrdinalIgnoreCase);
                }
                else if (renderer)
                {
                    int equal = line.IndexOf('=');
                    string key = equal > 0 ? line.Substring(0, equal).Trim() : "";
                    string value;
                    if (changes.TryGetValue(key, out value)) { lines[index] = key + "=" + value; changes.Remove(key); }
                }
            }
            if (renderer) insert = lines.Count;
            if (insert < 0) { lines.Add("[Renderer]"); insert = lines.Count; }
            lines.InsertRange(insert, changes.Select(pair => pair.Key + "=" + pair.Value));
            // Native replacements target a different Randy build; keep the accepted proxy path.
            bool native = false, foundNative = false;
            for (int index = 0; index < lines.Count; index++) {
                string line = lines[index].Trim();
                if (line.StartsWith("[") && line.EndsWith("]")) { native = line.Equals("[Native]", StringComparison.OrdinalIgnoreCase); foundNative |= native; }
                else if (native && !line.StartsWith(";") && !line.StartsWith("#") && line.Contains("=")) {
                    string key = line.Substring(0, line.IndexOf('=')).Trim();
                    lines[index] = key + "=" + (key.Equals("Version", StringComparison.OrdinalIgnoreCase) ? "1" : "off");
                }
            }
            // Include every switch even when an older/user-created INI omitted it.
            var nativeKeys = new[] { "Skin", "Anim", "Visuals", "CatRender", "CatMesh", "CatQuery", "Device", "Scene", "Direct", "Retain", "Pick", "PopLog" };
            int nativeHeader = lines.FindIndex(line => line.Trim().Equals("[Native]", StringComparison.OrdinalIgnoreCase));
            if (!foundNative) { lines.Add("[Native]"); nativeHeader = lines.Count - 1; }
            int nativeEnd = nativeHeader + 1;
            while (nativeEnd < lines.Count && !lines[nativeEnd].TrimStart().StartsWith("[")) nativeEnd++;
            var existingNative = lines.Skip(nativeHeader + 1).Take(nativeEnd - nativeHeader - 1).Select(line => line.Split('=')[0].Trim()).ToList();
            lines.InsertRange(nativeEnd, nativeKeys.Where(key => !existingNative.Contains(key, StringComparer.OrdinalIgnoreCase)).Select(key => key + "=off"));
            return new UTF8Encoding(false).GetBytes(string.Join(newline, lines));
        }

        internal static void EnsureClosed(string folder)
        {
            foreach (var process in Process.GetProcessesByName("AnarchyOnline"))
                using (process)
                {
                    string path;
                    try { path = process.MainModule.FileName; }
                    catch (InvalidOperationException) { if (process.HasExited) continue; throw; }
                    catch (System.ComponentModel.Win32Exception) { throw new IOException("Cannot check a running AO client. Close AO before applying graphics."); }
                    if (string.Equals(NormalizeFolder(Path.GetDirectoryName(path)), folder, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Close all AO clients using this game folder, then save again.");
                }
        }

        internal sealed class Write : Nexus.Core.GraphicsTransaction.Write { }
        internal static void Commit(IList<Write> writes, Action<int> afterWrite = null)
            => Nexus.Core.GraphicsTransaction.Commit(writes.Cast<Nexus.Core.GraphicsTransaction.Write>().ToList(), afterWrite);
        internal sealed class RecoveryItem
        {
            public string Name { get; set; }
            public string BeforeHash { get; set; }
            public string AfterHash { get; set; }
        }
        internal sealed class RecoveryRecord
        {
            public int Format { get; set; } = 1;
            public string BackupId { get; set; }
            public List<RecoveryItem> Items { get; set; } = new List<RecoveryItem>();
        }
        static FileStream LockFolder(string folder)
        {
            PlainFolder(folder);
            string path = Path.Combine(folder, "NexusGraphics.lock");
            if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked graphics lock is not supported.");
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        static void DurableNewFile(string path, byte[] bytes)
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
        }
        internal static void PrepareRecovery(string folder, IList<Write> writes)
        {
            var record = new RecoveryRecord { BackupId = Guid.NewGuid().ToString("N") };
            string backup = Path.Combine(folder, "NexusGraphicsBackups", record.BackupId);
            PlainFolder(backup); Directory.CreateDirectory(backup);
            foreach (var write in writes) {
                string name = Path.GetFileName(write.Path);
                if (!ManagedNames.Contains(name) || NormalizeFolder(Path.GetDirectoryName(write.Path)) != folder) throw new IOException("Invalid graphics recovery target.");
                if (write.Before != null) DurableNewFile(Path.Combine(backup, name), write.Before);
                record.Items.Add(new RecoveryItem { Name = name, BeforeHash = write.Before == null ? null : Hash(write.Before), AfterHash = Hash(write.After) });
            }
            var bytes = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(record));
            // The immutable manifest remains beside the backups after the pending marker clears.
            DurableNewFile(Path.Combine(backup, "recovery.json"), bytes);
            DurableNewFile(Path.Combine(folder, PendingName), bytes);
        }
        internal static void Recover(string folder)
        {
            folder = NormalizeFolder(folder);
            using (LockFolder(folder)) {
                EnsureClosed(folder);
                var json = OptionalFile(folder, PendingName);
                if (json == null) return;
                if (json.Length > 65536) throw new IOException("Invalid graphics recovery record.");
                RecoveryRecord record;
                try { record = new JavaScriptSerializer().Deserialize<RecoveryRecord>(Encoding.UTF8.GetString(json)); }
                catch (Exception error) { throw new IOException("Invalid graphics recovery record. Use the saved files in NexusGraphicsBackups.", error); }
                if (record == null || record.Format != 1 || !Guid.TryParseExact(record.BackupId, "N", out var id) || record.Items == null
                    || record.Items.Count == 0 || record.Items.Count > ManagedNames.Length
                    || record.Items.Any(item => item == null || !ManagedNames.Contains(item.Name))
                    || record.Items.Select(item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != record.Items.Count)
                    throw new IOException("Invalid graphics recovery record.");
                string backup = Path.Combine(folder, "NexusGraphicsBackups", record.BackupId); PlainFolder(backup);
                var restore = new List<Write>(); var remove = new List<string>();
                foreach (var item in record.Items) {
                    var before = item.BeforeHash == null ? null : RequireFile(backup, item.Name, item.BeforeHash);
                    var current = OptionalFile(folder, item.Name);
                    if (!Same(before, current) && (current == null || Hash(current) != item.AfterHash))
                        throw new IOException("Graphics recovery found another edit in " + item.Name + ". The file was preserved.");
                    if (Same(before, current)) continue;
                    if (before == null) remove.Add(item.Name);
                    else restore.Add(new Write { Path = Path.Combine(folder, item.Name), Before = current, After = before });
                }
                Commit(restore);
                // A retry accepts already-restored files. Delete only files created by this transaction.
                foreach (string name in remove) {
                    var item = record.Items.Single(entry => entry.Name == name);
                    RequireFile(folder, name, item.AfterHash);
                    File.Delete(Path.Combine(folder, name));
                }
                File.Delete(Path.Combine(folder, PendingName));
            }
        }
        internal static bool NeedsRendererDownload(Snapshot snapshot)
        {
            // Installed official 0.8.1 is a separate profile: ordinary settings edits
            // must never downgrade it to the older downloadable renderer/repair.
            if (Hash(snapshot.Renderer) == Release081Renderer) {
                if (snapshot.Enabled && snapshot.ModernUi)
                    throw new IOException("The Nexus in-game UI renderer is not available for AO-VK 0.8.1. Keep that option off.");
                return false;
            }
            return snapshot.Enabled && Hash(snapshot.Renderer) != (snapshot.ModernUi ? UiRenderer : VkRenderer);
        }

        internal static byte[] RendererPayload(Snapshot snapshot) => !NeedsRendererDownload(snapshot)
            ? snapshot.Renderer : Payload(snapshot.ModernUi ? "randy31-ui.dll" : "randy31.dll", snapshot.ModernUi ? UiRenderer : VkRenderer);

        internal static byte[] RebirthPayload(Snapshot snapshot)
        {
            if (!snapshot.Enabled || !snapshot.Rebirth) return snapshot.OriginalPatch;
            string hash = Hash(snapshot.Renderer) == Release081Renderer ? Release081Patch : snapshot.ModernUi ? UiPatch : VkPatch;
            if (Hash(snapshot.Patch) == hash) return snapshot.Patch;
            // The proprietary patch is not part of the public renderer download. Accept
            // only a matching patch already supplied by the user, or a private embedded build.
            foreach (string name in new[] { "version-aovk-test.dll", snapshot.ModernUi ? "version-rebirth-ui.dll" : "version-rebirth.dll" })
            {
                byte[] local = OptionalFile(snapshot.Folder, name);
                if (local != null && Hash(local) == hash) return local;
            }
            if (HasEmbeddedPayload) return Payload(snapshot.ModernUi ? "version-rebirth-ui.dll" : "version-rebirth.dll", hash);
            throw new IOException("This Rebirth installation needs a matching compatibility patch supplied separately. The public AO-VK download contains only the renderer. Existing game files have been kept.");
        }

        // Local maintenance path: update only the repair DLL, preserving the renderer
        // revision and exact preferences. Uses the normal graphics recovery journal.
        internal static void InstallStartupRepair(string folder, byte[] repair, Action<int> afterWrite = null)
        {
            folder = NormalizeFolder(folder);
            if (repair == null || Hash(repair) != VkPatch) throw new IOException("Unverified startup repair.");
            using (LockFolder(folder)) {
                EnsureClosed(folder);
                var snapshot = Read(folder);
                if (!snapshot.Rebirth || !snapshot.Enabled ||
                    !new[] { VkRenderer, UiRenderer, PreviousUi13Renderer }.Contains(Hash(snapshot.Renderer)))
                    throw new IOException("The startup repair does not support this renderer/patch pair.");
                if (Same(snapshot.Patch, repair)) return;
                var writes = new[] { new Write { Path = Path.Combine(folder, "version.dll"), Before = snapshot.Patch, After = repair } };
                PrepareRecovery(folder, writes);
                try {
                    EnsureClosed(folder);
                    Commit(writes, afterWrite);
                    File.Delete(Path.Combine(folder, PendingName));
                }
                catch {
                    try { if (Same(OptionalFile(folder, "version.dll"), snapshot.Patch)) File.Delete(Path.Combine(folder, PendingName)); } catch { }
                    throw;
                }
            }
        }

        internal static void Apply(IEnumerable<Snapshot> snapshots, Action<int> afterWrite = null)
        {
            var changed = snapshots.Where(snapshot => snapshot.Changed).ToList();
            if (changed.Count == 0) return;
            if (changed.Select(item => NormalizeFolder(item.Folder)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != changed.Count)
                throw new IOException("A game folder was selected more than once.");
            var writes = new List<Write>();
            var locks = new List<FileStream>(); var journals = new List<string>();
            try
            {
                foreach (var snapshot in changed.OrderBy(item => item.Folder, StringComparer.OrdinalIgnoreCase)) locks.Add(LockFolder(snapshot.Folder));
                foreach (var snapshot in changed) {
                    EnsureClosed(snapshot.Folder);
                    var latest = Read(snapshot.Folder);
                    if (!Same(latest.Renderer, snapshot.Renderer) || !Same(latest.Patch, snapshot.Patch) || !Same(latest.Ini, snapshot.Ini)
                        || !Same(latest.RendererAlias, snapshot.RendererAlias) || !Same(latest.PatchBackup, snapshot.PatchBackup))
                        throw new IOException("Graphics files changed while Settings was open. Reopen Settings and try again.");
                    if (snapshot.Enabled) {
                        writes.Add(new Write { Path = Path.Combine(snapshot.Folder, "randy31_orig.dll"), Before = snapshot.RendererAlias, After = snapshot.OriginalRenderer });
                        if (snapshot.Rebirth) writes.Add(new Write { Path = Path.Combine(snapshot.Folder, "version-stock-rebirth.dll"), Before = snapshot.PatchBackup, After = snapshot.OriginalPatch });
                    }
                    if (snapshot.Rebirth) writes.Add(new Write { Path = Path.Combine(snapshot.Folder, "version.dll"), Before = snapshot.Patch,
                        After = snapshot.Enabled ? RebirthPayload(snapshot) : snapshot.OriginalPatch });
                    writes.Add(new Write { Path = Path.Combine(snapshot.Folder, "randy31.dll"), Before = snapshot.Renderer,
                        After = snapshot.Enabled ? RendererPayload(snapshot) : snapshot.OriginalRenderer });
                    // Switching off alone preserves the exact INI, including its absence.
                    if (snapshot.Enabled || snapshot.Preset || snapshot.Values.Any(pair => pair.Value != snapshot.OriginalValues[pair.Key]))
                        writes.Add(new Write { Path = Path.Combine(snapshot.Folder, "randy-vk.ini"), Before = snapshot.Ini, After = Configuration(snapshot) });
                }
                foreach (var snapshot in changed) {
                    PrepareRecovery(snapshot.Folder, writes.Where(write => Path.GetDirectoryName(write.Path) == snapshot.Folder).ToList());
                    journals.Add(snapshot.Folder);
                }
                foreach (var snapshot in changed) EnsureClosed(snapshot.Folder);
                Commit(writes, afterWrite);
                foreach (string folder in journals) File.Delete(Path.Combine(folder, PendingName));
            }
            catch {
                // A completed rollback needs no user recovery. Keep records for partial writes.
                foreach (string folder in journals) {
                    try { if (writes.Where(write => Path.GetDirectoryName(write.Path) == folder).All(write => Same(OptionalFile(folder, Path.GetFileName(write.Path)), write.Before)))
                        File.Delete(Path.Combine(folder, PendingName)); } catch { }
                }
                throw;
            }
            finally { foreach (var held in locks) held.Dispose(); }
        }
    }
}
