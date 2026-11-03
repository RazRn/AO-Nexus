using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace NexusPlayTools
{
    public enum TimerKind { Fixed, ChanceWindow, Personal, Reset }
    public sealed class BossClock
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; }
        public string Scope { get; set; }
        public int Character { get; set; }
        public TimerKind Kind { get; set; }
        public double Seconds { get; set; } = 3600;
        public double WarningSeconds { get; set; } = 600;
        public long DueUtcTicks { get; set; }
        public long Revision { get; set; }
        public long WarnedRevision { get; set; }
        public long NotifiedRevision { get; set; }
        public bool Sound { get; set; }
        public bool Repeat { get; set; }
        public bool ObserveDeaths { get; set; }
        public string Evidence { get; set; }
        public string LastObservation { get; set; }
        public long ObservedUtcTicks { get; set; }
        public List<string> History { get; set; } = new List<string>();
        public double Remaining(DateTime utc) => DueUtcTicks == 0 ? double.NaN : (DueUtcTicks - utc.Ticks) / (double)TimeSpan.TicksPerSecond;
        public string Status(DateTime utc)
        {
            double left = Remaining(utc);
            if (double.IsNaN(left)) return "Not started";
            if (left <= 0) return Kind == TimerKind.ChanceWindow ? "Possibly up - check sighting" : Kind == TimerKind.Fixed ? "Expected now - unconfirmed" : "Due now";
            var time = TimeSpan.FromSeconds(left);
            return ((int)time.TotalHours).ToString("00") + ":" + time.Minutes.ToString("00") + ":" + time.Seconds.ToString("00");
        }
        public void Start(DateTime utc, string evidence)
        {
            DueUtcTicks = utc.AddSeconds(Seconds).Ticks;
            Revision = Math.Max(Revision + 1, DateTime.UtcNow.Ticks);
            Evidence = evidence;
            History.Insert(0, utc.ToString("u") + " " + evidence);
            if (History.Count > 20) History.RemoveRange(20, History.Count - 20);
        }
        public void Validate()
        {
            Guid id;
            if (!Guid.TryParseExact(Id, "N", out id) || string.IsNullOrWhiteSpace(Name) || Name.Length > 80 || Name.Any(char.IsControl)
                || string.IsNullOrEmpty(Scope) || Scope.Length > 240 || !Enum.IsDefined(typeof(TimerKind), Kind)
                || !Finite(Seconds) || Seconds < 10 || Seconds > 366 * 86400 || !Finite(WarningSeconds) || WarningSeconds < 0 || WarningSeconds > Seconds
                || DueUtcTicks < 0 || DueUtcTicks > DateTime.MaxValue.Ticks || Character < 0)
                throw new InvalidDataException("Invalid timer. Use a name, duration 10 seconds to 366 days and a valid reminder.");
            if (Repeat && Kind != TimerKind.Reset)
                throw new InvalidDataException("Repeat interval is only available for Reset timers. Turn off Repeat or select Reset.");
            if (ObserveDeaths && (Kind == TimerKind.Personal || Kind == TimerKind.Reset))
                throw new InvalidDataException("Death observation is only available for Fixed and ChanceWindow timers.");
            if (History == null) History = new List<string>();
            if (History.Count > 20) History = History.Take(20).ToList();
        }
        static bool Finite(double n) => !double.IsNaN(n) && !double.IsInfinity(n);
    }
    public sealed class ClockData { public List<BossClock> Timers { get; set; } = new List<BossClock>(); }
    public sealed class ClockAlert { public string Name, Text; public bool Sound; }
    public static class ToolFiles
    {
        public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AONexus", "PlayTools");
        public static string Hash(string value)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").Substring(0, 32);
        }
        // All characters under one server's Prefs directory share timers. Different
        // installations/servers retain independent scopes, even with equal character IDs.
        public static string Scope(string characterPath)
        {
            for (var d = new DirectoryInfo(Path.GetFullPath(characterPath)); d != null; d = d.Parent)
                if (d.Name.Equals("Prefs", StringComparison.OrdinalIgnoreCase)) return Hash(d.FullName.ToUpperInvariant());
            throw new InvalidDataException("Cannot identify the server preferences folder.");
        }
        public static T Read<T>(string path, int limit = 262144) where T : class, new()
        {
            if (!File.Exists(path)) return new T();
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length > limit) throw new InvalidDataException("Local tool data exceeds size limit.");
            return new JavaScriptSerializer { MaxJsonLength = limit }.Deserialize<T>(Encoding.UTF8.GetString(bytes)) ?? new T();
        }
        public static void Write<T>(string path, T value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(value), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            } finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
    // Short critical sections on background I/O threads only. Windows replacement
    // can briefly remove a pathname; serialize formation readers with its publisher.
    public static class FormationMailbox
    {
        static T Locked<T>(string path,Func<T> action)
        {
            using(var gate=new Mutex(false,"Local\\NexusPanelFormation."+ToolFiles.Hash(path.ToUpperInvariant()))) {
                bool held=false;
                try {
                    try {held=gate.WaitOne(250);}catch(AbandonedMutexException){held=true;}
                    if(!held)throw new IOException("Formation snapshot is busy.");
                    return action();
                }finally{if(held)gate.ReleaseMutex();}
            }
        }
        public static T Read<T>(string path) where T:class,new() => Locked(path,()=>ToolFiles.Read<T>(path,65536));
        public static void Write<T>(string path,T state) => Locked(path,()=>{ToolFiles.Write(path,state);return true;});
    }
    public sealed class ClockStore
    {
        readonly string path, mutex;
        public ClockStore(string directory = null)
        {
            path = Path.Combine(directory ?? ToolFiles.Root, "timers.json");
            mutex = "Local\\AONexus.Clocks." + ToolFiles.Hash(Path.GetFullPath(path).ToUpperInvariant());
        }
        public ClockData Read()
        {
            var data = ToolFiles.Read<ClockData>(path);
            if (data.Timers == null || data.Timers.Count > 128) throw new InvalidDataException("Invalid timer list.");
            foreach (var timer in data.Timers) timer.Validate();
            if (data.Timers.Select(t => t.Id).Distinct().Count() != data.Timers.Count) throw new InvalidDataException("Duplicate timer IDs.");
            return data;
        }
        public void Edit(Action<ClockData> edit)
        {
            using (var gate = new Mutex(false, mutex)) {
                bool held = false;
                try {
                    try { held = gate.WaitOne(250); } catch (AbandonedMutexException) { held = true; }
                    if (!held) throw new IOException("Timers are busy; try again.");
                    var data = Read(); edit(data);
                    if (data.Timers.Count > 128) throw new InvalidDataException("At most 128 timers.");
                    foreach (var timer in data.Timers) timer.Validate();
                    ToolFiles.Write(path, data);
                } finally { if (held) gate.ReleaseMutex(); }
            }
        }
        public List<ClockAlert> ClaimAlerts(DateTime utc)
        {
            var result = new List<ClockAlert>();
            // No writes every tick: only enter the write transaction for eligible timers.
            Func<BossClock, bool> eligible = t => t.DueUtcTicks > 0 && (t.Remaining(utc) <= 0 ? t.NotifiedRevision != t.Revision : t.WarningSeconds > 0 && t.Remaining(utc) <= t.WarningSeconds && t.WarnedRevision != t.Revision);
            if (!Read().Timers.Any(eligible)) return result;
            Edit(data => {
                foreach (var t in data.Timers.Where(eligible)) {
                    bool due = t.Remaining(utc) <= 0;
                    result.Add(new ClockAlert { Name = t.Name, Text = due ? t.Status(utc) : "Due in " + Math.Ceiling(t.Remaining(utc) / 60) + " minutes", Sound = t.Sound });
                    t.WarnedRevision = t.Revision;
                    if (due) {
                        t.NotifiedRevision = t.Revision;
                        if (t.Repeat && t.Kind == TimerKind.Reset) {
                            long interval = checked((long)(t.Seconds * TimeSpan.TicksPerSecond));
                            long steps = (utc.Ticks - t.DueUtcTicks) / interval + 1;
                            t.DueUtcTicks = checked(t.DueUtcTicks + steps * interval); t.Revision++;
                        }
                    }
                }
            });
            return result;
        }
    }
    public enum FormationShape { Column, Line, Wedge, EchelonLeft, EchelonRight, Ring }
    public sealed class FormationOptions
    {
        public FormationShape Shape { get; set; } = FormationShape.Wedge;
        public float Side { get; set; } = 3;
        public float Row { get; set; } = 4;
        public float Behind { get; set; } = 5;
        public int Slot { get; set; } = 1;
        public int Leader { get; set; }
        public bool Walk { get; set; }
        public bool Hold { get; set; }
        public void Validate()
        {
            if (!Enum.IsDefined(typeof(FormationShape), Shape) || Slot < 1 || Slot > 11 || (Shape == FormationShape.Ring && Slot > 6) || Leader < 0
                || !Valid(Side) || !Valid(Row) || !Valid(Behind)) throw new InvalidDataException("Formation: use slot 1-11 and distances 1-20 metres.");
        }
        static bool Valid(float n) => !float.IsNaN(n) && n >= 1 && n <= 20;
    }
    public struct FormationPoint { public float X, Z; public FormationPoint(float x, float z) { X = x; Z = z; } }
    public static class FormationGeometry
    {
        public static FormationPoint Offset(FormationOptions o)
        {
            o.Validate(); int rank = (o.Slot + 1) / 2; int side = o.Slot % 2 == 1 ? -1 : 1;
            switch (o.Shape) {
                case FormationShape.Line: return new FormationPoint(side * rank * o.Side, -o.Behind);
                case FormationShape.Wedge: return new FormationPoint(side * rank * o.Side, -o.Behind - (rank - 1) * o.Row);
                case FormationShape.EchelonLeft: return new FormationPoint(-o.Slot * o.Side, -o.Behind - (o.Slot - 1) * o.Row);
                case FormationShape.EchelonRight: return new FormationPoint(o.Slot * o.Side, -o.Behind - (o.Slot - 1) * o.Row);
                case FormationShape.Ring: double angle = (o.Slot - 1) * Math.PI * 2 / 6; return new FormationPoint((float)Math.Sin(angle) * o.Behind, -(float)Math.Cos(angle) * o.Behind);
                default: return new FormationPoint(0, -o.Behind - (o.Slot - 1) * o.Row);
            }
        }
        public static FormationPoint Target(FormationOptions options, float x, float z, float forwardX, float forwardZ)
        {
            var offset = Offset(options); float length = (float)Math.Sqrt(forwardX * forwardX + forwardZ * forwardZ);
            if (length < .01f || float.IsNaN(length) || float.IsInfinity(length)) throw new InvalidDataException("Unknown leader heading.");
            forwardX /= length; forwardZ /= length;
            return new FormationPoint(x + offset.X * forwardZ + offset.Z * forwardX, z - offset.X * forwardX + offset.Z * forwardZ);
        }
    }
    public sealed class FormationBeacon
    {
        public string Session, LeaderName;
        public int Channel;
        public int[] Members;
        public int Leader, Pid, Playfield;
        public long StartTicks, UtcTicks, SessionUtc;
        public float X, Y, Z, ForwardX, ForwardZ;
        public bool Active;
        public FormationOptions Options;
        public int SlotFor(int character, int channel, int[] currentTeam, DateTime now, long joinedAfter)
        {
            Guid token;
            if (!Fresh(now) || !Guid.TryParseExact(Session, "N", out token) || StartTicks <= 0 || Pid <= 0
                || Channel != channel || Members == null || Members.Length < 2 || Members.Length > 6
                || Members.Distinct().Count() != Members.Length || !Members.Contains(Leader)
                || currentTeam == null || !currentTeam.Contains(Leader) || !currentTeam.Contains(character)
                || SessionUtc < joinedAfter || character == Leader) return 0;
            return Array.IndexOf(Members.Where(id => id != Leader).OrderBy(id => id).ToArray(), character) + 1;
        }
        public bool Fresh(DateTime utc) => Active && UtcTicks <= utc.Ticks && utc.Ticks - UtcTicks < TimeSpan.TicksPerSecond * 2;
    }
}
