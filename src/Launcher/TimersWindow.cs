using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using NexusPlayTools;
using static AOLauncher.SidebarText;

namespace AOLauncher
{
    internal sealed partial class MainForm
    {
        TimersWindow timersWindow;
        Timer bossTimer;
        NotifyIcon bossTray;
        bool checkingClocks;
        void ShowTimers()
        {
            if (timersWindow == null || timersWindow.IsDisposed) { timersWindow = new TimersWindow(settings); timersWindow.Show(this); }
            else { timersWindow.WindowState = FormWindowState.Normal; timersWindow.Activate(); }
        }
        void InitializeBossTimers()
        {
            if (smoke) return;
            bossTray = new NotifyIcon { Icon = Icon, Text = "AO Nexus timers" };
            bossTray.BalloonTipClicked += (s,e) => ShowTimers();
            bossTray.MouseClick += (s,e) => ShowTimers();
            bossTimer = new Timer { Interval = 1000 };
            bossTimer.Tick += async (s,e) => {
                if (checkingClocks || closing) return;
                checkingClocks = true;
                try {
                    var alerts = await Task.Run(() => new ClockStore().ClaimAlerts(DateTime.UtcNow));
                    if (!closing && alerts.Count > 0) {
                        bossTray.Visible = true;
                        bossTray.ShowBalloonTip(8000, "AO Nexus timers", string.Join("\n", alerts.Take(5).Select(a => a.Name + ": " + a.Text)), ToolTipIcon.Info);
                        if (alerts.Any(a => a.Sound)) System.Media.SystemSounds.Asterisk.Play();
                    }
                } catch (Exception) { /* Optional reminder failure must not close the launcher. */ }
                finally { checkingClocks = false; }
            };
            bossTimer.Start();
        }
    }
    internal sealed class TimersWindow : Form
    {
        internal TimersWindow(LauncherSettings settings, ClockStore testStore=null)
        {
            Text=AOLauncher.Localization.Texts.Get("Text_AO_Nexus_Timers_C5B0BC712B55");Font=new Font("Segoe UI",10);
            Size=new Size(880,650);MinimumSize=new Size(680,520);StartPosition=FormStartPosition.CenterParent;
            Controls.Add(new TimersPanel(settings,null,null,testStore) {Dock=DockStyle.Fill});
        }
    }

