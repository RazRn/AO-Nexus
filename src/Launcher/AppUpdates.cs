using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace AOLauncher
{
    internal sealed class UpdateRelease
    {
        internal Version Version;
        internal string Tag, Notes, DownloadUrl, Sha256;
        internal long Size;
    }

    internal static class AppUpdates
    {
        internal const string Repository = "https://github.com/RazRn/AO-Nexus";
        internal const string LatestApi = "https://api.github.com/repos/RazRn/AO-Nexus/releases/latest";
        internal const long MaxPackageBytes = 100 * 1024 * 1024;
        internal const long MaxExecutableBytes = 150 * 1024 * 1024;
        internal static Version CurrentVersion => typeof(AppUpdates).Assembly.GetName().Version;
        internal static string FormatReleaseNotes(string markdown)
        {
            string text = (markdown ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
            if (text.Length > 32000) text = text.Substring(0, 32000);
            text = Regex.Replace(text, @"(?m)^#{1,6}\s+", "", RegexOptions.None, TimeSpan.FromMilliseconds(100));
            text = Regex.Replace(text, @"(?m)^[ \t]*[-*][ \t]+", "• ", RegexOptions.None, TimeSpan.FromMilliseconds(100));
            text = text.Replace("**", "").Replace("`", "");
            return text.Trim().Replace("\n", "\r\n");
        }
        internal static string UpdateRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AONexus", "Updates");
        public sealed class ReleaseDto
        {
            public string tag_name { get; set; }
            public string body { get; set; }
            public bool draft { get; set; }
            public bool prerelease { get; set; }
            public AssetDto[] assets { get; set; }
        }
        public sealed class AssetDto
        {
            public string name { get; set; }
            public string browser_download_url { get; set; }
            public string digest { get; set; }
            public long size { get; set; }
        }
        internal static UpdateRelease ParseRelease(string json)
        {
            var data = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 }.Deserialize<ReleaseDto>(json);
            if (data == null || data.draft || data.prerelease || !Regex.IsMatch(data.tag_name ?? "", @"^v\d+\.\d+\.\d+(\.\d+)?$"))
                throw new InvalidDataException("The latest release is not a stable version.");
            Version parsed;
            if (!Version.TryParse(data.tag_name.Substring(1), out parsed)) throw new InvalidDataException("Invalid release version.");
            var version = new Version(parsed.Major, parsed.Minor, parsed.Build, Math.Max(0, parsed.Revision));
            var assets = (data.assets ?? new AssetDto[0]).Where(asset => asset.name == "AO-Nexus-Luckyspawn.zip").ToArray();
            if (assets.Length != 1) throw new InvalidDataException("The release package is missing or ambiguous.");
            var file = assets[0];
            if (file.size <= 0 || file.size > MaxPackageBytes || !Regex.IsMatch(file.digest ?? "", "^sha256:[a-fA-F0-9]{64}$")
                || file.browser_download_url != Repository + "/releases/download/" + data.tag_name + "/AO-Nexus-Luckyspawn.zip")
                throw new InvalidDataException("The release package has no valid GitHub checksum or download address.");
            return new UpdateRelease { Version = version, Tag = data.tag_name, Notes = data.body ?? "", Size = file.size,
                DownloadUrl = file.browser_download_url, Sha256 = file.digest.Substring(7).ToLowerInvariant() };
        }
        private static HttpClient Client()
        {
            var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(5) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AO-Nexus/" + CurrentVersion.ToString(3));
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
            return client;
        }
        internal static async Task<UpdateRelease> CheckAsync(CancellationToken cancellation)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            using (var client = Client())
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                using (var response = await client.GetAsync(LatestApi, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var buffer = new MemoryStream())
                    {
                        await CopyBounded(input, buffer, 1024 * 1024, timeout.Token, null).ConfigureAwait(false);
                        return ParseRelease(Encoding.UTF8.GetString(buffer.ToArray()));
                    }
                }
            }
        }
        internal static bool AllowedDownload(Uri uri) => uri.Scheme == "https" && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo)
            && (uri.Host == "github.com" || uri.Host == "release-assets.githubusercontent.com" || uri.Host == "objects.githubusercontent.com");

        internal static async Task<string> StageAsync(UpdateRelease release, string target, CancellationToken cancellation, IProgress<int> progress)
        {
            if (release.Version <= CurrentVersion) throw new InvalidDataException("Updates must be newer than the installed version.");
            // Fail before downloading if this installation requires elevation. Never request administrator access.
            string probe = Path.Combine(Path.GetDirectoryName(target), ".nexus-write-" + Guid.NewGuid().ToString("N"));
            using (File.Create(probe)) { }
            File.Delete(probe);
            string folder = Path.Combine(UpdateRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string archive = Path.Combine(folder, "release.zip");
            try
            {
            using (var active = new FileStream(Path.Combine(folder, ".active"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
            using (var client = Client())
            {
                var address = new Uri(release.DownloadUrl);
                bool downloaded = false;
                for (int redirect = 0; redirect < 5; redirect++)
                {
                    if (!AllowedDownload(address)) throw new InvalidDataException("Unexpected release download host.");
                    using (var response = await client.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false))
                    {
                        if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400)
                        {
                            if (response.Headers.Location == null) throw new InvalidDataException("Missing download redirect.");
                            address = new Uri(address, response.Headers.Location); continue;
                        }
                        response.EnsureSuccessStatusCode();
                        if (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength != release.Size)
                            throw new InvalidDataException("Unexpected release package size.");
                        using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var output = new FileStream(archive, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                            await CopyBounded(input, output, release.Size, cancellation, progress).ConfigureAwait(false);
                        downloaded = true; break;
                    }
                }
                if (!downloaded) throw new InvalidDataException("Too many download redirects.");
            }
            cancellation.ThrowIfCancellationRequested();
            string executable = await Task.Run(() => VerifyAndExtract(archive, folder, release), cancellation).ConfigureAwait(false);
            File.Delete(archive);
            return executable;
            }
            }
            catch { UpdateCleanup.RemoveStage(UpdateRoot, folder); throw; }
        }
        private static async Task CopyBounded(Stream input, Stream output, long limit, CancellationToken cancellation, IProgress<int> progress)
        {
            byte[] buffer = new byte[81920]; long total = 0; int read;
            while ((read = await input.ReadAsync(buffer, 0, buffer.Length, cancellation).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > limit) throw new InvalidDataException("Download exceeded its expected size.");
                await output.WriteAsync(buffer, 0, read, cancellation).ConfigureAwait(false);
                progress?.Report((int)(total * 100 / limit));
            }
        }
        internal static string VerifyAndExtract(string archive, string folder, UpdateRelease release)
        {
            if (new FileInfo(archive).Length != release.Size || !string.Equals(Hash(archive), release.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("GitHub release checksum verification failed.");
            using (var zip = ZipFile.OpenRead(archive))
            {
                if (zip.Entries.Count > 1000) throw new InvalidDataException("Too many release package entries.");
                var executables = zip.Entries.Where(entry => entry.FullName.Equals("AO-Nexus/AONexus.exe", StringComparison.OrdinalIgnoreCase)).ToArray();
                var manifests = zip.Entries.Where(entry => entry.FullName.Equals("AO-Nexus/SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase)).ToArray();
                if (executables.Length != 1 || manifests.Length != 1 || executables[0].Length <= 0
                    || executables[0].Length > MaxExecutableBytes || manifests[0].Length > 65536)
                    throw new InvalidDataException("Invalid release package contents.");
                string manifest;
                using (var input = manifests[0].Open()) using (var buffer = new MemoryStream())
                {
                    CopyBounded(input, buffer, 65536, CancellationToken.None, null).GetAwaiter().GetResult();
                    manifest = Encoding.UTF8.GetString(buffer.ToArray()).TrimStart('\uFEFF');
                }
                var hashes = Regex.Matches(manifest, @"(?m)^([a-fA-F0-9]{64})  AONexus\.exe\r?$" );
                if (hashes.Count != 1) throw new InvalidDataException("Missing executable checksum.");
                // Extract only this exact entry to a path we chose. Never expand arbitrary ZIP paths.
                string staged = Path.Combine(folder, "AONexus.exe");
                using (var input = executables[0].Open())
                using (var output = new FileStream(staged, FileMode.CreateNew))
                    CopyBounded(input, output, executables[0].Length, CancellationToken.None, null).GetAwaiter().GetResult();
                if (!string.Equals(Hash(staged), hashes[0].Groups[1].Value, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Executable checksum verification failed.");
                var assembly = AssemblyName.GetAssemblyName(staged);
                if (assembly.Name != "AONexus" || assembly.Version != release.Version)
                    throw new InvalidDataException("Executable version does not match the release.");
                return staged;
            }
        }
        internal static string Hash(string path)
        {
            using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
    }

    internal static class UpdateJournal
    {
        private static readonly object Sync = new object();
        private static int limit = 5 * 1024 * 1024;
        internal static int MaxMegabytes { get { lock (Sync) return limit / (1024 * 1024); } }
        internal static void Configure(int megabytes)
        {
            lock (Sync)
            {
                limit = LogRetention.NormalizeMb(megabytes) * 1024 * 1024;
                try
                {
                    string file = Path.Combine(AppUpdates.UpdateRoot, "update-history.log");
                    if (!UpdateCleanup.PlainPath(file)) return;
                    if (File.Exists(file)) LogRetention.AppendFile(file, "", limit);
                    if (File.Exists(file + ".previous") && UpdateCleanup.PlainPath(file + ".previous")) File.Delete(file + ".previous");
                }
                catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        internal static void Record(string message)
        {
            lock (Sync)
            try
            {
                string file = Path.Combine(AppUpdates.UpdateRoot, "update-history.log");
                if (!UpdateCleanup.PlainPath(file)) return;
                Directory.CreateDirectory(AppUpdates.UpdateRoot);
                LogRetention.AppendFile(file, DateTime.UtcNow.ToString("u") + " " + (message ?? "").Replace('\r', ' ').Replace('\n', ' ') + Environment.NewLine, limit);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        internal static void Installed()
        {
            Record("Installed AO Nexus " + AppUpdates.CurrentVersion.ToString(3));
            try { File.WriteAllText(Path.Combine(AppUpdates.UpdateRoot, "installed-version.txt"), AppUpdates.CurrentVersion.ToString()); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        internal static bool ConsumeInstalledNotice(string root)
        {
            try
            {
                string file = Path.Combine(root, "installed-version.txt");
                if (!File.Exists(file) || new FileInfo(file).Length > 128 || File.ReadAllText(file).Trim() != AppUpdates.CurrentVersion.ToString()) return false;
                File.Delete(file); return true;
            }
            catch (IOException) { return false; } catch (UnauthorizedAccessException) { return false; }
        }
    }

    internal sealed class UpdateRequest
    {
        public string Target { get; set; }
        public string OldSha256 { get; set; }
        public string NewSha256 { get; set; }
        public int ParentId { get; set; }
        public long ParentStartTicks { get; set; }
        public int MaxLogSizeMb { get; set; } = 5;
    }

    internal static class UpdateInstaller
    {
        internal static void Start(string staged, string target)
        {
            using (var parent = Process.GetCurrentProcess())
            {
                var request = new UpdateRequest { Target = Path.GetFullPath(target), OldSha256 = AppUpdates.Hash(target), NewSha256 = AppUpdates.Hash(staged),
                    ParentId = parent.Id, ParentStartTicks = parent.StartTime.ToUniversalTime().Ticks, MaxLogSizeMb = UpdateJournal.MaxMegabytes };
                string path = Path.Combine(Path.GetDirectoryName(staged), "request.json");
                File.WriteAllText(path, new JavaScriptSerializer().Serialize(request));
                Process.Start(new ProcessStartInfo(staged, "--apply-update \"" + path + "\"") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
            }
        }
        // Called in a separate, verified staged copy before the usual single-instance guard.
        internal static void Apply(string requestPath)
        {
            string staged = Application.ExecutablePath;
            string folder = Path.GetDirectoryName(staged);
            string logPath = Path.Combine(folder, "update-result.txt");
            try
            {
                if (!Path.GetFullPath(folder).StartsWith(Path.GetFullPath(AppUpdates.UpdateRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    || Path.GetFullPath(requestPath) != Path.Combine(folder, "request.json") || new FileInfo(requestPath).Length > 16384)
                    throw new InvalidDataException("Invalid update request location.");
                var request = new JavaScriptSerializer().Deserialize<UpdateRequest>(File.ReadAllText(requestPath));
                if (request == null || !Path.IsPathRooted(request.Target) || Path.GetFullPath(request.Target).Equals(staged, StringComparison.OrdinalIgnoreCase)
                    || AppUpdates.Hash(staged) != request.NewSha256 || AssemblyName.GetAssemblyName(request.Target).Name != "AONexus")
                    throw new InvalidDataException("Invalid update request.");
                UpdateJournal.Configure(request.MaxLogSizeMb);
                Process parent = null;
                try { parent = Process.GetProcessById(request.ParentId); } catch (ArgumentException) { }
                using (parent)
                {
                    if (parent != null && !parent.HasExited)
                    {
                        try
                        {
                            if (parent.StartTime.ToUniversalTime().Ticks != request.ParentStartTicks
                                || !string.Equals(parent.MainModule.FileName, request.Target, StringComparison.OrdinalIgnoreCase))
                                throw new InvalidDataException("The launcher process changed; update cancelled.");
                            if (!parent.WaitForExit(60000)) throw new IOException("AO Nexus is still running. Update cancelled; no files were replaced.");
                        }
                        catch (InvalidOperationException) when (parent.HasExited) { }
                        catch (System.ComponentModel.Win32Exception) when (parent.HasExited) { }
                    }
                }
                using (var active = new FileStream(Path.Combine(folder, ".active"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                {
                string backup = ReplaceVerified(staged, request.Target, request.OldSha256, request.NewSha256);
                try
                {
                    UpdateJournal.Installed();
                    Process.Start(new ProcessStartInfo(request.Target) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(request.Target) });
                }
                catch
                {
                    File.Replace(backup, request.Target, null);
                    throw;
                }
                try { File.WriteAllText(logPath, "Update installed. Previous executable: " + backup); File.WriteAllText(Path.Combine(folder, "complete"), ""); }
                catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
            }
            catch (Exception ex)
            {
                UpdateJournal.Record("Update failed: " + ex.Message);
                try { File.WriteAllText(logPath, "Update failed: " + ex.Message); } catch { }
                MessageBox.Show("AO Nexus could not finish the update. Your profile was not changed.\r\n\r\n" + ex.Message
                    + "\r\n\r\n" + logPath, "AO Nexus update", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        internal static string ReplaceVerified(string staged, string target, string oldHash, string newHash)
        {
            if (AppUpdates.Hash(target) != oldHash || AppUpdates.Hash(staged) != newHash)
                throw new InvalidDataException("An executable changed after verification. Update cancelled.");
            string temporary = target + ".update-" + Guid.NewGuid().ToString("N");
            string backup = target + ".previous-" + Guid.NewGuid().ToString("N");
            try
            {
                File.Copy(staged, temporary, false);
                if (AppUpdates.Hash(temporary) != newHash) throw new InvalidDataException("Staged copy verification failed.");
                File.Replace(temporary, target, backup);
                return backup;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
