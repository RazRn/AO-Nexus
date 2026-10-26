using AOLauncher;
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
using System.Threading.Tasks;
using System.Web.Script.Serialization;

// An offline harness: all payload executables are inert bytes and are never executed.
internal static class EmbeddedBackendTests
{
    private static string testRoot;
    private static int checks;
    private sealed class Bundle
    {
        public byte[] Zip;
        public EmbeddedBackend.PayloadManifest Manifest;
        public string Hash { get { return Manifest.PayloadSha256; } }
        public byte[] Json { get { return Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(Manifest)); } }
    }

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 4 && args[0] == "--extract")
            {
                using (Stream zip = File.OpenRead(args[1]))
                using (Stream manifest = File.OpenRead(args[2]))
                    Console.WriteLine(EmbeddedBackend.EnsureAvailable(args[3], zip, manifest));
                return 0;
            }
            if (args.Length == 2 && args[0] == "--reparse")
            {
                ExpectRejected(() => Extract(args[1], GoodBundle()), "reparse cache ancestor");
                return 0;
            }
            if (args.Length == 3 && args[0] == "--embedded")
            {
                // Reflection loads only the extraction type, never Program/MainForm or personal settings.
                Assembly assembly = Assembly.LoadFrom(Path.GetFullPath(args[1]));
                Type type = assembly.GetType("AOLauncher.EmbeddedBackend", true);
                MethodInfo method = type.GetMethod("EnsureAvailable", BindingFlags.Static | BindingFlags.NonPublic,
                    null, new[] { typeof(string) }, null);
                string backend = (string)method.Invoke(null, new object[] { args[2] });
                if (!File.Exists(backend)) throw new Exception("Embedded backend was not extracted.");
                string repeated = (string)method.Invoke(null, new object[] { args[2] });
                if (backend != repeated) throw new Exception("Embedded payload did not reuse its verified cache.");
                Console.WriteLine("PASS: real embedded payload extracted and verified twice: " + backend);
                return 0;
            }
            if (args.Length != 1) throw new ArgumentException("Supply an isolated test directory.");
            testRoot = Path.GetFullPath(args[0]);
            Directory.CreateDirectory(testRoot);
            Run();
            Console.WriteLine("PASS: " + checks + " embedded-backend checks. No game or personal settings were accessed.");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }

    private static void Run()
    {
        Bundle bundle = GoodBundle();
        string cache = NewRoot("space and \u00e6\u00f8\u00e5");
        string executable = Extract(cache, bundle);
        Check(executable == Path.Combine(cache, bundle.Hash, "NexusEngine.exe"), "content-addressed path");
        Check(File.ReadAllText(executable) == "inert fake backend", "exact extracted content");
        Check(File.ReadAllText(Path.Combine(Path.GetDirectoryName(executable), "nested", "dependency.dll")) == "inert dependency", "nested DLL");
        DateTime originalWrite = File.GetLastWriteTimeUtc(executable);
        Check(Extract(cache, bundle) == executable && File.GetLastWriteTimeUtc(executable) == originalWrite, "verified cache reused without rewrite");

        Bundle next = GoodBundle("updated fake backend");
        using (var loadedDll = new FileStream(Path.Combine(Path.GetDirectoryName(executable), "LoginHandler.dll"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            string nextExe = Extract(cache, next);
            Check(nextExe != executable && File.Exists(executable), "new version leaves locked old runtime intact");
            Check(Extract(cache, bundle) == executable, "old version remains usable");
        }

        string corrupt = NewRoot("corrupt");
        string corruptExe = Extract(corrupt, bundle);
        File.WriteAllText(corruptExe, "modified");
        try { Extract(corrupt, bundle); throw new Exception("Accepted damaged cache."); }
        catch (IOException exception)
        {
            Check(exception.Message.Contains(Path.GetDirectoryName(corruptExe)) && exception.Message.Contains("rename"),
                "modified cache reports actionable preserved folder path");
        }
        Check(File.ReadAllText(corruptExe) == "modified", "damaged existing cache preserved");
        string extra = NewRoot("extra");
        string extraExe = Extract(extra, bundle);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(extraExe), "unexpected.dll"), "extra");
        ExpectRejected(() => Extract(extra, bundle), "unlisted executable cached file rejected");
        string missing = NewRoot("missing");
        string missingExe = Extract(missing, bundle);
        // Move only this inert fixture file; do not delete runtime versions.
        File.Move(Path.Combine(Path.GetDirectoryName(missingExe), "LoginHandler.dll"), Path.Combine(missing, "moved-fixture.dll"));
        ExpectRejected(() => Extract(missing, bundle), "missing cached dependency rejected");

        Bundle badHash = GoodBundle();
        badHash.Manifest.PayloadSha256 = new string('0', 64);
        ExpectRejected(() => Extract(NewRoot("payload-hash"), badHash), "wrong archive hash");
        Bundle badFileHash = GoodBundle();
        badFileHash.Manifest.Files[0].Sha256 = new string('0', 64);
        ExpectRejected(() => Extract(NewRoot("file-hash"), badFileHash), "wrong per-file hash");
        Bundle badLength = GoodBundle();
        badLength.Manifest.Files[0].Length++;
        ExpectRejected(() => Extract(NewRoot("file-length"), badLength), "wrong file length");
        Bundle missingExeManifest = GoodBundle();
        missingExeManifest.Manifest.Files = missingExeManifest.Manifest.Files.Skip(1).ToArray();
        ExpectRejected(() => Extract(NewRoot("no-backend"), missingExeManifest), "required executable missing from manifest");
        Bundle duplicateManifest = GoodBundle();
        duplicateManifest.Manifest.Files = duplicateManifest.Manifest.Files.Concat(new[] { duplicateManifest.Manifest.Files[0] }).ToArray();
        ExpectRejected(() => Extract(NewRoot("duplicate-manifest"), duplicateManifest), "duplicate manifest file");
        Bundle conflict = GoodBundle();
        conflict.Manifest.Files = conflict.Manifest.Files.Concat(new[] { new EmbeddedBackend.PayloadFile { Path = "nested", Sha256 = Hash(new byte[0]), Length = 0 } }).ToArray();
        ExpectRejected(() => Extract(NewRoot("manifest-conflict"), conflict), "file-directory manifest conflict");

        foreach (string unsafePath in new[] { "../outside.exe", "/absolute.exe", "D:/absolute.exe", "nested/../outside.dll", "nested\\outside.dll", "bad.dll:stream", "CON.dll", "nested/COM1", "trailing. ", "nested//x", "./x" })
        {
            Bundle malicious = GoodBundle();
            malicious.Manifest.Files[0].Path = unsafePath;
            ExpectRejected(() => Extract(NewRoot("unsafe-manifest"), malicious), "unsafe manifest path " + unsafePath);
            Bundle extraZip = GoodBundle();
            AddEntry(extraZip, unsafePath, "unlisted");
            ExpectRejected(() => Extract(NewRoot("unsafe-zip"), extraZip), "unsafe ZIP path " + unsafePath);
        }

        Bundle duplicateZip = GoodBundle();
        AddEntry(duplicateZip, "nexusengine.EXE", "inert fake backend");
        ExpectRejected(() => Extract(NewRoot("duplicate-zip"), duplicateZip), "case-insensitive ZIP duplicate");
        Bundle unlisted = GoodBundle();
        AddEntry(unlisted, "extra.dll", "extra");
        ExpectRejected(() => Extract(NewRoot("unlisted-zip"), unlisted), "unlisted ZIP entry");
        Bundle symlink = GoodBundle();
        ChangeEntry(symlink, "LoginHandler.dll", entry => entry.ExternalAttributes = unchecked((int)0xA1FF0000));
        ExpectRejected(() => Extract(NewRoot("symlink-zip"), symlink), "ZIP symbolic link");
        Bundle truncated = GoodBundle();
        truncated.Zip = truncated.Zip.Take(truncated.Zip.Length / 2).ToArray();
        truncated.Manifest.PayloadSha256 = Hash(truncated.Zip);
        ExpectRejected(() => Extract(NewRoot("truncated"), truncated), "truncated ZIP");
        Bundle absentZipFile = GoodBundle();
        ChangeEntry(absentZipFile, "LoginHandler.dll", entry => entry.Delete());
        ExpectRejected(() => Extract(NewRoot("incomplete"), absentZipFile), "manifest file absent from ZIP");
        Bundle directories = GoodBundle();
        AddEntry(directories, "nested/", "");
        Check(File.Exists(Extract(NewRoot("explicit-directory"), directories)), "valid explicit ZIP directory");

        string parallelRoot = NewRoot("threads");
        var tasks = Enumerable.Range(0, 8).Select(index => Task.Run(() => Extract(parallelRoot, bundle))).ToArray();
        Task.WaitAll(tasks);
        Check(tasks.All(task => task.Result == tasks[0].Result), "eight concurrent threads share one verified runtime");
        Check(Directory.GetDirectories(parallelRoot).Length == 1, "concurrent thread publication has no partial runtimes");

        string processRoot = NewRoot("processes");
        string zipPath = Path.Combine(testRoot, "process-payload.zip");
        string manifestPath = Path.Combine(testRoot, "process-manifest.json");
        File.WriteAllBytes(zipPath, bundle.Zip);
        File.WriteAllBytes(manifestPath, bundle.Json);
        string harness = Assembly.GetExecutingAssembly().Location;
        var processes = Enumerable.Range(0, 4).Select(index => Process.Start(new ProcessStartInfo {
            FileName = harness, Arguments = "--extract \"" + zipPath + "\" \"" + manifestPath + "\" \"" + processRoot + "\"",
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        })).ToArray();
        foreach (Process process in processes)
        {
            using (process)
            {
                if (!process.WaitForExit(15000)) throw new Exception("Concurrent extraction did not finish.");
                Check(process.ExitCode == 0, "concurrent process extraction: " + process.StandardError.ReadToEnd());
            }
        }
        Check(Directory.GetDirectories(processRoot).Length == 1, "four processes atomically publish one complete runtime");

        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            string cancelledRoot = NewRoot("cancelled");
            try { Extract(cancelledRoot, bundle, cancelled.Token); throw new Exception("Cancellation ignored."); }
            catch (OperationCanceledException) { Check(!Directory.Exists(cancelledRoot), "pre-cancelled extraction never creates cache"); }
        }
        string waitingRoot = NewRoot("cancel-wait");
        string user;
        using (WindowsIdentity identity = WindowsIdentity.GetCurrent()) user = identity.User == null ? identity.Name : identity.User.Value;
        string mutexName = "Local\\AOLauncher.Runtime." + Hash(Encoding.UTF8.GetBytes(waitingRoot.ToUpperInvariant() + "\n" + bundle.Hash + "\n" + user));
        using (var ownedMutex = new Mutex(true, mutexName))
        using (var cancellation = new CancellationTokenSource())
        {
            try
            {
                Task<string> blocked = Task.Run(() => Extract(waitingRoot, bundle, cancellation.Token));
                cancellation.CancelAfter(200);
                var watch = Stopwatch.StartNew();
                try { blocked.GetAwaiter().GetResult(); throw new Exception("Cancellation ignored while waiting for mutex."); }
                catch (OperationCanceledException) { Check(watch.ElapsedMilliseconds < 2000, "cancellation promptly interrupts mutex wait"); }
                Check(!Directory.Exists(Path.Combine(waitingRoot, bundle.Hash)), "cancelled wait does not publish a runtime");
            }
            finally { ownedMutex.ReleaseMutex(); }
        }
        Check(!Directory.EnumerateDirectories(testRoot, ".staging-*", SearchOption.AllDirectories).Any(), "failed extractions leave no owned staging directories");
    }

    private static string NewRoot(string name) { return Path.Combine(testRoot, name + "-" + Guid.NewGuid().ToString("N")); }
    private static string Extract(string root, Bundle bundle, CancellationToken cancellationToken = default(CancellationToken))
    {
        using (var zip = new MemoryStream(bundle.Zip, false))
        using (var manifest = new MemoryStream(bundle.Json, false))
            return EmbeddedBackend.EnsureAvailable(root, zip, manifest, cancellationToken);
    }
    private static Bundle GoodBundle(string backend = "inert fake backend")
    {
        var content = new Dictionary<string, string> {
            { "NexusEngine.exe", backend }, { "LoginHandler.dll", "inert fake login handler" },
            { "nested/dependency.dll", "inert dependency" }, { "NexusEngine.runtimeconfig.json", "{}" }
        };
        using (var bytes = new MemoryStream())
        {
            using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
                foreach (var entry in content)
                    using (var writer = new StreamWriter(zip.CreateEntry(entry.Key, CompressionLevel.Optimal).Open(), new UTF8Encoding(false))) writer.Write(entry.Value);
            byte[] payload = bytes.ToArray();
            return new Bundle { Zip = payload, Manifest = new EmbeddedBackend.PayloadManifest {
                PayloadSha256 = Hash(payload), Files = content.Select(entry => new EmbeddedBackend.PayloadFile {
                    Path = entry.Key, Length = Encoding.UTF8.GetByteCount(entry.Value), Sha256 = Hash(Encoding.UTF8.GetBytes(entry.Value))
                }).ToArray()
            } };
        }
    }
    private static void AddEntry(Bundle bundle, string name, string content)
    {
        using (var bytes = new MemoryStream())
        {
            bytes.Write(bundle.Zip, 0, bundle.Zip.Length);
            using (var zip = new ZipArchive(bytes, ZipArchiveMode.Update, true))
                using (var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false))) writer.Write(content);
            bundle.Zip = bytes.ToArray();
            bundle.Manifest.PayloadSha256 = Hash(bundle.Zip);
        }
    }
    private static void ChangeEntry(Bundle bundle, string name, Action<ZipArchiveEntry> change)
    {
        using (var bytes = new MemoryStream())
        {
            bytes.Write(bundle.Zip, 0, bundle.Zip.Length);
            using (var zip = new ZipArchive(bytes, ZipArchiveMode.Update, true)) change(zip.GetEntry(name));
            bundle.Zip = bytes.ToArray();
            bundle.Manifest.PayloadSha256 = Hash(bundle.Zip);
        }
    }
    private static string Hash(byte[] bytes) { using (SHA256 hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", ""); }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        checks++;
        Console.WriteLine("PASS: " + message);
    }
    private static void ExpectRejected(Action action, string message)
    {
        try { action(); }
        catch (Exception exception) when (exception is IOException || exception is InvalidDataException || exception is ArgumentException || exception is InvalidOperationException)
        { Check(true, message); return; }
        throw new Exception("FAIL: accepted " + message);
    }
}