    internal sealed class TimersPanel : UserControl
    {
        sealed class ScopeChoice { internal string Id, Label; public override string ToString() => Label; }
        readonly ClockStore store;
        readonly NexusComboBox scope = new NexusComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
        readonly ListBox list = new ListBox { Dock = DockStyle.Fill, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 62, BorderStyle = BorderStyle.None, BackColor = Theme.Background, ForeColor = Theme.Foreground };
        readonly TextBox details = new TextBox { Dock = DockStyle.Bottom, Height = 110, Multiline = true, ReadOnly = true, BackColor = Theme.Surface, ForeColor = Theme.Muted, BorderStyle = BorderStyle.FixedSingle, ScrollBars = ScrollBars.Vertical };
        readonly Timer refresh = new Timer { Interval = 1000 };
        internal TimersPanel(LauncherSettings settings, Action<string> saveScope=null, string savedScope=null, ClockStore testStore = null)
        {
            store = testStore ?? new ClockStore();
            Text = AOLauncher.Localization.Texts.Get("Text_AO_Nexus_Timers_C5B0BC712B55"); BackColor = Theme.Background; ForeColor = Theme.Foreground;
            Font = new Font("Segoe UI", 10);
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 50, Padding = new Padding(12), WrapContents = false };
            top.Controls.Add(scope);
            var choices = new Dictionary<string, ScopeChoice>();
            foreach (var account in settings.Accounts) {
                try { var id = ToolFiles.Scope(account.PrefsPath); if (!choices.ContainsKey(id)) choices.Add(id, new ScopeChoice { Id = id, Label = account.Label + " · " + account.Server + ":" + account.Port }); }
                catch { }
            }
            foreach (var choice in choices.Values) scope.Items.Add(choice);
            if (scope.Items.Count > 0) scope.SelectedItem=choices.Values.FirstOrDefault(c=>c.Id==savedScope)??choices.Values.First();
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 90, Padding = new Padding(10), WrapContents = true };
            Add(buttons, AOLauncher.Localization.Texts.Get("Text_New_timer_A2F536A0DB4D"), () => Edit(null));
            Add(buttons, AOLauncher.Localization.Texts.Get("Text_Edit_07C74AF669DF"), () => { if (Selected != null) Edit(Selected); });
            Add(buttons, AOLauncher.Localization.Texts.Get("Text_Killed_start_now_D18ED176BD3B"), () => Change(t => t.Start(DateTime.UtcNow, "Entered manually in Nexus")));
            Add(buttons, AOLauncher.Localization.Texts.Get("Text_Killed_at_8607E231BEB0"), KilledAt);
            Add(buttons, AOLauncher.Localization.Texts.Get("Text_Set_due_time_01195CDDC177"), SetDue);
            Add(buttons, AOLauncher.Localization.Texts.Get("Text_Stop_54ED8C868A9E"), () => Change(t => { t.DueUtcTicks = 0; t.Evidence = "Stopped manually"; }));
            Add(buttons, AOLauncher.Localization.Texts.Get("Text_Delete_50C904C3FE9F"), () => {
                var selected = Selected;
                if (selected != null && MessageBox.Show(this, AOLauncher.Localization.Texts.Get("Text_Delete_timer_7E706FD79953") + selected.Name + "?", Text, MessageBoxButtons.YesNo) == DialogResult.Yes)
                    Run(() => store.Edit(data => data.Timers.RemoveAll(t => t.Id == selected.Id)));
            });
            var help = new Label { Dock = DockStyle.Bottom, Height = 50, Padding = new Padding(12,6,12,0), ForeColor = Theme.Muted,
                Text = AOLauncher.Localization.Texts.Get("Text_In_game_nexustimers_or_nexusformation_Local_95A19265A151") };
            Controls.Add(SlimScrolling.Wrap(list)); Controls.Add(details); Controls.Add(help); Controls.Add(buttons); Controls.Add(top);
            list.DrawItem += Draw; list.SelectedIndexChanged += (s,e) => ShowDetails(); scope.SelectedIndexChanged += (s,e) => { Reload();saveScope?.Invoke((scope.SelectedItem as ScopeChoice)?.Id); };
            refresh.Tick += (s,e) => Reload(); VisibleChanged += (s,e) => { if(Visible){ Reload();refresh.Start(); }else refresh.Stop(); };
        }
        BossClock Selected => list.SelectedItem as BossClock;
        void Add(Control parent, string label, Action action) { var button = Theme.Button(label); button.AutoSize = true; button.Click += (s,e) => Run(action); parent.Controls.Add(button); }
        void Run(Action action) { try { action(); Reload(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); } }
        void Change(Action<BossClock> action) { var selected = Selected; if (selected != null) store.Edit(d => action(d.Timers.Single(t => t.Id == selected.Id))); }
        void Reload()
        {
            if (scope.SelectedItem == null) { details.Text = AOLauncher.Localization.Texts.Get("Text_Add_an_account_with_a_valid_50BED4B91646"); return; }
            try {
                string selected = Selected?.Id;
                var rows = store.Read().Timers.Where(t => t.Scope == ((ScopeChoice)scope.SelectedItem).Id).OrderBy(t => t.DueUtcTicks == 0 ? long.MaxValue : t.DueUtcTicks).ToArray();
                list.BeginUpdate(); list.Items.Clear(); list.Items.AddRange(rows);
                for (int i = 0; i < list.Items.Count; i++) if (((BossClock)list.Items[i]).Id == selected) list.SelectedIndex = i;
                list.EndUpdate(); ShowDetails();
            } catch (Exception ex) { details.Text = ex.Message; }
        }
        void ShowDetails()
        {
            var t = Selected;
            details.Text = t == null ? AOLauncher.Localization.Texts.Get("Text_Select_a_timer_Boss_times_are_8D6F0C2AF5C8") :
                (t.Evidence ?? "") + Environment.NewLine + "ID: " + t.Id + Environment.NewLine + string.Join(Environment.NewLine, t.History.Take(4));
        }
        void Draw(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var t = (BossClock)list.Items[e.Index]; bool selected = (e.State & DrawItemState.Selected) != 0;
            using (var b = new SolidBrush(selected ? Theme.Selection : Theme.Surface)) e.Graphics.FillRectangle(b, e.Bounds);
            var bounds = new Rectangle(e.Bounds.X + 12, e.Bounds.Y + 6, e.Bounds.Width - 24, 23);
            TextRenderer.DrawText(e.Graphics, t.Name + "   ·   " + t.Status(DateTime.UtcNow), Font, bounds, Theme.Foreground, TextFormatFlags.EndEllipsis);
            bounds.Y += 25;
            TextRenderer.DrawText(e.Graphics, t.Kind + " · " + t.Seconds / 60 + " min · " + (t.Character == 0 ? "Shared locally" : "Character " + t.Character) + (t.ObserveDeaths ? " · death observation enabled" : " · manual"), Font, bounds, Theme.Muted, TextFormatFlags.EndEllipsis);
        }
        void Edit(BossClock timer)
        {
            if (scope.SelectedItem == null) return;
            using (var dialog = new TimerEditor(timer, ((ScopeChoice)scope.SelectedItem).Id)) {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                var edited = dialog.Value;
                store.Edit(data => {
                    var old = data.Timers.FirstOrDefault(t => t.Id == edited.Id);
                    if (old == null) data.Timers.Add(edited);
                    else { old.Name = edited.Name; old.Kind = edited.Kind; old.Seconds = edited.Seconds; old.WarningSeconds = edited.WarningSeconds;
                        old.Sound = edited.Sound; old.Character = edited.Character; old.ObserveDeaths = edited.ObserveDeaths; old.Repeat = edited.Repeat; }
                });
            }
        }
        void SetDue()
        {
            if (Selected == null) return;
            using (var dialog = new Form { Text = AOLauncher.Localization.Texts.Get("Text_Due_time_local_time_B238E09046AE"), Size = new Size(390,160), StartPosition = FormStartPosition.CenterParent, BackColor = Theme.Background }) {
                var picker = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd HH:mm:ss", Dock = DockStyle.Top, Value = DateTime.Now.AddHours(1) };
                var save = Theme.Button(AOLauncher.Localization.Texts.Get("Text_Use_this_time_CB43AF6CDFB1")); save.Dock = DockStyle.Bottom; save.DialogResult = DialogResult.OK;
                dialog.Controls.Add(picker); dialog.Controls.Add(save);
                if (dialog.ShowDialog(this) == DialogResult.OK) Change(t => { t.Start(DateTime.UtcNow, "Due time set manually in Nexus"); t.DueUtcTicks = picker.Value.ToUniversalTime().Ticks; });
            }
        }
        void KilledAt()
        {
            if (Selected == null) return;
            using (var dialog = new Form { Text = AOLauncher.Localization.Texts.Get("Text_Kill_start_time_local_time_F726292A6D5C"), Size = new Size(390,160), StartPosition = FormStartPosition.CenterParent, BackColor = Theme.Background, ForeColor = Theme.Foreground }) {
                var picker = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd HH:mm:ss", Dock = DockStyle.Top, Value = DateTime.Now };
                var save = Theme.Button(AOLauncher.Localization.Texts.Get("Text_Use_this_time_CB43AF6CDFB1")); save.Dock = DockStyle.Bottom; save.DialogResult = DialogResult.OK;
                dialog.Controls.Add(picker); dialog.Controls.Add(save);
                if (dialog.ShowDialog(this) == DialogResult.OK) {
                    if (picker.Value > DateTime.Now) throw new ArgumentException("Kill time cannot be in the future.");
                    Change(t => t.Start(picker.Value.ToUniversalTime(), "Historical kill/start entered manually in Nexus"));
                }
            }
        }
        protected override void Dispose(bool disposing) { if (disposing) refresh.Dispose(); base.Dispose(disposing); }
    }
    internal sealed class TimerEditor : Form
    {
        internal BossClock Value;
        internal TimerEditor(BossClock original, string scope)
        {
            Text = AOLauncher.Localization.Texts.Get("Text_Timer_settings_D7D4B308B734"); Size = new Size(510,500); StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Background; ForeColor = Theme.Foreground; Font = new Font("Segoe UI",10);
            var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(16) };
            Func<string, string, TextBox> input = (label,value) => { panel.Controls.Add(new Label { Text = label, AutoSize = true }); var box = new TextBox { Text = value, Width = 430, BackColor = Theme.Surface, ForeColor = Theme.Foreground }; panel.Controls.Add(box); return box; };
            var name = input(AOLauncher.Localization.Texts.Get("Text_Name_exact_NPC_name_for_observation_AF602DC661C6"), original?.Name ?? "");
            var kind = new NexusComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 430 }; kind.Items.AddRange(Enum.GetNames(typeof(TimerKind))); kind.SelectedIndex = (int)(original?.Kind ?? TimerKind.Fixed); panel.Controls.Add(kind);
            var minutes = input(AOLauncher.Localization.Texts.Get("Text_Duration_minutes_419780CBCB98"), ((original?.Seconds ?? 3600)/60).ToString(CultureInfo.InvariantCulture));
            var warning = input(AOLauncher.Localization.Texts.Get("Text_Warn_before_minutes_0_disables_76E56676A08D"), ((original?.WarningSeconds ?? 600)/60).ToString(CultureInfo.InvariantCulture));
            var character = input(AOLauncher.Localization.Texts.Get("Text_Character_ID_0_shared_on_this_0C49B6928BC8"), (original?.Character ?? 0).ToString());
            var sound = new NexusCheckBox { Text = AOLauncher.Localization.Texts.Get("Text_Play_sound_14E24761AE37"), Checked = original?.Sound == true, Width = 430, Height = 26 };
            var observe = new NexusCheckBox { Text = AOLauncher.Localization.Texts.Get("Text_Observe_nearby_death_experimental_C72DF5700E7B"), Checked = original?.ObserveDeaths == true, Width = 430, Height = 26 };
            var repeat = new NexusCheckBox { Text = AOLauncher.Localization.Texts.Get("Text_Repeat_interval_Reset_timers_only_59490BBC9347"), Checked = original?.Repeat == true, Width = 430, Height = 26 };
            panel.Controls.Add(sound); panel.Controls.Add(observe); panel.Controls.Add(repeat);
            Action updateOptions = () => {
                var selected = (TimerKind)kind.SelectedIndex;
                repeat.Enabled = selected == TimerKind.Reset;
                observe.Enabled = selected == TimerKind.Fixed || selected == TimerKind.ChanceWindow;
                if (!repeat.Enabled) repeat.Checked = false;
                if (!observe.Enabled) observe.Checked = false;
            };
            kind.SelectedIndexChanged += (s,e) => updateOptions();
            updateOptions();
            var save = Theme.Button(AOLauncher.Localization.Texts.Get("Text_Save_6B32CBC124E1"), true); panel.Controls.Add(save);
            save.Click += (s,e) => {
                try {
                    Value = new BossClock { Id = original?.Id ?? Guid.NewGuid().ToString("N"), Scope = scope, Name = name.Text.Trim(), Kind = (TimerKind)kind.SelectedIndex,
                        Seconds = double.Parse(minutes.Text, CultureInfo.InvariantCulture)*60, WarningSeconds = double.Parse(warning.Text, CultureInfo.InvariantCulture)*60,
                        Character = int.Parse(character.Text), Sound = sound.Checked, ObserveDeaths = observe.Checked, Repeat = repeat.Checked };
                    Value.Validate(); if (Value.Kind == TimerKind.Personal && Value.Character == 0) throw new ArgumentException("Personal timers require a character ID.");
                    DialogResult = DialogResult.OK;
                } catch (Exception ex) { MessageBox.Show(this, ex.Message, Text); }
            };
            Controls.Add(panel);
        }
    }
}
