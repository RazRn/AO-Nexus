using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace AOLauncher
{
    // Explicit local benchmark. Saved credentials travel only through BackendRunner's private stdin.
    internal static class StartupBenchmark
    {
        [STAThread] static int Main(string[] args)
        {
            try { return Run(args).GetAwaiter().GetResult(); }
            catch(Exception error) { Console.Error.WriteLine("Benchmark stopped: "+error.GetType().Name);return 1; }
        }
        static async Task<int> Run(string[] args)
        {
            if(args.Length==3 && args[0]=="logout") {
                string game=Path.GetFullPath(args[2]).TrimEnd('\\');
                if(!new[]{@"D:\Games\AORebirth-Live",@"D:\Games\AORebirth-AOVK-Test"}.Contains(game,StringComparer.OrdinalIgnoreCase))throw new ArgumentException();
                var records=LoginPreflight.Read();
                foreach(int pid in args[1].Split(',').Select(int.Parse)) {
                    using(var process=Process.GetProcessById(pid)) {
                        var record=records.Single(c=>c.ProcessId==pid && c.StartTimeUtcTicks==process.StartTime.ToUniversalTime().Ticks
                            && PresetCamping.SamePath(c.GamePath,game));
                        if(!PresetCamping.IsSameProcess(record))throw new InvalidOperationException("Test client identity changed");
                        Console.WriteLine("Client "+pid+": "+PresetCamping.SendCamp(record));
                        if(!await Task.Run(()=>process.WaitForExit(30000)))throw new InvalidOperationException("Normal logout timed out; no force close");
                    }
                }
                return 0;
            }
            if(args.Length!=5)throw new ArgumentException();
            string mode=args[0],backend=Path.GetFullPath(args[2]),log=Path.GetFullPath(args[3]);
            int count=int.Parse(args[1]);bool launch=args[4]=="run";
            if(!new[]{"baseline","serial-fast","pipeline-2","pipeline-6"}.Contains(mode) || (count!=1 && count!=6))throw new ArgumentException();
            if(Process.GetProcessesByName("AnarchyOnline").Length!=0)throw new InvalidOperationException("Existing game clients");
            ServerRules.Install();Localization.Texts.SetLanguage("en");
            var settings=SettingsStore.Read();DiagnosticRedactor.Update(settings);
            var accounts=settings.Accounts.Where(a=>a.Characters.Any(c=>c.Selected)).ToList();
            if(accounts.Sum(a=>a.Characters.Count(c=>c.Selected))!=6 || accounts.Any(a=>a.Server!="2.24.96.30" || a.Port!=7500
                || !new[]{@"D:\Games\AORebirth-Live",@"D:\Games\AORebirth-AOVK-Test"}.Contains(Path.GetFullPath(a.GamePath).TrimEnd('\\'),StringComparer.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Benchmark selection must be the six saved Rebirth clients");
            var credentials=new Dictionary<string,string>();
            foreach(var account in accounts) {
                if(!account.RememberPassword)throw new InvalidOperationException("Saved credential required");
                var secret=CredentialStore.Read(account);if(string.IsNullOrEmpty(secret))throw new InvalidOperationException("Saved credential unavailable");
                credentials.Add(account.Id,secret);
            }
            var request=MainForm.CreateLaunchRequest(settings,credentials);
            request.Characters=request.Characters.Take(count).ToList();request.FastStartup=mode!="baseline";request.QuietLaunch=true;request.ReadyTimeoutSeconds=60;
            var serializer=new JavaScriptSerializer();
            var payload=serializer.Deserialize<Dictionary<string,object>>(serializer.Serialize(request));
            payload["AcceptancePipeline"]=mode.StartsWith("pipeline-");payload["MaxConcurrentPreloads"]=mode=="pipeline-6"?6:2;
            var info=new ProcessStartInfo(Path.Combine(backend,"NexusEngine.exe"),launch?"--launch-stdin":"--validate-stdin") {WorkingDirectory=backend};
            info.EnvironmentVariables["AONexusExperimentalAOVK"]=mode=="serial-fast" || mode=="pipeline-2"?"True":"False";
            using(var writer=new StreamWriter(log,false))
            using(var runner=new BackendRunner()) {
                var clock=Stopwatch.StartNew();var sync=new object();
                Action<string> record=line=>{if(line==null)return;foreach(var secret in credentials.Values)line=line.Replace(secret,"[secret]");
                    line=DiagnosticRedactor.Current.Redact(line);lock(sync){writer.WriteLine(clock.Elapsed.TotalSeconds.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+" "+line);writer.Flush();}
                    if(line.Contains("loaded in") || line.Contains("Login run finished") || line.Contains("Startup hooks:") || line.Contains("Validation OK"))Console.WriteLine(line);
                };
                record("Benchmark mode="+mode+" clients="+count+" launch="+launch);
                var task=runner.RunAsync(info,serializer.Serialize(payload),record,record);
                if(await Task.WhenAny(task,Task.Delay(180000))!=task) {runner.Stop();record("Benchmark timeout; game processes retained for diagnosis");}
                int code=await task;record("Backend exit="+code+" elapsed="+clock.Elapsed.TotalSeconds.ToString("F2"));
                foreach(var character in request.Characters)character.Password=null;
                credentials.Clear();payload.Clear();Console.WriteLine("Benchmark exit="+code+"; redacted timing log: "+log);return code;
            }
        }
    }
}
