using System;
using System.Collections;
using System.IO;
using System.Reflection;
using AONexus.PluginHost;

public sealed class FakePluginPipe
{
    public int Disconnects;
    public bool Fail;
    public void Disconnect() { Disconnects++; if (Fail) throw new IOException("fixture disconnect failure"); }
}

internal static class PluginHostLifecycleTests
{
    static int checks;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
    public static int Main()
    {
        var type = typeof(HostRequest).Assembly.GetType("AONexus.PluginHost.Program");
        var flags = BindingFlags.Static | BindingFlags.NonPublic;
        var managed = (IDictionary)type.GetField("managed", flags).GetValue(null);
        var itemType = type.GetNestedType("Managed", BindingFlags.NonPublic);
        var item = Activator.CreateInstance(itemType, true);
        var info = new HostClient { Pid = 42, StartTicks = 200, State = "Loaded", Detail = "original" };
        var pipe = new FakePluginPipe();
        itemType.GetField("Info", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(item, info);
        itemType.GetField("Pipe", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(item, pipe);
        managed.Add(42, item);
        var execute = type.GetMethod("Execute", flags);
        Func<long, HostResponse> eject = ticks => (HostResponse)execute.Invoke(null, new object[] { new HostRequest { Command = "eject", Pid = 42, StartTicks = ticks } });
        Check(!eject(100).Ok, "old client identity is rejected");
        Check(pipe.Disconnects == 0 && info.State == "Loaded", "reused PID keeps the new client's plugins");
        Check(!eject(0).Ok && pipe.Disconnects == 0, "missing lifetime identity is rejected");
        pipe.Fail = true;
        var failure = eject(200);
        Check(!failure.Ok && failure.Error.Contains("fixture disconnect failure"), "disconnect failure is reported");
        Check(info.State == "Loaded" && info.Detail == "original", "failed unload is not reported as Ejected");
        pipe.Fail = false;
        Check(eject(200).Ok && pipe.Disconnects == 2 && info.State == "Ejected", "matching client can retry and unload");
        Check((string)type.GetField("ProtocolVersion", flags | BindingFlags.Public).GetRawConstantValue() == "v2", "fixed host cannot silently reuse the old v1 process");
        managed.Clear();
        Console.WriteLine("PASS: " + checks + " plugin-host lifecycle checks; inert pipe fixture only.");
        return 0;
    }
}
