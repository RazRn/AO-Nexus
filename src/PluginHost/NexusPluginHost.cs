// AO Nexus plugin host.
//
// Does what AOSharp.exe's Inject/Eject buttons do, for every client Nexus asks it to:
// injects AOSharp.Bootstrap.dll with EasyHook, connects to the bootstrapper's named pipe
// (named after the game's process ID, exactly like AOSharp.exe) and sends the plugin list.
// The bootstrapper unloads every plugin when that pipe closes, so this process stays
// running while any client it injected is open. It runs apart from the Nexus window so
// restarting or updating Nexus never unloads plugins.
//
// Nexus talks to it over a per-user named pipe, one JSON request per connection.
// AOSharp's own files (EasyHook, the bootstrapper, Core) are loaded from the folder
// given with --aosharp; nothing from AOSharp is compiled into this executable.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace AONexus.PluginHost
{
    public sealed class HostRequest
    {
        public string Command { get; set; }
        public int Pid { get; set; }
        public long StartTicks { get; set; }
        public string Label { get; set; }
        public string[] Plugins { get; set; }
    }

    public sealed class HostClient
    {
        public string RequestId { get; set; }
        public int Pid { get; set; }
        public long StartTicks { get; set; }
        public string Label { get; set; }
        public string State { get; set; }
        public string Detail { get; set; }
        public string[] Plugins { get; set; }
        public DateTime ChangedUtc { get; set; }
    }

    public sealed class HostResponse
    {
        public bool Ok { get; set; }
        public string Error { get; set; }
        public string State { get; set; }
        public int HostPid { get; set; }
        public string AOSharpDirectory { get; set; }
        public List<HostClient> Clients { get; set; }
    }

    internal static class Program
    {
        internal const string ProtocolVersion = "v2";
        static string aosharpDirectory;
        static readonly object gate = new object();
        static readonly Dictionary<int, Managed> managed = new Dictionary<int, Managed>();
        static DateTime lastActivityUtc = DateTime.UtcNow;
        static string logPath;

        sealed class Managed
        {
            internal HostClient Info;
            internal object Pipe; // AOSharp.Bootstrap.IPC.IPCClient, loaded from the AOSharp folder
            internal Process Process;
        }

        internal static string PipeName()
        {
            using (var identity = WindowsIdentity.GetCurrent())
                return "AONexus.PluginHost." + ProtocolVersion + "." + identity.User.Value;
        }

        [STAThread]
        static int Main(string[] args)
        {
            string localData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AONexus", "PluginHost");
            Directory.CreateDirectory(localData);
            logPath = Path.Combine(localData, "host.log");
            for (int index = 0; index + 1 < args.Length; index++)
                if (args[index] == "--aosharp") aosharpDirectory = Path.GetFullPath(args[index + 1]);
            if (aosharpDirectory == null || !File.Exists(Path.Combine(aosharpDirectory, "AOSharp.Bootstrap.dll")) || !File.Exists(Path.Combine(aosharpDirectory, "EasyHook.dll")))
            {
                Log("Refusing to start: --aosharp must point to a folder with AOSharp.Bootstrap.dll and EasyHook.dll.");
                return 2;
            }
            AppDomain.CurrentDomain.AssemblyResolve += ResolveFromAOSharp;
            bool created;
            using (var single = new Mutex(true, @"Local\" + PipeName(), out created))
            {
                if (!created) { Log("Another plugin host is already running for this Windows user."); return 0; }
                Log("Plugin host started (pid " + Process.GetCurrentProcess().Id + ") using " + aosharpDirectory);
                var watchdog = new Thread(Watchdog) { IsBackground = true, Name = "watchdog" };
                watchdog.Start();
                Serve();
                Log("Plugin host stopped.");
            }
            return 0;
        }

        static Assembly ResolveFromAOSharp(object sender, ResolveEventArgs e)
        {
            string name = new AssemblyName(e.Name).Name;
            string candidate = Path.Combine(aosharpDirectory, name + ".dll");
            return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
        }

        static volatile bool stopping;

        static void Serve()
        {
            var security = new PipeSecurity();
            using (var identity = WindowsIdentity.GetCurrent())
                security.AddAccessRule(new PipeAccessRule(identity.User, PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
            while (!stopping)
            {
                using (var server = new NamedPipeServerStream(PipeName(), PipeDirection.InOut, 4, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 65536, 65536, security))
                {
                    var wait = server.BeginWaitForConnection(null, null);
                    while (!wait.AsyncWaitHandle.WaitOne(1000)) if (stopping) return;
                    try { server.EndWaitForConnection(wait); } catch (IOException) { continue; }
                    try { Handle(server); }
                    catch (Exception ex) { Log("Request failed: " + ex.Message); }
                }
            }
        }

        static void Handle(Stream stream)
        {
            var json = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024, RecursionLimit = 8 };
            var reader = new StreamReader(stream, new UTF8Encoding(false), false, 4096, true);
            string line = reader.ReadLine();
            if (line == null || line.Length > 512 * 1024) return;
            HostResponse response;
            try { response = Execute(json.Deserialize<HostRequest>(line)); }
            catch (Exception ex) { response = new HostResponse { Ok = false, Error = ex.Message }; }
            response.HostPid = Process.GetCurrentProcess().Id;
            response.AOSharpDirectory = aosharpDirectory;
            lock (gate) response.Clients = managed.Values.Select(item => item.Info).ToList();
            var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true);
            writer.WriteLine(json.Serialize(response));
            writer.Flush();
        }

        static HostResponse Execute(HostRequest request)
        {
            lastActivityUtc = DateTime.UtcNow;
            switch ((request?.Command ?? "").ToLowerInvariant())
            {
                case "status": return new HostResponse { Ok = true };
                case "inject": return Inject(request);
                case "eject": return Eject(request.Pid, request.StartTicks);
                case "shutdown":
                    lock (gate) if (managed.Values.Any(item => item.Info.State == "Loaded"))
                        return new HostResponse { Ok = false, Error = "Plugins are still loaded in open clients. Eject them first." };
                    stopping = true; return new HostResponse { Ok = true };
                default: return new HostResponse { Ok = false, Error = "Unknown command." };
            }
        }

        static HostResponse Inject(HostRequest request)
        {
            if (request.Plugins == null || request.Plugins.Length == 0) return new HostResponse { Ok = false, Error = "No plugins were selected." };
            if (request.Plugins.Length > 128) return new HostResponse { Ok = false, Error = "Too many plugins." };
            foreach (string plugin in request.Plugins)
                if (string.IsNullOrWhiteSpace(plugin) || !Path.IsPathRooted(plugin) || !plugin.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || !File.Exists(plugin))
                    return new HostResponse { Ok = false, Error = "Plugin file not found: " + plugin };
            Process process;
            try { process = Process.GetProcessById(request.Pid); }
            catch (ArgumentException) { return new HostResponse { Ok = false, Error = "The game client has closed." }; }
            try
            {
                // Same identity checks Nexus uses before touching a client: name and start time.
                if (!string.Equals(process.ProcessName, "AnarchyOnline", StringComparison.OrdinalIgnoreCase)
                    || process.StartTime.ToUniversalTime().Ticks != request.StartTicks || process.HasExited)
                { process.Dispose(); return new HostResponse { Ok = false, Error = "The process is not the expected game client." }; }
            }
            catch (Exception ex) { process.Dispose(); return new HostResponse { Ok = false, Error = "The game client could not be checked: " + ex.Message }; }

            lock (gate)
            {
                Managed existing;
                if (managed.TryGetValue(request.Pid, out existing) && existing.Info.StartTicks == request.StartTicks && existing.Info.State == "Loaded")
                {
                    // Already injected: the bootstrapper unloads the old plugins and loads the new list.
                    existing.Info.RequestId = Send(existing.Pipe, request.Plugins);
                    existing.Info.Plugins = request.Plugins; existing.Info.Label = request.Label; existing.Info.ChangedUtc = DateTime.UtcNow;
                    existing.Info.Detail = "Plugin list reloaded.";
                    process.Dispose();
                    Log("Reloaded " + request.Plugins.Length + " plugins in pid " + request.Pid + " (" + request.Label + ").");
                    return new HostResponse { Ok = true, State = "Loaded" };
                }
                if (existing != null) { existing.Process?.Dispose(); managed.Remove(request.Pid); }
            }

            // A pipe named after the PID means AOSharp's bootstrapper is already inside this client,
            // for example from AOSharp.exe. Injecting a second copy would hook the game twice.
            if (PipeExists(request.Pid.ToString()))
            {
                process.Dispose();
                return new HostResponse { Ok = false, State = "External", Error = "AO# is already loaded in this client by another program (for example AOSharp.exe). Eject it there first." };
            }

            var info = new HostClient { Pid = request.Pid, StartTicks = request.StartTicks, Label = request.Label, Plugins = request.Plugins, State = "Injecting", ChangedUtc = DateTime.UtcNow };
            try
            {
                string bootstrap = Path.Combine(aosharpDirectory, "AOSharp.Bootstrap.dll");
                InjectBootstrap(request.Pid, bootstrap);
                object pipe = Connect(request.Pid, info);
                info.RequestId = Send(pipe, request.Plugins);
                info.State = "Loaded"; info.Detail = request.Plugins.Length + " plugins sent."; info.ChangedUtc = DateTime.UtcNow;
                lock (gate) managed[request.Pid] = new Managed { Info = info, Pipe = pipe, Process = process };
                Log("Injected " + request.Plugins.Length + " plugins into pid " + request.Pid + " (" + request.Label + ").");
                return new HostResponse { Ok = true, State = "Loaded" };
            }
            catch (Exception ex)
            {
                process.Dispose();
                var inner = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                Log("Injection into pid " + request.Pid + " failed: " + inner.Message);
                return new HostResponse { Ok = false, State = "Failed", Error = inner.Message };
            }
        }

        static void InjectBootstrap(int pid, string bootstrap)
        {
            // EasyHook.RemoteHooking.Inject(int targetPid, string library32, string library64, params object[] args)
            var easyHook = Assembly.LoadFrom(Path.Combine(aosharpDirectory, "EasyHook.dll"));
            var remoteHooking = easyHook.GetType("EasyHook.RemoteHooking", true);
            var inject = remoteHooking.GetMethods(BindingFlags.Public | BindingFlags.Static).First(method =>
            {
                var parameters = method.GetParameters();
                return method.Name == "Inject" && parameters.Length == 4 && parameters[0].ParameterType == typeof(int)
                    && parameters[1].ParameterType == typeof(string) && parameters[2].ParameterType == typeof(string);
            });
            // The bootstrapper's channel name is the PID, matching AOSharp.exe.
            inject.Invoke(null, new object[] { pid, bootstrap, string.Empty, new object[] { pid.ToString() } });
        }

        static object Connect(int pid, HostClient info)
        {
            var bootstrap = Assembly.LoadFrom(Path.Combine(aosharpDirectory, "AOSharp.Bootstrap.dll"));
            var clientType = bootstrap.GetType("AOSharp.Bootstrap.IPC.IPCClient", true);
            object client = Activator.CreateInstance(clientType, pid.ToString());
            var disconnected = clientType.GetField("OnDisconnected");
            var handlerType = disconnected.FieldType; // Action<IPCClient>
            var callback = new Action<object>(_ => OnDisconnected(pid, info));
            var adapter = typeof(Program).GetMethod(nameof(Adapt), BindingFlags.NonPublic | BindingFlags.Static).MakeGenericMethod(clientType);
            disconnected.SetValue(client, adapter.Invoke(null, new object[] { callback }));
            clientType.GetMethod("Connect").Invoke(client, new object[] { 10000 });
            return client;
        }

        static Action<T> Adapt<T>(Action<object> callback) => value => callback(value);

        static string Send(object pipe, string[] plugins)
        {
            var bootstrap = pipe.GetType().Assembly;
            var messageType = bootstrap.GetType("AOSharp.Bootstrap.IPC.LoadAssemblyMessage", true);
            object message = Activator.CreateInstance(messageType);
            var requestField = messageType.GetField("RequestId");
            string requestId = requestField == null ? null : Guid.NewGuid().ToString("N");
            requestField?.SetValue(message, requestId);
            // In AOSharp's bootstrapper "Assemblies" is a public IEnumerable<string> field.
            var field = messageType.GetField("Assemblies");
            if (field != null) field.SetValue(message, plugins.ToList());
            else messageType.GetProperty("Assemblies").SetValue(message, plugins.ToList());
            var send = pipe.GetType().GetMethods().First(method => method.Name == "Send" && method.GetParameters().Length == 1
                && method.GetParameters()[0].ParameterType.Name == "IPCMessage");
            send.Invoke(pipe, new[] { message });
            return requestId;
        }

        static void OnDisconnected(int pid, HostClient info)
        {
            lock (gate)
            {
                if (info.State == "Ejected") return;
                info.State = "Disconnected"; info.Detail = "The client closed or AO# unloaded."; info.ChangedUtc = DateTime.UtcNow;
            }
            Log("Pipe to pid " + pid + " closed.");
        }

        static HostResponse Eject(int pid, long startTicks)
        {
            Managed item;
            string previousState, previousDetail;
            lock (gate)
            {
                if (!managed.TryGetValue(pid, out item)) return new HostResponse { Ok = false, Error = "Nexus did not load plugins into this client." };
                if (startTicks <= 0 || item.Info.StartTicks != startTicks)
                    return new HostResponse { Ok = false, Error = "The selected game client has closed or its process ID has been reused. Refresh the client list." };
                previousState = item.Info.State; previousDetail = item.Info.Detail;
                item.Info.State = "Ejected"; item.Info.Detail = "Plugins unloaded."; item.Info.ChangedUtc = DateTime.UtcNow;
            }
            // Breaking the pipe makes the bootstrapper tear down and unload every plugin.
            try { item.Pipe.GetType().GetMethod("Disconnect").Invoke(item.Pipe, null); }
            catch (Exception ex)
            {
                lock (gate) { item.Info.State = previousState; item.Info.Detail = previousDetail; }
                var cause = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                Log("Eject of pid " + pid + ": " + cause.Message);
                return new HostResponse { Ok = false, State = previousState, Error = "Plugins could not be unloaded: " + cause.Message };
            }
            Log("Ejected plugins from pid " + pid + ".");
            return new HostResponse { Ok = true, State = "Ejected" };
        }

        static bool PipeExists(string name)
        {
            try { return Directory.GetFiles(@"\\.\pipe\").Any(path => string.Equals(Path.GetFileName(path), name, StringComparison.OrdinalIgnoreCase)); }
            catch { return false; }
        }

        // Forgets closed clients and stops once nothing is loaded and Nexus has been quiet.
        static void Watchdog()
        {
            while (!stopping)
            {
                Thread.Sleep(5000);
                lock (gate)
                {
                    foreach (var pair in managed.ToArray())
                    {
                        bool exited;
                        try { exited = pair.Value.Process == null || pair.Value.Process.HasExited; } catch { exited = true; }
                        if (exited && (DateTime.UtcNow - pair.Value.Info.ChangedUtc).TotalMinutes > 1)
                        {
                            pair.Value.Process?.Dispose();
                            managed.Remove(pair.Key);
                        }
                        else if (exited && pair.Value.Info.State == "Loaded")
                        {
                            pair.Value.Info.State = "Disconnected"; pair.Value.Info.Detail = "The client closed."; pair.Value.Info.ChangedUtc = DateTime.UtcNow;
                        }
                    }
                    bool anyLoaded = managed.Values.Any(item => item.Info.State == "Loaded");
                    if (!anyLoaded && (DateTime.UtcNow - lastActivityUtc).TotalMinutes > 2) stopping = true;
                }
            }
        }

        static readonly object logGate = new object();
        internal static void Log(string message)
        {
            try
            {
                lock (logGate)
                {
                    if (File.Exists(logPath) && new FileInfo(logPath).Length > 1024 * 1024)
                    {
                        File.Copy(logPath, logPath + ".old", true);
                        File.Delete(logPath);
                    }
                    File.AppendAllText(logPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine);
                }
            }
            catch { }
        }
    }
}
