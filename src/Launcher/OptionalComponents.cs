using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace AOLauncher
{
    // Release descriptors ship with Nexus: neither a remote manifest nor a redirect can
    // substitute a different runtime. Packages are immutable and shared by installations.
    internal static class OptionalComponents
    {
        internal const string Repository = "https://github.com/RazRn/AO-Nexus";
        internal const long MaximumDownload = 128L * 1024 * 1024;
        internal static Func<HttpMessageHandler> CreateHandler = () => new HttpClientHandler { AllowAutoRedirect = false };
        internal sealed class Descriptor
        {
            public string Id { get; set; }
            public string Version { get; set; }
            public string Url { get; set; }
            public long Size { get; set; }
            public string RequiredFile { get; set; }
            public string Manifest { get; set; }
            public bool Retired { get; set; }
        }
        internal static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AONexus", "Components");
        internal static Descriptor Read(string id)
        {
            ValidateId(id);
            using (var stream = typeof(OptionalComponents).Assembly.GetManifestResourceStream("AOLauncher.Components." + id + ".json"))
            {
                if (stream == null) throw new IOException("This Nexus build has no download configured for " + id + ". You can still use an existing installation.");
                using (var reader = new StreamReader(stream, Encoding.UTF8)) return Parse(reader.ReadToEnd(), id);
            }
        }
        internal static Descriptor Parse(string json, string id)
        {
            ValidateId(id);
            var item = new JavaScriptSerializer { MaxJsonLength = 2 * 1024 * 1024 }.Deserialize<Descriptor>(json);
            if (item != null && item.Retired)
                throw new IOException(Localization.Texts.Get("Components.DownloadUnavailable"));
            if (item == null || item.Id != id || !Regex.IsMatch(item.Version ?? "", @"^[a-zA-Z0-9][a-zA-Z0-9._-]{0,79}$")
                || item.Url != Repository + "/releases/download/" + item.Version + "/AO-Nexus-" + id + ".zip"
                || item.Size <= 0 || item.Size > MaximumDownload || string.IsNullOrEmpty(item.Manifest)
                || item.RequiredFile != "randy31.dll")
                throw new InvalidDataException("Invalid optional component descriptor.");
            return item;
        }
        static void ValidateId(string id)
        {
            if (id != "aovk") throw new ArgumentException("Unknown component.");
        }
        internal static string Installed(string id)
        {
            Descriptor item;
            try { item = Read(id); } catch (IOException) { return null; }
            return Installed(item, Root);
        }
        internal static string Installed(Descriptor item, string root)
        {
            using (var manifest = new MemoryStream(Encoding.UTF8.GetBytes(item.Manifest)))
            {
                string file = EmbeddedBackend.FindVerifiedBundle(Path.Combine(root, item.Id), manifest, item.RequiredFile);
                return file == null ? null : Path.GetDirectoryName(file);
            }
        }
        internal static byte[] ReadAovkFile(string name)
        {
            if (!new[] { "randy31.dll", "randy31-ui.dll", "version-rebirth.dll", "version-rebirth-ui.dll" }.Contains(name))
                throw new InvalidDataException("Unknown AO-VK file.");
            string folder = Installed("aovk");
            if (folder == null) throw new IOException("Download AO-VK in Settings > Graphics before enabling it.");
            return File.ReadAllBytes(Path.Combine(folder, name));
        }
        internal static async Task<string> InstallAsync(string id, CancellationToken cancellation, IProgress<int> progress)
        {
            var item = Read(id);
            return await InstallAsync(item, Root, cancellation, progress).ConfigureAwait(false);
        }
        internal static async Task<string> InstallAsync(Descriptor item, string root, CancellationToken cancellation, IProgress<int> progress)
        {
            if (item.Retired) throw new IOException(Localization.Texts.Get("Components.DownloadUnavailable"));
            cancellation.ThrowIfCancellationRequested();
            var installed = Installed(item, root);
            if (installed != null) return installed;
            // Temporary files are unique; no active runtime or game directory is overwritten.
            string archive = Path.Combine(Path.GetTempPath(), "AONexus-component-" + Guid.NewGuid().ToString("N") + ".zip");
            try
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
                using (var client = new HttpClient(CreateHandler()))
                {
                    timeout.CancelAfter(TimeSpan.FromMinutes(10));
                    client.Timeout = Timeout.InfiniteTimeSpan;
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("AO-Nexus/" + typeof(OptionalComponents).Assembly.GetName().Version.ToString(3));
                    var address = new Uri(item.Url);
                    bool downloaded = false;
                    for (int redirect = 0; redirect < 5; redirect++)
                    {
                        if (!AppUpdates.AllowedDownload(address)) throw new InvalidDataException("Unexpected component download host.");
                        using (var response = await client.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                        {
                            if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400)
                            {
                                if (response.Headers.Location == null) throw new InvalidDataException("Missing component redirect.");
                                address = new Uri(address, response.Headers.Location); continue;
                            }
                            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                                throw new IOException("This optional package has not been published for this Nexus build. Your existing setup has been kept.");
                            response.EnsureSuccessStatusCode();
                            if (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value != item.Size)
                                throw new InvalidDataException("Unexpected component download size.");
                            using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                            using (var output = new FileStream(archive, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                            {
                                byte[] buffer = new byte[81920]; long total = 0; int read;
                                while ((read = await input.ReadAsync(buffer, 0, buffer.Length, timeout.Token).ConfigureAwait(false)) > 0)
                                {
                                    total += read;
                                    if (total > item.Size) throw new InvalidDataException("Component download exceeded its expected size.");
                                    await output.WriteAsync(buffer, 0, read, timeout.Token).ConfigureAwait(false);
                                    progress?.Report((int)(total * 90 / item.Size));
                                }
                                if (total != item.Size) throw new InvalidDataException("Incomplete component download. Try again.");
                            }
                            downloaded = true; break;
                        }
                    }
                    if (!downloaded) throw new IOException("Too many component download redirects.");
                    string folder = await Task.Run(() => InstallArchive(item, root, archive, timeout.Token), timeout.Token).ConfigureAwait(false);
                    progress?.Report(100);
                    return folder;
                }
            }
            finally { if (File.Exists(archive)) File.Delete(archive); }
        }
        internal static string InstallArchive(Descriptor item, string root, string archive, CancellationToken cancellation)
        {
            if (new FileInfo(archive).Length != item.Size) throw new InvalidDataException("Unexpected component archive size.");
            using (var payload = File.OpenRead(archive))
            using (var manifest = new MemoryStream(Encoding.UTF8.GetBytes(item.Manifest)))
                return Path.GetDirectoryName(EmbeddedBackend.EnsureBundleAvailable(Path.Combine(root, item.Id), payload, manifest, item.RequiredFile, cancellation));
        }
    }
}
