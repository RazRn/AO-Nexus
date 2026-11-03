using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using AOLauncher.Localization;
using static AOLauncher.SidebarText;

namespace AOLauncher
{
    internal static class SetupPreferences
    {
        internal static LauncherSettings Copy(LauncherSettings settings) => SettingsStore.Deserialize(new JavaScriptSerializer().Serialize(settings));

        internal static LauncherSettings Skip(LauncherSettings original)
        {
            var result = Copy(original);
            result.HasCompletedSetup = result.HasChosenUpdatePreference = true;
            result.CheckForUpdatesOnStartup = result.ShowServerNews = result.ShowServerHealth = false;
            result.BackgroundHealthChecks = result.HealthTrayNotifications = false;
            result.MonitorClientHealth = result.CollectClientExitReports = false;
            result.NewsSourceIds.Clear(); result.HealthServerIds.Clear();
            SetupNewOptions.MarkReviewed(result);
            return result;
        }

        internal static List<string> NewsForServers(IEnumerable<string> serverIds) => serverIds
            .Select(id => id == "live-2019" ? "live" : id)
            .Where(id => ServerNews.Sources.Any(source => source.Id == id)).Distinct().ToList();
    }

    // Works on a detached profile. Neither navigation nor account discovery saves
    // settings, contacts a server, writes credentials, or starts a game.
    internal sealed class SetupWizard : EditorDialog
    {
        private readonly LauncherSettings original, draft;
        private readonly bool firstRun;
        private readonly bool newOptionsOnly;
        private readonly string originalLanguage;
        private readonly Button back;
        private bool startWithWindows;
        private readonly bool originalStartup;
        private int step;
        internal LauncherSettings Result { get; private set; }
        internal bool StartWithWindows => startWithWindows;
        internal Func<LauncherSettings, bool, string> Commit { get; set; }

        internal SetupWizard(LauncherSettings settings, bool startup, bool firstRun) : this(settings, startup, firstRun, false) { }
        internal SetupWizard(LauncherSettings settings, bool startup, bool firstRun, bool newOptionsOnly) : base(AOLauncher.Localization.Texts.Get("Text_Welcome_to_AO_Nexus_B4D7976EE4B2"), 640)
        {
            this.newOptionsOnly = newOptionsOnly;
            original = settings; draft = SetupPreferences.Copy(settings); this.firstRun = firstRun;
            originalLanguage = settings.Language; originalStartup = startWithWindows = startup;
            if (firstRun) { draft.NewsSourceIds.Clear(); draft.HealthServerIds.Clear(); }
            ClientSize = new Size(680, 640); ControlBox = !firstRun;
            Fields.ClientSizeChanged += (sender, e) =>
            {
                foreach (var note in Fields.Controls.OfType<Label>())
                    note.MaximumSize = new Size(Math.Max(100, Fields.ClientSize.Width - 24), 0);
            };
            SaveButton.Name = "setupNext"; CancelAction.Name = "setupSkip";
            CancelAction.DialogResult = DialogResult.None;
            back = Theme.Button(AOLauncher.Localization.Texts.Get("Text_Back_629DEFB2AE16")); back.Name = "setupBack";
            SaveButton.Parent.Controls.Add(back);
            back.Click += (sender, e) => { if (step > 0) { step--; RenderStep(); } };
            SaveButton.Click += (sender, e) =>
            {
                if (!newOptionsOnly && step < 3) { step++; RenderStep(); }
                else Complete(draft, startWithWindows);
            };
            CancelAction.Click += (sender, e) =>
            {
                if (firstRun) Complete(SetupPreferences.Skip(original), originalStartup);
                else DialogResult = DialogResult.Cancel;
            };
            FormClosed += (sender, e) => { Texts.SetLanguage(originalLanguage); GuiStrings.Register(); NexusStrings.Register(); };
            RenderStep();
        }

        private void Complete(LauncherSettings choice, bool startup)
        {
            if (!newOptionsOnly) choice.HasCompletedSetup = choice.HasChosenUpdatePreference = true;
            SetupNewOptions.MarkReviewed(choice);
            try
            {
                string error = Commit?.Invoke(choice, startup);
                if (!string.IsNullOrEmpty(error)) { ErrorLabel.Text = error; return; }
                Result = choice; startWithWindows = startup; DialogResult = DialogResult.OK;
            }
            catch (Exception error) { ErrorLabel.Text = error.Message; }
        }

