using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using static AOLauncher.SidebarText;

namespace AOLauncher
{
    // Window management only: no injection, FPS cap, priority or graphics changes.
    // Keep this opt-in until more client versions and real plugin workloads are measured.
    internal sealed class BackgroundClients : IDisposable
    {
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window, int command);
        private readonly Func<LauncherSettings> settings;
        private readonly Timer timer = new Timer { Interval = 250 };
        private readonly Dictionary<string, Tuple<string, Support>> supported = new Dictionary<string, Tuple<string, Support>>(StringComparer.OrdinalIgnoreCase);
        private readonly Action<string> report;
        private HashSet<string> allowedFolders=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool refreshing,disposed;
        private int generation;
        internal string Status {get;private set;}=AOLauncher.Localization.Texts.Get("Text_Auto_minimize_off_CD89AA988020");
        internal sealed class Support {internal bool Allowed, RequiresAovk;internal string Description;}
        private List<PresetCamping.Client> clients = new List<PresetCamping.Client>();
        private long lastDiscovery;
        private uint previousForeground;
        private bool enabled;
        private bool presetStartup;
        internal void ManagePresetStartup(bool active) {presetStartup=active;previousForeground=0;}
        internal BackgroundClients(Func<LauncherSettings> settingsProvider,Action<string> reportStatus=null)
        {
            settings = settingsProvider;report=reportStatus;
            timer.Tick += Tick;
        }
        internal void SetEnabled(bool value)
        {
            enabled = value;
            generation++;clients.Clear();allowedFolders.Clear();lastDiscovery = 0; previousForeground = 0;
            SetStatus(value?AOLauncher.Localization.Texts.Get("Text_Auto_minimize_checking_clients_45F7ED9CD20F"):AOLauncher.Localization.Texts.Get("Text_Auto_minimize_off_CD89AA988020"));
            if (value) timer.Start(); else timer.Stop();
            // Windows retains the original normal/maximized geometry. Disabling
            // stops managing windows; it does not bring six games to the front.
        }
        internal static bool ShouldMinimize(bool enabled, bool stableFocus, bool foregroundIsGame, int clientPid, uint foregroundPid, bool visible, bool minimized)
            => enabled && stableFocus && foregroundIsGame && foregroundPid != 0 && clientPid != foregroundPid && visible && !minimized;

