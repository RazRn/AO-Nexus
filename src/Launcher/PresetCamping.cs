using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Threading;
using System.Web.Script.Serialization;

namespace AOLauncher
{
    // No passwords, arbitrary commands, keyboard injection or process termination in this protocol.
    internal static class PresetCamping
    {
        internal sealed class Client
        {
            public string AccountIdentity { get; set; }
            public string Username { get; set; }
            public bool Pending { get; set; }
            public bool CreateCharacter { get; set; }
            public string Server { get; set; }
            public int Port { get; set; }
            public int CharacterId { get; set; }
            public string GamePath { get; set; }
            public int ProcessId { get; set; }
            public long StartTimeUtcTicks { get; set; }
            public string CharacterName { get; set; }
            public string CampToken { get; set; }
            internal bool CanCamp => ValidToken(CampToken) && EventExists(EventName(this, "request"));
        }
        internal static bool ValidToken(string token) => token != null && token.Length == 32 && token.All(c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f');
        internal static string EventName(Client client, string kind) => @"Local\AONexus.Camp.v1." + client.ProcessId + "." + client.CampToken + "." + kind;
        private static bool EventExists(string name)
        {
            try { using (var handle = EventWaitHandle.OpenExisting(name, EventWaitHandleRights.Synchronize)) return true; }
            catch { return false; }
        }
        internal static List<Client> Find(LauncherSettings settings, LaunchPreset preset)
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AOQuickLauncher", "ActiveClients.json");
            if (!File.Exists(path)) return new List<Client>();
            string json;
            using (var mutex = new Mutex(false, @"Local\AOQuickLauncher.ActiveClients"))
            {
                bool acquired;
                try { acquired = mutex.WaitOne(250); } catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) throw new IOException("The active client list is busy. Try again.");
                try
                {
                    if (new FileInfo(path).Length > 1024 * 1024) throw new IOException("The active client list is too large.");
                    json = File.ReadAllText(path);
                }
                finally { mutex.ReleaseMutex(); }
            }
            var records = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024, RecursionLimit = 8 }.Deserialize<List<Client>>(json);
            return Match(settings, preset, records).Where(IsSameProcess).GroupBy(c => c.ProcessId).Select(group => group.First()).ToList();
        }
        internal static IEnumerable<Client> Match(LauncherSettings settings, LaunchPreset preset, IEnumerable<Client> records)
        {
            return (records ?? Enumerable.Empty<Client>()).Take(128).Where(client => client != null && !client.Pending && !client.CreateCharacter
                && client.CharacterId > 0 && !string.IsNullOrWhiteSpace(client.CharacterName)
                && preset.Characters.Any(reference => reference.CharacterId == client.CharacterId
                    && settings.Accounts.Any(account => string.Equals(account.Id, reference.AccountId, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(client.AccountIdentity, "id:" + account.Id, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(client.Username, account.Username?.Trim(), StringComparison.OrdinalIgnoreCase)
                        && account.Characters.Any(character => character.CharacterId == reference.CharacterId)
                        && string.Equals(account.Server, client.Server, StringComparison.OrdinalIgnoreCase) && account.Port == client.Port
                        && SamePath(account.GamePath, client.GamePath))));
        }
        internal static bool SamePath(string a, string b)
        {
            try { return !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b)
                && string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }
        internal static bool IsSameProcess(Client client)
        {
            try
            {
                using (var process = Process.GetProcessById(client.ProcessId))
                    return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == client.StartTimeUtcTicks
                        && SamePath(process.MainModule.FileName, Path.Combine(client.GamePath, "AnarchyOnline.exe"))
                        && string.Equals(process.MainWindowTitle, "Anarchy Online - " + client.CharacterName, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
        internal static string SendCamp(Client client)
        {
            if (!IsSameProcess(client) || !ValidToken(client.CampToken)) return "Client changed or exited; /camp was not sent.";
            try
            {
                using (var request = EventWaitHandle.OpenExisting(EventName(client, "request"), EventWaitHandleRights.Modify))
                using (var accepted = EventWaitHandle.OpenExisting(EventName(client, "accepted"), EventWaitHandleRights.Modify | EventWaitHandleRights.Synchronize))
                using (var rejected = EventWaitHandle.OpenExisting(EventName(client, "rejected"), EventWaitHandleRights.Modify | EventWaitHandleRights.Synchronize))
                {
                    accepted.Reset(); rejected.Reset();
                    if (!IsSameProcess(client)) return "Client changed or exited; /camp was not sent.";
                    request.Set();
                    int reply = WaitHandle.WaitAny(new WaitHandle[] { accepted, rejected }, 3000);
                    if (reply == 0) return "/camp accepted by the client. Waiting for the game's normal logout; no forced close.";
                    if (reply == 1) return "The client declined /camp. Check combat, zoning or its current character.";
                    request.Reset();
                    return "No /camp acknowledgement. Check the game window; no forced close or automatic retry.";
                }
            }
            catch { return "/camp is unavailable. Restart this character using the updated Nexus."; }
        }
    }
}
