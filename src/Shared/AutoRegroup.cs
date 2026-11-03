using System;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Text;
using System.Threading;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

namespace NexusTelemetry
{
    // Short-lived local permission, tied to both process lifetimes and game-loop evidence.
    public sealed class AutoRegroupPermission
    {
        public int Pid, Character, Leader, LeaderPid, Area, Channel;
        public long StartTicks, LeaderStartTicks, IssuedQpc, LeaderUpdateQpc;
        public int[] Members;
        public bool Enabled;

        internal static AutoRegroupPermission Read(string path) => WithFileLock(path, () => {
            if (!File.Exists(path)) return null;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream)) {
                if (stream.Length > 8192) return null;
                var buffer = new char[8193];
                int count = reader.ReadBlock(buffer, 0, buffer.Length);
                if (count > 8192) return null;
                return new JavaScriptSerializer().Deserialize<AutoRegroupPermission>(new string(buffer, 0, count));
            }
        });

        internal static bool Write(string path, AutoRegroupPermission permission) => WithFileLock(path, () => {
            string temporary=path+".tmp";
            File.WriteAllText(temporary,new JavaScriptSerializer().Serialize(permission));
            if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);
            return true;
        });

        static T WithFileLock<T>(string path, Func<T> action)
        {
            string key;
            using(var hash=SHA256.Create())key=BitConverter.ToString(hash.ComputeHash(
                Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant()))).Replace("-","");
            // Windows replacement can briefly deny readers even with delete sharing.
            // Both processes use a bounded lock; a missed read grants no movement.
            using(var mutex=new Mutex(false,@"Local\AONexus.Regroup."+key)) {
                bool acquired=false;
                try {
                    try { acquired=mutex.WaitOne(50); }
                    catch(AbandonedMutexException) { acquired=true; }
                    return acquired?action():default(T);
                } finally { if(acquired)mutex.ReleaseMutex(); }
            }
        }

        public bool Fresh(long now, int pid, long start)
        {
            return Enabled && Pid == pid && StartTicks == start && LeaderPid > 0 && LeaderPid != pid
                && LeaderStartTicks > 0 && Character > 0 && Leader > 0 && Leader != Character
                && Area > 0 && Channel >= 0 && Channel <= 255
                && Members != null && Members.Length >= 2 && Members.Length <= 6
                && Members.All(id => id > 0) && Members.Distinct().Count() == Members.Length
                && Members.Contains(Character) && Members.Contains(Leader)
                && Recent(now, IssuedQpc, 3) && Recent(now, LeaderUpdateQpc, 2);
        }

        static bool Recent(long now, long value, int seconds) => value > 0 && value <= now
            && now - value < seconds * Stopwatch.Frequency;
    }

    internal sealed class AutoRegroupProgress
    {
        double started, lastSample, progressAt, bestDistance;
        string terminal;
        internal void Start(double now, double distance)
        {
            started = lastSample = progressAt = now;
            bestDistance = distance;
            terminal = null;
        }

        // Terminal states latch: no repeated movement attempts after a blocked return.
        internal string Observe(double now, double distance, double arrival, bool clearPath)
        {
            if (terminal != null) return terminal;
            if (double.IsNaN(distance) || double.IsInfinity(distance) || distance < 0)
                return terminal = "Invalid position";
            if (now < lastSample || now - lastSample > 1)
                return terminal = "Client update delayed";
            lastSample = now;
            if (distance <= arrival) return terminal = "Regrouped";
            if (!clearPath) return terminal = "Path obstructed";
            if (distance > 60) return terminal = "Leader too far away";
            if (now - started >= 20) return terminal = "Regroup timed out";
            if (bestDistance - distance >= .5) { bestDistance = distance; progressAt = now; }
            if (now - progressAt >= 4) return terminal = "No progress toward leader";
            return "Regrouping";
        }
    }
}