        private Label Note(string text, bool heading = false)
        {
            var label = new Label { AutoSize = true, Dock = DockStyle.Top, Text = text,
                ForeColor = heading ? Theme.Accent : Theme.Muted, Margin = new Padding(0, 0, 0, heading ? 18 : 14),
                MaximumSize = new Size(Math.Max(100, ClientSize.Width - 80), 0) };
            if (heading) label.Font = new Font(Font, FontStyle.Bold);
            Fields.Controls.Add(label); return label;
        }

        private CheckBox Check(string name, string text, bool selected, Action<bool> change)
        {
            var box = new NexusCheckBox { Name = name, Text = text, Checked = selected, AutoSize = true,
                Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 12) };
            box.CheckedChanged += (sender, e) => change(box.Checked); Fields.Controls.Add(box); return box;
        }

        internal void RenderStep()
        {
            Fields.SuspendLayout();
            foreach (Control control in Fields.Controls.Cast<Control>().ToArray()) control.Dispose();
            Fields.Controls.Clear(); Fields.RowStyles.Clear(); Fields.RowCount = 0;
            ErrorLabel.Text = "";
            if (newOptionsOnly) {
                Text = AOLauncher.Localization.Texts.Get("Text_New_options_in_AO_Nexus_C230AE926A9E");
                back.Visible = false;
                SaveButton.Text = AOLauncher.Localization.Texts.Get("Text_Save_choices_F3EF7533A58D"); CancelAction.Text = AOLauncher.Localization.Texts.Get("Text_Later_AFBE95F81840");
                Note(AOLauncher.Localization.Texts.Get("Text_Choose_how_to_use_the_new_31BD80AB2116"));
                SetupNewOptions.Add(Fields, draft, original.OptionsReviewVersion);
                Fields.ResumeLayout(true); Theme.Apply(this); Fields.AutoScrollPosition = Point.Empty; QueueContentFit(); return;
            }
            Text = AOLauncher.Localization.Texts.Get("Text_AO_Nexus_setup_0321187C12EE");
            back.Text = AOLauncher.Localization.Texts.Get("Text_Back_629DEFB2AE16"); back.Visible = step > 0;
            CancelAction.Text = firstRun ? AOLauncher.Localization.Texts.Get("Text_Skip_setup_A7E0AF8CA306") : AOLauncher.Localization.Texts.Get("Text_Cancel_34A3CBFEA4A5");
            SaveButton.Text = step == 3 ? AOLauncher.Localization.Texts.Get("Text_Finish_E18185A51BC5") : AOLauncher.Localization.Texts.Get("Text_Next_5A25B0B171F8");
            var names = new[] { AOLauncher.Localization.Texts.Get("Text_Welcome_B422C20EC6E5"), AOLauncher.Localization.Texts.Get("Text_Your_servers_D17CCCE5E2B5"), AOLauncher.Localization.Texts.Get("Text_App_preferences_F6BF57DCC78B"), AOLauncher.Localization.Texts.Get("Text_Review_and_add_accounts_43243A638D86") };
            Note((step + 1) + " / 4  ·  " + names[step], true);
            if (step == 0) Welcome();
            else if (step == 1) Servers();
            else if (step == 2) Preferences();
            else Review();
            Fields.ResumeLayout(true); Theme.Apply(this); Fields.AutoScrollPosition = Point.Empty;
            QueueContentFit();
        }

