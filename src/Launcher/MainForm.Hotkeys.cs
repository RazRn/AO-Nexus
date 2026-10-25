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
        private HotkeyCapture startHotkeyField, campHotkeyField;
        private Label hotkeyHelp;
        private bool shortcutActionBusy;
        private int nextHotkeyId = 0x1000;
        private readonly Dictionary<int, Tuple<string, bool>> registeredHotkeys = new Dictionary<int, Tuple<string, bool>>();
        private readonly Dictionary<string, string> hotkeyFailures = new Dictionary<string, string>();

        private Control BuildPresetHotkeys()
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2, Margin = Padding.Empty };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            panel.Controls.Add(new Label { Text = T("Start shortcut"), AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 7, 8, 0) }, 0, 0);
            panel.Controls.Add(new Label { Text = T("Logout shortcut"), AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(12, 7, 8, 0) }, 2, 0);
            startHotkeyField = new HotkeyCapture { Name = "presetStartHotkey", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 2), AccessibleName = T("Start shortcut") };
            campHotkeyField = new HotkeyCapture { Name = "presetCampHotkey", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 2), AccessibleName = T("Logout shortcut") };
            hotkeyHelp = new Label { Dock = DockStyle.Fill, ForeColor = Theme.Muted, AutoEllipsis = true, Font = new Font("Segoe UI", 8F), Margin = Padding.Empty };
            panel.Controls.Add(startHotkeyField, 1, 0); panel.Controls.Add(campHotkeyField, 3, 0);
            panel.Controls.Add(hotkeyHelp, 0, 1); panel.SetColumnSpan(hotkeyHelp, 4);
            startHotkeyField.Commit = value => SaveHotkey(false, value);
            campHotkeyField.Commit = value => SaveHotkey(true, value);
            foreach (var field in new[] { startHotkeyField, campHotkeyField })
            {
                field.Error = error => { hotkeyHelp.Text = error; hotkeyHelp.ForeColor = Theme.Amber; };
                field.CaptureChanged = () => { RegisterPresetHotkeys(); RefreshHotkeyFields(); };
                editingControls.Add(field);
            }
            return panel;
        }

        private void RefreshHotkeyFields()
        {
            if (startHotkeyField == null) return;
            var preset = CurrentPreset;
            startHotkeyField.Enabled = campHotkeyField.Enabled = preset != null && !running && !shortcutActionBusy;
            startHotkeyField.Binding = preset?.StartHotkey;
            campHotkeyField.Binding = preset?.CampHotkey;
            startHotkeyField.RefreshText(); campHotkeyField.RefreshText();
            string error;
            bool failed = preset != null && hotkeyFailures.TryGetValue(preset.Id, out error);
            hotkeyHelp.Text = failed ? hotkeyFailures[preset.Id] : T("Saved per preset · Ctrl/Alt + key · Delete clears · Esc cancels · Logout uses /quit.");
            hotkeyHelp.ForeColor = failed ? Theme.Amber : Theme.Muted;
        }

        private string SaveHotkey(bool camp, HotkeyBinding value)
        {
            var preset = CurrentPreset;
            if (preset == null || running || storageBlocked) return T("Select a saved preset first.");
            if (value != null && !value.IsValid) return T("This shortcut is not supported.");
            foreach (var other in settings.Presets)
            {
                if ((!ReferenceEquals(preset, other) || camp) && value != null && value.SameAs(other.StartHotkey)
                    || (!ReferenceEquals(preset, other) || !camp) && value != null && value.SameAs(other.CampHotkey))
                    return F("Shortcut already used by preset “{0}”.", other.Name);
            }
            // Capture suspends our registrations. Probe Windows before saving; no binding is replaced on failure.
            if (!smoke && value != null)
            {
                if (!RegisterHotKey(Handle, 0xBFFF, value.Modifiers | 0x4000u, (uint)value.Key))
                    return T("This shortcut is reserved or already used by another app.");
                UnregisterHotKey(Handle, 0xBFFF);
            }
            var old = camp ? preset.CampHotkey : preset.StartHotkey;
            if (camp) preset.CampHotkey = value; else preset.StartHotkey = value;
            try { if (!smoke) SettingsStore.Save(settings); }
            catch (Exception ex)
            {
                if (camp) preset.CampHotkey = old; else preset.StartHotkey = old;
                return F("Could not save settings: {0}", ex.Message);
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
            if (smoke || closing || !IsHandleCreated || startHotkeyField?.Capturing == true || campHotkeyField?.Capturing == true) return;
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
            RefreshHotkeyFields();
        }
        protected override void OnHandleDestroyed(EventArgs e) { UnregisterPresetHotkeys(); base.OnHandleDestroyed(e); }
        protected override void WndProc(ref Message m)
        {
            Tuple<string, bool> action;
            if (m.Msg == 0x0312 && registeredHotkeys.TryGetValue(m.WParam.ToInt32(), out action))
            {
                if (!shortcutActionBusy && !running && !closing && Enabled
                    && !OwnedForms.Any(form => form.Visible) && startHotkeyField?.Capturing != true && campHotkeyField?.Capturing != true)
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
            catch (Exception ex) { if (!closing) ShowInfo(ex.Message); }
            finally { shortcutActionBusy = false; if (!closing) RefreshHotkeyFields(); }
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
            if (NexusMessageBox.Show(this, question, T("Log out preset"), MessageBoxButtons.OKCancel,
                MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
            foreach (var control in editingControls) control.Enabled = false;
            startButton.Enabled = false;
            try
            {
                // Snapshot identities are checked again immediately before each signal. Never discover extra clients after confirmation.
                foreach (var client in supported)
                {
                    if (closing) break;
                    string result = await Task.Run(() => PresetCamping.SendCamp(client));
                    if (!closing) AppendLog(client.CharacterName + ": " + T(result));
                }
            }
            finally { if (!closing) { foreach (var control in editingControls) control.Enabled = true; UpdateSelection(); } }
        }
        [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    }
}
