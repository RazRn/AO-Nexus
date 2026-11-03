using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using AOSharp.Core;
using NexusShortcuts;

// Uses the packaged Settings.Load and production FirstChanceException watcher in an
// isolated process. No game, native Preferences call, plugin Run or network listener.
static class ShortcutsExceptionsTests
{
    static readonly Type Instrument = typeof(NexusShortcutsPlugin);
    const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
    const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    static int checks;
    static object Faults => Instrument.GetField("faults", PrivateStatic).GetValue(null);
    static void Check(bool value, string message)
    {
        if (!value) throw new Exception("FAIL: " + message);
        checks++;
        Console.WriteLine("PASS: " + message);
    }
    static void Reset()
    {
        Faults.GetType().GetMethod("Clear").Invoke(Faults, null);
        Instrument.GetField("budgetUsed", PrivateStatic).SetValue(null, 0);
    }
    static object Fault()
    {
        object[] args = { typeof(ShortcutsExceptionsTests).Assembly.GetName().Name, null };
        return (bool)Faults.GetType().GetMethod("TryGetValue").Invoke(Faults, args) ? args[1] : null;
    }
    static int Errors()
    {
        var fault = Fault();
        return fault == null ? 0 : (int)fault.GetType().GetField("Errors", PrivateInstance).GetValue(fault);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void LoadSettings(string path)
    {
        var settings = (Settings)FormatterServices.GetUninitializedObject(typeof(Settings));
        typeof(Settings).GetField("_savePath", PrivateInstance).SetValue(settings, path);
        settings.Load();
        var values = (IDictionary)typeof(Settings).GetField("_storedValues", PrivateInstance).GetValue(settings);
        Check(values != null, "real Settings.Load returns a usable dictionary");
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowMissing() { throw new FileNotFoundException("Required plugin asset missing", "required.dat"); }
    static int Main()
    {
        string folder = Path.Combine(Path.GetTempPath(), "NexusExceptions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            new NexusShortcutsPlugin(); // Only subscribes the production watcher.
            Reset();
            LoadSettings(Path.Combine(folder, "SyncManager.config"));
            Check(Errors() == 0, "missing optional settings file is not a plugin fault");
            Reset();
            LoadSettings(Path.Combine(folder, "absent", "SyncManager.config"));
            Check(Errors() == 0, "missing optional settings directory is not a plugin fault");
            string config = Path.Combine(folder, "corrupt.config");
            File.WriteAllText(config, "{ broken json");
            Reset();
            LoadSettings(config);
            Check(Errors() > 0, "corrupt settings still produce a diagnostic even though AOSharp catches it");
            File.WriteAllText(config, "{}");
            Reset();
            LoadSettings(config);
            Check(Errors() == 0, "valid settings produce no diagnostic");
            Reset();
            using (File.Open(config, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) LoadSettings(config);
            Check(Errors() > 0, "locked settings file is not silently exempted");
            Reset();
            try { File.ReadAllText(Path.Combine(folder, "required.dat")); } catch (FileNotFoundException) { }
            Check(Errors() > 0, "missing file outside Settings.Load remains visible");
            Reset();
            try { ThrowMissing(); } catch (FileNotFoundException) { }
            Check(Errors() > 0, "other caught plugin exceptions remain visible");
            Reset();
            AOSharp.Bootstrap.Plugin.Initialize();
            var fault = Fault();
            Check(fault != null && ((string)fault.GetType().GetField("StartError", PrivateInstance).GetValue(fault)).Contains("failed to start"), "exception escaping reflected startup is still a confirmed startup failure");
            Console.WriteLine("PASS: " + checks + " exception checks against packaged AOSharp.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { Directory.Delete(folder, true); }
    }
}
namespace AOSharp.Bootstrap
{
    static class Plugin
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Initialize()
        {
            try { typeof(ShortcutsExceptionsTests).GetMethod("ThrowMissing").Invoke(null, null); }
            catch (TargetInvocationException) { }
        }
    }
}
