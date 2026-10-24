using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

// This fixture invokes the shipped policy with synthetic accounts. It never calls
// the production credential directory or reads an existing user's profile.
public static class SavedPasswordUpdateTests
{
    private const BindingFlags Static = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private static int checks;
    private sealed class Fixture
    {
        internal readonly Dictionary<string,string> Cache = new Dictionary<string,string>();
        internal readonly Dictionary<string,string> Disk = new Dictionary<string,string>();
        internal readonly List<string> Events = new List<string>();
        internal bool FailSave, FailPreference, FailDelete;
        internal string Directory, Error;
        private readonly Type accountType, credentialType, entryType;
        private readonly MethodInfo update, prepare;
        internal Fixture(Assembly assembly)
        {
            accountType = assembly.GetType("AOLauncher.AccountConfig", true);
            credentialType = assembly.GetType("AOLauncher.CredentialStore", true);
            entryType = assembly.GetType("AOLauncher.PasswordEntry", true);
            var policy = assembly.GetType("AOLauncher.AccountPasswords", true);
            update = policy.GetMethod("TryUpdateSaved", Static);
            prepare = policy.GetMethod("TryPrepare", Static);
            Check(update != null, "The shipped assembly must expose the dedicated saved-password policy.");
        }
        internal object Account(string id, bool remember)
        {
            object account = Activator.CreateInstance(accountType);
            Set(account, "Id", id); Set(account, "Label", "Synthetic " + id); Set(account, "Username", "synthetic-" + id);
            Set(account, "Server", "192.0.2.47"); Set(account, "Port", 7047); Set(account, "RememberPassword", remember);
            return account;
        }
        internal object Store(string method, params object[] args) { return credentialType.GetMethod(method, Static).Invoke(null, args); }
        internal bool Update(object account, string password)
        {
            var p = update.GetParameters();
            object[] args = {
                account, password, Cache,
                Bridge(p[3].ParameterType, a => {
                    Events.Add("save:" + Id(a[0]));
                    Check(Remember(a[0]), "Updating a saved password must opt in before the encrypted store runs.");
                    if (FailSave) throw new IOException("synthetic failed save: " + password);
                    if (Directory == null) Disk[Id(a[0])] = (string)a[1]; else Store("SaveToDirectory", a[0], a[1], Directory);
                    return null;
                }),
                Bridge(p[4].ParameterType, a => {
                    Events.Add("preference:" + Id(a[0]));
                    Check(Remember(a[0]), "Preference persistence must see opt-in.");
                    if (FailPreference) throw new IOException("synthetic failed preference: " + password);
                    return null;
                }),
                Bridge(p[5].ParameterType, a => {
                    Events.Add("delete:" + Id(a[0]));
                    if (FailDelete) throw new IOException("synthetic failed delete: " + password);
                    if (Directory == null) Disk.Remove(Id(a[0])); else Store("DeleteFromDirectory", a[0], Directory);
                    return null;
                }), null
            };
            bool ok = (bool)update.Invoke(null, args); Error = (string)args[6]; return ok;
        }
        internal string Prepare(object account, out int promptCount)
        {
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(accountType)); list.Add(account);
            int count = 0;
            var p = prepare.GetParameters();
            object[] args = {
                list, Cache,
                Bridge(p[2].ParameterType, a => { count++; return null; }),
                Bridge(p[3].ParameterType, a => { string value; return Directory == null ? (Disk.TryGetValue(Id(a[0]), out value) ? value : null) : Store("ReadFromDirectory", a[0], Directory); }),
                Bridge(p[4].ParameterType, a => { throw new Exception("Updated credentials must not prompt and resave during the next login."); }),
                Bridge(p[5].ParameterType, a => { throw new Exception("Updated opted-in credentials must not be deleted during login."); }),
                Bridge(p[6].ParameterType, a => { throw new Exception("Updated preference must already be saved."); }),
                Bridge(p[7].ParameterType, a => { throw new Exception("The fixture should not report a login preparation error."); }), null
            };
            Check((bool)prepare.Invoke(null, args), "The next login must prepare with the newly saved password.");
            promptCount = count;
            var credentials = (Dictionary<string,string>)args[8]; string password = credentials[Id(account)]; credentials.Clear(); return password;
        }
    }
    private static Delegate Bridge(Type type, Func<object[],object> callback)
    {
        var invoke = type.GetMethod("Invoke");
        var parameters = invoke.GetParameters().Select(p => Expression.Parameter(p.ParameterType)).ToArray();
        var call = Expression.Invoke(Expression.Constant(callback), Expression.NewArrayInit(typeof(object), parameters.Select(p => Expression.Convert(p, typeof(object)))));
        Expression body = invoke.ReturnType == typeof(void) ? (Expression)Expression.Block(call, Expression.Empty()) : Expression.Convert(call, invoke.ReturnType);
        return Expression.Lambda(type, body, parameters).Compile();
    }
    private static void Set(object value, string name, object field) { value.GetType().GetProperty(name).SetValue(value, field, null); }
    private static string Id(object value) { return (string)value.GetType().GetProperty("Id").GetValue(value, null); }
    private static bool Remember(object value) { return (bool)value.GetType().GetProperty("RememberPassword").GetValue(value, null); }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
    private static void NoSecret(string message, string secret) { Check(!string.IsNullOrEmpty(message) && !message.Contains(secret) && !message.Contains("synthetic failed"), "Failures must use safe messages without passwords or raw exception details."); }
    public static int Run(Assembly assembly, string testDirectory)
    {
        checks = 0;
        var f = new Fixture(assembly); object saved = f.Account("saved", true), other = f.Account("other", true);
        f.Cache["saved"] = "synthetic-old"; f.Disk["saved"] = "synthetic-old";
        f.Cache["other"] = "synthetic-other"; f.Disk["other"] = "synthetic-other";
        Check(f.Update(saved, "synthetic-new") && string.IsNullOrEmpty(f.Error), "An existing remembered password can be updated without logging in.");
        Check(f.Cache["saved"] == "synthetic-new" && f.Disk["saved"] == "synthetic-new", "Updating must replace both the saved password and stale session cache.");
        Check(f.Cache["other"] == "synthetic-other" && f.Disk["other"] == "synthetic-other", "Updating must not change another account's cache or credential.");
        Check(f.Events.SequenceEqual(new[] { "save:saved" }), "An opted-in account update must not rewrite its unchanged preference or delete credentials.");
        int prompted;
        Check(f.Prepare(saved, out prompted) == "synthetic-new" && prompted == 0, "The next login must reuse the updated cached password without prompting.");
        f.Cache.Clear();
        Check(f.Prepare(saved, out prompted) == "synthetic-new" && prompted == 0, "A fresh session must read the updated saved password.");

        f = new Fixture(assembly); object ask = f.Account("ask", false);
        Check(f.Update(ask, "synthetic-opt-in") && Remember(ask), "Explicitly updating a saved password must enable remembering for that account.");
        Check(f.Events.SequenceEqual(new[] { "save:ask", "preference:ask" }) && f.Cache["ask"] == "synthetic-opt-in", "A new opt-in must save the credential before preference persistence and caching.");

        f = new Fixture(assembly); saved = f.Account("empty", true); f.Cache["empty"] = "synthetic-unchanged";
        Check(!f.Update(saved, "") && !string.IsNullOrEmpty(f.Error), "An empty replacement must be rejected by the policy as well as the dialog.");
        Check(f.Events.Count == 0 && f.Cache["empty"] == "synthetic-unchanged" && Remember(saved), "Empty-password validation must not touch credential storage, cache or preference.");

        foreach (bool original in new[] { true, false })
        {
            f = new Fixture(assembly); saved = f.Account("failed", original); f.Cache["failed"] = "synthetic-previous"; f.Disk["failed"] = "synthetic-previous"; f.FailSave = true;
            Check(!f.Update(saved, "synthetic-secret-save-error"), "An encrypted save failure must not appear successful.");
            Check(Remember(saved) == original && f.Cache["failed"] == "synthetic-previous" && f.Disk["failed"] == "synthetic-previous", "A failed save must preserve the preference, existing cache and saved password.");
            Check(f.Events.SequenceEqual(new[] { "save:failed" }), "A failed encrypted save must not persist preferences or delete previous credentials.");
            NoSecret(f.Error, "synthetic-secret-save-error");
        }

        f = new Fixture(assembly); ask = f.Account("preference-fails", false); f.FailPreference = true; f.Cache[Id(ask)] = "synthetic-cache-before";
        Check(!f.Update(ask, "synthetic-secret-preference-error") && !Remember(ask), "An opt-in preference failure must restore the original opt-out.");
        Check(f.Cache[Id(ask)] == "synthetic-cache-before" && !f.Disk.ContainsKey(Id(ask)), "A preference failure must leave cache unchanged and remove the newly saved credential.");
        Check(f.Events.SequenceEqual(new[] { "save:preference-fails", "preference:preference-fails", "delete:preference-fails" }), "Preference failure must trigger scoped credential rollback.");
        NoSecret(f.Error, "synthetic-secret-preference-error");
        f = new Fixture(assembly); ask = f.Account("cleanup-fails", false); f.FailPreference = f.FailDelete = true;
        Check(!f.Update(ask, "synthetic-secret-cleanup-error") && !Remember(ask) && !f.Cache.ContainsKey(Id(ask)), "Cleanup failure must still leave the preference disabled and avoid caching the new password.");
        NoSecret(f.Error, "synthetic-secret-cleanup-error");

        f = new Fixture(assembly) { Directory = testDirectory }; saved = f.Account("encrypted", true); other = f.Account("encrypted-other", true);
        f.Store("SaveToDirectory", saved, "synthetic-dpapi-old", testDirectory); f.Store("SaveToDirectory", other, "synthetic-dpapi-other", testDirectory);
        string otherPath = (string)f.Store("AccountPath", other, testDirectory); byte[] otherBytes = File.ReadAllBytes(otherPath);
        f.Cache[Id(saved)] = "synthetic-dpapi-old";
        Check(f.Update(saved, "synthetic-dpapi-new"), "The actual encrypted store must accept an update in the isolated test directory.");
        Check((string)f.Store("ReadFromDirectory", saved, testDirectory) == "synthetic-dpapi-new", "The actual DPAPI credential must roundtrip the replacement password.");
        Check(File.ReadAllBytes(otherPath).SequenceEqual(otherBytes), "Encrypted update must leave the other account's credential byte-for-byte unchanged.");
        string updatedPath = (string)f.Store("AccountPath", saved, testDirectory);
        Check(!System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(updatedPath)).Contains("synthetic-dpapi-new"), "The updated password must not be written as plaintext.");
        f.Cache.Clear();
        Check(f.Prepare(saved, out prompted) == "synthetic-dpapi-new" && prompted == 0, "The updated encrypted password must be reusable in a fresh session.");
        Check(System.IO.Directory.GetFiles(testDirectory, "*.tmp").Length == 0, "Successful encrypted replacement must leave no temporary files.");
        return checks;
    }
}
