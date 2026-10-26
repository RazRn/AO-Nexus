using AOLauncher.Localization;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace AOLauncher
{
    /// <summary>Materializes the bundled backend without replacing DLLs that a running game may still use.</summary>
    internal static class EmbeddedBackend
    {
        private const int MaximumManifestBytes = 1024 * 1024;
        private const long MaximumPayloadBytes = 128L * 1024 * 1024;
        private const long MaximumExpandedBytes = 256L * 1024 * 1024;
        private const int MaximumFiles = 2048;
        private const string BackendFileName = "NexusEngine.exe";

        internal static string EnsureAvailable()
        {
            return EnsureAvailable(CancellationToken.None);
        }

        internal static string EnsureAvailable(CancellationToken cancellationToken)
        {
            return EnsureAvailable(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AONexus", "Runtime"), cancellationToken);
        }

        internal static string EnsureAvailable(string cacheRoot)
        {
            return EnsureAvailable(cacheRoot, CancellationToken.None);
        }

        internal static string EnsureAvailable(string cacheRoot, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assembly assembly = typeof(EmbeddedBackend).Assembly;
            using (Stream payload = assembly.GetManifestResourceStream("AOLauncher.Backend.zip"))
            using (Stream manifest = assembly.GetManifestResourceStream("AOLauncher.Backend.manifest.json"))
            {
                if (payload == null || manifest == null)
                    throw new InvalidDataException(Texts.T("The launcher is missing its bundled login files. Download the complete launcher again."));
                return EnsureAvailable(cacheRoot, payload, manifest, cancellationToken);
            }
        }

        // The stream overload also permits isolated tests without reading personal settings or launching a game.
        // Streams remain owned by the caller and are read from their current positions.
        internal static string EnsureAvailable(string cacheRoot, Stream payloadStream, Stream manifestStream,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return EnsureBundleAvailable(cacheRoot, payloadStream, manifestStream, BackendFileName, cancellationToken);
        }

        // Monitoring uses the same bounded, verified, immutable extraction path.
        internal static string EnsureBundleAvailable(string cacheRoot, Stream payloadStream, Stream manifestStream,
            string requiredFile, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(cacheRoot)) throw new ArgumentException("A cache folder is required.", nameof(cacheRoot));
            if (payloadStream == null) throw new ArgumentNullException(nameof(payloadStream));
            if (manifestStream == null) throw new ArgumentNullException(nameof(manifestStream));

            cancellationToken.ThrowIfCancellationRequested();
            PayloadManifest manifest = ReadManifest(manifestStream, cancellationToken);
            Dictionary<string, PayloadFile> files = ValidateManifest(manifest, requiredFile);
            using (var payload = new MemoryStream())
            {
                CopyBounded(payloadStream, payload, MaximumPayloadBytes, cancellationToken);
                payload.Position = 0;
                if (!string.Equals(Hash(payload, cancellationToken), manifest.PayloadSha256, StringComparison.OrdinalIgnoreCase))
                    throw InvalidBundle();
                payload.Position = 0;

                string root = Path.GetFullPath(cacheRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (root.Length == 2 && root[1] == ':') root += Path.DirectorySeparatorChar;
                EnsureNoReparseAncestors(root);
                Directory.CreateDirectory(root);
                EnsureNoReparseAncestors(root);
                string version = Path.Combine(root, manifest.PayloadSha256.ToUpperInvariant());
                string mutexIdentity;
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                    mutexIdentity = identity.User == null ? identity.Name : identity.User.Value;
                string mutexName = "Local\\AOLauncher.Runtime." + Hash(Encoding.UTF8.GetBytes(
                    root.ToUpperInvariant() + "\n" + manifest.PayloadSha256.ToUpperInvariant() + "\n" + mutexIdentity));

                using (var mutex = new Mutex(false, mutexName))
                {
                    bool acquired = false;
                    try
                    {
                        var wait = Stopwatch.StartNew();
                        do
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            try { acquired = mutex.WaitOne(100); }
                            catch (AbandonedMutexException) { acquired = true; }
                        } while (!acquired && wait.Elapsed < TimeSpan.FromSeconds(30));
                        if (!acquired)
                            throw new IOException(Texts.T("Another launcher is preparing the login files. Wait a moment and try again."));

                        cancellationToken.ThrowIfCancellationRequested();
                        EnsureNoReparseAncestors(version);
                        if (Directory.Exists(version) || File.Exists(version))
                        {
                            VerifyExistingVersion(version, files, cancellationToken);
                            return Path.Combine(version, requiredFile);
                        }

                        // A failed or interrupted extraction can never be mistaken for a complete version.
                        // Existing versions are immutable, including when the bundle changes after an update.
                        string staging = Path.Combine(root, ".staging-" + Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(staging);
                        try
                        {
                            EnsureNoReparseAncestors(staging);
                            Extract(payload, staging, files, cancellationToken);
                            VerifyDirectory(staging, files, cancellationToken);
                            cancellationToken.ThrowIfCancellationRequested();
                            EnsureNoReparseAncestors(root);
                            EnsureNoReparseAncestors(version);
                            Directory.Move(staging, version);
                            return Path.Combine(version, requiredFile);
                        }
                        finally
                        {
                            // Only our unpublished temporary directory is eligible for cleanup. Never remove
                            // a published version: LoginHandler.dll can remain loaded in an open game.
                            TryRemoveStaging(staging, root);
                        }
                    }
                    finally { if (acquired) mutex.ReleaseMutex(); }
                }
            }
        }

        private static PayloadManifest ReadManifest(Stream stream, CancellationToken cancellationToken)
        {
            try
            {
                using (var bytes = new MemoryStream())
                {
                    CopyBounded(stream, bytes, MaximumManifestBytes, cancellationToken);
                    string json = new UTF8Encoding(false, true).GetString(bytes.ToArray()).TrimStart('\uFEFF');
                    return new JavaScriptSerializer { MaxJsonLength = MaximumManifestBytes, RecursionLimit = 16 }
                        .Deserialize<PayloadManifest>(json);
                }
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException || exception is DecoderFallbackException)
            {
                throw InvalidBundle(exception);
            }
        }

        private static Dictionary<string, PayloadFile> ValidateManifest(PayloadManifest manifest, string requiredFile)
        {
            if (manifest == null || !IsHash(manifest.PayloadSha256) || manifest.Files == null ||
                manifest.Files.Length == 0 || manifest.Files.Length > MaximumFiles) throw InvalidBundle();
            var files = new Dictionary<string, PayloadFile>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (PayloadFile file in manifest.Files)
            {
                if (file == null || !IsSafeRelativePath(file.Path) || !IsHash(file.Sha256) ||
                    file.Length < 0 || file.Length > MaximumExpandedBytes || files.ContainsKey(file.Path)) throw InvalidBundle();
                total += file.Length;
                if (total > MaximumExpandedBytes) throw InvalidBundle();
                files.Add(file.Path, file);
            }
            if (!IsSafeRelativePath(requiredFile) || !files.ContainsKey(requiredFile)) throw InvalidBundle();
            foreach (string path in files.Keys)
            {
                int slash = path.IndexOf('/');
                while (slash >= 0)
                {
                    if (files.ContainsKey(path.Substring(0, slash))) throw InvalidBundle();
                    slash = path.IndexOf('/', slash + 1);
                }
            }
            return files;
        }

        private static void Extract(Stream payload, string staging, Dictionary<string, PayloadFile> files, CancellationToken cancellationToken)
        {
            using (var zip = new ZipArchive(payload, ZipArchiveMode.Read, true))
            {
                if (zip.Entries.Count > MaximumFiles * 2) throw InvalidBundle();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var extracted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    bool directory = entry.FullName.EndsWith("/", StringComparison.Ordinal);
                    string relative = directory ? entry.FullName.Substring(0, entry.FullName.Length - 1) : entry.FullName;
                    int unixType = (entry.ExternalAttributes >> 16) & 0xF000;
                    if (!IsSafeRelativePath(relative) || !seen.Add(relative) || unixType == 0xA000 ||
                        (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0) throw InvalidBundle();
                    if (directory)
                    {
                        if (entry.Length != 0 || files.ContainsKey(relative) ||
                            !files.Keys.Any(path => path.StartsWith(relative + "/", StringComparison.OrdinalIgnoreCase))) throw InvalidBundle();
                        continue;
                    }
                    PayloadFile expected;
                    if (!files.TryGetValue(relative, out expected) || entry.Length != expected.Length) throw InvalidBundle();
                    string destination = CombineInside(staging, expected.Path);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    EnsureNoReparseAncestors(destination);
                    using (Stream input = entry.Open())
                    using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        CopyBounded(input, output, expected.Length, cancellationToken);
                    VerifyFile(destination, expected, cancellationToken);
                    extracted.Add(relative);
                }
                if (extracted.Count != files.Count) throw InvalidBundle();
            }
        }

        private static void VerifyExistingVersion(string version, Dictionary<string, PayloadFile> files, CancellationToken cancellationToken)
        {
            try { VerifyDirectory(version, files, cancellationToken); }
            catch (Exception exception) when (exception is IOException || exception is InvalidDataException || exception is UnauthorizedAccessException)
            {
                throw new IOException(Texts.F("Cached login files are damaged or have been changed. Close all AO game windows, then rename this folder and restart AO Nexus:\n{0}", version), exception);
            }
        }

        private static void VerifyDirectory(string directory, Dictionary<string, PayloadFile> files, CancellationToken cancellationToken)
        {
            EnsureNoReparseAncestors(directory);
            if (!Directory.Exists(directory)) throw InvalidBundle();
            var actual = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int entryCount = 0;
            CollectFiles(directory, directory, actual, cancellationToken, 0, ref entryCount);
            if (actual.Count != files.Count || actual.Any(path => !files.ContainsKey(path))) throw InvalidBundle();
            foreach (PayloadFile file in files.Values) VerifyFile(CombineInside(directory, file.Path), file, cancellationToken);
        }

        private static void CollectFiles(string directory, string root, HashSet<string> files,
            CancellationToken cancellationToken, int depth, ref int entryCount)
        {
            if (depth > 128) throw InvalidBundle();
            foreach (string path in Directory.EnumerateFileSystemEntries(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++entryCount > MaximumFiles * 2) throw InvalidBundle();
                FileAttributes attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw UnsafeCache(path);
                if ((attributes & FileAttributes.Directory) != 0) CollectFiles(path, root, files, cancellationToken, depth + 1, ref entryCount);
                else if (!files.Add(path.Substring(root.Length + 1).Replace(Path.DirectorySeparatorChar, '/'))) throw InvalidBundle();
            }
        }

        private static void VerifyFile(string path, PayloadFile expected, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureNoReparseAncestors(path);
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                if (input.Length != expected.Length || !string.Equals(Hash(input, cancellationToken), expected.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw InvalidBundle();
        }

        private static string CombineInside(string root, string relative)
        {
            string destination = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!destination.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw InvalidBundle();
            return destination;
        }

        private static bool IsSafeRelativePath(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Length > 220 || path.IndexOf('\\') >= 0 || Path.IsPathRooted(path)) return false;
            foreach (string segment in path.Split('/'))
            {
                if (segment.Length == 0 || segment == "." || segment == ".." || segment.EndsWith(".", StringComparison.Ordinal) ||
                    segment.EndsWith(" ", StringComparison.Ordinal) || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
                string stem = segment.Split('.')[0].ToUpperInvariant();
                if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL" || stem == "CLOCK$" ||
                    (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) &&
                        ((stem[3] >= '0' && stem[3] <= '9') || stem[3] == '\u00b9' || stem[3] == '\u00b2' || stem[3] == '\u00b3'))) return false;
            }
            return true;
        }

        private static void EnsureNoReparseAncestors(string path)
        {
            for (string current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            {
                try
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw UnsafeCache(current);
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
            }
        }

        private static void TryRemoveStaging(string staging, string root)
        {
            try
            {
                if (!string.Equals(Path.GetDirectoryName(staging), root, StringComparison.OrdinalIgnoreCase) ||
                    !Path.GetFileName(staging).StartsWith(".staging-", StringComparison.Ordinal) || !Directory.Exists(staging)) return;
                EnsureNoReparseAncestors(staging);
                int entryCount = 0;
                CollectFiles(staging, staging, new HashSet<string>(StringComparer.OrdinalIgnoreCase), CancellationToken.None, 0, ref entryCount);
                Directory.Delete(staging, true);
            }
            catch (IOException) { }
            catch (InvalidDataException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static void CopyBounded(Stream input, Stream output, long maximum, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                total += read;
                if (total > maximum) throw InvalidBundle();
                output.Write(buffer, 0, read);
            }
        }

        private static bool IsHash(string value)
        {
            return value != null && value.Length == 64 && value.All(character =>
                (character >= '0' && character <= '9') || (character >= 'A' && character <= 'F') || (character >= 'a' && character <= 'f'));
        }

        private static string Hash(byte[] bytes)
        {
            using (var input = new MemoryStream(bytes, false)) return Hash(input);
        }

        private static string Hash(Stream stream, CancellationToken cancellationToken = default(CancellationToken))
        {
            using (SHA256 hash = SHA256.Create())
            {
                byte[] buffer = new byte[81920];
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    hash.TransformBlock(buffer, 0, read, buffer, 0);
                }
                cancellationToken.ThrowIfCancellationRequested();
                hash.TransformFinalBlock(buffer, 0, 0);
                return BitConverter.ToString(hash.Hash).Replace("-", "");
            }
        }

        private static InvalidDataException InvalidBundle(Exception inner = null)
        {
            return new InvalidDataException(Texts.T("The bundled login files failed verification. Download the complete launcher again."), inner);
        }

        private static IOException UnsafeCache(string path)
        {
            return new IOException(Texts.F("The login cache cannot use a linked file or folder: {0}", path));
        }

        internal sealed class PayloadManifest
        {
            public PayloadManifest() { }
            public string PayloadSha256 { get; set; }
            public PayloadFile[] Files { get; set; }
        }

        internal sealed class PayloadFile
        {
            public PayloadFile() { }
            public string Path { get; set; }
            public string Sha256 { get; set; }
            public long Length { get; set; }
        }
    }
}
