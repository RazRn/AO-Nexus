using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using static AOLauncher.Localization.Texts;

namespace AOLauncher
{
    internal sealed partial class MainForm
    {
        private bool editingPreset;
        private bool shortcutActionBusy;
        private int nextHotkeyId = 0x1000;
        private readonly Dictionary<int, Tuple<string, bool>> registeredHotkeys = new Dictionary<int, Tuple<string, bool>>();
        private readonly Dictionary<string, string> hotkeyFailures = new Dictionary<string, string>();

        private string ValidatePresetHotkeys(LaunchPreset preset, HotkeyBinding start, HotkeyBinding logout)
        {
            if (start != null && !start.IsValid || logout != null && !logout.IsValid)
                return T("This shortcut is not supported.");
            if (start != null && start.SameAs(logout)) return T("Start and logout need different shortcuts.");
            foreach (var value in new[] { start, logout })
            {
                if (value == null) continue;
                foreach (var other in settings.Presets.Where(item => !ReferenceEquals(item, preset)))
                    if (value.SameAs(other.StartHotkey) || value.SameAs(other.CampHotkey))
                        return F("Shortcut already used by preset “{0}”.", other.Name);
                // Our registrations are suspended for the entire modal editor.
                if (!smoke)
                {
                    if (!RegisterHotKey(Handle, 0xBFFF, value.Modifiers | 0x4000u, (uint)value.Key))
                        return T("This shortcut is reserved or already used by another app.");
                    UnregisterHotKey(Handle, 0xBFFF);
                }
            }
            return null;
        }
        private void UnregisterPresetHotkeys()
        {
            if (IsHandleCreated) foreach (int id in registeredHotkeys.Keys) UnregisterHotKey(Handle, id);
            registeredHotkeys.Clear();
        }
        private void RegisterPresetHotkeys()
        {
            UnregisterPresetHotkeys(); hotkeyFailures.Clear();
            if (smoke || closing || !IsHandleCreated || editingPreset) return;
            foreach (var preset in settings.Presets)
                for (int action = 0; action < 2; action++)
                {
                    var binding = action == 0 ? preset.StartHotkey : preset.CampHotkey;
                    if (binding == null) continue;
                    int id = nextHotkeyId++;
                    if (!binding.IsValid || id >= 0xBFFF || !RegisterHotKey(Handle, id, binding.Modifiers | 0x4000u, (uint)binding.Key))
                    {
                        string error = F("Shortcut unavailable: {0}. Choose another combination.", binding.ToString());
                        hotkeyFailures[preset.Id] = error;
                        AppendLog(preset.Name + ": " + error);
                    }
                    else registeredHotkeys.Add(id, Tuple.Create(preset.Id, action == 1));
                }
        }
        protected override void OnHandleDestroyed(EventArgs e) { UnregisterPresetHotkeys(); base.OnHandleDestroyed(e); }
        private static bool HasBlockingShortcutWindow(Form owner)
        {
            // Persistent companion windows are part of normal play. Editors and
            // confirmations (including ones owned by a companion) still block actions.
            return owner.OwnedForms.Any(form => form.Visible &&
                (!form.Enabled || !(form is LogViewer || form is TeamWindow || form is TimersWindow)
                    || HasBlockingShortcutWindow(form)));
        }
        private bool PresetShortcutContextAvailable()
            => !running && !closing && Enabled && !editingPreset && !HasBlockingShortcutWindow(this)
                && (teamWindow == null || teamWindow.IsDisposed || !teamWindow.Visible
                    || (teamWindow.Enabled && !HasBlockingShortcutWindow(teamWindow)));
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0112 && (m.WParam.ToInt64() & 0xFFF0) == 0xF020 && trayIcon != null)
            { RequestMinimize(); return; }
            Tuple<string, bool> action;
            if (m.Msg == 0x0312 && registeredHotkeys.TryGetValue(m.WParam.ToInt32(), out action))
            {
                if (!shortcutActionBusy && PresetShortcutContextAvailable())
                {
                    shortcutActionBusy = true; // Before BeginInvoke: suppress queued repeats and modal reentrancy.
                    BeginInvoke((Action)(async () => await InvokePresetShortcut(action.Item1, action.Item2)));
                }
                return;
            }
            base.WndProc(ref m);
        }
        private async Task InvokePresetShortcut(string id, bool camp)
        {
            try
            {
                // A dialog may have opened since the Windows message was queued.
                if (smoke || !PresetShortcutContextAvailable()) return;
                var preset = settings.Presets.FirstOrDefault(item => item.Id == id);
                if (preset == null) return;
                ShowLauncher();
                if (camp) await CampPreset(preset);
                else
                {
                    var result = PresetService.Apply(settings, preset);
                    RefreshPresetPicker(); RefreshTree(); SaveSettings();
                    if (result.MissingCount > 0) AppendLog(F("{0} saved characters no longer exist in the list and were skipped.", result.MissingCount));
                    await StartSelectedCore();
                }
            }
            catch (Exception ex) { DiagnosticJournal.Exception("presets", "Preset shortcut failed", ex); if (!closing) ShowInfo(ex.Message); }
            finally { shortcutActionBusy = false; }
        }
        private async Task CampPreset(LaunchPreset preset)
        {
            var clients = PresetCamping.Find(settings, preset);
            if (clients.Count == 0)
            { ShowInfo(T("No running clients could be matched safely to this preset. Start its characters with Nexus first.")); return; }
            await CampClients(clients, count => F("Send /quit to {0} clients from preset “{1}”?", count, preset.Name));
        }
        private async Task LogoutTeamClients(List<PresetCamping.Client> requested)
        {
            if(smoke || shortcutActionBusy || !PresetShortcutContextAvailable()) return;
            shortcutActionBusy=true;
            try {
                var clients=requested.Where(PresetCamping.IsSameProcess).GroupBy(c=>c.ProcessId+":"+c.StartTimeUtcTicks).Select(g=>g.First()).ToList();
                if(clients.Count==0) { ShowInfo(AOLauncher.Localization.Texts.Get("Text_These_clients_have_closed_or_can_0E44E6EC6D6B"));return; }
                await CampClients(clients,count=>string.Format(AOLauncher.Localization.Texts.Get("Text_Log_out_0_clients_from_the_C229B59F2A22"),count),teamWindow);
            } catch(Exception ex) {
                DiagnosticJournal.Exception("team-window","Logout failed",ex);
                if(!closing) ShowInfo(ex.Message);
            } finally { shortcutActionBusy=false; }
        }
        private async Task CampClients(List<PresetCamping.Client> clients, Func<int,string> heading, IWin32Window confirmationOwner=null)
        {
            var supported = clients.Where(client => client.CanCamp).ToList();
            string names = string.Join("\r\n", clients.Select(client => "• " + client.CharacterName + " — " + client.Server + ":" + client.Port
                + (client.CanCamp ? "" : " — " + T("restart with this Nexus version to enable /quit"))));
            if (supported.Count == 0) { ShowInfo(names); return; }
            string question = heading(supported.Count)
                + "\r\n\r\n" + names + "\r\n\r\n" + T("The game handles the normal logout countdown. Combat, movement or cancellation may prevent logout. Clients are never forcibly closed.");
            if (confirmationOwner is TeamWindow) {
                string compact = supported.Count==1
                    ? string.Format(AOLauncher.Localization.Texts.Get("Text_Log_out_0_767A3261AAB4"), supported[0].CharacterName)
                    : string.Format(AOLauncher.Localization.Texts.Get("Text_Log_out_these_0_characters_84A6586DB616"), supported.Count)
                        + "\r\n\r\n" + string.Join(", ", supported.Select(client=>client.CharacterName));
                if (!ConfirmTeamWindowLogout(compact, confirmationOwner)) return;
            } else if (!ConfirmGameActionWithOwner(true, question, confirmationOwner)) return;
            foreach (var control in editingControls) control.Enabled = false;
            startButton.Enabled = false;
            var processes = new List<System.Diagnostics.Process>();
            TeamWindow movementPanel=null;
            header.BeginActivity(supported.Count);
            try
            {
                foreach (var client in supported) processes.Add(GuiSessions.OpenConfirmed(client));
                if(teamWindow!=null && !teamWindow.IsDisposed) {
                    movementPanel=teamWindow;
                    await movementPanel.PrepareMovementForLogout();
                }
                // Snapshot identities are checked again immediately before each signal. Never discover extra clients after confirmation.
                var waiting=new List<System.Diagnostics.Process>();
                foreach (var client in supported)
                {
                    if (closing) break;
                    bool accepted=false;
                    DevelopmentRecorder.Action("logout",client,"normal logout requested");
                    SuppressCrashRelog(client);
                    string result = await Task.Run(() => PresetCamping.SendCamp(client,out accepted));
                    DevelopmentRecorder.Action("logout",client,result);if(accepted)waiting.Add(processes[supported.IndexOf(client)]);
                    if (!closing) AppendLog(client.CharacterName + ": " + T(result));
                }
                var elapsed = System.Diagnostics.Stopwatch.StartNew();
                while (!closing && waiting.Count>0 && elapsed.Elapsed < TimeSpan.FromSeconds(90))
                {
                    int exited = processes.Count(process => process.HasExited);
                    header.ReportActivity(exited);
                    if (waiting.All(process=>process.HasExited)) break;
                    await Task.Delay(250);
                }
                if (!closing && processes.Any(process => !process.HasExited)) {
                    AppendLog(AOLauncher.Localization.Texts.Get("Text_Some_clients_are_still_open_Logout_D1C478726969"));
                    if(confirmationOwner is TeamWindow) ShowInfo(AOLauncher.Localization.Texts.Get("Text_Logout_did_not_complete_If_the_25CA5E62057B"));
                }
                for(int i=0;i<supported.Count;i++)DevelopmentRecorder.Action("logout",supported[i],processes[i].HasExited?"exit confirmed":"still running");
            }
            finally
            {
                movementPanel?.FinishMovementLogout();
                foreach (var process in processes) process.Dispose();
                if (!header.IsDisposed) header.EndActivity();
                if (!closing) { foreach (var control in editingControls) control.Enabled = true; UpdateSelection(); }
            }
        }
        [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    }
}
