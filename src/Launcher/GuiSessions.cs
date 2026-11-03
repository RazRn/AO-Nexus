using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static AOLauncher.SidebarText;

namespace AOLauncher
{
    internal sealed class GuiSessionInspection
    {
        internal List<PresetCamping.Client> Clients = new List<PresetCamping.Client>();
        internal string BlockReason;
        internal bool ExpandedScope;
    }

    internal static class GuiSessions
    {
        internal static string CaptureNotice(string source, bool running)
            => AOLauncher.Localization.Texts.Get("Text_Source_character_1FB5F6FF7634") + source + "\r\n" + (running
                ? AOLauncher.Localization.Texts.Get("Text_The_source_is_running_Nexus_will_206DB631781F")
                : AOLauncher.Localization.Texts.Get("Text_The_source_is_already_closed_Has_38D389CDAA51"));
        // The snapshot is deliberately broader than the target selection so external clients
        // cannot be mistaken for offline characters. No command line or credential is read.
        internal static GuiSessionInspection Inspect(LauncherSettings settings, IList<GuiCharacterChoice> targets, bool installationWide)
        {
            var result = new GuiSessionInspection();
            foreach (var target in targets)
            {
                if (string.IsNullOrWhiteSpace(target.Account.PrefsPath) || !Path.IsPathRooted(target.Account.PrefsPath)
                    || !string.Equals(Path.GetFileName(target.Account.PrefsPath.TrimEnd('\\', '/')), target.Account.Username?.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    result.BlockReason = AOLauncher.Localization.Texts.Get("Text_The_account_Prefs_folder_must_match_20C608B89E39"); return result;
                }
                if (target.Character != null && !PresetCamping.SamePath(target.CharacterPath, MainForm.GuiCharacterPath(target.Account, target.Character)))
                { result.BlockReason = AOLauncher.Localization.Texts.Get("Text_The_character_Prefs_path_changed_Reopen_4DFBDD97A3C0"); return result; }
            }
            var preset = new LaunchPreset { Characters = settings.Accounts.SelectMany(account => account.Characters.Select(character => new PresetCharacter { AccountId = account.Id, CharacterId = character.CharacterId })).ToList() };
            var known = PresetCamping.Find(settings, preset);
            // Prefs can be selected manually and need not belong to the chosen game folder.
            // Missing installation logs must not turn a character-only change into a team relog.
            // Match tracked characters by identity/path even when the installation mapping is
            // unavailable. Unknown clients still block when they cannot be ruled out safely.
            var discovered = PrefsDiscovery.Find(PrefsDiscovery.DefaultRoot, null, null);
            bool narrowScope = VerifiedScope(targets, discovered);
            result.ExpandedScope = installationWide && !narrowScope;
            foreach (var process in Process.GetProcessesByName("AnarchyOnline"))
            using (process)
            {
                try
                {
                    if (process.HasExited) continue;
                    string path = Path.GetDirectoryName(process.MainModule.FileName);
                    long ticks = process.StartTime.ToUniversalTime().Ticks;
                    var tracked = known.FirstOrDefault(client => client.ProcessId == process.Id && client.StartTimeUtcTicks == ticks);
                    if (tracked == null)
                    {
                        if (narrowScope && !targets.Any(target => PresetCamping.SamePath(target.Account.GamePath, path))) continue;
                        result.BlockReason = UnidentifiedClientReason(process.MainWindowTitle, path);
                        return result;
                    }
                    var account = settings.Accounts.First(item => string.Equals(tracked.AccountIdentity, "id:" + item.Id, StringComparison.OrdinalIgnoreCase));
                    var character = account.Characters.First(item => item.CharacterId == tracked.CharacterId);
                    if (AffectsClient(targets, installationWide, narrowScope, tracked, account, character)) result.Clients.Add(tracked);
                }
                catch (InvalidOperationException) { }
                catch (Exception error) when (error is System.ComponentModel.Win32Exception || error is UnauthorizedAccessException)
                {
                    result.BlockReason = AOLauncher.Localization.Texts.Get("Text_Nexus_can_t_check_one_of_6078CF56CFC2"); return result;
                }
            }
            return result;
        }

        // Shown when an AnarchyOnline.exe is open that Nexus did not log in (or cannot match to a
        // character yet). Names the window and folder and says exactly how to fix it.
        internal static string UnidentifiedClientReason(string windowTitle, string folder)
        {
            string title = string.IsNullOrWhiteSpace(windowTitle) || !windowTitle.StartsWith("Anarchy Online - ", StringComparison.OrdinalIgnoreCase)
                ? AOLauncher.Localization.Texts.Get("Text_Anarchy_Online_at_the_login_or_74FC2B2B20CC")
                : windowTitle.Trim();
            return AOLauncher.Localization.Texts.Get("Text_Nexus_can_t_change_GUI_files_E7C8EDC0BF88")
                + "\r\n\r\n   • " + title + "\r\n     " + folder + "\r\n\r\n"
                + AOLauncher.Localization.Texts.Get("Text_The_game_saves_its_GUI_files_D9354DAD76F2")
                + "\r\n\r\n" + AOLauncher.Localization.Texts.Get("Text_How_to_fix_it_D4115B6D9D31") + "\r\n"
                + AOLauncher.Localization.Texts.Get("Text_1_Find_the_window_with_Alt_331C95DD55FE") + "\r\n"
                + AOLauncher.Localization.Texts.Get("Text_2_If_you_just_logged_in_9B89805B9B12") + "\r\n"
                + AOLauncher.Localization.Texts.Get("Text_3_Otherwise_log_out_normally_type_CEDDB8493154") + "\r\n"
                + AOLauncher.Localization.Texts.Get("Text_4_Open_Game_GUI_again_Characters_7E9C94574284");
        }

        internal static bool AffectsClient(IEnumerable<GuiCharacterChoice> targets, bool installationWide, bool verifiedScope,
            PresetCamping.Client client, AccountConfig account, CharacterConfig character)
        {
            if (installationWide) return !verifiedScope || targets.Any(target => PresetCamping.SamePath(target.Account.GamePath, client.GamePath));
            return targets.Any(target => target.Character != null && (
                PresetCamping.SamePath(target.CharacterPath, MainForm.GuiCharacterPath(account, character))
                || target.Character.CharacterId == client.CharacterId && (
                    string.Equals("id:" + target.Account.Id, client.AccountIdentity, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(target.Account.Username?.Trim(), client.Username, StringComparison.OrdinalIgnoreCase)
                        && (!verifiedScope || PresetCamping.SamePath(target.Account.GamePath, client.GamePath)))));
        }

        internal static bool MatchesProcess(int expectedPid, long expectedStart, string expectedPath, int actualPid, long actualStart, string actualPath)
            => expectedPid == actualPid && expectedStart == actualStart && PresetCamping.SamePath(expectedPath, actualPath);

        internal static bool VerifiedScope(IEnumerable<GuiCharacterChoice> targets, IEnumerable<PrefsCandidate> discovered)
            => targets.All(target => discovered.Any(candidate => PresetCamping.SamePath(candidate.PrefsPath, target.Account.PrefsPath)
                && PresetCamping.SamePath(candidate.GamePath, target.Account.GamePath)));

        internal static Process OpenConfirmed(PresetCamping.Client client)
        {
            Process process = Process.GetProcessById(client.ProcessId);
            try
            {
                // Acquiring the handle pins the process object; a later reused PID cannot satisfy this wait.
                var handle = process.Handle;
                if (process.HasExited || !MatchesProcess(client.ProcessId, client.StartTimeUtcTicks, Path.Combine(client.GamePath, "AnarchyOnline.exe"),
                    process.Id, process.StartTime.ToUniversalTime().Ticks, process.MainModule.FileName)) throw new IOException(AOLauncher.Localization.Texts.Get("Text_The_game_process_changed_No_GUI_973A26A59BA1"));
                return process;
            }
            catch { process.Dispose(); throw; }
        }

        internal static async Task<bool> WaitForExitAsync(Func<bool> exited, TimeSpan timeout, CancellationToken cancellation)
        {
            var watch = Stopwatch.StartNew();
            while (!exited())
            {
                cancellation.ThrowIfCancellationRequested();
                if (watch.Elapsed >= timeout) return false;
                await Task.Delay(200, cancellation).ConfigureAwait(false);
            }
            cancellation.ThrowIfCancellationRequested(); return true;
        }
    }
}