        private void Welcome()
        {
            Note(AOLauncher.Localization.Texts.Get("Text_Choose_your_language_servers_and_a_C97F19F459C6"));
            Note(AOLauncher.Localization.Texts.Get("Text_Language_C87857889CA6"));
            var language = new NexusComboBox { Name = "setupLanguage", Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 0, 0, 18) };
            language.Items.AddRange(new object[] { AOLauncher.Localization.Texts.Get("Text_Automatic_Windows_language_A732CA9BCE47"), "Dansk", "English" });
            language.SelectedIndex = draft.Language == "da" ? 1 : draft.Language == "en" ? 2 : 0;
            language.MatchHeight(SaveButton);
            language.SelectedIndexChanged += (sender, e) =>
            {
                draft.Language = language.SelectedIndex == 1 ? "da" : language.SelectedIndex == 2 ? "en" : "auto";
                Texts.SetLanguage(draft.Language); GuiStrings.Register(); NexusStrings.Register();
                BeginInvoke((Action)(() => { if (!IsDisposed) RenderStep(); }));
            };
            Fields.Controls.Add(language);
            Note(firstRun
                ? AOLauncher.Localization.Texts.Get("Text_Nothing_connects_to_news_health_services_0CF7CE019E53")
                : AOLauncher.Localization.Texts.Get("Text_Your_existing_accounts_and_presets_are_2438B537D65F"));
            var privacy = Theme.Button(AOLauncher.Localization.Texts.Get("Text_Privacy_and_connections_597D8EF1EF13")); privacy.Name = "setupPrivacy";
            privacy.Click += (sender, e) => { using (var dialog = new PrivacyDialog()) dialog.ShowDialog(this); };
            Fields.Controls.Add(privacy);
        }

        private void Servers()
        {
            Note(AOLauncher.Localization.Texts.Get("Text_Select_servers_for_news_and_health_2B34D3A8384C"));
            var selected = new HashSet<string>(draft.HealthServerIds);
            foreach (var id in draft.NewsSourceIds) if (draft.LoginServers.Any(server => server.Id == id)) selected.Add(id);
            foreach (var server in draft.LoginServers)
            {
                string id = server.Id;
                Check("setupServer-" + id, server.Name, selected.Contains(id), enabled =>
                {
                    if (enabled) selected.Add(id); else selected.Remove(id);
                    draft.HealthServerIds = draft.LoginServers.Where(item => selected.Contains(item.Id)).Select(item => item.Id).ToList();
                    draft.NewsSourceIds = SetupPreferences.NewsForServers(draft.HealthServerIds);
                });
            }
            Check("setupNews", AOLauncher.Localization.Texts.Get("Text_Show_server_news_CC9264E5114D"), draft.ShowServerNews, value => draft.ShowServerNews = value);
            Check("setupHealth", AOLauncher.Localization.Texts.Get("Text_Show_server_health_548A59DDF29C"), draft.ShowServerHealth, value => draft.ShowServerHealth = value);
            Note(AOLauncher.Localization.Texts.Get("Text_News_uses_the_selected_servers_official_14C9093C3CEB"));
        }

        private void Preferences()
        {
            Check("setupUpdates", AOLauncher.Localization.Texts.Get("Text_Check_GitHub_for_updates_when_Nexus_8C9475F12550"), draft.CheckForUpdatesOnStartup, value => draft.CheckForUpdatesOnStartup = value);
            Note(AOLauncher.Localization.Texts.Get("Text_Checks_once_at_startup_Downloads_and_81D83132EEE6"));
            Check("setupStartup", AOLauncher.Localization.Texts.Get("Text_Start_with_Windows_408B4BA98620"), startWithWindows, value => startWithWindows = value);
            Note(AOLauncher.Localization.Texts.Get("Text_Starts_Nexus_in_the_tray_It_17FD0D22B67E"));
            CheckBox notifications = null;
            var background = Check("setupBackground", AOLauncher.Localization.Texts.Get("Text_Check_selected_servers_in_the_background_E9C695B964D5"), draft.BackgroundHealthChecks || draft.HealthTrayNotifications, value =>
            {
                draft.BackgroundHealthChecks = value;
                if (!value) { draft.HealthTrayNotifications = false; if (notifications != null) notifications.Checked = false; }
            });
            notifications = Check("setupNotifications", AOLauncher.Localization.Texts.Get("Text_Notify_me_about_outages_and_recovery_81175164B268"), draft.HealthTrayNotifications, value =>
            {
                draft.HealthTrayNotifications = value; if (value) { draft.BackgroundHealthChecks = true; background.Checked = true; }
            });
            Note(AOLauncher.Localization.Texts.Get("Text_Background_checks_run_every_60_seconds_6676D44C413A"));
            Note(AOLauncher.Localization.Texts.Get("Text_Minimize_sends_Nexus_to_the_tray_B5C1DBE11C41"));
            Note(AOLauncher.Localization.Texts.Get("Text_Game_clients_0C9E9394E82E"), true);
            SetupNewOptions.Add(Fields, draft, 0);
        }

        private void Review()
        {
            string on = AOLauncher.Localization.Texts.Get("Text_On_74735AB159B4"), off = AOLauncher.Localization.Texts.Get("Text_Off_CAD65C6B8619");
            Note(AOLauncher.Localization.Texts.Get("Text_Finish_saves_these_preferences_Adding_an_0ECF0D05EE4C"));
            string servers = string.Join(", ", draft.LoginServers.Where(server => draft.HealthServerIds.Contains(server.Id)).Select(server => server.Name));
            Note(AOLauncher.Localization.Texts.Get("Text_Health_servers_FDDD7FDB3206") + (servers.Length == 0 ? AOLauncher.Localization.Texts.Get("Text_None_D5BD8CBDBF60") : servers)
                + "\r\n" + AOLauncher.Localization.Texts.Get("Text_News_sources_A6E652046EED") + (draft.NewsSourceIds.Count == 0 ? AOLauncher.Localization.Texts.Get("Text_None_D5BD8CBDBF60") : string.Join(", ", ServerNews.Sources.Where(source => draft.NewsSourceIds.Contains(source.Id)).Select(source => source.Name)))
                + "\r\n" + AOLauncher.Localization.Texts.Get("Text_News_health_view_D19A7FD7E381") + (draft.ShowServerNews ? on : off) + " / " + (draft.ShowServerHealth ? on : off)
                + "\r\n" + AOLauncher.Localization.Texts.Get("Text_Update_checks_Windows_startup_9E3DAA4F0B37") + (draft.CheckForUpdatesOnStartup ? on : off) + " / " + (startWithWindows ? on : off)
                + "\r\n" + AOLauncher.Localization.Texts.Get("Text_Background_checks_notifications_C115A3CDB7B4") + (draft.BackgroundHealthChecks ? on : off) + " / " + (draft.HealthTrayNotifications ? on : off));
            var accounts = new NexusComboBox { Name = "setupAccounts", Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 0, 0, 12) };
            foreach (var option in SetupNewOptions.All) Note(AOLauncher.Localization.Texts.Get(option.TextKey) + ": " + (option.Read(draft) ? on : off));
            accounts.MatchHeight(SaveButton);
            foreach (var account in draft.Accounts) accounts.Items.Add(account.Label + " · " + AccountTargets.ServerName(draft, account));
            if (accounts.Items.Count == 0) accounts.Items.Add(AOLauncher.Localization.Texts.Get("Text_No_accounts_added_yet_9470643382AE"));
            accounts.SelectedIndex = 0; Fields.Controls.Add(accounts);
            var add = Theme.Button(draft.Accounts.Count == 0 ? AOLauncher.Localization.Texts.Get("Text_Add_first_account_D49D0215364F") : AOLauncher.Localization.Texts.Get("Text_Add_another_account_2BFD2743EEB3"));
            add.Name = "setupAddAccount"; add.Click += (sender, e) => AddAccount(); Fields.Controls.Add(add);
            Note(AOLauncher.Localization.Texts.Get("Text_Choose_the_account_s_server_and_29F71895471C"));
        }

        private void AddAccount()
        {
            using (var dialog = new AccountDialog(null, draft))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                var account = new AccountConfig { Label = dialog.AccountLabel, Username = dialog.Username, PrefsPath = dialog.PrefsPath,
                    GamePath = dialog.GamePath, Server = dialog.Server, Port = dialog.Port, ServerProfileId = dialog.ServerProfileId,
                    CharacterSlots = dialog.CharacterSlots, RememberPassword = dialog.RememberPassword };
                if (draft.Accounts.Any(other => AccountTargets.SameServer(other, account) && string.Equals(other.Username, account.Username, StringComparison.OrdinalIgnoreCase)))
                { ErrorLabel.Text = Texts.T("This account already exists. Add more characters under the existing account."); return; }
                string error;
                if (!MainForm.TryMergeImportedCharacters(draft, account, dialog.ImportedCharacterIds, out error)) { ErrorLabel.Text = error; return; }
                draft.Accounts.Add(account); RenderStep();
            }
        }
    }
}
