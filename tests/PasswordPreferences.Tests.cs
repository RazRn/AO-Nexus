using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

// Reflection keeps this fixture against the actual shipped policy, with every
// credential operation replaced by an in-memory delegate.
public static class PasswordPreferencesTests
{
    private const BindingFlags AllStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private static int checks;
    private sealed class Fixture
    {
        internal readonly Dictionary<string, string> Cache = new Dictionary<string, string>();
        internal readonly Dictionary<string, string> Disk = new Dictionary<string, string>();
        internal readonly Dictionary<string, Queue<Tuple<string, bool>>> Replies = new Dictionary<string, Queue<Tuple<string, bool>>>();
        internal readonly List<string> Prompted = new List<string>();
        internal readonly List<string> Read = new List<string>();
        internal readonly List<string> Saved = new List<string>();
        internal readonly List<string> Deleted = new List<string>();
        internal readonly List<string> Changed = new List<string>();
        internal readonly List<string> Reports = new List<string>();
        internal bool FailRead, FailSave, FailDelete;
        private readonly Type accountType, entryType;
        private readonly MethodInfo prepare;
        internal Dictionary<string, string> Credentials;
        internal Fixture(Assembly assembly)
        {
            accountType = assembly.GetType("AOLauncher.AccountConfig", true);
            entryType = assembly.GetType("AOLauncher.PasswordEntry", true);
            prepare = assembly.GetType("AOLauncher.AccountPasswords", true).GetMethod("TryPrepare", AllStatic);
        }
        internal object Account(string id, bool remember)
        {
            object account = Activator.CreateInstance(accountType);
            Set(account, "Id", id); Set(account, "Username", "synthetic-" + id); Set(account, "RememberPassword", remember);
            return account;
        }
        internal void Reply(string id, string password, bool remember)
        {
            if (!Replies.ContainsKey(id)) Replies[id] = new Queue<Tuple<string, bool>>();
            Replies[id].Enqueue(Tuple.Create(password, remember));
        }
        internal bool Prepare(params object[] accounts)
        {
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(accountType));
            foreach (object account in accounts) list.Add(account);
            var parameters = prepare.GetParameters();
            object[] args = {
                list, Cache,
                Bridge(parameters[2].ParameterType, a => {
                    string id = Id(a[0]); Prompted.Add(id);
                    if (!Replies.ContainsKey(id) || Replies[id].Count == 0) return null;
                    var reply = Replies[id].Dequeue();
                    object entry = Activator.CreateInstance(entryType);
                    Set(entry, "Password", reply.Item1); Set(entry, "RememberPassword", reply.Item2);
                    return entry;
                }),
                Bridge(parameters[3].ParameterType, a => {
                    string id = Id(a[0]); Read.Add(id); if (FailRead) throw new InvalidOperationException("synthetic read failure");
                    string value; return Disk.TryGetValue(id, out value) ? value : null;
                }),
                Bridge(parameters[4].ParameterType, a => {
                    string id = Id(a[0]); Saved.Add(id); if (FailSave) throw new InvalidOperationException("synthetic save failure");
                    Disk[id] = (string)a[1]; return null;
                }),
                Bridge(parameters[5].ParameterType, a => {
                    string id = Id(a[0]); Deleted.Add(id); if (FailDelete) throw new InvalidOperationException("synthetic delete failure");
                    Disk.Remove(id); return null;
                }),
                Bridge(parameters[6].ParameterType, a => { Changed.Add(Id(a[0])); return null; }),
                Bridge(parameters[7].ParameterType, a => { Reports.Add((string)a[0]); return null; }),
                null
            };
            bool result = (bool)prepare.Invoke(null, args);
            Credentials = (Dictionary<string, string>)args[8];
            return result;
        }
    }
    private static Delegate Bridge(Type type, Func<object[], object> callback)
    {
        var invoke = type.GetMethod("Invoke");
        var parameters = invoke.GetParameters().Select(p => Expression.Parameter(p.ParameterType)).ToArray();
        var call = Expression.Invoke(Expression.Constant(callback), Expression.NewArrayInit(typeof(object), parameters.Select(p => Expression.Convert(p, typeof(object)))));
        Expression body = invoke.ReturnType == typeof(void) ? (Expression)Expression.Block(call, Expression.Empty()) : Expression.Convert(call, invoke.ReturnType);
        return Expression.Lambda(type, body, parameters).Compile();
    }
    private static void Set(object instance, string name, object value) { instance.GetType().GetProperty(name).SetValue(instance, value, null); }
    private static string Id(object account) { return (string)account.GetType().GetProperty("Id").GetValue(account, null); }
    private static bool Remember(object account) { return (bool)account.GetType().GetProperty("RememberPassword").GetValue(account, null); }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    public static int Run(Assembly assembly)
    {
        checks = 0;
        var fixture = new Fixture(assembly);
        object ask = fixture.Account("ask", false), saved = fixture.Account("saved", true);
        fixture.Cache["ask"] = "stale-session-secret"; fixture.Disk["ask"] = "stale-disk-secret"; fixture.Disk["saved"] = "synthetic-saved-secret";
        fixture.Reply("ask", "synthetic-action-one", false);
        Check(fixture.Prepare(ask, ask, saved), "Mixed remember/ask accounts should prepare successfully.");
        Check(fixture.Prompted.SequenceEqual(new[] { "ask" }), "A repeated account must prompt once per launch action, not once per char.");
        Check(fixture.Credentials.Count == 2 && fixture.Credentials["ask"] == "synthetic-action-one" && fixture.Credentials["saved"] == "synthetic-saved-secret", "Operation credentials must preserve independent per-account choices.");
        Check(!fixture.Cache.ContainsKey("ask") && !fixture.Disk.ContainsKey("ask") && fixture.Deleted.Contains("ask"), "Opt-out must evict the old cache and delete only its stored secret.");
        Check(fixture.Read.SequenceEqual(new[] { "saved" }) && fixture.Saved.Count == 0, "Opt-out must not read or write persistent secrets.");
        Check(fixture.Cache["saved"] == "synthetic-saved-secret", "Opt-in may reuse its stored secret in the current session.");
        fixture.Credentials.Clear(); fixture.Reply("ask", "synthetic-action-two", false);
        Check(fixture.Prepare(ask, saved) && fixture.Credentials["ask"] == "synthetic-action-two", "A subsequent action must prompt again for an opt-out account.");
        Check(fixture.Prompted.Count == 2 && fixture.Read.Count == 1 && fixture.Saved.Count == 0, "Only opt-in session cache may skip the later prompt.");

        fixture = new Fixture(assembly); ask = fixture.Account("ask", false);
        fixture.Reply("ask", "synthetic-new-saved-secret", true);
        Check(fixture.Prepare(ask) && Remember(ask), "Checking Remember in the password dialog must enable persistence for that account.");
        Check(fixture.Changed.SequenceEqual(new[] { "ask" }) && fixture.Saved.SequenceEqual(new[] { "ask" }) && fixture.Disk["ask"] == "synthetic-new-saved-secret", "Opting in must persist the preference once and securely save the supplied secret.");
        Check(fixture.Prepare(ask) && fixture.Prompted.Count == 1, "An opted-in account should reuse the session cache on later actions.");

        fixture = new Fixture(assembly); saved = fixture.Account("saved", true);
        fixture.Reply("saved", "synthetic-now-transient", false);
        Check(fixture.Prepare(saved) && !Remember(saved), "Unchecking Remember at the prompt must switch that account to transient use.");
        Check(fixture.Changed.SequenceEqual(new[] { "saved" }) && fixture.Deleted.Contains("saved") && fixture.Saved.Count == 0 && fixture.Cache.Count == 0, "Opting out at the prompt must delete stale storage and avoid both write and session cache.");
        fixture.Reply("saved", "synthetic-next-transient", false);
        Check(fixture.Prepare(saved) && fixture.Prompted.Count == 2, "A newly opted-out account must prompt on its very next action.");

        fixture = new Fixture(assembly); ask = fixture.Account("ask", false); object cancel = fixture.Account("cancel", false);
        fixture.Reply("ask", "synthetic-before-cancel", false);
        Check(!fixture.Prepare(ask, cancel), "Cancelling any account's password prompt must cancel the complete launch action.");
        Check(fixture.Credentials.Count == 0 && fixture.Cache.Count == 0 && fixture.Saved.Count == 0, "Cancelled operation credentials and transient secrets must be discarded.");
        fixture.Reply("ask", "", false);
        Check(!fixture.Prepare(ask) && fixture.Credentials.Count == 0, "An empty prompted password must not create a launch request.");

        fixture = new Fixture(assembly); cancel = fixture.Account("cancel", false); ask = fixture.Account("later", false);
        fixture.Cache["later"] = "stale-unreached-secret"; fixture.Disk["later"] = "stale-unreached-secret";
        Check(!fixture.Prepare(cancel, ask) && !fixture.Cache.ContainsKey("later") && !fixture.Disk.ContainsKey("later"), "Cancelling the first prompt must still purge stale credentials for later selected opt-out accounts.");
        Check(fixture.Prompted.SequenceEqual(new[] { "cancel" }), "Cancellation must not open another account's password prompt.");

        fixture = new Fixture(assembly); saved = fixture.Account("saved", true); fixture.FailRead = true; fixture.FailSave = true;
        fixture.Reply("saved", "synthetic-recovery", true);
        Check(fixture.Prepare(saved) && fixture.Credentials["saved"] == "synthetic-recovery", "An unavailable encrypted store must allow a fresh supplied password for this launch.");
        Check(fixture.Reports.Count == 2 && fixture.Reports.All(r => !r.Contains("synthetic-recovery")), "Read/save failures must be reported without exposing the secret.");
        Check(fixture.Cache["saved"] == "synthetic-recovery", "Opt-in save failure preserves established session-only fallback.");

        fixture = new Fixture(assembly); ask = fixture.Account("ask", false); fixture.FailDelete = true;
        fixture.Cache["ask"] = "stale-session-secret"; fixture.Disk["ask"] = "stale-disk-secret"; fixture.Reply("ask", "synthetic-transient", false);
        Check(fixture.Prepare(ask) && fixture.Credentials["ask"] == "synthetic-transient", "A deletion failure must not make the launcher use the saved password.");
        Check(fixture.Cache.Count == 0 && fixture.Read.Count == 0 && fixture.Saved.Count == 0 && fixture.Reports.Count > 0, "A failed opt-out deletion must report the failure and still deny cached/persisted reuse.");
        return checks;
    }
}
