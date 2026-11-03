using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using AOSharp.Core;
using AOSharp.Core.UI;

[assembly: AssemblyTitle("Nexus Shortcuts")]
[assembly: AssemblyProduct("Nexus Shortcuts")]
[assembly: AssemblyDescription("Lets AO Nexus run plugin chat commands (such as /form or /handler) in this client and see which plugins started")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.5.0.0")]

namespace NexusShortcuts
{
    // AO Nexus buttons such as "Form team" or "Open handler settings" run the same chat command
    // you would type. Nexus connects to a pipe named after this client's process ID; only the
    // Windows user running the game can open it.
    //
    // Only commands another plugin has registered (AOSharp's own command table) can run: never
    // game chat, tells or anything typed to other players. Commands run on the game's update
    // thread, exactly as if typed, at most a few per frame.
    //
    // Protocol, one UTF-8 line per connection:  LIST  ->  OK <command> <command> ...
    //                                           RUN <command> [args]  ->  OK | UNKNOWN | BUSY | ERROR <text>
    //                                           WHO   ->  OK <profession> <name> | WAIT
    //                                           HEALTH  ->  OK <json> | WAIT
    //
    // HEALTH exists because AOSharp swallows every error while it loads and starts plugins: a
    // broken plugin simply does nothing. Nexus loads this plugin first, so it sees the others
    // load and start, and reports per plugin: loaded or not, the commands and game events it
    // hooked (proof that it started), the error that stopped it, and exceptions observed.
    // First-chance notifications precede catch handlers; they do not prove plugin failure.
    // It only reads; it never changes another plugin.
    public sealed class NexusShortcutsPlugin : AOPluginEntry
    {
        internal const int MaxLine = 2560;
        const int MaxErrorAnalysesPerSecond = 25;

        sealed class Fault
        {
            internal int Errors;
            internal string Last, StartError;
        }

        static int watching;
        [ThreadStatic] static bool inWatch;
        static readonly ConcurrentDictionary<string, Fault> faults = new ConcurrentDictionary<string, Fault>(StringComparer.OrdinalIgnoreCase);
        static readonly ConcurrentQueue<string> loadFaults = new ConcurrentQueue<string>();
        static long budgetSecond; static int budgetUsed;

        // Runs while AOSharp creates this plugin, before it loads the plugins listed after it.
        public NexusShortcutsPlugin()
        {
            if (Interlocked.Exchange(ref watching, 1) == 0)
                AppDomain.CurrentDomain.FirstChanceException += Watch;
        }

        volatile string who, health;
        float sinceSnapshot = 99;
        FieldInfo[] eventFields;
        readonly ConcurrentQueue<string> pending = new ConcurrentQueue<string>();
        readonly ManualResetEvent stopping = new ManualResetEvent(false);
        Thread server;
        Dictionary<string, Action<string, string[], ChatWindow>> commands;
        ChatWindow output;
        string pipeName;
        NexusPluginUi pluginUi;
        NexusTools playTools;
        NexusAutoTeam autoTeam;
        CastPostureGuard postureGuard;

        public override void Run()
        {
            try
            {
                commands = (Dictionary<string, Action<string, string[], ChatWindow>>)typeof(Chat)
                    .GetField("_customCommands", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
                // No window pointer: ChatWindow.WriteLine then writes to the normal chat output.
                output = (ChatWindow)typeof(ChatWindow).GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(IntPtr) }, null).Invoke(new object[] { IntPtr.Zero });
            }
            catch (Exception ex) { Chat.WriteLine("Nexus Shortcuts: this AO# version is not supported (" + ex.Message + ")."); return; }
            eventFields = EventFields();
            autoTeam = new NexusAutoTeam();
            postureGuard = new CastPostureGuard();
            pluginUi = new NexusPluginUi(commands);
            try { playTools = new NexusTools(commands); pluginUi.OpenTools = () => playTools.Open(); }
            catch (Exception ex) { Chat.WriteLine("Nexus tools unavailable: " + ex.Message); }
            using (var process = Process.GetCurrentProcess()) pipeName = PipeName(process.Id);
            Game.OnUpdate += Update;
            server = new Thread(Serve) { IsBackground = true, Name = "Nexus Shortcuts" };
            server.Start();
        }

        public static string PipeName(int pid) => "AONexus.Shortcuts." + pid;

        public override void Teardown()
        {
            Game.OnUpdate -= Update;
            autoTeam?.Dispose();
            postureGuard?.Dispose();
            pluginUi?.Dispose();
            playTools?.Dispose();
            stopping.Set();
            server?.Join(2000);
        }

        void Update(object sender, float delta)
        {
            postureGuard?.Update();
            autoTeam?.Update();
            pluginUi?.Update(delta);
            try { playTools?.Update(delta); } catch (Exception) { }
            // Game state is read here, on the game thread; the pipe thread only returns copies.
            sinceSnapshot += delta;
            if (sinceSnapshot >= 2f)
            {
                sinceSnapshot = 0;
                try
                {
                    var player = DynelManager.LocalPlayer;
                    if (player != null) who = player.Profession + " " + player.Name;
                    health = Snapshot();
                }
                catch (Exception) { }
            }
            for (int i = 0; i < 4 && pending.TryDequeue(out string line); i++)
            {
                string[] parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0 || !commands.TryGetValue(parts[0], out var command) || command == null) continue;
                try { command(parts[0], parts.Skip(1).ToArray(), output); }
                catch (Exception ex) { Chat.WriteLine("Nexus Shortcuts: /" + parts[0] + " failed: " + ex.Message); }
            }
        }

        static PipeSecurity CurrentUserOnly()
        {
            var security = new PipeSecurity();
            using (var identity = WindowsIdentity.GetCurrent())
                security.AddAccessRule(new PipeAccessRule(identity.User, PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
            return security;
        }

        void Serve()
        {
            while (!stopping.WaitOne(0))
            {
                try
                {
                    using (var pipe = CreatePipe())
                    {
                        var wait = pipe.BeginWaitForConnection(null, null);
                        if (WaitHandle.WaitAny(new[] { wait.AsyncWaitHandle, stopping }) == 1) return;
                        pipe.EndWaitForConnection(wait);
                        string request = ReadLine(pipe);
                        byte[] reply = Encoding.UTF8.GetBytes(Handle(request) + "\n");
                        pipe.Write(reply, 0, reply.Length);
                        pipe.Flush();
                        try { pipe.WaitForPipeDrain(); } catch { }
                    }
                }
                catch (Exception) { if (stopping.WaitOne(250)) return; }
            }
        }

        // A method of its own, so a runtime without these pipe APIs fails inside Serve's try block.
        NamedPipeServerStream CreatePipe() =>
            new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 1024, 1024, CurrentUserOnly());

        static string ReadLine(Stream stream)
        {
            var bytes = new List<byte>();
            var buffer = new byte[1];
            var started = DateTime.UtcNow;
            while (bytes.Count < MaxLine && (DateTime.UtcNow - started).TotalSeconds < 3)
            {
                int read = stream.Read(buffer, 0, 1);
                if (read <= 0 || buffer[0] == (byte)'\n') break;
                bytes.Add(buffer[0]);
            }
            return Encoding.UTF8.GetString(bytes.ToArray()).Trim();
        }

        string Handle(string request)
        {
            if(request=="FORMATIONSTATUS")return playTools?.FormationReport??"WAIT";
            if(request.StartsWith("FORMATION ",StringComparison.Ordinal))return playTools?.QueueFormation(request.Substring(10))??"WAIT";
            if (request == "TEAMSTATUS") return autoTeam?.Status ?? "WAIT";
            if (request == "TEAMVIEW") return autoTeam?.View ?? "WAIT";
            if (request.StartsWith("TEAMLEADER ", StringComparison.Ordinal)) return autoTeam?.QueueLeader(request) ?? "WAIT";
            if (request == "TEAMRECOVERYSTATUS") return autoTeam?.RecoveryStatus ?? "WAIT";
            if (request.StartsWith("TEAMRESTORE ", StringComparison.Ordinal)) return autoTeam?.QueueRecovery(request) ?? "WAIT";
            if (request.StartsWith("TEAMCANCEL ", StringComparison.Ordinal)) return autoTeam?.Cancel(request) ?? "WAIT";
            if (request.StartsWith("TEAMPLAN ", StringComparison.Ordinal)) return autoTeam?.Queue(request) ?? "WAIT";
            if (string.Equals(request, "WHO", StringComparison.Ordinal)) return who != null ? "OK " + who : "WAIT";
            if (string.Equals(request, "HEALTH", StringComparison.Ordinal)) return health != null ? "OK " + health : "WAIT";
            if (string.Equals(request, "LIST", StringComparison.Ordinal))
            {
                // Plugins register commands on the game thread; a copy taken mid-change is retried.
                for (int attempt = 0; attempt < 3; attempt++)
                    try { return "OK " + string.Join(" ", commands.Keys.ToArray().Where(Safe).OrderBy(key => key, StringComparer.OrdinalIgnoreCase)); }
                    catch (InvalidOperationException) { Thread.Sleep(20); }
                return "BUSY";
            }
            if (!request.StartsWith("RUN ", StringComparison.Ordinal)) return "ERROR unknown request";
            string line = request.Substring(4).Trim().TrimStart('/');
            if (line.Length == 0 || line.Any(char.IsControl)) return "ERROR empty command";
            string name = line.Split(' ')[0];
            bool known;
            try { known = commands.ContainsKey(name); } catch (InvalidOperationException) { return "BUSY"; }
            if (!Safe(name) || !known) return "UNKNOWN " + name;
            if (pending.Count >= 16) return "BUSY";
            pending.Enqueue(line);
            return "OK";
        }

        // Command names are plain words; anything else is refused.
        static bool Safe(string name) => name.Length > 0 && name.Length <= 40 && name.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-');

        // ---- Plugin health -------------------------------------------------------------------

        static void Watch(object sender, FirstChanceExceptionEventArgs e)
        {
            if (inWatch) return;
            inWatch = true;
            try
            {
                var exception = e.Exception;
                var stack = new StackTrace(1, false);
                bool starting = HasFrame(stack, "AOSharp.Bootstrap.Plugin", "Initialize");
                bool loading = HasFrame(stack, "AOSharp.Bootstrap.PluginProxy", "LoadPlugin");
                // AOSharp calls each plugin's constructor and Init through reflection: an exception
                // that escapes them arrives wrapped, and AOSharp then drops it without a word.
                if (exception is TargetInvocationException wrapped && wrapped.InnerException != null && (starting || loading))
                {
                    var inner = wrapped.InnerException;
                    string owner = Owner(new StackTrace(inner, false)) ?? "?";
                    faults.GetOrAdd(owner, key => new Fault()).StartError = (loading ? "could not be created: " : "failed to start: ") + Describe(inner);
                    return;
                }
                if (IsOptionalSettingsLookup(exception, stack)) return;
                if (loading)
                {
                    // AOSharp first asks for each reference by name and, when that fails, loads it from the
                    // plugin's folder: the first FileNotFoundException is normal.
                    if (exception is FileNotFoundException && !HasFrame(stack, "System.Reflection.Assembly", "LoadFrom")) return;
                    if (loadFaults.Count < 20) loadFaults.Enqueue(Describe(exception));
                    return;
                }
                long second = DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond;
                if (Interlocked.Exchange(ref budgetSecond, second) != second) Interlocked.Exchange(ref budgetUsed, 0);
                if (Interlocked.Increment(ref budgetUsed) > MaxErrorAnalysesPerSecond) return;
                string culprit = Owner(stack);
                if (culprit == null) return;
                var fault = faults.GetOrAdd(culprit, key => new Fault());
                Interlocked.Increment(ref fault.Errors);
                fault.Last = Describe(exception);
            }
            catch (Exception) { }
            finally { inWatch = false; }
        }

        static bool IsOptionalSettingsLookup(Exception exception, StackTrace stack)
        {
            // The packaged AOSharp Settings.Load catches a missing file/directory and installs
            // an empty dictionary; AddVariable then supplies defaults. Only exempt that direct
            // file read. Bad JSON, denied access, missing DLLs and other plugin IO still count.
            if (!(exception is FileNotFoundException) && !(exception is DirectoryNotFoundException)) return false;
            bool read = false;
            foreach (var frame in stack.GetFrames() ?? new StackFrame[0])
            {
                var method = frame.GetMethod();
                if (method == null) continue;
                if (method.DeclaringType == typeof(File) && method.Name == "ReadAllText") { read = true; continue; }
                if (!read) continue;
                return method.DeclaringType == typeof(Settings) && method.Name == "Load";
            }
            return false;
        }

        static bool HasFrame(StackTrace stack, string type, string method)
        {
            foreach (var frame in stack.GetFrames() ?? new StackFrame[0])
            {
                var info = frame.GetMethod();
                if (info != null && info.Name == method && info.DeclaringType != null && info.DeclaringType.FullName == type) return true;
            }
            return false;
        }

        // The first plugin assembly on the stack: not .NET, not AOSharp itself, not this plugin.
        static string Owner(StackTrace stack)
        {
            foreach (var frame in stack.GetFrames() ?? new StackFrame[0])
            {
                var assembly = frame.GetMethod()?.DeclaringType?.Assembly;
                if (assembly != null && IsPluginCode(assembly)) return assembly.GetName().Name;
            }
            return null;
        }

        static string aosharpFolder;
        static bool IsPluginCode(Assembly assembly)
        {
            if (assembly.IsDynamic || assembly.GlobalAssemblyCache || assembly == typeof(NexusShortcutsPlugin).Assembly) return false;
            string location;
            try { location = assembly.Location; } catch (Exception) { return false; }
            if (string.IsNullOrEmpty(location)) return false;
            if (aosharpFolder == null) aosharpFolder = Path.GetDirectoryName(typeof(Game).Assembly.Location);
            return !string.Equals(Path.GetDirectoryName(location), aosharpFolder, StringComparison.OrdinalIgnoreCase);
        }

        static string Describe(Exception exception)
        {
            if (exception is ReflectionTypeLoadException types && types.LoaderExceptions != null && types.LoaderExceptions.Any(item => item != null))
                exception = types.LoaderExceptions.First(item => item != null);
            string text = exception.GetType().Name + ": " + exception.Message;
            if (exception is FileNotFoundException missing && !string.IsNullOrEmpty(missing.FileName) && text.IndexOf(missing.FileName, StringComparison.OrdinalIgnoreCase) < 0) text += " (" + missing.FileName + ")";
            text = new string(text.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
            return text.Length > 300 ? text.Substring(0, 300) + "…" : text;
        }

        // Static delegate fields of the AOSharp classes that run every frame (Game.OnUpdate and the
        // like): a plugin that subscribed to one has started. Only classes AOSharp has already
        // initialised are read, so reading them runs no code.
        static FieldInfo[] EventFields()
        {
            var core = typeof(Game).Assembly;
            var names = new[] { "AOSharp.Core.Game", "AOSharp.Core.DynelManager", "AOSharp.Core.Network", "AOSharp.Core.Item", "AOSharp.Core.PerkAction",
                "AOSharp.Core.Spell", "AOSharp.Core.UI.Chat", "AOSharp.Core.Team", "AOSharp.Core.MiscClientEvents", "AOSharp.Core.Inventory.Inventory" };
            var result = new List<FieldInfo>();
            foreach (string name in names)
            {
                var type = core.GetType(name, false);
                if (type == null) continue;
                result.AddRange(type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(field => typeof(Delegate).IsAssignableFrom(field.FieldType)));
            }
            return result.ToArray();
        }

        readonly Dictionary<Assembly, bool> entryAssemblies = new Dictionary<Assembly, bool>();

        string Snapshot()
        {
            var hooks = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in eventFields)
            {
                Delegate value;
                try { value = field.GetValue(null) as Delegate; } catch (Exception) { continue; }
                if (value == null) continue;
                foreach (var target in value.GetInvocationList())
                {
                    var assembly = target.Method?.DeclaringType?.Assembly;
                    if (assembly == null || !IsPluginCode(assembly)) continue;
                    string key = assembly.GetName().Name;
                    hooks[key] = (hooks.TryGetValue(key, out int count) ? count : 0) + 1;
                }
            }
            var owned = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in commands.ToArray())
            {
                var assembly = pair.Value?.Method?.DeclaringType?.Assembly;
                if (assembly == null || !IsPluginCode(assembly) || !Safe(pair.Key)) continue;
                string key = assembly.GetName().Name;
                if (!owned.TryGetValue(key, out var list)) owned[key] = list = new List<string>();
                list.Add(pair.Key);
            }
            var json = new StringBuilder("{\"who\":").Append(Text(who)).Append(",\"plugins\":[");
            bool first = true;
            var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!IsPluginCode(assembly)) continue;
                if (!entryAssemblies.TryGetValue(assembly, out bool entry))
                {
                    try { entry = assembly.GetExportedTypes().Any(type => !type.IsAbstract && type.GetInterface("AOSharp.Core.IAOPluginEntry") != null); }
                    catch (Exception) { entry = true; }
                    entryAssemblies[assembly] = entry;
                }
                string name = assembly.GetName().Name;
                faults.TryGetValue(name, out var fault);
                // Helper libraries next to a plugin are listed only when they had errors.
                if (!entry && fault == null) continue;
                reported.Add(name);
                if (!first) json.Append(',');
                first = false;
                json.Append("{\"a\":").Append(Text(name)).Append(",\"f\":").Append(Text(assembly.Location)).Append(",\"entry\":").Append(entry ? "true" : "false")
                    .Append(",\"c\":[").Append(string.Join(",", (owned.TryGetValue(name, out var list) ? list : new List<string>()).Select(Text))).Append(']')
                    .Append(",\"h\":").Append(hooks.TryGetValue(name, out int hooked) ? hooked : 0)
                    .Append(",\"s\":").Append(Text(fault?.StartError)).Append(",\"e\":").Append(fault?.Errors ?? 0).Append(",\"m\":").Append(Text(fault?.Last)).Append('}');
            }
            foreach (var pair in faults.Where(item => !reported.Contains(item.Key) && item.Value.StartError != null))
            {
                if (!first) json.Append(',');
                first = false;
                json.Append("{\"a\":").Append(Text(pair.Key)).Append(",\"f\":null,\"entry\":true,\"c\":[],\"h\":0,\"s\":").Append(Text(pair.Value.StartError))
                    .Append(",\"e\":").Append(pair.Value.Errors).Append(",\"m\":").Append(Text(pair.Value.Last)).Append('}');
            }
            json.Append("],\"load\":[").Append(string.Join(",", loadFaults.ToArray().Distinct().Select(Text))).Append(']')
                .Append(",\"postureRejections\":").Append(postureGuard?.Rejections ?? 0)
                .Append(",\"postureCorrections\":").Append(postureGuard?.Corrections ?? 0).Append('}');
            return json.ToString();
        }

        static string Text(string value)
        {
            if (value == null) return "null";
            var text = new StringBuilder("\"");
            foreach (char c in value)
            {
                if (c == '"' || c == '\\') text.Append('\\').Append(c);
                else if (c < ' ') text.Append("\\u").Append(((int)c).ToString("x4"));
                else text.Append(c);
            }
            return text.Append('"').ToString();
        }
    }
}
