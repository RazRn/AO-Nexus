using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace AOLauncher
{
    // Opt-in on this PC. Bounded structured evidence; no credentials, chat, window titles or raw memory.
    internal sealed class DevelopmentRecordingStore
    {
        readonly object gate=new object();readonly string root;readonly int limit;
        internal DevelopmentRecordingStore(string directory,int bytes=16*1024*1024){root=directory;limit=bytes;}
        internal void Append(object record)
        {
            string line=new JavaScriptSerializer().Serialize(record)+"\n";
            if(Encoding.UTF8.GetByteCount(line)>limit)return;
            lock(gate) {
                Directory.CreateDirectory(root);string path=Path.Combine(root,"development.jsonl");
                if(File.Exists(path) && new FileInfo(path).Length+Encoding.UTF8.GetByteCount(line)>limit) {
                    string oldest=Path.Combine(root,"development.2.jsonl"),previous=Path.Combine(root,"development.1.jsonl");
                    if(File.Exists(oldest))File.Delete(oldest);
                    if(File.Exists(previous))File.Move(previous,oldest);
                    File.Move(path,previous);
                }
                File.AppendAllText(path,line,new UTF8Encoding(false));
            }
        }
        internal Dictionary<string,string> Read()
        {
            lock(gate) {
                var result=new Dictionary<string,string>();
                foreach(string name in new[]{"development.2.jsonl","development.1.jsonl","development.jsonl"}) {
                    string path=Path.Combine(root,name);
                    if(File.Exists(path) && new FileInfo(path).Length<=limit)result[name]=File.ReadAllText(path);
                }
                return result;
            }
        }
    }
    internal static class DevelopmentRecorder
    {
        static readonly DevelopmentRecordingStore store=new DevelopmentRecordingStore(DiagnosticJournal.DefaultRoot);
        static readonly string marker=Path.Combine(DiagnosticJournal.DefaultRoot,"development-recording.enabled");
        static readonly string run=Guid.NewGuid().ToString("N");
        static volatile bool enabled;
        static string error;
        static long heartbeat;
        static Timer watchdog;
        internal static bool Enabled=>enabled;
        internal static string Error=>error;
        internal static void Start()
        {
            enabled=File.Exists(marker);Beat();
            Write("session-start",new {Version=typeof(DevelopmentRecorder).Assembly.GetName().Version.ToString(),QpcFrequency=Stopwatch.Frequency});
            watchdog=new Timer(_=>Write("launcher-heartbeat",new {UiGapMs=(Stopwatch.GetTimestamp()-Interlocked.Read(ref heartbeat))*1000.0/Stopwatch.Frequency}),null,2000,2000);
        }
        internal static void Stop(){Write("session-stop",new {});watchdog?.Dispose();watchdog=null;}
        internal static void Beat(){Interlocked.Exchange(ref heartbeat,Stopwatch.GetTimestamp());}
        internal static void SetEnabled(bool value)
        {
            Directory.CreateDirectory(DiagnosticJournal.DefaultRoot);
            if(value)File.WriteAllText(marker,"Local development recording enabled. No automatic upload.");
            else if(File.Exists(marker))File.Delete(marker);
            if(!value)Write("recording-stopped",new {});
            enabled=value;if(value)Write("recording-started",new {});
        }
        internal static void Write(string kind,object data)
        {
            if(!enabled)return;
            try {store.Append(new {Utc=DateTime.UtcNow.ToString("O"),Run=run,Qpc=Stopwatch.GetTimestamp(),Kind=kind,Data=data});error=null;}
            catch(Exception e){error=e.GetType().Name;}
        }
        internal static void Action(string action,PresetCamping.Client client,string stage)
        {
            Write("client-action",new {Action=action,Pid=client?.ProcessId,StartTicks=client?.StartTimeUtcTicks,Stage=DiagnosticRedactor.Current.Redact(stage)});
            DiagnosticJournal.Record("client-action",action+" pid="+client?.ProcessId+" start="+client?.StartTimeUtcTicks+" "+stage);
        }
        internal static object Sample(ClientObservation row)=>new {
            row.Pid,row.StartTicks,State=row.Condition.ToString(),row.ExitedUtc,row.ExitCode,Measurement=row.Sample==null?null:new {
                row.Sample.Utc,row.Sample.State,Reason=DiagnosticRedactor.Current.Redact(row.Sample.Reason??""),row.Sample.SnapshotStatus,
                row.Sample.WindowResponding,row.Sample.WindowUnresponsiveSamples,row.Sample.CpuTotalMs,row.Sample.WorkingSetBytes,
                row.Sample.CpuPercent,row.Sample.FreeClientBytes,row.Sample.LargestFreeClientBytes,row.Sample.ClientAddressBytes,row.Sample.MemoryMeasuredQpc,
                row.Sample.PulseAgeMs,row.Sample.UpdateAgeMs,row.Sample.ObservedUpdateGapMs,row.Sample.RecentMaxGapMs,row.Sample.MaxGapMs,
                row.Sample.HelperAgeMs,row.Sample.ReplyAgeMs,row.Sample.ReplyMs,row.Sample.Sent,row.Sample.Replies,row.Sample.Timeouts,row.Sample.Pending,row.Sample.Late,
                row.Sample.Minimized,row.Sample.Foreground,row.Sample.Zoning},
            Vitals=row.Pulse==null?null:new {row.Pulse.Active,row.Pulse.Zoning,row.Pulse.Updates,row.Pulse.Health,row.Pulse.MaxHealth,row.Pulse.Nano,row.Pulse.MaxNano,row.Pulse.Attacking,row.Pulse.CastNanoId,row.Pulse.CastAttemptQpc,row.Pulse.VitalsQpc},
            Movement=row.Pulse?.Movement,
            Meter=row.Pulse?.CombatMeter==null?null:new {row.Pulse.CombatMeter.Source,row.Pulse.CombatMeter.Status,row.Pulse.CombatMeter.ElapsedSeconds,row.Pulse.CombatMeter.ObservedQpc,
                Actors=row.Pulse.CombatMeter.Actors?.Select(a=>new {a.Damage,a.NanoDamage,a.PetDamage,a.Healing,a.Xp,a.Axp,a.Sk,a.ExperienceEvents}).ToArray()}
        };
        internal static void Poll(IEnumerable<ClientObservation> rows,double elapsedMs)
        {
            if(!enabled)return;
            try {using(var process=Process.GetCurrentProcess())Write("monitor-poll",new {ElapsedMs=elapsedMs,LauncherMemory=process.WorkingSet64,LauncherCpuMs=process.TotalProcessorTime.TotalMilliseconds,Clients=rows.Select(Sample).ToArray()});}
            catch(Exception e){error=e.GetType().Name;}
        }
        internal static Dictionary<string,string> Read()=>store.Read();
    }
}
