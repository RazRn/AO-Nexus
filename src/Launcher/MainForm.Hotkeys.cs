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
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0112 && (m.WParam.ToInt64() & 0xFFF0) == 0xF020 && trayIcon != null)
            { RequestMinimize(); return; }
            Tuple<string, bool> action;
            if (m.Msg == 0x0312 && registeredHotkeys.TryGetValue(m.WParam.ToInt32(), out action))
            {
                if (!shortcutActionBusy && !running && !closing && Enabled
                    && !OwnedForms.Any(form => form.Visible && !(form is LogViewer)) && !editingPreset)
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
                if (closing || running || smoke) return;
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
            var supported = clients.Where(client => client.CanCamp).ToList();
            string names = string.Join("\r\n", clients.Select(client => "• " + client.CharacterName + " — " + client.Server + ":" + client.Port
                + (client.CanCamp ? "" : " — " + T("restart with this Nexus version to enable /quit"))));
            if (supported.Count == 0) { ShowInfo(names); return; }
            string question = F("Send /quit to {0} clients from preset “{1}”?", supported.Count, preset.Name)
                + "\r\n\r\n" + names + "\r\n\r\n" + T("The game handles the normal logout countdown. Combat, movement or cancellation may prevent logout. Clients are never forcibly closed.");
            if (!ConfirmGameAction(true, question)) return;
            foreach (var control in editingControls) control.Enabled = false;
            startButton.Enabled = false;
            var processes = new List<System.Diagnostics.Process>();
            header.BeginActivity(supported.Count);
            try
            {
                foreach (var client in supported) processes.Add(GuiSessions.OpenConfirmed(client));
                // Snapshot identities are checked again immediately before each signal. Never discover extra clients after confirmation.
                foreach (var client in supported)
                {
                    if (closing) break;
                    string result = await Task.Run(() => PresetCamping.SendCamp(client));
                    if (!closing) AppendLog(client.CharacterName + ": " + T(result));
                }
                var elapsed = System.Diagnostics.Stopwatch.StartNew();
                while (!closing && elapsed.Elapsed < TimeSpan.FromSeconds(90))
                {
                    int exited = processes.Count(process => process.HasExited);
                    header.ReportActivity(exited);
                    if (exited == processes.Count) break;
                    await Task.Delay(250);
                }
                if (!closing && processes.Any(process => !process.HasExited))
                    AppendLog(SidebarText.L("Some clients are still open. Logout may have been interrupted; no clients were forcibly closed.",
                        "Nogle klienter er stadig åbne. Logout kan være afbrudt; ingen klienter blev tvangslukket."));
            }
            finally
            {
                foreach (var process in processes) process.Dispose();
                if (!header.IsDisposed) header.EndActivity();
                if (!closing) { foreach (var control in editingControls) control.Enabled = true; UpdateSelection(); }
            }
        }
        [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    }
}
