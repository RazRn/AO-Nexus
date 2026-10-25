using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static AOLauncher.SidebarText;

namespace AOLauncher
{
    internal sealed partial class MainForm
    {
        private bool guiOperationBusy;
        private CancellationTokenSource guiCancellation;

        private void OpenGameGui(object sender, EventArgs e)
        {
            if (running || guiOperationBusy) return;
            using (var dialog = new GuiManagerDialog(settings, GuiLayouts.DefaultRoot, smoke))
            {
                dialog.DefaultTemplateId = settings.DefaultGuiTemplateId;
                dialog.SaveDefaultTemplate = id => { settings.DefaultGuiTemplateId = id; SaveSettings(); };
                dialog.SaveProfile = SaveSettings;
                dialog.RequestCancelOperation = () => guiCancellation?.Cancel();
                dialog.ExecuteOperation = (description, targets, installationWide, action) => ExecuteGuiOperation(dialog, description, targets, installationWide, action);
                dialog.ShowDialog(this);
            }
            RefreshTree();
        }

        private async Task<bool> ExecuteGuiOperation(IWin32Window owner, string description, List<GuiCharacterChoice> targets, bool installationWide, Action action)
        {
            if (smoke || closing || running || guiOperationBusy || targets == null || targets.Count == 0) return false;
            guiOperationBusy = true;
            guiCancellation = new CancellationTokenSource();
            var cancellation = guiCancellation;
            var handles = new List<Process>();
            var credentials = new Dictionary<string, string>();
            bool changed = false;
            try
            {
                foreach (var target in targets)
                {
                    if (!settings.Accounts.Contains(target.Account) || (!installationWide && (target.Character == null || !target.Account.Characters.Contains(target.Character))))
                        throw new IOException(L("The target selection changed. Reopen Game GUI and try again.", "Valget er ændret. Åbn Spil-GUI igen og prøv igen."));
                    if (string.IsNullOrWhiteSpace(target.Account.GamePath) || !Path.IsPathRooted(target.Account.GamePath))
                        throw new IOException(L("Set the affected account's game folder first so running clients can be checked safely.", "Vælg først den berørte kontos spilmappe, så kørende klienter kan kontrolleres sikkert."));
                }
                var inspection = await Task.Run(() => GuiSessions.Inspect(settings, targets, installationWide));
                cancellation.Token.ThrowIfCancellationRequested();
                if (inspection.BlockReason != null) throw new IOException(inspection.BlockReason);
                var clients = inspection.Clients;
                if (clients.Any(client => !client.CanCamp)) throw new IOException(L("Some affected clients cannot receive /quit. Close them normally, then try again.", "Nogle berørte klienter kan ikke modtage /quit. Luk dem normalt, og prøv igen."));
                string names = string.Join("\r\n", targets.Select(target => "• " + target.Label).Distinct().Take(20));
                string question = description + "\r\n\r\n" + names;
                var captureSource = (owner as GuiManagerDialog)?.CaptureSource;
                if (captureSource != null)
                {
                    bool sourceRunning = clients.Any(client => client.CharacterId == captureSource.Character.CharacterId
                        && string.Equals(client.Username, captureSource.Account.Username, StringComparison.OrdinalIgnoreCase)
                        && PresetCamping.SamePath(client.GamePath, captureSource.Account.GamePath));
                    question += "\r\n\r\n" + GuiSessions.CaptureNotice(captureSource.Label, sourceRunning);
                }
                if (targets.Count > 20) question += "\r\n+ " + (targets.Count - 20);
                if (installationWide) question += "\r\n\r\n" + L("This skin is shared by accounts using the same installation preferences.", "Dette skin deles af konti med de samme installationsindstillinger.");
                if (clients.Count > 0)
                {
                    if (inspection.ExpandedScope) question += "\r\n\r\n" + L("The Prefs-to-installation mapping could not be verified. For safety this includes all identified running AO clients.", "Forbindelsen mellem Prefs og installation kunne ikke verificeres. Af sikkerhedshensyn medtages alle identificerede AO-klienter.");
                    question += "\r\n\r\n" + L("Relog these clients to apply the change?", "Log disse klienter ud og ind for at anvende ændringen?") + "\r\n"
                        + string.Join("\r\n", clients.Select(client => "• " + client.CharacterName)) + "\r\n\r\n"
                        + L("Nexus sends /quit, waits up to 90 seconds for actual exit, then applies the change and logs these characters back in. Cancelled logout leaves files untouched. Clients are never forcibly closed.",
                            "Nexus sender /quit, venter op til 90 sekunder på fuld lukning, anvender ændringen og logger disse chars ind igen. Afbrudt logout efterlader filerne uændrede. Klienter tvangslukkes aldrig.");
                }
                if (NexusMessageBox.Show(owner, question, L("Game GUI", "Spil-GUI"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.OK) return false;
                var restartAccounts = settings.Accounts.Where(account => clients.Any(client => string.Equals(client.AccountIdentity, "id:" + account.Id, StringComparison.OrdinalIgnoreCase))).ToList();
                // Ask before quitting, so cancelling a password prompt never logs out a team.
                if (restartAccounts.Count > 0 && !EnsurePasswords(restartAccounts, out credentials)) return false;
                var request = CreateGuiRelogRequest(settings, clients, credentials);
                credentials.Clear();
                try
                {
                    SetRunning(true);
                    startButton.Text = L("Updating GUI…", "Opdaterer GUI…");
                    StatusText = L("STATUS · Updating GUI", "STATUS · Opdaterer GUI");
                    foreach (var client in clients) handles.Add(GuiSessions.OpenConfirmed(client));
                    foreach (var client in clients)
                    {
                        cancellation.Token.ThrowIfCancellationRequested();
                        string result = await Task.Run(() => PresetCamping.SendCamp(client));
                        AppendLog(client.CharacterName + ": " + Localization.Texts.T(result));
                    }
                    if (clients.Count > 0)
                    {
                        AppendLog(L("Waiting for game clients to exit before changing GUI files…", "Venter på at spilklienterne lukker, før GUI-filer ændres…"));
                        bool exited = await GuiSessions.WaitForExitAsync(() => handles.All(process => process.HasExited), TimeSpan.FromSeconds(90), cancellation.Token);
                        if (!exited) throw new IOException(L("Logout did not finish. No GUI files were changed. Some clients may already be closed; start them again when ready.", "Logout blev ikke færdigt. Ingen GUI-filer blev ændret. Nogle klienter kan allerede være lukket; start dem igen, når du er klar."));
                    }
                    cancellation.Token.ThrowIfCancellationRequested();
                    var recheck = await Task.Run(() => GuiSessions.Inspect(settings, targets, installationWide));
                    cancellation.Token.ThrowIfCancellationRequested();
                    if (recheck.BlockReason != null || recheck.Clients.Count > 0) throw new IOException(recheck.BlockReason ?? L("An affected client started again. No GUI files were changed.", "En berørt klient er startet igen. Ingen GUI-filer blev ændret."));
                    // File engines stage, back up and roll back writes. Cancellation is honoured before
                    // the transaction, never between its writes, to keep recovery deterministic.
                    await Task.Run(action);
                    changed = true;
                    AppendLog(description + ": " + L("completed.", "færdig."));
                    if (!closing && !cancellation.IsCancellationRequested && request.Characters.Count > 0)
                    {
                        AppendLog(L("Restarting the clients that were closed for this GUI change…", "Genstarter de klienter, der blev lukket til GUI-ændringen…"));
                        SetRunning(false);
                        await RunLaunchRequestAsync(request, restartAccounts, false);
                    }
                    else if (request.Characters.Count > 0) AppendLog(L("Automatic relog cancelled. Start these characters when ready.", "Automatisk genlogin afbrudt. Start disse chars, når du er klar."));
                    return true;
                }
                finally { foreach (var character in request.Characters) character.Password = null; }
            }
            catch (OperationCanceledException)
            {
                AppendLog(L("GUI operation cancelled. Clients already closed stay closed; no forced restart.", "GUI-handling afbrudt. Klienter, der allerede er lukket, forbliver lukket; ingen tvungen genstart.")); return changed;
            }
            catch (Exception error)
            {
                AppendLog(L("GUI operation: ", "GUI-handling: ") + error.Message);
                if (!closing) NexusMessageBox.Show(owner, error.Message, L("Game GUI", "Spil-GUI"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return changed;
            }
            finally
            {
                credentials.Clear(); foreach (var handle in handles) handle.Dispose();
                guiCancellation = null; cancellation.Dispose(); guiOperationBusy = false;
                if (!closing) SetRunning(false);
            }
        }

        internal static LaunchRequest CreateGuiRelogRequest(LauncherSettings settings, IEnumerable<PresetCamping.Client> clients, IDictionary<string, string> credentials)
        {
            // Validate an isolated selection. Never change dashboard checkboxes or a team preset.
            var selected = new LauncherSettings { LoginServers = settings.LoginServers, Accounts = new List<AccountConfig>() };
            foreach (var account in settings.Accounts)
            {
                var ids = new HashSet<int>(clients.Where(client => string.Equals(client.AccountIdentity, "id:" + account.Id, StringComparison.OrdinalIgnoreCase)
                    && PresetCamping.SamePath(account.GamePath, client.GamePath) && client.Server == account.Server && client.Port == account.Port).Select(client => client.CharacterId));
                if (ids.Count == 0) continue;
                selected.Accounts.Add(new AccountConfig { Id = account.Id, Label = account.Label, Username = account.Username, GamePath = account.GamePath,
                    Server = account.Server, Port = account.Port, ServerProfileId = account.ServerProfileId,
                    Characters = account.Characters.Where(character => ids.Contains(character.CharacterId)).Select(character => new CharacterConfig {
                        CharacterId = character.CharacterId, Name = character.Name, NamePending = character.NamePending, Selected = true }).ToList() });
            }
            if (selected.Accounts.Count == 0) return new LaunchRequest();
            ValidateSelectedAccounts(selected);
            return CreateLaunchRequest(selected, credentials);
        }

        internal static string GuiCharacterPath(AccountConfig account, CharacterConfig character)
        {
            if (string.IsNullOrWhiteSpace(account.PrefsPath) || character == null || character.CharacterId <= 0 || !Path.IsPathRooted(account.PrefsPath)) return null;
            return Path.Combine(Path.GetFullPath(account.PrefsPath), "Char" + character.CharacterId);
        }

        private async Task<bool> ApplyPendingGuiBeforeLaunch(List<AccountConfig> accounts)
        {
            var targets = accounts.SelectMany(account => account.Characters.Where(character => character.Selected && !string.IsNullOrEmpty(character.PendingGuiTemplateId))
                .Select(character => new GuiCharacterChoice { Account = account, Character = character, CharacterPath = GuiCharacterPath(account, character), Label = character.Name })).ToList();
            if (targets.Count == 0) return true;
            guiOperationBusy = true; guiCancellation = new CancellationTokenSource(); SetRunning(true);
            startButton.Text = L("Updating GUI…", "Opdaterer GUI…");
            StatusText = L("STATUS · Applying queued GUI defaults", "STATUS · Anvender ventende GUI-standarder");
            try
            {
            foreach (var target in targets)
            {
                if (closing || guiCancellation.IsCancellationRequested) return false;
                if (!Directory.Exists(target.CharacterPath))
                { AppendLog(target.Label + ": " + L("default GUI is queued until its Prefs folder exists after a normal logout.", "standard-GUI afventer en Prefs-mappe efter normalt logout.")); continue; }
                try
                {
                    var inspection = await Task.Run(() => GuiSessions.Inspect(settings, new[] { target }, false));
                    if (closing || guiCancellation.IsCancellationRequested) return false;
                    if (inspection.BlockReason != null || inspection.Clients.Count > 0)
                    { AppendLog(target.Label + ": " + L("default GUI is pending. Close the client normally or apply it in Game GUI.", "standard-GUI afventer. Luk klienten normalt eller anvend den under Spil-GUI.")); continue; }
                    string templateId = target.Character.PendingGuiTemplateId;
                    await Task.Run(() => GuiLayouts.Apply(GuiLayouts.DefaultRoot, templateId, target.CharacterPath));
                    target.Character.PendingGuiTemplateId = null; SaveSettings();
                    AppendLog(target.Label + ": " + L("default GUI applied; previous files backed up.", "standard-GUI anvendt; tidligere filer sikkerhedskopieret."));
                }
                catch (Exception error) { ShowInfo(error.Message); return false; }
            }
            return !closing && !guiCancellation.IsCancellationRequested;
            }
            finally { guiCancellation.Dispose(); guiCancellation = null; guiOperationBusy = false; if (!closing) SetRunning(false); }
        }
    }
}
