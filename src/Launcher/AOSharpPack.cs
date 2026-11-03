using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace AOLauncher
{
    internal sealed class PluginInfo
    {
        internal string Id, Name, Group, Path;
        // Suite: "Z" (main), "AOSP", "Classic" (standalone), "Nexus" or null (your own DLLs).
        // A set uses one of Z/AOSP: their profession handlers and managers would fight each other.
        internal string Suite, Settings, Note, About;
        internal string[] Commands = new string[0];
        internal bool Custom;
        public override string ToString() => Name;
    }

    // The AOSharp runtime and plugins Nexus injects. A local build can embed the whole pack
    // (AOSharp + bundled plugins) in AONexus.exe; otherwise Nexus uses a folder laid out like
    // AO#\dist (AOSharp\ and Plugins\) chosen in the Plugins window.
    internal sealed class AOSharpPack
    {
        internal string Root, AOSharpDirectory, Description;
        internal bool Embedded;
        internal List<PluginInfo> Plugins = new List<PluginInfo>();

        static AOSharpPack embedded;
        static bool embeddedChecked;
        static readonly object gate = new object();

        internal static bool HasEmbeddedPack => typeof(AOSharpPack).Assembly.GetManifestResourceInfo("AOLauncher.AOSharpPack.zip") != null;
        internal static string CacheRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AONexus", "AOSharp");

        // Returns null when no usable pack exists. Extraction is verified and happens once per session.
        internal static AOSharpPack Locate(LauncherSettings settings, out string error)
        {
            error = null;
            if (settings.AOSharpDisabled) { error = AOLauncher.Localization.Texts.Get("Text_AO_is_disabled_Enable_it_in_2B8ABE072BE3"); return null; }
            if (!string.IsNullOrWhiteSpace(settings.AOSharpPackPath))
            {
                var external = FromFolder(settings.AOSharpPackPath, out error);
                if (external != null) return external;
                if (!HasEmbeddedPack) return null;
            }
            if (!HasEmbeddedPack) { error = error ?? AOLauncher.Localization.Texts.Get("Text_AO_is_optional_Enable_it_in_2BD744B4BD86"); return null; }
            lock (gate)
            {
                if (embeddedChecked) { if (embedded == null) error = AOLauncher.Localization.Texts.Get("Text_The_built_in_AO_pack_could_9DD0A77F97E4"); return embedded; }
                embeddedChecked = true;
                try
                {
                    var assembly = typeof(AOSharpPack).Assembly;
                    using (var payload = assembly.GetManifestResourceStream("AOLauncher.AOSharpPack.zip"))
                    using (var manifest = assembly.GetManifestResourceStream("AOLauncher.AOSharpPack.manifest.json"))
                    {
                        string catalog = EmbeddedBackend.EnsureBundleAvailable(CacheRoot, payload, manifest, "plugins.json", CancellationToken.None);
                        embedded = FromFolder(Path.GetDirectoryName(catalog), out error);
                        if (embedded != null) { embedded.Embedded = true; embedded.Description = AOLauncher.Localization.Texts.Get("Text_Built_into_AO_Nexus_A9B71E152583") + " · " + embedded.Plugins.Count + " plugins"; }
                    }
                }
                catch (Exception ex) { error = ex.Message; FailureDiagnostics.Install.Report("plugin-pack-extract", "AO# pack extraction failed", ex); }
                return embedded;
            }
        }

        internal static AOSharpPack FromFolder(string folder, out string error)
        {
            error = null;
            try
            {
                string root = Path.GetFullPath(folder);
                // Accept the dist folder itself or its AOSharp subfolder.
                if (File.Exists(Path.Combine(root, "AOSharp.Bootstrap.dll")) && Directory.Exists(Path.Combine(root, "..", "Plugins")))
                    root = Path.GetFullPath(Path.Combine(root, ".."));
                string aosharp = Path.Combine(root, "AOSharp");
                foreach (string required in new[] { "AOSharp.Bootstrap.dll", "AOSharp.Core.dll", "EasyHook.dll", "EasyHook32.dll" })
                    if (!File.Exists(Path.Combine(aosharp, required)))
                    {
                        error = AOLauncher.Localization.Texts.Get("Text_This_folder_is_not_an_AO_33601CBFBBE2") + required;
                        return null;
                    }
                var pack = new AOSharpPack { Root = root, AOSharpDirectory = aosharp, Description = root };
                pack.Plugins = ReadCatalog(root);
                return pack;
            }
            catch (Exception ex) { error = ex.Message; FailureDiagnostics.Install.Report("plugin-pack-catalog", "Could not read plugin installation", ex); return null; }
        }

        // plugins.json (built packs) lists primary assemblies. A plain AO#\dist folder has none,
        // so the same names are found by the folder layout the AOSharp fork uses.
        static List<PluginInfo> ReadCatalog(string root)
        {
            var result = new List<PluginInfo>();
            string catalog = Path.Combine(root, "plugins.json");
            if (File.Exists(catalog))
            {
                var data = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(catalog));
                foreach (var item in ((System.Collections.ArrayList)data["Plugins"]).Cast<Dictionary<string, object>>())
                {
                    string id = (string)item["Id"], relative = ((string)item["Path"]).Replace('/', Path.DirectorySeparatorChar);
                    string path = Path.GetFullPath(Path.Combine(root, relative));
                    if (!path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) continue;
                    Func<string, string> text = key => item.TryGetValue(key, out var value) ? value as string : null;
                    var commands = item.TryGetValue("Commands", out var list) && list is System.Collections.ArrayList array ? array.OfType<string>().ToArray() : new string[0];
                    result.Add(new PluginInfo { Id = "pack:" + id, Name = text("Name") ?? DisplayName(id), Group = (string)item["Group"], Path = path,
                        Suite = text("Suite") ?? SuiteOf(id), Settings = text("Settings"), Note = text("Note"), About = text("About"), Commands = commands });
                }
                return result;
            }
            string plugins = Path.Combine(root, "Plugins");
            if (!Directory.Exists(plugins)) return result;
            foreach (string folder in Directory.GetDirectories(plugins))
            {
                string name = Path.GetFileName(folder);
                if (string.Equals(name, "AOSP", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (string dll in Directory.GetFiles(folder, "*.dll"))
                    {
                        string id = Path.GetFileNameWithoutExtension(dll);
                        string group = id.StartsWith("Automaton.") ? "Automatons" : id.StartsWith("Manager.") ? "Managers"
                            : id.StartsWith("ProfessionHandler.") && id != "ProfessionHandler.Generic" ? "Profession handlers" : id.StartsWith("Utility.") ? "Utilities" : null;
                        if (group != null) result.Add(new PluginInfo { Id = "pack:" + id, Name = DisplayName(id), Group = group, Path = dll, Suite = "AOSP" });
                    }
                }
                else if (File.Exists(Path.Combine(folder, name + ".dll")))
                    result.Add(new PluginInfo { Id = "pack:" + name, Name = name, Group = "Classic", Path = Path.Combine(folder, name + ".dll"), Suite = "Classic" });
            }
            return result;
        }

        // Older catalogs have no Suite field: AOSP ids are dotted (Manager.Sync), Z ids start with "Z.".
        internal static string SuiteOf(string id)
        {
            if (id == null) return null;
            if (id.StartsWith("pack:")) id = id.Substring(5);
            if (id.StartsWith("Z.")) return "Z";
            if (id.StartsWith("Nexus.")) return "Nexus";
            return id.Contains(".") ? "AOSP" : "Classic";
        }

        static string DisplayName(string id)
        {
            int dot = id.IndexOf('.');
            if (dot < 0) return id;
            string group = id.Substring(0, dot), name = id.Substring(dot + 1);
            return group == "ProfessionHandler" ? name + " handler" : group + " · " + name;
        }

        // Pack plugins, Nexus Health Monitor and plugins the user added themselves.
        internal static List<PluginInfo> AllPlugins(LauncherSettings settings, AOSharpPack pack)
        {
            var result = new List<PluginInfo>(pack?.Plugins ?? new List<PluginInfo>());
            string shortcuts = BundledShortcuts();
            if (shortcuts != null) {
                result.RemoveAll(p => p.Id == PluginSet.ShortcutsId);
                result.Add(new PluginInfo { Id=PluginSet.ShortcutsId, Name="Nexus Shortcuts", Group="AO Nexus", Suite="Nexus", Path=shortcuts,
                    About=AOLauncher.Localization.Texts.Get("Text_In_game_buttons_and_automatic_team_0EB939B4E0FB") });
            }
            try
            {
                string monitor = MonitorPlugin();
                if (monitor != null) result.Add(new PluginInfo { Id = PluginSet.HealthMonitorId, Name = "Nexus Health Monitor", Group = "AO Nexus", Path = monitor, Suite = "Nexus",
                    About = AOLauncher.Localization.Texts.Get("Text_Measures_game_responsiveness_for_the_client_DC1F2D3D4D5A") });
            }
            catch (Exception ex) { FailureDiagnostics.Install.Report("monitor-payload", "Could not prepare monitoring plugin", ex); }
            foreach (var custom in settings.CustomPlugins)
                result.Add(new PluginInfo { Id = custom.Id, Name = custom.Name, Group = AOLauncher.Localization.Texts.Get("Text_Your_plugins_ACAD0928E355"), Path = custom.Path, Custom = true });
            return result;
        }

        static string monitorPath;
        static string shortcutsPath;
        static readonly object shortcutsGate = new object();
        internal static string BundledShortcuts()
        {
            lock(shortcutsGate) {
                if(shortcutsPath!=null && File.Exists(shortcutsPath))return shortcutsPath;
                using(var stream=typeof(AOSharpPack).Assembly.GetManifestResourceStream("AOLauncher.NexusShortcuts.dll")) {
                    if(stream==null)return null;
                    byte[] bytes; using(var buffer=new MemoryStream()) {stream.CopyTo(buffer);bytes=buffer.ToArray();}
                    using(var sha=SHA256.Create()) {
                        string hash=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
                        string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AONexus","Shortcuts",hash);
                        Directory.CreateDirectory(folder);
                        string path=Path.Combine(folder,"NexusShortcuts.dll");
                        if(!File.Exists(path) || !sha.ComputeHash(File.ReadAllBytes(path)).SequenceEqual(sha.ComputeHash(bytes)))
                            File.WriteAllBytes(path,bytes);
                        shortcutsPath=path;return path;
                    }
                }
            }
        }
        static bool monitorChecked;
        static readonly object monitorGate = new object();

        // Preparing the monitoring files hashes and verifies the whole bundle, so it is done once per
        // session (again only if the file disappears), not each time the plugin list is built.
        internal static string MonitorPlugin()
        {
            lock (monitorGate)
            {
                if (monitorChecked && (monitorPath == null || File.Exists(monitorPath))) return monitorPath;
                // A failure throws before monitorChecked is set, so the next call tries again.
                string path = Path.Combine(EmbeddedMonitoring.EnsureAvailable(), "NexusHealthPlugin.dll");
                monitorPath = File.Exists(path) ? path : null;
                monitorChecked = true;
                return monitorPath;
            }
        }
    }

    // Talks to NexusPluginHost.exe (src/PluginHost), which owns the injected pipes.
    internal static class PluginHostClient
    {
        internal sealed class Request
        {
            public string Command { get; set; }
            public int Pid { get; set; }
            public long StartTicks { get; set; }
            public string Label { get; set; }
            public string[] Plugins { get; set; }
        }
        internal sealed class ClientState
        {
            public string RequestId { get; set; }
            public int Pid { get; set; }
            public long StartTicks { get; set; }
            public string Label { get; set; }
            public string State { get; set; }
            public string Detail { get; set; }
            public string[] Plugins { get; set; }
        }
        internal sealed class Response
        {
            public bool Ok { get; set; }
            public string Error { get; set; }
            public string State { get; set; }
            public int HostPid { get; set; }
            public string AOSharpDirectory { get; set; }
            public List<ClientState> Clients { get; set; }
        }

        static string PipeName()
        {
            using (var identity = WindowsIdentity.GetCurrent())
                return "AONexus.PluginHost.v2." + identity.User.Value;
        }

        static string hostPath;
        static readonly object gate = new object();

        // The host EXE is embedded in Nexus and extracted to a folder named by its checksum.
        static string HostExecutable()
        {
            lock (gate)
            {
                if (hostPath != null && File.Exists(hostPath)) return hostPath;
                byte[] bytes;
                using (var stream = typeof(PluginHostClient).Assembly.GetManifestResourceStream("AOLauncher.NexusPluginHost.exe"))
                {
                    if (stream == null) throw new InvalidDataException("The plugin host is missing from this AO Nexus build.");
                    using (var buffer = new MemoryStream()) { stream.CopyTo(buffer); bytes = buffer.ToArray(); }
                }
                string hash;
                using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AONexus", "PluginHost", hash.Substring(0, 16));
                string path = Path.Combine(folder, "NexusPluginHost.exe");
                if (!File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(bytes))
                {
                    Directory.CreateDirectory(folder);
                    string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    File.WriteAllBytes(temporary, bytes);
                    if (File.Exists(path)) File.Delete(path);
                    File.Move(temporary, path);
                }
                return hostPath = path;
            }
        }

        internal static Response Send(Request request, AOSharpPack pack, bool start, int timeoutMs = 20000)
            => Send(request, pack, start, timeoutMs, new PipeTransport(), FailureDiagnostics.Plugins);

        internal interface ITransport
        {
            Response Exchange(Request request, int connectTimeoutMs, int responseTimeoutMs);
            void Start(AOSharpPack pack);
        }

        sealed class PipeTransport : ITransport
        {
            public Response Exchange(Request request, int connectTimeoutMs, int responseTimeoutMs)
            {
                using (var pipe = new NamedPipeClientStream(".", PipeName(), PipeDirection.InOut))
                {
                    pipe.Connect(connectTimeoutMs);
                    var json = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 };
                    using (var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true))
                    {
                        writer.WriteLine(json.Serialize(request));
                        writer.Flush();
                        var read = Task.Run(() => new StreamReader(pipe, new UTF8Encoding(false), false, 4096, true).ReadLine());
                        if (!read.Wait(responseTimeoutMs))
                            return new Response { Ok = false, Error = "The plugin host did not answer in time." };
                        return read.Result == null
                            ? new Response { Ok = false, Error = "The plugin host closed the connection." }
                            : json.Deserialize<Response>(read.Result);
                    }
                }
            }

            public void Start(AOSharpPack pack)
            {
                using (Process.Start(new ProcessStartInfo(HostExecutable(), "--aosharp \"" + pack.AOSharpDirectory.TrimEnd('\\') + "\"")
                { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = pack.AOSharpDirectory })) { }
            }
        }

        // The same coordinator is used with an inert transport in unit tests.
        // Never log serialized requests: they can contain user-controlled paths.
        internal static Response Send(Request request, AOSharpPack pack, bool start, int timeoutMs,
            ITransport transport, FailureDiagnostics diagnostics)
        {
            string operation = "plugin-host/" + request.Command;
            return Nexus.Core.PluginDispatch.Send(
                connectTimeout => transport.Exchange(request, connectTimeout, timeoutMs),
                pack == null ? (Action)null : () => transport.Start(pack), start, request.Command == "status",
                response => response.Ok, response => response.Error,
                (error, state) => new Response { Ok = false, Error = error, State = state },
                (outcome, error, detail) => diagnostics.Report(operation, outcome, error, detail, request.Pid, request.StartTicks),
                () => diagnostics.Recovered(operation, "Request acknowledged; plugin initialization is checked separately", request.Pid, request.StartTicks));
        }
    }
}
