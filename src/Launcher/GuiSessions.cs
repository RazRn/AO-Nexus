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
            => L("Source character: ", "Kilde-char: ") + source + "\r\n" + (running
                ? L("The source is running. Nexus will send /quit and wait for it to exit before reading its newly saved layout, then log it back in. Capture/copy is cancelled if logout does not finish. Continue with relog and capture?",
                    "Kilden er aktiv. Nexus sender /quit og venter på fuld lukning, før det nyligt gemte layout læses, og logger derefter ind igen. Kopiering afbrydes, hvis logout ikke fuldføres. Fortsæt med genlogin og kopiering?")
                : L("The source is already closed. Has it logged out normally since the last layout changes? Continue only if its latest changes were saved. Otherwise cancel, log that character in, arrange its layout and try again; Nexus can handle the logout and relog.",
                    "Kilden er allerede lukket. Er den logget normalt ud siden de sidste layoutændringer? Fortsæt kun, hvis ændringerne er gemt. Ellers annuller, log charen ind, tilpas layoutet og prøv igen; Nexus kan håndtere logout og genlogin."));
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
                    result.BlockReason = L("The account Prefs folder must match its login username. Check Edit account → Account and Prefs before changing GUI files.", "Kontoens Prefs-mappe skal matche loginbrugernavnet. Kontroller Rediger konto → Konto og Prefs, før GUI-filer ændres."); return result;
                }
                if (target.Character != null && !PresetCamping.SamePath(target.CharacterPath, MainForm.GuiCharacterPath(target.Account, target.Character)))
                { result.BlockReason = L("The character Prefs path changed. Reopen Game GUI.", "Charens Prefs-sti er ændret. Åbn Spil-GUI igen."); return result; }
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
                        result.BlockReason = L("A game client in an affected installation cannot be identified safely. Close it normally, then try again.", "En spilklient i en berørt installation kan ikke identificeres sikkert. Luk den normalt, og prøv igen.");
                        return result;
                    }
                    var account = settings.Accounts.First(item => string.Equals(tracked.AccountIdentity, "id:" + item.Id, StringComparison.OrdinalIgnoreCase));
                    var character = account.Characters.First(item => item.CharacterId == tracked.CharacterId);
                    if (AffectsClient(targets, installationWide, narrowScope, tracked, account, character)) result.Clients.Add(tracked);
                }
                catch (InvalidOperationException) { }
                catch (Exception error) when (error is System.ComponentModel.Win32Exception || error is UnauthorizedAccessException)
                {
                    result.BlockReason = L("Nexus cannot inspect a running game client. Close the game clients normally before changing GUI files.", "Nexus kan ikke kontrollere en kørende spilklient. Luk spilklienterne normalt, før GUI-filer ændres."); return result;
                }
            }
            return result;
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
                    process.Id, process.StartTime.ToUniversalTime().Ticks, process.MainModule.FileName)) throw new IOException(L("The game process changed. No GUI files were written.", "Spilprocessen er ændret. Ingen GUI-filer blev skrevet."));
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