        private void Tick(object sender, EventArgs args)
        {
            if (!enabled || presetStartup) return;
            try
            {
                uint foreground;
                GetWindowThreadProcessId(GetForegroundWindow(), out foreground);
                bool stable = foreground == previousForeground;
                previousForeground = foreground;
                long now = Stopwatch.GetTimestamp();
                if (lastDiscovery == 0 || (now - lastDiscovery) / (double)Stopwatch.Frequency >= 2)
                {
                    lastDiscovery = now;
                    RefreshClients();
                }
                // Browsers, Nexus and other applications must leave the current
                // game's window alone. Only another verified AO session triggers this.
                var foregroundClient = clients.FirstOrDefault(c => c.ProcessId == foreground);
                bool foregroundIsGame = foregroundClient != null && PresetCamping.IsSameProcess(foregroundClient);
                if (!stable || !foregroundIsGame) return;
                foreach (var client in clients)
                {
                    // Revalidate PID, start time, executable and character before touching its window.
                    if (client.ProcessId == foreground || !allowedFolders.Contains(client.GamePath) || !PresetCamping.IsSameProcess(client)) continue;
                    using (var process = Process.GetProcessById(client.ProcessId))
                    {
                        if (process.HasExited || process.StartTime.ToUniversalTime().Ticks != client.StartTimeUtcTicks) continue;
                        var window = process.MainWindowHandle;
                        if (window == IntPtr.Zero || !ShouldMinimize(enabled, stable, foregroundIsGame, client.ProcessId, foreground, IsWindowVisible(window), IsIconic(window))) continue;
                        // Never steal focus from the newly selected game or another application.
                        uint current;
                        GetWindowThreadProcessId(GetForegroundWindow(), out current);
                        if (current != foreground || current == client.ProcessId) continue;
                        if(MinimizeWindow(window))DevelopmentRecorder.Action("background-minimize",client,"window minimized without activation");
                    }
                }
            }
            catch(Exception error) { clients.Clear();SetStatus(AOLauncher.Localization.Texts.Get("Text_Auto_minimize_paused_FB26C4A4466B")+error.GetType().Name); }
        }
        internal static bool MinimizeWindow(IntPtr window)=>window!=IntPtr.Zero && ShowWindowAsync(window,7); // SW_SHOWMINNOACTIVE
        void SetStatus(string status){if(Status==status)return;Status=status;report?.Invoke(status);}
        async void RefreshClients()
        {
            if(refreshing || disposed)return;refreshing=true;int version=generation;
            try {
                // Files and renderer hashes are inspected on a worker, not the 250 ms UI timer.
                var json=new JavaScriptSerializer();var snapshot=json.Deserialize<LauncherSettings>(json.Serialize(settings()));
                var all=new LaunchPreset {Characters=snapshot.Accounts.SelectMany(a=>a.Characters.Select(c=>new PresetCharacter {AccountId=a.Id,CharacterId=c.CharacterId})).ToList()};
                var result=await Task.Run(()=>{
                    var found=PresetCamping.Find(snapshot,all);
                    var modes=new Dictionary<string,Support>(StringComparer.OrdinalIgnoreCase);
                    foreach(string folder in found.Select(c=>c.GamePath).Distinct(StringComparer.OrdinalIgnoreCase)){ var mode=CachedSupport(folder); modes[folder]=!snapshot.ExperimentalAovk && mode.RequiresAovk ? new Support {Description=AOLauncher.Localization.Texts.Get("Experiments.EnableAovk")} : mode; }
                    return Tuple.Create(found,modes);
                });
                if(disposed || !enabled || generation!=version)return;
                clients=result.Item1;allowedFolders=new HashSet<string>(result.Item2.Where(p=>p.Value.Allowed).Select(p=>p.Key),StringComparer.OrdinalIgnoreCase);
                int ready=clients.Count(c=>allowedFolders.Contains(c.GamePath)),skipped=clients.Count-ready;
                string status=clients.Count==0?AOLauncher.Localization.Texts.Get("Text_Auto_minimize_waiting_for_Nexus_managed_07E5EF13CC14")
                    :string.Format(AOLauncher.Localization.Texts.Get("Text_Auto_minimize_0_supported_1_skipped_63099102AAC1"),ready,skipped)
                        +" · "+string.Join("; ",result.Item2.Select(p=>Path.GetFileName(p.Key.TrimEnd('\\','/'))+": "+p.Value.Description));
                SetStatus(status);
            } catch(Exception error){if(!disposed && version==generation){clients.Clear();allowedFolders.Clear();SetStatus(AOLauncher.Localization.Texts.Get("Text_Auto_minimize_unavailable_3F3E37BBD064")+error.Message);}}
            finally {refreshing=false;}
        }
        Support CachedSupport(string folder)
        {
            try {
                // Include the patch, backups, config and client files so an AO-VK switch/repair invalidates the result.
                string stamp=string.Join("|",new[]{"randy31.dll","version.dll","randy31_orig.dll","version-stock-rebirth.dll","AnarchyOnline.exe","GUI.dll","Interfaces.dll","N3.dll","Cheetah.dll","NexusGraphics.pending.json","randy-vk.ini","randy31-aovk-test.dll","version-aovk-test.dll"}
                    .Select(name=>{var f=new FileInfo(Path.Combine(folder,name));return f.Exists?name+":"+f.Length+":"+f.LastWriteTimeUtc.Ticks:name+":missing";}));
                if(supported.TryGetValue(folder,out var previous) && previous.Item1==stamp)return previous.Item2;
                var mode=Inspect(folder);supported[folder]=Tuple.Create(stamp,mode);return mode;
            } catch(Exception error){return new Support {Description=AOLauncher.Localization.Texts.Get("Text_inspection_failed_C7A00A282681")+error.GetType().Name};}
        }
        internal static bool KnownVkRenderer(string hash)=>new[]{AovkGraphics.VkRenderer,AovkGraphics.Release081Renderer,AovkGraphics.UiRenderer,AovkGraphics.PreviousUiRenderer,AovkGraphics.PreviousUi13Renderer,AovkGraphics.PreviousUi14Renderer,AovkGraphics.PreviousUi15Renderer}.Contains(hash);
        internal static Support Inspect(string folder)
        {
            try {
                if(File.Exists(Path.Combine(folder,"Cheetah.dll")))return new Support {Description=AOLauncher.Localization.Texts.Get("Text_new_engine_is_not_supported_B0D788077C99")};
                AovkGraphics.EnsureReadyForLaunch(folder);
                string file=Path.Combine(folder,"randy31.dll");
                if(!File.Exists(file))return new Support {Description=AOLauncher.Localization.Texts.Get("Text_renderer_missing_2FC22DDB7DFF")};
                string hash;using(var stream=File.OpenRead(file))using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");
                if(hash==AovkGraphics.StockRenderer)return new Support {Allowed=true,Description=AOLauncher.Localization.Texts.Get("Text_classic_renderer_BF6244FD13C8")};
                if(!KnownVkRenderer(hash))return new Support {Description=AOLauncher.Localization.Texts.Get("Text_renderer_not_supported_FA19B674A14A")};
                var graphics=AovkGraphics.Read(folder); // Reuse the matched renderer/client/patch/backup validation.
                return new Support {Allowed=graphics.Enabled,RequiresAovk=true,Description=AOLauncher.Localization.Texts.Get("Text_AO_VK_experimental_1148983CF035")};
            } catch(Exception error){return new Support {Description=AOLauncher.Localization.Texts.Get("Text_skipped_76D597C0FA0B")+error.Message};}
        }
        public void Dispose() { disposed=true;generation++;enabled = false; timer.Stop(); timer.Dispose(); clients.Clear(); }
    }
}
