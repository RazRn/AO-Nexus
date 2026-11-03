using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
class PluginReadinessTests
{
    static int checks;
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
    static int Main(string[] args) {
        try {
            var assembly = Assembly.LoadFrom(args[0]);
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
            var policy = assembly.GetType("AOLauncher.PluginAutoInjector").GetMethod("ReadyToStart", flags);
            Func<bool?, double, bool> allowed = (ready, seconds) => (bool)policy.Invoke(null, new object[] { ready, seconds });
            Check(allowed(true, 0), "Ready client has no artificial delay");
            Check(!allowed(false, 120), "Not-ready never ages into ready");
            Check(!allowed(null, 7.99) && allowed(null, 8), "Missing bridge retains legacy fallback");
            var camping = assembly.GetType("AOLauncher.PresetCamping");
            var clientType = camping.GetNestedType("Client", BindingFlags.NonPublic);
            var client = Activator.CreateInstance(clientType, true);
            string token = Guid.NewGuid().ToString("N");
            clientType.GetProperty("CampToken").SetValue(client, token);
            clientType.GetProperty("ProcessId").SetValue(client, -71);
            var query = camping.GetMethod("QueryPluginReadiness", flags);
            Func<bool?> read = () => (bool?)query.Invoke(null, new[] { client });
            Check(read() == null, "Older module has no readiness channel");
            string prefix = @"Local\AONexus.Quit.v1.-71." + token + ".plugins.";
            using (var request = new EventWaitHandle(false, EventResetMode.AutoReset, prefix + "request"))
            using (var ready = new EventWaitHandle(false, EventResetMode.AutoReset, prefix + "ready"))
            using (var waiting = new EventWaitHandle(false, EventResetMode.AutoReset, prefix + "waiting")) {
                ready.Set(); // a stale positive must not permit injection
                Check(read() == false, "Stale positive is cleared; unresponsive game waits");
                Check(!request.WaitOne(0), "Timed-out request is withdrawn");
                foreach (bool response in new[] { true, false, true }) {
                    var task = Task.Run(() => { if (!request.WaitOne(2000)) throw new Exception("No request"); (response ? ready : waiting).Set(); });
                    bool? actual = read(); task.GetAwaiter().GetResult();
                    Check(actual == response, "Fresh readiness follows game-thread response including zoning");
                }
            }
            clientType.GetProperty("CampToken").SetValue(client, "bad");
            Check(read() == null, "Invalid legacy token cannot open arbitrary events");
            Check((bool?)camping.GetMethod("PluginsReady", flags).Invoke(null, new[] { client }) == false, "Wrong/exited process cannot be ready");
            Console.WriteLine("PASS: " + checks + " readiness, timeout, stale-response and legacy fallback checks; no game interaction.");
            return 0;
        } catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
