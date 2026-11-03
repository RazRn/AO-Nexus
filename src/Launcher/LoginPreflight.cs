using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;

namespace AOLauncher
{
    // Finds game windows from an earlier login that never reached the game.
    //
    // The login engine records each client it starts as "pending" until the window
    // title shows the character. A login that times out (stuck at the login screen,
    // character selection, a dialog or a disconnect) stays pending while its window is
    // open, and the engine then skips that character on every later login with only a
    // log line ("a tracked game client is still logging in; skipped").
    //
    // Each pending window is classified so the launcher can resolve it without guessing:
    //  - Stale: no login is waiting for it any more (older than the engine's ready timeout
    //    plus a margin, or no login engine is running at all). It never reached the game,
    //    so closing it loses nothing; Nexus closes it and logs the character in.
    //  - InUse: stale, but it is the window you are looking at right now (foreground), so
    //    you may be using it by hand. Nexus asks before closing it.
    //  - InProgress: another login (another Nexus window) may still be logging it in.
    //    It is left alone and reported.
    // A window whose title already shows the character is in game and never listed.
    internal static class LoginPreflight
    {
        internal enum StuckKind { Stale, InUse, InProgress }

        internal sealed class StuckClient
        {
            internal AccountConfig Account;
            internal CharacterConfig Character;
            internal int ProcessId;
            internal DateTime StartedLocal;
            internal string WindowTitle;
            internal string ExecutablePath;
            internal bool CloseApproved;
            internal StuckKind Kind;
        }

        // The engine waits ReadyTimeoutSeconds (120 by default) for a client to reach the game.
        internal static TimeSpan AbandonedAfter(int readyTimeoutSeconds) => TimeSpan.FromSeconds(Math.Max(10, readyTimeoutSeconds) + 30);

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        static bool AnyLoginEngineRunning()
        {
            try { foreach (var process in Process.GetProcessesByName("NexusEngine")) using (process) if (!process.HasExited) return true; }
            catch { return true; } // unknown: be careful and treat logins as possibly running
            return false;
        }

        static bool IsForeground(int processId)
        {
            try { uint owner; GetWindowThreadProcessId(GetForegroundWindow(), out owner); return owner == (uint)processId; }
            catch { return false; }
        }

        internal static string RegistryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AONexus", "Sessions", "ActiveClients.json");

        internal static List<StuckClient> FindStuck(LauncherSettings settings, IEnumerable<AccountConfig> accounts, int readyTimeoutSeconds = 120)
        {
            var result = new List<StuckClient>();
            List<PresetCamping.Client> records;
            try { records = Read(); } catch { return result; }
            bool engineRunning = AnyLoginEngineRunning();
            foreach (var account in accounts)
                foreach (var character in account.Characters.Where(item => item.Selected))
                {
                    var record = records.FirstOrDefault(item => item != null && item.Pending && !item.CreateCharacter
                        && item.CharacterId == character.CharacterId
                        && string.Equals(item.AccountIdentity, "id:" + account.Id, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(item.Server, account.Server, StringComparison.OrdinalIgnoreCase) && item.Port == account.Port
                        && PresetCamping.SamePath(item.GamePath, account.GamePath));
                    if (record == null) continue;
                    var stuck = Inspect(record, character);
                    if (stuck == null) continue;
                    stuck.Account = account; stuck.Character = character;
                    bool abandoned = !engineRunning || DateTime.Now - stuck.StartedLocal > AbandonedAfter(readyTimeoutSeconds);
                    stuck.Kind = !abandoned ? StuckKind.InProgress : IsForeground(stuck.ProcessId) ? StuckKind.InUse : StuckKind.Stale;
                    result.Add(stuck);
                }
            return result;
        }

        internal static List<PresetCamping.Client> Read()
        {
            if (!File.Exists(RegistryPath)) return new List<PresetCamping.Client>();
            string json;
            using (var mutex = new Mutex(false, @"Local\AONexus.ActiveClients"))
            {
                bool acquired;
                try { acquired = mutex.WaitOne(500); } catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) throw new IOException("The active client list is busy.");
                try
                {
                    if (new FileInfo(RegistryPath).Length > 1024 * 1024) throw new IOException("The active client list is too large.");
                    json = File.ReadAllText(RegistryPath);
                }
                finally { mutex.ReleaseMutex(); }
            }
            return new JavaScriptSerializer { MaxJsonLength = 1024 * 1024, RecursionLimit = 8 }.Deserialize<List<PresetCamping.Client>>(json) ?? new List<PresetCamping.Client>();
        }

        // Same identity rules as the engine: process ID, start time and game executable.
        // A window that already shows this character is in game, not stuck.
        static StuckClient Inspect(PresetCamping.Client record, CharacterConfig character)
        {
            try
            {
                using (var process = Process.GetProcessById(record.ProcessId))
                {
                    if (process.HasExited || process.StartTime.ToUniversalTime().Ticks != record.StartTimeUtcTicks) return null;
                    if (!PresetCamping.SamePath(process.MainModule.FileName, Path.Combine(record.GamePath, "AnarchyOnline.exe"))) return null;
                    string title = process.MainWindowTitle ?? "";
                    if (title.StartsWith("Anarchy Online - ", StringComparison.OrdinalIgnoreCase)) return null;
                    return new StuckClient { ProcessId = process.Id, StartedLocal = process.StartTime, WindowTitle = title,
                        ExecutablePath = process.MainModule.FileName };
                }
            }
            catch { return null; }
        }

        // Closes a window that never reached the game: first normally, then by force.
        internal static bool CloseOrForce(StuckClient client)
        {
            if (Close(client, TimeSpan.FromSeconds(8))) return true;
            return ForceClose(client);
        }

        // Asks the game to close normally; it is only at a login or selection screen.
        internal static bool Close(StuckClient client, TimeSpan wait)
        {
            try
            {
                using (var process = Process.GetProcessById(client.ProcessId))
                {
                    if (process.HasExited) return true;
                    if (process.StartTime != client.StartedLocal) return true; // a different process now
                    if (!CanClose(client, process.StartTime, process.MainModule.FileName, process.MainWindowTitle, IsForeground(process.Id))) return false;
                    process.CloseMainWindow();
                    return process.WaitForExit((int)wait.TotalMilliseconds);
                }
            }
            catch (ArgumentException) { return true; }
            catch { return false; }
        }

        internal static bool ForceClose(StuckClient client)
        {
            try
            {
                using (var process = Process.GetProcessById(client.ProcessId))
                {
                    if (process.HasExited || process.StartTime != client.StartedLocal) return true;
                    if (!CanClose(client, process.StartTime, process.MainModule.FileName, process.MainWindowTitle, IsForeground(process.Id))) return false;
                    process.Kill();
                    return process.WaitForExit(5000);
                }
            }
            catch (ArgumentException) { return true; }
            catch { return false; }
        }

        internal static bool CanClose(StuckClient client, DateTime started, string executable, string title, bool foreground)
            => started == client.StartedLocal && !string.IsNullOrEmpty(client.ExecutablePath)
                && PresetCamping.SamePath(client.ExecutablePath, executable)
                && !(title ?? "").StartsWith("Anarchy Online - ", StringComparison.OrdinalIgnoreCase)
                && (!foreground || client.CloseApproved);
    }
}
