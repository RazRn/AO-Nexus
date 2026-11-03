using System;
using System.IO;
using System.Linq;
using System.Reflection;
using AOSharp.Bootstrap;
using AOSharp.Common;
using Newtonsoft.Json;

internal static class RuntimeDiagnosticsTests
{
    static int checks;
    static void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;}
    static int Main(string[] args)
    {
        string folder=AppDomain.CurrentDomain.BaseDirectory;
        typeof(PluginRuntimeStatus).GetField("directoryOverride",BindingFlags.NonPublic|BindingFlags.Static).SetValue(null,folder);
        string[] paths=new[]{"Good.dll","Second.dll","Bad.dll","Missing.dll"}.Select(file=>Path.Combine(folder,file)).ToArray();
        var proxy=new PluginProxy();
        proxy.BeginSession(paths,"fixture-request");
        foreach(var path in paths)proxy.LoadPlugin(path);
        proxy.RunPluginInitializations();
        string reportFile;
        using(var process=System.Diagnostics.Process.GetCurrentProcess())
            reportFile=Path.Combine(folder,process.Id+"-"+process.StartTime.ToUniversalTime().Ticks+".json");
        var report=JsonConvert.DeserializeObject<PluginRuntimeStatus.Report>(File.ReadAllText(reportFile));
        Check(report.RequestId=="fixture-request","Request identifier roundtrips");
        Check(report.Plugins.Single(p=>p.Path==paths[0]).State=="Started","Old compiled plugin starts unchanged");
        Check(report.Plugins.Single(p=>p.Path==paths[1]).State=="Started","Second unchanged compiled plugin starts");
        Check(report.Plugins.Single(p=>p.Path==paths[2]).State=="Failed" && report.Plugins.Single(p=>p.Path==paths[2]).Error.Contains("inert startup failure"),"Thrown initialization acknowledged as failure");
        Check(report.Plugins.Single(p=>p.Path==paths[3]).State=="Failed","Missing DLL acknowledged as failure");
        Check(report.Conflicts.Any(c=>c.Resource=="/nexusfixture" && c.Previous==paths[0] && c.Current==paths[1]),"Command ownership conflict attributed");
        Check(report.Conflicts.Any(c=>c.Resource=="Combat handler" && c.Previous==paths[0] && c.Current==paths[1]),"Combat handler conflict attributed");
        proxy.Teardown();
        report=JsonConvert.DeserializeObject<PluginRuntimeStatus.Report>(File.ReadAllText(reportFile));
        Check(report.Plugins.Single(p=>p.Path==paths[0]).State=="Stopped","Teardown acknowledged");
        var message=new AOSharp.Bootstrap.IPC.LoadAssemblyMessage{Assemblies=paths,RequestId="wire-request"};
        var received=new AOSharp.Bootstrap.IPC.LoadAssemblyMessage();received.Deserialize(message.Serialize());
        Check(received.RequestId==message.RequestId && received.Assemblies.SequenceEqual(paths),"Loader wire request roundtrip");
        using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream))
        {
            writer.Write(message.OpCode);writer.Write(JsonConvert.SerializeObject(paths));
            received.Deserialize(stream.ToArray());
            Check(received.Assemblies.SequenceEqual(paths) && received.RequestId == null,"Legacy loader message accepted without stale acknowledgement");
        }
        Console.WriteLine("PASS: "+checks+" runtime checks; plugins compiled against old AOSharp loaded unchanged, no game hooks initialized.");return 0;
    }
}
