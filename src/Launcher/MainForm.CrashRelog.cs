using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Web.Script.Serialization;
using System.Threading.Tasks;
using Nexus.Core;
using static AOLauncher.Localization.Texts;

namespace AOLauncher
{
    internal sealed partial class MainForm
    {
        readonly CrashRelogPolicy crashRelog = new CrashRelogPolicy();
        bool crashRelogBusy;
        static string CrashCharacterKey(PresetCamping.Client client) =>
            client.AccountIdentity + "|" + client.CharacterId + "|" + client.Server + ":" + client.Port + "|" + (client.GamePath ?? "").ToUpperInvariant();
        static string CrashInstanceKey(PresetCamping.Client client) => client.ProcessId + ":" + client.StartTimeUtcTicks;

        void SuppressCrashRelog(PresetCamping.Client client) =>
            crashRelog.Suppress(CrashCharacterKey(client), CrashInstanceKey(client));

        void ObserveCrashRelog()
        {
            crashRelog.SetEnabled(settings.AutoRelogCrashedClients);
            if (!settings.AutoRelogCrashedClients) return;
            foreach (var row in clientObservations.Where(r => r.Client != null && !r.ExitedUtc.HasValue && !r.Client.Pending && !r.Client.CreateCharacter))
                crashRelog.ObserveAlive(CrashCharacterKey(row.Client), CrashInstanceKey(row.Client));
            foreach (var row in clientObservations.Where(r => r.Client != null && r.ExitedUtc.HasValue))
            {
                var result = crashRelog.ObserveExit(CrashCharacterKey(row.Client), CrashInstanceKey(row.Client), row.ExitCode,
                    row.ExitedUtc.Value, DateTime.UtcNow);
                if (result != null) AppendLog(row.Name + ": " + Get("CrashRelog." + result));
            }
        }

        // Include pending launches and zoning windows, unlike the visible-client UI list.
        internal static bool HasCrashRelogReplacement(PresetCamping.Client crashed, string registryPath = null)
        {
            string path = registryPath ?? Path.Combine(SettingsStore.DirectoryPath, "Sessions", "ActiveClients.json");
            string json;
            using (var mutex = new Mutex(false, @"Local\AONexus.ActiveClients"))
            {
                bool acquired;
                try { acquired = mutex.WaitOne(250); } catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) throw new IOException("Active client list is busy.");
                try
                {
                    if (!File.Exists(path) || new FileInfo(path).Length > 1024 * 1024) throw new IOException("Active client list is unavailable.");
                    json = File.ReadAllText(path);
                }
                finally { mutex.ReleaseMutex(); }
            }
            var records = new JavaScriptSerializer { MaxJsonLength=1024*1024 }.Deserialize<List<PresetCamping.Client>>(json);
            if (records == null) throw new IOException("Invalid active client list.");
            foreach (var client in records.Where(c => c != null && CrashCharacterKey(c) == CrashCharacterKey(crashed)))
            {
                try
                {
                    using (var process = Process.GetProcessById(client.ProcessId))
                        if (!process.HasExited && process.StartTime.ToUniversalTime().Ticks == client.StartTimeUtcTicks) return true;
                }
                catch (ArgumentException) { /* The recorded process no longer exists. */ }
                // Any other inspection failure cancels recovery; absence was not established.
            }
            return false;
        }

        async Task TryCrashRelogAsync()
        {
            if (crashRelogBusy || smoke || storageBlocked || !settings.AutoRelogCrashedClients || shortcutActionBusy
                || guiOperationBusy || !PresetShortcutContextAvailable()) return;
            var pending = crashRelog.Next(DateTime.UtcNow);
            if (pending == null) return;
            crashRelogBusy = true;
            shortcutActionBusy = true;
            var prepared = new List<LaunchCharacter>();
            var credentials = new Dictionary<string, string>();
            try
            {
                var row = clientObservations.FirstOrDefault(r => r.Client != null && r.ExitedUtc.HasValue
                    && CrashInstanceKey(r.Client) == pending.InstanceKey && CrashCharacterKey(r.Client) == pending.CharacterKey);
                if (row == null) { crashRelog.Cancel(pending.CharacterKey); return; }
                var client = row.Client;
                var account = settings.Accounts.FirstOrDefault(a => "id:" + a.Id == client.AccountIdentity
                    && string.Equals(a.Username?.Trim(), client.Username, StringComparison.OrdinalIgnoreCase)
                    && a.Server == client.Server && a.Port == client.Port && PresetCamping.SamePath(a.GamePath, client.GamePath)
                    && a.Characters.Any(c => c.CharacterId == client.CharacterId));
                if (account == null || !account.RememberPassword)
                {
                    crashRelog.Cancel(pending.CharacterKey);
                    AppendLog(row.Name + ": " + Get("CrashRelog.Credentials"));
                    return;
                }
                // Re-enumerate actual clients: never restart solely from a stale UI snapshot.
                if (await Task.Run(() => HasCrashRelogReplacement(client)))
                { crashRelog.Cancel(pending.CharacterKey); return; }
                if (!settings.AutoRelogCrashedClients || !PresetShortcutContextAvailable() || guiOperationBusy) return;
                // Read only an already-saved credential. Background recovery never asks for or saves a password.
                var password = CredentialStore.Read(account);
                if (string.IsNullOrEmpty(password))
                {
                    crashRelog.Cancel(pending.CharacterKey);
                    AppendLog(row.Name + ": " + Get("CrashRelog.Credentials"));
                    return;
                }
                credentials[account.Id] = password;
                var request = CreateGuiRelogRequest(settings, new[] { client }, credentials);
                request.QuietLaunch = settings.QuietLaunch;
                request.FastStartup = settings.FastStartup;
                prepared = request.Characters.ToList();
                credentials.Clear(); password = null;
                if (prepared.Count != 1) { crashRelog.Cancel(pending.CharacterKey); return; }
                if (!crashRelog.BeginAttempt(pending, DateTime.UtcNow)) return;
                var preset = pluginInjector?.LaunchPresetFor(account.Id, client.CharacterId, client.StartTimeUtcTicks);
                pluginInjector?.RecordLaunch(account.Id, client.CharacterId, preset);
                AppendLog(row.Name + ": " + Get("CrashRelog.Starting"));
                DevelopmentRecorder.Action("crash-relog", client, "confirmed crash exit; replacement requested");
                await RunLaunchRequestAsync(request, new List<AccountConfig> { account }, false);
            }
            catch (Exception error)
            {
                crashRelog.Cancel(pending.CharacterKey);
                DiagnosticJournal.Exception("crash-relog", "Automatic crash relog failed", error);
                if (!closing && !IsDisposed) AppendLog(Get("CrashRelog.Failed"));
            }
            finally
            {
                credentials.Clear();
                foreach (var character in prepared) character.Password = null;
                shortcutActionBusy = false;
                crashRelogBusy = false;
            }
        }
    }
}
