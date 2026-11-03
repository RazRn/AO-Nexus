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
                        throw new IOException(AOLauncher.Localization.Texts.Get("Text_The_target_selection_changed_Reopen_Game_A27AA84B8C4C"));
                    if (string.IsNullOrWhiteSpace(target.Account.GamePath) || !Path.IsPathRooted(target.Account.GamePath))
                        throw new IOException(AOLauncher.Localization.Texts.Get("Text_Set_the_affected_account_s_game_8A2A726F8BE5"));
                }
                var inspection = await Task.Run(() => GuiSessions.Inspect(settings, targets, installationWide));
                cancellation.Token.ThrowIfCancellationRequested();
                if (inspection.BlockReason != null) throw new IOException(inspection.BlockReason);
                var clients = inspection.Clients;
                if (clients.Any(client => !client.CanCamp)) throw new IOException(AOLauncher.Localization.Texts.Get("Text_Some_affected_clients_cannot_receive_quit_183EEF72FA89"));
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
                if (installationWide) question += "\r\n\r\n" + AOLauncher.Localization.Texts.Get("Text_This_skin_is_shared_by_accounts_C0E979E2066C");
                if (clients.Count > 0)
                {
                    if (inspection.ExpandedScope) question += "\r\n\r\n" + AOLauncher.Localization.Texts.Get("Text_The_Prefs_to_installation_mapping_could_619FBAAFCE66");
                    question += "\r\n\r\n" + AOLauncher.Localization.Texts.Get("Text_Relog_these_clients_to_apply_the_888D824AC786") + "\r\n"
                        + string.Join("\r\n", clients.Select(client => "• " + client.CharacterName)) + "\r\n\r\n"
                        + AOLauncher.Localization.Texts.Get("Text_Nexus_sends_quit_waits_up_to_7F19FBD86109");
                }
                if (NexusMessageBox.Show(owner, question, AOLauncher.Localization.Texts.Get("Text_Game_GUI_D88D4981981C"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.OK) return false;
                var restartAccounts = settings.Accounts.Where(account => clients.Any(client => string.Equals(client.AccountIdentity, "id:" + account.Id, StringComparison.OrdinalIgnoreCase))).ToList();
                // Ask before quitting, so cancelling a password prompt never logs out a team.
                if (restartAccounts.Count > 0 && !EnsurePasswords(restartAccounts, out credentials)) return false;
                var request = CreateGuiRelogRequest(settings, clients, credentials);
                credentials.Clear();
                try
                {
                    SetRunning(true);
                    startButton.Text = AOLauncher.Localization.Texts.Get("Text_Updating_GUI_5967946BEF70");
                    StatusText = AOLauncher.Localization.Texts.Get("Text_STATUS_Updating_GUI_C769A6EE0557");
                    foreach (var client in clients) handles.Add(GuiSessions.OpenConfirmed(client));
                    foreach (var client in clients)
                    {
                        cancellation.Token.ThrowIfCancellationRequested();
                        SuppressCrashRelog(client);
                        string result = await Task.Run(() => PresetCamping.SendCamp(client));
                        AppendLog(client.CharacterName + ": " + Localization.Texts.T(result));
                    }
                    if (clients.Count > 0)
                    {
                        AppendLog(AOLauncher.Localization.Texts.Get("Text_Waiting_for_game_clients_to_exit_283197C171FE"));
                        bool exited = await GuiSessions.WaitForExitAsync(() => handles.All(process => process.HasExited), TimeSpan.FromSeconds(90), cancellation.Token);
                        if (!exited) throw new IOException(AOLauncher.Localization.Texts.Get("Text_Logout_did_not_finish_No_GUI_82CAC24B8DF6"));
                    }
                    cancellation.Token.ThrowIfCancellationRequested();
                    var recheck = await Task.Run(() => GuiSessions.Inspect(settings, targets, installationWide));
                    cancellation.Token.ThrowIfCancellationRequested();
                    if (recheck.BlockReason != null || recheck.Clients.Count > 0) throw new IOException(recheck.BlockReason ?? AOLauncher.Localization.Texts.Get("Text_An_affected_client_started_again_No_C2CC4B4FDC69"));
                    // File engines stage, back up and roll back writes. Cancellation is honoured before
                    // the transaction, never between its writes, to keep recovery deterministic.
                    await Task.Run(action);
                    changed = true;
                    AppendLog(description + ": " + AOLauncher.Localization.Texts.Get("Text_completed_E4662723D59F"));
                    if (!closing && !cancellation.IsCancellationRequested && request.Characters.Count > 0)
                    {
                        AppendLog(AOLauncher.Localization.Texts.Get("Text_Restarting_the_clients_that_were_closed_E0D41A79F626"));
                        SetRunning(false);
                        await RunLaunchRequestAsync(request, restartAccounts, false);
                    }
                    else if (request.Characters.Count > 0) AppendLog(AOLauncher.Localization.Texts.Get("Text_Automatic_relog_cancelled_Start_these_characters_86D85FAC7197"));
                    return true;
                }
                finally { foreach (var character in request.Characters) character.Password = null; }
            }
            catch (OperationCanceledException)
            {
                AppendLog(AOLauncher.Localization.Texts.Get("Text_GUI_operation_cancelled_Clients_already_closed_31564D95F87F")); return changed;
            }
            catch (Exception error)
            {
                DiagnosticJournal.Exception("gui", "GUI operation failed", error);
                AppendLog(AOLauncher.Localization.Texts.Get("Text_GUI_operation_795D7F5C28D9") + error.Message);
                if (!closing) NexusMessageBox.Show(owner, error.Message, AOLauncher.Localization.Texts.Get("Text_Game_GUI_D88D4981981C"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            startButton.Text = AOLauncher.Localization.Texts.Get("Text_Updating_GUI_5967946BEF70");
            StatusText = AOLauncher.Localization.Texts.Get("Text_STATUS_Applying_queued_GUI_defaults_62887C86032C");
            try
            {
            foreach (var target in targets)
            {
                if (closing || guiCancellation.IsCancellationRequested) return false;
                if (!Directory.Exists(target.CharacterPath))
                { AppendLog(target.Label + ": " + AOLauncher.Localization.Texts.Get("Text_default_GUI_is_queued_until_its_A72F0E2D9B5B")); continue; }
                try
                {
                    var inspection = await Task.Run(() => GuiSessions.Inspect(settings, new[] { target }, false));
                    if (closing || guiCancellation.IsCancellationRequested) return false;
                    if (inspection.BlockReason != null || inspection.Clients.Count > 0)
                    { AppendLog(target.Label + ": " + AOLauncher.Localization.Texts.Get("Text_default_GUI_is_pending_Close_the_457C5493674F")); continue; }
                    string templateId = target.Character.PendingGuiTemplateId;
                    await Task.Run(() => GuiLayouts.Apply(GuiLayouts.DefaultRoot, templateId, target.CharacterPath));
                    target.Character.PendingGuiTemplateId = null; SaveSettings();
                    AppendLog(target.Label + ": " + AOLauncher.Localization.Texts.Get("Text_default_GUI_applied_previous_files_backed_0138B02E699A"));
                }
                catch (Exception error) { ShowInfo(error.Message); return false; }
            }
            return !closing && !guiCancellation.IsCancellationRequested;
            }
            finally { guiCancellation.Dispose(); guiCancellation = null; guiOperationBusy = false; if (!closing) SetRunning(false); }
        }
    }
}
