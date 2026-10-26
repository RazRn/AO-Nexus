using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows.Forms;

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
        private readonly Dictionary<string, Tuple<DateTime, bool>> supported = new Dictionary<string, Tuple<DateTime, bool>>(StringComparer.OrdinalIgnoreCase);
        private List<PresetCamping.Client> clients = new List<PresetCamping.Client>();
        private long lastDiscovery;
        private uint previousForeground;
        private bool enabled;
        internal BackgroundClients(Func<LauncherSettings> settingsProvider)
        {
            settings = settingsProvider;
            timer.Tick += Tick;
        }
        internal void SetEnabled(bool value)
        {
            enabled = value;
            clients.Clear(); lastDiscovery = 0; previousForeground = 0;
            if (value) timer.Start(); else timer.Stop();
            // Windows retains the original normal/maximized geometry. Disabling
            // stops managing windows; it does not bring six games to the front.
        }
        internal static bool ShouldMinimize(bool enabled, bool stableFocus, bool foregroundIsGame, int clientPid, uint foregroundPid, bool visible, bool minimized)
            => enabled && stableFocus && foregroundIsGame && foregroundPid != 0 && clientPid != foregroundPid && visible && !minimized;

        private void Tick(object sender, EventArgs args)
        {
            if (!enabled) return;
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
                    var value = settings();
                    var all = new LaunchPreset { Characters = value.Accounts.SelectMany(a => a.Characters.Select(c =>
                        new PresetCharacter { AccountId = a.Id, CharacterId = c.CharacterId })).ToList() };
                    clients = PresetCamping.Find(value, all);
                }
                // Browsers, Nexus and other applications must leave the current
                // game's window alone. Only another verified AO session triggers this.
                var foregroundClient = clients.FirstOrDefault(c => c.ProcessId == foreground);
                bool foregroundIsGame = foregroundClient != null && PresetCamping.IsSameProcess(foregroundClient);
                if (!stable || !foregroundIsGame) return;
                foreach (var client in clients)
                {
                    // Revalidate PID, start time, executable and character before touching its window.
                    if (client.ProcessId == foreground || !Supported(client.GamePath) || !PresetCamping.IsSameProcess(client)) continue;
                    using (var process = Process.GetProcessById(client.ProcessId))
                    {
                        if (process.HasExited || process.StartTime.ToUniversalTime().Ticks != client.StartTimeUtcTicks) continue;
                        var window = process.MainWindowHandle;
                        if (window == IntPtr.Zero || !ShouldMinimize(enabled, stable, foregroundIsGame, client.ProcessId, foreground, IsWindowVisible(window), IsIconic(window))) continue;
                        // Never steal focus from the newly selected game or another application.
                        uint current;
                        GetWindowThreadProcessId(GetForegroundWindow(), out current);
                        if (current != foreground || current == client.ProcessId) continue;
                        ShowWindowAsync(window, 7); // SW_SHOWMINNOACTIVE
                    }
                }
            }
            catch { clients.Clear(); } // A busy/missing registry or exited process is not permission to guess.
        }
        private bool Supported(string gamePath)
        {
            string file = Path.Combine(gamePath, "randy31.dll");
            if (File.Exists(Path.Combine(gamePath, "Cheetah.dll")) || !File.Exists(file)) return false;
            var stamp = File.GetLastWriteTimeUtc(file);
            Tuple<DateTime, bool> cached;
            if (supported.TryGetValue(file, out cached) && cached.Item1 == stamp) return cached.Item2;
            bool valid;
            using (var stream = File.OpenRead(file)) using (var hash = SHA256.Create())
                valid = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "") == "9D9E0DA25AC6F8C7ECDCE7A47A2409575945966A29E3B77850F7C62033A04BE3";
            supported[file] = Tuple.Create(stamp, valid);
            return valid;
        }
        public void Dispose() { enabled = false; timer.Stop(); timer.Dispose(); clients.Clear(); }
    }
}
