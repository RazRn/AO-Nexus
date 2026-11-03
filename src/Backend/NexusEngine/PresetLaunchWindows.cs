using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace NexusEngine
{
    // Bounded to one preset launch. No renderer hooks, global hotkeys, priority
    // changes or extra waits on the login/admission path.
    internal sealed class PresetLaunchWindows : IDisposable
    {
        sealed class Client {
            internal int Pid; internal long Start; internal string Folder;
            internal bool Leader, Supported, RestoredForStartup;
            internal DateTime Added=DateTime.UtcNow;
        }
        readonly object gate=new object();
        readonly List<Client> clients=new List<Client>();
        readonly CancellationTokenSource stop=new CancellationTokenSource();
        readonly int launcherPid;
        readonly Task worker;
        bool focusSuspended;
        string lastState;
        internal bool Enabled {get;}
        internal static bool SupportsMinimizedStart(StartupProfile profile)=>profile==StartupProfile.Stock || profile==StartupProfile.AOVKVerified;
        // Input timestamps include mouse movement and cannot distinguish a
        // user's selection from a new follower activating itself. Keep the
        // preset leader preferred within this batch. Leaving for another app
        // pauses management until the user returns to Nexus or the leader.
        internal static bool ShouldSuspendFocus(bool suspended,int foreground,int launcherPid,int leaderPid,IEnumerable<int> batchPids)
            =>foreground==0 ? suspended : foreground==launcherPid || foreground==leaderPid ? false : suspended || !batchPids.Contains(foreground);
        internal static bool MayRestoreLeader(int foreground,int launcherPid,int leaderPid,IEnumerable<int> batchPids)
            =>foreground!=0 && foreground!=leaderPid && (foreground==launcherPid || batchPids.Contains(foreground));

        internal PresetLaunchWindows(bool enabled,int launcherPid)
        {
            Enabled=enabled && launcherPid>0;this.launcherPid=launcherPid;
            worker=Enabled?Task.Run(Watch):Task.CompletedTask;
        }
        internal void Add(int pid,long started,string folder,bool leader,bool supported)
        {
            if(!Enabled)return;
            lock(gate) {
                if(clients.Any(c=>c.Pid==pid && c.Start==started))return;
                clients.Add(new Client {Pid=pid,Start=started,Folder=Path.GetFullPath(folder),Leader=leader,Supported=supported});
            }
        }
        async Task Watch()
        {
            try {
                while(!stop.IsCancellationRequested) {
                    try {Tick();} catch(Exception e) when(e is System.ComponentModel.Win32Exception || e is InvalidOperationException || e is ArgumentException) { }
                    await Task.Delay(100,stop.Token).ConfigureAwait(false);
                }
            } catch(OperationCanceledException) { }
        }
        void Tick()
        {
            Client[] batch;lock(gate)batch=clients.ToArray();
            var leader=batch.FirstOrDefault(c=>c.Leader);
            int foreground=Foreground();
            if(leader==null)return;
            bool suspended=ShouldSuspendFocus(focusSuspended,foreground,launcherPid,leader.Pid,batch.Select(c=>c.Pid));
            if(suspended!=focusSuspended) {
                focusSuspended=suspended;
                Console.WriteLine("Preset windows: focus management "+(suspended ? "paused for another application" : "resumed")+"; foreground="+foreground+".");
            }
            if(focusSuspended || !TryWindow(leader,out var leaderWindow,out _))return;
            if(MayRestoreLeader(foreground,launcherPid,leader.Pid,batch.Select(c=>c.Pid)) && Foreground()==foreground) {
                if(IsIconic(leaderWindow))ShowWindowAsync(leaderWindow,9);
                // Put the leader next in the normal window stack without
                // making it topmost. Minimizing our auto-activated follower
                // then lets Windows choose the leader naturally.
                SetWindowPos(leaderWindow,IntPtr.Zero,0,0,0,0,0x13);
                var activeFollower=batch.FirstOrDefault(c=>!c.Leader && c.Supported && c.Pid==foreground);
                if(activeFollower!=null && TryWindow(activeFollower,out var activeWindow,out _))
                    // Unlike SW_SHOWMINNOACTIVE, SW_MINIMIZE hands activation to
                    // the next window. Otherwise an iconic follower can remain
                    // foreground and later cause the leader to be minimized too.
                    ShowWindowAsync(activeWindow,6);
                if(SetForegroundWindow(leaderWindow)) {
                    Console.WriteLine("Preset windows: first character active.");
                }
            }
            string state="leader="+leader.Pid+" foreground="+Foreground();
            if(state!=lastState){lastState=state;Console.WriteLine("Preset windows: "+state);}
            foreach(var client in batch.Where(c=>!c.Leader && c.Supported)) {
                if(!TryWindow(client,out var window,out bool ready))continue;
                // Some renderers stop initializing while iconic. Fail open in
                // the background once, then minimize only after character-ready.
                if(!ready && !client.RestoredForStartup && (DateTime.UtcNow-client.Added).TotalSeconds>=8 && IsIconic(window)) {
                    client.RestoredForStartup=true;ShowWindowAsync(window,4); // SW_SHOWNOACTIVATE
                    Console.WriteLine("Preset windows: follower needs a background window to finish initialization.");
                    continue;
                }
                if(!ready && client.RestoredForStartup)continue;
                // Only minimize when the leader actually has focus; never fight
                // an unrelated app. This preference ends with the launch batch.
                if(Foreground()==leader.Pid && !IsIconic(window))ShowWindowAsync(window,7); // SW_SHOWMINNOACTIVE
            }
        }
        static bool TryWindow(Client client,out IntPtr window,out bool ready)
        {
            window=IntPtr.Zero;ready=false;
            try {
                using(var process=Process.GetProcessById(client.Pid)) {
                    if(process.HasExited || process.StartTime.ToUniversalTime().Ticks!=client.Start)return false;
                    if(!string.Equals(process.MainModule.FileName,Path.Combine(client.Folder,"AnarchyOnline.exe"),StringComparison.OrdinalIgnoreCase))return false;
                    window=process.MainWindowHandle;
                    ready=process.MainWindowTitle.StartsWith("Anarchy Online - ",StringComparison.Ordinal);
                    return window!=IntPtr.Zero && IsWindowVisible(window);
                }
            } catch {return false;}
        }
        static int Foreground(){GetWindowThreadProcessId(GetForegroundWindow(),out uint pid);return (int)pid;}
        public void Dispose(){stop.Cancel();worker.GetAwaiter().GetResult();stop.Dispose();}
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr window,int command);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
    }
}
